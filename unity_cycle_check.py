import bpy
import json
import sys


def matrices_at(frame):
    scene = bpy.context.scene
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    evaluated = rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return {
        bone.name: [value for row in evaluated.pose.bones[bone.name].matrix for value in row]
        for bone in rig.data.bones
        if bone.use_deform
    }


def maximum_delta(left, right):
    return max(
        abs(a - b)
        for name in left
        for a, b in zip(left[name], right[name])
    )


rig = next(obj for obj in bpy.data.objects if obj.type == "ARMATURE" and obj.name == "KAI_Rig")
scene = bpy.context.scene
reference_frame = scene.frame_start
reference = matrices_at(reference_frame)
action = rig.animation_data.action if rig.animation_data else None
end = int(max(scene.frame_end + 1, action.frame_range[1] if action else scene.frame_end))
comparisons = []
for frame in range(int(reference_frame) + 1, end + 1):
    comparisons.append({"frame": frame, "max_delta": maximum_delta(reference, matrices_at(frame))})

print("KAI_CYCLE_BEGIN")
print(
    json.dumps(
        {
            "file": bpy.data.filepath,
            "action": action.name if action else None,
            "scene_range": [scene.frame_start, scene.frame_end],
            "action_range": list(action.frame_range) if action else None,
            "reference_frame": reference_frame,
            "closest_frames": sorted(comparisons, key=lambda item: item["max_delta"])[:10],
            "scene_end_delta": next(
                (item["max_delta"] for item in comparisons if item["frame"] == scene.frame_end),
                None,
            ),
        },
        indent=2,
    )
)
print("KAI_CYCLE_END")
