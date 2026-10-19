"""TennisPlayer 캐릭터 정리 파이프라인 (Blender 5.2, 백그라운드 실행)

  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup ^
      --python ArtSource/Blender/character_pipeline.py -- <프로젝트 폴더>

VARCO 3D 원본(ArtSource/Character/Raw: 리깅 GLB + Idle·Walk·Run 애니메이션 GLB)을 게임용 FBX로 정리한다.
  0 정리 전 모델을 그대로 FBX로 (Unity 비교용)
  1 불러오기, 애니메이션 3개를 리그 하나에 합치기
  2 불필요한 조각 제거 (UV 이음새로 쪼개진 점 합치기, 겹친 면 삭제)
  3 게임용 검수: 구멍·틈 메우기, 뒤집힌 면 수정
  4 최적화: Decimate로 폴리곤 줄이기
  5 게임 규격: 실제 키 1.8m, 원점 = 발밑 중앙, 정면 -Y(Unity +Z), Apply Transform
  6 애니메이션 정리: 이름 Idle·Walk·Run, 0프레임 시작, 반복(처음·끝 자세) 확인과 보정
  7 스타일 통일: game_tone.py로 베이스컬러·질감 보정, Unity URP용 맵 만들기
  8 FBX 내보내기, 다시 불러와 검증, .blend 저장
단계별 수치는 ArtSource/Character/pipeline_report.json에 남는다 (포트폴리오 정리 전·후 표).
"""
import json
import math
import os
import re
import shutil
import sys
import tempfile

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import game_tone  # noqa: E402

ROOT = sys.argv[sys.argv.index("--") + 1]
RAW = os.path.join(ROOT, "ArtSource", "Character", "Raw")
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Art", "Characters", "TennisPlayer")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
BEFORE_DIR = os.path.join(OUT_DIR, "Before")
BLEND = os.path.join(ROOT, "ArtSource", "Character", "TennisPlayer.blend")
REPORT = os.path.join(ROOT, "ArtSource", "Character", "pipeline_report.json")

NAME = "TennisPlayer"
HEIGHT = 1.80            # 실제 성인 남자 키 (m)
TARGET_TRIS = 20000      # 게임 캐릭터 폴리곤 예산 (삼각형)
WELD = 1e-5              # 같은 자리에 겹친 점 (UV 이음새로 쪼개진 것)
ZIP = 0.002              # 틈 메우기: 경계 점끼리 이 거리 안이면 붙인다 (원본 키 1m 기준 2mm)
HOLE_SIDES = 12          # 이 변 수 이하의 구멍만 메운다 (큰 개구부는 건드리지 않음)
# Run은 VARCO "jog_looping" (반복용으로 만든 동작). "run"(05) 은 출발 동작이라 한 주기도 맞지 않아 쓰지 않는다.
CLIPS = [("Idle", "03_Anim_Idle.glb"), ("Walk", "04_Anim_Walk.glb"), ("Run", "06_Anim_Jog.glb")]
LOOP_LIMIT_DEG = 3.0     # 처음·끝 자세 차이가 이보다 크면 반복할 때 튄다
LOOP_LIMIT_M = 0.02      # 위치 차이 기준 (2cm)
MIN_CYCLE_FRAMES = 15    # 0.5초보다 짧은 구간은 한 걸음 주기도 안 된다
LOOP_BLEND_FRAMES = 6    # 남은 작은 차이는 마지막 몇 프레임을 첫 자세로 이어 붙인다

report = {}


# ---------- 공통 ----------

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30  # VARCO 애니메이션은 30fps


