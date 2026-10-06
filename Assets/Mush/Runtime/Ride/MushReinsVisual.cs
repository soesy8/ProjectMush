using System.Collections.Generic;
using UnityEngine;

namespace Mush.Prototype
{
    /// <summary>
    /// Keeps the two visual reins connected between the player's tracked hands
    /// and the harness anchors on the dogs.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(1500)]
    [DisallowMultipleComponent]
    public sealed class MushReinsVisual : MonoBehaviour
    {
        private const int PositionCount = 65;
        private const int ContactPasses = 8;
        private const float ContactResponse = 12f;
        private const float ContactFollowSpeed = 0.12f;
        private const float ContactDeadZone = 0.001f;

        [SerializeField] private Transform leftGrip;
        [SerializeField] private Transform rightGrip;
        [SerializeField] private Transform leftHarness;
        [SerializeField] private Transform rightHarness;
        [SerializeField] private LineRenderer leftRein;
        [SerializeField] private LineRenderer rightRein;
        [SerializeField] private Transform sled;
        [SerializeField] private Transform leftDog;
        [SerializeField] private Transform rightDog;
        private readonly List<ReinSurface> surfaces = new();
        private readonly Vector3[] positions = new Vector3[PositionCount];
        private readonly Vector3[] contactOffsets = new Vector3[PositionCount];
        private readonly ReinState leftState = new();
        private readonly ReinState rightState = new();

        private float leftPullDistance;
        private float rightPullDistance;
        private bool reinsHeld;

        public void Configure(
            Transform newLeftGrip,
            Transform newRightGrip,
            Transform newLeftHarness,
            Transform newRightHarness,
            LineRenderer newLeftRein,
            LineRenderer newRightRein)
        {
            if (leftGrip != newLeftGrip || leftHarness != newLeftHarness || leftRein != newLeftRein)
                leftState.Reset();
            if (rightGrip != newRightGrip || rightHarness != newRightHarness || rightRein != newRightRein)
                rightState.Reset();
            leftGrip = newLeftGrip;
            rightGrip = newRightGrip;
            leftHarness = newLeftHarness;
            rightHarness = newRightHarness;
            leftRein = newLeftRein;
            rightRein = newRightRein;
            UpdateReins();
        }

        public void SetHeld(bool held)
        {
            reinsHeld = held;
            if (leftRein != null)
                leftRein.enabled = true;
            if (rightRein != null)
                rightRein.enabled = true;
        }

        public void SetPull(float leftPull, float rightPull)
        {
            leftPullDistance = Mathf.Max(0f, leftPull);
            rightPullDistance = Mathf.Max(0f, rightPull);
        }

        public void ConfigureRouting(Transform newSled, Transform newLeftDog, Transform newRightDog)
        {
            if (leftDog != newLeftDog || sled != newSled) leftState.Reset();
            if (rightDog != newRightDog || sled != newSled) rightState.Reset();
            sled = newSled;
            leftDog = newLeftDog;
            rightDog = newRightDog;
            CacheSurfaces();
            UpdateReins();
        }

        private void OnEnable()
        {
            leftState.Reset();
            rightState.Reset();
            CacheSurfaces();
            UpdateReins();
        }

        private void OnDisable() => ClearSurfaces();

        private void CacheSurfaces()
        {
            ClearSurfaces();
            AddSurfaces(sled);
            AddSurfaces(leftDog);
            AddSurfaces(rightDog);
        }

        private void AddSurfaces(Transform model)
        {
            if (model == null) return;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(false))
            {
                if (IsAttachmentRenderer(renderer.transform, leftHarness) || IsAttachmentRenderer(renderer.transform, rightHarness))
                    continue;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                    surfaces.Add(new ReinSurface(renderer, skin, skin.sharedMesh));
                else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    surfaces.Add(new ReinSurface(renderer, null, filter.sharedMesh));
            }
        }

        private static bool IsAttachmentRenderer(Transform candidate, Transform attachment)
        {
            if (attachment == null) return false;
            if (candidate.IsChildOf(attachment)) return true;
            // The supplied harness and its connection ring are intentional contact
            // surfaces. Treating the ring itself as an obstacle pulls the rope away.
            Transform harnessRoot = attachment.parent;
            return harnessRoot != null && harnessRoot.name == "HarnessPosition" && candidate.IsChildOf(harnessRoot);
        }

        private void ClearSurfaces()
        {
            foreach (ReinSurface surface in surfaces) surface.Dispose();
            surfaces.Clear();
        }

        private void LateUpdate()
        {
            UpdateReins();
        }

        private void UpdateReins()
        {
            // Query helpers are enabled only during these synchronous visual queries.
            // They are excluded from all physical contacts and disabled before physics runs.
            float slack = reinsHeld ? 0.035f : 0.07f;
            try
            {
                foreach (ReinSurface surface in surfaces) surface.Prepare();
                UpdateRein(leftRein, leftGrip, leftHarness, leftDog, leftState, slack / (1f + leftPullDistance));
                UpdateRein(rightRein, rightGrip, rightHarness, rightDog, rightState, slack / (1f + rightPullDistance));
            }
            finally
            {
                foreach (ReinSurface surface in surfaces) surface.Finish();
            }
        }

