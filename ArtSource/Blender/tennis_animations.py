"""테니스 동작을 손으로 짠 핵심 자세로 만든다 (Blender 5.2, 백그라운드 실행). 모션캡처가 들어오기 전 임시 동작.

  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b ArtSource/Character/Final/TennisPlayer_Final.blend ^
      --python ArtSource/Blender/tennis_animations.py -- <저장할 .blend>

  1 핵심 자세(KEYS)마다 몸통은 직접(FK), 팔·다리는 목표 지점(IK)으로 잡는다
     - 라켓은 오른손에 고정: 라켓 손잡이 위치·샤프트 방향·타구면 방향을 주면 오른손 위치와 방향이 정해진다
     - 왼손은 라켓 목(throat), 양손 그립(grip2), 또는 직접 준 위치
     - 발은 바닥에 붙이고 무릎은 IK로 굽힌다
  2 자세 사이는 Blender 보간, 매 프레임 결과를 그대로 키로 굽고 IK·빈 오브젝트는 지운다
  3 동작마다 NLA 스트립으로 넣는다 (Idle·Walk·Run 옆). 임팩트 프레임은 CONTACT에 적는다
  4 오른손에 라켓 표식(RacketSocket·Tip·Face)을 붙인다. Unity가 이 세 점으로 라켓 모델을 손에 단다
  5 손 쥐기 셰이프 키 GripR·GripL (손가락 뼈가 없어서). Unity가 블렌드셰이프 값을 정한다

FBX까지 한 번에: 뒤에 finalize_character.py를 이어 붙이고 FBX 경로를 더 준다
  ... --python ArtSource/Blender/tennis_animations.py --python ArtSource/Blender/finalize_character.py ^
      -- ArtSource/Character/Final/TennisPlayer_Tennis.blend Assets/_Project/Art/Characters/TennisPlayer/TennisPlayer.fbx

좌표는 캐릭터 기준 (r 오른쪽, f 앞, u 위, m). 캐릭터는 Blender -Y를 본다. 각도는 도, 몸 돌리기(yaw)는 + 가 왼쪽.
"""
import math
from copy import deepcopy
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30
GRIP_ANGLE = math.radians(0)   # 샤프트가 엄지 방향에서 손가락 방향으로 기운 각도. 0 = 손잡이가 손바닥을 가로질러 손목을 펴면 라켓이 아래팔과 직각 (프로 선수 영상의 ㄴ자)
PALM = 0.112                   # 손목(손 뼈 시작)에서 손잡이 축까지 (손가락이 갈라지는 곳 바로 아래)
HANDLE_OFFSET = 0.041          # 손 뼈 축에서 손바닥 쪽으로 손잡이 중심까지 (손바닥 면 0.022 + 손잡이 반지름)
RIGHT_GRIP = 0.09              # 라켓 끝에서 오른손 손잡이 지점까지
LEFT_GRIP = 0.17               # 양손 그립의 왼손
THROAT = 0.30                  # 준비 자세에서 왼손이 받치는 라켓 목
ANKLE = 0.098                  # 쉬는 자세의 발목 높이
READY_FEET = {"L": (-0.30, 0.0, 10.0, 0.0), "R": (0.30, 0.0, -10.0, 0.0)}  # (r, f, 발끝 yaw, 들기)


def P(r, f, u):
    return Vector((-r, -f, u))


def D(r, f, u):
    return P(r, f, u).normalized()


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 4, "Z")


def rot(deg, axis):
    return Matrix.Rotation(math.radians(deg), 4, axis)


# ---------- 라켓·손 ----------

def hand_frame(s, p, right):
    """샤프트 방향 s, 손바닥 방향 p로 손 뼈의 방향(열 = X, Y 손가락, Z 손등)을 만든다."""
    q = p.cross(s)
    c, n = math.cos(GRIP_ANGLE), math.sin(GRIP_ANGLE)
    if right:  # 오른손: 엄지 = -X
        thumb, fingers = s * c - q * n, s * n + q * c
        x = -thumb
    else:      # 왼손: 엄지 = +X
        thumb, fingers = s * c + q * n, s * n - q * c
        x = thumb
    return Matrix((x, fingers, -p)).transposed()


def hand_matrix(handle_point, s, p, right):
    """손잡이 한 점을 쥔 손 뼈의 월드 행렬 (원점 = 손목)."""
    m = hand_frame(s, p, right)
    wrist = handle_point - m.col[1] * PALM - p * HANDLE_OFFSET
    out = m.to_4x4()
    out.translation = wrist
    return out


UPPER_ARM, FOREARM = 0.268, 0.26
WRIST_TWIST_SHARE = 0.6   # 손 비틀림 중 아래팔이 나눠 갖는 몫 (뼈에 비틀림 뼈가 없어서 손목·팔꿈치에 나눈다)
WRIST_MAX_BEND = 50.0     # 손목이 꺾일 수 있는 최대 각도 (도). 더 꺾이면 라켓 방향을 팔에 맞춘다


ELBOW_NEUTRAL_WRIST = 0.4  # 팔꿈치 방향: 손목이 곧은 쪽(1)과 자세마다 정한 방향(0) 사이


def elbow_pole(shoulder, hand, natural):
    """팔꿈치 극 목표. 팔꿈치는 어깨·손목에서 정해지는 원 위에 있고, 그 원에서 두 방향을 섞어 고른다.
      - 손목이 곧은 쪽: 손가락 축을 손목 뒤로 늘인 점에 가장 가까운 자리 (손목이 덜 꺾인다)
      - natural: 자세마다 정한 팔꿈치 방향 (어깨 기준, 대개 아래·옆). 손목만 보고 고르면 팔꿈치가 들려 어깨가 과하게 돈다"""
    wrist = hand.translation
    axis = wrist - shoulder
    d = max(axis.length, 1e-4)
    axis /= d
    a = (UPPER_ARM ** 2 - FOREARM ** 2 + d * d) / (2 * d)
    center = shoulder + axis * a
    want = wrist - hand.col[1].to_3d().normalized() * FOREARM
    neutral = want - center
    neutral -= axis * neutral.dot(axis)
    pose = natural - axis * natural.dot(axis)
    v = pose.normalized() * (1 - ELBOW_NEUTRAL_WRIST)
    if neutral.length > 0.02:
        v += neutral.normalized() * ELBOW_NEUTRAL_WRIST
    if v.length < 1e-4:
        v = pose
    return center + v.normalized() * 0.6


