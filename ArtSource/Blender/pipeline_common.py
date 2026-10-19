"""Blender 정리 파이프라인 공용 함수: 불러오기, 메시 검수, 애니메이션 반복, 텍스처 (Blender 5.2)."""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector

import game_tone

FPS = 30                 # VARCO 애니메이션은 30fps
WELD = 1e-5              # 같은 자리에 겹친 점 (UV 이음새로 쪼개진 것)
HOLE_SIDES = 12          # 이 변 수 이하의 닫힌 구멍만 메운다
LOOP_LIMIT_DEG = 3.0     # 처음·끝 자세 차이가 이보다 크면 반복할 때 튄다
LOOP_LIMIT_M = 0.02
MIN_CYCLE_FRAMES = 15    # 0.5초보다 짧은 구간은 한 걸음 주기도 안 된다
LOOP_BLEND_FRAMES = 6    # 남은 차이는 마지막 몇 프레임을 첫 자세로 이어 붙인다


def r(v, n=4):
    return [round(x, n) for x in v] if hasattr(v, "__len__") else round(v, n)


# ---------- 씬·불러오기 ----------

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS


def import_glb(path):
    """GLB를 불러와 새로 생긴 오브젝트 목록을 돌려준다. 불러오기가 만드는 뼈 표시용 구(Icosphere)는 지운다."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        if o.type == "ARMATURE":
            for pb in o.pose.bones:
                pb.custom_shape = None
    shapes = [o for o in new if o.name.startswith("Icosphere")]
    remove_objects(shapes)
    return [o for o in new if o not in shapes]


def remove_objects(objs):
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)


def select_only(objs, active):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def apply_modifier(obj, name, first=True):
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
        if first:
            bpy.ops.object.modifier_move_to_index(modifier=name, index=0)
        bpy.ops.object.modifier_apply(modifier=name)


def verts(obj):
    """월드 좌표 점 배열 (N, 3)."""
    n = len(obj.data.vertices)
    co = np.empty(n * 3, np.float64)
    obj.data.vertices.foreach_get("co", co)
    co = co.reshape(n, 3)
    m = np.array(obj.matrix_world)
    return co @ m[:3, :3].T + m[:3, 3]


def bounds(points):
    return points.min(0), points.max(0)


# ---------- 메시 검수 ----------

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


def islands(bm):
    """서로 이어진 면 묶음 목록."""
    seen, out = set(), []
    for f in bm.faces:
        if f in seen:
            continue
        stack, faces = [f], []
        seen.add(f)
        while stack:
            x = stack.pop()
            faces.append(x)
            for e in x.edges:
                for g in e.link_faces:
                    if g not in seen:
                        seen.add(g)
                        stack.append(g)
        out.append(faces)
    return out


def orient_normals(bm):
    """노멀을 이웃끼리 맞춘 뒤(recalc), 묶음마다 바깥을 보는지 검사해 안쪽이면 묶음 전체를 뒤집는다.
    검사: 면 노멀 · (면 중심 − 묶음 중심)의 면적 가중합 > 0 이면 바깥.
    recalc만으로는 얇은 옷처럼 열린 껍질에서 안팎을 반대로 고를 때가 있다 (상의가 통째로 뒤집혔던 원인)."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    reversed_faces = 0
    for faces in islands(bm):
        centers = [f.calc_center_median() for f in faces]
        c = sum(centers, Vector()) / len(centers)
        s = sum(f.normal.dot(p - c) * f.calc_area() for f, p in zip(faces, centers))
        if s < 0:
            bmesh.ops.reverse_faces(bm, faces=faces)
            reversed_faces += len(faces)
    return reversed_faces


def count_flipped(bm):
    """방향이 틀린 면 수 (노멀을 바르게 맞추면 뒤집히는 면)."""
    copy = bm.copy()
    before = [f.normal.copy() for f in copy.faces]
    orient_normals(copy)
    copy.normal_update()
    n = sum(1 for f, b in zip(copy.faces, before) if f.normal.dot(b) < 0)
    copy.free()
    return n


