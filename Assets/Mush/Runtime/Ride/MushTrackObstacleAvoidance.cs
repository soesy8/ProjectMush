using Unity.Profiling;
using UnityEngine;

namespace Mush.Prototype
{
    /// <summary>Runs before the original controller, so extra yaw changes real travel.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class MushTrackObstacleAvoidance : MonoBehaviour
    {
        private const int ObstacleMask = 1 << 2;
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Mush.TrackObstacleAvoidance");

        [Header("주행 연결")]
        [SerializeField] private MushMapRideBootstrap ride;
        [SerializeField] private MushSledKeyboardController controller;
        [SerializeField] private MushCurvedMapRuntime course;

        [Header("장애물 회피")]
        [SerializeField] private bool avoidanceEnabled = true;
        [SerializeField] private bool preventPenetration = true;
        [SerializeField] private bool showDetectionGizmos = false;

        [Header("인식 크기 및 거리")]
        [SerializeField, Min(0.1f), InspectorName("팀 반폭 (m)")] private float teamHalfWidth = 1.25f;
        [SerializeField, Min(0f), InspectorName("바깥 회피 여유 (m)")] private float avoidancePadding = 0.6f;
        [SerializeField, Range(0.1f, 2f), InspectorName("앞을 보는 시간 (초)")] private float lookAheadSeconds = 1.1f;
        [SerializeField, Min(1f), InspectorName("최소 전방 거리 (m)")] private float minimumLookAhead = 6f;
        [SerializeField, Range(5f, 60f), InspectorName("주변 검색 빈도 (Hz)")] private float queryFrequency = 20f;

        [Header("회피 회전")]
        [SerializeField, Range(1f, 90f), InspectorName("추가 회전 속도 상한 (도/초)")] private float maximumAvoidanceTurnRate = 42f;
        [SerializeField, Range(0.1f, 1f), InspectorName("목표 방향 반응 시간 (초)")] private float headingResponseSeconds = 0.35f;
        [SerializeField, Range(0.03f, 1f), InspectorName("회피 시작 부드러움 (초)")] private float turnInSeconds = 0.12f;
        [SerializeField, Range(0.03f, 1f), InspectorName("회피 해제 부드러움 (초)")] private float releaseSeconds = 0.28f;
        [SerializeField, Range(5f, 45f), InspectorName("장애물당 추가 회전 상한 (도)")] private float maximumHeadingOffset = 24f;

        private readonly Collider[] nearby = new Collider[128];
        private readonly RaycastHit[] sweepHits = new RaycastHit[128];
        private int nearbyCount;
        private float nextQuery;
        private float yawRate;
        private float appliedYawRate;
        private float yawVelocity;
        private float headingOffset;
        private float lookAhead;
        private Vector3 previousPosition;
        private bool movedThisFrame;
        private bool skipSafety;
        private bool warnedSaturation;
        private MushTrackObstacle focus;
        private int passSide;
        private Vector3 passForward;
        private int safetyCorrections;
        private double averageMicroseconds;

        protected MushSledKeyboardController Controller => controller;
        public bool AvoidanceEnabled => avoidanceEnabled;
        public float AppliedYawRate => appliedYawRate;
        public int CandidateCount => nearbyCount;
        public int SafetyCorrections => safetyCorrections;
        public double AverageMicroseconds => averageMicroseconds;
        public string FocusName => focus != null ? focus.name : "-";

        protected virtual void Start()
        {
            if (ride == null || controller == null)
            {
                enabled = false;
                return;
            }
            controller.MotionReset += ResetState;
        }

        protected virtual void OnDestroy()
        {
            if (controller != null) controller.MotionReset -= ResetState;
        }

        protected virtual void OnDisable() => ResetState();

        public void SetAvoidanceEnabled(bool value)
        {
            avoidanceEnabled = value;
            ResetState();
        }

        protected void ResetState()
        {
            yawRate = yawVelocity = 0f;
            appliedYawRate = 0f;
            headingOffset = 0f;
            focus = null;
            passSide = 0;
            passForward = Vector3.zero;
            nextQuery = 0f;
            skipSafety = true;
        }

        private bool CanMove => isActiveAndEnabled && avoidanceEnabled && controller != null && controller.enabled &&
            controller.RideStarted && controller.CurrentSpeed > 0.05f && ride != null &&
            !ride.IsPaused && !ride.HasFinished && Time.deltaTime > 0f;

        protected virtual void Update()
        {
            movedThisFrame = CanMove;
            if (!movedThisFrame)
            {
                yawRate = yawVelocity = 0f;
                appliedYawRate = 0f;
                return;
            }

            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            using (Marker.Auto())
            {
                Transform root = controller.transform;
                previousPosition = root.position;
                skipSafety = false;
                lookAhead = Mathf.Max(minimumLookAhead, controller.CurrentSpeed * lookAheadSeconds);
                if (Time.time >= nextQuery)
                {
                    nearbyCount = Physics.OverlapSphereNonAlloc(root.position + Vector3.up,
                        lookAhead + teamHalfWidth + avoidancePadding + 2f, nearby,
                        ObstacleMask, QueryTriggerInteraction.Collide);
                    if (nearbyCount == nearby.Length && !warnedSaturation)
                    {
                        warnedSaturation = true;
                        Debug.LogWarning("[Mush] Nearby obstacle buffer full; reduce query range or increase the fixed buffer.", this);
                    }
                    nextQuery = Time.time + 1f / Mathf.Max(1f, queryFrequency);
                }

                float targetRate = GetTargetYaw(root.position, root.forward);
                float response = Mathf.Abs(targetRate) > Mathf.Abs(yawRate) ? turnInSeconds : releaseSeconds;
                yawRate = Mathf.SmoothDamp(yawRate, targetRate, ref yawVelocity, response,
                    Mathf.Infinity, Time.deltaTime);
                float nextOffset = Mathf.Clamp(headingOffset + yawRate * Time.deltaTime,
                    -maximumHeadingOffset, maximumHeadingOffset);
                appliedYawRate = (nextOffset - headingOffset) / Time.deltaTime;
                root.Rotate(Vector3.up, nextOffset - headingOffset, Space.World);
                headingOffset = nextOffset;
            }
            RecordCost(started);
        }

        private float GetTargetYaw(Vector3 position, Vector3 direction)
        {
            Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            MushTrackObstacle best = null;
            float nearestEntry = float.PositiveInfinity;
            // Keep the pass side and its yaw budget until the whole team is past
            // this obstacle. The committed heading is never unwound.
            if (focus != null && focus.gameObject.activeInHierarchy && focus.Body.enabled &&
                SharesRideHeight(focus, position))
            {
                float alongPlayerHeading = Vector3.Dot(focus.Center - position, passForward);
                if (alongPlayerHeading >= -focus.Radius - teamHalfWidth &&
                    alongPlayerHeading <= lookAhead + focus.Radius + teamHalfWidth)
                    best = focus;
            }
            // A latched target owns the pass until it is behind the whole team.
            if (best == null)
            for (int i = 0; i < nearbyCount; i++)
            {
                Collider hit = nearby[i];
                if (hit == null || !hit.enabled || !hit.gameObject.activeInHierarchy ||
                    !hit.TryGetComponent<MushTrackObstacle>(out var obstacle)) continue;
                Vector3 delta = obstacle.Center - position;
                float along = Vector3.Dot(delta, forward);
                float side = Vector3.Dot(delta, right);
                float radius = obstacle.Radius + teamHalfWidth + avoidancePadding;
                if (along < -obstacle.Radius || along > lookAhead + obstacle.Radius ||
                    Mathf.Abs(side) >= radius || !SharesRideHeight(obstacle, position)) continue;
                float entry = along - Mathf.Sqrt(Mathf.Max(0f, radius * radius - side * side));
                if (entry >= nearestEntry) continue;
                nearestEntry = entry;
                best = obstacle;
            }
            if (best == null)
            {
                focus = null;
                passSide = 0;
                // Release steering without undoing any rotation already applied.
                return 0f;
            }

            Vector3 offset = best.Center - position;
            float lateral = Vector3.Dot(offset, right);
            float clearance = best.Radius + teamHalfWidth + avoidancePadding;
            if (best != focus)
            {
                focus = best;
                passForward = forward;
                headingOffset = 0f;
                passSide = ChoosePassSide(best, position, right, lateral, clearance);
            }
            float forwardDistance = Vector3.Dot(offset, forward);
            if (forwardDistance <= 0f || Mathf.Abs(lateral) >= clearance + 0.15f)
                return 0f;
            float angle = Mathf.Atan2(lateral + passSide * (clearance + 0.15f),
                Mathf.Max(1f, forwardDistance)) * Mathf.Rad2Deg;
            // Once the path clears, never steer back towards the same prop.
            angle = passSide * Mathf.Max(0f, angle * passSide);
            float speedFactor = Mathf.Clamp01(controller.CurrentSpeed / Mathf.Max(1f, controller.FirstLevelSpeed));
            return Mathf.Clamp(angle / Mathf.Max(0.1f, headingResponseSeconds),
                -maximumAvoidanceTurnRate, maximumAvoidanceTurnRate) * speedFactor;
        }

        private int ChoosePassSide(MushTrackObstacle obstacle, Vector3 position, Vector3 right, float lateral, float clearance)
        {
            int away = Mathf.Abs(lateral) > 0.15f ? (lateral > 0f ? -1 : 1) : 1;
            if (course == null) return away;
            Vector3 leftPoint = obstacle.Center - right * (clearance + 0.3f);
            Vector3 rightPoint = obstacle.Center + right * (clearance + 0.3f);
            bool leftValid = course.TryGetCourseSurface(leftPoint, out _, out _, out _, out float leftLane);
            bool rightValid = course.TryGetCourseSurface(rightPoint, out _, out _, out _, out float rightLane);
            if (!leftValid || !rightValid) return away;
            float allowed = Mathf.Max(0f, course.RoadHalfWidthMeters - teamHalfWidth);
            bool leftOnRoad = Mathf.Abs(leftLane) <= allowed;
            bool rightOnRoad = Mathf.Abs(rightLane) <= allowed;
            if (leftOnRoad != rightOnRoad) return leftOnRoad ? -1 : 1;
            return Mathf.Abs(lateral) <= 0.15f ? (Mathf.Abs(leftLane) < Mathf.Abs(rightLane) ? -1 : 1) : away;
        }

        private static bool SharesRideHeight(MushTrackObstacle obstacle, Vector3 position)
        {
            Bounds bounds = obstacle.Body.bounds;
            return position.y + 1.5f >= bounds.min.y && position.y + 0.2f <= bounds.max.y;
        }

        protected virtual void LateUpdate()
        {
            if (!movedThisFrame || !CanMove || !preventPenetration || skipSafety) return;
            Vector3 requested = controller.transform.position;
            if (Vector3.Distance(previousPosition, requested) > Mathf.Max(3f, controller.CurrentSpeed * Time.deltaTime * 5f))
                return; // Recovery/teleport is not a travelled segment.
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            using (Marker.Auto())
            {
                Vector3 safe = ConstrainSegment(previousPosition, requested, teamHalfWidth);
                if ((safe - requested).sqrMagnitude > 0.000001f)
                {
                    if (course != null && course.TryGetCourseSurface(safe, out Vector3 surface, out _, out _, out _))
                        safe.y = surface.y + controller.RideHeight;
                    controller.transform.position = safe;
                    safetyCorrections++;
                }
            }
            RecordCost(started);
        }

        // A swept sphere prevents missing a narrow prop on a slow frame. Trigger callbacks are unnecessary.
        public Vector3 ConstrainSegment(Vector3 from, Vector3 requested, float radius)
        {
            Vector3 position = from;
            Vector3 remaining = requested - from;
            remaining.y = 0f;
            for (int iteration = 0; iteration < 2 && remaining.sqrMagnitude > 0.000001f; iteration++)
            {
                float distance = remaining.magnitude;
                Vector3 direction = remaining / distance;
                int count = Physics.SphereCastNonAlloc(position + Vector3.up * 0.9f, radius,
                    direction, sweepHits, distance + 0.03f, ObstacleMask, QueryTriggerInteraction.Collide);
                int nearest = -1;
                float nearestDistance = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    var hit = sweepHits[i];
                    if (hit.collider == null || !hit.collider.TryGetComponent<MushTrackObstacle>(out _) ||
                        hit.distance >= nearestDistance) continue;
                    nearest = i;
                    nearestDistance = hit.distance;
                }
                if (nearest < 0) { position += remaining; break; }
                float travel = Mathf.Clamp(nearestDistance - 0.03f, 0f, distance);
                position += direction * travel;
                Vector3 normal = Vector3.ProjectOnPlane(sweepHits[nearest].normal, Vector3.up).normalized;
                if (normal.sqrMagnitude < 0.001f) break;
                remaining = Vector3.ProjectOnPlane(remaining - direction * travel, normal);
            }
            position.y = requested.y;
            return position;
        }

