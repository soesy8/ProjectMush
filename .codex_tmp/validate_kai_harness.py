import bpy
import bmesh
import json
import math
import os
import sys
from mathutils import Vector


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
out_path = args[0] if args else os.path.join(os.getcwd(), "kai_harness_validation.json")
scene = bpy.context.scene
collection = bpy.data.collections.get("KAI_Harness_Modeling")
armature = bpy.data.objects.get("KAI_Rig")

report = {
    "file": bpy.data.filepath,
    "collection_found": collection is not None,
    "object_count": len(collection.objects) if collection else 0,
    "objects": [],
    "texture_nodes": [],
    "rig_motion_test": {},
    "ok": True,
}

if collection:
    for obj in sorted(collection.objects, key=lambda value: value.name):
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        nonmanifold = sum(1 for edge in bm.edges if not edge.is_manifold)
        bm.free()
        armature_modifiers = [m for m in obj.modifiers if m.type == "ARMATURE" and m.object == armature]
        item = {
            "name": obj.name,
            "vertices": len(obj.data.vertices),
            "faces": len(obj.data.polygons),
            "nonmanifold_edges": nonmanifold,
            "uv_layers": len(obj.data.uv_layers),
            "vertex_groups": len(obj.vertex_groups),
            "armature_bound": bool(armature_modifiers),
            "modifiers": [m.type for m in obj.modifiers],
        }
        report["objects"].append(item)
        if nonmanifold or obj.type != "MESH" or not armature_modifiers or len(obj.data.uv_layers):
            report["ok"] = False

for material in bpy.data.materials:
    if material.name.startswith("HRN_") and material.use_nodes:
        for node in material.node_tree.nodes:
            if node.type.startswith("TEX_"):
                report["texture_nodes"].append({"material": material.name, "node": node.name, "type": node.type})
if report["texture_nodes"]:
    report["ok"] = False

# Confirm that the separate harness is actually driven by the existing rig.
if collection and armature:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    samples = {}
    for obj in collection.objects:
        evaluated = obj.evaluated_get(depsgraph)
        evaluated_mesh = evaluated.to_mesh()
        if len(evaluated_mesh.vertices):
            samples[obj.name] = evaluated.matrix_world @ evaluated_mesh.vertices[0].co
        evaluated.to_mesh_clear()
    original_pose_position = armature.data.pose_position
    test_pose_position = "POSE" if original_pose_position == "REST" else "REST"
    armature.data.pose_position = test_pose_position
    armature.update_tag(refresh={"OBJECT", "DATA", "TIME"})
    bpy.context.view_layer.update()
    depsgraph.update()
    movements = {}
    for obj in collection.objects:
        evaluated = obj.evaluated_get(depsgraph)
        evaluated_mesh = evaluated.to_mesh()
        if obj.name in samples and len(evaluated_mesh.vertices):
            moved = evaluated.matrix_world @ evaluated_mesh.vertices[0].co
            movements[obj.name] = (moved - samples[obj.name]).length
        evaluated.to_mesh_clear()
    armature.data.pose_position = original_pose_position
    bpy.context.view_layer.update()
    report["rig_motion_test"] = {
        "test": "pose_to_rest_toggle",
        "from": original_pose_position,
        "to": test_pose_position,
        "max_vertex_motion": max(movements.values()) if movements else 0.0,
        "moving_objects": sum(1 for distance in movements.values() if distance > 1e-6),
        "sample_count": len(movements),
    }
    if report["rig_motion_test"]["moving_objects"] == 0:
        report["ok"] = False

with open(out_path, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)
print("VALIDATION_OK=" + str(report["ok"]))
print("VALIDATION_REPORT=" + out_path)
