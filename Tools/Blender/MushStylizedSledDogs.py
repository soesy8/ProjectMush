"""
Mush prototype sled dogs for Blender 5.x.

This script builds a friendly stylized Siberian Husky and Alaskan Malamute,
creates a studio preview, saves a .blend file, and exports one FBX per dog.

Run from Blender:
    Scripting > Open > MushStylizedSledDogs.py > Run Script

The generated files are written to Tools/Blender/output next to this script.
The script clears the current Blender scene before building the dogs.
"""

import math
import os

import bpy
from mathutils import Vector


# -----------------------------------------------------------------------------
# Output and presentation settings
# -----------------------------------------------------------------------------

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else bpy.path.abspath("//")
OUTPUT_DIR = os.path.join(SCRIPT_DIR, "output")
BLEND_PATH = os.path.join(OUTPUT_DIR, "Mush_Stylized_SledDogs.blend")
PREVIEW_PATH = os.path.join(OUTPUT_DIR, "Mush_Stylized_SledDogs_Preview.png")

RENDER_PREVIEW = True
SAVE_BLEND = True
EXPORT_FBX = True


# -----------------------------------------------------------------------------
# Scene helpers
# -----------------------------------------------------------------------------

def clear_scene():
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def ensure_collection(name):
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(collection)
    return collection


def move_to_collection(obj, collection):
    for old_collection in list(obj.users_collection):
        old_collection.objects.unlink(obj)
    collection.objects.link(obj)


def parent_local(obj, parent, location):
    obj.parent = parent
    obj.location = location


def make_material(name, color, roughness=0.7, metallic=0.0, specular_ior=0.35):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True

    bsdf = material.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        if bsdf.inputs.get("Base Color"):
            bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        if bsdf.inputs.get("Roughness"):
            bsdf.inputs["Roughness"].default_value = roughness
        if bsdf.inputs.get("Metallic"):
            bsdf.inputs["Metallic"].default_value = metallic
        if bsdf.inputs.get("Specular IOR Level"):
            bsdf.inputs["Specular IOR Level"].default_value = specular_ior
    return material


def add_uv_sphere(name, parent, collection, location, scale, material, segments=24, rings=16, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments,
        ring_count=rings,
        radius=1.0,
        location=(0, 0, 0),
    )
    obj = bpy.context.object
    obj.name = name
    move_to_collection(obj, collection)
    parent_local(obj, parent, location)
    obj.rotation_euler = rotation
    obj.scale = scale
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def add_ear(name, parent, collection, location, width, height, depth, material, tilt=0.0, bevel=0.035):
    half_width = width * 0.5
    half_depth = depth * 0.5
    vertices = [
        (-half_width, -half_depth, 0.0),
        (half_width, -half_depth, 0.0),
        (0.0, -half_depth * 0.65, height),
        (-half_width, half_depth, 0.0),
        (half_width, half_depth, 0.0),
        (0.0, half_depth * 0.65, height),
    ]
    faces = [
        (0, 1, 2),
        (5, 4, 3),
        (0, 3, 4, 1),
        (1, 4, 5, 2),
        (2, 5, 3, 0),
    ]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    parent_local(obj, parent, location)
    obj.rotation_euler[1] = tilt
    obj.data.materials.append(material)

    modifier = obj.modifiers.new("Soft ear edges", "BEVEL")
    modifier.width = bevel
    modifier.segments = 3
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    return obj


def add_curve_mesh(name, parent, collection, points, radii, bevel_depth, material, resolution=3):
    curve_data = bpy.data.curves.new(name + "_Curve", "CURVE")
    curve_data.dimensions = "3D"
    curve_data.resolution_u = resolution
    curve_data.bevel_depth = bevel_depth
    curve_data.bevel_resolution = 3
    curve_data.resolution_u = 12
    curve_data.use_fill_caps = True

    spline = curve_data.splines.new("NURBS")
    spline.points.add(len(points) - 1)
    for index, (point, radius) in enumerate(zip(points, radii)):
        spline.points[index].co = (*point, 1.0)
        spline.points[index].radius = radius
    spline.order_u = min(4, len(points))
    spline.use_endpoint_u = True

    obj = bpy.data.objects.new(name, curve_data)
    collection.objects.link(obj)
    parent_local(obj, parent, (0.0, 0.0, 0.0))
    obj.data.materials.append(material)
    return obj