        public Vector3 ConstrainFollowerPosition(Vector3 from, Vector3 requested)
        {
            if (!CanMove || !preventPenetration || skipSafety ||
                Vector3.Distance(from, requested) > Mathf.Max(3f, controller.CurrentSpeed * Time.deltaTime * 5f))
                return requested;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Vector3 safe;
            using (Marker.Auto()) safe = ConstrainSegment(from, requested, 0.85f);
            if ((safe - requested).sqrMagnitude > 0.000001f) safetyCorrections++;
            RecordCost(started);
            return safe;
        }

        private void RecordCost(long started)
        {
            double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000000d /
                System.Diagnostics.Stopwatch.Frequency;
            averageMicroseconds += (elapsed - averageMicroseconds) * 0.05d;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (!showDetectionGizmos || controller == null) return;
            Transform root = controller.transform;
            float distance = Mathf.Max(minimumLookAhead, controller.CurrentSpeed * lookAheadSeconds);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(root.position + Vector3.up, root.position + Vector3.up + root.forward * distance);
            Gizmos.DrawWireSphere(root.position + Vector3.up + root.forward * distance, teamHalfWidth + avoidancePadding);
            if (focus != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(focus.Center, focus.Radius + teamHalfWidth + avoidancePadding);
            }
        }
    }
}
