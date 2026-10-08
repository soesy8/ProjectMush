using System.Collections.Generic;
using UnityEngine;

namespace Mush.Testing
{
    /// <summary>Reversible turn poses layered over the ride's grounding and gait.</summary>
    internal sealed class TrackDogReinRetreat
    {
        private sealed class DogState
        {
            public MushRideDog member;
            public Transform visual;
            public float side;
            public float distance;
            public float velocity;
            public Vector3 appliedOffset;
            public Quaternion appliedYaw = Quaternion.identity;
        }

        private readonly Transform motionRoot;
        private readonly MushCurvedMapRuntime course;
        private readonly List<DogState> dogs = new List<DogState>();

        public TrackDogReinRetreat(Transform root, MushCurvedMapRuntime surface)
        {
            motionRoot = root;
            course = surface;
            foreach (MushRideDog member in root.GetComponentsInChildren<MushRideDog>(false))
            {
                if (member.Visual == null) continue;
                float lateral = root.InverseTransformPoint(member.transform.position).x;
                dogs.Add(new DogState { member = member, visual = member.Visual,
                    side = Mathf.Abs(lateral) > 0.01f ? Mathf.Sign(lateral) : 0f });
            }
        }

        public void RemovePreviousOffsets()
        {
            foreach (DogState dog in dogs)
            {
                if (dog.visual != null) dog.visual.localPosition -= dog.appliedOffset;
                if (dog.member != null)
                    dog.member.transform.localRotation *= Quaternion.Inverse(dog.appliedYaw);
                dog.appliedOffset = Vector3.zero;
                dog.appliedYaw = Quaternion.identity;
            }
        }

        public void Apply(float turn, bool running, float maximumDistance,
            float maximumLateralShift, float maximumYaw,
            float retreatSeconds, float returnSeconds, float deltaTime)
        {
            // The bootstrap owns gait and grounding. Strip the previous visual
            // offset before it updates, then add only this frame's offset.
            RemovePreviousOffsets();
            foreach (DogState dog in dogs)
            {
                if (dog.visual == null || dog.visual.parent == null || dog.member == null) continue;
                float inside = running ? Mathf.Clamp01(turn * dog.side) : 0f;
                float target = maximumDistance * inside;
                float response = target > dog.distance ? retreatSeconds : returnSeconds;
                dog.distance = Mathf.SmoothDamp(dog.distance, target, ref dog.velocity,
                    Mathf.Max(0.03f, response), Mathf.Infinity, deltaTime);
                if (target == 0f && dog.distance < 0.0005f && Mathf.Abs(dog.velocity) < 0.005f)
                    dog.distance = dog.velocity = 0f;
                float turnAmount = running ? turn : 0f;
                // Rotate the unanimated holder, preserving the model's FBX axes
                // and the surface tilt already supplied by the bootstrap.
                dog.appliedYaw = Quaternion.AngleAxis(
                    turnAmount * maximumYaw * Mathf.Lerp(0.7f, 1f, inside), Vector3.up);
                dog.member.transform.localRotation *= dog.appliedYaw;
                Vector3 normal = dog.member.transform.up;
                Vector3 forward = Vector3.ProjectOnPlane(motionRoot.forward, normal).normalized;
                Vector3 right = Vector3.ProjectOnPlane(motionRoot.right, normal).normalized;
                Vector3 offset = -forward * dog.distance + right * (turnAmount * maximumLateralShift);
                Vector3 sourcePosition = dog.member.transform.position;
                if (course != null && course.TryGetCourseSurface(sourcePosition,
                        out Vector3 sourceSurface, out _, out _, out _) &&
                    course.TryGetCourseSurface(sourcePosition + offset,
                        out Vector3 shiftedSurface, out _, out _, out _))
                    offset.y += shiftedSurface.y - sourceSurface.y;

                dog.appliedOffset = dog.visual.parent.InverseTransformVector(offset);
                dog.visual.localPosition += dog.appliedOffset;
            }
        }

        public void Reset()
        {
            RemovePreviousOffsets();
            foreach (DogState dog in dogs) dog.distance = dog.velocity = 0f;
        }
    }
}
