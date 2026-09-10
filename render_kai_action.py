import bpy
import os
import sys


args = sys.argv[sys.argv.index("--") + 1 :]
action_name = args[0]
output_dir = args[1]
frames = [int(value) for value in args[2].split(",")]

scene = bpy.context.scene
armature = bpy.data.objects["KAI_Rig"]
action = bpy.data.actions[action_name]
armature.animation_data_create()
armature.animation_data.action = action
if hasattr(armature.animation_data, "action_slot") and hasattr(action, "slots") and action.slots:
    suitable = [slot for slot in action.slots if slot.target_id_type == "OBJECT"]
    if suitable:
        armature.animation_data.action_slot = suitable[0]

scene.camera = bpy.data.objects.get("Preview_Camera")
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 640
scene.render.resolution_y = 480
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.world.color = (0.04, 0.04, 0.04)

os.makedirs(output_dir, exist_ok=True)
for frame in frames:
    scene.frame_set(frame)
    scene.render.filepath = os.path.join(output_dir, f"{action_name}_{frame:03d}.png")
    bpy.ops.render.render(write_still=True)
    print(f"RENDERED={scene.render.filepath}")