def fix_arm_roll(mats, side):
    """팔꿈치가 사람 팔꿈치처럼 굽게: 위팔·아래팔의 굽힘 축을 어깨-팔꿈치-손목 평면에 수직으로 맞춘다.
    IK는 팔꿈치 위치만 맞추고 위팔을 아무렇게나 돌려 두어, 팔꿈치가 관절 반대로 굽은 것처럼 어깨·팔꿈치 메시가 꼬였다.
    쉬는 자세(T자, 손바닥 아래)에서 팔꿈치는 앞으로 굽고 그 축이 오른팔 +Z, 왼팔 -Z다. 손의 월드 방향은 그대로."""
    shoulder = mats[side + "Arm"].translation
    elbow = mats[side + "ForeArm"].translation
    wrist = mats[side + "Hand"].translation
    upper, fore = (elbow - shoulder).normalized(), (wrist - elbow).normalized()
    axis = upper.cross(fore)
    straightness = min(1.0, max(0.0, (axis.length - 0.05) / 0.25))  # 거의 펴진 팔은 굽힘 축이 없어 그대로 둔다
    if straightness <= 0:
        return
    axis = axis.normalized() * (1 if side == "Right" else -1)
    for bone, y in ((side + "Arm", upper), (side + "ForeArm", fore)):
        z = (axis - y * axis.dot(y)).normalized()
        natural = Matrix((y.cross(z), y, z)).transposed().to_quaternion()
        q = mats[bone].to_quaternion()
        if q.dot(natural) < 0:
            natural.negate()
        m = q.slerp(natural, straightness).to_matrix().to_4x4()
        m.translation = mats[bone].translation
        mats[bone] = m


def hold_racket_left(mats, along, weight):
    """양손 그립·라켓 목 받치기: 이번 프레임의 실제 라켓에서 왼손 자리를 다시 잡고 왼팔을 그 자리로 다시 푼다 (2뼈 IK).
    핵심 자세 사이를 보간하면 라켓(오른손)과 왼손이 따로 움직여 손잡이에서 벌어지기 때문.
    along: 손잡이 끝에서 왼손 손잡이 지점까지, weight: 0 = 그대로, 1 = 라켓을 쥔다."""
    racket = mats["RightHand"] @ racket_in_hand()
    s = racket.col[2].to_3d().normalized()
    n = racket.col[1].to_3d().normalized()
    target = hand_matrix(racket.translation + s * along, s, -n, False)
    shoulder = mats["LeftArm"].translation
    to = target.translation - shoulder
    reach = min(max(to.length, abs(UPPER_ARM - FOREARM) + 1e-3), UPPER_ARM + FOREARM - 1e-3)
    axis = to.normalized()
    pole = elbow_pole(shoulder, target, mats["LeftForeArm"].translation - shoulder)
    side = pole - shoulder
    side = (side - axis * side.dot(axis)).normalized()
    a = (UPPER_ARM ** 2 - FOREARM ** 2 + reach * reach) / (2 * reach)
    elbow = shoulder + axis * a + side * math.sqrt(max(0.0, UPPER_ARM ** 2 - a * a))
    wrist = shoulder + axis * reach
    for bone, head, tail in (("LeftArm", shoulder, elbow), ("LeftForeArm", elbow, wrist)):
        q = mats[bone].to_quaternion()
        aimed = (mats[bone].to_3x3() @ Vector((0, 1, 0))).rotation_difference((tail - head).normalized()) @ q
        m = q.slerp(aimed, weight).to_matrix().to_4x4()
        m.translation = mats[bone].translation.lerp(head, weight)
        mats[bone] = m
    hand = mats["LeftHand"].to_quaternion().slerp(target.to_quaternion(), weight).to_matrix().to_4x4()
    hand.translation = mats["LeftHand"].translation.lerp(wrist, weight)
    mats["LeftHand"] = hand


def relax_wrist(mats, side, max_bend=WRIST_MAX_BEND):
    """구운 프레임에서 손목을 사람 손목처럼: 비틀림 일부를 아래팔로 옮기고, 꺾임은 max_bend까지.
    손의 월드 방향은 꺾임을 자를 때만 바뀐다 (라켓은 손을 따라간다)."""
    fore, hand = mats[side + "ForeArm"], mats[side + "Hand"]
    qf, qh = fore.to_quaternion(), hand.to_quaternion()
    q = qf.inverted() @ qh
    if q.w < 0:
        q.negate()
    twist = Quaternion((q.w, 0.0, q.y, 0.0))
    if twist.magnitude < 1e-6:
        twist = Quaternion()
    twist.normalize()
    swing = q @ twist.inverted()
    limit = math.radians(max_bend)
    if swing.angle > limit:
        swing = Quaternion(swing.axis, limit)
    give = Quaternion((0.0, 1.0, 0.0), 2 * math.atan2(twist.y, twist.w) * WRIST_TWIST_SHARE)
    qf2 = qf @ give
    fore2 = qf2.to_matrix().to_4x4()
    fore2.translation = fore.translation
    hand2 = (qf2 @ give.inverted() @ swing @ twist).to_matrix().to_4x4()
    hand2.translation = hand.translation
    mats[side + "ForeArm"], mats[side + "Hand"] = fore2, hand2


def racket_axes(s, n):
    s = s.normalized()
    n = (n - s * n.dot(s)).normalized()
    return s, n


def racket_matrix(grip, s, n):
    """라켓 모델 행렬: 원점 = 손잡이 끝, Z = 샤프트, Y = 타구면(오른손 손바닥 쪽)."""
    butt = grip - s * RIGHT_GRIP
    m = Matrix((n.cross(s), n, s)).transposed().to_4x4()
    m.translation = butt
    return m


def racket_in_hand():
    """오른손 뼈 기준 라켓 위치 (어느 자세에서나 같다)."""
    s, n = Vector((0, 0, 1)), Vector((0, 1, 0))
    return hand_matrix(Vector(), s, n, True).inverted() @ racket_matrix(Vector(), s, n)


# ---------- 손 쥐기 (셰이프 키) ----------
# 리그에 손가락 뼈가 없어서, 손가락이 손잡이를 감싼 모양을 셰이프 키 GripR·GripL로 만든다 (Unity에서 블렌드셰이프).
# 손 뼈 공간 (Y 손가락, Z 손등, 오른손 엄지 -X): 손잡이 축은 손바닥을 비스듬히 가로지르고(GRIP_ANGLE),
# 손가락은 손잡이 축 바로 위에서부터 축을 감아 돈다. 엄지는 손 길이 축으로 돌려 손잡이 반대쪽을 덮는다.
GRIP_CURL_RADIUS = 0.035   # 손가락 가운데 선이 손잡이 축을 도는 반지름
GRIP_MAX_CURL = 170.0      # 손끝이 손바닥을 뚫지 않게
THUMB_BASE_X = 0.035       # 엄지가 손바닥에서 갈라지는 곳 (엄지 쪽 폭)
THUMB_WRAP = 75.0


