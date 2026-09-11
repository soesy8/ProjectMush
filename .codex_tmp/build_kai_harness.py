import bpy
import math
import os
import sys
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree


args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
if not args:
    raise RuntimeError("Output .blend path is required")
output_path = os.path.abspath(args[0])

scene = bpy.context.scene
scene.frame_set(scene.frame_current)
body = bpy.data.objects["KAI_Model"]
armature = bpy.data.objects["KAI_Rig"]

# Re-running the script remains deterministic.
old_collection = bpy.data.collections.get("KAI_Harness_Modeling")
if old_collection:
    for old_obj in list(old_collection.objects):
        bpy.data.objects.remove(old_obj, do_unlink=True)
    bpy.data.collections.remove(old_collection)

collection = bpy.data.collections.new("KAI_Harness_Modeling")
scene.collection.children.link(collection)


def flat_material(name, color, metallic=0.0, roughness=0.55):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (*color, 1.0)
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    return material


# Plain preview materials only: no images, texture nodes, UV maps, or painted data.
mat_padding = flat_material("HRN_Preview_DarkPadding", (0.025, 0.03, 0.035), 0.0, 0.72)
mat_webbing = flat_material("HRN_Preview_OrangeWebbing", (1.0, 0.16, 0.015), 0.0, 0.48)
mat_hardware = flat_material("HRN_Preview_GoldHardware", (0.95, 0.55, 0.055), 0.55, 0.28)


# Build a nearest-vertex skinning sampler from the currently evaluated dog surface.
depsgraph = bpy.context.evaluated_depsgraph_get()
eval_body = body.evaluated_get(depsgraph)
eval_mesh = eval_body.to_mesh()
evaluated_world = [eval_body.matrix_world @ vertex.co for vertex in eval_mesh.vertices]
kdtree = KDTree(len(evaluated_world))
for index, coordinate in enumerate(evaluated_world):
    kdtree.insert(coordinate, index)
kdtree.balance()
group_names = {group.index: group.name for group in body.vertex_groups}


def skin_sample(world_coordinate):
    _, source_index, _ = kdtree.find(world_coordinate)
    source_vertex = body.data.vertices[source_index]
    weights = []
    total = 0.0
    for membership in source_vertex.groups:
        bone_name = group_names.get(membership.group)
        pose_bone = armature.pose.bones.get(bone_name) if bone_name else None
        if pose_bone and pose_bone.bone.use_deform and membership.weight > 1e-6:
            weights.append((bone_name, float(membership.weight)))
            total += float(membership.weight)
    if total <= 1e-8:
        return body.matrix_world.inverted() @ world_coordinate, []

    zero = Matrix(((0.0, 0.0, 0.0, 0.0),) * 4)
    deform_matrix = zero.copy()
    normalized = []
    for bone_name, weight in weights:
        normalized_weight = weight / total
        pose_bone = armature.pose.bones[bone_name]
        bone_matrix = (
            armature.matrix_world
            @ pose_bone.matrix
            @ pose_bone.bone.matrix_local.inverted()
            @ armature.matrix_world.inverted()
            @ body.matrix_world
        )
        deform_matrix = deform_matrix + bone_matrix * normalized_weight
        normalized.append((bone_name, normalized_weight))
    rest_coordinate = deform_matrix.inverted_safe() @ world_coordinate
    return rest_coordinate, normalized


def create_skinned_mesh(name, world_vertices, faces, material, bevel_world=0.0022):
    rest_vertices = []
    all_weights = []
    for coordinate in world_vertices:
        rest, weights = skin_sample(Vector(coordinate))
        rest_vertices.append(tuple(rest))
        all_weights.append(weights)

    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(rest_vertices, [], faces)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.matrix_world = body.matrix_world.copy()
    obj.parent = armature
    obj.matrix_parent_inverse = armature.matrix_world.inverted()
    obj.data.materials.append(material)

    vertex_groups = {}
    for vertex_index, weights in enumerate(all_weights):
        for bone_name, weight in weights:
            group = vertex_groups.get(bone_name)
            if group is None:
                group = obj.vertex_groups.new(name=bone_name)
                vertex_groups[bone_name] = group
            group.add([vertex_index], weight, "REPLACE")

    armature_modifier = obj.modifiers.new("Follow KAI Rig", "ARMATURE")
    armature_modifier.object = armature
    armature_modifier.use_deform_preserve_volume = False

    if bevel_world > 0:
        scale = abs(body.matrix_world.to_scale().x)
        bevel = obj.modifiers.new("Modeled Edge Softening", "BEVEL")
        bevel.width = bevel_world / scale
        bevel.segments = 1
        bevel.limit_method = "ANGLE"
        bevel.angle_limit = math.radians(25.0)
    return obj


