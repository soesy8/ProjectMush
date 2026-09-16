using UnityEngine;

namespace Mush.Prototype
{
    /// <summary>
    /// Keeps the two visual reins connected between the player's tracked hands
    /// and the harness anchors on the dogs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushReinsVisual : MonoBehaviour
    {
        [SerializeField] private Transform leftGrip;
        [SerializeField] private Transform rightGrip;
        [SerializeField] private Transform leftHarness;
        [SerializeField] private Transform rightHarness;
        [SerializeField] private LineRenderer leftRein;
        [SerializeField] private LineRenderer rightRein;

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

        private void LateUpdate()
        {
            UpdateReins();
        }

        private void UpdateReins()
        {
            float slack = reinsHeld ? 0.12f : 0.34f;
            UpdateRein(leftRein, leftGrip, leftHarness, leftPullDistance, transform.forward, slack);
            UpdateRein(rightRein, rightGrip, rightHarness, rightPullDistance, transform.forward, slack);
        }

        private static void UpdateRein(
            LineRenderer rein,
            Transform grip,
            Transform harness,
            float pullDistance,
            Vector3 sledForward,
            float slack)
        {
            if (rein == null || grip == null || harness == null)
                return;

            rein.positionCount = 3;
            Vector3 start = grip.position - sledForward * pullDistance;
            Vector3 end = harness.position;
            Vector3 midpoint = Vector3.Lerp(start, end, 0.5f) + Vector3.down * slack;
            rein.SetPosition(0, start);
            rein.SetPosition(1, midpoint);
            rein.SetPosition(2, end);
        }
    }
}
