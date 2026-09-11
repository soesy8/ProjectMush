import bpy
import json
import sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1 :]
out_path = args[0]
obj = bpy.data.objects["KAI_Model"]
depsgraph = bpy.context.evaluated_depsgraph_get()
eval_obj = obj.evaluated_get(depsgraph)
mesh = eval_obj.to_mesh()
points = [eval_obj.matrix_world @ v.co for v in mesh.vertices]

report = {"y_slices": [], "z_slices": []}
for y in [round(-0.45 + i * 0.05, 3) for i in range(16)]:
    pts = [p for p in points if abs(p.y - y) <= 0.0125 and p.z > 0.35]
    if pts:
        report["y_slices"].append({
            "y": y, "count": len(pts),
            "x_min": min(p.x for p in pts), "x_max": max(p.x for p in pts),
            "z_min": min(p.z for p in pts), "z_max": max(p.z for p in pts),
            "top": sorted([[p.x, p.y, p.z] for p in pts], key=lambda v: v[2], reverse=True)[:8],
        })
for z in [round(0.45 + i * 0.05, 3) for i in range(10)]:
    pts = [p for p in points if abs(p.z - z) <= 0.0125 and -0.45 < p.y < 0.35]
    if pts:
        report["z_slices"].append({
            "z": z, "count": len(pts),
            "x_min": min(p.x for p in pts), "x_max": max(p.x for p in pts),
            "y_min": min(p.y for p in pts), "y_max": max(p.y for p in pts),
        })
with open(out_path, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)
eval_obj.to_mesh_clear()
