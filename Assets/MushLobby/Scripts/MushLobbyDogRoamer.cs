using UnityEngine;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyDogRoamer : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform tail;
        [SerializeField] private Vector2 areaMin = new Vector2(-1.25f, -0.15f);
        [SerializeField] private Vector2 areaMax = new Vector2(1.15f, 1.55f);
        [SerializeField] private float walkSpeed = 0.42f;
        [SerializeField] private float turnSpeed = 5f;
        [SerializeField] private float pauseMinimum = 0.8f;
        [SerializeField] private float pauseMaximum = 2.2f;
        [SerializeField] private float furnitureClearance = 0.26f;
        [Header("Character")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform callTarget;
        [SerializeField] private float callSideOffset;
        [SerializeField] private float callDistance = 1.25f;
        [SerializeField] private float unpettedCallWait = 5f;

        private Vector3 target;
        private float pauseTimer;
        private float celebrateTimer;
        private float reactionTimer;
        private float tailWagTimer;
        private float idleBounceTimer;
        private float animationTime;
        private float nextIdleActionTime;
        private Vector3 visualRestPosition;
        private Quaternion tailRestRotation;
        private Transform[] fallbackLegs;
        private Quaternion[] fallbackLegRestRotations;
        private int poseCorrectionFrames = 8;
        private bool called;
        private bool reachedCallPoint;
        private float callWaitTimer;
        private Vector3 calledDestinationWorld;
        private Vector3 calledLookPointWorld;

        public bool IsMoving { get; private set; }
        public Transform VisualRoot => visualRoot != null ? visualRoot : transform;

        public void Configure(Transform newVisualRoot, Transform newTail, Vector2 newAreaMin, Vector2 newAreaMax)
        {
            visualRoot = newVisualRoot;
            tail = newTail;
            areaMin = newAreaMin;
            areaMax = newAreaMax;
        }

        public void ConfigureCharacter(Animator newAnimator, Transform newCallTarget, float newCallSideOffset)
        {
            animator = newAnimator;
            callTarget = newCallTarget;
            callSideOffset = newCallSideOffset;
        }

        private void Awake()
        {
            EnsureRuntimeVisual();
            CacheVisualParts();
            PlaceInFrontOfLobbyCamera();
            OrientVisualFromGeometry();
            NormalizeVisualBounds();
            BuildLegPivots();
            SnapPawsToFloor();
            if (visualRoot != null)
                visualRestPosition = visualRoot.localPosition;
            if (tail != null)
                tailRestRotation = tail.localRotation;
            FitInteractionCollider();
            PickTarget();
            pauseTimer = Random.Range(pauseMinimum, pauseMaximum);
            nextIdleActionTime = Time.time + Random.Range(3.5f, 7f);
        }

        private void NormalizeVisualBounds()
        {
            if (visualRoot == null)
                return;

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            if (bounds.size.y <= 0.0001f)
                return;

            bool malamute = name.IndexOf("Malamute", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Bori", System.StringComparison.OrdinalIgnoreCase) >= 0;
            float targetHeight = malamute ? 0.96f : 0.88f;
            visualRoot.localScale *= targetHeight / bounds.size.y;

            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);

            Vector3 rootPosition = transform.position;
            visualRoot.position += new Vector3(
                rootPosition.x - bounds.center.x,
                rootPosition.y - bounds.min.y,
                rootPosition.z - bounds.center.z);
        }

        private void CacheVisualParts()
        {
            if (visualRoot == null)
                return;

            bool malamute = name.IndexOf("Malamute", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Bori", System.StringComparison.OrdinalIgnoreCase) >= 0;
            string prefix = malamute ? "Malamute_" : "Husky_";
            if (tail == null)
                tail = FindPart(visualRoot, prefix + "Tail") ?? FindPart(visualRoot, "Tail");
        }

        private void OrientVisualFromGeometry()
        {
            if (visualRoot == null)
                return;

            if (!TryGetPartCenter("Front_L_Paw", out Vector3 frontLeftPaw) ||
                !TryGetPartCenter("Front_R_Paw", out Vector3 frontRightPaw) ||
                !TryGetPartCenter("Rear_L_Paw", out Vector3 rearLeftPaw) ||
                !TryGetPartCenter("Rear_R_Paw", out Vector3 rearRightPaw) ||
                !TryGetPartCenter("Head", out Vector3 headCenter))
                return;

            Vector3 pawCenter = (frontLeftPaw + frontRightPaw + rearLeftPaw + rearRightPaw) * 0.25f;
            Vector3 leftCenter = (frontLeftPaw + rearLeftPaw) * 0.5f;
            Vector3 rightCenter = (frontRightPaw + rearRightPaw) * 0.5f;
            Vector3 frontCenter = (frontLeftPaw + frontRightPaw) * 0.5f;
            Vector3 rearCenter = (rearLeftPaw + rearRightPaw) * 0.5f;

            Vector3 anatomicalRight = rightCenter - leftCenter;
            Vector3 anatomicalForward = frontCenter - rearCenter;
            if (anatomicalRight.sqrMagnitude < 0.0001f || anatomicalForward.sqrMagnitude < 0.0001f)
                return;

            Vector3 anatomicalUp = Vector3.Cross(anatomicalForward, anatomicalRight).normalized;
            if (Vector3.Dot(anatomicalUp, headCenter - pawCenter) < 0f)
            {
                anatomicalUp = -anatomicalUp;
                anatomicalRight = -anatomicalRight;
            }

            anatomicalForward = Vector3.ProjectOnPlane(anatomicalForward, anatomicalUp).normalized;
            Quaternion currentAnatomicalPose = Quaternion.LookRotation(anatomicalForward, anatomicalUp);
            Quaternion desiredPose = Quaternion.LookRotation(transform.forward, Vector3.up);
            Quaternion correction = desiredPose * Quaternion.Inverse(currentAnatomicalPose);
            visualRoot.rotation = correction * visualRoot.rotation;
        }

        private bool TryGetPartCenter(string fragment, out Vector3 center)
        {
            Transform part = FindPart(visualRoot, fragment);
            if (part != null)
            {
                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer == null)
                    renderer = part.GetComponentInChildren<Renderer>(true);
                center = renderer != null ? renderer.bounds.center : part.position;
                return true;
            }

            center = Vector3.zero;
            return false;
        }

        private void PlaceInFrontOfLobbyCamera()
        {
            Transform cameraTransform = callTarget;
            if (cameraTransform == null)
            {
                Camera camera = Camera.main;
                if (camera != null)
                    cameraTransform = camera.transform;
            }
            if (cameraTransform == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;

            Vector3 visiblePosition = cameraTransform.position + forward * 1.65f + right * callSideOffset;
            visiblePosition.y = transform.parent != null ? transform.parent.position.y : 0f;
            transform.position = visiblePosition;
            transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        }

        private void EnsureRuntimeVisual()
        {
            if (visualRoot != null)
                return;

            bool malamute = name.IndexOf("Malamute", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Bori", System.StringComparison.OrdinalIgnoreCase) >= 0;
            string resourcePath = malamute
                ? "MushDogs/Mush_LowPoly_Malamute"
                : "MushDogs/Mush_LowPoly_Husky";
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogError("[Mush] Could not load lobby dog model: " + resourcePath, this);
                return;
            }

            GameObject visual = Instantiate(prefab, transform);
            visual.name = malamute ? "Dog Visual - LowPoly Malamute" : "Dog Visual - LowPoly Husky";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * (malamute ? 0.40f : 0.39f);
            visualRoot = visual.transform;

            string prefix = malamute ? "Malamute_" : "Husky_";
            Transform head = FindPart(visualRoot, prefix + "Head");
            Transform leftEye = FindPart(visualRoot, prefix + "Eye_L");
            Transform rightEye = FindPart(visualRoot, prefix + "Eye_R");
            Transform mouth = FindPart(visualRoot, prefix + "Mouth");
            tail = FindPart(visualRoot, prefix + "Tail");

            foreach (Collider rootCollider in GetComponents<Collider>())
                rootCollider.enabled = false;
            if (head != null)
            {
                SphereCollider headCollider = head.GetComponent<SphereCollider>();
                if (headCollider == null)
                    headCollider = head.gameObject.AddComponent<SphereCollider>();
                headCollider.radius = malamute ? 0.53f : 0.49f;
            }

            MushLobbyDogExpression expression = GetComponent<MushLobbyDogExpression>();
            if (expression == null)
                expression = gameObject.AddComponent<MushLobbyDogExpression>();
            Camera camera = Camera.main;
            if (camera == null)
                camera = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            expression.Configure(this, head, leftEye, rightEye, mouth, camera);

            MushLobbyDogInteraction interaction = GetComponent<MushLobbyDogInteraction>();
            if (interaction != null)
                interaction.ConfigureDogParts(this, head, expression);
        }

        private static Transform FindPart(Transform root, string fragment)
        {
            if (root == null)
                return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            }
            return null;
        }

        private void Start()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            animator.Rebind();
            animator.Play("Locomotion", 0, 0f);
            animator.Update(0f);
        }

        private void Update()
        {
            animationTime += Time.deltaTime;
            if (celebrateTimer > 0f)
                celebrateTimer -= Time.deltaTime;
            if (reactionTimer > 0f)
                reactionTimer -= Time.deltaTime;
            if (tailWagTimer > 0f)
                tailWagTimer -= Time.deltaTime;
            if (idleBounceTimer > 0f)
                idleBounceTimer -= Time.deltaTime;

            if (reactionTimer > 0f)
            {
                IsMoving = false;
                SetAnimatorSpeed(0f);
                Animate(false);
                return;
            }

            if (called && callTarget != null)
            {
                UpdateCalledMovement();
                return;
            }

            if (pauseTimer > 0f)
            {
                pauseTimer -= Time.deltaTime;
                IsMoving = false;
                SetAnimatorSpeed(0f);
                TryPlayIdleAction();
                Animate(false);
                return;
            }

            Vector3 flatPosition = transform.localPosition;
            flatPosition.y = 0f;
            Vector3 difference = target - flatPosition;
            difference.y = 0f;

            if (difference.sqrMagnitude <= 0.025f)
            {
                PickTarget();
                pauseTimer = Random.Range(pauseMinimum, pauseMaximum);
                IsMoving = false;
                SetAnimatorSpeed(0f);
                Animate(false);
                return;
            }

            Vector3 direction = difference.normalized;
            float speedMultiplier = celebrateTimer > 0f ? 1.65f : 1f;
            Vector3 worldDirection = transform.parent != null
                ? transform.parent.TransformDirection(direction)
                : direction;
            worldDirection = MushLobbyFurnitureObstacle.FindOpenDirection(
                transform.position,
                worldDirection,
                0.58f,
                furnitureClearance);
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                PickTarget();
                pauseTimer = 0.2f;
                IsMoving = false;
                SetAnimatorSpeed(0f);
                Animate(false);
                return;
            }
            direction = transform.parent != null
                ? transform.parent.InverseTransformDirection(worldDirection).normalized
                : worldDirection.normalized;
            transform.localPosition += direction * (walkSpeed * speedMultiplier * Time.deltaTime);
            Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, facing, turnSpeed * Time.deltaTime);
            IsMoving = true;
            SetAnimatorSpeed(celebrateTimer > 0f ? 1f : 0.48f);
            Animate(true);
        }

        private void LateUpdate()
        {
            if (poseCorrectionFrames > 0)
            {
                OrientVisualFromGeometry();
                poseCorrectionFrames--;
            }
            SnapPawsToFloor();
            if (poseCorrectionFrames > 0 && visualRoot != null)
                visualRestPosition = visualRoot.localPosition;
        }

        public void CallTo(Transform newTarget)
        {
            if (newTarget != null)
                callTarget = newTarget;
            called = callTarget != null;
            if (called)
                CaptureCalledDestination();
            reachedCallPoint = false;
            callWaitTimer = 0f;
            pauseTimer = 0f;
        }

        private void CaptureCalledDestination()
        {
            Vector3 forward = Vector3.ProjectOnPlane(callTarget.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(callTarget.right, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;

            // Freeze both values at the instant Space is pressed. WASD only
            // rotates the lobby camera, so it must not move an already-called dog.
            calledDestinationWorld = callTarget.position + forward * callDistance + right * callSideOffset;
            calledDestinationWorld.y = transform.position.y;
            calledLookPointWorld = callTarget.position;
            calledLookPointWorld.y = transform.position.y;
        }

        public void ResumeRoaming()
        {
            called = false;
            reachedCallPoint = false;
            callWaitTimer = 0f;
            PickTarget();
            pauseTimer = Random.Range(0.25f, 0.7f);
        }

        public void MarkPetted()
        {
            // Petting is activity, so keep the dog nearby for another five
            // seconds. It must still resume roaming when interaction stops.
            if (called && reachedCallPoint)
                callWaitTimer = unpettedCallWait;
        }

        public void Celebrate()
        {
            celebrateTimer = 2.4f;
            reactionTimer = 2.4f;
            tailWagTimer = 2.4f;
            pauseTimer = Random.Range(0.4f, 0.8f);
            TriggerAnimation("Happy");
        }

        public void PlayEat()
        {
            SetAnimatorSpeed(0f);
            TriggerAnimation("Eat");
        }

        public void PlayPet()
        {
            reactionTimer = Mathf.Max(reactionTimer, 1.15f);
            tailWagTimer = Mathf.Max(tailWagTimer, 1.45f);
            SetAnimatorSpeed(0f);
            TriggerAnimation("Pet");
        }

        public void PlayHeadTilt()
        {
            reactionTimer = Mathf.Max(reactionTimer, 0.65f);
            tailWagTimer = Mathf.Max(tailWagTimer, 0.85f);
            SetAnimatorSpeed(0f);
            TriggerAnimation("HeadTilt");
        }

        public void WagTail(float duration)
        {
            tailWagTimer = Mathf.Max(tailWagTimer, duration);
        }

        private void PickTarget()
        {
            for (int attempt = 0; attempt < 18; attempt++)
            {
                Vector3 candidate = new(
                    Random.Range(areaMin.x, areaMax.x),
                    0f,
                    Random.Range(areaMin.y, areaMax.y));
                Vector3 worldCandidate = transform.parent != null
                    ? transform.parent.TransformPoint(candidate)
                    : candidate;
                if (MushLobbyFurnitureObstacle.IsBlocked(worldCandidate, furnitureClearance))
                    continue;
                target = candidate;
                return;
            }

            // All random samples can only fail when furniture was installed
            // directly around the dog. Staying put is safer than crossing it.
            target = transform.localPosition;
            target.y = 0f;
        }

        private void Animate(bool walking)
        {
            bool animatorReady = animator != null && animator.isActiveAndEnabled &&
                                 animator.runtimeAnimatorController != null && animator.avatar != null;
            if (animatorReady)
                return;

            if (visualRoot != null)
                visualRoot.localPosition = visualRestPosition;

            if (tail != null)
            {
                bool wagging = walking || tailWagTimer > 0f || celebrateTimer > 0f;
                float wagSpeed = celebrateTimer > 0f ? 18f : tailWagTimer > 0f ? 11f : 7f;
                float wagAngle = celebrateTimer > 0f ? 34f : tailWagTimer > 0f ? 22f : walking ? 9f : 0f;
                Quaternion targetWag = tailRestRotation * Quaternion.Euler(
                    0f,
                    wagging ? Mathf.Sin(animationTime * wagSpeed) * wagAngle : 0f,
                    0f);
                tail.localRotation = Quaternion.Slerp(tail.localRotation, targetWag, Time.deltaTime * 14f);
            }

            if (fallbackLegs == null || fallbackLegRestRotations == null)
                return;
            for (int index = 0; index < fallbackLegs.Length; index++)
            {
                if (fallbackLegs[index] == null)
                    continue;
                bool oppositeDiagonal = index == 1 || index == 2;
                float phase = oppositeDiagonal ? Mathf.PI : 0f;
                float targetAngle = walking ? Mathf.Sin(animationTime * 8.5f + phase) * 15f : 0f;
                Quaternion targetRotation = fallbackLegRestRotations[index] * Quaternion.Euler(targetAngle, 0f, 0f);
                fallbackLegs[index].localRotation = Quaternion.Slerp(
                    fallbackLegs[index].localRotation,
                    targetRotation,
                    Time.deltaTime * 12f);
            }
        }

        private void BuildLegPivots()
        {
            if (visualRoot == null)
                return;

            string[][] legGroups =
            {
                new[] { "_Front_L_Upper", "_Front_L_Lower", "_Front_L_Paw" },
                new[] { "_Front_R_Upper", "_Front_R_Lower", "_Front_R_Paw" },
                new[] { "_Rear_L_Thigh", "_Rear_L_Shin", "_Rear_L_Paw" },
                new[] { "_Rear_R_Thigh", "_Rear_R_Shin", "_Rear_R_Paw" }
            };

            fallbackLegs = new Transform[legGroups.Length];
            fallbackLegRestRotations = new Quaternion[legGroups.Length];
            for (int index = 0; index < legGroups.Length; index++)
            {
                Transform upper = FindPart(visualRoot, legGroups[index][0]);
                Transform lower = FindPart(visualRoot, legGroups[index][1]);
                Transform paw = FindPart(visualRoot, legGroups[index][2]);
                if (upper == null || lower == null || paw == null)
                    continue;

                GameObject pivotObject = new GameObject("Walk Pivot " + index);
                Transform pivot = pivotObject.transform;
                pivot.SetParent(visualRoot, true);

                Vector3 upperToLower = lower.position - upper.position;
                pivot.position = upper.position - upperToLower * 0.35f;
                pivot.rotation = transform.rotation;

                upper.SetParent(pivot, true);
                lower.SetParent(pivot, true);
                paw.SetParent(pivot, true);

                fallbackLegs[index] = pivot;
                fallbackLegRestRotations[index] = pivot.localRotation;
            }
        }

        private void FitInteractionCollider()
        {
            if (visualRoot == null)
                return;

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);

            CapsuleCollider bodyCollider = GetComponent<CapsuleCollider>();
            if (bodyCollider == null)
                bodyCollider = gameObject.AddComponent<CapsuleCollider>();

            bodyCollider.enabled = true;
            bodyCollider.isTrigger = false;
            bodyCollider.direction = 1;
            bodyCollider.center = transform.InverseTransformPoint(bounds.center);
            bodyCollider.height = Mathf.Max(0.4f, bounds.size.y);
            bodyCollider.radius = Mathf.Max(0.18f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.78f);
        }

        private void SnapPawsToFloor()
        {
            if (visualRoot == null)
                return;

            string[] pawNames =
            {
                "Front_L_Paw",
                "Front_R_Paw",
                "Rear_L_Paw",
                "Rear_R_Paw"
            };

            float lowestPaw = float.PositiveInfinity;
            bool foundPaw = false;
            for (int index = 0; index < pawNames.Length; index++)
            {
                Transform paw = FindPart(visualRoot, pawNames[index]);
                if (paw == null)
                    continue;

                Renderer renderer = paw.GetComponent<Renderer>();
                if (renderer == null)
                    renderer = paw.GetComponentInChildren<Renderer>(true);

                float pawBottom = renderer != null ? renderer.bounds.min.y : paw.position.y;
                lowestPaw = Mathf.Min(lowestPaw, pawBottom);
                foundPaw = true;
            }

            if (!foundPaw)
                return;

            float floorY = transform.position.y;
            visualRoot.position += Vector3.up * (floorY - lowestPaw);
        }

        private void UpdateCalledMovement()
        {
            Vector3 currentWorld = transform.position;
            Vector3 difference = Vector3.ProjectOnPlane(calledDestinationWorld - currentWorld, Vector3.up);

            if (!reachedCallPoint && difference.sqrMagnitude > 0.075f)
            {
                Vector3 direction = MushLobbyFurnitureObstacle.FindOpenDirection(
                    transform.position,
                    difference.normalized,
                    0.58f,
                    furnitureClearance);
                if (direction.sqrMagnitude < 0.0001f)
                {
                    IsMoving = false;
                    SetAnimatorSpeed(0f);
                    Animate(false);
                    return;
                }
                transform.position += direction * (walkSpeed * 1.35f * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(direction, Vector3.up),
                    turnSpeed * Time.deltaTime);
                IsMoving = true;
                SetAnimatorSpeed(0.58f);
                Animate(true);
                return;
            }

            if (!reachedCallPoint)
            {
                reachedCallPoint = true;
                callWaitTimer = unpettedCallWait;
            }

            Vector3 towardPlayer = Vector3.ProjectOnPlane(calledLookPointWorld - transform.position, Vector3.up);
            if (towardPlayer.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(towardPlayer.normalized, Vector3.up),
                    turnSpeed * Time.deltaTime);
            }
            IsMoving = false;
            SetAnimatorSpeed(0f);
            Animate(false);

            callWaitTimer -= Time.deltaTime;
            if (callWaitTimer <= 0f)
                ResumeRoaming();
        }

        private void TryPlayIdleAction()
        {
            if (Time.time < nextIdleActionTime)
                return;

            nextIdleActionTime = Time.time + Random.Range(5f, 9f);
            float choice = Random.value;
            if (choice < 0.50f)
            {
                tailWagTimer = Random.Range(1.1f, 2.0f);
                TriggerAnimation("TailWag");
            }
            else
            {
                idleBounceTimer = Random.Range(0.8f, 1.4f);
                TriggerAnimation(choice < 0.75f ? "HeadTilt" : choice < 0.90f ? "Sit" : "LieDown");
            }
        }

        private void SetAnimatorSpeed(float speed)
        {
            if (animator != null)
                animator.SetFloat("Speed", speed, 0.10f, Time.deltaTime);
        }

        private void TriggerAnimation(string parameter)
        {
            if (animator != null)
                animator.SetTrigger(parameter);
        }
    }
}
