using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace Mush.Prototype
{
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
        private Camera desktopCamera;
        private Transform leftControllerAnchor;
        private Transform rightControllerAnchor;
        private Transform leftDesktopMitten;
        private Transform rightDesktopMitten;
        private bool configuredHandsAreDesktop;
        private Vector3 leftMouseTarget;
        private Vector3 rightMouseTarget;
        private bool mouseTargetsInitialized;
        private bool terrainSpeedLimited;

        public bool RideStarted => rideStarted;
        public int SpeedLevel => speedLevel;
        public float CurrentSpeed => currentSpeed;
        public float CurrentSteering => currentSteering;
        public bool IsBoosting => rideStarted && speedLevel == 2;
        public float FirstLevelSpeed => firstLevelSpeed;
        public float SecondLevelSpeed => secondLevelSpeed;
        public bool TerrainSpeedLimited => terrainSpeedLimited;

        public void Configure(
            MushReinsVisual newReinsVisual,
            Transform newLeftHandVisual,
            Transform newRightHandVisual,
            Animator newLeftHandAnimator,
            Animator newRightHandAnimator,
            bool newHandsAreDesktop = false)
        {
            reinsVisual = newReinsVisual;
            leftHandVisual = newLeftHandVisual;
            rightHandVisual = newRightHandVisual;
            leftHandAnimator = newLeftHandAnimator;
            rightHandAnimator = newRightHandAnimator;
            configuredHandsAreDesktop = newHandsAreDesktop;
            StoreHandRestPositions();
            if (!rideStarted)
                reinsVisual?.SetHeld(false);
        }

        private void Awake()
        {
            StoreHandRestPositions();
            PrepareGuaranteedDesktopHands();
            reinsVisual?.SetHeld(false);
            SetGripPose(0f);
        }

        private void Update()
        {
            UpdateDesktopMouseHands();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.spaceKey.wasPressedThisFrame)
                StartRide();

            if (!rideStarted)
            {
                UpdateSteeringVisuals(0f, 0f);
                return;
            }

            SetSpeedLevel(keyboard.wKey.isPressed || commandBoostHeld);

            float steeringInput = 0f;
            if (keyboard.aKey.isPressed)
                steeringInput -= 1f;
            if (keyboard.dKey.isPressed)
                steeringInput += 1f;

            float steeringRate = Mathf.Approximately(steeringInput, 0f)
                ? steeringReleaseRate
                : steeringBuildRate;
            currentSteering = Mathf.MoveTowards(currentSteering, steeringInput, steeringRate * Time.deltaTime);

            float leftPull = Mathf.Clamp01(-currentSteering);
            float rightPull = Mathf.Clamp01(currentSteering);
            UpdateSteeringVisuals(leftPull, rightPull);

            float targetSpeed = GetSpeedForLevel(speedLevel);
            float speedChangeRate = targetSpeed >= currentSpeed
                ? acceleration
                : terrainSpeedLimited ? Mathf.Max(deceleration, terrainLimitDeceleration) : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedChangeRate * Time.deltaTime);

            float speedSteeringFactor = Mathf.InverseLerp(0f, firstLevelSpeed, currentSpeed);
            float yaw = currentSteering * maximumTurnRate * speedSteeringFactor * Time.deltaTime;
            transform.Rotate(0f, yaw, 0f, Space.Self);
            MoveAlongGround(currentSpeed * Time.deltaTime);
        }

        public void StartRide()
        {
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
            if (!rideStarted)
                return;

            commandBoostHeld = true;
            SetSpeedLevel(true);
        }

        public void SetBoost(bool held)
        {
            commandBoostHeld = held;
            if (rideStarted)
                SetSpeedLevel(held);
        }

        public void SetTerrainSpeedLimit(bool limited, float firstLevelLimit = 3f, float secondLevelLimit = 5f)
        {
            terrainLimitedFirstLevelSpeed = Mathf.Max(0.1f, firstLevelLimit);
            terrainLimitedSecondLevelSpeed = Mathf.Max(
                terrainLimitedFirstLevelSpeed,
                secondLevelLimit);
            terrainSpeedLimited = limited;
        }

        private float GetSpeedForLevel(int level)
        {
            if (level <= 0)
                return 0f;

            if (terrainSpeedLimited)
            {
                return level >= 2
                    ? terrainLimitedSecondLevelSpeed
                    : terrainLimitedFirstLevelSpeed;
            }

            return level >= 2 ? secondLevelSpeed : firstLevelSpeed;
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

            // Probe only in world-down direction. Using transform.up here allowed
            // steep banks and terrain undersides to turn the probe sideways,
            // after which the complete team could drive below the map.
            Vector3 rayOrigin = new Vector3(
                nextPosition.x,
                currentHeight + Mathf.Max(groundProbeHeight, 8f),
                nextPosition.z);
            float rayDistance = Mathf.Max(groundProbeHeight + groundProbeDistance, 24f);
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
                // When already below the road, recover above it immediately.
                // Normal downward movement remains smoothed over small crests.
                nextPosition.y = targetHeight > currentHeight + 0.3f
                    ? targetHeight
                    : Mathf.MoveTowards(currentHeight, targetHeight, 7f * Time.deltaTime);
            }
            else
            {
                nextPosition.y = currentHeight;
            }

            Vector3 uprightForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (uprightForward.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(uprightForward.normalized, Vector3.up);

            transform.position = nextPosition;
        }

        private void UpdateSteeringVisuals(float leftPull01, float rightPull01)
        {
            float leftPull = leftPull01 * maximumHandPull;
            float rightPull = rightPull01 * maximumHandPull;
            reinsVisual?.SetPull(leftPull, rightPull);

            if (!handRestPositionsStored)
                StoreHandRestPositions();

            if (leftHandVisual != null)
                leftHandVisual.localPosition = leftHandRestPosition + Vector3.back * leftPull;
            if (rightHandVisual != null)
                rightHandVisual.localPosition = rightHandRestPosition + Vector3.back * rightPull;
            if (leftDesktopMitten != null)
                leftDesktopMitten.localPosition = Vector3.back * leftPull;
            if (rightDesktopMitten != null)
                rightDesktopMitten.localPosition = Vector3.back * rightPull;
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

        private void PrepareGuaranteedDesktopHands()
        {
            desktopCamera = Camera.main;
            if (desktopCamera == null)
                desktopCamera = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

            leftControllerAnchor = leftHandVisual != null ? leftHandVisual.parent : FindChild("Left Controller");
            rightControllerAnchor = rightHandVisual != null ? rightHandVisual.parent : FindChild("Right Controller");

            if (leftControllerAnchor != null)
                leftDesktopMitten = BuildWinterMitten("Desktop Left Winter Glove", leftControllerAnchor, -1);
            if (rightControllerAnchor != null)
                rightDesktopMitten = BuildWinterMitten("Desktop Right Winter Glove", rightControllerAnchor, 1);

            UpdateHandRenderMode();
        }

        private void UpdateDesktopMouseHands()
        {
            UpdateHandRenderMode();
            if (XRSettings.isDeviceActive || desktopCamera == null)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null || leftControllerAnchor == null || rightControllerAnchor == null)
                return;

            Vector2 pointer = mouse.position.ReadValue();
            Vector2 normalized = new Vector2(
                Mathf.Clamp01(pointer.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(pointer.y / Mathf.Max(1f, Screen.height)));

            if (!mouseTargetsInitialized)
            {
                leftMouseTarget = ViewportHandPosition(new Vector2(0.31f, 0.27f));
                rightMouseTarget = ViewportHandPosition(new Vector2(0.69f, 0.27f));
                mouseTargetsInitialized = true;
            }

            bool leftOnly = mouse.leftButton.isPressed && !mouse.rightButton.isPressed;
            bool rightOnly = mouse.rightButton.isPressed && !mouse.leftButton.isPressed;
            if (leftOnly)
            {
                leftMouseTarget = ViewportHandPosition(new Vector2(
                    Mathf.Lerp(0.12f, 0.56f, normalized.x),
                    Mathf.Lerp(0.14f, 0.68f, normalized.y)));
            }
            else if (rightOnly)
            {
                rightMouseTarget = ViewportHandPosition(new Vector2(
                    Mathf.Lerp(0.44f, 0.88f, normalized.x),
                    Mathf.Lerp(0.14f, 0.68f, normalized.y)));
            }
            else
            {
                Vector2 offset = (normalized - new Vector2(0.5f, 0.5f)) * 2f;
                leftMouseTarget = ViewportHandPosition(new Vector2(
                    0.31f + offset.x * 0.11f,
                    0.27f + offset.y * 0.15f));
                rightMouseTarget = ViewportHandPosition(new Vector2(
                    0.69f + offset.x * 0.11f,
                    0.27f + offset.y * 0.15f));
            }

            float blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            leftControllerAnchor.position = Vector3.Lerp(leftControllerAnchor.position, leftMouseTarget, blend);
            rightControllerAnchor.position = Vector3.Lerp(rightControllerAnchor.position, rightMouseTarget, blend);
        }

        private Vector3 ViewportHandPosition(Vector2 viewport)
        {
            return desktopCamera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, 0.82f));
        }

        private void UpdateHandRenderMode()
        {
            bool desktopMode = !XRSettings.isDeviceActive;
            if (leftDesktopMitten != null)
                leftDesktopMitten.gameObject.SetActive(desktopMode);
            if (rightDesktopMitten != null)
                rightDesktopMitten.gameObject.SetActive(desktopMode);

            bool showConfiguredHands = configuredHandsAreDesktop ? desktopMode : !desktopMode;
            SetRenderersEnabled(leftHandVisual, showConfiguredHands);
            SetRenderersEnabled(rightHandVisual, showConfiguredHands);
        }

        private static void SetRenderersEnabled(Transform root, bool enabled)
        {
            if (root == null)
                return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = enabled;
        }

        private Transform BuildWinterMitten(string objectName, Transform parent, int side)
        {
            Transform existing = parent.Find(objectName);
            if (existing != null)
                return existing;

            GameObject root = new GameObject(objectName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.Euler(8f, side * 5f, side * 4f);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material glove = new Material(shader) { name = "Runtime Brown Winter Glove" };
            SetMaterialColor(glove, new Color(0.22f, 0.075f, 0.025f));
            Material fur = new Material(shader) { name = "Runtime Cream Fur Cuff" };
            SetMaterialColor(fur, new Color(0.78f, 0.66f, 0.48f));

            CreateHandPrimitive("Palm", PrimitiveType.Sphere, root.transform,
                new Vector3(0f, 0f, 0.02f), new Vector3(0.17f, 0.12f, 0.23f), Vector3.zero, glove);
            CreateHandPrimitive("Curled Fingers", PrimitiveType.Sphere, root.transform,
                new Vector3(0f, -0.005f, 0.13f), new Vector3(0.18f, 0.115f, 0.16f), Vector3.zero, glove);
            CreateHandPrimitive("Thumb", PrimitiveType.Capsule, root.transform,
                new Vector3(side * 0.105f, -0.018f, 0.045f), new Vector3(0.055f, 0.085f, 0.055f),
                new Vector3(62f, 0f, side * -32f), glove);
            CreateHandPrimitive("Wrist", PrimitiveType.Cylinder, root.transform,
                new Vector3(0f, 0f, -0.13f), new Vector3(0.09f, 0.075f, 0.09f),
                new Vector3(90f, 0f, 0f), glove);
            CreateHandPrimitive("Fur Cuff", PrimitiveType.Cylinder, root.transform,
                new Vector3(0f, 0f, -0.20f), new Vector3(0.125f, 0.055f, 0.125f),
                new Vector3(90f, 0f, 0f), fur);

            return root.transform;
        }

        private static void CreateHandPrimitive(
            string objectName,
            PrimitiveType type,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Vector3 euler,
            Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = objectName;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = position;
            primitive.transform.localRotation = Quaternion.Euler(euler);
            primitive.transform.localScale = scale;
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.18f);
        }

        private Transform FindChild(string objectName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName)
                    return child;
            }
            return null;
        }
    }
}
