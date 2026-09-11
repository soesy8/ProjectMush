import bpy
import math
from mathutils import Matrix, Vector

body = bpy.data.objects["KAI_Model"]
arm = bpy.data.objects["KAI_Rig"]
deps = bpy.context.evaluated_depsgraph_get()
eval_body = body.evaluated_get(deps)
eval_mesh = eval_body.to_mesh()

group_names = {g.index: g.name for g in body.vertex_groups}
errors = []
for idx in range(0, len(body.data.vertices), max(1, len(body.data.vertices) // 30)):
    vert = body.data.vertices[idx]
    weighted = Matrix(((0,0,0,0),(0,0,0,0),(0,0,0,0),(0,0,0,0)))
    total = 0.0
    for g in vert.groups:
        name = group_names.get(g.group)
        pb = arm.pose.bones.get(name) if name else None
        if pb and pb.bone.use_deform:
            deform = arm.matrix_world @ pb.matrix @ pb.bone.matrix_local.inverted() @ arm.matrix_world.inverted() @ body.matrix_world
            weighted = weighted + deform * g.weight
            total += g.weight
    if total > 0:
        weighted = weighted * (1.0 / total)
        calc = weighted @ vert.co
    else:
        calc = body.matrix_world @ vert.co
    actual = eval_body.matrix_world @ eval_mesh.vertices[idx].co
    errors.append((idx, (calc-actual).length, tuple(calc), tuple(actual), total))
for row in sorted(errors, key=lambda x: x[1], reverse=True)[:12]:
    print("SKIN", row)
eval_body.to_mesh_clear()