def add_smile(name, parent, collection, side, material):
    points = [
        (0.0, -1.575, 1.535),
        (0.075 * side, -1.580, 1.490),
        (0.155 * side, -1.565, 1.505),
    ]
    return add_curve_mesh(name, parent, collection, points, (0.8, 1.0, 0.65), 0.012, material, 2)


def convert_curves_to_meshes():
    for obj in list(bpy.context.scene.objects):
        if obj.type != "CURVE":
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.convert(target="MESH")


def add_eye(prefix, parent, collection, x, iris_material, materials, eye_scale=1.0):
    # A thin dark rim, warm sclera, colored iris, and small pupil keep the eyes
    # readable without turning them into thick concentric goggles.
    add_uv_sphere(
        prefix + "_Eye",
        parent,
        collection,
        (x, -1.355, 1.905),
        (0.108 * eye_scale, 0.045, 0.128 * eye_scale),
        materials["eye_dark"],
        32,
        20,
    )
    add_uv_sphere(
        prefix + "_Sclera",
        parent,
        collection,
        (x, -1.394, 1.905),
        (0.086 * eye_scale, 0.018, 0.105 * eye_scale),
        materials["sclera"],
        32,
        20,
    )
    add_uv_sphere(
        prefix + "_Iris",
        parent,
        collection,
        (x + 0.010, -1.411, 1.902),
        (0.066 * eye_scale, 0.011, 0.078 * eye_scale),
        iris_material,
        32,
        20,
    )
    add_uv_sphere(
        prefix + "_Pupil",
        parent,
        collection,
        (x + 0.012, -1.421, 1.899),
        (0.031 * eye_scale, 0.006, 0.048 * eye_scale),
        materials["black"],
        24,
        16,
    )
    add_uv_sphere(
        prefix + "_Highlight",
        parent,
        collection,
        (x - 0.004, -1.428, 1.940),
        (0.011, 0.005, 0.014),
        materials["highlight"],
        16,
        10,
    )


def add_leg_set(name, parent, collection, spec, materials):
    width = spec["leg_spacing"]
    front_y = -0.30
    rear_y = 0.62

    for side, sign in (("L", -1), ("R", 1)):
        add_uv_sphere(
            f"{name}_FrontLeg_{side}",
            parent,
            collection,
            (width * sign, front_y, 0.62),
            (spec["front_leg_width"], 0.175, 0.45),
            materials["cream"],
            20,
            14,
        )
        add_uv_sphere(
            f"{name}_FrontPaw_{side}",
            parent,
            collection,
            (width * sign, front_y - 0.10, 0.18),
            (0.205, 0.285, 0.135),
            materials["cream"],
            20,
            14,
        )

        add_uv_sphere(
            f"{name}_RearThigh_{side}",
            parent,
            collection,
            (width * 1.07 * sign, rear_y, 0.72),
            (0.285 * spec["bulk"], 0.36, 0.40),
            materials["mid"],
            20,
            14,
        )
        add_uv_sphere(
            f"{name}_RearLeg_{side}",
            parent,
            collection,
            (width * 1.07 * sign, rear_y + 0.02, 0.43),
            (spec["front_leg_width"] * 1.03, 0.165, 0.31),
            materials["cream"],
            20,
            14,
        )
        add_uv_sphere(
            f"{name}_RearPaw_{side}",
            parent,
            collection,
            (width * 1.07 * sign, rear_y - 0.09, 0.17),
            (0.215 * spec["bulk"], 0.275, 0.13),
            materials["cream"],
            20,
            14,
        )


