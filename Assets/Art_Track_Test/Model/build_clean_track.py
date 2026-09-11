import bpy
import math
from pathlib import Path


OUTPUT = Path(r"D:\project\ProjectMush\Assets\Art_Track_Test\Model\Track_SnowRoad_CleanGrid.fbx")
WIDTH = 8.0
LENGTH = 15.0
COLUMNS = 13
ROWS = 16


bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

vertices = []
uvs = []
for row in range(ROWS):
    z01 = row / (ROWS - 1)
    z = LENGTH * (0.5 - z01)
    longitudinal = math.sin(z01 * math.pi) ** 2
    for column in range(COLUMNS):
        x01 = column / (COLUMNS - 1)
        x = WIDTH * (x01 - 0.5)
        normalized_x = abs(x) / (WIDTH * 0.5)
        crown = 0.22 * max(0.0, 1.0 - normalized_x ** 1.7)
        packed_center = -0.025 * math.exp(-((abs(x) - 1.35) / 0.45) ** 2)
        subtle_surface = 0.025 * longitudinal * math.cos(x * math.pi / WIDTH)
        vertices.append((x, crown + packed_center + subtle_surface, z))
        uvs.append((x01, row / 3.0))

faces = []
face_materials = []
for row in range(ROWS - 1):
    for column in range(COLUMNS - 1):
        a = row * COLUMNS + column
        b = a + 1
        c = a + COLUMNS
        d = c + 1
        faces.append((a, c, d, b))
        center_x = ((column + 0.5) / (COLUMNS - 1) - 0.5) * WIDTH
        normalized_x = abs(center_x) / (WIDTH * 0.5)
        if 0.30 <= normalized_x < 0.45:
            face_materials.append(2)
        elif normalized_x >= 0.75:
            face_materials.append(1)
        else:
            face_materials.append(0)

mesh = bpy.data.meshes.new("SnowRoad_CleanGrid_Mesh")
mesh.from_pydata(vertices, [], faces)
mesh.update()

uv_layer = mesh.uv_layers.new(name="UVMap")
for polygon in mesh.polygons:
    for loop_index in polygon.loop_indices:
        vertex_index = mesh.loops[loop_index].vertex_index
        uv_layer.data[loop_index].uv = uvs[vertex_index]

materials = []
for name, color, roughness in (
    ("PackedSnow", (0.76, 0.86, 0.93, 1.0), 0.70),
    ("SnowField", (0.91, 0.96, 1.0, 1.0), 0.76),
    ("SledTrack", (0.50, 0.64, 0.74, 1.0), 0.82),
):
    material = bpy.data.materials.new(name)
    material.diffuse_color = color
    material.roughness = roughness
    mesh.materials.append(material)
    materials.append(material)

for polygon, material_index in zip(mesh.polygons, face_materials):
    polygon.material_index = material_index
    polygon.use_smooth = True

road = bpy.data.objects.new("SnowRoad_CleanGrid", mesh)
bpy.context.collection.objects.link(road)
road.location = (0.0, 0.0, 0.0)
road.rotation_euler = (0.0, 0.0, 0.0)
road.scale = (1.0, 1.0, 1.0)

bpy.context.view_layer.objects.active = road
road.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=str(OUTPUT),
    use_selection=True,
    object_types={"MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_NONE",
    axis_forward="-Z",
    axis_up="Y",
    mesh_smooth_type="FACE",
    use_mesh_modifiers=True,
    bake_anim=False,
    add_leaf_bones=False,
    path_mode="AUTO",
)

print(f"Created {OUTPUT}")
print(f"Grid: {COLUMNS} columns x {ROWS} rows, {len(vertices)} vertices, {len(faces)} quads")
