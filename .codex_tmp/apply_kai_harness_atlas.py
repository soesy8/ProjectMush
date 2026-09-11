import bpy
import binascii
import os
import struct
import sys
import zlib


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
if len(args) < 2:
    raise RuntimeError("Usage: target_output.blend complete_harness_source.blend")

output_path = os.path.abspath(args[0])
source_path = os.path.abspath(args[1])
output_dir = os.path.dirname(output_path)

expected_names = {
    "HRN_BackConnectorPadding",
    "HRN_BackConnectorWebbing",
    "HRN_BackReinforcement",
    "HRN_BuckleFace",
    "HRN_BuckleOuter",
    "HRN_ChestConnectorPadding",
    "HRN_ChestConnectorWebbing",
    "HRN_GirthPadding",
    "HRN_GirthWebbing",
    "HRN_LeadRing",
    "HRN_NeckPadding",
    "HRN_NeckWebbing",
}

collection = bpy.data.collections.get("KAI_Harness_Modeling")
if collection is None:
    collection = bpy.data.collections.new("KAI_Harness_Modeling")
    bpy.context.scene.collection.children.link(collection)

rig = bpy.data.objects.get("KAI_Rig")
if rig is None:
    raise RuntimeError("KAI_Rig was not found in the target copy")

# The supplied copy is missing most harness objects. Restore only missing harness pieces,
# leaving the dog, rig, and any existing harness objects in this copy untouched.
present_names = {obj.name for obj in bpy.data.objects}
missing_names = sorted(expected_names - present_names)
if missing_names:
    if not os.path.isfile(source_path):
        raise RuntimeError("Complete harness source was not found: " + source_path)
    with bpy.data.libraries.load(source_path, link=False) as (data_from, data_to):
        unavailable = sorted(set(missing_names) - set(data_from.objects))
        if unavailable:
            raise RuntimeError("Missing harness objects unavailable in source: " + ",".join(unavailable))
        data_to.objects = missing_names
    for obj in data_to.objects:
        if obj is None:
            continue
        if not obj.users_collection:
            collection.objects.link(obj)
        elif collection not in obj.users_collection:
            collection.objects.link(obj)
        obj.parent = rig
        obj.matrix_parent_inverse = rig.matrix_world.inverted()
        for modifier in obj.modifiers:
            if modifier.type == "ARMATURE":
                modifier.object = rig

harness_objects = [bpy.data.objects.get(name) for name in sorted(expected_names)]
if any(obj is None for obj in harness_objects):
    raise RuntimeError("Harness restoration did not produce all expected objects")

# Ensure every harness object is linked to the intended harness collection.
for obj in harness_objects:
    if collection not in obj.users_collection:
        collection.objects.link(obj)

atlas_size = 1024
half = atlas_size // 2

# UV layout (Blender UV origin is bottom-left):
# top-left     = orange webbing
# top-right    = dark padding
# bottom-left  = gold hardware
# bottom-right = neutral spare tile
base_srgb = {
    "webbing": (1.00, 0.18, 0.015, 1.0),
    "padding": (0.018, 0.026, 0.036, 1.0),
    "hardware": (0.95, 0.55, 0.035, 1.0),
    "spare": (0.18, 0.19, 0.21, 1.0),
}
smoothness = {
    "webbing": (0.40, 0.40, 0.40, 1.0),
    "padding": (0.22, 0.22, 0.22, 1.0),
    "hardware": (0.78, 0.78, 0.78, 1.0),
    "spare": (0.35, 0.35, 0.35, 1.0),
}
metallic = {
    "webbing": (0.0, 0.0, 0.0, 1.0),
    "padding": (0.0, 0.0, 0.0, 1.0),
    "hardware": (1.0, 1.0, 1.0, 1.0),
    "spare": (0.0, 0.0, 0.0, 1.0),
}


def create_atlas_image(name, filename, values, is_color):
    filepath = os.path.join(output_dir, filename)
    def rgba8(value):
        return bytes(max(0, min(255, round(channel * 255.0))) for channel in value)

    top_row = rgba8(values["webbing"]) * half + rgba8(values["padding"]) * half
    bottom_row = rgba8(values["hardware"]) * half + rgba8(values["spare"]) * half
    raw_scanlines = (b"\x00" + top_row) * half + (b"\x00" + bottom_row) * half

    def png_chunk(chunk_type, payload):
        checksum = binascii.crc32(chunk_type + payload) & 0xFFFFFFFF
        return struct.pack(">I", len(payload)) + chunk_type + payload + struct.pack(">I", checksum)

    png_bytes = b"\x89PNG\r\n\x1a\n"
    png_bytes += png_chunk(b"IHDR", struct.pack(">IIBBBBB", atlas_size, atlas_size, 8, 6, 0, 0, 0))
    png_bytes += png_chunk(b"IDAT", zlib.compress(raw_scanlines, 9))
    png_bytes += png_chunk(b"IEND", b"")
    with open(filepath, "wb") as handle:
        handle.write(png_bytes)

    existing = bpy.data.images.get(name)
    if existing:
        bpy.data.images.remove(existing, do_unlink=True)
    image = bpy.data.images.load(filepath, check_existing=False)
    image.name = name
    image.colorspace_settings.name = "sRGB" if is_color else "Non-Color"
    return image


