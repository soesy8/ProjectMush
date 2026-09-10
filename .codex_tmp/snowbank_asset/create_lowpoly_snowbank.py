import bpy
import json
import math
import os
import random
import sys
from mathutils import Vector


ASSET_NAME = "Mush_Track_Snowbank_LowPoly"
SEED = 240908
LENGTH = 12.0
DEPTH = 2.55
MAX_HEIGHT = 2.15


def get_output_dir():
    marker = "--"
    if marker in sys.argv:
        args = sys.argv[sys.argv.index(marker) + 1 :]
        if args:
            return os.path.abspath(args[0])
    return os.path.abspath(os.path.join(os.path.dirname(__file__), "output_snowbank"))


def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def smooth_noise(values, passes=2):
    result = list(values)
    for _ in range(passes):
        result = [result[0]] + [
            result[i - 1] * 0.25 + result[i] * 0.5 + result[i + 1] * 0.25
            for i in range(1, len(result) - 1)
        ] + [result[-1]]
    return result


def create_snowbank():
    rng = random.Random(SEED)
    # Fewer, wider surface cells produce larger and more readable polygon facets.
    x_count = 23
    y_count = 7
    xs = [-LENGTH * 0.5 + LENGTH * i / (x_count - 1) for i in range(x_count)]
    ys = [-DEPTH * 0.5 + DEPTH * j / (y_count - 1) for j in range(y_count)]

    ridge_noise = smooth_noise([rng.uniform(-0.34, 0.34) for _ in xs], 1)
    center_noise = smooth_noise([rng.uniform(-0.25, 0.25) for _ in xs], 1)
    width_noise = smooth_noise([rng.uniform(-0.16, 0.16) for _ in xs], 1)

    verts = []
    top_index = {}

    for i, x in enumerate(xs):
        u = i / (x_count - 1)
        end_falloff = min(1.0, (u / 0.09), ((1.0 - u) / 0.09))
        end_falloff = 0.58 + 0.42 * max(0.0, min(1.0, end_falloff))

        # A tall left mass, stepped secondary peaks, and a tapered right end make
        # the silhouette more deliberately sculpted than a uniform snow berm.
        left_to_right_slope = 1.12 - 0.30 * u
        peak_a = 0.18 * max(0.0, 1.0 - abs(u - 0.18) / 0.13)
        peak_b = 0.13 * max(0.0, 1.0 - abs(u - 0.46) / 0.12)
        peak_c = 0.10 * max(0.0, 1.0 - abs(u - 0.70) / 0.10)
        length_shape = left_to_right_slope + peak_a + peak_b + peak_c
        local_height = MAX_HEIGHT * length_shape * end_falloff + ridge_noise[i]
        center = 0.16 + center_noise[i]
        half_width = DEPTH * 0.5 * (1.0 + width_noise[i])

        for j, y_base in enumerate(ys):
            v = j / (y_count - 1)
            y = center + (y_base / (DEPTH * 0.5)) * half_width
            normalized = (y - center) / max(0.001, half_width)
            cross = max(0.0, 1.0 - abs(normalized) ** 1.32)
            asymmetric = 1.0 - 0.16 * normalized
            z = 0.08 + local_height * (cross ** 0.48) * asymmetric

            # Preserve grounded edges while breaking up the broad faces into crystalline facets.
            if j not in (0, y_count - 1):
                z += rng.uniform(-0.16, 0.16)
                y += rng.uniform(-0.11, 0.11)
                # Push alternating crest points into visibly sharp wedges.
                if j in (y_count // 2 - 1, y_count // 2, y_count // 2 + 1):
                    z += 0.13 if (i + j) % 3 == 0 else -0.035
            if i not in (0, x_count - 1):
                x_jitter = rng.uniform(-0.13, 0.13)
            else:
                x_jitter = 0.0
            top_index[(i, j)] = len(verts)
            verts.append((x + x_jitter, y, max(0.055, z)))

    # Two rows of bottom vertices close the mesh without exposing a central seam.
    front_bottom = []
    back_bottom = []
    for i, x in enumerate(xs):
        front_y = verts[top_index[(i, 0)]][1]
        back_y = verts[top_index[(i, y_count - 1)]][1]
        front_bottom.append(len(verts))
        verts.append((x, front_y, 0.0))
        back_bottom.append(len(verts))
        verts.append((x, back_y, 0.0))

    faces = []
    # Alternating diagonals keep the low-poly rhythm organic.
    for i in range(x_count - 1):
        for j in range(y_count - 1):
            a = top_index[(i, j)]
            b = top_index[(i + 1, j)]
            c = top_index[(i + 1, j + 1)]
            d = top_index[(i, j + 1)]
            if (i + j) % 2 == 0:
                faces.extend([(a, b, c), (a, c, d)])
            else:
                faces.extend([(a, b, d), (b, c, d)])

    # Front and back skirts.
    for i in range(x_count - 1):
        f0, f1 = top_index[(i, 0)], top_index[(i + 1, 0)]
        b0, b1 = front_bottom[i], front_bottom[i + 1]
        faces.extend([(f0, b1, f1), (f0, b0, b1)])

        t0, t1 = top_index[(i, y_count - 1)], top_index[(i + 1, y_count - 1)]
        c0, c1 = back_bottom[i], back_bottom[i + 1]
        faces.extend([(t0, t1, c1), (t0, c1, c0)])

        faces.extend([(b0, c1, b1), (b0, c0, c1)])

    # Flat end caps.
    left_ring = [top_index[(0, j)] for j in range(y_count)]
    right_ring = [top_index[(x_count - 1, j)] for j in range(y_count)]
    left_center = len(verts)
    verts.append((xs[0], 0.16, 0.28))
    right_center = len(verts)
    verts.append((xs[-1], 0.16, 0.28))

    left_boundary = [front_bottom[0]] + left_ring + [back_bottom[0]]
    right_boundary = [front_bottom[-1]] + right_ring + [back_bottom[-1]]
    for k in range(len(left_boundary) - 1):
        faces.append((left_center, left_boundary[k + 1], left_boundary[k]))
        faces.append((right_center, right_boundary[k], right_boundary[k + 1]))
    faces.append((left_center, back_bottom[0], front_bottom[0]))
    faces.append((right_center, front_bottom[-1], back_bottom[-1]))

    mesh = bpy.data.meshes.new(f"{ASSET_NAME}_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(ASSET_NAME, mesh)
    bpy.context.collection.objects.link(obj)

    # Triangles and flat normals are intentional: they reproduce the faceted concept-art style.
    triangulate = obj.modifiers.new(name="Triangulated_Facets", type="TRIANGULATE")
    triangulate.quad_method = "BEAUTY"
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    for polygon in mesh.polygons:
        polygon.use_smooth = False

    material = bpy.data.materials.new("Snow_Matte_White")
    material.diffuse_color = (0.86, 0.91, 0.98, 1.0)
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.88, 0.93, 1.0, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.88
    bsdf.inputs["Specular IOR Level"].default_value = 0.25
    obj.data.materials.append(material)

    obj["asset_type"] = "Track Snowbank"
    obj["style"] = "Low-poly faceted snow"
    obj["dimensions_m"] = f"{LENGTH:.1f} x {DEPTH:.2f} x {MAX_HEIGHT:.2f}"
    obj["unity_scale"] = "1 Blender unit = 1 meter"
    obj["reference"] = "01_Track_and_Snowbank.png - lower snowbank"
    return obj


def add_preview_scene(asset):
    # A softly blue-grey ground plane gives a clean concept-art preview.
    bpy.ops.mesh.primitive_plane_add(size=40.0, location=(0.0, 0.0, -0.015))
    ground = bpy.context.object
    ground.name = "Preview_Ground"
    ground_mat = bpy.data.materials.new("Preview_Background")
    ground_mat.use_nodes = True
    ground_bsdf = ground_mat.node_tree.nodes.get("Principled BSDF")
    ground_bsdf.inputs["Base Color"].default_value = (0.36, 0.47, 0.64, 1.0)
    ground_bsdf.inputs["Roughness"].default_value = 0.92
    ground.data.materials.append(ground_mat)

    bpy.ops.object.light_add(type="AREA", location=(-3.8, -4.5, 7.5))
    key = bpy.context.object
    key.name = "Preview_Key"
    key.data.energy = 1250
    key.data.shape = "DISK"
    key.data.size = 5.0

    bpy.ops.object.light_add(type="AREA", location=(4.5, 2.7, 4.0))
    fill = bpy.context.object
    fill.name = "Preview_Fill"
    fill.data.energy = 650
    fill.data.color = (0.58, 0.72, 1.0)
    fill.data.size = 4.0

    bpy.ops.object.light_add(type="AREA", location=(0.0, 4.0, 6.0))
    rim = bpy.context.object
    rim.name = "Preview_Rim"
    rim.data.energy = 900
    rim.data.color = (0.78, 0.88, 1.0)
    rim.data.size = 3.0

    bpy.ops.object.camera_add(location=(11.6, -14.5, 8.0))
    camera = bpy.context.object
    camera.name = "Preview_Camera"
    direction = Vector((0.0, 0.0, 0.78)) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 55
    bpy.context.scene.camera = camera

    world = bpy.context.scene.world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.14, 0.20, 0.32, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.55

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.render.resolution_percentage = 100

    for obj in (ground, key, fill, rim, camera):
        obj.hide_render = False
    return ground


def export_asset(asset, output_dir):
    os.makedirs(output_dir, exist_ok=True)
    scene = bpy.context.scene

    blend_path = os.path.join(output_dir, f"{ASSET_NAME}.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    bpy.ops.object.select_all(action="DESELECT")
    asset.select_set(True)
    bpy.context.view_layer.objects.active = asset

    fbx_path = os.path.join(output_dir, f"{ASSET_NAME}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        path_mode="AUTO",
    )

    obj_path = os.path.join(output_dir, f"{ASSET_NAME}.obj")
    bpy.ops.wm.obj_export(
        filepath=obj_path,
        export_selected_objects=True,
        export_materials=True,
        forward_axis="NEGATIVE_Z",
        up_axis="Y",
        apply_modifiers=True,
    )

    preview_path = os.path.join(output_dir, f"{ASSET_NAME}_Preview.png")
    scene.render.filepath = preview_path
    bpy.ops.render.render(write_still=True)

    dimensions = asset.dimensions
    mesh = asset.data
    metadata = {
        "asset": ASSET_NAME,
        "description": "Standalone low-poly snowbank based on the lower asset in 01_Track_and_Snowbank.png",
        "units": "meters",
        "dimensions": {"x": round(dimensions.x, 3), "y": round(dimensions.y, 3), "z": round(dimensions.z, 3)},
        "mesh": {"vertices": len(mesh.vertices), "edges": len(mesh.edges), "triangles": len(mesh.polygons)},
        "origin": "Grounded at world Z=0, centered along length",
        "style": "Flat-shaded, triangulated, matte snow material",
        "files": [os.path.basename(blend_path), os.path.basename(fbx_path), os.path.basename(obj_path), os.path.basename(preview_path)],
    }
    with open(os.path.join(output_dir, f"{ASSET_NAME}_Manifest.json"), "w", encoding="utf-8") as handle:
        json.dump(metadata, handle, indent=2, ensure_ascii=False)


def main():
    output_dir = get_output_dir()
    reset_scene()
    asset = create_snowbank()
    add_preview_scene(asset)
    export_asset(asset, output_dir)
    print(f"SNOWBANK_OUTPUT={output_dir}")


if __name__ == "__main__":
    main()
