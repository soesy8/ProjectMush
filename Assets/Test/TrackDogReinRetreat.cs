using System.Collections.Generic;
using UnityEngine;

namespace Mush.Testing
{
    /// <summary>Reversible visual-only retreat of the dogs on the inside of a turn.</summary>
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
                dog.appliedOffset = Vector3.zero;
            }
        }

        public void Apply(float steering, bool running, float maximumDistance,
            float retreatSeconds, float returnSeconds, float deltaTime)
        {
            // The bootstrap owns gait and grounding. Strip the previous visual
            // offset before it updates, then add only this frame's offset.
            RemovePreviousOffsets();
            foreach (DogState dog in dogs)
            {
                if (dog.visual == null || dog.visual.parent == null || dog.member == null) continue;
                float target = running && steering * dog.side > 0f
                    ? maximumDistance * Mathf.Clamp01(Mathf.Abs(steering)) : 0f;
                float response = target > dog.distance ? retreatSeconds : returnSeconds;
                dog.distance = Mathf.SmoothDamp(dog.distance, target, ref dog.velocity,
                    Mathf.Max(0.03f, response), Mathf.Infinity, deltaTime);
                if (target == 0f && dog.distance < 0.0005f && Mathf.Abs(dog.velocity) < 0.005f)
                    dog.distance = dog.velocity = 0f;
                if (dog.distance <= 0f) continue;

                Vector3 offset = -motionRoot.forward * dog.distance;
                Vector3 sourcePosition = dog.member.transform.position;
                if (course != null && course.TryGetCourseSurface(sourcePosition,
                        out Vector3 sourceSurface, out _, out _, out _) &&
                    course.TryGetCourseSurface(sourcePosition + offset,
                        out Vector3 shiftedSurface, out _, out _, out _))
                    offset.y += shiftedSurface.y - sourceSurface.y;

                // The imported model has its own axis correction; translate in
                // the actual travel direction and leave its animation intact.
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