def build_dog(name, x_offset, spec, palette):
    collection = ensure_collection(name)
    root = bpy.data.objects.new(name + "_ROOT", None)
    collection.objects.link(root)
    root.location = (x_offset, 0.0, 0.0)

    materials = {
        "dark": palette["dark"],
        "mid": palette["mid"],
        "cream": palette["cream"],
        "black": COMMON["black"],
        "eye_dark": COMMON["eye_dark"],
        "sclera": COMMON["sclera"],
        "highlight": COMMON["highlight"],
        "pink": COMMON["pink"],
        "tongue": COMMON["tongue"],
    }

    # Legs are placed first so their tops disappear naturally into the torso.
    add_leg_set(name, root, collection, spec, materials)

    # One broad body mass, one saddle, and one neck mass. This keeps the silhouette
    # readable without building the entire animal from dozens of rock-like blobs.
    add_uv_sphere(
        name + "_Body",
        root,
        collection,
        (0.0, 0.18, 1.03),
        (0.58 * spec["bulk"], 0.97, 0.60 * spec["bulk"]),
        materials["mid"],
        28,
        18,
    )
    add_uv_sphere(
        name + "_BackSaddle",
        root,
        collection,
        (0.0, 0.24, 1.29),
        (0.565 * spec["bulk"], 0.90, 0.36 * spec["bulk"]),
        materials["dark"],
        28,
        18,
    )
    add_uv_sphere(
        name + "_Neck",
        root,
        collection,
        (0.0, -0.48, 1.40),
        (0.50 * spec["bulk"], 0.43, 0.58 * spec["bulk"]),
        materials["dark"],
        28,
        18,
    )
    add_uv_sphere(
        name + "_Chest",
        root,
        collection,
        (0.0, -0.66, 1.18),
        (0.34 * spec["ruff"], 0.255, 0.55 * spec["ruff"]),
        materials["cream"],
        28,
        18,
    )

    # A single round head and one clean face plate replace the old three-disc mask.
    add_uv_sphere(
        name + "_Head",
        root,
        collection,
        (0.0, -0.79, 1.86),
        (0.56 * spec["head_width"], 0.50 * spec["head_depth"], 0.55 * spec["head_height"]),
        materials["dark"],
        32,
        20,
    )
    add_uv_sphere(
        name + "_FacePlate",
        root,
        collection,
        (0.0, -1.205, 1.78),
        (0.405 * spec["face_width"], 0.165, 0.405 * spec["face_height"]),
        materials["cream"],
        32,
        20,
    )

    # Soft cheek volumes make the muzzle smile instead of producing a hard mask.
    for side, sign in (("L", -1), ("R", 1)):
        add_uv_sphere(
            f"{name}_Cheek_{side}",
            root,
            collection,
            (0.225 * sign * spec["face_width"], -1.255, 1.665),
            (0.235 * spec["cheek"], 0.145, 0.225 * spec["cheek"]),
            materials["cream"],
            24,
            16,
        )

    add_uv_sphere(
        name + "_Muzzle",
        root,
        collection,
        (0.0, -1.395, 1.59),
        (0.29 * spec["muzzle"], 0.20, 0.175 * spec["muzzle"]),
        materials["cream"],
        28,
        18,
    )
    add_uv_sphere(
        name + "_Nose",
        root,
        collection,
        (0.0, -1.575, 1.635),
        (0.125 * spec["muzzle"], 0.072, 0.082),
        materials["black"],
        28,
        18,
    )

    eye_x = 0.195 * spec["eye_spacing"]
    add_eye(name + "_L", root, collection, -eye_x, palette["iris"], materials, spec["eye_size"])
    add_eye(name + "_R", root, collection, eye_x, palette["iris"], materials, spec["eye_size"])

    # Ears are beveled wedges. The smaller inset gives color without becoming a
    # second detached triangle floating in front of the dog.
    ear_x = 0.31 * spec["head_width"]
    for side, sign in (("L", -1), ("R", 1)):
        tilt = math.radians(7.0 * sign)
        add_ear(
            f"{name}_Ear_{side}",
            root,
            collection,
            (ear_x * sign, -0.79, 2.18),
            0.27 * spec["ear_size"],
            0.43 * spec["ear_size"],
            0.16,
            materials["dark"],
            tilt,
        )
        add_ear(
            f"{name}_InnerEar_{side}",
            root,
            collection,
            (ear_x * sign, -0.885, 2.22),
            0.145 * spec["ear_size"],
            0.285 * spec["ear_size"],
            0.035,
            materials["pink"],
            tilt,
            0.018,
        )

    # A small closed smile stays friendly without creating a black moustache or
    # a tongue that reads like a separate red ball.
    add_uv_sphere(
        name + "_Mouth",
        root,
        collection,
        (0.0, -1.592, 1.495),
        (0.038, 0.015, 0.030),
        materials["black"],
        20,
        12,
    )
    for side, sign in (("L", -1), ("R", 1)):
        add_uv_sphere(
            f"{name}_Smile_{side}",
            root,
            collection,
            (0.052 * sign, -1.588, 1.485),
            (0.060, 0.009, 0.010),
            materials["black"],
            16,
            10,
            rotation=(0.0, math.radians(18.0 * sign), 0.0),
        )

    # A continuous tapered curve forms a proper curled sled-dog tail.
    side = spec["tail_side"]
    tail_points = [
        (0.0, 0.93, 1.23),
        (0.20 * side, 1.18, 1.43),
        (0.42 * side, 1.22, 1.72),
        (0.46 * side, 1.05, 2.00),
        (0.29 * side, 0.79, 2.13),
        (0.06 * side, 0.68, 2.03),
    ]
    add_curve_mesh(
        name + "_Tail",
        root,
        collection,
        tail_points,
        (1.00, 1.10, 1.08, 0.92, 0.72, 0.42),
        0.22 * spec["tail_fluff"],
        materials["cream"],
        4,
    )
    add_uv_sphere(
        name + "_TailBase",
        root,
        collection,
        (0.0, 0.88, 1.25),
        (0.28 * spec["tail_fluff"], 0.31, 0.27),
        materials["mid"],
        24,
        16,
    )

    return root