def grip_offsets(points, right):
    """손 뼈 공간 점들 → 쥔 모양 점들."""
    side = -1.0 if right else 1.0   # 엄지 쪽 x 부호
    c, s = math.cos(GRIP_ANGLE), math.sin(GRIP_ANGLE)
    axis = Vector((side * c, s, 0.0))           # 손잡이 방향 (엄지 쪽으로)
    along = Vector((-side * s, c, 0.0))         # 손바닥 위에서 손잡이에 수직, 손끝 쪽
    up = Vector((0.0, 0.0, 1.0))                # 손등 쪽
    center = Vector((0.0, PALM, -HANDLE_OFFSET))
    out = []
    for p in points:
        q = p.copy()
        d = p - center
        b = d.dot(along)
        if b > 0:  # 손잡이 축 위를 지난 손가락: 축을 감아 돈다 (층마다 반지름 유지)
            h = d.dot(up)
            phi = min(b / GRIP_CURL_RADIUS, math.radians(GRIP_MAX_CURL))
            q = center + axis * d.dot(axis) + (up * math.cos(phi) + along * math.sin(phi)) * h
        elif p.x * side > THUMB_BASE_X:  # 엄지: 손 길이 축으로 돌려 손바닥 쪽으로
            t = min(1.0, (p.x * side - THUMB_BASE_X) / 0.035)
            pivot = Vector((side * THUMB_BASE_X, p.y, -0.01))
            q = pivot + Matrix.Rotation(math.radians(THUMB_WRAP * t * side), 3, "Y") @ (p - pivot)
        out.append(q)
    return out


def add_grip_shape_keys(arm, body):
    if body.data.shape_keys is None:
        body.shape_key_add(name="Basis", from_mix=False)
    for side, name in (("Right", "GripR"), ("Left", "GripL")):
        old = body.data.shape_keys.key_blocks.get(name)
        if old is not None:
            body.shape_key_remove(old)
        key = body.shape_key_add(name=name, from_mix=False)
        bone = arm.matrix_world @ arm.data.bones[side + "Hand"].matrix_local
        to_hand = bone.inverted() @ body.matrix_world
        to_mesh = body.matrix_world.inverted() @ bone
        group = body.vertex_groups[side + "Hand"].index
        idx = [v.index for v in body.data.vertices
               if any(g.group == group and g.weight > 0.5 for g in v.groups)]
        moved = grip_offsets([to_hand @ body.data.vertices[i].co for i in idx], side == "Right")
        for i, p in zip(idx, moved):
            key.data[i].co = to_mesh @ p
        print(f"[tennis] {name}: 손 점 {len(idx)}개")


# Unity에서 라켓을 손에 붙일 표식: 위치만 보면 되도록 세 점 (축 변환과 상관없이 방향이 맞는다)
RACKET_MARKERS = {"RacketSocket": Vector((0, 0, 0)), "RacketTip": Vector((0, 0, 0.5)), "RacketFace": Vector((0, 0.1, 0))}


def add_racket_markers(arm):
    """오른손 뼈에 붙은 빈 오브젝트: 손잡이 끝, 샤프트 쪽 한 점, 타구면 쪽 한 점."""
    bone = arm.data.bones["RightHand"]
    for name, local in RACKET_MARKERS.items():
        old = bpy.data.objects.get(name)
        if old is not None:
            bpy.data.objects.remove(old, do_unlink=True)
        o = bpy.data.objects.new(name, None)
        o.empty_display_size = 0.03
        bpy.context.scene.collection.objects.link(o)
        o.parent = arm
        o.parent_type = "BONE"
        o.parent_bone = bone.name
        o.matrix_parent_inverse = Matrix.Identity(4)
        # 뼈 자식은 뼈 끝(tail) 기준이라 길이만큼 되돌린다
        o.matrix_basis = Matrix.Translation((0, -bone.length, 0)) @ racket_in_hand() @ Matrix.Translation(local)


# ---------- 핵심 자세 ----------
# 각 자세: frame, root (r,f,u 이동), hips·torso (yaw), lean (앞으로 숙임), side (+ 오른쪽 어깨 내림),
# look (머리 yaw, pitch), racket (손잡이 r,f,u / 샤프트 / 타구면), left ('throat' | 'grip2' | (위치, 손가락, 손등)),
# elbow / lelbow (팔이 다 펴졌을 때만 쓰는 팔꿈치 방향, 어깨 기준. 보통은 손 방향에서 자동으로 고른다 → elbow_pole),
# feet (바꿀 발만). 빠진 값은 앞 자세 그대로.
# frame·feet만 있는 키는 발만 옮긴다 (걸음 중간에 몸이 멈칫하지 않게).

READY = dict(root=(0, 0, -0.10), hips=0, torso=0, lean=14, side=0, look=(0, 0),
             racket=((0.10, 0.38, 1.00), (-0.35, 0.65, 0.55), (-1, 0, 0)), left="throat",
             elbow=(0.35, -0.2, -0.6), lelbow=(-0.35, -0.2, -0.6), feet=dict(READY_FEET))

