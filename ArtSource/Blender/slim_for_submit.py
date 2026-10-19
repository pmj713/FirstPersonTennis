"""제출용으로 캐릭터 .blend·FBX 용량 줄이기 (Blender 5.2, 백그라운드 실행).

  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b <최종 .blend> ^
      --python ArtSource/Blender/slim_for_submit.py -- <저장할 .blend> <내보낼 .fbx>

.blend는 이미 zstd로 압축되어 있고 안에 묶인 텍스처도 PNG라 zip으로 다시 압축해도 거의 줄지 않는다.
용량의 대부분인 묶인 텍스처를 줄인다:
  베이스컬러 → JPG (품질 90), 노멀 → PNG (몸 1024, 헤어·신발 512), 거칠기·차폐(ORM) → PNG 512
FBX는 텍스처를 넣어(embed) 받는 쪽에서 바로 색이 보이게 한다.
"""
import json
import os
import shutil
import sys
import tempfile

import bpy

out_blend, out_fbx = sys.argv[sys.argv.index("--") + 1:][:2]
tmp = tempfile.mkdtemp(prefix="slim_")
scene = bpy.context.scene
used = {n.image for m in bpy.data.materials if m.users and m.use_nodes
        for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}

report = {}
for img in list(bpy.data.images):
    if img not in used or img.size[0] == 0:
        continue
    before = img.packed_file.size if img.packed_file else 0
    name = bpy.path.clean_name(img.name)
    if "Normal" in img.name:
        size, fmt, ext = (1024 if "Body" in img.name else 512), "PNG", ".png"  # 헤어·신발은 화면에서 작다
    elif "BaseColor" in img.name:
        size, fmt, ext = img.size[0], "JPEG", ".jpg"  # 해상도는 두고 형식만 (얼굴 디테일 유지)
    else:  # 원본 glTF ORM (차폐·거칠기·금속성)
        size, fmt, ext = 512, "PNG", ".png"
    if img.size[0] > size:
        img.scale(size, size)
    path = os.path.join(tmp, name + ext)
    scene.render.image_settings.file_format = fmt
    if fmt == "JPEG":
        scene.render.image_settings.quality = 90
    scene.render.image_settings.color_mode = "RGB"
    img.save_render(path, scene=scene)
    is_data = img.colorspace_settings.name
    if img.packed_file:
        img.unpack(method="REMOVE")
    img.filepath = path
    img.source = "FILE"
    img.reload()
    img.colorspace_settings.name = is_data
    img.pack()
    report[img.name] = {"size": list(img.size), "format": fmt, "kb_before": before // 1024, "kb_after": img.packed_file.size // 1024}

bpy.data.orphans_purge(do_recursive=True)
bpy.ops.wm.save_as_mainfile(filepath=out_blend, compress=True, copy=True)

arm = next(o for o in scene.objects if o.type == "ARMATURE")
meshes = [o for o in scene.objects if o.type == "MESH" and o.parent == arm]
for o in scene.objects:
    o.select_set(o is arm or o in meshes)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         add_leaf_bones=False, mesh_smooth_type="FACE",
                         bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                         path_mode="COPY", embed_textures=True)
shutil.rmtree(tmp, ignore_errors=True)
print("SLIM_OK " + json.dumps({"images": report, "blend_mb": round(os.path.getsize(out_blend) / 1e6, 2),
                               "fbx_mb": round(os.path.getsize(out_fbx) / 1e6, 2)}, ensure_ascii=False))