def strap_mesh(name, centers, width_axes, outward_axes, width, thickness, material, closed=False, bevel=0.0022):
    centers = [Vector(value) for value in centers]
    width_axes = [Vector(value).normalized() for value in width_axes]
    outward_axes = [Vector(value).normalized() for value in outward_axes]
    vertices = []
    for center, width_axis, outward in zip(centers, width_axes, outward_axes):
        half_width = width * 0.5
        half_thickness = thickness * 0.5
        vertices.extend([
            center + width_axis * half_width + outward * half_thickness,
            center - width_axis * half_width + outward * half_thickness,
            center - width_axis * half_width - outward * half_thickness,
            center + width_axis * half_width - outward * half_thickness,
        ])
    faces = []
    segment_count = len(centers) if closed else len(centers) - 1
    for segment in range(segment_count):
        current = segment
        following = (segment + 1) % len(centers)
        for corner in range(4):
            next_corner = (corner + 1) % 4
            faces.append((current * 4 + corner, following * 4 + corner,
                          following * 4 + next_corner, current * 4 + next_corner))
    if not closed:
        faces.append((0, 3, 2, 1))
        last = (len(centers) - 1) * 4
        faces.append((last, last + 1, last + 2, last + 3))
    return create_skinned_mesh(name, vertices, faces, material, bevel)


def poly_prism(name, center, radius, depth, sides, material):
    center = Vector(center)
    vertices = []
    # Axis is Y, and the front face points toward -Y.
    for y_offset in (-depth * 0.5, depth * 0.5):
        for i in range(sides):
            angle = (2.0 * math.pi * i / sides) + math.pi / 8.0
            vertices.append(center + Vector((radius * math.cos(angle), y_offset, radius * math.sin(angle))))
    faces = []
    faces.append(tuple(range(sides - 1, -1, -1)))
    faces.append(tuple(range(sides, sides * 2)))
    for i in range(sides):
        j = (i + 1) % sides
        faces.append((i, j, sides + j, sides + i))
    return create_skinned_mesh(name, vertices, faces, material, bevel_world=0.003)


def box_mesh(name, center, size, material, bevel=0.002):
    center = Vector(center)
    sx, sy, sz = (value * 0.5 for value in size)
    vertices = [
        center + Vector((x, y, z))
        for x, y, z in [
            (-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
            (-sx, -sy, sz), (sx, -sy, sz), (sx, sy, sz), (-sx, sy, sz),
        ]
    ]
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1),
             (1, 5, 6, 2), (2, 6, 7, 3), (4, 0, 3, 7)]
    return create_skinned_mesh(name, vertices, faces, material, bevel)


def d_ring_mesh(name, outer_yz, inner_yz, x_depth, material):
    vertices = []
    for x in (-x_depth * 0.5, x_depth * 0.5):
        for y, z in outer_yz:
            vertices.append(Vector((x, y, z)))
        for y, z in inner_yz:
            vertices.append(Vector((x, y, z)))
    n = len(outer_yz)
    faces = []
    front_outer = 0
    front_inner = n
    back_outer = 2 * n
    back_inner = 3 * n
    for i in range(n):
        j = (i + 1) % n
        faces.extend([
            (front_outer + i, front_outer + j, front_inner + j, front_inner + i),
            (back_outer + i, back_inner + i, back_inner + j, back_outer + j),
            (front_outer + i, back_outer + i, back_outer + j, front_outer + j),
            (front_inner + i, front_inner + j, back_inner + j, back_inner + i),
        ])
    return create_skinned_mesh(name, vertices, faces, material, bevel_world=0.002)


# Neck loop: a faceted path with a deliberate front V, then a rounded back section.
neck_center = Vector((0.0, -0.265, 0.760))
neck_v = Vector((0.0, 0.177, 0.127))
neck_v_dir = neck_v.normalized()
neck_width_axis = Vector((1.0, 0.0, 0.0)).cross(neck_v_dir).normalized()
neck_anchors = [
    Vector((0.000, -0.454, 0.668)),
    Vector((0.105, -0.405, 0.718)),
    Vector((0.205, -0.272, 0.778)),
    Vector((0.150, -0.145, 0.848)),
    Vector((0.000, -0.088, 0.895)),
    Vector((-0.150, -0.145, 0.848)),
    Vector((-0.205, -0.272, 0.778)),
    Vector((-0.105, -0.405, 0.718)),
]
neck_centers = []
for anchor_index, anchor in enumerate(neck_anchors):
    following = neck_anchors[(anchor_index + 1) % len(neck_anchors)]
    for step in range(3):
        neck_centers.append(anchor.lerp(following, step / 3.0))
neck_outward = [(center - neck_center).normalized() for center in neck_centers]
neck_width_axes = [neck_width_axis] * len(neck_centers)
strap_mesh("HRN_NeckPadding", neck_centers, neck_width_axes, neck_outward,
           0.064, 0.014, mat_padding, closed=True, bevel=0.0025)
strap_mesh("HRN_NeckWebbing", [c + n * 0.009 for c, n in zip(neck_centers, neck_outward)],
           neck_width_axes, neck_outward, 0.046, 0.010, mat_webbing, closed=True, bevel=0.002)

# Rib-cage/girth loop immediately behind the forelegs.
girth_center = Vector((0.0, 0.105, 0.670))
girth_centers = []
girth_outward = []
girth_width_axes = []
for i in range(24):
    angle = 2.0 * math.pi * i / 24.0
    radial = Vector((math.cos(angle), 0.0, math.sin(angle))).normalized()
    girth_centers.append(girth_center + Vector((0.184 * math.cos(angle), 0.0, 0.185 * math.sin(angle))))
    girth_outward.append(radial)
    girth_width_axes.append(Vector((0.0, 1.0, 0.0)))
