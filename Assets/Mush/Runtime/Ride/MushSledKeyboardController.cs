using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Mush.Prototype
{
    public enum MushDogRideEffect
    {
        None = 0,
        Buff = 1,
        Penalty = 2,
    }

    /// <summary>
    /// Keyboard-only prototype controls. Public commands are kept separate so
    /// Quest controller Input Actions can call the same methods later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushSledKeyboardController : MonoBehaviour
    {
        private static readonly int GripParameter = Animator.StringToHash("Grip");

        [Header("참조 오브젝트")]
        [SerializeField, InspectorName("고삐 시각 효과")] private MushReinsVisual reinsVisual;
        [SerializeField, InspectorName("왼손 오브젝트")] private Transform leftHandVisual;
        [SerializeField, InspectorName("오른손 오브젝트")] private Transform rightHandVisual;
        [SerializeField, InspectorName("왼손 애니메이터")] private Animator leftHandAnimator;
        [SerializeField, InspectorName("오른손 애니메이터")] private Animator rightHandAnimator;

        // Fixed ranges use the saved Track_v2 values as their baseline (+200% = 3x).
        // Retain the original lower limits to avoid negative motion rates.
        [Header("주행 속도 및 가감속")]
        [InspectorName("기본 주행 속도 (m/s)")]
        [Tooltip("가속 입력을 누르지 않을 때의 목표 속도입니다.\n기준값: 8. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 24f)] private float firstLevelSpeed = 8f;
        [InspectorName("가속 주행 속도 (m/s)")]
        [Tooltip("가속 입력을 유지할 때의 목표 속도입니다.\n기준값: 15. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 45f)] private float secondLevelSpeed = 15f;
        [InspectorName("가속력 (m/s²)")]
        [Tooltip("목표 속도까지 초당 증가하는 속도입니다. 물리 힘이 아닌 가속률입니다.\n기준값: 3.5. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 10.5f)] private float acceleration = 3.5f;
        [InspectorName("감속력 (m/s²)")]
        [Tooltip("목표 속도가 낮아졌을 때 초당 감소하는 속도입니다.\n기준값: 2.2. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 6.6f)] private float deceleration = 2.2f;

        [Header("지형 속도 제한")]
        [InspectorName("지형 제한 기본 속도 (m/s)")]
        [Tooltip("지형 속도 제한이 활성화됐을 때의 기본 목표 속도입니다. 기존 SetTerrainSpeedLimit 호출이 실행 중 이 값을 덮어쓸 수 있습니다.\n기준값: 3. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 9f)] private float terrainLimitedFirstLevelSpeed = 3f;
        [InspectorName("지형 제한 가속 속도 (m/s)")]
        [Tooltip("지형 속도 제한이 활성화됐을 때의 가속 목표 속도입니다. 기존 SetTerrainSpeedLimit 호출이 실행 중 이 값을 덮어쓸 수 있습니다.\n기준값: 5. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 15f)] private float terrainLimitedSecondLevelSpeed = 5f;
        [InspectorName("지형 제한 감속력 (m/s²)")]
        [Tooltip("지형 제한 시 적용할 감속률입니다. 일반 감속력과 비교해 큰 값을 사용합니다.\n기준값: 4.5. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 13.5f)] private float terrainLimitDeceleration = 4.5f;

        [Header("회전 및 조향")]
        [InspectorName("최대 회전 속도 (도/초)")]
        [Tooltip("일반 코스에서 조향을 끝까지 입력했을 때의 최대 회전 속도입니다. 낮은 주행 속도에서는 회전이 줄어듭니다.\n기준값: 34. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(1f, 102f)] private float maximumTurnRate = 34f;
        [InspectorName("조향 반응 속도 (초당)")]
        [Tooltip("조향 입력에 도달하는 속도입니다. 클수록 회전 입력에 빠르게 반응합니다.\n기준값: 2.4. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 7.2f)] private float steeringBuildRate = 2.4f;
        [InspectorName("조향 복귀 속도 (초당)")]
        [Tooltip("조향 입력을 놓았을 때 직진 상태로 복귀하는 속도입니다.\n기준값: 4.5. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 13.5f)] private float steeringReleaseRate = 4.5f;
        [InspectorName("손 최대 당김 거리 (m)")]
        [Tooltip("키보드 조향 시 손과 고삐를 당기는 시각적 거리입니다. VR 조향 감도를 변경하지 않습니다.\n기준값: 0.24. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0f, 0.72f)] private float maximumHandPull = 0.24f;
        [InspectorName("급커브 최대 회전 속도 (도/초)")]
        [Tooltip("급커브 코스에서 사용하는 별도의 최대 회전 속도입니다.\n기준값: 48. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(1f, 144f)] private float sharpCurveMaximumTurnRate = 48f;
        [InspectorName("급커브 가속 시 회전 배율")]
        [Tooltip("급커브 코스에서 가속 주행 속도에 도달했을 때의 회전 배율입니다. 1보다 작으면 회전이 약해지고, 크면 강해집니다.\n기준값: 0.42. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 1.26f)] private float sharpCurveBoostTurnRateMultiplier = 0.42f;

        [Header("개 상태 효과")]
        [InspectorName("개 상태에 따른 속도 변화 (m/s)")]
        [Tooltip("개 상태가 좋으면 목표 속도에 더하고, 나쁘면 빼는 값입니다.\n기준값: 5. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0f, 15f)] private float dogEffectSpeedChange = 5f;
        [InspectorName("좋은 개 상태 조향 반응 배율")]
        [Tooltip("개 상태가 좋을 때 조향 반응 및 복귀 속도에 곱하는 배율입니다.\n기준값: 1.2. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(1f, 3.6f)] private float buffSteeringResponseMultiplier = 1.2f;
        [InspectorName("나쁜 개 상태 가속력 배율")]
        [Tooltip("개 상태가 나쁠 때 가속력에 곱하는 배율입니다. 1보다 작으면 가속이 약해지고, 크면 강해집니다.\n기준값: 0.8. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 2.4f)] private float penaltyAccelerationMultiplier = 0.8f;

        [Header("지면 추종")]
        [InspectorName("지면 탐색 시작 높이 (m)")]
        [Tooltip("지면 탐색 광선의 시작 높이입니다. 기존 복구 로직에 따라 실제 탐색에는 최소 8m를 사용합니다.\n기준값: 2.5. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 7.5f)] private float groundProbeHeight = 2.5f;
        [InspectorName("지면 탐색 거리 (m)")]
        [Tooltip("지면 탐색 광선 길이 계산에 더하는 거리입니다. 기존 복구 로직에 따라 실제 광선 길이는 최소 160m입니다.\n기준값: 6. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0.1f, 18f)] private float groundProbeDistance = 6f;
        [InspectorName("지면 위 주행 높이 (m)")]
        [Tooltip("탐색한 지면 위에 썰매 이동 루트를 띄우는 높이입니다.\n기준값: 0.06. 슬라이더는 기준값의 ±200% 범위에서 기존 최소값을 유지합니다.")]
        [SerializeField, Range(0f, 0.18f)] private float rideHeight = 0.06f;

        private bool rideStarted;
        private int speedLevel;
        private float currentSpeed;
        private float currentSteering;
        private bool commandBoostHeld;
        private Vector3 leftHandRestPosition;
        private Vector3 rightHandRestPosition;
        private bool handRestPositionsStored;
        private bool terrainSpeedLimited;
        private bool offCourseRecoveryActive;
        private float offCourseRecoveryAccelerationMultiplier = 1f;
        private float courseSpeedMultiplier = 1f;
        private MushCurvedMapRuntime courseSurface;
        private MushDogRideEffect activeDogEffect;
        private bool externalSteeringActive;
        private float externalSteeringInput;
        private float externalReinTension;
        private float reinTension;
        public event System.Action AccelerationEntered;

        [System.Serializable]
        public sealed class SavedMotion
        {
            public bool started;
            public int level;
            public float speed;
            public float steering;
            public bool boost;
            public MushDogRideEffect effect;
            public bool terrainLimited;
            public float courseMultiplier = 1f;
            public bool recoveryActive;
            public float recoveryAcceleration = 1f;
        }

        public SavedMotion CaptureMotion() => new()
        {
            started = rideStarted, level = speedLevel, speed = currentSpeed,
            steering = currentSteering, boost = commandBoostHeld, effect = activeDogEffect,
            terrainLimited = terrainSpeedLimited, courseMultiplier = courseSpeedMultiplier,
            recoveryActive = offCourseRecoveryActive, recoveryAcceleration = offCourseRecoveryAccelerationMultiplier,
        };

        public void RestoreMotion(SavedMotion motion)
        {
            if (motion == null) return;
            rideStarted = motion.started;
            speedLevel = rideStarted ? Mathf.Clamp(motion.level, 1, 2) : 0;
            currentSpeed = Mathf.Max(0f, motion.speed);
            currentSteering = float.IsFinite(motion.steering) ? Mathf.Clamp(motion.steering, -1f, 1f) : 0f;
            commandBoostHeld = motion.boost;
            // Derive effects from saved care/stamina state, never the old manual toggle.
            activeDogEffect = EffectForDogCondition();
            terrainSpeedLimited = motion.terrainLimited;
            courseSpeedMultiplier = float.IsFinite(motion.courseMultiplier) ? Mathf.Max(0.01f, motion.courseMultiplier) : 1f;
            offCourseRecoveryActive = motion.recoveryActive;
            offCourseRecoveryAccelerationMultiplier = float.IsFinite(motion.recoveryAcceleration) ? Mathf.Max(0.01f, motion.recoveryAcceleration) : 1f;
            currentSpeed = Mathf.Min(currentSpeed, GetSpeedForLevel(speedLevel));
            reinsVisual?.SetHeld(rideStarted);
            SetGripPose(rideStarted ? 1f : 0f);
        }

        public bool RideStarted => rideStarted;
        public int SpeedLevel => speedLevel;
        public float CurrentSpeed => currentSpeed;
        public float CurrentSteering => currentSteering;
        public float ReinTension => rideStarted ? reinTension : 0f;
        public bool IsBoosting => rideStarted && speedLevel == 2;
        public float FirstLevelSpeed => firstLevelSpeed;
        public float SecondLevelSpeed => secondLevelSpeed;
        public float RideHeight => rideHeight;
        public bool TerrainSpeedLimited => terrainSpeedLimited;
        public float CourseSpeedMultiplier => courseSpeedMultiplier;
        public MushDogRideEffect ActiveDogEffect => activeDogEffect;
        public string ActiveDogEffectLabel => activeDogEffect switch
        {
            MushDogRideEffect.Buff => "BUFF",
            MushDogRideEffect.Penalty => "PENALTY",
            _ => "NONE",
        };

        public void Configure(
            MushReinsVisual newReinsVisual,
            Transform newLeftHandVisual,
            Transform newRightHandVisual,
            Animator newLeftHandAnimator,
            Animator newRightHandAnimator)
        {
            reinsVisual = newReinsVisual;
            leftHandVisual = newLeftHandVisual;
            rightHandVisual = newRightHandVisual;
            leftHandAnimator = newLeftHandAnimator;
            rightHandAnimator = newRightHandAnimator;
            StoreHandRestPositions();
            ShowBothHands();
            if (!rideStarted)
                reinsVisual?.SetHeld(false);
        }

        private void Awake()
        {
            StoreHandRestPositions();
            ShowBothHands();
            reinsVisual?.SetHeld(false);
            SetGripPose(0f);
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            UpdateDogConditionEffect();

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                StartRide();

            if (!rideStarted)
            {
                reinTension = 0f;
                UpdateSteeringVisuals(0f, 0f);
                return;
            }

            SetSpeedLevel((keyboard != null && keyboard.wKey.isPressed) || commandBoostHeld);

            float steeringInput = externalSteeringActive ? externalSteeringInput : 0f;
            if (!externalSteeringActive && keyboard != null)
            {
                if (keyboard.aKey.isPressed)
                    steeringInput -= 1f;
                if (keyboard.dKey.isPressed)
                    steeringInput += 1f;
            }

            // Two Quest pulls can cancel steering while both reins remain under tension.
            reinTension = externalSteeringActive
                ? Mathf.Max(Mathf.Abs(externalSteeringInput), externalReinTension)
                : keyboard != null && (keyboard.aKey.isPressed || keyboard.dKey.isPressed) ? 1f : 0f;

            float steeringRate = Mathf.Approximately(steeringInput, 0f)
                ? steeringReleaseRate
                : steeringBuildRate;
            if (activeDogEffect == MushDogRideEffect.Buff)
                steeringRate *= buffSteeringResponseMultiplier;
            currentSteering = Mathf.MoveTowards(currentSteering, steeringInput, steeringRate * Time.deltaTime);

            float leftPull = Mathf.Clamp01(-currentSteering);
            float rightPull = Mathf.Clamp01(currentSteering);
            UpdateSteeringVisuals(leftPull, rightPull);

            float targetSpeed = GetSpeedForLevel(speedLevel);
            float effectiveAcceleration = activeDogEffect == MushDogRideEffect.Penalty
                ? acceleration * penaltyAccelerationMultiplier
                : acceleration;
            if (offCourseRecoveryActive)
                effectiveAcceleration *= offCourseRecoveryAccelerationMultiplier;
            float speedChangeRate = targetSpeed >= currentSpeed
                ? effectiveAcceleration
                : terrainSpeedLimited ? Mathf.Max(deceleration, terrainLimitDeceleration) : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedChangeRate * Time.deltaTime);
            if (terrainSpeedLimited)
                currentSpeed = Mathf.Min(currentSpeed, targetSpeed);

            float speedSteeringFactor = Mathf.InverseLerp(0f, firstLevelSpeed, currentSpeed);
            float activeTurnRate = maximumTurnRate;
            if (courseSurface != null && courseSurface.IsSharpCurveMap)
            {
                // At level 1 the player has enough authority to take each
                // hairpin with a deliberate full pull. At level 2 the runners
                // understeer heavily, so holding boost through the whole map is
                // no longer a viable racing line.
                float highSpeed01 = Mathf.InverseLerp(firstLevelSpeed, secondLevelSpeed, currentSpeed);
                activeTurnRate = sharpCurveMaximumTurnRate * Mathf.Lerp(
                    1f,
                    sharpCurveBoostTurnRateMultiplier,
                    highSpeed01);
            }
            float yaw = currentSteering * activeTurnRate * speedSteeringFactor * Time.deltaTime;
            transform.Rotate(0f, yaw, 0f, Space.Self);
            MoveAlongGround(currentSpeed * Time.deltaTime);
        }

        public void StartRide()
        {
            if (Time.timeScale <= 0f) return;
            if (rideStarted)
                return;

            rideStarted = true;
            speedLevel = 1;
            reinsVisual?.SetHeld(true);
            SetGripPose(1f);
            Debug.Log("[Mush] Reins grabbed. Ride started at speed level 1.", this);
        }

        public void IncreaseSpeed()
        {
            if (Time.timeScale <= 0f) return;
            if (!rideStarted)
                return;

            commandBoostHeld = true;
            SetSpeedLevel(true);
        }

        public void SetBoost(bool held)
        {
            if (Time.timeScale <= 0f) return;
            commandBoostHeld = held;
            if (rideStarted)
                SetSpeedLevel(held);
        }

        public void SetExternalSteering(float steering, bool active = true)
        {
            externalSteeringActive = active;
            externalSteeringInput = Mathf.Clamp(steering, -1f, 1f);
        }

        public void SetExternalReinTension(float tension) => externalReinTension = Mathf.Clamp01(tension);

        public void SetTerrainSpeedLimit(bool limited, float firstLevelLimit = 3f, float secondLevelLimit = 5f)
        {
            terrainLimitedFirstLevelSpeed = Mathf.Max(0.1f, firstLevelLimit);
            terrainLimitedSecondLevelSpeed = Mathf.Max(
                terrainLimitedFirstLevelSpeed,
                secondLevelLimit);
            terrainSpeedLimited = limited;
        }

        public void ApplyOffCourseImpact(float retainedSpeedRatio, float recoveryAccelerationMultiplier)
        {
            terrainSpeedLimited = true;
            currentSpeed = Mathf.Min(
                currentSpeed * Mathf.Clamp(retainedSpeedRatio, 0.05f, 1f),
                GetSpeedForLevel(speedLevel));
            offCourseRecoveryActive = true;
            offCourseRecoveryAccelerationMultiplier = Mathf.Clamp(
                recoveryAccelerationMultiplier,
                0.1f,
                1f);
        }

        public void ClearOffCourseImpactRecovery()
        {
            offCourseRecoveryActive = false;
            offCourseRecoveryAccelerationMultiplier = 1f;
        }

        public void SetCourseSpeedMultiplier(float multiplier)
        {
            float nextMultiplier = Mathf.Clamp(multiplier, 0.25f, 3f);
            if (Mathf.Approximately(nextMultiplier, courseSpeedMultiplier))
                return;

            float previousMultiplier = courseSpeedMultiplier;
            courseSpeedMultiplier = nextMultiplier;
            if (!rideStarted)
                return;

            if (nextMultiplier > previousMultiplier && previousMultiplier > 0.0001f)
            {
                // The downhill modifier describes actual travel speed, not
                // only a target that is reached several seconds later.
                currentSpeed = Mathf.Min(
                    currentSpeed * (nextMultiplier / previousMultiplier),
                    GetSpeedForLevel(speedLevel));
            }
            else
            {
                currentSpeed = Mathf.Min(currentSpeed, GetSpeedForLevel(speedLevel));
            }
        }

        public void SetCourseSurface(MushCurvedMapRuntime surface)
        {
            courseSurface = surface;
        }

        public void ResetMotionForCourseRecovery()
        {
            commandBoostHeld = false;
            speedLevel = rideStarted ? 1 : 0;
            currentSpeed = 0f;
            currentSteering = 0f;
            externalSteeringInput = 0f;
            externalReinTension = reinTension = 0f;
            terrainSpeedLimited = false;
            ClearOffCourseImpactRecovery();
            UpdateSteeringVisuals(0f, 0f);
        }

        private static MushDogRideEffect EffectForDogCondition() => MushGameSave.DogCondition switch
        {
            MushDogCondition.Good => MushDogRideEffect.Buff,
            MushDogCondition.Bad => MushDogRideEffect.Penalty,
            _ => MushDogRideEffect.None,
        };

        private void UpdateDogConditionEffect()
        {
            SetDogRideEffect(EffectForDogCondition());
        }

        private void SetDogRideEffect(MushDogRideEffect effect)
        {
            if (activeDogEffect == effect)
                return;

            activeDogEffect = effect;
            currentSpeed = Mathf.Min(currentSpeed, GetSpeedForLevel(speedLevel));
            string detail = effect switch
            {
                MushDogRideEffect.Buff => "최고속도 +5, 조향 반응성 +20%",
                MushDogRideEffect.Penalty => "최고속도 -5, 가속력 -20%",
                _ => "효과 해제",
            };
            Debug.Log($"[Mush] Dog ride effect: {ActiveDogEffectLabel} ({detail})", this);
        }

        private float GetSpeedForLevel(int level)
        {
            if (level <= 0)
                return 0f;

            float levelSpeed;
            if (terrainSpeedLimited)
            {
                levelSpeed = level >= 2
                    ? terrainLimitedSecondLevelSpeed
                    : terrainLimitedFirstLevelSpeed;

                // Off-road values remain hard limits independent of the downhill multiplier.
                return Mathf.Max(0.1f, levelSpeed);
            }
            else
            {
                levelSpeed = level >= 2 ? secondLevelSpeed : firstLevelSpeed;
            }

            float adjustedSpeed = levelSpeed * courseSpeedMultiplier;
            if (activeDogEffect == MushDogRideEffect.Buff)
                adjustedSpeed += dogEffectSpeedChange;
            else if (activeDogEffect == MushDogRideEffect.Penalty)
                adjustedSpeed -= dogEffectSpeedChange;
            return Mathf.Max(0.1f, adjustedSpeed);
        }

        private void SetSpeedLevel(bool boostHeld)
        {
            int nextLevel = boostHeld ? 2 : 1;
            if (speedLevel == nextLevel)
                return;

            speedLevel = nextLevel;
            if (nextLevel == 2) AccelerationEntered?.Invoke();
            Debug.Log($"[Mush] Speed level: {speedLevel}/2", this);
        }

        private void MoveAlongGround(float distance)
        {
            Vector3 nextPosition = transform.position + transform.forward * distance;
            if (courseSurface != null)
                nextPosition = courseSurface.ConstrainRideMovement(transform.position, nextPosition);
            float currentHeight = transform.position.y;

            // Procedural maps can provide their exact surface.  This keeps the
            // kinematic ride root attached even when a 1.5x descent moves more
            // vertically in one second than the old ray follower could recover.
            if (courseSurface != null && courseSurface.TryGetCourseSurface(
                    nextPosition,
                    out Vector3 sampledSurface,
                    out _,
                    out _,
                    out _))
            {
                nextPosition.y = sampledSurface.y + rideHeight;
                KeepRootUpright();
                transform.position = nextPosition;
                return;
            }

            // Probe only in world-down direction. Using transform.up here allowed
            // steep banks and terrain undersides to turn the probe sideways,
            // after which the complete team could drive below the map.
            Vector3 rayOrigin = new Vector3(
                nextPosition.x,
                currentHeight + Mathf.Max(groundProbeHeight, 8f),
                nextPosition.z);
            // The hard map drops by more than one hundred metres.  A long
            // recovery probe lets the sled reacquire the visible road even if
            // a frame hitch or a previous shallow probe left it above the
            // descent instead of permanently flying at the crest height.
            float rayDistance = Mathf.Max(groundProbeHeight + groundProbeDistance, 160f);
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            bool foundGround = false;
            RaycastHit bestHit = default;
            float highestSurface = float.NegativeInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit candidate = hits[i];
                if (candidate.normal.y < 0.58f)
                    continue;

                // The terrain mesh continues underneath the raised road mesh.
                // Always choosing the closest hit can therefore leave the sled
                // trapped on that lower surface after it returns to the road.
                // The upper walkable hit is the visible surface in both cases.
                float surfaceHeight = candidate.point.y + rideHeight;
                if (surfaceHeight > highestSurface)
                {
                    foundGround = true;
                    bestHit = candidate;
                    highestSurface = surfaceHeight;
                }
            }

            if (foundGround)
            {
                float targetHeight = bestHit.point.y + rideHeight;
                // This controller is kinematic, so keeping a fixed 7 m/s
                // vertical correction made the 1.5x downhill section outrun
                // its ground follower.  The generated road is continuous;
                // matching its sampled height directly prevents both flying
                // above a descent and clipping through a steep rise.
                nextPosition.y = targetHeight;
            }
            else
            {
                nextPosition.y = currentHeight;
            }

            KeepRootUpright();

            transform.position = nextPosition;
        }

        private void KeepRootUpright()
        {
            Vector3 uprightForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (uprightForward.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(uprightForward.normalized, Vector3.up);
        }

        private void UpdateSteeringVisuals(float leftPull01, float rightPull01)
        {
            float leftPull = XRSettings.isDeviceActive ? 0f : leftPull01 * maximumHandPull;
            float rightPull = XRSettings.isDeviceActive ? 0f : rightPull01 * maximumHandPull;
            reinsVisual?.SetPull(leftPull, rightPull);

            if (!handRestPositionsStored)
                StoreHandRestPositions();

            if (leftHandVisual != null)
                leftHandVisual.position = leftHandVisual.parent.TransformPoint(leftHandRestPosition) - transform.forward * leftPull;
            if (rightHandVisual != null)
                rightHandVisual.position = rightHandVisual.parent.TransformPoint(rightHandRestPosition) - transform.forward * rightPull;
        }

        private void StoreHandRestPositions()
        {
            if (leftHandVisual == null || rightHandVisual == null)
                return;

            leftHandRestPosition = leftHandVisual.localPosition;
            rightHandRestPosition = rightHandVisual.localPosition;
            handRestPositionsStored = true;
        }

        private void SetGripPose(float value)
        {
            if (leftHandAnimator != null)
                leftHandAnimator.SetFloat(GripParameter, value);
            if (rightHandAnimator != null)
                rightHandAnimator.SetFloat(GripParameter, value);
        }

        private void ShowBothHands()
        {
            SetRenderersEnabled(leftHandVisual);
            SetRenderersEnabled(rightHandVisual);
        }

        private static void SetRenderersEnabled(Transform root)
        {
            if (root == null)
                return;
            root.gameObject.SetActive(true);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;
        }
    }
}

#if UNITY_EDITOR
namespace Mush.Prototype
{
    [UnityEditor.CustomEditor(typeof(MushSledKeyboardController))]
    [UnityEditor.CanEditMultipleObjects]
    internal sealed class MushSledKeyboardControllerEditor : UnityEditor.Editor
    {
        private readonly System.Collections.Generic.List<UnityEditor.SerializedProperty> properties = new();
        private readonly System.Collections.Generic.List<GUIContent> labels = new();

        private void OnEnable()
        {
            properties.Clear();
            labels.Clear();
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                var field = typeof(MushSledKeyboardController).GetField(
                    iterator.name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var name = field == null ? null : System.Attribute.GetCustomAttribute(
                    field, typeof(InspectorNameAttribute)) as InspectorNameAttribute;
                var tooltip = field == null ? null : System.Attribute.GetCustomAttribute(
                    field, typeof(TooltipAttribute)) as TooltipAttribute;
                properties.Add(iterator.Copy());
                labels.Add(new GUIContent(
                    iterator.name == "m_Script" ? "스크립트" : name?.displayName ?? iterator.displayName,
                    tooltip?.tooltip ?? iterator.tooltip));
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            float previousLabelWidth = UnityEditor.EditorGUIUtility.labelWidth;
            UnityEditor.EditorGUIUtility.labelWidth = Mathf.Max(
                previousLabelWidth, Mathf.Min(240f, UnityEditor.EditorGUIUtility.currentViewWidth * 0.55f));
            try
            {
                for (int i = 0; i < properties.Count; i++)
                {
                    using (new UnityEditor.EditorGUI.DisabledScope(properties[i].name == "m_Script"))
                        UnityEditor.EditorGUILayout.PropertyField(properties[i], labels[i], true);
                }
            }
            finally
            {
                UnityEditor.EditorGUIUtility.labelWidth = previousLabelWidth;
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
