import bpy
import math
import os
import sys
from mathutils import Vector


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
output_dir = args[0] if args else os.path.join(os.getcwd(), "kai_turntable")
os.makedirs(output_dir, exist_ok=True)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 700
scene.render.resolution_y = 700
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.world.color = (0.035, 0.035, 0.035)

for obj in list(scene.objects):
    if obj.type in {"CAMERA", "LIGHT"} and obj.name.startswith("Codex_"):
        bpy.data.objects.remove(obj, do_unlink=True)

body = bpy.data.objects.get("KAI_Model")
corners = [body.matrix_world @ Vector(corner) for corner in body.bound_box]
center = sum(corners, Vector()) / 8.0
mins = Vector((min(v.x for v in corners), min(v.y for v in corners), min(v.z for v in corners)))
maxs = Vector((max(v.x for v in corners), max(v.y for v in corners), max(v.z for v in corners)))
span = max(maxs - mins)

cam_data = bpy.data.cameras.new("Codex_Camera")
cam = bpy.data.objects.new("Codex_Camera", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = "ORTHO"
cam.data.ortho_scale = span * 1.28

def point_camera(target):
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()

for idx, loc in enumerate([
    center + Vector((2.6, -3.0, 1.2)),
    center + Vector((3.3, 0.0, 0.8)),
    center + Vector((0.0, -3.3, 0.5)),
    center + Vector((0.0, 0.0, 4.0)),
]):
    light_data = bpy.data.lights.new(f"Codex_Area_{idx}", "AREA")
    light_data.energy = 550.0
    light_data.shape = "DISK"
    light_data.size = 3.0
    light = bpy.data.objects.new(f"Codex_Area_{idx}", light_data)
    scene.collection.objects.link(light)
    light.location = loc
    light.rotation_euler = (center - loc).to_track_quat("-Z", "Y").to_euler()

views = {
    "front": center + Vector((0.0, -span * 3.0, 0.0)),
    "left": center + Vector((span * 3.0, 0.0, 0.0)),
    "back": center + Vector((0.0, span * 3.0, 0.0)),
    "top": center + Vector((0.0, 0.0, span * 3.0)),
    "perspective": center + Vector((span * 2.2, -span * 2.6, span * 1.45)),
}

for name, loc in views.items():
    cam.location = loc
    point_camera(center)
    scene.render.filepath = os.path.join(output_dir, f"kai_{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"RENDERED={scene.render.filepath}")

print(f"BOUNDS_MIN={tuple(round(v, 6) for v in mins)}")
print(f"BOUNDS_MAX={tuple(round(v, 6) for v in maxs)}")