def bm_stats(bm):
    return {
        "verts": len(bm.verts),
        "tris": sum(len(f.verts) - 2 for f in bm.faces),
        "parts": count_parts(bm),
        "boundary_edges": sum(1 for e in bm.edges if e.is_boundary),
        "nonmanifold_edges": sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary),
        "flipped_faces": count_flipped(bm),
    }


def mesh_stats(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    s = bm_stats(bm)
    bm.free()
    return s


def boundary_loops(bm):
    """열린 가장자리 묶음: (변 목록, 닫힌 고리인지, 둘레)."""
    seen, out = set(), []
    for e in bm.edges:
        if not e.is_boundary or e in seen:
            continue
        stack, edges = [e], []
        seen.add(e)
        while stack:
            x = stack.pop()
            edges.append(x)
            for v in x.verts:
                for y in v.link_edges:
                    if y.is_boundary and y not in seen:
                        seen.add(y)
                        stack.append(y)
        verts = {v for x in edges for v in x.verts}
        closed = all(sum(1 for y in v.link_edges if y.is_boundary) == 2 for v in verts)
        out.append((edges, closed, sum(x.calc_length() for x in edges)))
    return out


def close_tears(bm, max_size):
    """찢어진 곳 오므리기: 열린 변·꼬인 변(면 3장 이상)을 이어진 덩어리로 묶고, 크기가 max_size 이하인 덩어리는
    점들을 한 점으로 모아 틈을 닫는다 (Point Merge). 면을 잘라 내지 않아 주변 모양·UV가 그대로 남는다.
    AI 옷 메시는 바깥 면과 안쪽 면이 군데군데 엉겨 붙어 작은 틈(안이 비치는 구멍)이 생긴다."""
    def bad(e):
        return e.is_boundary or len(e.link_faces) > 2
    seen, clusters = set(), []
    for e in bm.edges:
        if e in seen or not bad(e):
            continue
        stack, verts = [e], set()
        seen.add(e)
        while stack:
            x = stack.pop()
            verts.update(x.verts)
            for v in x.verts:
                for y in v.link_edges:
                    if y not in seen and bad(y):
                        seen.add(y)
                        stack.append(y)
        clusters.append(list(verts))
    closed = skipped = 0
    for verts in clusters:
        lo = Vector([min(v.co[i] for v in verts) for i in range(3)])
        hi = Vector([max(v.co[i] for v in verts) for i in range(3)])
        if (hi - lo).length > max_size:
            skipped += 1  # 원래 열린 곳(소매·허리 등)일 수 있다
            continue
        center = sum((v.co for v in verts), Vector()) / len(verts)
        bmesh.ops.pointmerge(bm, verts=verts, merge_co=center)
        closed += 1
    bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=bm.edges)
    bm.verts.index_update()
    seen, dup = set(), []
    for f in bm.faces:
        key = frozenset(v.index for v in f.verts)
        if key in seen:
            dup.append(f)
        else:
            seen.add(key)
    bmesh.ops.delete(bm, geom=dup, context="FACES_ONLY")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    left = sum(1 for e in bm.edges if bad(e))
    return {"tears_closed": closed, "large_skipped": skipped, "bad_edges_left": left}


def find_dents(bm, zone, angle_deg=140.0, radius=0.05):
    """주머니처럼 안으로 파인 홈 찾기: 급하게 꺾인 변(angle_deg 이상)을 radius 안끼리 묶고,
    묶음 중심이 zone(좌표) 안인 것만 (깃·밑단·소매 끝처럼 원래 접힌 곳은 zone 밖)."""
    limit = math.radians(angle_deg)
    pts = [(e.verts[0].co + e.verts[1].co) / 2 for e in bm.edges
           if len(e.link_faces) == 2 and e.calc_face_angle(0.0) > limit]
    clusters = []
    for p in pts:
        for c in clusters:
            if (c[0] - p).length < radius:
                c[1].append(p)
                c[0] = sum(c[1], Vector()) / len(c[1])
                break
        else:
            clusters.append([p.copy(), [p]])
    centers = [c for c, members in clusters if len(members) >= 4 and zone(c)]
    rims = sum(1 for c, members in clusters if len(members) >= 4 and not zone(c))
    return centers, rims


