import bpy
import json
import os


def rounded_matrix(matrix):
    return [[round(value, 6) for value in row] for row in matrix]


data = {
    "file": bpy.data.filepath,
    "scene": {
        "frame_start": bpy.context.scene.frame_start,
        "frame_end": bpy.context.scene.frame_end,
        "fps": bpy.context.scene.render.fps,
        "fps_base": bpy.context.scene.render.fps_base,
    },
    "objects": [],
    "actions": [],
}

for obj in sorted(bpy.data.objects, key=lambda item: item.name):
    item = {
        "name": obj.name,
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else None,
        "matrix_world": rounded_matrix(obj.matrix_world),
    }
    if obj.type == "MESH":
        item.update({
            "vertices": len(obj.data.vertices),
            "polygons": len(obj.data.polygons),
            "armature_modifiers": [
                modifier.object.name if modifier.object else None
                for modifier in obj.modifiers
                if modifier.type == "ARMATURE"
            ],
            "vertex_groups": sorted(group.name for group in obj.vertex_groups),
        })
    elif obj.type == "ARMATURE":
        item.update({
            "bones": [
                {
                    "name": bone.name,
                    "parent": bone.parent.name if bone.parent else None,
                    "use_deform": bone.use_deform,
                }
                for bone in obj.data.bones
            ],
            "active_action": (
                obj.animation_data.action.name
                if obj.animation_data and obj.animation_data.action
                else None
            ),
        })
    data["objects"].append(item)

for action in sorted(bpy.data.actions, key=lambda item: item.name):
    slots = []
    if hasattr(action, "slots"):
        slots = [slot.identifier for slot in action.slots]
    data["actions"].append({
        "name": action.name,
        "frame_range": [round(value, 6) for value in action.frame_range],
        "slots": slots,
        "users": action.users,
    })

print("KAI_INSPECT_BEGIN")
print(json.dumps(data, ensure_ascii=False, indent=2))
print("KAI_INSPECT_END")
