"""TennisPlayer 파츠 캐릭터 파이프라인 (Blender 5.2, 백그라운드 실행)

  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup ^
      --python ArtSource/Blender/character_parts_pipeline.py -- <프로젝트 폴더>

VARCO 3D로 따로 만든 파츠(ArtSource/Character/Parts/Raw)를 한 캐릭터로 조립해 게임용 FBX로 정리한다.
  몸(대머리·맨몸, 리깅, Idle·Walk·Run) + 헤어 + 상의 + 하의 + 신발
  0 정리 전: 원본 파츠를 그대로 한 줄로 늘어놓은 FBX (Unity 비교용)
  1 몸: 애니메이션 합치기, 메시 검수, 키 1.78m(머리카락 제외)·발밑 원점·Apply Transform
  2 파츠 검수: 점 합치기, 작은 떨어진 조각 삭제, 구멍·노멀, Decimate
  3 파츠 맞추기: 몸의 뼈 위치·몸통 치수에 맞춰 크기·위치, 상의 소매를 T포즈로 들기, 몸을 뚫는 곳 밀어내기
  4 스킨: 몸의 뼈 가중치를 가장 가까운 면에서 파츠로 옮김 (헤어는 Head 100%)
  5 옷·신발 안쪽에 가려지는 몸을 지운다 (폴리곤 절약, 움직일 때 살이 옷을 뚫고 보이는 것 방지)
  6 애니메이션 반복 정리, 게임 톤 머티리얼, FBX 내보내기·검증, .blend 저장
단계별 수치는 ArtSource/Character/Parts/pipeline_report.json에 남는다.
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
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import pipeline_common as pc  # noqa: E402

ROOT = sys.argv[sys.argv.index("--") + 1]
SRC = os.path.join(ROOT, "ArtSource", "Character", "Parts")
RAW = os.path.join(SRC, "Raw")
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Art", "Characters", "TennisPlayer")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
BEFORE_DIR = os.path.join(OUT_DIR, "Before")
BLEND = os.path.join(SRC, "TennisPlayer.blend")
REPORT = os.path.join(SRC, "pipeline_report.json")

NAME = "TennisPlayer"
BODY_HEIGHT = 1.78   # 대머리 몸 키 (m). 머리카락이 올라가면 약 1.8m
BODY_FILE = "12_Body_Rigged.glb"
# Run은 VARCO "jog_looping" (반복용 동작). "run"은 출발 동작이라 반복이 맞지 않는다 (첫 시도에서 확인)
CLIPS = [("Idle", "13_Body_Anim_Idle.glb"), ("Walk", "14_Body_Anim_Walk.glb"), ("Run", "15_Body_Anim_Jog.glb")]
PARTS = {"Hair": "21_Hair_3D.glb", "Top": "31_Top_3D.glb", "Bottom": "41_Bottom_3D.glb", "Shoes": "51_Shoes_3D.glb"}
PARTS = {k: f for k, f in PARTS.items() if os.path.exists(os.path.join(RAW, f))}  # 아직 없는 파츠는 건너뛴다
# 삼각형 예산 (합 약 24k)과 텍스처 크기. 얼굴·상의는 화면에 크게 보여 2048
BUDGET = {"Body": 9000, "Hair": 4000, "Top": 5000, "Bottom": 3000, "Shoes": 3000}
TEX_SIZE = {"Body": 2048, "Hair": 1024, "Top": 2048, "Bottom": 1024, "Shoes": 1024}
CLEARANCE = {"Hair": 0.004, "Top": 0.008, "Bottom": 0.006}  # 몸에서 띄우는 거리 (m)

report = {"parts": {}}
H = BODY_HEIGHT


def garment_zone(top_cut, bottom_cut, side_cut):
    """파인 홈을 고칠 영역 (원본 옷 좌표): 위 top_cut, 아래 bottom_cut 비율과 좌우 side_cut 비율 바깥은
    깃·허리·밑단·소매 끝처럼 원래 접힌 곳이라 뺀다."""
    def make(bm):
        lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
        hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
        h, half = hi.z - lo.z, (hi.x - lo.x) / 2
        cx = (hi.x + lo.x) / 2
        return lambda c: lo.z + bottom_cut * h < c.z < hi.z - top_cut * h and abs(c.x - cx) < side_cut * half
    return make


DENT_ZONES = {"Top": garment_zone(0.25, 0.08, 0.5), "Bottom": garment_zone(0.12, 0.1, 0.9)}


# ---------- 0 정리 전 ----------

def export_before():
    """원본 파츠를 고치지 않고 한 줄로 늘어놓아 FBX로 (각자 1m로 정규화된 크기·원점 그대로)."""
    pc.reset()
    files = [BODY_FILE] + list(PARTS.values())
    objs = []
    for i, f in enumerate(files):
        new = pc.import_glb(os.path.join(RAW, f))
        roots = [o for o in new if o.parent is None]
        for o in roots:
            o.location.x += i * 1.15
        objs += new
        mesh = next(o for o in new if o.type == "MESH")
        key = "Body" if f == BODY_FILE else next(k for k, v in PARTS.items() if v == f)
        mesh.name = f"{key}_Before"
        report["parts"].setdefault(key, {})["raw"] = dict(pc.mesh_stats(mesh), size_m=pc.r(list(mesh.dimensions), 3))
    tmp = tempfile.mkdtemp(prefix="tp_before_")
    for img in bpy.data.images:
        if img.packed_file:
            img.filepath_raw = os.path.join(tmp, bpy.path.clean_name(img.name) + ".png")
            img.file_format = "PNG"
            img.save()
    os.makedirs(BEFORE_DIR, exist_ok=True)
    pc.select_only(objs, objs[0])
    bpy.ops.export_scene.fbx(filepath=os.path.join(BEFORE_DIR, NAME + "_Before.fbx"), use_selection=True,
                             object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", add_leaf_bones=False, bake_anim=False,
                             path_mode="COPY", embed_textures=True)
    shutil.rmtree(tmp, ignore_errors=True)


# ---------- 1 몸 ----------

def load_body():
    pc.reset()
    new = pc.import_glb(os.path.join(RAW, BODY_FILE))
    arm = next(o for o in new if o.type == "ARMATURE")
    body = next(o for o in new if o.type == "MESH")
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    arm.name = arm.data.name = NAME + "_Rig"
    body.name = body.data.name = NAME + "_Body"

    clips = {}
    for clip, file in CLIPS:
        new2 = pc.import_glb(os.path.join(RAW, file))
        a2 = next(o for o in new2 if o.type == "ARMATURE")
        act = a2.animation_data.action
        a2.animation_data.action = None
        act.name = clip
        act.use_fake_user = True
        pc.remove_objects(new2)
        pc.shift_keys(act, -act.frame_range[0])  # 처음 키가 2프레임(0.067초)에서 시작
        clips[clip] = act
    bpy.data.orphans_purge(do_recursive=True)

    report["parts"]["Body"]["clean"] = pc.clean_mesh(body, zip_dist=0.002, tear_size=0.05)

    # 키 1.78m, 발밑 중앙 원점: v' = k (v - center)
    v = pc.verts(body)
    lo, hi = pc.bounds(v)
    k = H / (hi[2] - lo[2])
    feet = v[v[:, 2] < lo[2] + 0.03 * (hi[2] - lo[2])]
    center = Vector(((feet[:, 0].min() + feet[:, 0].max()) / 2, (feet[:, 1].min() + feet[:, 1].max()) / 2, lo[2]))
    arm.location = -k * center
    arm.scale = (k, k, k)
    bpy.context.view_layer.update()
    pc.select_only([arm, body], arm)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for act in clips.values():
        pc.scale_location_keys(act, k)
    lo2, hi2 = pc.bounds(pc.verts(body))
    bones = arm.data.bones
    toe = bones["LeftToeBase"].head_local - bones["LeftFoot"].head_local
    report["body_spec"] = {"scale": pc.r(k), "height_m": pc.r(hi2[2] - lo2[2], 3), "bounds_min": pc.r(list(lo2), 3),
                           "facing": "-Y (Unity +Z)" if toe.y < 0 else "+Y",
                           "transforms_applied": [pc.r(list(o.scale), 3) for o in (arm, body)]}
    return arm, body, clips


def landmarks(arm, body):
    """맞추기에 쓰는 몸 치수: 뼈 머리 위치와 높이 띠별 몸통 너비·깊이 (T포즈 기본 자세)."""
    b = {bone.name: arm.matrix_world @ bone.head_local for bone in arm.data.bones}
    v = pc.verts(body)
    L = {"bones": b, "v": v, "head_top": v[:, 2].max(), "shoulder_x": abs(b["LeftArm"].x)}
    return L


# ---------- 2 파츠 검수 ----------

def load_part(key, decimate=True):
    new = pc.import_glb(os.path.join(RAW, PARTS[key]))
    obj = next(o for o in new if o.type == "MESH")
    pc.remove_objects([o for o in new if o is not obj])
    obj.name = obj.data.name = f"{NAME}_{key}"
    pc.select_only([obj], obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    info = report["parts"][key]
    # 면 1% 미만 조각 = 떠 있는 부스러기. 옷은 찢어진 작은 틈(8cm 이하 덩어리)을 오므려 닫고, 주머니처럼 파인 홈을 편다
    cloth = key in ("Top", "Bottom")
    info["clean"] = pc.clean_mesh(obj, zip_dist=0.002, min_part_ratio=0.01, tear_size=0.08 if cloth else 0.0,
                                  dent_zone=DENT_ZONES.get(key))
    if decimate:
        info["decimate"] = pc.decimate(obj, BUDGET[key])
    return obj


def set_verts(obj, co):
    obj.data.vertices.foreach_set("co", np.ascontiguousarray(co, np.float64).ravel())
    obj.data.update()


def push_outside(obj, target, offset):
    """target 표면 안쪽에 들어간 점만 바깥으로 밀어낸다 (Shrinkwrap: Outside).
    Outside Surface는 모든 점을 표면에 붙여 옷이 몸에 달라붙고 안감이 겉으로 비친다."""
    mod = obj.modifiers.new("Clearance", "SHRINKWRAP")
    mod.target = target
    mod.wrap_method = "NEAREST_SURFACEPOINT"
    mod.wrap_mode = "OUTSIDE"
    mod.offset = offset
    before = pc.verts(obj)
    pc.apply_modifier(obj, "Clearance")
    moved = np.linalg.norm(pc.verts(obj) - before, axis=1)
    return {"verts_pushed": int((moved > 1e-5).sum()), "max_push_m": pc.r(float(moved.max()), 4)}


# ---------- 3 맞추기 ----------

def smoothstep(x):
    t = np.clip(x, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def band(v, z, half):
    """높이 z 근처 단면의 (x 최소, x 최대, y 최소, y 최대)."""
    s = v[np.abs(v[:, 2] - z) < half]
    return s[:, 0].min(), s[:, 0].max(), s[:, 1].min(), s[:, 1].max()


def fit_to_body_band(obj, frac, z_lo, z_hi, body_v, slack_x, slack_y, xmax):
    """옷을 [z_lo, z_hi] 높이에 세우고, 옷 높이 frac 지점의 단면을 같은 높이의 몸 단면에 맞춘다.
    (상자 전체로 맞추면 깃·밑단·뒤판처럼 튀어나온 곳 때문에 앞뒤가 어긋난다)"""
    v = pc.verts(obj)
    lo, hi = pc.bounds(v)
    sx0, sx1, sy0, sy1 = band(v, lo[2] + frac * (hi[2] - lo[2]), 0.02 * (hi[2] - lo[2]))
    z = z_lo + frac * (z_hi - z_lo)
    bv = body_v[np.abs(body_v[:, 0]) < xmax]
    bx0, bx1, by0, by1 = band(bv, z, 0.012 * H)
    s = np.array([(bx1 - bx0) * slack_x / (sx1 - sx0), (by1 - by0) * slack_y / (sy1 - sy0), (z_hi - z_lo) / (hi[2] - lo[2])])
    src = np.array([(sx0 + sx1) / 2, (sy0 + sy1) / 2, lo[2]])
    dst = np.array([(bx0 + bx1) / 2, (by0 + by1) / 2, z_lo])
    set_verts(obj, (v - src) * s + dst)
    return s


def fit_bottom(obj, L):
    b = L["bones"]
    waist_z = b["Hips"].z + 0.075 * H          # 허리 밴드: 배꼽 조금 아래
    hem_z = b["LeftLeg"].z + 0.07 * H           # 밑단: 무릎 조금 위
    s = fit_to_body_band(obj, 0.8, hem_z, waist_z, L["v"], 1.1, 1.2, 0.3 * H)  # 엉덩이 높이 단면보다 넉넉하게
    report["parts"]["Bottom"]["fit"] = {"waist_z": pc.r(waist_z, 3), "hem_z": pc.r(hem_z, 3), "scale_xyz": pc.r(list(s), 3)}
    return waist_z, hem_z


def straighten_sleeves(obj):
    """상의 소매가 아래로 처져 있으면 어깨를 축으로 들어 올려 T포즈 팔(수평)에 맞춘다. 겨드랑이 쪽은 서서히."""
    v = pc.verts(obj)
    lo, hi = pc.bounds(v)
    height = hi[2] - lo[2]
    mid = v[np.abs(v[:, 2] - (lo[2] + 0.35 * height)) < 0.03 * height]  # 소매 아래 몸통 높이
    torso_half = np.abs(mid[:, 0]).max()
    shoulder_z = hi[2] - 0.15 * height
    armpit_z = lo[2] + 0.5 * height
    # 소매 = 겨드랑이보다 위 + 몸통 바깥. 밑단이 퍼져 몸통보다 넓어도 건드리지 않는다
    wz = smoothstep((v[:, 2] - (armpit_z - 0.08 * height)) / (0.12 * height))
    out = {"torso_half_width": pc.r(torso_half, 3)}
    for side in (1, -1):
        sel = v[:, 0] * side
        upper = (sel > 0) & (v[:, 2] > armpit_z)
        ends = v[upper & (sel > 0.9 * sel[upper].max())]
        end = ends.mean(0)
        pivot = np.array([side * torso_half, end[1], shoulder_z])
        theta = math.atan2(pivot[2] - end[2], abs(end[0]) - torso_half)  # 수평에서 아래로 처진 각도
        blend = 0.06 * (hi[0] - lo[0])
        w = smoothstep((sel - (torso_half - blend)) / (2 * blend)) * wz  # 몸통은 그대로, 소매는 전부
        a = -side * theta * w     # 위로 드는 쪽 (Y축 회전)
        p = v - pivot
        rx = p[:, 0] * np.cos(a) + p[:, 2] * np.sin(a)
        rz = -p[:, 0] * np.sin(a) + p[:, 2] * np.cos(a)
        moving = (sel > 0) & (w > 0)
        v[moving, 0] = rx[moving] + pivot[0]
        v[moving, 2] = rz[moving] + pivot[2]
        out["left" if side > 0 else "right"] = {"sleeve_drop_deg": pc.r(math.degrees(theta), 1)}
    set_verts(obj, v)
    return torso_half, out


def fit_top(obj, L, bottom_waist_z):
    b = L["bones"]
    torso_half_raw, sleeves = straighten_sleeves(obj)
    collar_z = b["Neck"].z + 0.025 * H
    hem_z = b["Hips"].z - 0.02 * H              # 반바지 허리를 덮는다
    # 몸통 단면(옷 높이 35%, 소매 아래)을 같은 높이의 몸통에 맞춘다. X 배율이 소매 길이에도 그대로 적용된다
    s = fit_to_body_band(obj, 0.35, hem_z, collar_z, L["v"], 1.15, 1.3, L["shoulder_x"] * 0.95)
    # 소매 끝(커프) 중심 높이를 팔 높이에 맞춘다: 겨드랑이 위 + 몸통 바깥만 옮긴다
    v = pc.verts(obj)
    arm_z = (b["LeftArm"].z + b["RightArm"].z) / 2
    torso_half = torso_half_raw * s[0]
    armpit_z = hem_z + 0.5 * (collar_z - hem_z)
    sel = np.abs(v[:, 0])
    upper = v[:, 2] > armpit_z
    cuffs = v[upper & (sel > 0.9 * sel[upper].max())]
    dz = float(arm_z - cuffs[:, 2].mean())
    w = smoothstep((sel - torso_half) / 0.06) * smoothstep((v[:, 2] - (armpit_z - 0.05)) / 0.08)
    v[:, 2] += dz * w
    set_verts(obj, v)
    report["parts"]["Top"]["fit"] = dict(sleeves, collar_z=pc.r(collar_z, 3), hem_z=pc.r(hem_z, 3),
                                         scale_xyz=pc.r(list(s), 3), sleeve_raise_m=pc.r(float(dz), 3),
                                         sleeve_end_x=pc.r(float(sel.max()), 3))
    return hem_z, collar_z, float(sel.max())


def fit_shoes(obj, L, arm):
    """신발 두 짝을 나눠 각 발에 맞춘다: 발 방향으로 돌리고, 발 길이 ×1.12로 키우고, 바닥에 올린다."""
    b = L["bones"]
    v = L["v"]
    pc.select_only([obj], obj)
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.separate(type="LOOSE")
        bpy.ops.object.mode_set(mode="OBJECT")
    pieces = sorted([o for o in bpy.context.scene.objects if o.name.startswith(obj.name)], key=lambda o: -len(o.data.polygons))
    shoes = pieces[:2]
    pc.remove_objects(pieces[2:])
    info = {}
    for shoe in shoes:
        sv = pc.verts(shoe)
        side = "Left" if sv[:, 0].mean() > 0 else "Right"  # 몸 왼쪽 = +X (정면이 -Y)
        foot_bone, toe_bone = b[f"{side}Foot"], b[f"{side}ToeBase"]
        fv = v[(v[:, 2] < foot_bone.z) & (np.sign(v[:, 0]) == (1 if side == "Left" else -1))]
        foot_dir = np.array([toe_bone.x - foot_bone.x, toe_bone.y - foot_bone.y])
        foot_dir /= np.linalg.norm(foot_dir)
        # 신발 긴 축 (주성분), 앞쪽 = 점이 낮게 깔린 쪽이 아니라 원본에서 -Y 쪽
        c = sv[:, :2] - sv[:, :2].mean(0)
        axis = np.linalg.eigh(c.T @ c)[1][:, -1]
        if axis[1] > 0:
            axis = -axis
        ang = math.atan2(foot_dir[1], foot_dir[0]) - math.atan2(axis[1], axis[0])
        rot = np.array([[math.cos(ang), -math.sin(ang)], [math.sin(ang), math.cos(ang)]])
        sv[:, :2] = (sv[:, :2] - sv[:, :2].mean(0)) @ rot.T
        along_shoe = sv[:, :2] @ foot_dir
        fc = fv[:, :2] - fv[:, :2].mean(0)
        along_foot = fc @ foot_dir
        k = (along_foot.max() - along_foot.min()) * 1.12 / (along_shoe.max() - along_shoe.min())
        sv *= k
        sv[:, 2] -= sv[:, 2].min()  # 밑창 = 바닥
        mid_shoe = (along_shoe.max() + along_shoe.min()) / 2 * k
        foot_mid = fv[:, :2].mean(0) + foot_dir * (along_foot.max() + along_foot.min()) / 2
        sv[:, :2] += foot_mid - foot_dir * mid_shoe
        set_verts(shoe, sv)
        shoe.name = shoe.data.name = f"{NAME}_Shoe_{side}"
        info[side] = {"scale": pc.r(k, 3), "turn_deg": pc.r(math.degrees(ang), 1), "top_z": pc.r(float(sv[:, 2].max()), 3)}
    # 두 짝을 다시 한 오브젝트로 (머티리얼 하나)
    pc.select_only(shoes, shoes[0])
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = joined.data.name = f"{NAME}_Shoes"
    report["parts"]["Shoes"]["fit"] = info
    return joined


def fit_hair(obj, L):
    """마네킹 머리에 씌운 헤어 3D: 마네킹 두상으로 방향·크기·위치를 몸 머리에 맞춘 뒤, 회색 마네킹 면을 지워 머리카락만 남긴다."""
    cols = pc.face_colors(obj)
    sat = cols.max(1) - cols.min(1)
    lum = cols @ np.array([0.299, 0.587, 0.114])
    mannequin = (sat < 0.08) & (lum > 0.35)  # 매끈한 밝은 회색
    v = pc.verts(obj)
    face_v = [np.array(p.vertices) for p in obj.data.polygons]
    man_idx = np.unique(np.concatenate([face_v[i] for i in np.nonzero(mannequin)[0]]))
    hair_idx = np.unique(np.concatenate([face_v[i] for i in np.nonzero(~mannequin)[0]]))
    man, hair = v[man_idx], v[hair_idx]
    top = hair[:, 2].max()
    # 정면: 머리카락이 덮지 않은 회색 얼굴 쪽. 머리 높이(위쪽 45%)에서 머리카락 중심 → 마네킹 중심 방향
    lo_z = man[:, 2].min()
    head_lo = lo_z + 0.55 * (top - lo_z)
    fwd = man[man[:, 2] > head_lo][:, :2].mean(0) - hair[hair[:, 2] > head_lo][:, :2].mean(0)
    ang = math.atan2(-1.0, 0.0) - math.atan2(fwd[1], fwd[0])  # 정면을 -Y로
    c, s = math.cos(ang), math.sin(ang)
    v[:, :2] = v[:, :2] @ np.array([[c, s], [-s, c]])
    # 크기: 머리 높이(위쪽 45%)의 전체 너비(두상 + 옆머리) = 몸 머리 너비 × 1.1
    # 위치: 머리 높이 단면 중심을 몸 머리 중심에, 머리카락 꼭대기를 정수리 1.2% 위에
    head = v[v[:, 2] > head_lo]
    body_head = L["v"][L["v"][:, 2] > L["bones"]["Head"].z + 0.02 * H]
    body_head = body_head[np.abs(body_head[:, 0]) < 0.15 * H]
    k = (body_head[:, 0].max() - body_head[:, 0].min()) * 1.1 / (head[:, 0].max() - head[:, 0].min())
    v = v * k
    head_c = (head[:, :2].min(0) + head[:, :2].max(0)) / 2 * k
    body_c = (body_head[:, :2].min(0) + body_head[:, :2].max(0)) / 2
    v[:, :2] += body_c - head_c
    v[:, 2] += L["head_top"] + 0.012 * H - top * k
    set_verts(obj, v)
    # 마네킹 면 지우기 → 머리카락만
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(mannequin)[0]], context="FACES")
    loose = [x for x in bm.verts if not x.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    report["parts"]["Hair"]["fit"] = {"turn_deg": pc.r(math.degrees(ang), 1), "scale": pc.r(k, 3),
                                      "mannequin_faces_removed": int(mannequin.sum()), "hair_faces": int((~mannequin).sum())}


# ---------- 4 스킨, 5 가려지는 몸 ----------

def skin(part, body, arm, rigid_bone=None):
    for g in body.vertex_groups:
        part.vertex_groups.new(name=g.name)
    if rigid_bone:
        part.vertex_groups[rigid_bone].add(range(len(part.data.vertices)), 1.0, "REPLACE")
    else:
        mod = part.modifiers.new("Weights", "DATA_TRANSFER")
        mod.object = body
        mod.use_object_transform = True
        mod.use_vert_data = True
        mod.data_types_verts = {"VGROUP_WEIGHTS"}
        mod.vert_mapping = "POLYINTERP_NEAREST"
        mod.layers_vgroup_select_src = "ALL"
        mod.layers_vgroup_select_dst = "NAME"
        pc.apply_modifier(part, "Weights")
    part.parent = arm
    part.matrix_parent_inverse = arm.matrix_world.inverted()
    mod = part.modifiers.new("Armature", "ARMATURE")
    mod.object = arm


def shoe_collar_z(shoes):
    """신발 입구 높이: 신발 위쪽 절반에 있는 열린 가장자리 점들의 평균 (밑창 쪽 작은 틈은 뺀다)."""
    v = pc.verts(shoes)
    top = v[:, 2].max()
    bm = bmesh.new()
    bm.from_mesh(shoes.data)
    zs = [z for z in ((shoes.matrix_world @ p.co).z for p in bm.verts if p.is_boundary) if z > 0.5 * top]
    bm.free()
    return float(np.mean(zs)) if zs else 0.6 * top


def hide_covered_body(body, garments, below_z=0.0, reach=0.07, edge_margin=0.03):
    """옷·신발에 덮인 몸 점을 지운다: 점에서 바깥(노멀)으로 쏜 선이 reach 안에서 옷에 닿고,
    옷 가장자리(소매 끝·밑단·깃·신발 입구)에서 edge_margin보다 먼 점만. 가장자리 근처 살은 남겨 움직일 때 틈이 안 보이게.
    below_z 아래(신발 속 발)는 신발 밖으로 삐져나와도 보이지 않게 통째로 지운다."""
    dg = bpy.context.evaluated_depsgraph_get()
    edge_pts = []
    trees = {}
    for name, g in garments.items():
        trees[name] = BVHTree.FromObject(g, dg)
        bm = bmesh.new()
        bm.from_mesh(g.data)
        edge_pts += [g.matrix_world @ v.co for v in bm.verts if v.is_boundary]
        bm.free()
    kd = KDTree(len(edge_pts))
    for i, p in enumerate(edge_pts):
        kd.insert(p, i)
    kd.balance()
    mw = body.matrix_world
    nm = mw.to_3x3()
    remove = np.zeros(len(body.data.vertices), bool)
    counts = dict.fromkeys(garments, 0)
    counts["feet_in_shoes"] = 0
    for i, vert in enumerate(body.data.vertices):
        co = mw @ vert.co
        if co.z < below_z:
            remove[i] = True
            counts["feet_in_shoes"] += 1
            continue
        if kd.find(co)[2] < edge_margin:
            continue
        n = (nm @ vert.normal).normalized()
        for name, tree in trees.items():
            if tree.ray_cast(co + n * 0.001, n, reach)[0] is not None:
                remove[i] = True
                counts[name] += 1
                break
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    before = len(bm.faces)
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(remove)[0]], context="VERTS")
    after = len(bm.faces)
    bm.to_mesh(body.data)
    bm.free()
    return {"verts_by_region": counts, "faces_removed": before - after}


# ---------- 6 내보내기 ----------

def export(arm, meshes, clips):
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
    pc.select_only([arm] + meshes, arm)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             add_leaf_bones=False, mesh_smooth_type="FACE",
                             bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             path_mode="STRIP")  # 텍스처는 Unity에서 URP 머티리얼로 연결
    pc.use_action(arm, clips["Idle"])
    bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True, relative_remap=True)
    return path


def verify(path):
    pc.reset()
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    pts = np.concatenate([pc.verts(o) for o in meshes])
    report["fbx_check"] = {
        "file_mb": pc.r(os.path.getsize(path) / 1e6, 1),
        "meshes": {o.name: pc.mesh_stats(o)["tris"] for o in meshes},
        "tris_total": sum(pc.mesh_stats(o)["tris"] for o in meshes),
        "height_m": pc.r(float(pts[:, 2].max() - pts[:, 2].min()), 3),
        "bones": len(next(o for o in bpy.data.objects if o.type == "ARMATURE").data.bones),
        "fbx_takes": sorted({m.decode() for m in re.findall(rb"([\w|.]+)\x00\x01AnimStack", open(path, "rb").read())}),
        "materials": sorted({m.name for o in meshes for m in o.data.materials}),
    }


def main():
    export_before()
    arm, body, clips = load_body()
    L = landmarks(arm, body)

    bottom = load_part("Bottom")
    waist_z, shorts_hem_z = fit_bottom(bottom, L)
    report["parts"]["Bottom"]["clearance"] = push_outside(bottom, body, CLEARANCE["Bottom"])

    top = load_part("Top")
    shirt_hem_z, collar_z, sleeve_end_x = fit_top(top, L, waist_z)
    # 상의는 몸과 반바지 허리 둘 다 바깥에 있어야 한다: 임시로 합친 충돌용 메시에 밀어낸다
    collider = body.copy()
    collider.data = body.data.copy()
    collider.modifiers.clear()
    bpy.context.scene.collection.objects.link(collider)
    b2 = bottom.copy()
    b2.data = bottom.data.copy()
    bpy.context.scene.collection.objects.link(b2)
    pc.select_only([collider, b2], collider)
    bpy.ops.object.join()
    report["parts"]["Top"]["clearance"] = push_outside(top, collider, CLEARANCE["Top"])
    pc.remove_objects([collider])

    shoes = fit_shoes(load_part("Shoes"), L, arm)
    parts = {"Body": body, "Top": top, "Bottom": bottom, "Shoes": shoes}
    if "Hair" in PARTS:
        hair = load_part("Hair", decimate=False)  # 마네킹을 지운 뒤에 줄인다
        fit_hair(hair, L)
        report["parts"]["Hair"]["decimate"] = pc.decimate(hair, BUDGET["Hair"])
        report["parts"]["Hair"]["clearance"] = push_outside(hair, body, CLEARANCE["Hair"])
        parts["Hair"] = hair

    arm.data.pose_position = "REST"
    for part in (bottom, top, shoes):
        skin(part, body, arm)
    if "Hair" in parts:
        skin(parts["Hair"], body, arm, rigid_bone="Head")
    arm.data.pose_position = "POSE"

    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    feet_cut = shoe_collar_z(shoes) - 0.01
    report["parts"]["Body"]["hidden_under_clothes"] = dict(
        hide_covered_body(body, {"Top": top, "Bottom": bottom, "Shoes": shoes}, below_z=feet_cut), feet_cut_z=pc.r(feet_cut, 3))
    arm.data.pose_position = "POSE"
    report["parts"]["Body"]["decimate"] = pc.decimate(body, BUDGET["Body"])

    os.makedirs(TEX_DIR, exist_ok=True)
    materials = {}
    for key, obj in parts.items():
        cloth = key in ("Top", "Bottom")
        materials[key] = pc.game_material(obj, f"{NAME}_{key}", TEX_DIR, TEX_SIZE[key], fill_gray=cloth, lift_dark=key == "Top")
        obj.name = obj.data.name = f"{NAME}_{key}"
    report["materials"] = materials

    report["animations"] = {name: dict(pc.tidy_loop(arm, act), facing=pc.fix_facing(arm, act),
                                       ground=pc.ground_feet(arm, act, list(parts.values())))
                            for name, act in clips.items()}
    report["parts_final"] = {o.name: pc.mesh_stats(o)["tris"] for o in parts.values()}

    path = export(arm, list(parts.values()), clips)
    verify(path)
    with open(REPORT, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print("PIPELINE_OK " + json.dumps(report, ensure_ascii=False))


main()