def lift_dents(obj, centers, radius=0.06, hull_voxel=0.03):
    """파인 홈을 바깥 껍데기로 밀어 올린다.
    껍데기 = 복사본을 큰 복셀로 리메시해 작은 주머니를 메운 매끈한 겉면. 홈 주변 점들(버텍스 그룹)만
    Shrinkwrap(Outside)으로 껍데기 표면까지 민다. 면을 잘라 내지 않아 새 구멍이 생기지 않는다."""
    if not centers:
        return {"dents": 0, "verts_lifted": 0}
    group = obj.vertex_groups.new(name="Dents")
    co = verts(obj)
    hit = np.zeros(len(co), bool)
    for c in centers:
        hit |= np.linalg.norm(co - np.array(c), axis=1) < radius
    group.add([int(i) for i in np.nonzero(hit)[0]], 1.0, "REPLACE")

    hull = obj.copy()
    hull.data = obj.data.copy()
    hull.modifiers.clear()
    bpy.context.scene.collection.objects.link(hull)
    rm = hull.modifiers.new("Hull", "REMESH")
    rm.mode = "VOXEL"
    rm.voxel_size = hull_voxel
    apply_modifier(hull, "Hull")
    sm = hull.modifiers.new("Soft", "SMOOTH")
    sm.factor = 0.5
    sm.iterations = 5
    apply_modifier(hull, "Soft")

    mod = obj.modifiers.new("LiftDents", "SHRINKWRAP")
    mod.target = hull
    mod.wrap_method = "NEAREST_SURFACEPOINT"
    mod.wrap_mode = "OUTSIDE"
    mod.offset = 0.002
    mod.vertex_group = group.name
    apply_modifier(obj, "LiftDents")
    moved = int((np.linalg.norm(verts(obj) - co, axis=1) > 1e-4).sum())
    remove_objects([hull])
    temp = obj.vertex_groups.get("Dents")  # 모디파이어를 적용하면 앞서 받은 그룹 참조가 무효가 된다
    if temp:
        obj.vertex_groups.remove(temp)
    return {"dents": len(centers), "verts_in_zone": int(hit.sum()), "verts_lifted": moved}


