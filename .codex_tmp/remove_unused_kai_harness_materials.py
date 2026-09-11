import bpy
import os
import sys


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
output_path = os.path.abspath(args[0]) if args else bpy.data.filepath
collection = bpy.data.collections["KAI_Harness_Modeling"]
harness_objects = [obj for obj in collection.objects if obj.type == "MESH"]
if len(harness_objects) != 12:
    raise RuntimeError(f"Expected 12 harness meshes, found {len(harness_objects)}")

for old_name in ("HRN_Preview_DarkPadding", "HRN_Preview_OrangeWebbing", "HRN_Preview_GoldHardware"):
    material = bpy.data.materials.get(old_name)
    if material:
        if material.users != 0:
            raise RuntimeError(f"Refusing to remove in-use material {old_name}: users={material.users}")
        bpy.data.materials.remove(material, do_unlink=False)

if len([obj for obj in collection.objects if obj.type == "MESH"]) != 12:
    raise RuntimeError("Harness object count changed during unused material cleanup")
used = {material.name for obj in harness_objects for material in obj.data.materials}
if used != {"HRN_Harness_SingleMaterial"}:
    raise RuntimeError(f"Unexpected harness materials: {sorted(used)}")

bpy.ops.wm.save_as_mainfile(filepath=output_path, check_existing=False)
print("HARNESS_OBJECTS=12")
print("HARNESS_MATERIALS=" + ",".join(sorted(used)))
print("SAVED_BLEND=" + output_path)
