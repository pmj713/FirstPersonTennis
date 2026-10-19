"""소품 정리 파이프라인: 테니스공·라켓 (Blender 5.2, 백그라운드 실행)

  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup ^
      --python ArtSource/Blender/prop_pipeline.py -- <프로젝트 폴더>

VARCO 3D 원본(ArtSource/Props/Raw)을 게임용 FBX로 정리한다.
  0 정리 전: 원본을 그대로 FBX로 (Unity 비교용)
  1 검수: 점 합치기, 떠 있는 부스러기 삭제, 찢어진 틈 닫기, 노멀 방향
  2 방향: 긴 축을 세로(Z)로, 라켓은 넓은 헤드가 위·줄 면이 정면(-Y, Unity +Z)
  3 최적화: Decimate
  4 게임 규격: 실제 크기 (공 지름 6.7cm, 라켓 길이 68.5cm), 원점 (공 = 중심: 물리 계산 기준, 라켓 = 손잡이 끝 바닥 중앙), Apply Transform
  5 스타일 통일: game_tone.py로 캐릭터와 같은 톤, Unity URP용 맵
  6 FBX 내보내기·검증, .blend 저장
단계별 수치는 ArtSource/Props/pipeline_report.json에 남는다.
"""
import json
import math
import os
import re
import shutil
import sys
import tempfile

import bpy
import numpy as np

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import pipeline_common as pc  # noqa: E402

ROOT = sys.argv[sys.argv.index("--") + 1]
SRC = os.path.join(ROOT, "ArtSource", "Props")
RAW = os.path.join(SRC, "Raw")
# 게임 에셋(Assets/_Project/Art/Props)은 이 결과를 다듬은 최종본(ArtSource/Props/Final)이라 덮어쓰지 않게 따로 낸다
OUT = os.path.join(SRC, "Export")
REPORT = os.path.join(SRC, "pipeline_report.json")

# 실제 크기: ITF 규격 공 지름 6.54~6.86cm, 성인 라켓 길이 68.5cm
PROPS = {
    "TennisBall": {"file": "11_Ball_3D.glb", "size_m": 0.067, "origin": "center", "tris": 1000, "tex": 512},
    "TennisRacket": {"file": "21_Racket_3D.glb", "size_m": 0.685, "origin": "bottom", "tris": 4000, "tex": 1024},
}

report = {}


def export_before(name, cfg):
    pc.reset()
    obj = next(o for o in pc.import_glb(os.path.join(RAW, cfg["file"])) if o.type == "MESH")
    obj.name = name + "_Before"
    report[name] = {"raw": dict(pc.mesh_stats(obj), size_m=pc.r(list(obj.dimensions), 3),
                                origin_offset_m=pc.r(list((pc.bounds(pc.verts(obj))[0] + pc.bounds(pc.verts(obj))[1]) / 2), 3))}
    tmp = tempfile.mkdtemp(prefix="prop_before_")
    for img in bpy.data.images:
        if img.packed_file:
            img.filepath_raw = os.path.join(tmp, bpy.path.clean_name(img.name) + ".png")
            img.file_format = "PNG"
            img.save()
    out = os.path.join(OUT, name, "Before")
    os.makedirs(out, exist_ok=True)
    pc.select_only([obj], obj)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + "_Before.fbx"), use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             path_mode="COPY", embed_textures=True)
    shutil.rmtree(tmp, ignore_errors=True)


def orient_long_axis(obj, name):
    """주성분으로 긴 축 → Z, 중간 축 → X, 가장 얇은 축 → Y (라켓 줄 면이 정면을 본다).
    라켓은 넓은 쪽(헤드)이 위로 오게 뒤집는다."""
    v = pc.verts(obj)
    c = v.mean(0)
    vals, vecs = np.linalg.eigh(np.cov((v - c).T))
    thin, mid, long_ = vecs[:, 0], vecs[:, 1], vecs[:, 2]
    rot = np.vstack([mid, thin, long_])  # 행: 새 X, Y, Z
    if np.linalg.det(rot) < 0:
        rot[0] = -rot[0]
    w = (v - c) @ rot.T
    flipped = False
    if name == "TennisRacket":
        z = w[:, 2]
        top, bottom = w[z > np.percentile(z, 80)], w[z < np.percentile(z, 20)]
        if np.ptp(top[:, 0]) < np.ptp(bottom[:, 0]):  # 헤드(넓은 쪽)가 아래면 Z축으로 뒤집기
            w[:, 2] = -w[:, 2]
            w[:, 0] = -w[:, 0]
            flipped = True
    obj.data.vertices.foreach_set("co", np.ascontiguousarray(w, np.float64).ravel())
    obj.data.update()
    angle = math.degrees(math.acos(min(1.0, abs(long_[2]))))
    return {"long_axis_tilt_deg": pc.r(angle, 1), "flipped_head_up": flipped,
            "extent_long_mid_thin": pc.r([float(np.ptp(w[:, 2])), float(np.ptp(w[:, 0])), float(np.ptp(w[:, 1]))], 3)}


