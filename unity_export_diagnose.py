import bpy
import hashlib
import json


def digest(values):
    payload = "|".join(values).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def bone_signature(armature):
    values = []
    for bone in armature.data.bones:
        values.append(
            ";".join(
                [
                    bone.name,
                    bone.parent.name if bone.parent else "",
                    str(bool(bone.use_deform)),
                    *(f"{v:.9f}" for row in bone.matrix_local for v in row),
                ]
            )
        )
    return digest(values)


def mesh_signature(mesh_obj):
    values = []
    for vertex in mesh_obj.data.vertices:
        values.append(
            ";".join(
                [
                    str(vertex.index),
                    *(f"{v:.9f}" for v in vertex.co),
                    *(f"{v:.9f}" for v in vertex.normal),
                ]
            )
        )
    for polygon in mesh_obj.data.polygons:
        values.append(
            "p;" + ";".join(str(index) for index in polygon.vertices)
        )
    return digest(values)


def action_details(action):
    frames = set()
    curve_count = 0
    paths = set()
    slot_ids = []
    if hasattr(action, "slots"):
        slot_ids = [slot.identifier for slot in action.slots]
    if hasattr(action, "layers"):
        for layer in action.layers:
            for strip in layer.strips:
                for channelbag in strip.channelbags:
                    for curve in channelbag.fcurves:
                        curve_count += 1
                        paths.add(curve.data_path)
                        frames.update(round(point.co[0], 6) for point in curve.keyframe_points)
    return {
        "name": action.name,
        "frame_range": [round(value, 6) for value in action.frame_range],
        "slots": slot_ids,
        "curve_count": curve_count,
        "frames": sorted(frames),
        "path_count": len(paths),
    }


armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
result = {
    "file": bpy.data.filepath,
    "scene": {
        "start": bpy.context.scene.frame_start,
        "end": bpy.context.scene.frame_end,
        "fps": bpy.context.scene.render.fps / bpy.context.scene.render.fps_base,
    },
    "armatures": [
        {
            "name": obj.name,
            "bone_count": len(obj.data.bones),
            "bone_signature": bone_signature(obj),
            "active_action": obj.animation_data.action.name if obj.animation_data and obj.animation_data.action else None,
            "active_slot": (
                obj.animation_data.action_slot.identifier
                if obj.animation_data and obj.animation_data.action_slot
                else None
            ),
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
                for track in (obj.animation_data.nla_tracks if obj.animation_data else [])
            ],
        }
        for obj in armatures
    ],
    "meshes": [
        {
            "name": obj.name,
            "vertex_count": len(obj.data.vertices),
            "polygon_count": len(obj.data.polygons),
            "mesh_signature": mesh_signature(obj),
            "materials": [material.name if material else None for material in obj.data.materials],
        }
        for obj in meshes
    ],
    "materials": [
        {
            "name": material.name,
            "use_nodes": material.use_nodes,
        }
        for material in bpy.data.materials
    ],
    "images": [
        {
            "name": image.name,
            "filepath": image.filepath,
            "packed": bool(image.packed_file),
            "source": image.source,
        }
        for image in bpy.data.images
    ],
    "actions": [action_details(action) for action in bpy.data.actions],
}

print("KAI_DIAGNOSE_BEGIN")
print(json.dumps(result, ensure_ascii=False, indent=2))
print("KAI_DIAGNOSE_END")
