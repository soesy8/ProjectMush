import bpy
import math
import os
import sys


args = sys.argv[sys.argv.index("--") + 1 :]
output_path = args[0]

armature = bpy.data.objects.get("KAI_Rig")
source_action = bpy.data.actions.get("KAI_Walk")
if armature is None or source_action is None:
    raise RuntimeError("Expected KAI_Rig and KAI_Walk in the source file")

scene = bpy.context.scene
source_frames = [1, 9, 17, 25, 33, 41]
pose_frames = [1, 7, 13, 19, 25, 31]
seam_frame = 37

# Sample the model's own walk rig so its existing IK layout, joint limits, and
# weighting remain the anatomical basis for the new six-pose cycle.
armature.animation_data_create()
armature.animation_data.action = source_action
if hasattr(armature.animation_data, "action_slot") and hasattr(source_action, "slots"):
    suitable_slots = [slot for slot in source_action.slots if slot.target_id_type == "OBJECT"]
    if suitable_slots:
        armature.animation_data.action_slot = suitable_slots[0]

samples = []
for source_frame in source_frames:
    scene.frame_set(source_frame)
    bpy.context.view_layer.update()
    pose = {}
    for bone in armature.pose.bones:
        pose[bone.name] = {
            "rotation_mode": bone.rotation_mode,
            "location": bone.location.copy(),
            "rotation_euler": bone.rotation_euler.copy(),
            "rotation_quaternion": bone.rotation_quaternion.copy(),
            "rotation_axis_angle": tuple(bone.rotation_axis_angle),
            "scale": bone.scale.copy(),
        }
    samples.append(pose)

# Slightly strengthen only the airborne paw lift. The adjustment is relative to
# this dog's control scale and keeps the fore/aft stride from the source rig.
lift_controls = {
    "IK_front_L",
    "IK_front_R",
    "IK_L",
    "IK_R",
    "HockTarget.L",
    "HockTarget.R",
}
for pose in samples:
    for bone_name in lift_controls:
        if bone_name in pose and pose[bone_name]["location"].z > 0.0005:
            pose[bone_name]["location"].z *= 1.12

# Identify only the controls/channels that actually change over the six sampled
# poses. Static rig settings remain as pose defaults, keeping the action clean.
animated_channels = {}
for bone in armature.pose.bones:
    bone_samples = [pose[bone.name] for pose in samples]
    channels = []
    for prop in ("location", "rotation_euler", "scale"):
        vectors = [sample[prop] for sample in bone_samples]
        if any(
            abs(vectors[index][axis] - vectors[0][axis]) > 1.0e-7
            for index in range(1, len(vectors))
            for axis in range(len(vectors[0]))
        ):
            channels.append(prop)
    if bone.rotation_mode == "QUATERNION":
        quaternions = [sample["rotation_quaternion"] for sample in bone_samples]
        if any(
            abs(quaternions[index][axis] - quaternions[0][axis]) > 1.0e-7
            for index in range(1, len(quaternions))
            for axis in range(4)
        ):
            channels = [channel for channel in channels if channel != "rotation_euler"]
            channels.append("rotation_quaternion")
    animated_channels[bone.name] = channels

# Remove animation assignments everywhere in the copy, then remove the old
# actions themselves. The source file is never saved by this script.
for obj in bpy.data.objects:
    if obj.animation_data:
        obj.animation_data_clear()
for datablock_collection in (
    bpy.data.armatures,
    bpy.data.meshes,
    bpy.data.cameras,
    bpy.data.lights,
    bpy.data.materials,
):
    for datablock in datablock_collection:
        if getattr(datablock, "animation_data", None):
            datablock.animation_data_clear()
if scene.animation_data:
    scene.animation_data_clear()
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action, do_unlink=True)