# -----------------------------------------------------------------------------
# Preview environment and export
# -----------------------------------------------------------------------------

def look_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def add_area_light(name, location, energy, color, size, target):
    bpy.ops.object.light_add(type="AREA", location=location)
    light = bpy.context.object
    light.name = name
    light.data.energy = energy
    light.data.color = color
    light.data.shape = "DISK"
    light.data.size = size
    look_at(light, target)
    return light


def setup_preview():
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"

    scene.render.resolution_x = 1000
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.filepath = PREVIEW_PATH

    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass

    world = scene.world
    if world is None:
        world = bpy.data.worlds.new("Mush Studio World")
        scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.065, 0.075, 0.09, 1.0)
    background.inputs["Strength"].default_value = 0.35

    ground_material = make_material("M_StudioGround", (0.33, 0.36, 0.40), 0.92)
    bpy.ops.mesh.primitive_plane_add(size=20.0, location=(0.0, 0.0, 0.045))
    ground = bpy.context.object
    ground.name = "Preview_Ground"
    ground.data.materials.append(ground_material)

    bpy.ops.object.camera_add(location=(5.35, -11.65, 3.45))
    camera = bpy.context.object
    camera.name = "Preview_Camera"
    camera.data.lens = 65.0
    look_at(camera, (0.0, 0.02, 1.22))
    scene.camera = camera

    add_area_light("Key", (-4.3, -5.4, 6.2), 1050.0, (1.0, 0.82, 0.70), 4.5, (0, 0, 1.2))
    add_area_light("Fill", (4.8, -3.8, 4.0), 850.0, (0.72, 0.84, 1.0), 4.0, (0, 0, 1.15))
    add_area_light("Rim", (0.0, 4.5, 6.0), 1200.0, (0.78, 0.88, 1.0), 3.5, (0, 0.3, 1.4))


def recursive_children(root):
    result = []
    stack = list(root.children)
    while stack:
        child = stack.pop()
        result.append(child)
        stack.extend(child.children)
    return result


def export_root(root, filepath):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for child in recursive_children(root):
        if child.type in {"MESH", "EMPTY"}:
            child.select_set(True)
    bpy.context.view_layer.objects.active = root

    try:
        bpy.ops.export_scene.fbx(
            filepath=filepath,
            use_selection=True,
            object_types={"EMPTY", "MESH"},
            apply_unit_scale=True,
            bake_space_transform=False,
            axis_forward="-Z",
            axis_up="Y",
            add_leaf_bones=False,
            bake_anim=False,
        )
        print("Exported:", filepath)
    except Exception as exc:
        print("FBX export skipped:", exc)


