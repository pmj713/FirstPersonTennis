"""정리 전·후 캐릭터를 Blender에서 나란히 렌더 (포트폴리오용: 텍스처, 와이어프레임 두 장).

  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b ArtSource/Character/TennisPlayer.blend ^
      --python ArtSource/Blender/render_compare.py -- <프로젝트 폴더>

정리 후 = TennisPlayer.blend (파이프라인 결과), 정리 전 = VARCO 원본 GLB. 둘 다 기본 자세(T자)로 같은 카메라에 담는다.
"""
import os
import sys

import bpy

ROOT = sys.argv[sys.argv.index("--") + 1]
OUT = os.path.join(ROOT, "Docs", "Portfolio")
RAW = os.path.join(ROOT, "ArtSource", "Character", "Raw", "02_Rigged.glb")

scene = bpy.context.scene
after_arm = next(o for o in scene.objects if o.type == "ARMATURE")
after_body = next(o for o in scene.objects if o.type == "MESH")
after_arm.data.pose_position = "REST"
after_arm.location.x = 0.9

before_objs = set(scene.objects)
bpy.ops.import_scene.gltf(filepath=RAW)
new = [o for o in scene.objects if o not in before_objs]
before_arm = next(o for o in new if o.type == "ARMATURE")
before_body = next(o for o in new if o.type == "MESH" and o.parent == before_arm)
for pb in before_arm.pose.bones:
    pb.custom_shape = None
for o in new:
    if o not in (before_arm, before_body):
        bpy.data.objects.remove(o, do_unlink=True)  # 불러오기가 만든 뼈 표시용 구
before_arm.location.x = -1.3
before_arm.hide_render = after_arm.hide_render = True

cam_data = bpy.data.cameras.new("CompareCam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 4.4
cam = bpy.data.objects.new("CompareCam", cam_data)
scene.collection.objects.link(cam)
cam.location = (-0.2, -8.0, 0.95)
cam.rotation_euler = (1.5708, 0.0, 0.0)  # +Y를 본다 (캐릭터 정면은 -Y)
scene.camera = cam

scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x, scene.render.resolution_y = 1600, 900
scene.render.image_settings.file_format = "PNG"
if scene.world is None:
    scene.world = bpy.data.worlds.new("World")
scene.world.color = (0.82, 0.85, 0.88)
shading = scene.display.shading
shading.light = "STUDIO"
os.makedirs(OUT, exist_ok=True)

# 1 텍스처
shading.color_type = "TEXTURE"
scene.render.filepath = os.path.join(OUT, "blender_before_after_textured.png")
bpy.ops.render.render(write_still=True)

# 2 와이어프레임: 회색 몸 위에 검은 선 (폴리곤 밀도 비교)
gray = bpy.data.materials.new("CompareGray")
gray.diffuse_color = (0.75, 0.75, 0.75, 1.0)
wire = bpy.data.materials.new("CompareWire")
wire.diffuse_color = (0.05, 0.05, 0.07, 1.0)
for body in (before_body, after_body):
    body.data.materials.clear()
    body.data.materials.append(gray)
    body.data.materials.append(wire)
    mod = body.modifiers.new("Wire", "WIREFRAME")
    mod.thickness = 0.0012 * body.dimensions.z
    mod.use_replace = False
    mod.material_offset = 1
    mod.use_even_offset = False
shading.color_type = "MATERIAL"
scene.render.filepath = os.path.join(OUT, "blender_before_after_wire.png")
bpy.ops.render.render(write_still=True)
print("RENDER_OK")