CLIPS = {
    # 준비 자세: 무릎을 굽히고 가볍게 들썩인다 (반복)
    "Ready": (40, [
        dict(READY, frame=0),
        dict(READY, frame=20, root=(0, 0, -0.125), lean=16),
        dict(READY, frame=40),
    ]),
    # 포핸드 탑스핀: 몸 돌리기 → 라켓 뒤로 → 헤드 떨어뜨림 → 임팩트(오른쪽 앞) → 왼쪽 어깨 옆으로
    "Forehand": (48, [
        dict(READY, frame=0),
        # 몸 돌리기: 팔꿈치를 굽혀 라켓 손은 오른쪽 어깨 높이, 헤드는 위로. 왼팔은 공 쪽으로 어깨 높이까지 뻗는다
        dict(READY, frame=8, root=(0.03, 0, -0.12), hips=-35, torso=-75, lean=12, look=(-25, 0),
             racket=((0.30, -0.05, 1.30), (0.1, -0.35, 1), (0, 0, -1)),
             left=((0.10, 0.55, 1.30), (0.3, 0.95, 0), (0, 0, 1)), lelbow=(-0.3, 0, -0.6)),
        # 테이크백 끝: 손은 어깨 높이 뒤, 무릎을 더 굽힌다
        dict(frame=16, root=(0.05, 0, -0.15), hips=-45, torso=-95, look=(-35, 0),
             racket=((0.45, -0.30, 1.30), (0.2, -0.55, 0.8), (0, 0, -1)),
             left=((0.15, 0.50, 1.28), (0.5, 0.85, 0), (0, 0, 1))),
        # 드롭: 라켓 헤드가 엉덩이 아래 뒤로 떨어지고 무릎이 가장 깊게 굽는다
        dict(frame=20, root=(0.04, 0, -0.17), hips=-35, torso=-70, look=(-20, 5),
             racket=((0.50, -0.15, 0.85), (0.15, -0.8, -0.55), (0, 0.3, -1)), elbow=(0.2, -0.5, -0.6),
             left=((-0.05, 0.45, 1.20), (0.3, 0.95, 0), (0, 0, 1))),
        # 임팩트: 몸 오른쪽 앞, 왼팔은 옆으로 벌려 균형
        dict(frame=24, root=(0.0, 0.02, -0.12), hips=-5, torso=0, lean=14, look=(0, 12),
             racket=((0.45, 0.50, 0.95), (1, 0.1, 0.15), (0, 1, 0)),
             left=((-0.35, 0.30, 1.15), (-0.2, 0.95, 0), (0, 0, 1)), elbow=(0.2, -0.4, -0.7), lelbow=(-0.3, -0.3, -0.6)),
        # 팔로스루: 머리 위가 아니라 가슴~어깨 높이로 왼쪽 어깨를 향해 감아 간다
        dict(frame=30, root=(-0.02, 0.02, -0.11), hips=20, torso=55, look=(5, 5),
             racket=((-0.10, 0.45, 1.15), (-0.90, -0.30, 0.25), (0, 1, 0)), elbow=(0.3, 0.4, -0.6),
             left=((-0.35, 0.15, 1.10), (0.2, 0.9, 0), (0, 0, 1))),
        # 마무리: 손은 왼쪽 어깨 앞, 라켓은 왼쪽 어깨를 감싸 뒤를 향한다
        dict(frame=36, root=(-0.03, 0, -0.10), hips=30, torso=85, lean=12, look=(15, 0),
             racket=((-0.30, 0.22, 1.28), (-0.30, -0.95, 0.05), (0, 0, -1)), elbow=(0.1, 0.8, -0.2),
             left=((-0.38, 0.05, 1.05), (0.2, 0.9, -0.2), (0, 0, 1)), lelbow=(-0.4, -0.3, -0.5)),
        dict(READY, frame=48),
    ]),
    # 양손 백핸드: 오른발을 앞으로 내딛고 왼쪽으로 몸 돌리기 → 임팩트(왼쪽 앞) → 오른쪽 어깨 옆으로, 마무리까지 두 손
    "Backhand": (48, [
        dict(READY, frame=0),
        dict(READY, frame=8, root=(-0.03, 0.03, -0.12), hips=35, torso=80, lean=12, look=(25, 0), left="grip2",
             racket=((-0.35, 0.05, 1.15), (-0.15, -0.25, 1), (0, 0, 1)), elbow=(0.1, 0.1, -0.7), lelbow=(-0.4, -0.3, -0.6),
             feet=dict(R=(0.16, 0.16, 10, 0.08))),
        # 테이크백 끝: 두 손은 왼쪽 어깨 높이 뒤, 헤드는 위로
        dict(frame=16, root=(-0.05, 0.08, -0.15), hips=45, torso=100, look=(35, 0),
             racket=((-0.50, -0.25, 1.20), (-0.3, -0.6, 0.75), (0, 0, 1)), feet=dict(R=(0.02, 0.32, 35, 0))),
        # 드롭: 라켓 헤드가 엉덩이 아래 뒤로, 무릎이 가장 깊게
        dict(frame=20, root=(-0.04, 0.10, -0.17), hips=35, torso=70, look=(20, 5),
             racket=((-0.48, -0.08, 0.80), (-0.25, -0.85, -0.5), (0, -0.3, 1))),
        dict(frame=24, root=(0.0, 0.12, -0.12), hips=0, torso=15, lean=14, look=(0, 12),
             racket=((-0.30, 0.48, 0.95), (-1, 0.1, 0.15), (0, -1, 0)), elbow=(0.2, 0.2, -0.7)),
        # 팔로스루: 머리 위가 아니라 가슴~어깨 높이로 오른쪽 어깨를 향해 감아 간다
        dict(frame=30, root=(0.02, 0.12, -0.11), hips=-20, torso=-50, look=(-5, 5),
             racket=((0.10, 0.45, 1.15), (0.90, -0.30, 0.25), (0, -1, 0)), elbow=(0.1, 0.4, -0.7)),
        # 마무리: 두 손은 오른쪽 어깨 앞, 라켓은 오른쪽 어깨를 감싸 뒤를 향한다
        dict(frame=36, root=(0.03, 0.10, -0.10), hips=-30, torso=-80, lean=12, look=(-15, 0),
             racket=((0.30, 0.22, 1.28), (0.30, -0.95, 0.05), (0, 0, 1)), elbow=(0.0, 0.6, -0.6), lelbow=(-0.3, 0.5, -0.5)),
        dict(frame=40, feet=dict(R=(0.02, 0.32, 35, 0))),
        dict(frame=44, feet=dict(R=(0.16, 0.16, 0, 0.06))),
        dict(READY, frame=48),
    ]),
    # 서브: 옆으로 서서 팔 내리기 → 트로피 자세(왼팔 토스) → 라켓 등 뒤로 떨어뜨림 → 머리 위 임팩트 → 왼쪽 아래로
    "Serve": (72, [
        dict(READY, frame=0, root=(0, 0, -0.06), hips=-75, torso=-85, lean=8, look=(0, 5),
             racket=((0.30, 0.15, 1.05), (0.2, 0.9, 0.3), (1, 0, 0)), elbow=(0.2, -0.4, -0.6), lelbow=(0.3, 0.2, -0.6),
             feet=dict(L=(0.0, 0.25, -45, 0), R=(0.15, -0.25, -90, 0))),
        dict(frame=12, root=(0.0, -0.05, -0.08), lean=10, look=(0, 10),
             racket=((0.20, -0.05, 0.85), (0.0, 0.5, -0.85), (1, 0, 0)),
             left=((0.10, 0.30, 0.95), (0.3, 0.4, -0.85), (1, 0, 0))),
        dict(frame=24, root=(-0.05, -0.05, -0.18), hips=-70, torso=-100, lean=-8, side=20, look=(-20, -45),
             racket=((0.15, -0.35, 1.55), (0.05, -0.1, 1), (1, 0, 0)), elbow=(0.1, -0.5, -0.1),
             left=((0.10, 0.30, 2.05), (0, 0.2, 1), (-1, 0, 0)), lelbow=(0.3, 0.3, -0.3)),
        dict(frame=32, root=(0.0, 0.0, -0.05), hips=-40, torso=-70, lean=-10, side=10, look=(-10, -50),
             racket=((0.10, -0.25, 1.65), (-0.6, -0.2, -0.75), (0, -1, 0)), elbow=(0.2, 0.0, 0.6),
             left=((0.05, 0.30, 1.70), (0, 0.3, 1), (-1, 0, 0))),
        dict(frame=40, root=(0.0, 0.08, 0.04), hips=-10, torso=0, lean=15, side=-10, look=(0, -35),
             racket=((0.15, 0.30, 2.05), (0.05, 0.25, 1), (0, 1, 0)), elbow=(0.3, 0.2, 0.5),
             left=((-0.05, 0.35, 1.25), (0.5, 0.5, -0.3), (0, 1, 0)), lelbow=(-0.3, -0.2, -0.6),
             feet=dict(L=(0.0, 0.25, -45, 0.03), R=(0.15, -0.25, -90, 0.04))),
        dict(frame=44, root=(0, 0.12, -0.02), hips=0, torso=20, lean=22, side=-5,
             racket=((0.12, 0.55, 1.70), (0.25, 0.85, -0.45), (0.9, -0.3, 0)),
             feet=dict(R=(0.10, 0.15, -45, 0.12))),
        dict(frame=48, root=(0.0, 0.15, -0.10), hips=10, torso=35, lean=30, side=0, look=(0, 10),
             racket=((-0.30, 0.30, 0.90), (-0.5, -0.3, -0.8), (0, -1, 0)), elbow=(0.3, 0.3, -0.5),
             left=((-0.30, 0.10, 1.05), (0.2, 0.9, -0.2), (0, 0, 1)),
             feet=dict(L=(0.0, 0.25, -45, 0), R=(0.05, 0.40, 0, 0))),
        dict(frame=58, root=(0.0, 0.10, -0.10), hips=0, torso=10, lean=15, left="throat",
             racket=((0.10, 0.38, 1.00), (-0.35, 0.65, 0.55), (-1, 0, 0)), elbow=(0.35, -0.2, -0.6),
             lelbow=(-0.35, -0.2, -0.6)),
        dict(frame=64, feet=dict(L=(-0.15, 0.12, -15, 0.06), R=(0.18, 0.20, -5, 0.06))),
        dict(READY, frame=72),
    ]),
    # 스매시: 오른발 뒤로 빼며 옆으로 → 왼팔로 공 가리키기 → 라켓 떨어뜨림 → 머리 위 임팩트 → 왼쪽 아래로
    "Smash": (44, [
        dict(READY, frame=0),
        dict(frame=4, feet=dict(R=(0.28, -0.12, -45, 0.08))),
        dict(frame=8, root=(0.0, -0.10, -0.10), hips=-60, torso=-85, lean=0, side=15, look=(-20, -40),
             racket=((0.20, -0.30, 1.55), (0.05, -0.1, 1), (1, 0, 0)), elbow=(0.1, -0.5, -0.1),
             left=((-0.05, 0.30, 2.00), (0, 0.3, 1), (-1, 0, 0)), lelbow=(0.3, 0.3, -0.3),
             feet=dict(R=(0.25, -0.30, -80, 0))),
        dict(frame=15, root=(0.0, -0.05, -0.06), hips=-40, torso=-65, side=10, look=(-10, -45),
             racket=((0.10, -0.25, 1.65), (-0.6, -0.2, -0.75), (0, -1, 0)), elbow=(0.2, 0.0, 0.6)),
        dict(frame=22, root=(0.0, 0.05, 0.0), hips=-10, torso=0, lean=15, side=-10, look=(0, -30),
             racket=((0.15, 0.30, 2.05), (0.05, 0.3, 1), (0, 1, 0)), elbow=(0.3, 0.2, 0.5),
             left=((-0.05, 0.35, 1.25), (0.5, 0.5, -0.3), (0, 1, 0)), lelbow=(-0.3, -0.2, -0.6)),
        dict(frame=26, root=(0, 0.08, -0.04), hips=0, torso=20, lean=20, side=-5,
             racket=((0.12, 0.55, 1.70), (0.25, 0.85, -0.45), (0.9, -0.3, 0)),
             feet=dict(R=(0.24, -0.10, -50, 0.08))),
        dict(frame=30, root=(0.0, 0.08, -0.10), hips=15, torso=35, lean=25, side=0, look=(0, 10),
             racket=((-0.30, 0.30, 0.90), (-0.5, -0.3, -0.8), (0, -1, 0)), elbow=(0.3, 0.3, -0.5),
             left=((-0.30, 0.10, 1.05), (0.2, 0.9, -0.2), (0, 0, 1)), feet=dict(R=(0.22, 0.08, -20, 0))),
        dict(frame=38, feet=dict(R=(0.28, 0.03, -12, 0.04))),
        dict(READY, frame=44),
    ]),
    # 포핸드 발리: 짧게 몸 돌리기, 라켓 헤드를 세운 채 앞으로 밀기. 왼발 한 걸음
    "VolleyForehand": (30, [
        dict(READY, frame=0),
        dict(READY, frame=7, root=(0.03, 0.05, -0.12), hips=-15, torso=-40, look=(-10, 5),
             racket=((0.42, 0.25, 1.25), (0.15, 0.1, 1), (0, 1, 0.3)), elbow=(0.4, -0.2, -0.5),
             left=((-0.05, 0.45, 1.25), (0.5, 0.8, 0), (0, 0, 1)), lelbow=(-0.3, 0, -0.6),
             feet=dict(L=(-0.25, 0.15, 5, 0.06))),
        dict(frame=14, root=(0.02, 0.12, -0.13), hips=-5, torso=-15, lean=18, look=(0, 10),
             racket=((0.38, 0.60, 1.15), (0.55, 0.35, 0.75), (0, 1, 0.25)), elbow=(0.4, -0.1, -0.6),
             left=((-0.20, 0.35, 1.15), (0.3, 0.9, 0), (0, 0, 1)), feet=dict(L=(-0.20, 0.30, 0, 0))),
        dict(frame=20, hips=0, torso=-5, racket=((0.34, 0.68, 1.12), (0.55, 0.35, 0.75), (0, 1, 0.3))),
        dict(frame=25, feet=dict(L=(-0.25, 0.15, 5, 0.05))),
        dict(READY, frame=30),
    ]),
    # 백핸드 발리: 왼손으로 라켓 목을 받치고 몸 돌리기 → 한 손으로 밀며 왼팔은 뒤로. 오른발 한 걸음
    "VolleyBackhand": (30, [
        dict(READY, frame=0),
        dict(READY, frame=7, root=(-0.03, 0.05, -0.12), hips=20, torso=50, look=(15, 5),
             racket=((-0.30, 0.25, 1.25), (-0.15, 0.1, 1), (0, -1, -0.3)), elbow=(0.1, 0.1, -0.7),
             lelbow=(-0.4, -0.3, -0.6), feet=dict(R=(0.25, 0.15, -5, 0.06))),
        dict(frame=14, root=(-0.02, 0.12, -0.13), hips=10, torso=20, lean=18, look=(0, 10),
             racket=((-0.30, 0.60, 1.15), (-0.55, 0.35, 0.75), (0, -1, -0.25)), elbow=(0.1, 0.3, -0.6),
             left=((-0.45, -0.05, 1.10), (-0.6, -0.6, -0.3), (0, 0, 1)), lelbow=(-0.5, -0.4, -0.3),
             feet=dict(R=(0.20, 0.30, 0, 0))),
        dict(frame=20, hips=5, torso=10, racket=((-0.34, 0.68, 1.12), (-0.55, 0.35, 0.75), (0, -1, -0.3))),
        dict(frame=25, feet=dict(R=(0.25, 0.15, -5, 0.05))),
        dict(READY, frame=30),
    ]),
}
CONTACT = {"Forehand": 24, "Backhand": 24, "Serve": 40, "Smash": 22, "VolleyForehand": 14, "VolleyBackhand": 14}