# Restore the first sampled pose as the static basis for channels that do not
# need curves, then author a fresh action at six-frame intervals.
for bone in armature.pose.bones:
    value = samples[0][bone.name]
    bone.rotation_mode = value["rotation_mode"]
    bone.location = value["location"]
    bone.scale = value["scale"]
    if bone.rotation_mode == "QUATERNION":
        bone.rotation_quaternion = value["rotation_quaternion"]
    elif bone.rotation_mode == "AXIS_ANGLE":
        bone.rotation_axis_angle = value["rotation_axis_angle"]
    else:
        bone.rotation_euler = value["rotation_euler"]

new_action = bpy.data.actions.new("KAI_Walk_36f_6Key")
armature.animation_data_create()
armature.animation_data.action = new_action

def apply_pose_and_key(pose, target_frame):
    scene.frame_set(target_frame)
    for bone_name, channels in animated_channels.items():
        if not channels:
            continue
        bone = armature.pose.bones[bone_name]
        value = pose[bone_name]
        bone.rotation_mode = value["rotation_mode"]
        for channel in channels:
            setattr(bone, channel, value[channel])
            bone.keyframe_insert(data_path=channel, frame=target_frame, group=bone_name)


for pose, target_frame in zip(samples, pose_frames):
    apply_pose_and_key(pose, target_frame)

# A duplicate of pose 1 just outside the 1-36 playback range provides the
# missing interpolation segment from pose 6 back to pose 1 without adding a
# seventh visible pose to the requested 36-frame timeline.
apply_pose_and_key(samples[0], seam_frame)

# Use restrained Bezier tangents: smooth body motion, no contact-foot overshoot.
fcurves = []
if hasattr(new_action, "fcurves"):
    fcurves = list(new_action.fcurves)
elif hasattr(new_action, "layers"):
    for layer in new_action.layers:
        for strip in layer.strips:
            for channelbag in getattr(strip, "channelbags", []):
                fcurves.extend(channelbag.fcurves)
for fcurve in fcurves:
    fcurve.extrapolation = "CONSTANT"
    for key in fcurve.keyframe_points:
        key.interpolation = "BEZIER"
        key.handle_left_type = "AUTO_CLAMPED"
        key.handle_right_type = "AUTO_CLAMPED"
        frame = int(round(key.co.x))
        if frame in (1, 19, 37):
            key.type = "EXTREME"
        else:
            key.type = "BREAKDOWN"

if hasattr(new_action, "use_frame_range"):
    new_action.use_frame_range = True
    new_action.frame_start = 1
    new_action.frame_end = 36

new_action["cycle_length_frames"] = 36
new_action["unique_pose_frames"] = "1, 7, 13, 19, 25, 31"
new_action["loop_support_frame"] = 37
new_action["reference_style"] = "Six-pose lateral dog walk; proportions adapted to KAI rig"

scene.frame_start = 1
scene.frame_end = 36
scene.use_preview_range = True
scene.frame_preview_start = 1
scene.frame_preview_end = 36
scene.frame_set(1)

scene.timeline_markers.clear()
markers = [
    ("01_Contact_A", 1),
    ("02_Weight_Shift_A", 7),
    ("03_Passing_A", 13),
    ("04_Contact_B", 19),
    ("05_Weight_Shift_B", 25),
    ("06_Passing_B", 31),
]
for marker_name, marker_frame in markers:
    scene.timeline_markers.new(marker_name, frame=marker_frame)

scene["walk_cycle_notes"] = (
    "6 unique poses at frames 1/7/13/19/25/31; 6-frame spacing; "
    "frames 1-36; seam duplicate at frame 37 outside playback range."
)
scene["walk_reference"] = "Dog_Walk_Cycle.avif (used proportionally)"

os.makedirs(os.path.dirname(output_path), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=output_path, check_existing=False)
print(f"OUTPUT_WRITTEN={output_path}")
print(f"ANIMATED_BONES={','.join(name for name, channels in animated_channels.items() if channels)}")
print(f"FCURVES={len(fcurves)}")