        private void UpdateRein(
            LineRenderer rein,
            Transform grip,
            Transform harness,
            Transform dog,
            ReinState state,
            float slack)
        {
            if (rein == null || grip == null || harness == null)
            {
                state.Reset();
                return;
            }

            // The grip point is on the moving art hand. Do not apply its pull a second time.
            Vector3 start = grip.position;
            Vector3 end = harness.position;
            Transform contactFrame = dog != null ? dog : transform;
            double updateTime = Time.realtimeSinceStartupAsDouble;
            bool snapContacts = !state.initialized;
            float deltaTime = Application.isPlaying
                ? Time.deltaTime
                : Mathf.Clamp((float)(updateTime - state.lastUpdateTime), 0f, 0.05f);
            state.lastUpdateTime = updateTime;
            float targetSlack = slack;
            if (snapContacts) state.slack = slack;
            else state.slack = Mathf.Lerp(state.slack, slack, 1f - Mathf.Exp(-ContactResponse * deltaTime));
            slack = state.slack;
            AnimationCurve width = rein.widthCurve;
            float radius = Mathf.Max(width.Evaluate(0f), width.Evaluate(1f)) * rein.widthMultiplier * 0.5f;
            for (int index = 0; index < PositionCount; index++) contactOffsets[index] = Vector3.zero;

            BuildCurve(start, end, slack);
            // Keep the direct hand-to-harness curve. Only an actual triangle hit can
            // introduce a small, smooth correction along the contact normal.
            float spread = Mathf.Clamp(0.6f / Mathf.Max(0.1f, Vector3.Distance(start, end)), 0.04f, 0.2f);
            float maximumOffset = Mathf.Clamp(radius + 0.008f, 0.01f, 0.05f);
            for (int pass = 0; pass < ContactPasses; pass++)
            {
                if (!TryFindContact(radius, out float contactT, out Vector3 correction)) break;
                bool changed = false;
                for (int index = 1; index < PositionCount - 1; index++)
                {
                    float t = index / (PositionCount - 1f);
                    float offset = (t - contactT) / spread;
                    // Do not divide by the near-zero weight at an attachment. The
                    // correction must stay within five centimetres of the natural rope.
                    float weight = Mathf.Exp(-2f * offset * offset) *
                        Mathf.SmoothStep(0f, 1f, t / spread) *
                        Mathf.SmoothStep(0f, 1f, (1f - t) / spread);
                    Vector3 next = Vector3.ClampMagnitude(contactOffsets[index] + correction * weight, maximumOffset);
                    changed |= (next - contactOffsets[index]).sqrMagnitude > 0.00000001f;
                    contactOffsets[index] = next;
                }
                if (!changed) break;
                BuildCurve(start, end, slack);
            }

            // Only filter contact offsets. Both authored attachment positions are
            // read live, so following the dog never leaves a gap at its harness.
            // Store offsets in the dog's frame to avoid lag from world movement.
            bool settling = Mathf.Abs(state.slack - targetSlack) > 0.0001f;
            for (int index = 1; index < PositionCount - 1; index++)
            {
                Vector3 target = contactFrame.InverseTransformVector(contactOffsets[index]);
                Vector3 currentWorld = contactFrame.TransformVector(state.localOffsets[index]);
                Vector3 nextWorld = snapContacts ? contactOffsets[index] :
                    SmoothContactOffset(currentWorld, contactOffsets[index], deltaTime);
                settling |= (nextWorld - contactOffsets[index]).sqrMagnitude > ContactDeadZone * ContactDeadZone;
                state.localOffsets[index] = snapContacts ? target : contactFrame.InverseTransformVector(nextWorld);
                contactOffsets[index] = Vector3.ClampMagnitude(nextWorld, maximumOffset);
            }
            state.initialized = true;
            BuildCurve(start, end, slack);
            rein.useWorldSpace = true;
            rein.positionCount = PositionCount;
            rein.SetPositions(positions);
#if UNITY_EDITOR
            if (!Application.isPlaying && settling) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        private static Vector3 SmoothContactOffset(Vector3 current, Vector3 target, float deltaTime)
        {
            if ((target - current).sqrMagnitude <= ContactDeadZone * ContactDeadZone) return current;
            float blend = 1f - Mathf.Exp(-ContactResponse * Mathf.Max(0f, deltaTime));
            Vector3 filtered = Vector3.Lerp(current, target, blend);
            return Vector3.MoveTowards(current, filtered, ContactFollowSpeed * Mathf.Max(0f, deltaTime));
        }

        private sealed class ReinState
        {
            public readonly Vector3[] localOffsets = new Vector3[PositionCount];
            public bool initialized;
            public float slack;
            public double lastUpdateTime;

            public void Reset()
            {
                initialized = false;
                lastUpdateTime = 0d;
                System.Array.Clear(localOffsets, 0, localOffsets.Length);
            }
        }

        private void BuildCurve(Vector3 start, Vector3 end, float slack)
        {
            for (int index = 0; index < PositionCount; index++)
            {
                float t = index / (PositionCount - 1f);
                positions[index] = Vector3.Lerp(start, end, t) +
                    Vector3.down * (slack * 4f * t * (1f - t)) + contactOffsets[index];
            }
        }

        private bool TryFindContact(float radius, out float contactT, out Vector3 correction)
        {
            contactT = 0f;
            correction = Vector3.zero;
            float deepestContact = 0f;
            float ropeLength = Vector3.Distance(positions[0], positions[PositionCount - 1]);
            for (int index = 0; index < PositionCount - 1; index++)
            {
                Vector3 delta = positions[index + 1] - positions[index];
                float length = delta.magnitude;
                if (length < 0.0001f) continue;
                Vector3 direction = delta / length;
                Vector3 lateral = Vector3.Cross(Vector3.up, direction).normalized * radius;
                foreach (ReinSurface surface in surfaces)
                {
                    // Test the centre and four edges of the rope against real geometry.
                    for (int probe = 0; probe < 5; probe++)
                    {
                        Vector3 offset = probe switch
                        {
                            1 => lateral,
                            2 => -lateral,
                            3 => Vector3.up * radius,
                            4 => Vector3.down * radius,
                            _ => Vector3.zero
                        };
                        if (!surface.Cast(positions[index] + offset, direction, length, out RaycastHit hit) &&
                            !surface.Cast(positions[index + 1] + offset, -direction, length, out hit)) continue;
                        float fraction = Mathf.Clamp01(Vector3.Dot(hit.point - positions[index] - offset, direction) / length);
                        float t = (index + fraction) / (PositionCount - 1f);
                        // Permit the rope to meet its authored ring and grip instead
                        // of forcing it above the entire dog at the final contact.
                        float clearance = Mathf.Min(radius + 0.008f,
                            Mathf.Min(t, 1f - t) * ropeLength);
                        Vector3 normal = hit.normal;
                        Vector3 point = Vector3.Lerp(positions[index], positions[index + 1], fraction);
                        float needed = clearance - Vector3.Dot(point - hit.point, normal);
                        if (needed > deepestContact)
                        {
                            deepestContact = needed;
                            correction = normal * needed;
                            contactT = t;
                        }
                    }
                }
            }
            return deepestContact > 0.0001f;
        }

        private sealed class ReinSurface
        {
            private readonly Renderer renderer;
            private readonly SkinnedMeshRenderer skin;
            private readonly Mesh source;
            private Mesh baked;
            private readonly GameObject helper;
            private readonly MeshCollider collider;
            private bool disposed;

            public ReinSurface(Renderer renderer, SkinnedMeshRenderer skin, Mesh source)
            {
                this.renderer = renderer;
                this.skin = skin;
                this.source = source;
                helper = new GameObject("Rein Surface Query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                helper.transform.SetParent(renderer.transform, false);
                collider = helper.AddComponent<MeshCollider>();
                collider.enabled = false;
                collider.excludeLayers = ~0;
                collider.layerOverridePriority = int.MaxValue;
            }

            public void Prepare()
            {
                if (disposed || renderer == null || collider == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return;
                if (skin != null)
                {
#if UNITY_EDITOR
                    // A scene saver may have persisted an older query mesh.
                    // Never bake animation into that asset or retain ownership of it.
                    if (baked != null && UnityEditor.EditorUtility.IsPersistent(baked)) baked = null;
#endif
                    if (baked == null)
                        baked = new Mesh { name = "Rein Animated Surface", hideFlags = HideFlags.HideAndDontSave };
                    skin.BakeMesh(baked, false);
                    collider.sharedMesh = null;
                    collider.sharedMesh = baked;
                }
                else if (collider.sharedMesh != source) collider.sharedMesh = source;
                collider.enabled = true;
            }

            public bool Cast(Vector3 start, Vector3 direction, float length, out RaycastHit hit)
            {
                hit = default;
                if (collider == null || !collider.enabled || !collider.bounds.IntersectRay(new Ray(start, direction), out float distance) || distance > length)
                    return false;
                return collider.Raycast(new Ray(start, direction), out hit, length);
            }

            public void Finish()
            {
                if (collider != null) collider.enabled = false;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (collider != null)
                {
                    collider.enabled = false;
                    collider.sharedMesh = null;
                }
                DestroyTemporaryObject(helper);
                DestroyTemporaryObject(baked);
                baked = null;
            }

            private static void DestroyTemporaryObject(Object target)
            {
                if (target == null) return;
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.IsPersistent(target)) return;
#endif
                if (Application.isPlaying) Destroy(target);
                else DestroyImmediate(target);
            }
        }
    }
}
