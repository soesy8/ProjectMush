import bpy
import json
import sys


args = sys.argv[sys.argv.index("--") + 1 :]
other_path = args[0]
base = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE" and obj.name == "KAI_Rig")

with bpy.data.libraries.load(other_path, link=False) as (data_from, data_to):
    data_to.objects = [name for name in data_from.objects if name == "KAI_Rig"]

other = data_to.objects[0]
bpy.context.scene.collection.objects.link(other)

differences = []
for base_bone in base.data.bones:
    other_bone = other.data.bones.get(base_bone.name)
    if other_bone is None:
        differences.append({"bone": base_bone.name, "status": "missing"})
        continue
    maximum = max(
        abs(base_bone.matrix_local[row][column] - other_bone.matrix_local[row][column])
        for row in range(4)
        for column in range(4)
    )
    if maximum > 1e-7:
        differences.append(
            {
                "bone": base_bone.name,
                "max_matrix_delta": round(maximum, 9),
                "base_head": [round(value, 6) for value in base_bone.head_local],
                "other_head": [round(value, 6) for value in other_bone.head_local],
                "base_tail": [round(value, 6) for value in base_bone.tail_local],
                "other_tail": [round(value, 6) for value in other_bone.tail_local],
            }
        )

result = {
    "base": bpy.data.filepath,
    "other": other_path,
    "different_bones": differences,
    "base_constraints": {
        bone.name: [constraint.type for constraint in bone.constraints]
        for bone in base.pose.bones
        if bone.constraints
    },
    "other_constraints": {
        bone.name: [constraint.type for constraint in bone.constraints]
        for bone in other.pose.bones
        if bone.constraints
    },
}
print("KAI_COMPARE_BEGIN")
print(json.dumps(result, ensure_ascii=False, indent=2))
print("KAI_COMPARE_END")