def clean_mesh(obj, zip_dist, min_part_ratio=0.0, tear_size=0.0, dent_zone=None):
    """점 합치기 → 틈 붙이기 → 작은 떨어진 조각 삭제 → 겹친 면·떨어진 점 삭제 → 닫힌 구멍 메우기 → 노멀 통일.
    min_part_ratio: 전체 면 수에 대한 이 비율보다 작은 떨어진 조각을 지운다 (0이면 지우지 않음)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    s = {"raw": {"verts": len(bm.verts), "tris": sum(len(f.verts) - 2 for f in bm.faces)}}
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=WELD)
    s["after_weld"] = bm_stats(bm)  # 점을 합친 뒤에야 진짜 조각·구멍·뒤집힌 면이 보인다

    bverts = list({v for e in bm.edges if e.is_boundary for v in e.verts})
    n = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bverts, dist=zip_dist)
    s["slit_verts_zipped"] = n - len(bm.verts)

    if min_part_ratio > 0:
        groups = islands(bm)
        limit = len(bm.faces) * min_part_ratio
        small = [f for g in groups if len(g) < limit for f in g]
        bmesh.ops.delete(bm, geom=small, context="FACES")
        s["small_parts_removed"] = sum(1 for g in groups if len(g) < limit)

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
    if tear_size > 0:
        passes = []
        for _ in range(3):  # 오므리면서 새로 생긴 작은 틈까지
            passes.append(close_tears(bm, tear_size))
            if passes[-1]["tears_closed"] == 0:
                break
        s["tears"] = {"passes": len(passes), "tears_closed": sum(p["tears_closed"] for p in passes),
                      "bad_edges_left": passes[-1]["bad_edges_left"]}
    dents = []
    if dent_zone:
        bm.normal_update()
        dents, rims = find_dents(bm, dent_zone(bm))
        s["dents_found"], s["fold_rims_skipped"] = len(dents), rims
    orient_normals(bm)
    bm.to_mesh(obj.data)
    bm.free()
    if dents:
        s["dents"] = lift_dents(obj, [list(c) for c in dents])
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
        if obj.data.has_custom_normals:
            bpy.ops.mesh.customdata_custom_splitnormals_clear()  # 점을 합치면 원본 분할 노멀이 맞지 않는다
    obj.data.shade_smooth()
    s["after"] = mesh_stats(obj)
    return s


def recalc_normals(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    flipped = count_flipped(bm)
    orient_normals(bm)
    bm.to_mesh(obj.data)
    bm.free()
    return flipped


def decimate(obj, target_tris):
    """Collapse Decimate (다른 모디파이어보다 먼저 적용)."""
    tris = mesh_stats(obj)["tris"]
    ratio = min(1.0, target_tris / tris)
    mod = obj.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    apply_modifier(obj, "Decimate")
    flipped = recalc_normals(obj)  # 줄이면서 접힌 면이 생길 수 있다
    return {"tris_before": tris, "ratio": r(ratio), "flipped_after_decimate_fixed": flipped, "after": mesh_stats(obj)}


# ---------- 애니메이션 ----------

def fcurves(action):
    return [fc for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]


def shift_keys(action, offset):
    for fc in fcurves(action):
        for kp in fc.keyframe_points:
            kp.co.x += offset
            kp.handle_left.x += offset
            kp.handle_right.x += offset
        fc.update()


def scale_location_keys(action, k):
    """뼈 위치 키는 뼈 공간 길이라, 아마추어를 k배 하면 같이 k배 한다."""
    for fc in fcurves(action):
        if fc.data_path.endswith(".location"):
            for kp in fc.keyframe_points:
                kp.co.y *= k
                kp.handle_left.y *= k
                kp.handle_right.y *= k
            fc.update()


def use_action(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    ad.action_slot = act.slots[0]


def all_poses(arm, act):
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
    for fc in fcurves(act):
        pts = fc.keyframe_points
        for i in reversed(range(len(pts))):
            if pts[i].co.x < a - 1e-3 or pts[i].co.x > b + 1e-3:
                pts.remove(pts[i], fast=True)
        fc.update()
    shift_keys(act, -a)


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


def facing_yaw(arm, act, samples=9):
    """동작 중 몸이 돌아간 평균 각도 (도). 왼다리→오른다리 방향으로 재며, 기본 자세(T포즈)는 0."""
    yaws = []
    s, e = act.frame_range
    use_action(arm, act)
    for k in range(samples):
        bpy.context.scene.frame_set(int(s + (e - s) * k / (samples - 1)))
        side = arm.pose.bones["LeftUpLeg"].head - arm.pose.bones["RightUpLeg"].head
        yaws.append(math.degrees(math.atan2(side.y, side.x)))
    return float(np.mean(yaws))


def fix_facing(arm, act, limit_deg=20.0):
    """동작 전체가 옆으로 돌아가 있으면(예: VARCO jog_looping은 약 -90도) 가장 위 뼈의 회전·위치 키를 되돌려 정면(-Y)을 보게 한다."""
    from mathutils import Quaternion
    yaw = facing_yaw(arm, act)
    info = {"yaw_before_deg": r(yaw, 1)}
    if abs(yaw) < limit_deg:
        return info
    curves = fcurves(act)
    bone = next((b for b in ("Root", "Hips") if any(fc.data_path == f'pose.bones["{b}"].rotation_quaternion' for fc in curves)), None)
    if bone is None:
        info["skipped"] = "회전 키가 있는 Root/Hips 뼈가 없음"
        return info
    rest = arm.data.bones[bone].matrix_local.to_3x3()
    axis = rest.inverted() @ Vector((0.0, 0.0, 1.0))  # 위쪽(Z)을 뼈 공간으로
    turn = Quaternion(axis, math.radians(-yaw))
    for prop, size in (("rotation_quaternion", 4), ("location", 3)):
        chans = sorted((fc for fc in curves if fc.data_path == f'pose.bones["{bone}"].{prop}'), key=lambda c: c.array_index)
        if len(chans) != size:
            continue
        for i in range(len(chans[0].keyframe_points)):
            vals = [c.keyframe_points[i].co.y for c in chans]
            new = turn @ Quaternion(vals) if size == 4 else turn @ Vector(vals)
            for c, val in zip(chans, new):
                kp = c.keyframe_points[i]
                d = val - kp.co.y
                kp.co.y = val
                kp.handle_left.y += d
                kp.handle_right.y += d
        for c in chans:
            c.update()
    info.update(bone=bone, yaw_after_deg=r(facing_yaw(arm, act), 1))
    return info


def lowest_z(meshes):
    """지금 자세에서 메시들의 가장 낮은 점 높이."""
    dg = bpy.context.evaluated_depsgraph_get()
    low = float("inf")
    for o in meshes:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        co = np.empty(len(me.vertices) * 3)
        me.vertices.foreach_get("co", co)
        m = np.array(o.matrix_world)
        z = co.reshape(-1, 3) @ m[2, :3] + m[2, 3]
        low = min(low, float(z.min()))
        ev.to_mesh_clear()
    return low


def ground_feet(arm, act, meshes, bone="Root"):
    """발이 바닥을 뚫지 않게: 프레임마다 가장 낮은 점이 0보다 아래면 그만큼 몸(뼈 위치 키)을 올린다.
    공중에 뜬 프레임(달리기 체공)은 그대로 둔다. VARCO walk_looping은 원본부터 키의 4.3%만큼 바닥 아래로 꺼진다."""
    chans = sorted((fc for fc in fcurves(act) if fc.data_path == f'pose.bones["{bone}"].location'), key=lambda c: c.array_index)
    if len(chans) != 3:
        return {"skipped": f"{bone} 위치 키가 없음"}
    up = arm.data.bones[bone].matrix_local.to_3x3().inverted() @ Vector((0.0, 0.0, 1.0))  # 위쪽을 뼈 공간으로
    use_action(arm, act)
    before, raised = [], 0
    for i, kp in enumerate(chans[0].keyframe_points):
        bpy.context.scene.frame_set(int(round(kp.co.x)))
        low = lowest_z(meshes)
        before.append(low)
        if low < 0:
            for c, comp in zip(chans, up):
                k = c.keyframe_points[i]
                d = -low * comp
                k.co.y += d
                k.handle_left.y += d
                k.handle_right.y += d
            raised += 1
    for c in chans:
        c.update()
    after = []
    for kp in chans[0].keyframe_points:
        bpy.context.scene.frame_set(int(round(kp.co.x)))
        after.append(lowest_z(meshes))
    return {"lowest_before_m": r(min(before), 3), "frames_raised": raised, "lowest_after_m": r(min(after), 3)}


def tidy_loop(arm, act):
    """반복 확인: 처음·끝 자세가 다르면 한 주기를 찾아 잘라내고, 남은 차이는 이어 붙인다."""
    poses = all_poses(arm, act)
    deg, dist = pose_diff(poses[0], poses[-1])
    info = {"frames": r(list(act.frame_range), 1), "seconds": r(len(poses) / FPS, 2),
            "loop_diff_deg": r(deg, 2), "loop_diff_m": r(dist, 4)}
    if deg > LOOP_LIMIT_DEG or dist > LOOP_LIMIT_M:
        a, b = find_cycle(poses)
        trim(act, a, b)
        deg2, dist2 = pose_diff(poses[a], poses[b])
        if (a, b) == (0, len(poses) - 1):
            info["cycle_kept"] = "전체 (클립 안에 더 맞는 구간이 없음)"
        else:
            info.update(cycle_kept=[a, b], cycle_seconds=r((b - a) / FPS, 2), cycle_diff_deg=r(deg2, 2), cycle_diff_m=r(dist2, 4))
        if deg2 > 0.5 or dist2 > 0.005:
            close_loop(act)
            info["blended_last_frames"] = LOOP_BLEND_FRAMES
        after = all_poses(arm, act)
        deg3, dist3 = pose_diff(after[0], after[-1])
        info.update(loop_diff_after_deg=r(deg3, 2), loop_diff_after_m=r(dist3, 4))
    start, end = act.frame_range
    act.use_frame_range = True
    act.frame_start, act.frame_end = start, end
    act.use_cyclic = True
    info["frames_after"] = [start, end]
    return info


# ---------- 텍스처 ----------

def image_into(socket):
    """소켓 위쪽으로 연결된 첫 이미지 텍스처 노드."""
    stack = [socket]
    while stack:
        s = stack.pop()
        for link in s.links:
            n = link.from_node
            if n.type == "TEX_IMAGE":
                return n
            stack.extend(n.inputs)
    return None


def face_colors(obj):
    """면마다 베이스컬러 텍스처 색 (면의 UV 점들과 중심에서 뽑은 평균). 색으로 면을 고를 때 쓴다."""
    bsdf = next(n for n in obj.data.materials[0].node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    px = pixels(image_into(bsdf.inputs["Base Color"]).image)[..., :3]
    h, w = px.shape[:2]
    uv = np.empty(len(obj.data.loops) * 2)
    obj.data.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    cols = np.empty((len(obj.data.polygons), 3))
    for p in obj.data.polygons:
        pts = uv[p.loop_start:p.loop_start + p.loop_total]
        pts = np.vstack([pts, pts.mean(0)])
        x = np.clip((pts[:, 0] % 1.0) * (w - 1), 0, w - 1).astype(int)
        y = np.clip((pts[:, 1] % 1.0) * (h - 1), 0, h - 1).astype(int)
        cols[p.index] = px[y, x].mean(0)
    return cols


def delete_faces(obj, mask):
    """mask가 True인 면과 떨어진 점을 지운다."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(mask)[0]], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    return int(mask.sum())