def spherify(obj):
    """공: 바닥에 닿아 납작해진 곳까지 모든 점을 같은 반지름의 구 표면으로 (UV는 그대로)."""
    v = pc.verts(obj)
    c = (v.min(0) + v.max(0)) / 2
    d = np.linalg.norm(v - c, axis=1)
    radius = float(np.median(d))
    v = c + (v - c) / d[:, None] * radius
    obj.data.vertices.foreach_set("co", np.ascontiguousarray(v, np.float64).ravel())
    obj.data.update()
    return {"radius_spread_before": pc.r([float(d.min() / radius), float(d.max() / radius)], 3)}


def strip_strings(obj):
    """라켓: AI가 조각조각 만든 줄(헤드 안쪽의 흰·회색 면)을 지운다. 손잡이 끝의 흰 캡은 헤드 밖이라 남는다."""
    cols = pc.face_colors(obj)
    sat = cols.max(1) - cols.min(1)
    lum = cols @ np.array([0.299, 0.587, 0.114])
    z = np.array([p.center.z for p in obj.data.polygons])
    head = z > z.min() + 0.4 * (z.max() - z.min())
    whitish = (sat < 0.12) & (lum > 0.4)
    removed = pc.delete_faces(obj, head & whitish)
    parts = pc.remove_small_parts(obj, 0.01)  # 프레임에서 떨어져 떠 있던 줄 조각
    return {"string_faces_removed": removed, "string_pieces_removed": parts, "frame": pc.mesh_stats(obj)}


