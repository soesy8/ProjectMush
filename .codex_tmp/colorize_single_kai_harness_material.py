import bpy
import os
import sys


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
output_path = os.path.abspath(args[0]) if args else bpy.data.filepath
collection = bpy.data.collections.get("KAI_Harness_Modeling")
material = bpy.data.materials.get("HRN_Harness_SingleMaterial")
if collection is None or material is None:
    raise RuntimeError("Harness collection or single harness material was not found")

harness_objects = [obj for obj in collection.objects if obj.type == "MESH"]
if len(harness_objects) != 12:
    raise RuntimeError(f"Expected 12 harness meshes, found {len(harness_objects)}")

colors = {
    "padding": (0.018, 0.024, 0.030, 1.0),
    "webbing": (1.000, 0.105, 0.008, 1.0),
    "hardware": (1.000, 0.520, 0.025, 1.0),
}

for obj in harness_objects:
    if "Padding" in obj.name:
        role = "padding"
    elif "Buckle" in obj.name or "LeadRing" in obj.name:
        role = "hardware"
    else:
        role = "webbing"

    attribute = obj.data.color_attributes.get("harness_color")
    if attribute is None:
        attribute = obj.data.color_attributes.new(
            name="harness_color", type="FLOAT_COLOR", domain="POINT"
        )
    for value in attribute.data:
        value.color = colors[role]
    obj["harness_color_role"] = role

    # Retain exactly one shared material slot on every harness mesh.
    obj.data.materials.clear()
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.material_index = 0

material.use_nodes = True
nodes = material.node_tree.nodes
links = material.node_tree.links
nodes.clear()
output = nodes.new("ShaderNodeOutputMaterial")
output.location = (420, 0)
principled = nodes.new("ShaderNodeBsdfPrincipled")
principled.location = (120, 0)
principled.inputs["Roughness"].default_value = 0.48
attribute_node = nodes.new("ShaderNodeAttribute")
attribute_node.location = (-180, 40)
attribute_node.attribute_name = "harness_color"
attribute_node.label = "Per-part color (single material)"
links.new(attribute_node.outputs["Color"], principled.inputs["Base Color"])
links.new(principled.outputs["BSDF"], output.inputs["Surface"])
material.diffuse_color = colors["webbing"]
material["color_source"] = "harness_color vertex attribute"
material["image_textures"] = False
collection["material_setup"] = "one shared material with per-mesh color attributes"

bpy.ops.wm.save_as_mainfile(filepath=output_path, check_existing=False)
print("HARNESS_OBJECTS=" + str(len(harness_objects)))
print("SHARED_MATERIAL=" + material.name)
print("COLOR_ROLES=" + ",".join(sorted(colors)))
print("SAVED_BLEND=" + output_path)
