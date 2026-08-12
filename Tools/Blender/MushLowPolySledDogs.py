"""
Mush low-poly sled dog prototype generator for Blender 5.x.

Builds a recognizable Siberian Husky and Alaskan Malamute with:
- angular low-poly surfaces
- upright ears and long canine muzzles
- husky face blaze / saddle markings
- proportional legs and paws
- tapered raised tails
- blue collars

Run in a NEW Blender file. The current scene is cleared.
Outputs are written to Tools/Blender/lowpoly_output next to this script.
"""

import math
import os

import bpy
from mathutils import Vector


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else bpy.path.abspath("//")
OUTPUT_DIR = os.path.join(SCRIPT_DIR, "lowpoly_output")
BLEND_PATH = os.path.join(OUTPUT_DIR, "Mush_LowPoly_SledDogs.blend")
PREVIEW_PATH = os.path.join(OUTPUT_DIR, "Mush_LowPoly_SledDogs_Preview.png")

RENDER_PREVIEW = True
EXPORT_FBX = True
SAVE_BLEND = True


def clear_scene():
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


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


def set_parent_local(obj, parent, location):
    obj.parent = parent
    obj.location = location


def make_material(name, color, roughness=0.82, metallic=0.0):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    # Node display names are localized (for example, Korean Blender renames
    # "Principled BSDF"), so locate the shader by its stable node type.
    bsdf = next(
        (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
        None,
    )
    if bsdf is not None:
        if bsdf.inputs.get("Base Color"):
            bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        if bsdf.inputs.get("Roughness"):
            bsdf.inputs["Roughness"].default_value = roughness
        if bsdf.inputs.get("Metallic"):
            bsdf.inputs["Metallic"].default_value = metallic
    return material


def add_ico(name, parent, collection, location, scale, material, subdivisions=2, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1.0, location=(0, 0, 0))
    obj = bpy.context.object
    obj.name = name
    move_to_collection(obj, collection)
    set_parent_local(obj, parent, location)
    obj.scale = scale
    obj.rotation_euler = rotation
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    return obj


def apply_scale(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)


def add_segment(name, parent, collection, start, end, start_radius, end_radius, material, sides=7):
    start_v = Vector(start)
    end_v = Vector(end)
    direction = end_v - start_v
    length = direction.length
    midpoint = (start_v + end_v) * 0.5

    bpy.ops.mesh.primitive_cone_add(
        vertices=sides,
        radius1=start_radius,
        radius2=end_radius,
        depth=length,
        end_fill_type="NGON",
        location=(0, 0, 0),
    )
    obj = bpy.context.object
    obj.name = name
    move_to_collection(obj, collection)
    set_parent_local(obj, parent, midpoint)
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = direction.to_track_quat("Z", "Y")
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    return obj


def add_ear(name, parent, collection, location, width, height, depth, material, tilt=0.0):
    half_width = width * 0.5
    half_depth = depth * 0.5
    vertices = [
        (-half_width, -half_depth, 0.0),
        (half_width, -half_depth, 0.0),
        (0.0, -half_depth * 0.55, height),
        (-half_width, half_depth, 0.0),
        (half_width, half_depth, 0.0),
        (0.0, half_depth * 0.55, height),
    ]
    faces = [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    set_parent_local(obj, parent, location)
    obj.rotation_euler[1] = tilt
    obj.data.materials.append(material)
    return obj


def add_poly_tube(name, parent, collection, points, radii, dark_material, light_material, sides=7, light_start=0.68):
    points = [Vector(point) for point in points]
    # Keep the mesh origin at the first point so Unity can wag the complete
    # low-poly tail around the place where it joins the body.
    origin = points[0].copy()
    vertices = []
    faces = []
    material_indices = []

    for index, point in enumerate(points):
        if index == 0:
            tangent = (points[1] - points[0]).normalized()
        elif index == len(points) - 1:
            tangent = (points[-1] - points[-2]).normalized()
        else:
            tangent = (points[index + 1] - points[index - 1]).normalized()

        reference = Vector((0, 0, 1))
        if abs(tangent.dot(reference)) > 0.9:
            reference = Vector((1, 0, 0))
        axis_a = tangent.cross(reference).normalized()
        axis_b = tangent.cross(axis_a).normalized()

        for side in range(sides):
            angle = math.tau * side / sides
            radial = axis_a * math.cos(angle) + axis_b * math.sin(angle)
            vertices.append(tuple(point - origin + radial * radii[index]))

    for ring in range(len(points) - 1):
        ratio = ring / max(1, len(points) - 2)
        for side in range(sides):
            next_side = (side + 1) % sides
            a = ring * sides + side
            b = ring * sides + next_side
            c = (ring + 1) * sides + next_side
            d = (ring + 1) * sides + side
            faces.append((a, b, c, d))
            material_indices.append(1 if ratio >= light_start else 0)

    faces.append(tuple(reversed(range(sides))))
    material_indices.append(0)
    last_ring = (len(points) - 1) * sides
    faces.append(tuple(last_ring + side for side in range(sides)))
    material_indices.append(1)

    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    set_parent_local(obj, parent, origin)
    obj.data.materials.append(dark_material)
    obj.data.materials.append(light_material)
    for polygon, material_index in zip(obj.data.polygons, material_indices):
        polygon.material_index = material_index
        polygon.use_smooth = False
    return obj


def add_collar(name, parent, collection, location, scale, material):
    bpy.ops.mesh.primitive_torus_add(
        align="WORLD",
        major_segments=12,
        minor_segments=4,
        location=(0, 0, 0),
        major_radius=0.43,
        minor_radius=0.052,
    )
    collar = bpy.context.object
    collar.name = name
    move_to_collection(collar, collection)
    set_parent_local(collar, parent, location)
    collar.scale = scale
    collar.rotation_euler[0] = math.radians(7.0)
    collar.data.materials.append(material)
    for polygon in collar.data.polygons:
        polygon.use_smooth = False
    return collar


def add_eye(name, parent, collection, x, eye_material):
    # Prototype eye: one small matte oval placed directly on the face.
    # There is deliberately no socket, eye shadow, iris, pupil, or highlight.
    add_ico(
        name,
        parent,
        collection,
        (x, -1.307, 2.025),
        (0.046, 0.008, 0.032),
        eye_material,
        2,
    )


def add_leg(name, parent, collection, x, y, spec, palette, front=True):
    if front:
        hip = (x, y, 1.18)
        knee = (x, y - 0.015, 0.72)
        ankle = (x, y - 0.025, 0.25)
        add_segment(name + "_Upper", parent, collection, hip, knee, 0.145 * spec["leg_bulk"], 0.115, palette["mid"], 7)
        add_segment(name + "_Lower", parent, collection, knee, ankle, 0.112, 0.090, palette["cream"], 7)
        paw_y = y - 0.11
    else:
        hip = (x, y, 1.08)
        knee = (x, y + 0.18, 0.70)
        ankle = (x, y + 0.06, 0.25)
        add_ico(name + "_Thigh", parent, collection, (x, y, 0.88), (0.24 * spec["leg_bulk"], 0.32, 0.36), palette["mid"], 1)
        add_segment(name + "_Shin", parent, collection, knee, ankle, 0.120, 0.083, palette["cream"], 7)
        paw_y = y - 0.02

    add_ico(name + "_Paw", parent, collection, (x, paw_y, 0.16), (0.15, 0.24, 0.095), palette["cream"], 1)


def build_dog(name, root_location, spec, palette, common):
    collection = ensure_collection(name)
    root = bpy.data.objects.new(name + "_ROOT", None)
    collection.objects.link(root)
    root.location = root_location

    leg_x = spec["leg_spacing"]
    for side, sign in (("L", -1), ("R", 1)):
        add_leg(name + "_Front_" + side, root, collection, leg_x * sign, -0.40, spec, palette, True)
        add_leg(name + "_Rear_" + side, root, collection, leg_x * 1.04 * sign, 0.66, spec, palette, False)

    # Long, athletic torso and angular saddle marking.
    add_ico(
        name + "_Torso",
        root,
        collection,
        (0.0, 0.15, 1.23),
        (0.55 * spec["bulk"], 1.06, 0.53 * spec["bulk"]),
        palette["mid"],
        2,
    )
    add_ico(
        name + "_Saddle",
        root,
        collection,
        (0.0, 0.20, 1.49),
        (0.53 * spec["bulk"], 0.98, 0.30),
        palette["dark"],
        2,
    )
    add_ico(
        name + "_Belly",
        root,
        collection,
        (0.0, -0.03, 1.05),
        (0.39 * spec["bulk"], 0.78, 0.30),
        palette["cream"],
        1,
    )

    # Deep chest and neck keep the dog recognizable as a working sled breed.
    add_ico(
        name + "_Chest",
        root,
        collection,
        (0.0, -0.57, 1.30),
        (0.45 * spec["chest"], 0.49, 0.65 * spec["chest"]),
        palette["mid"],
        2,
    )
    add_ico(
        name + "_ChestWhite",
        root,
        collection,
        (0.0, -0.91, 1.24),
        (0.31 * spec["chest"], 0.17, 0.52 * spec["chest"]),
        palette["cream"],
        2,
    )
    add_ico(
        name + "_Neck",
        root,
        collection,
        (0.0, -0.70, 1.66),
        (0.40 * spec["neck"], 0.43, 0.49 * spec["neck"]),
        palette["dark"],
        2,
    )

    # Smaller head, long muzzle, and upright ears match a husky rather than a toy.
    head = add_ico(
        name + "_Head",
        root,
        collection,
        (0.0, -0.86, 1.99),
        (0.43 * spec["head"], 0.46, 0.40 * spec["head"]),
        palette["dark"],
        2,
    )
    apply_scale(head)
    add_ico(
        name + "_LowerFaceMask",
        root,
        collection,
        (0.0, -1.17, 1.81),
        (0.33 * spec["head"], 0.20, 0.19),
        palette["cream"],
        2,
    )
    add_ico(
        name + "_ForeheadBlaze",
        root,
        collection,
        (0.0, -1.235, 2.09),
        (0.115, 0.055, 0.22),
        palette["cream"],
        1,
    )
    add_ico(
        name + "_Muzzle",
        root,
        collection,
        (0.0, -1.40, 1.81),
        (0.27 * spec["muzzle"], 0.34, 0.17 * spec["muzzle"]),
        palette["cream"],
        2,
    )
    add_ico(
        name + "_Nose",
        root,
        collection,
        (0.0, -1.70, 1.84),
        (0.115 * spec["muzzle"], 0.075, 0.085),
        common["black"],
        1,
    )
    add_ico(
        name + "_Mouth",
        root,
        collection,
        (0.0, -1.735, 1.745),
        (0.095, 0.020, 0.026),
        common["black"],
        1,
    )

    eye_x = 0.17 * spec["eye_spacing"]
    add_eye(name + "_Eye_L", root, collection, -eye_x, palette["eye"])
    add_eye(name + "_Eye_R", root, collection, eye_x, palette["eye"])

    ear_x = 0.255 * spec["head"]
    for side, sign in (("L", -1), ("R", 1)):
        tilt = math.radians(sign * spec["ear_tilt"])
        add_ear(
            name + "_Ear_" + side,
            root,
            collection,
            (ear_x * sign, -0.87, 2.25),
            0.22 * spec["ear"],
            0.41 * spec["ear"],
            0.15,
            palette["dark"],
            tilt,
        )
        add_ear(
            name + "_InnerEar_" + side,
            root,
            collection,
            (ear_x * sign, -0.955, 2.285),
            0.105 * spec["ear"],
            0.275 * spec["ear"],
            0.025,
            common["ear_pink"],
            tilt,
        )

    add_collar(
        name + "_BlueCollar",
        root,
        collection,
        (0.0, -0.69, 1.64),
        (1.0 * spec["neck"], 0.90, 1.0),
        common["blue"],
    )

    tail_side = spec["tail_side"]
    tail_points = [
        (0.0, 1.12, 1.39),
        (0.08 * tail_side, 1.39, 1.49),
        (0.15 * tail_side, 1.58, 1.67),
        (0.18 * tail_side, 1.61, 1.88),
        (0.13 * tail_side, 1.53, 2.07),
        (0.06 * tail_side, 1.42, 2.16),
    ]
    tail_radii = [0.14, 0.135, 0.12, 0.10, 0.075, 0.035]
    add_poly_tube(
        name + "_Tail",
        root,
        collection,
        tail_points,
        [radius * spec["tail"] for radius in tail_radii],
        palette["dark"],
        palette["cream"],
        7,
        0.68,
    )

    return root


def look_at(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def add_area_light(name, location, energy, size, color, target):
    bpy.ops.object.light_add(type="AREA", location=location)
    light = bpy.context.object
    light.name = name
    light.data.energy = energy
    light.data.shape = "DISK"
    light.data.size = size
    light.data.color = color
    look_at(light, target)


def setup_preview(common):
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = PREVIEW_PATH
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass

    world = scene.world or bpy.data.worlds.new("LowPoly Studio World")
    scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.82, 0.84, 0.88, 1.0)
    background.inputs["Strength"].default_value = 0.18

    bpy.ops.mesh.primitive_plane_add(size=18.0, location=(0, 0, 0.05))
    ground = bpy.context.object
    ground.name = "Preview_Ground"
    ground.data.materials.append(common["ground"])

    bpy.ops.object.camera_add(location=(4.8, -10.7, 3.25))
    camera = bpy.context.object
    camera.name = "Preview_Camera"
    camera.data.lens = 66.0
    look_at(camera, (0.0, 0.0, 1.18))
    scene.camera = camera

    add_area_light("Key", (-4.2, -5.2, 6.3), 260, 4.5, (1.0, 0.90, 0.80), (0, 0, 1.2))
    add_area_light("Fill", (4.8, -4.0, 4.5), 150, 4.0, (0.75, 0.86, 1.0), (0, 0, 1.2))
    add_area_light("Rim", (0.0, 4.2, 5.8), 220, 3.5, (0.82, 0.90, 1.0), (0, 0.3, 1.4))


def recursive_children(root):
    children = []
    stack = list(root.children)
    while stack:
        child = stack.pop()
        children.append(child)
        stack.extend(child.children)
    return children


def export_root(root, filepath):
    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for child in recursive_children(root):
        if child.type in {"EMPTY", "MESH"}:
            child.select_set(True)
    bpy.context.view_layer.objects.active = root
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


def make_palettes():
    common = {
        "black": make_material("LP_Black", (0.018, 0.020, 0.024), 0.38),
        "white": make_material("LP_EyeGlint", (0.98, 0.99, 1.0), 0.25),
        "ear_pink": make_material("LP_InnerEar", (0.63, 0.25, 0.25), 0.80),
        "blue": make_material("LP_CollarBlue", (0.025, 0.18, 0.72), 0.42),
        "ground": make_material("LP_Ground", (0.72, 0.75, 0.80), 0.95),
    }
    husky = {
        "dark": make_material("LP_HuskyDark", (0.075, 0.085, 0.105), 0.86),
        "mid": make_material("LP_HuskyGray", (0.30, 0.33, 0.37), 0.88),
        "cream": make_material("LP_HuskyWhite", (0.84, 0.86, 0.84), 0.91),
        "eye": make_material("LP_HuskyIceBlueEye", (0.10, 0.42, 0.82), 0.72),
    }
    malamute = {
        "dark": make_material("LP_MalamuteDark", (0.11, 0.095, 0.085), 0.87),
        "mid": make_material("LP_MalamuteGray", (0.36, 0.33, 0.30), 0.89),
        "cream": make_material("LP_MalamuteCream", (0.80, 0.76, 0.67), 0.92),
        "eye": make_material("LP_MalamuteAmberEye", (0.62, 0.25, 0.035), 0.75),
    }
    return common, husky, malamute


HUSKY_SPEC = {
    "bulk": 0.94,
    "chest": 0.96,
    "neck": 0.95,
    "head": 0.96,
    "muzzle": 0.96,
    "eye_spacing": 1.00,
    "ear": 1.05,
    "ear_tilt": 4.0,
    "leg_spacing": 0.31,
    "leg_bulk": 0.95,
    "tail": 1.00,
    "tail_side": 1.0,
}

MALAMUTE_SPEC = {
    "bulk": 1.10,
    "chest": 1.10,
    "neck": 1.08,
    "head": 1.08,
    "muzzle": 1.05,
    "eye_spacing": 1.07,
    "ear": 0.94,
    "ear_tilt": 7.0,
    "leg_spacing": 0.36,
    "leg_bulk": 1.12,
    "tail": 1.12,
    "tail_side": -1.0,
}


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    clear_scene()
    common, husky_palette, malamute_palette = make_palettes()

    husky_root = build_dog("Husky", (-1.40, 0.0, 0.0), HUSKY_SPEC, husky_palette, common)
    malamute_root = build_dog("Malamute", (1.42, 0.15, 0.0), MALAMUTE_SPEC, malamute_palette, common)
    setup_preview(common)

    if RENDER_PREVIEW:
        bpy.context.scene.render.filepath = PREVIEW_PATH
        bpy.ops.render.render(write_still=True)
        print("Rendered:", PREVIEW_PATH)

    if EXPORT_FBX:
        export_root(husky_root, os.path.join(OUTPUT_DIR, "Mush_LowPoly_Husky.fbx"))
        export_root(malamute_root, os.path.join(OUTPUT_DIR, "Mush_LowPoly_Malamute.fbx"))

    if SAVE_BLEND:
        bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
        print("Saved:", BLEND_PATH)

    bpy.ops.object.select_all(action="DESELECT")
    husky_root.select_set(True)
    bpy.context.view_layer.objects.active = husky_root
    print("Mush low-poly sled dogs generated successfully.")


if __name__ == "__main__":
    main()