# 슬라이스·로브·서브 구질 클립 (참고 영상: Docs/AnimationReferences.md)
# 슬라이스: 높은 준비 자세에서 앞·아래로 내려치고 타격 쪽으로 뻗는다. 탑스핀처럼 반대쪽 어깨로 감지 않는다
CLIPS["SliceForehand"] = (48, [
    dict(READY, frame=0),
    dict(READY, frame=8, hips=-25, torso=-55, root=(0.03, 0, -0.12),
         racket=((0.35, 0.05, 1.32), (0.35, -0.15, 0.9), (0, 1, 0.3)),
         left=((-0.15, 0.40, 1.2), (0, 1, 0), (0, 0, 1))),
    dict(frame=16, hips=-35, torso=-70,
         racket=((0.43, -0.08, 1.35), (0.55, -0.3, 0.8), (0, 1, 0.35))),
    dict(frame=24, hips=-10, torso=-20, root=(0, 0.08, -0.13),
         racket=((0.40, 0.48, 1.02), (0.95, 0.15, 0.25), (0, 1, 0.3))),
    dict(frame=32, hips=0, torso=5,
         racket=((0.30, 0.62, 0.95), (0.8, 0.5, 0.25), (0, 1, 0.3))),
    dict(frame=38, racket=((0.15, 0.55, 1.00), (0.6, 0.6, 0.4), (0, 1, 0.3))),
    dict(READY, frame=48),
])
CLIPS["SliceBackhand"] = (48, [
    dict(READY, frame=0),
    dict(READY, frame=8, hips=30, torso=65, root=(-0.03, 0, -0.12),
         racket=((-0.25, 0.18, 1.35), (-0.3, -0.1, 0.95), (0, -1, -0.3))),
    dict(frame=16, hips=40, torso=85,
         racket=((-0.35, 0.0, 1.38), (-0.5, -0.3, 0.8), (0, -1, -0.3)),
         feet=dict(R=(0.12, 0.20, 20, 0.06))),
    dict(frame=24, hips=20, torso=45, root=(0, 0.10, -0.13),
         racket=((-0.35, 0.48, 1.02), (-0.95, 0.15, 0.25), (0, -1, -0.3)),
         left=((-0.40, -0.25, 1.25), (-0.5, -0.8, 0), (0, 0, 1)),
         feet=dict(R=(0.05, 0.30, 25, 0))),
    dict(frame=32, hips=15, torso=35,
         racket=((-0.38, 0.62, 0.95), (-0.8, 0.5, 0.25), (0, -1, -0.3)),
         left=((-0.45, -0.30, 1.25), (-0.5, -0.8, 0), (0, 0, 1))),
    dict(frame=38, racket=((-0.28, 0.55, 1.05), (-0.6, 0.6, 0.4), (0, -1, -0.3))),
    dict(frame=42, feet=dict(R=(0.16, 0.15, 10, 0.06))),
    dict(READY, frame=48),
])