def import_glb(path):
    """GLB를 불러와 (아마추어, 몸 메시, 나머지 오브젝트)를 돌려준다."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == "ARMATURE")
    body = next(o for o in new if o.type == "MESH" and o.parent == arm)
    return arm, body, [o for o in new if o not in (arm, body)]


def remove_objects(objs):
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)


def select_only(objs, active):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def fcurves(action):
    return [fc for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]


def count_parts(bm):
    seen, parts = set(), 0
    for f in bm.faces:
        if f in seen:
            continue
        parts += 1
        stack = [f]
        seen.add(f)
        while stack:
            for e in stack.pop().edges:
                for g in e.link_faces:
                    if g not in seen:
                        seen.add(g)
                        stack.append(g)
    return parts


def count_flipped(bm):
    """이웃과 방향이 반대인 면 수 (노멀을 다시 계산하면 뒤집히는 면)."""
    copy = bm.copy()
    before = [f.normal.copy() for f in copy.faces]
    bmesh.ops.recalc_face_normals(copy, faces=copy.faces)
    n = sum(1 for f, b in zip(copy.faces, before) if f.normal.dot(b) < 0)
    copy.free()
    return n


def mesh_stats(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    s = {
        "verts": len(bm.verts),
        "tris": sum(len(f.verts) - 2 for f in bm.faces),
        "parts": count_parts(bm),
        "boundary_edges": sum(1 for e in bm.edges if e.is_boundary),
        "nonmanifold_edges": sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary),
    }
    bm.free()
    return s


def world_bounds(obj, evaluated=False):
    if evaluated:
        dg = bpy.context.evaluated_depsgraph_get()
        ev = obj.evaluated_get(dg)
        me = ev.to_mesh()
        pts = [obj.matrix_world @ v.co for v in me.vertices]
        ev.to_mesh_clear()
    else:
        pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    return lo, hi


def r(v, n=4):
    return [round(x, n) for x in v] if hasattr(v, "__len__") else round(v, n)


# ---------- 0 정리 전 ----------

def export_before():
    """VARCO 리깅 원본을 아무것도 고치지 않고 FBX로 (Unity 비교 캡처용). 텍스처는 FBX 안에 넣는다."""
    reset()
    arm, body, extras = import_glb(os.path.join(RAW, "02_Rigged.glb"))
    for pb in arm.pose.bones:
        pb.custom_shape = None
    remove_objects(extras)  # glTF 불러오기가 뼈 표시용으로 만든 구(Icosphere): 원본 파일엔 없는 것
    arm.name, body.name = NAME + "_Before", NAME + "_Before_Body"
    lo, hi = world_bounds(body)
    report["before"] = dict(mesh_stats(body), height_m=r(hi.z - lo.z), bounds_min=r(lo), bounds_max=r(hi),
                            bones=len(arm.data.bones), material_double_sided=not body.data.materials[0].use_backface_culling)
    # 묶여 있는 텍스처를 파일로 풀어야 FBX에 넣을 수 있다
    tmp = tempfile.mkdtemp(prefix="tp_before_")
    for img in bpy.data.images:
        if img.packed_file:
            img.filepath_raw = os.path.join(tmp, bpy.path.clean_name(img.name) + ".png")
            img.file_format = "PNG"
            img.save()
    os.makedirs(BEFORE_DIR, exist_ok=True)
    select_only([arm, body], arm)
    bpy.ops.export_scene.fbx(filepath=os.path.join(BEFORE_DIR, NAME + "_Before.fbx"), use_selection=True,
                             object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", add_leaf_bones=False, bake_anim=False,
                             path_mode="COPY", embed_textures=True)
    shutil.rmtree(tmp, ignore_errors=True)


# ---------- 1 불러오기·합치기 ----------

def load_and_merge():
    reset()
    arm, body, extras = import_glb(os.path.join(RAW, "02_Rigged.glb"))
    for pb in arm.pose.bones:
        pb.custom_shape = None
    remove_objects(extras)
    for a in list(bpy.data.actions):  # 리깅 파일의 빈 동작
        bpy.data.actions.remove(a)
    arm.name = arm.data.name = NAME + "_Rig"
    body.name = body.data.name = NAME + "_Body"

    clips = {}
    for clip, file in CLIPS:
        a2, b2, ex2 = import_glb(os.path.join(RAW, file))
        act = a2.animation_data.action
        a2.animation_data.action = None
        act.name = clip
        act.use_fake_user = True
        remove_objects([a2, b2] + ex2)
        # 처음 키가 2프레임(0.067초)에서 시작해서 0프레임으로 당긴다
        start = act.frame_range[0]
        for fc in fcurves(act):
            for kp in fc.keyframe_points:
                kp.co.x -= start
                kp.handle_left.x -= start
                kp.handle_right.x -= start
            fc.update()
        clips[clip] = act
    bpy.data.orphans_purge(do_recursive=True)
    report["animations_imported"] = {c: r(list(a.frame_range), 1) for c, a in clips.items()}
    return arm, body, clips


def use_action(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    ad.action_slot = act.slots[0]


# ---------- 2·3 조각 제거, 구멍·뒤집힌 면 ----------

def clean_mesh(body):
    bm = bmesh.new()
    bm.from_mesh(body.data)
    s = {"verts_before": len(bm.verts)}

    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=WELD)
    s["verts_after_weld"] = len(bm.verts)
    # 점을 합친 뒤에야 진짜 구멍·뒤집힌 면이 보인다 (전에는 UV 이음새마다 경계였음)
    s["found_after_weld"] = {
        "parts": count_parts(bm),
        "boundary_edges": sum(1 for e in bm.edges if e.is_boundary),
        "nonmanifold_edges": sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary),
        "flipped_faces": count_flipped(bm),
    }

    bverts = list({v for e in bm.edges if e.is_boundary for v in e.verts})
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bverts, dist=ZIP)
    s["slit_verts_zipped"] = before - len(bm.verts)

    bm.verts.index_update()
    seen, dup = set(), []
    for f in bm.faces:
        key = frozenset(v.index for v in f.verts)
        if key in seen:
            dup.append(f)
        else:
            seen.add(key)
    bmesh.ops.delete(bm, geom=dup, context="FACES_ONLY")
    s["duplicate_faces_removed"] = len(dup)

    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    s["loose_verts_removed"] = len(loose)

    holes = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=HOLE_SIDES)
    s["holes_filled"] = len(holes["faces"])

    normals = {f: f.normal.copy() for f in bm.faces}
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    s["flipped_faces_fixed"] = sum(1 for f in bm.faces if f in normals and f.normal.dot(normals[f]) < 0)

    bm.to_mesh(body.data)
    bm.free()
    # 원본의 분할 노멀은 점을 합치면서 맞지 않게 되므로 지우고 부드러운 셰이딩으로
    with bpy.context.temp_override(object=body, active_object=body, selected_objects=[body]):
        if body.data.has_custom_normals:
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
    body.data.shade_smooth()
    report["cleanup"] = dict(s, after=mesh_stats(body))


# ---------- 4 Decimate ----------

def decimate(body):
    tris = mesh_stats(body)["tris"]
    ratio = min(1.0, TARGET_TRIS / tris)
    mod = body.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    with bpy.context.temp_override(object=body, active_object=body, selected_objects=[body]):
        bpy.ops.object.modifier_move_to_index(modifier="Decimate", index=0)  # 아마추어 변형 전에 적용
        bpy.ops.object.modifier_apply(modifier="Decimate")
    report["decimate"] = {"ratio": r(ratio), "tris_before": tris, "after": mesh_stats(body)}


# ---------- 5 게임 규격 ----------

def game_spec(arm, body, clips):
    lo, hi = world_bounds(body)
    height = hi.z - lo.z
    k = HEIGHT / height
    feet = [body.matrix_world @ v.co for v in body.data.vertices]
    feet = [p for p in feet if p.z < lo.z + 0.03 * height]
    center = Vector(((min(p.x for p in feet) + max(p.x for p in feet)) / 2,
                     (min(p.y for p in feet) + max(p.y for p in feet)) / 2, lo.z))
    # 발밑 중앙을 원점으로 옮기고 키를 1.8m로: v' = k (v - center)
    arm.location = -k * center
    arm.scale = (k, k, k)
    bpy.context.view_layer.update()
    select_only([arm, body], arm)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # 뼈 위치 키(Root 상하 흔들림)는 뼈 공간 길이라 같은 배율로 키운다
    for act in clips.values():
        for fc in fcurves(act):
            if fc.data_path.endswith(".location"):
                for kp in fc.keyframe_points:
                    kp.co.y *= k
                    kp.handle_left.y *= k
                    kp.handle_right.y *= k
                fc.update()
    lo2, hi2 = world_bounds(body)
    bones = arm.data.bones
    toe = (bones["LeftToeBase"].head_local - bones["LeftFoot"].head_local)
    report["game_spec"] = {
        "height_before_m": r(height), "scale": r(k), "origin_moved_by_m": r(list(center)),
        "height_after_m": r(hi2.z - lo2.z), "bounds_min": r(lo2), "bounds_max": r(hi2),
        "facing": "-Y (Blender 정면 = Unity +Z)" if toe.y < 0 else "+Y (돌려야 함)",
        "armature_transform": {"loc": r(arm.location), "rot": r(arm.rotation_euler), "scale": r(arm.scale)},
        "body_transform": {"loc": r(body.location), "rot": r(body.rotation_euler), "scale": r(body.scale)},
    }


# ---------- 6 애니메이션 ----------

def all_poses(arm, act):
    """프레임마다 뼈의 (회전, 위치) — 아마추어 공간."""
    s, e = (int(round(x)) for x in act.frame_range)
    use_action(arm, act)
    poses = []
    for f in range(s, e + 1):
        bpy.context.scene.frame_set(f)
        poses.append({pb.name: (pb.matrix.to_quaternion(), pb.matrix.translation.copy()) for pb in arm.pose.bones})
    return poses


def pose_diff(a, b):
    deg = max(math.degrees(a[n][0].rotation_difference(b[n][0]).angle) for n in a)
    dist = max((a[n][1] - b[n][1]).length for n in a)
    return deg, dist


def loop_error(arm, act):
    poses = all_poses(arm, act)
    return pose_diff(poses[0], poses[-1])


def find_cycle(poses):
    """처음·끝 자세가 같은 구간 (i, j). 기준 안에 드는 구간 중 가장 긴 것, 없으면 가장 비슷한 것."""
    best = None
    for i in range(len(poses)):
        for j in range(i + MIN_CYCLE_FRAMES, len(poses)):
            deg, dist = pose_diff(poses[i], poses[j])
            ok = deg <= LOOP_LIMIT_DEG and dist <= LOOP_LIMIT_M
            key = (ok, j - i) if ok else (ok, -(deg + dist * 100))  # 1cm를 1도로 친다
            if best is None or key > best[0]:
                best = (key, i, j)
    return best[1], best[2]


def trim(act, a, b):
    """[a, b] 프레임만 남기고 0프레임부터 시작하게 당긴다."""
    for fc in fcurves(act):
        pts = fc.keyframe_points
        for i in reversed(range(len(pts))):
            if pts[i].co.x < a - 1e-3 or pts[i].co.x > b + 1e-3:
                pts.remove(pts[i], fast=True)
        for kp in pts:
            kp.co.x -= a
            kp.handle_left.x -= a
            kp.handle_right.x -= a
        fc.update()


def close_loop(act):
    """마지막 LOOP_BLEND_FRAMES 프레임을 첫 프레임 자세로 서서히 맞춘다."""
    s, e = act.frame_range
    groups = {}
    for fc in fcurves(act):
        groups.setdefault(fc.data_path, []).append(fc)
    for path, curves in groups.items():
        curves.sort(key=lambda c: c.array_index)
        first = [c.evaluate(s) for c in curves]
        last = [c.evaluate(e) for c in curves]
        sign = -1.0 if path.endswith("rotation_quaternion") and sum(a * b for a, b in zip(first, last)) < 0 else 1.0
        for c, target in zip(curves, first):
            target *= sign
            for kp in c.keyframe_points:
                if kp.co.x >= e - LOOP_BLEND_FRAMES:
                    w = (kp.co.x - (e - LOOP_BLEND_FRAMES)) / LOOP_BLEND_FRAMES
                    d = (target - kp.co.y) * w
                    kp.co.y += d
                    kp.handle_left.y += d
                    kp.handle_right.y += d
            c.update()


def tidy_animations(arm, body, clips):
    out = {}
    for name, act in clips.items():
        poses = all_poses(arm, act)
        deg, dist = pose_diff(poses[0], poses[-1])
        info = {"frames": r(list(act.frame_range), 1), "seconds": r(len(poses) / 30, 2),
                "loop_diff_deg": r(deg, 2), "loop_diff_m": r(dist, 4)}
        if deg > LOOP_LIMIT_DEG or dist > LOOP_LIMIT_M:
            # 처음·끝이 달라 반복하면 튄다: 클립 안에서 한 주기를 찾아 그 구간만 남긴다
            a, b = find_cycle(poses)
            trim(act, a, b)
            deg2, dist2 = pose_diff(poses[a], poses[b])
            if (a, b) == (0, len(poses) - 1):
                info["cycle_kept"] = "전체 (클립 안에 더 맞는 구간이 없음)"
            else:
                info.update(cycle_kept=[a, b], cycle_seconds=r((b - a) / 30, 2),
                            cycle_diff_deg=r(deg2, 2), cycle_diff_m=r(dist2, 4))
            if deg2 > 0.5 or dist2 > 0.005:
                close_loop(act)  # 남은 차이는 마지막 몇 프레임으로 이어 붙인다
                info["blended_last_frames"] = LOOP_BLEND_FRAMES
            deg3, dist3 = loop_error(arm, act)
            info.update(loop_diff_after_deg=r(deg3, 2), loop_diff_after_m=r(dist3, 4))
        start, end = act.frame_range
        act.use_frame_range = True
        act.frame_start, act.frame_end = start, end
        act.use_cyclic = True
        info["frames_after"] = [start, end]
        # 발이 땅에 붙어 있는지: 첫 프레임에서 몸의 가장 낮은 점
        use_action(arm, act)
        bpy.context.scene.frame_set(int(act.frame_start))
        info["lowest_point_m"] = r(world_bounds(body, evaluated=True)[0].z, 3)
        out[name] = info
    use_action(arm, clips["Idle"])
    report["animations"] = out


# ---------- 7 머티리얼·텍스처 ----------

def image_into(socket):
    """소켓 위쪽으로 연결된 첫 이미지 텍스처 노드의 이미지."""
    stack = [socket]
    while stack:
        s = stack.pop()
        for link in s.links:
            n = link.from_node
            if n.type == "TEX_IMAGE":
                return n
            stack.extend(n.inputs)
    return None


def pixels(img):
    w, h = img.size
    buf = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(buf)
    return buf.reshape(h, w, 4)


def save_png(name, arr, colorspace):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(arr, np.float32).ravel())
    img.filepath_raw = os.path.join(TEX_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img


def materials(body):
    os.makedirs(TEX_DIR, exist_ok=True)
    mat = body.data.materials[0]
    mat.name = "M_" + NAME
    mat.use_backface_culling = True  # 뒤집힌 면을 고쳤으니 양면 렌더링을 끈다
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    base_node = image_into(bsdf.inputs["Base Color"])
    orm_node = image_into(bsdf.inputs["Roughness"])
    normal_node = image_into(bsdf.inputs["Normal"])

    base = pixels(base_node.image)
    toned = base.copy()
    toned[..., :3] = game_tone.tone(base[..., :3])
    base_node.image = save_png(NAME + "_BaseColor", toned, "sRGB")

    orm = pixels(orm_node.image)  # glTF: R 차폐(AO), G 거칠기, B 금속성
    ms = np.zeros_like(orm)
    ms[..., 0] = orm[..., 2]                         # URP 금속성 맵: R 금속성
    ms[..., 3] = game_tone.smoothness(orm[..., 1])   # A 매끈함
    save_png(NAME + "_MetallicSmoothness", ms, "Non-Color")
    ao = np.ones_like(orm)
    ao[..., 0] = ao[..., 1] = ao[..., 2] = orm[..., 0]
    save_png(NAME + "_Occlusion", ao, "Non-Color")
    normal_node.image = save_png(NAME + "_Normal", pixels(normal_node.image), "Non-Color")

    lum = np.array([0.299, 0.587, 0.114])
    def sat(rgb):
        return float(np.mean(rgb.max(-1) - rgb.min(-1)))
    report["material"] = {
        "name": mat.name, "double_sided_before": True, "double_sided_after": False,
        "texture_size": list(base_node.image.size),
        "base_mean_luminance": [r(float(np.mean(base[..., :3] @ lum)), 3), r(float(np.mean(toned[..., :3] @ lum)), 3)],
        "base_mean_saturation": [r(sat(base[..., :3]), 3), r(sat(toned[..., :3]), 3)],
        "mean_roughness": [r(float(np.mean(orm[..., 1])), 3), r(float(np.mean(1 - ms[..., 3])), 3)],
        "tone": {k: getattr(game_tone, k) for k in ("SATURATION", "CONTRAST", "WARMTH", "BRIGHTNESS", "ROUGHNESS_MIN")},
        "unity_maps": [NAME + s for s in ("_BaseColor", "_Normal", "_MetallicSmoothness", "_Occlusion")],
    }


# ---------- 8 내보내기·검증 ----------

def export(arm, body, clips):
    # 동작마다 NLA 스트립 하나: FBX 테이크 이름이 그대로 Idle·Walk·Run이 된다
    # (모든 동작 내보내기는 "TennisPlayer_Rig|Idle"처럼 오브젝트 이름이 붙음)
    ad = arm.animation_data
    ad.action = None
    for name, act in clips.items():
        track = ad.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, int(act.frame_range[0]), act)
        strip.name = name
        if hasattr(strip, "action_slot"):
            strip.action_slot = act.slots[0]
    path = os.path.join(OUT_DIR, NAME + ".fbx")
    select_only([arm, body], arm)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             add_leaf_bones=False, mesh_smooth_type="FACE",
                             bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             path_mode="STRIP")  # 텍스처는 Unity에서 URP 머티리얼로 연결
    bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True, relative_remap=True)
    return path


def verify(path):
    reset()
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    lo, hi = world_bounds(body)
    report["fbx_check"] = {
        "file_mb": r(os.path.getsize(path) / 1e6, 1),
        "objects": [o.name for o in bpy.data.objects], "bones": len(arm.data.bones),
        "tris": mesh_stats(body)["tris"], "height_m": r(hi.z - lo.z, 3), "bounds_min": r(lo, 3),
        "actions": {a.name: r(list(a.frame_range), 1) for a in bpy.data.actions},
        # FBX 안의 테이크 이름 (Unity 클립 이름). 다시 불러온 동작 이름엔 블렌더가 오브젝트 이름을 붙인다
        "fbx_takes": sorted({m.decode() for m in re.findall(rb"([\w|.]+)\x00\x01AnimStack", open(path, "rb").read())}),
        "materials": [m.name for m in body.data.materials],
    }


def main():
    export_before()
    arm, body, clips = load_and_merge()
    clean_mesh(body)
    decimate(body)
    game_spec(arm, body, clips)
    tidy_animations(arm, body, clips)
    materials(body)
    path = export(arm, body, clips)
    verify(path)
    with open(REPORT, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print("PIPELINE_OK " + json.dumps(report, ensure_ascii=False))


main()