def string_texture(path, size=512, mains=16, crosses=19, width=3):
    """줄 무늬 텍스처: 흰 줄(불투명) 사이는 투명. Unity에서 알파 컷아웃으로 쓴다."""
    img = np.zeros((size, size, 4), np.float32)
    color = np.array([0.93, 0.93, 0.89, 1.0], np.float32)
    for i in range(mains):
        x = int((i + 0.5) / mains * size)
        img[:, max(0, x - width // 2):x + width // 2 + 1] = color
    for j in range(crosses):
        y = int((j + 0.5) / crosses * size)
        img[max(0, y - width // 2):y + width // 2 + 1, :] = color
    return pc.save_png(path, "TennisRacket_Strings_BaseColor", img, "sRGB")


def string_bed(frame, tex_dir):
    """헤드 안쪽 테두리를 따라 줄 평면을 만든다 (프레임과 따로 된 파츠, 두 머티리얼).
    테두리 = 헤드 중심에서 각도마다 가장 가까운 프레임 점 → 부채꼴 면. 프레임 안으로 2mm 밀어 넣어 틈이 안 보이게."""
    v = pc.verts(frame)
    z0, z1 = v[:, 2].min(), v[:, 2].max()
    head = v[v[:, 2] > z0 + 0.45 * (z1 - z0)]
    cx, cz = (head[:, 0].min() + head[:, 0].max()) / 2, (head[:, 2].min() + head[:, 2].max()) / 2
    y = float(np.median(head[:, 1]))
    ang = np.arctan2(head[:, 2] - cz, head[:, 0] - cx)
    dist = np.hypot(head[:, 0] - cx, head[:, 2] - cz)
    rim = []
    for a in np.radians(np.arange(0, 360, 4)):
        sel = np.abs((ang - a + np.pi) % (2 * np.pi) - np.pi) < np.radians(3)
        r = dist[sel].min() + 0.002 if sel.any() else None
        if r:
            rim.append((cx + r * math.cos(a), y, cz + r * math.sin(a)))
    pts = [(cx, y, cz)] + rim
    faces = [(0, i + 1, (i + 1) % len(rim) + 1) for i in range(len(rim))]
    me = bpy.data.meshes.new("TennisRacket_Strings")
    me.from_pydata(pts, [], faces)
    xs, zs = [p[0] for p in pts], [p[2] for p in pts]
    uv = me.uv_layers.new(name="UVMap")
    for loop in me.loops:
        p = pts[loop.vertex_index]
        uv.data[loop.index].uv = ((p[0] - min(xs)) / (max(xs) - min(xs)), (p[2] - min(zs)) / (max(zs) - min(zs)))
    obj = bpy.data.objects.new("TennisRacket_Strings", me)
    bpy.context.scene.collection.objects.link(obj)
    mat = bpy.data.materials.new("M_TennisRacket_Strings")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = string_texture(os.path.join(tex_dir, "TennisRacket_Strings_BaseColor.png"))
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    mat.node_tree.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    mat.use_backface_culling = False  # 줄은 앞뒤 어디서나 보인다
    me.materials.append(mat)
    return obj, {"rim_points": len(rim), "head_size_m": pc.r([max(xs) - min(xs), max(zs) - min(zs)], 3), "plane_y": pc.r(y, 4)}


def game_spec(obj, cfg):
    v = pc.verts(obj)
    lo, hi = pc.bounds(v)
    k = cfg["size_m"] / (hi - lo).max()
    if cfg["origin"] == "center":
        pivot = (lo + hi) / 2
    else:  # 바닥 중앙 (라켓 손잡이 끝)
        pivot = np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])
    v = (v - pivot) * k
    obj.data.vertices.foreach_set("co", np.ascontiguousarray(v, np.float64).ravel())
    obj.data.update()
    obj.location = (0, 0, 0)
    pc.select_only([obj], obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    lo2, hi2 = pc.bounds(pc.verts(obj))
    return {"scale": pc.r(float(k), 4), "size_m": pc.r(list(hi2 - lo2), 3), "bounds_min": pc.r(list(lo2), 3),
            "origin": "중심 (물리 계산 기준)" if cfg["origin"] == "center" else "손잡이 끝 바닥 중앙"}


def process(name, cfg):
    pc.reset()
    obj = next(o for o in pc.import_glb(os.path.join(RAW, cfg["file"])) if o.type == "MESH")
    obj.name = obj.data.name = name
    pc.select_only([obj], obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    info = report[name]
    info["clean"] = pc.clean_mesh(obj, zip_dist=0.002, min_part_ratio=0.005, tear_size=0.03)
    if name == "TennisRacket":
        info["strings_removed"] = strip_strings(obj)
    info["orient"] = orient_long_axis(obj, name)
    if name == "TennisBall":
        info["spherify"] = spherify(obj)
    info["decimate"] = pc.decimate(obj, cfg["tris"])
    info["game_spec"] = game_spec(obj, cfg)
    tex_dir = os.path.join(OUT, name, "Textures")
    os.makedirs(tex_dir, exist_ok=True)
    info["material"] = pc.game_material(obj, name, tex_dir, cfg["tex"])
    objs = [obj]
    if name == "TennisRacket":
        strings, info["strings_new"] = string_bed(obj, tex_dir)
        obj.name = obj.data.name = name + "_Frame"
        objs.append(strings)

    path = os.path.join(OUT, name, name + ".fbx")
    pc.select_only(objs, obj)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             mesh_smooth_type="FACE", path_mode="STRIP")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC, name + ".blend"), compress=True, relative_remap=True)

    pc.reset()
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    pts = np.concatenate([pc.verts(m) for m in meshes])
    lo, hi = pts.min(0), pts.max(0)
    info["fbx_check"] = {"file_mb": pc.r(os.path.getsize(path) / 1e6, 2),
                         "meshes": {m.name: pc.mesh_stats(m)["tris"] for m in meshes},
                         "size_m": pc.r(list(hi - lo), 3), "bounds_min": pc.r(list(lo), 3),
                         "materials": sorted({mt.name for m in meshes for mt in m.data.materials})}


def main():
    for name, cfg in PROPS.items():
        if not os.path.exists(os.path.join(RAW, cfg["file"])):
            continue
        export_before(name, cfg)
        process(name, cfg)
    with open(REPORT, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print("PIPELINE_OK " + json.dumps(report, ensure_ascii=False))


main()