def build_material_library():
    global COMMON
    COMMON = {
        "black": make_material("M_Nose_Black", (0.012, 0.015, 0.018), 0.24, specular_ior=0.52),
        "eye_dark": make_material("M_Eye_Dark", (0.018, 0.025, 0.032), 0.30, specular_ior=0.48),
        "sclera": make_material("M_Eye_Sclera", (0.88, 0.86, 0.78), 0.42, specular_ior=0.42),
        "highlight": make_material("M_Eye_Highlight", (0.96, 0.98, 1.0), 0.08, specular_ior=0.6),
        "pink": make_material("M_InnerEar", (0.72, 0.29, 0.28), 0.72),
        "tongue": make_material("M_Tongue", (0.62, 0.10, 0.12), 0.58),
    }
    husky = {
        "dark": make_material("M_Husky_Dark", (0.075, 0.09, 0.115), 0.82),
        "mid": make_material("M_Husky_Mid", (0.25, 0.29, 0.34), 0.86),
        "cream": make_material("M_Husky_Cream", (0.82, 0.84, 0.82), 0.90),
        "iris": make_material("M_Husky_Iris", (0.08, 0.38, 0.78), 0.38, specular_ior=0.46),
    }
    malamute = {
        "dark": make_material("M_Malamute_Dark", (0.105, 0.085, 0.075), 0.84),
        "mid": make_material("M_Malamute_Mid", (0.34, 0.30, 0.26), 0.88),
        "cream": make_material("M_Malamute_Cream", (0.82, 0.77, 0.66), 0.91),
        "iris": make_material("M_Malamute_Iris", (0.34, 0.12, 0.025), 0.40, specular_ior=0.44),
    }
    return husky, malamute


HUSKY_SPEC = {
    "bulk": 0.96,
    "ruff": 1.00,
    "head_width": 0.96,
    "head_depth": 0.97,
    "head_height": 0.98,
    "face_width": 0.97,
    "face_height": 1.00,
    "cheek": 0.98,
    "muzzle": 0.96,
    "eye_spacing": 1.00,
    "eye_size": 0.96,
    "ear_size": 1.05,
    "leg_spacing": 0.33,
    "front_leg_width": 0.15,
    "tail_fluff": 1.00,
    "tail_side": 1.0,
}

MALAMUTE_SPEC = {
    "bulk": 1.10,
    "ruff": 1.12,
    "head_width": 1.08,
    "head_depth": 1.04,
    "head_height": 1.04,
    "face_width": 1.07,
    "face_height": 0.98,
    "cheek": 1.10,
    "muzzle": 1.06,
    "eye_spacing": 1.06,
    "eye_size": 0.93,
    "ear_size": 0.92,
    "leg_spacing": 0.38,
    "front_leg_width": 0.17,
    "tail_fluff": 1.10,
    "tail_side": -1.0,
}


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    clear_scene()
    husky_palette, malamute_palette = build_material_library()

    husky_root = build_dog("Husky", -1.48, HUSKY_SPEC, husky_palette)
    malamute_root = build_dog("Malamute", 1.48, MALAMUTE_SPEC, malamute_palette)

    convert_curves_to_meshes()
    setup_preview()

    if RENDER_PREVIEW:
        bpy.context.scene.render.filepath = PREVIEW_PATH
        bpy.ops.render.render(write_still=True)
        print("Rendered:", PREVIEW_PATH)

    if EXPORT_FBX:
        export_root(husky_root, os.path.join(OUTPUT_DIR, "Mush_Husky_Prototype.fbx"))
        export_root(malamute_root, os.path.join(OUTPUT_DIR, "Mush_Malamute_Prototype.fbx"))

    if SAVE_BLEND:
        bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
        print("Saved:", BLEND_PATH)

    bpy.ops.object.select_all(action="DESELECT")
    husky_root.select_set(True)
    bpy.context.view_layer.objects.active = husky_root
    print("Mush stylized sled dogs generated successfully.")


if __name__ == "__main__":
    main()