def remove_small_parts(obj, min_ratio):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    groups = islands(bm)
    limit = len(bm.faces) * min_ratio
    small = [f for g in groups if len(g) < limit for f in g]
    bmesh.ops.delete(bm, geom=small, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    return sum(1 for g in groups if len(g) < limit)


def pixels(img):
    w, h = img.size
    buf = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(buf)
    return buf.reshape(h, w, 4)


def save_png(path, name, arr, colorspace, size=None):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(arr, np.float32).ravel())
    if size and size != w:
        img.scale(size, size)
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def recolor_gray(rgb):
    """옷 텍스처의 회색 안감을 옷 본래 색으로 다시 칠한다 (명암은 유지, 아주 밝은 흰 장식은 그대로).
    VARCO가 앞모습 사진만 보고 등판·안쪽을 회색으로 칠한 곳을 고친다."""
    w = np.array([0.299, 0.587, 0.114], dtype=rgb.dtype)
    lum = rgb @ w
    sat = rgb.max(-1) - rgb.min(-1)
    ref = np.median(rgb[sat > 0.15], axis=0)          # 옷 본래 색
    gray = (sat < 0.1) & (lum > 0.15) & (lum < 0.8)
    if not gray.any():
        return rgb, 0.0
    t = np.clip((0.12 - sat) / 0.06, 0, 1)
    mask = t * t * (3 - 2 * t) * ((lum > 0.15) & (lum < 0.8))
    target = ref[None, None, :] * (lum / lum[gray].mean())[..., None]
    out = rgb * (1 - mask[..., None]) + target * mask[..., None]
    return np.clip(out, 0, 1), float(gray.mean())


