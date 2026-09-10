import bmesh
import bpy
import json
import os
import sys


def output_dir():
    if "--" in sys.argv:
        args = sys.argv[sys.argv.index("--") + 1 :]
        if args:
            return os.path.abspath(args[0])
    raise RuntimeError("Output directory argument is required")


folder = output_dir()
fbx_path = os.path.join(folder, "Mush_Track_Snowbank_LowPoly.fbx")

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=fbx_path)

mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
if len(mesh_objects) != 1:
    raise RuntimeError(f"Expected one mesh in FBX, found {len(mesh_objects)}")

obj = mesh_objects[0]
bm = bmesh.new()
bm.from_mesh(obj.data)
non_manifold_edges = [edge.index for edge in bm.edges if not edge.is_manifold]
degenerate_faces = [face.index for face in bm.faces if face.calc_area() < 1e-8]
bm.free()

report = {
    "fbx": os.path.basename(fbx_path),
    "object_count": len(bpy.context.scene.objects),
    "mesh_object_count": len(mesh_objects),
    "object_name": obj.name,
    "dimensions_m": [round(value, 4) for value in obj.dimensions],
    "location": [round(value, 4) for value in obj.location],
    "vertices": len(obj.data.vertices),
    "edges": len(obj.data.edges),
    "polygons": len(obj.data.polygons),
    "all_triangles": all(len(poly.vertices) == 3 for poly in obj.data.polygons),
    "non_manifold_edge_count": len(non_manifold_edges),
    "degenerate_face_count": len(degenerate_faces),
    "material_slots": [slot.material.name if slot.material else None for slot in obj.material_slots],
    "status": "PASS" if not non_manifold_edges and not degenerate_faces else "FAIL",
}

path = os.path.join(folder, "Mush_Track_Snowbank_LowPoly_Validation.json")
with open(path, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)

print(json.dumps(report, indent=2))
if report["status"] != "PASS":
    raise RuntimeError("Snowbank validation failed")
