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

        [Header("Prototype References")]
        [SerializeField] private MushReinsVisual reinsVisual;
        [SerializeField] private Transform leftHandVisual;
        [SerializeField] private Transform rightHandVisual;
        [SerializeField] private Animator leftHandAnimator;
        [SerializeField] private Animator rightHandAnimator;

        [Header("Two-stage Speed")]
        [SerializeField, Min(0.1f)] private float firstLevelSpeed = 8f;
        [SerializeField, Min(0.1f)] private float secondLevelSpeed = 15f;
        [SerializeField, Min(0.1f)] private float acceleration = 3.5f;
        [SerializeField, Min(0.1f)] private float deceleration = 2.2f;

        [Header("Terrain Speed Limit")]
        [SerializeField, Min(0.1f)] private float terrainLimitedFirstLevelSpeed = 3f;
        [SerializeField, Min(0.1f)] private float terrainLimitedSecondLevelSpeed = 5f;
        [SerializeField, Min(0.1f)] private float terrainLimitDeceleration = 4.5f;

        [Header("Steering")]
        [SerializeField, Min(1f)] private float maximumTurnRate = 34f;
        [SerializeField, Min(0.1f)] private float steeringBuildRate = 2.4f;
        [SerializeField, Min(0.1f)] private float steeringReleaseRate = 4.5f;
        [SerializeField, Range(0f, 0.5f)] private float maximumHandPull = 0.24f;
        [SerializeField, Min(1f)] private float sharpCurveMaximumTurnRate = 48f;
        [SerializeField, Range(0.1f, 1f)] private float sharpCurveBoostTurnRateMultiplier = 0.42f;

        [Header("Dog Condition Effects")]
        [SerializeField, Min(0f)] private float dogEffectSpeedChange = 5f;
        [SerializeField, Min(1f)] private float buffSteeringResponseMultiplier = 1.2f;
        [SerializeField, Range(0.1f, 1f)] private float penaltyAccelerationMultiplier = 0.8f;

        [Header("Ground Following")]
        [SerializeField, Min(0.1f)] private float groundProbeHeight = 2.5f;
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 6f;
        [SerializeField, Min(0f)] private float rideHeight = 0.06f;

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
            terrainSpeedLimited = false;
            currentSpeed *= Mathf.Clamp(retainedSpeedRatio, 0.05f, 1f);
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
            Debug.Log($"[Mush] Speed level: {speedLevel}/2", this);
        }

        private void MoveAlongGround(float distance)
        {
            Vector3 nextPosition = transform.position + transform.forward * distance;
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