strap_mesh("HRN_GirthPadding", girth_centers, girth_width_axes, girth_outward,
           0.068, 0.014, mat_padding, closed=True, bevel=0.0025)
strap_mesh("HRN_GirthWebbing", [c + n * 0.009 for c, n in zip(girth_centers, girth_outward)],
           girth_width_axes, girth_outward, 0.048, 0.010, mat_webbing, closed=True, bevel=0.002)

# Back bridge between neck and girth loops.
back_centers = [
    Vector((0.0, -0.090, 0.896)),
    Vector((0.0, -0.050, 0.883)),
    Vector((0.0, 0.000, 0.870)),
    Vector((0.0, 0.055, 0.861)),
    Vector((0.0, 0.105, 0.864)),
]
back_width_axes = [Vector((1.0, 0.0, 0.0))] * len(back_centers)
back_outward = [Vector((0.0, 0.0, 1.0))] * len(back_centers)
strap_mesh("HRN_BackConnectorPadding", back_centers, back_width_axes, back_outward,
           0.064, 0.014, mat_padding, closed=False, bevel=0.0025)
strap_mesh("HRN_BackConnectorWebbing", [c + Vector((0.0, 0.0, 0.009)) for c in back_centers],
           back_width_axes, back_outward, 0.046, 0.010, mat_webbing, closed=False, bevel=0.002)

# Sternum bridge: visible on the front, then curves under the chest to the girth loop.
chest_centers = [
    Vector((0.0, -0.454, 0.665)),
    Vector((0.0, -0.456, 0.605)),
    Vector((0.0, -0.433, 0.545)),
    Vector((0.0, -0.370, 0.492)),
    Vector((0.0, -0.275, 0.458)),
    Vector((0.0, -0.150, 0.448)),
    Vector((0.0, -0.020, 0.458)),
    Vector((0.0, 0.105, 0.480)),
]
chest_width_axes = [Vector((1.0, 0.0, 0.0))] * len(chest_centers)
chest_outward = []
for i, center in enumerate(chest_centers):
    if i == 0:
        tangent = chest_centers[1] - center
    elif i == len(chest_centers) - 1:
        tangent = center - chest_centers[i - 1]
    else:
        tangent = chest_centers[i + 1] - chest_centers[i - 1]
    outward = tangent.normalized().cross(Vector((1.0, 0.0, 0.0))).normalized()
    chest_outward.append(outward)
strap_mesh("HRN_ChestConnectorPadding", chest_centers, chest_width_axes, chest_outward,
           0.064, 0.014, mat_padding, closed=False, bevel=0.0025)
strap_mesh("HRN_ChestConnectorWebbing", [c + n * 0.009 for c, n in zip(chest_centers, chest_outward)],
           chest_width_axes, chest_outward, 0.044, 0.010, mat_webbing, closed=False, bevel=0.002)

# Layered low-poly chest medallion/buckle.
poly_prism("HRN_BuckleOuter", (0.0, -0.476, 0.668), 0.056, 0.022, 8, mat_hardware)
poly_prism("HRN_BuckleFace", (0.0, -0.491, 0.668), 0.041, 0.014, 8, mat_hardware)

# Reinforcement tab and upright lead attachment ring on the back.
box_mesh("HRN_BackReinforcement", (0.0, 0.105, 0.882), (0.070, 0.062, 0.020), mat_webbing, bevel=0.0025)
outer = [
    (0.071, 0.883), (0.139, 0.883), (0.139, 0.920),
    (0.129, 0.939), (0.105, 0.947), (0.081, 0.939), (0.071, 0.920),
]
inner = [
    (0.087, 0.895), (0.123, 0.895), (0.123, 0.916),
    (0.116, 0.926), (0.105, 0.931), (0.094, 0.926), (0.087, 0.916),
]
d_ring_mesh("HRN_LeadRing", outer, inner, 0.014, mat_hardware)

# Helpful custom properties for downstream artists/tools.
collection["asset_role"] = "separate_harness_model"
collection["reference_style"] = "Siberian Husky orange Y-harness"
collection["texture_work"] = "none"
collection["rig_binding"] = "armature weights sampled from KAI_Model at build pose"
for obj in collection.objects:
    obj["part_of"] = "KAI_Harness_Modeling"
    obj["has_texture_maps"] = False

eval_body.to_mesh_clear()

# Select the harness collection content for convenient handoff in Blender.
bpy.ops.object.select_all(action="DESELECT")
for obj in collection.objects:
    obj.select_set(True)
if collection.objects:
    bpy.context.view_layer.objects.active = collection.objects.get("HRN_NeckWebbing") or collection.objects[0]

os.makedirs(os.path.dirname(output_path), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=output_path, check_existing=False)
print("HARNESS_OBJECTS=" + ",".join(sorted(obj.name for obj in collection.objects)))
print("SAVED_BLEND=" + output_path)