# 로브: 스트로크 클립을 복사해 드롭을 더 낮추고 위로 길게 들어 올린다. 마무리는 같은 어깨 쪽
for side, sign in (("Forehand", 1), ("Backhand", -1)):
    length, keys = deepcopy(CLIPS[side])
    for k in keys:
        if k["frame"] == 20:
            k["racket"] = ((sign * 0.40, 0.10, 0.68), (sign * 0.75, 0.1, -0.4), (0, sign, sign * 0.25))
        elif k["frame"] == 24:
            k["racket"] = ((sign * 0.35, 0.48, 0.86), (sign * 0.8, 0.25, 0.15), (0, sign, sign * 0.4))
        elif k["frame"] == 30:
            k["racket"] = ((sign * 0.05, 0.52, 1.24), (-sign * 0.8, 0.15, 0.35), (0, sign, 0))
    CLIPS["Lob" + side] = (length, keys)
    CONTACT["Lob" + side] = 24
    CONTACT["Slice" + side] = 24

# 서브 구질은 마우스를 놓을 때 정해지므로 토스는 같고, 임팩트·팔로스루 경로만 다르다 (슬라이스: 공을 가로질러, 킥: 위·옆으로)
for name, contact_racket, extension in (
    ("ServeSlice", ((0.22, 0.30, 2.02), (0.15, 0.25, 1), (0.3, 1, 0)),
     ((-0.02, 0.52, 1.72), (-0.5, 0.7, -0.4), (0.8, 0.5, 0))),
    ("ServeKick", ((0.02, 0.20, 2.05), (0.25, 0.15, 1), (0, 1, 0)),
     ((0.32, 0.42, 1.88), (0.8, 0.4, 0.2), (0, 1, -0.2))),
):
    length, keys = deepcopy(CLIPS["Serve"])
    for k in keys:
        if k["frame"] == 40: k["racket"] = contact_racket
        elif k["frame"] == 44: k["racket"] = extension
    CLIPS[name] = (length, keys)
    CONTACT[name] = 40


# ---------- 리그 ----------

