import bpy
from pathlib import Path

blend_path = Path(bpy.data.filepath)
target_name = "T_Carpet_Green_Paw_BaseColor.png"
matched = []

for image in bpy.data.images:
    if Path(bpy.path.abspath(image.filepath)).name == target_name or image.name == target_name:
        image.reload()
        matched.append(image)
        print(
            f"IMAGE name={image.name!r} path={bpy.path.abspath(image.filepath)!r} "
            f"size={tuple(image.size)} packed={image.packed_file is not None}"
        )

users = []
for material in bpy.data.materials:
    if not material.use_nodes or not material.node_tree:
        continue
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image in matched:
            users.append((material.name, node.name))
            print(f"MATERIAL material={material.name!r} node={node.name!r}")

print(f"SUMMARY blend={str(blend_path)!r} matched_images={len(matched)} material_users={len(users)}")
if not matched or not users:
    raise RuntimeError("Green carpet texture is not connected to a Blender material")

# Persist only if the texture was packed; otherwise Blender reads the updated external PNG.
if any(image.packed_file is not None for image in matched):
    for image in matched:
        if image.packed_file is not None:
            image.pack()
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    print("SAVED updated packed texture data")
else:
    print("EXTERNAL_TEXTURE_OK Blender uses the updated PNG directly")
