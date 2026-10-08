using UnityEngine;

namespace Mush.Prototype
{
    /// <summary>
    /// Keeps the visual reins connected to the hands and harnesses without
    /// creating collision meshes or querying the dogs and sled for contacts.
    /// </summary>
    [ExecuteAlways]
    // Draw after the desktop view driver restores the hands (order 10000).
    [DefaultExecutionOrder(11000)]
    [DisallowMultipleComponent]
    public sealed class MushReinsVisual : MonoBehaviour
    {
        private const int PositionCount = 65;
        private const float SlackResponse = 12f;

        [SerializeField] private Transform leftGrip;
        [SerializeField] private Transform rightGrip;
        [SerializeField] private Transform leftHarness;
        [SerializeField] private Transform rightHarness;
        [SerializeField] private LineRenderer leftRein;
        [SerializeField] private LineRenderer rightRein;
        private readonly Vector3[] positions = new Vector3[PositionCount];
        private readonly ReinState leftState = new();
        private readonly ReinState rightState = new();

        private float leftPullDistance;
        private float rightPullDistance;
        private bool reinsHeld;
        private Vector3 leftTurnBend;
        private Vector3 rightTurnBend;
        private float leftTurnSlack;
        private float rightTurnSlack;

        /// <summary>Optional world-space turn curve supplied by a ride pose driver.</summary>
        public void SetTurnPresentation(Vector3 leftBend, Vector3 rightBend,
            float leftSlack, float rightSlack)
        {
            leftTurnBend = leftBend;
            rightTurnBend = rightBend;
            leftTurnSlack = Mathf.Max(0f, leftSlack);
            rightTurnSlack = Mathf.Max(0f, rightSlack);
        }

        public void ClearTurnPresentation()
        {
            leftTurnBend = rightTurnBend = Vector3.zero;
            leftTurnSlack = rightTurnSlack = 0f;
            leftState.Reset();
            rightState.Reset();
            UpdateReins();
        }

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

        private void OnEnable()
        {
            leftState.Reset();
            rightState.Reset();
            UpdateReins();
        }

        private void LateUpdate()
        {
            UpdateReins();
        }

        private void UpdateReins()
        {
            float slack = reinsHeld ? 0.035f : 0.07f;
            UpdateRein(leftRein, leftGrip, leftHarness, leftState,
                (slack + leftTurnSlack) / (1f + leftPullDistance), leftTurnBend);
            UpdateRein(rightRein, rightGrip, rightHarness, rightState,
                (slack + rightTurnSlack) / (1f + rightPullDistance), rightTurnBend);
        }

        private void UpdateRein(
            LineRenderer rein,
            Transform grip,
            Transform harness,
            ReinState state,
            float slack,
            Vector3 turnBend)
        {
            if (rein == null || grip == null || harness == null)
            {
                state.Reset();
                return;
            }

            double updateTime = Time.realtimeSinceStartupAsDouble;
            float deltaTime = Application.isPlaying
                ? Time.deltaTime
                : Mathf.Clamp((float)(updateTime - state.lastUpdateTime), 0f, 0.05f);
            state.lastUpdateTime = updateTime;
            if (!state.initialized)
                state.slack = slack;
            else
                state.slack = Mathf.Lerp(state.slack, slack, 1f - Mathf.Exp(-SlackResponse * deltaTime));
            state.initialized = true;

            // Read both anchors live so the ends stay attached while the hands
            // and dogs move. Pulling tightens only the curve's visual slack.
            Vector3 start = grip.position;
            Vector3 end = harness.position;
            for (int index = 0; index < PositionCount; index++)
            {
                float t = index / (PositionCount - 1f);
                positions[index] = Vector3.Lerp(start, end, t) +
                    (Vector3.down * state.slack + turnBend) * (4f * t * (1f - t));
            }
            rein.useWorldSpace = true;
            rein.positionCount = PositionCount;
            rein.SetPositions(positions);
#if UNITY_EDITOR
            if (!Application.isPlaying && Mathf.Abs(state.slack - slack) > 0.0001f)
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        private sealed class ReinState
        {
            public bool initialized;
            public float slack;
            public double lastUpdateTime;

            public void Reset()
            {
                initialized = false;
                lastUpdateTime = 0d;
            }
        }
    }
}
