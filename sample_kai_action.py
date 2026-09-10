import bpy
import json
import os
import sys


args = sys.argv[sys.argv.index("--") + 1 :]
action_name = args[0]
output_path = args[1]
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

tracked = [
    "ROOT", "BODY", "spine.003", "spine.002", "spine.001", "spine",
    "spine.005", "spine.006", "spine.007", "spine.008", "spine.009",
    "spine.010", "spine.011", "Head", "Face", "Ear.L", "Ear.R",
    "IK_front_L", "IK_front_R", "PawOrientation_front_L", "PawOrientation_front_R",
    "IK_L", "IK_R", "HockTarget.L", "HockTarget.R",
    "PawOrientation_L", "PawOrientation_R",
]

report = {"action": action_name, "frames": {}}
for frame in frames:
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    frame_data = {}
    for name in tracked:
        bone = armature.pose.bones.get(name)
        if not bone:
            continue
        head_world = armature.matrix_world @ bone.head
        tail_world = armature.matrix_world @ bone.tail
        frame_data[name] = {
            "location": [round(v, 6) for v in bone.location],
            "rotation_euler": [round(v, 6) for v in bone.rotation_euler],
            "rotation_quaternion": [round(v, 6) for v in bone.rotation_quaternion],
            "scale": [round(v, 6) for v in bone.scale],
            "head_world": [round(v, 6) for v in head_world],
            "tail_world": [round(v, 6) for v in tail_world],
        }
    report["frames"][str(frame)] = frame_data

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)
print(f"SAMPLE_WRITTEN={output_path}")