class Rig:
    FK = ["Root", "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "LeftShoulder", "RightShoulder"]

    def __init__(self, arm):
        self.arm = arm
        self.pb = arm.pose.bones
        self.rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
        self.parent = {b.name: b.parent.name if b.parent else None for b in arm.data.bones}
        self.order = [b.name for b in arm.data.bones]  # 부모가 먼저 온다
        for pb in self.pb:
            pb.rotation_mode = "QUATERNION"
        self.empties = {n: self._empty(n) for n in
                        ("RHand", "LHand", "RElbow", "LElbow", "LFoot", "RFoot", "LKnee", "RKnee")}
        self.constraints = []
        self._ik("RightForeArm", "RHand", "RElbow")
        self._ik("LeftForeArm", "LHand", "LElbow")
        self._ik("RightLeg", "RFoot", "RKnee")
        self._ik("LeftLeg", "LFoot", "LKnee")
        for bone, target in (("RightHand", "RHand"), ("LeftHand", "LHand"), ("RightFoot", "RFoot"), ("LeftFoot", "LFoot")):
            c = self.pb[bone].constraints.new("COPY_ROTATION")
            c.target = self.empties[target]
            self.constraints.append((bone, c))
        self.prev_q = {}

    def _empty(self, name):
        o = bpy.data.objects.new("TA_" + name, None)
        bpy.context.scene.collection.objects.link(o)
        o.rotation_mode = "QUATERNION"
        return o

    def _ik(self, bone, target, pole):
        c = self.pb[bone].constraints.new("IK")
        c.target = self.empties[target]
        c.pole_target = self.empties[pole]
        c.chain_count = 2
        self.constraints.append((bone, c))

    def head_of(self, pose, bone):
        """FK 자세에서 뼈 시작점 (팔·다리 뿌리 위치 계산용)."""
        parent = self.parent[bone]
        return (pose[parent] @ self.rest[parent].inverted() @ self.rest[bone]).translation

    # 몸통 자세 → FK 뼈의 아마추어 공간 행렬
    def body(self, k):
        yaw_h, yaw_t, lean, side = k["hips"], k["torso"], k["lean"], k["side"]
        look_yaw, look_pitch = k["look"]

        def delta(yaw, lean_deg, side_deg):
            return rot_z(yaw) @ rot(lean_deg, Vector((1, 0, 0))) @ rot(side_deg, Vector((0, -1, 0)))

        deltas = {
            "Root": Matrix.Identity(4),
            "Hips": delta(yaw_h, lean * 0.3, side * 0.2),
            "Spine": delta(yaw_h + (yaw_t - yaw_h) / 3, lean * 0.53, side * 0.47),
            "Spine1": delta(yaw_h + (yaw_t - yaw_h) * 2 / 3, lean * 0.77, side * 0.73),
            "Spine2": delta(yaw_t, lean, side),
            "Neck": delta((yaw_t + look_yaw) / 2, (lean + look_pitch) / 2, side / 2),
            "Head": delta(look_yaw, look_pitch, 0),
        }
        deltas["LeftShoulder"] = deltas["RightShoulder"] = deltas["Spine2"]
        pose = {}
        for name in self.FK:
            r = self.rest[name]
            if name == "Root":
                head = r.translation + P(*k["root"])
            else:
                head = self.head_of(pose, name)
            m = deltas[name] @ r.to_3x3().to_4x4()
            m.translation = head
            pose[name] = m
        return pose

    def set_empty(self, name, matrix, frame):
        o = self.empties[name]
        loc, q, _ = matrix.decompose()
        prev = self.prev_q.get(name)
        if prev is not None and prev.dot(q) < 0:
            q.negate()
        self.prev_q[name] = q
        o.location = loc
        o.rotation_quaternion = q
        o.keyframe_insert("location", frame=frame)
        o.keyframe_insert("rotation_quaternion", frame=frame)

    def key(self, k, feet_only=False):
        """feet_only: 발만 옮기는 중간 키. 몸통·팔은 키를 넣지 않아 앞뒤 자세 사이로 그대로 이어진다."""
        pose = self.body(k)
        if not feet_only:
            self.key_upper(k, pose)
        self.key_feet(k, pose)

    def key_upper(self, k, pose):
        f = k["frame"]
        for name in self.FK:
            parent = self.parent[name]
            ref = self.rest[name] if parent is None else pose[parent] @ self.rest[parent].inverted() @ self.rest[name]
            basis = ref.inverted() @ pose[name]
            loc, q, _ = basis.decompose()
            pb = self.pb[name]
            prev = self.prev_q.get(name)
            if prev is not None and prev.dot(q) < 0:
                q.negate()
            self.prev_q[name] = q
            pb.location = loc
            pb.rotation_quaternion = q
            pb.keyframe_insert("location", frame=f)
            pb.keyframe_insert("rotation_quaternion", frame=f)

        # 라켓 → 오른손
        (gr, gf, gu), sd, nd = k["racket"]
        s, n = racket_axes(D(*sd), D(*nd))
        grip = P(gr, gf, gu)
        shoulder_r = self.head_of(pose, "RightArm")
        rh = hand_matrix(grip, s, n, True)
        rh.translation = self.reach(shoulder_r, rh.translation)
        self.set_empty("RHand", rh, f)
        self.set_empty("RElbow", Matrix.Translation(elbow_pole(shoulder_r, rh, P(*k["elbow"]))), f)

        # 왼손
        shoulder_l = self.head_of(pose, "LeftArm")
        left = k["left"]
        butt = grip - s * RIGHT_GRIP
        if left == "throat":
            lh = hand_matrix(butt + s * THROAT, s, -n, False)
        elif left == "grip2":
            lh = hand_matrix(butt + s * LEFT_GRIP, s, -n, False)
        else:
            pos, fingers, back = left
            y = D(*fingers)
            z = (D(*back) - y * D(*back).dot(y)).normalized()
            lh = Matrix((y.cross(z), y, z)).transposed().to_4x4()
            lh.translation = P(*pos)
        lh.translation = self.reach(shoulder_l, lh.translation)
        self.set_empty("LHand", lh, f)
        self.set_empty("LElbow", Matrix.Translation(elbow_pole(shoulder_l, lh, P(*k["lelbow"]))), f)

    def key_feet(self, k, pose):
        f = k["frame"]
        for side in ("L", "R"):
            r, fw, yaw, lift = k["feet"][side]
            bone = "LeftFoot" if side == "L" else "RightFoot"
            foot = rot_z(yaw) @ self.rest[bone].to_3x3().to_4x4()
            foot.translation = P(r, fw, ANKLE + lift)
            self.set_empty(side + "Foot", foot, f)
            hip = self.head_of(pose, "LeftUpLeg" if side == "L" else "RightUpLeg")
            toe = rot_z(yaw).to_3x3() @ Vector((0, -1, 0))
            self.set_empty(side + "Knee", Matrix.Translation((hip + foot.translation) / 2 + toe * 0.8), f)

    @staticmethod
    def reach(shoulder, target, limit=0.515):
        """팔이 다 펴지지 않게 어깨에서 limit 안으로 당긴다 (위팔 0.268 + 아래팔 0.26)."""
        d = target - shoulder
        return shoulder + d * min(1.0, limit / d.length) if d.length > 1e-6 else target

    def calibrate_poles(self):
        """IK 무릎·팔꿈치가 극 목표 쪽을 보도록 pole_angle을 고른다."""
        scratch = bpy.data.actions.new("TA_tmp_calibrate")
        self.arm.animation_data.action = scratch
        self.key(dict(READY, frame=0))
        bpy.context.scene.frame_set(0)
        for bone, c in self.constraints:
            if c.type != "IK":
                continue
            upper = self.pb[bone].parent
            best = None
            for deg in range(-180, 180, 15):
                c.pole_angle = math.radians(deg)
                bpy.context.view_layer.update()
                a, e, w = upper.head, self.pb[bone].head, self.pb[bone].tail
                axis = (w - a).normalized()
                pole = c.pole_target.matrix_world.translation
                ve = (e - a) - axis * (e - a).dot(axis)
                vp = (pole - a) - axis * (pole - a).dot(axis)
                score = ve.normalized().dot(vp.normalized()) if ve.length > 1e-5 and vp.length > 1e-5 else -2
                if best is None or score > best[0]:
                    best = (score, deg)
            c.pole_angle = math.radians(best[1])
            print(f"[tennis] pole {bone}: {best[1]} deg (일치 {best[0]:.3f})")
        self.arm.animation_data.action = None
        bpy.data.actions.remove(scratch)
        for o in self.empties.values():
            o.animation_data_clear()

    def build(self, name, length, keys):
        """핵심 자세를 키로 넣고, 매 프레임 결과(IK 포함)를 새 동작으로 굽는다."""
        scratch = bpy.data.actions.new("TA_tmp_" + name)
        ad = self.arm.animation_data
        ad.action = scratch
        for o in self.empties.values():
            o.animation_data_clear()
        self.prev_q = {}
        prev = {}
        grips = []  # (프레임, 왼손이 라켓을 쥐는 정도, 손잡이 끝에서 왼손까지)
        for k in keys:
            full = dict(prev)
            full.update(k)
            if "feet" in k:
                feet = dict(prev.get("feet", READY_FEET))
                feet.update(k["feet"])
                full["feet"] = feet
            feet_only = set(k) <= {"frame", "feet"}
            self.key(full, feet_only=feet_only)
            if not feet_only:
                left = full["left"]
                along = THROAT if left == "throat" else LEFT_GRIP if left == "grip2" else (grips[-1][2] if grips else LEFT_GRIP)
                grips.append((full["frame"], 1.0 if left in ("throat", "grip2") else 0.0, along))
            prev = full

        def grip_at(f):
            for (f0, w0, a0), (f1, w1, a1) in zip(grips, grips[1:]):
                if f0 <= f <= f1:
                    t = (f - f0) / max(1, f1 - f0)
                    return w0 + (w1 - w0) * t, a0 + (a1 - a0) * t
            return grips[-1][1], grips[-1][2]

        frames = []
        for f in range(0, length + 1):
            bpy.context.scene.frame_set(f)
            mats = {pb.name: pb.matrix.copy() for pb in self.pb}
            fix_arm_roll(mats, "Right")
            relax_wrist(mats, "Right")
            weight, along = grip_at(f)
            if weight > 0:
                hold_racket_left(mats, along, weight)
            fix_arm_roll(mats, "Left")
            relax_wrist(mats, "Left", WRIST_MAX_BEND + 20.0 * weight)  # 라켓을 쥔 왼손은 손목을 더 젖힌다 (양손 백핸드)
            frames.append(mats)

        baked = bpy.data.actions.new(name)
        ad.action = baked
        last = {}
        for f, mats in enumerate(frames):
            for bone in self.order:
                parent = self.parent[bone]
                ref = self.rest[bone] if parent is None else mats[parent] @ self.rest[parent].inverted() @ self.rest[bone]
                loc, q, _ = (ref.inverted() @ mats[bone]).decompose()
                if bone in last and last[bone].dot(q) < 0:
                    q.negate()
                last[bone] = q
                pb = self.pb[bone]
                pb.location = loc
                pb.rotation_quaternion = q
                pb.scale = (1, 1, 1)
                pb.keyframe_insert("location", frame=f)
                pb.keyframe_insert("rotation_quaternion", frame=f)
        bpy.data.actions.remove(scratch)
        return baked

    def cleanup(self):
        for bone, c in self.constraints:
            self.pb[bone].constraints.remove(c)
        for o in self.empties.values():
            bpy.data.objects.remove(o, do_unlink=True)


def main():
    out_blend = sys.argv[sys.argv.index("--") + 1]
    scene = bpy.context.scene
    scene.render.fps = FPS
    arm = next(o for o in scene.objects if o.type == "ARMATURE")
    ad = arm.animation_data or arm.animation_data_create()
    muted = {t.name: t.mute for t in ad.nla_tracks}
    for t in ad.nla_tracks:
        t.mute = True

    rig = Rig(arm)
    rig.calibrate_poles()
    baked = {name: rig.build(name, length, keys) for name, (length, keys) in CLIPS.items()}
    rig.cleanup()

    ad.action = None
    for pb in arm.pose.bones:
        pb.location, pb.rotation_quaternion, pb.scale = (0, 0, 0), (1, 0, 0, 0), (1, 1, 1)
    for t in list(ad.nla_tracks):
        if t.name in baked:
            ad.nla_tracks.remove(t)
    for name, act in baked.items():
        act.use_fake_user = True
        track = ad.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 0, act)
        strip.action_slot = act.slots[0]
        print(f"[tennis] {name}: 0~{int(act.frame_range[1])}프레임, 임팩트 {CONTACT.get(name, '-')}")
    for t in ad.nla_tracks:
        t.mute = muted.get(t.name, False)
    add_racket_markers(arm)
    add_grip_shape_keys(arm, bpy.data.objects["TennisPlayer_Body"])
    bpy.ops.wm.save_as_mainfile(filepath=out_blend, compress=True, copy=True)
    print(f"[tennis] 저장: {out_blend}")


if __name__ == "__main__":
    main()
