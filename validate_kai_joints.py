import bpy
import json
import math
import os
import sys
from mathutils import Vector


output_path = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene
armature = bpy.data.objects["KAI_Rig"]
frames = list(range(1, 37))


def world_point(point):
    return armature.matrix_world @ point


def internal_angle(a, joint, b):
    first = (a - joint).normalized()
    second = (b - joint).normalized()
    return math.degrees(first.angle(second))


def signed_distance_yz(point, line_a, line_b):
    # Signed 2D distance in the sagittal Y/Z plane.
    ay, az = line_a.y, line_a.z
    by, bz = line_b.y, line_b.z
    py, pz = point.y, point.z
    dy, dz = by - ay, bz - az
    denominator = math.sqrt(dy * dy + dz * dz)
    if denominator < 1.0e-9:
        return 0.0
    return (dy * (pz - az) - dz * (py - ay)) / denominator


report = {
    "file": bpy.data.filepath,
    "forward_axis": "-Y",
    "joint_definitions": {
        "front_elbow": "front_thigh tail / front_shin head",
        "front_carpus": "front_shin tail / front_foot head",
        "hind_knee_stifle": "thigh tail / shin head",
        "hind_hock_ankle": "shin tail / foot head",
    },
    "constraints": {},
    "frames": {},
}

for bone_name in ("front_foot.L", "front_foot.R", "shin.L", "shin.R", "foot.L", "foot.R"):
    bone = armature.pose.bones[bone_name]
    report["constraints"][bone_name] = []
    for constraint in bone.constraints:
        report["constraints"][bone_name].append({
            "name": constraint.name,
            "type": constraint.type,
            "target": getattr(getattr(constraint, "target", None), "name", None),
            "subtarget": getattr(constraint, "subtarget", ""),
            "chain_count": getattr(constraint, "chain_count", None),
            "pole_target": getattr(getattr(constraint, "pole_target", None), "name", None),
            "pole_subtarget": getattr(constraint, "pole_subtarget", ""),
            "pole_angle_degrees": math.degrees(getattr(constraint, "pole_angle", 0.0)),
            "influence": constraint.influence,
        })

for frame in frames:
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    frame_report = {"front": {}, "hind": {}}
    for side in ("L", "R"):
        upper = armature.pose.bones[f"front_thigh.{side}"]
        lower = armature.pose.bones[f"front_shin.{side}"]
        pastern = armature.pose.bones[f"front_foot.{side}"]
        shoulder = world_point(upper.head)
        elbow = world_point(upper.tail)
        carpus = world_point(lower.tail)
        paw = world_point(pastern.tail)
        frame_report["front"][side] = {
            "shoulder": [round(v, 6) for v in shoulder],
            "elbow": [round(v, 6) for v in elbow],
            "carpus": [round(v, 6) for v in carpus],
            "paw": [round(v, 6) for v in paw],
            "elbow_internal_angle_deg": round(internal_angle(shoulder, elbow, carpus), 3),
            "carpus_internal_angle_deg": round(internal_angle(elbow, carpus, paw), 3),
            "elbow_sagittal_offset": round(signed_distance_yz(elbow, shoulder, carpus), 6),
            "carpus_sagittal_offset": round(signed_distance_yz(carpus, elbow, paw), 6),
        }

        thigh = armature.pose.bones[f"thigh.{side}"]
        shin = armature.pose.bones[f"shin.{side}"]
        foot = armature.pose.bones[f"foot.{side}"]
        hip = world_point(thigh.head)
        knee = world_point(thigh.tail)
        hock = world_point(shin.tail)
        hind_paw = world_point(foot.tail)
        frame_report["hind"][side] = {
            "hip": [round(v, 6) for v in hip],
            "knee": [round(v, 6) for v in knee],
            "hock": [round(v, 6) for v in hock],
            "paw": [round(v, 6) for v in hind_paw],
            "knee_internal_angle_deg": round(internal_angle(hip, knee, hock), 3),
            "hock_internal_angle_deg": round(internal_angle(knee, hock, hind_paw), 3),
            "knee_sagittal_offset": round(signed_distance_yz(knee, hip, hock), 6),
            "hock_sagittal_offset": round(signed_distance_yz(hock, knee, hind_paw), 6),
        }
    report["frames"][str(frame)] = frame_report

with open(output_path, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)
print(f"JOINT_REPORT_WRITTEN={output_path}")