def box_blur(img, k):
    """(h, w, c) 배열의 k×k 평균 (누적합으로 빠르게)."""
    pad = k // 2
    for axis in (0, 1):
        widths = [(0, 0)] * img.ndim
        widths[axis] = (pad + 1, pad)
        a = np.cumsum(np.pad(img, widths, mode="edge"), axis=axis)
        hi = np.take(a, range(k, a.shape[axis]), axis=axis)
        lo = np.take(a, range(0, a.shape[axis] - k), axis=axis)
        img = (hi - lo) / k
    return img


def lift_dark_spots(rgb, k=41, ratio=0.6):
    """주변(k 픽셀) 평균보다 ratio배 넘게 어두운 얼룩을 주변 색으로 메운다 (파인 홈 안쪽에 칠해진 어두운 색)."""
    w = np.array([0.299, 0.587, 0.114], dtype=rgb.dtype)
    lum = rgb @ w
    around = box_blur(rgb, k)
    t = np.clip((ratio * (around @ w) - lum) / (0.15 * (around @ w) + 1e-4), 0, 1)
    t = box_blur(t[..., None], 9)[..., 0]  # 경계를 부드럽게
    return rgb * (1 - t[..., None]) + around * t[..., None], float((t > 0.5).mean())


def game_material(obj, name, tex_dir, size, fill_gray=False, lift_dark=False):
    """glTF PBR 머티리얼을 게임 톤으로 보정하고 Unity URP용 맵 4장을 만든다:
    베이스컬러(톤 보정), 노멀, 금속성·매끈함(R 금속, A 매끈함), 차폐. fill_gray면 회색 안감을 옷 색으로."""
    import os
    mat = obj.data.materials[0]
    mat.name = "M_" + name
    mat.use_backface_culling = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    base_node = image_into(bsdf.inputs["Base Color"])
    orm_node = image_into(bsdf.inputs["Roughness"])
    normal_node = image_into(bsdf.inputs["Normal"])

    def path(map_name):
        return os.path.join(tex_dir, f"{name}_{map_name}.png")

    base = pixels(base_node.image)
    toned = base.copy()
    gray_fraction = dark_fraction = 0.0
    if fill_gray:
        toned[..., :3], gray_fraction = recolor_gray(base[..., :3])
    if lift_dark:
        toned[..., :3], dark_fraction = lift_dark_spots(toned[..., :3])
    toned[..., :3] = game_tone.tone(toned[..., :3])
    base_node.image = save_png(path("BaseColor"), f"{name}_BaseColor", toned, "sRGB", size)
    orm = pixels(orm_node.image)  # glTF: R 차폐(AO), G 거칠기, B 금속성
    ms = np.zeros_like(orm)
    ms[..., 0] = orm[..., 2]
    ms[..., 3] = game_tone.smoothness(orm[..., 1])
    save_png(path("MetallicSmoothness"), f"{name}_MetallicSmoothness", ms, "Non-Color", size)
    ao = np.ones_like(orm)
    ao[..., 0] = ao[..., 1] = ao[..., 2] = orm[..., 0]
    save_png(path("Occlusion"), f"{name}_Occlusion", ao, "Non-Color", size)
    normal_node.image = save_png(path("Normal"), f"{name}_Normal", pixels(normal_node.image), "Non-Color", size)

    def sat(rgb):
        return float(np.mean(rgb.max(-1) - rgb.min(-1)))
    return {"material": mat.name, "texture_size": size, "gray_lining_recolored": r(gray_fraction, 3),
            "dark_spots_lifted": r(dark_fraction, 4),
            "saturation": [r(sat(base[..., :3]), 3), r(sat(toned[..., :3]), 3)],
            "mean_roughness": [r(float(np.mean(orm[..., 1])), 3), r(float(np.mean(1 - ms[..., 3])), 3)]}
