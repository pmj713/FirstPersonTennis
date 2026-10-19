"""손으로 다듬은 최종 캐릭터 .blend를 마무리해 FBX로 내보낸다 (Blender 5.2, 백그라운드 실행).

  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b <최종 .blend> ^
      --python ArtSource/Blender/finalize_character.py -- <저장할 .blend> <내보낼 .fbx>

파이프라인을 다시 돌리면 손으로 고친 내용이 덮어써지므로, 손본 .blend는 이 스크립트로만 마무리한다.
  1 동작마다 몸 방향 확인: 옆으로 돌아간 동작(Run = VARCO jog_looping, 약 -90도)을 정면으로
  2 발이 바닥을 뚫는 프레임은 몸을 올린다 (VARCO walk_looping은 원본부터 약 7cm 꺼짐)
  3 원본은 그대로 두고 새 .blend로 저장
  3 FBX: 리그 + 파츠, 동작은 NLA 스트립 이름 그대로 (Idle·Walk·Run), 텍스처는 Unity에서 연결
"""
import json
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import pipeline_common as pc  # noqa: E402

out_blend, out_fbx = sys.argv[sys.argv.index("--") + 1:][:2]
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
meshes = [o for o in scene.objects if o.type == "MESH" and o.parent == arm]
ad = arm.animation_data
muted = {t.name: t.mute for t in ad.nla_tracks}
for t in ad.nla_tracks:
    t.mute = True
report = {name: {"facing": pc.fix_facing(arm, bpy.data.actions[name]),
                 "ground": pc.ground_feet(arm, bpy.data.actions[name], meshes)} for name in ("Idle", "Walk", "Run")}
for t in ad.nla_tracks:
    t.mute = muted[t.name]
pc.use_action(arm, bpy.data.actions["Idle"])
ad.action = None  # NLA 스트립으로 내보낸다

bpy.ops.wm.save_as_mainfile(filepath=out_blend, compress=True, copy=True)
os.makedirs(os.path.dirname(out_fbx), exist_ok=True)
markers = [o for o in scene.objects if o.type == "EMPTY" and o.parent == arm]  # 라켓 표식 (tennis_animations.py)
pc.select_only([arm] + meshes + markers, arm)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         add_leaf_bones=False, mesh_smooth_type="FACE",
                         bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, path_mode="STRIP")
report["fbx"] = {"path": out_fbx, "mb": pc.r(os.path.getsize(out_fbx) / 1e6, 2),
                 "meshes": {o.name: pc.mesh_stats(o)["tris"] for o in meshes}}
print("FINALIZE_OK " + json.dumps(report, ensure_ascii=False))
