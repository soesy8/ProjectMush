import bpy
import os
import sys


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
output_path = os.path.abspath(args[0]) if args else bpy.data.filepath
collection = bpy.data.collections.get("KAI_Harness_Modeling")
if collection is None:
    raise RuntimeError("KAI_Harness_Modeling collection was not found")

material_name = "HRN_Harness_SingleMaterial"
material = bpy.data.materials.get(material_name) or bpy.data.materials.new(material_name)
material.diffuse_color = (1.0, 0.16, 0.015, 1.0)
material.use_nodes = True
principled = material.node_tree.nodes.get("Principled BSDF")
principled.inputs["Base Color"].default_value = (1.0, 0.16, 0.015, 1.0)
principled.inputs["Metallic"].default_value = 0.0
principled.inputs["Roughness"].default_value = 0.5

harness_objects = [obj for obj in collection.objects if obj.type == "MESH"]
expected_count = len(harness_objects)
if expected_count != 12:
    raise RuntimeError(f"Expected 12 harness meshes, found {expected_count}")

for obj in harness_objects:
    if obj.type != "MESH":
        continue
    obj.data.materials.clear()
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.material_index = 0
    obj["harness_material"] = material_name

collection["material_setup"] = "single shared material"
if len([obj for obj in collection.objects if obj.type == "MESH"]) != expected_count:
    raise RuntimeError("Harness object count changed during material reassignment")
bpy.ops.wm.save_as_mainfile(filepath=output_path, check_existing=False)
print("UNIFIED_MATERIAL=" + material_name)
print("HARNESS_MESHES=" + str(expected_count))
print("SAVED_BLEND=" + output_path)