material_name = "HRN_Harness_AtlasMaterial"
material = bpy.data.materials.get(material_name) or bpy.data.materials.new(material_name)
material.use_nodes = True
material.node_tree.nodes.clear()

base_image = create_atlas_image(
    "KAI_Harness_Atlas_BaseColor", "KAI_Harness_Atlas_BaseColor.png", base_srgb, True
)
smooth_image = create_atlas_image(
    "KAI_Harness_Atlas_Smoothness", "KAI_Harness_Atlas_Smoothness.png", smoothness, False
)
metal_image = create_atlas_image(
    "KAI_Harness_Atlas_Metallic", "KAI_Harness_Atlas_Metallic.png", metallic, False
)

material.diffuse_color = base_srgb["webbing"]
nodes = material.node_tree.nodes
links = material.node_tree.links
nodes.clear()

output = nodes.new("ShaderNodeOutputMaterial")
output.location = (620, 20)
principled = nodes.new("ShaderNodeBsdfPrincipled")
principled.location = (330, 20)
texcoord = nodes.new("ShaderNodeTexCoord")
texcoord.location = (-720, 20)

base_node = nodes.new("ShaderNodeTexImage")
base_node.name = "KAI Atlas - Base Color"
base_node.label = "2x2 Atlas Base Color"
base_node.image = base_image
base_node.location = (-430, 230)
base_node.interpolation = "Linear"

smooth_node = nodes.new("ShaderNodeTexImage")
smooth_node.name = "KAI Atlas - Smoothness"
smooth_node.label = "2x2 Atlas Smoothness"
smooth_node.image = smooth_image
smooth_node.location = (-430, 0)
smooth_node.interpolation = "Linear"

invert_smooth = nodes.new("ShaderNodeMath")
invert_smooth.name = "Smoothness to Roughness"
invert_smooth.label = "Roughness = 1 - Smoothness"
invert_smooth.operation = "SUBTRACT"
invert_smooth.inputs[0].default_value = 1.0
invert_smooth.location = (40, -40)

metal_node = nodes.new("ShaderNodeTexImage")
metal_node.name = "KAI Atlas - Metallic"
metal_node.label = "2x2 Atlas Metallic"
metal_node.image = metal_image
metal_node.location = (-430, -230)
metal_node.interpolation = "Linear"

for texture_node in (base_node, smooth_node, metal_node):
    links.new(texcoord.outputs["UV"], texture_node.inputs["Vector"])
links.new(base_node.outputs["Color"], principled.inputs["Base Color"])
links.new(smooth_node.outputs["Color"], invert_smooth.inputs[1])
links.new(invert_smooth.outputs["Value"], principled.inputs["Roughness"])
links.new(metal_node.outputs["Color"], principled.inputs["Metallic"])
links.new(principled.outputs["BSDF"], output.inputs["Surface"])

for obj in harness_objects:
    obj.data.materials.clear()
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.material_index = 0

# Remove superseded, now-unused harness materials without unlinking any object.
for candidate in list(bpy.data.materials):
    if candidate != material and candidate.name.startswith("HRN_") and candidate.users == 0:
        bpy.data.materials.remove(candidate, do_unlink=False)

collection["atlas_layout_top_left"] = "orange webbing"
collection["atlas_layout_top_right"] = "dark padding"
collection["atlas_layout_bottom_left"] = "gold hardware"
collection["atlas_layout_bottom_right"] = "neutral spare"
collection["atlas_uv_work"] = "not created or modified"
collection["restored_missing_harness_objects"] = len(missing_names)

# Keep all texture paths portable relative to the .blend in the same directory.
for image in (base_image, smooth_image, metal_image):
    image.filepath = bpy.path.relpath(image.filepath_raw, start=output_dir)

os.makedirs(output_dir, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=output_path, check_existing=False)
print("RESTORED_OBJECTS=" + str(len(missing_names)))
print("HARNESS_OBJECTS=" + str(len(harness_objects)))
print("UV_LAYERS=" + str(sum(len(obj.data.uv_layers) for obj in harness_objects)))
print("MATERIAL=" + material.name)
print("BASE_COLOR=" + base_image.filepath_raw)
print("SMOOTHNESS=" + smooth_image.filepath_raw)
print("METALLIC=" + metal_image.filepath_raw)
print("SAVED_BLEND=" + output_path)
