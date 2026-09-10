import bpy
import os
import sys


output_dir = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene
armature = bpy.data.objects["KAI_Rig"]
action = bpy.data.actions["KAI_Walk"]
armature.animation_data_create()
armature.animation_data.action = action
if hasattr(armature.animation_data, "action_slot") and action.slots:
    armature.animation_data.action_slot = action.slots[0]
scene.frame_set(1)

tail_names = ["spine.003", "spine.002", "spine.001", "spine"]
base = {name: armature.pose.bones[name].rotation_euler.copy() for name in tail_names}
tests = {
    "baseline": [0.0, 0.0, 0.0, 0.0],
    "positive_soft": [0.25, 0.18, 0.12, 0.08],
    "negative_soft": [-0.25, -0.18, -0.12, -0.08],
    "positive_curl": [0.42, 0.32, 0.25, 0.2],
    "negative_curl": [-0.42, -0.32, -0.25, -0.2],
    "s_curve": [-0.35, -0.15, 0.25, 0.35],
}

scene.camera = bpy.data.objects["Preview_Camera"]
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 640
scene.render.resolution_y = 480
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.color = (0.04, 0.04, 0.04)
os.makedirs(output_dir, exist_ok=True)

for label, offsets in tests.items():
    for name, offset in zip(tail_names, offsets):
        bone = armature.pose.bones[name]
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = base[name]
        bone.rotation_euler.x += offset
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(output_dir, f"tail_{label}.png")
    bpy.ops.render.render(write_still=True)
    print(f"RENDERED={scene.render.filepath}")
