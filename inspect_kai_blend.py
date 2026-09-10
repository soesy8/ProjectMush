import bpy
import json
import os
import sys


def compact_matrix(matrix):
    return [[round(value, 6) for value in row] for row in matrix]


def compact_vector(vector):
    return [round(value, 6) for value in vector]


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
output_path = args[0] if args else os.path.join(os.getcwd(), "kai_blend_inspection.json")

scene = bpy.context.scene
report = {
    "blender_version": bpy.app.version_string,
    "file": bpy.data.filepath,
    "scene": {
        "name": scene.name,
        "frame_start": scene.frame_start,
        "frame_end": scene.frame_end,
        "frame_current": scene.frame_current,
        "fps": scene.render.fps,
        "fps_base": scene.render.fps_base,
        "unit_system": scene.unit_settings.system,
    },
    "objects": [],
    "actions": [],
}

for action in bpy.data.actions:
    action_info = {
        "name": action.name,
        "frame_range": [round(float(x), 4) for x in action.frame_range],
        "users": action.users,
        "slots": [],
        "fcurves": [],
    }
    if hasattr(action, "slots"):
        for slot in action.slots:
            action_info["slots"].append({
                "identifier": getattr(slot, "identifier", ""),
                "target_id_type": getattr(slot, "target_id_type", ""),
            })
    fcurves = list(getattr(action, "fcurves", []))
    if not fcurves and hasattr(action, "layers"):
        for layer in action.layers:
            for strip in layer.strips:
                channelbags = getattr(strip, "channelbags", [])
                for channelbag in channelbags:
                    fcurves.extend(channelbag.fcurves)
    for fcurve in fcurves:
        action_info["fcurves"].append({
            "data_path": fcurve.data_path,
            "array_index": fcurve.array_index,
            "keys": [[round(k.co.x, 4), round(k.co.y, 6)] for k in fcurve.keyframe_points],
        })
    report["actions"].append(action_info)

for obj in scene.objects:
    info = {
        "name": obj.name,
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else None,
        "parent_type": obj.parent_type,
        "location": compact_vector(obj.location),
        "rotation_mode": obj.rotation_mode,
        "rotation_euler": compact_vector(obj.rotation_euler),
        "scale": compact_vector(obj.scale),
        "dimensions": compact_vector(obj.dimensions),
        "matrix_world": compact_matrix(obj.matrix_world),
        "hidden_render": obj.hide_render,
        "modifiers": [
            {
                "name": modifier.name,
                "type": modifier.type,
                "object": getattr(getattr(modifier, "object", None), "name", None),
            }
            for modifier in obj.modifiers
        ],
        "animation": None,
    }
    if obj.animation_data:
        info["animation"] = {
            "action": obj.animation_data.action.name if obj.animation_data.action else None,
            "nla_tracks": [
                {
                    "name": track.name,
                    "mute": track.mute,
                    "strips": [
                        {
                            "name": strip.name,
                            "action": strip.action.name if strip.action else None,
                            "frame_start": strip.frame_start,
                            "frame_end": strip.frame_end,
                        }
                        for strip in track.strips
                    ],
                }
                for track in obj.animation_data.nla_tracks
            ],
        }
    if obj.type == "ARMATURE":
        info["display_type"] = obj.display_type
        info["bones"] = []
        for bone in obj.data.bones:
            pose_bone = obj.pose.bones.get(bone.name)
            constraints = []
            if pose_bone:
                for constraint in pose_bone.constraints:
                    constraints.append({
                        "name": constraint.name,
                        "type": constraint.type,
                        "target": getattr(getattr(constraint, "target", None), "name", None),
                        "subtarget": getattr(constraint, "subtarget", ""),
                        "influence": getattr(constraint, "influence", None),
                    })
            info["bones"].append({
                "name": bone.name,
                "parent": bone.parent.name if bone.parent else None,
                "use_deform": bone.use_deform,
                "head_local": compact_vector(bone.head_local),
                "tail_local": compact_vector(bone.tail_local),
                "length": round(bone.length, 6),
                "roll": round(bone.matrix_local.to_euler().y, 6),
                "pose_rotation_mode": pose_bone.rotation_mode if pose_bone else None,
                "pose_location": compact_vector(pose_bone.location) if pose_bone else None,
                "pose_rotation_euler": compact_vector(pose_bone.rotation_euler) if pose_bone else None,
                "pose_rotation_quaternion": compact_vector(pose_bone.rotation_quaternion) if pose_bone else None,
                "pose_scale": compact_vector(pose_bone.scale) if pose_bone else None,
                "constraints": constraints,
            })
    if obj.type == "MESH":
        info["vertex_count"] = len(obj.data.vertices)
        info["polygon_count"] = len(obj.data.polygons)
        info["vertex_groups"] = [group.name for group in obj.vertex_groups]
    report["objects"].append(info)

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(report, handle, ensure_ascii=False, indent=2)

print(f"INSPECTION_WRITTEN={output_path}")
