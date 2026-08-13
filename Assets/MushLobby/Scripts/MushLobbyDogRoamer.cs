using System.Collections.Generic;
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
        [SerializeField] private float furnitureClearance = 0.42f; // 가구 외곽에서 개 몸통이 떨어져 있어야 하는 최소 여유 거리다.
        [SerializeField] private float pawGroundClearance = 0.008f; // 실제 바닥/러그 윗면을 계산한 뒤 발 메시가 면과 겹치지 않을 정도의 아주 작은 여유만 더한다.
        [SerializeField] private float proceduralSleepBodyDrop = 0.075f; // 전용 눕기 애니메이션이 없을 때 몸통을 낮추는 양이다. 다리를 접는 동작과 합쳐도 바닥 아래로 잠기지 않게 작게 유지한다.
        [Header("Ambient Life")]
        [SerializeField] private float runSpeed = 0.95f; // 멀리 이동하거나 장난칠 때 사용하는 달리기 속도다.
        [SerializeField, Range(0f, 1f)] private float randomRunChance = 0.32f; // 다음 생활 지점으로 갈 때 달리기를 선택할 확률이다.
        [SerializeField, Range(0f, 1f)] private float sleepChance = 0.18f; // 지점 도착 후 잠들기를 선택할 확률이다.
        [SerializeField, Range(0f, 1f)] private float playChance = 0.20f; // 다른 개에게 장난을 걸 확률이다.
        [SerializeField] private Vector2 sleepDuration = new Vector2(5.5f, 10f); // 한 번 누워 쉬는 시간 범위다.
        [SerializeField] private Vector2 playDuration = new Vector2(3.5f, 6.5f); // 두 마리가 한 번 장난치는 시간 범위다.
        [SerializeField] private float socialCooldownDuration = 10f; // 같은 장난 행동이 너무 자주 반복되지 않게 막는 시간이다.
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
        private Quaternion visualRestRotation; // 절차식 수면 자세 뒤 원래 몸 방향으로 되돌릴 때 사용한다.
        private Quaternion tailRestRotation;
        private Transform[] fallbackLegs;
        private Quaternion[] fallbackLegRestRotations;
        private int poseCorrectionFrames = 8;
        private bool called;
        private bool reachedCallPoint;
        private float callWaitTimer;
        private Vector3 calledDestinationWorld;
        private Vector3 calledLookPointWorld;
        private int routeIndex = -1; // 현재 향하고 있는 안전 생활 경로 지점의 번호다.
        private bool runningToTarget; // 일반 배회 중 이번 구간을 달릴지 기억한다.
        private float sleepTimer; // 0보다 크면 현재 잠자는 행동을 유지한다.
        private bool sleepPoseFrozen; // 눕기 애니메이션의 마지막 자세를 고정했는지 기억한다.
        private float socialCooldown; // 0보다 크면 다른 개에게 새 장난을 걸지 않는다.
        private MushLobbyDogRoamer playPartner; // 함께 장난치는 상대 개다.
        private bool playLeader; // 장난 중 추격 경로를 먼저 달리는 쪽인지 구분한다.
        private float playTimer; // 현재 장난 행동의 남은 시간이다.
        private float fallbackLocomotionSpeed; // Animator가 없는 현재 프로토타입 모델에서 절차식 다리 속도를 걷기/달리기에 맞춘다.
        private Renderer[] groundSurfaceRenderers; // 바닥 판재와 러그의 실제 Renderer Bounds를 저장해 현재 위치의 정확한 접지 높이를 계산한다.

        private static readonly List<MushLobbyDogRoamer> ActiveDogs = new(); // 로비에 살아 있는 개들을 모아 두 마리 상호작용에 사용한다.

        // 하우징 가구가 벽 쪽 슬롯에 놓여도 개가 가구 사이를 직선으로 가르지 않도록 만든 프로토타입 안전 순환 경로다.
        // 순서대로 인접한 지점만 이동하므로 방 전체에서 무작위 좌표를 뽑던 기존 방식보다 경로가 훨씬 명확하다.
        private static readonly Vector3[] SafeRoutePoints =
        {
            // 플레이어는 +Z 뒤쪽 좌석에서 -Z 정면을 바라본다. 새 로비는 가로를 줄이고 정면 깊이를 늘렸으므로
            // 개 경로도 양옆 벽으로 퍼지지 않고 중앙과 정면을 길게 왕복한다. 하우징 가구는 더 깊은 좌우 코너에 있어 이 경로와 겹치지 않는다.
            new(-0.95f, 0f, 0.45f), // 호출 위치에서 자연스럽게 배회로 이어지는 왼쪽 가까운 지점이다.
            new(-2.15f, 0f, -0.35f), // 집 꾸미기 상자 안쪽을 지나되 상자 금지 반경에는 들어가지 않는 왼쪽 지점이다.
            new(-2.25f, 0f, -1.85f), // 지도 스탠드와 왼쪽 클릭 구역을 바깥으로 돌아 정면 깊은 공간으로 내려가는 지점이다.
            new(-1.55f, 0f, -3.05f), // 의자/탁자보다 중앙 쪽을 통과하는 왼쪽 중간 지점이다.
            new(-0.55f, 0f, -4.85f), // 깊어진 방을 실제로 활용하되 하우징 가구 앞을 침범하지 않는 왼쪽 깊은 지점이다.
            new(0.65f, 0f, -4.85f), // 두 마리가 깊은 구역에서 추격 놀이할 수 있는 오른쪽 깊은 지점이다.
            new(1.55f, 0f, -3.05f), // 개 침대보다 중앙 쪽에 충분히 떨어진 오른쪽 중간 지점이다.
            new(2.25f, 0f, -1.85f), // 상점과 지도 스탠드의 금지 반경을 피해 돌아오는 오른쪽 지점이다.
            new(2.15f, 0f, -0.35f), // 플레이어 오른쪽 가까운 구간에서도 벽 쪽으로 과하게 퍼지지 않는 지점이다.
            new(0.95f, 0f, 0.45f), // 호출 위치에서 배회로 자연스럽게 이어지는 오른쪽 가까운 지점이다.
            new(0.00f, 0f, 0.10f), // 순환 경로를 이어 주는 중앙 지점이다.
        };

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
            if (!ActiveDogs.Contains(this))
                ActiveDogs.Add(this); // 두 마리가 서로를 찾을 수 있도록 이 개를 현재 로비 개 목록에 등록한다.

            EnsureRuntimeVisual();
            CacheVisualParts();
            CacheGroundSurfaces(); // 로비 바닥과 러그를 한 번만 찾아 두고, 이후 매 프레임 전체 씬 검색을 하지 않게 한다.
            PlaceInFrontOfLobbyCamera();
            OrientVisualFromGeometry();
            NormalizeVisualBounds();
            BuildLegPivots();
            SnapPawsToFloor();
            if (visualRoot != null)
            {
                visualRestPosition = visualRoot.localPosition;
                visualRestRotation = visualRoot.localRotation;
            }
            if (tail != null)
                tailRestRotation = tail.localRotation;
            FitInteractionCollider();
            EnsureAnimatorForAmbientLife(); // 현재 FBX에 이미 사용 가능한 Animator가 있으면 활용하고, 없으면 절차식 생활 동작을 사용한다.
            PickTarget();
            pauseTimer = 0f; // 시작 직후 이유 없이 멀뚱히 서 있지 않고 첫 안전 지점으로 바로 이동한다.
            nextIdleActionTime = Time.time + Random.Range(3.5f, 7f);
        }

        private void OnDestroy()
        {
            ActiveDogs.Remove(this); // 씬을 나가거나 개가 제거될 때 정적 목록에 죽은 참조가 남지 않게 정리한다.
            BreakPlayPair(false); // 장난 중 삭제되면 상대 개도 정상 배회 상태로 돌려놓는다.
        }

        private void EnsureAnimatorForAmbientLife()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true); // 현재 FBX가 이미 Animator를 가지고 있다면 그대로 활용한다.
            if (animator == null || animator.runtimeAnimatorController == null)
                return; // 현재 프로토타입처럼 컨트롤러가 없으면 아래의 절차식 걷기/달리기/수면 자세가 대신 동작한다.

            animator.applyRootMotion = false; // 실제 위치 이동은 이 스크립트가 담당하므로 애니메이션 루트 이동은 끈다.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 시야 밖에서도 생활 행동 시간이 정상적으로 흐르게 한다.
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
            if (celebrateTimer > 0f) celebrateTimer -= Time.deltaTime;
            if (reactionTimer > 0f) reactionTimer -= Time.deltaTime;
            if (tailWagTimer > 0f) tailWagTimer -= Time.deltaTime;
            if (idleBounceTimer > 0f) idleBounceTimer -= Time.deltaTime;
            if (socialCooldown > 0f) socialCooldown -= Time.deltaTime;

            if (reactionTimer > 0f)
            {
                IsMoving = false;
                SetAnimatorSpeed(0f);
                Animate(false);
                return;
            }

            if (called && callTarget != null)
            {
                UpdateCalledMovement(); // 호출은 잠자기/장난/배회보다 항상 우선한다.
                return;
            }

            if (sleepTimer > 0f)
            {
                sleepTimer -= Time.deltaTime;
                IsMoving = false;
                SetAnimatorSpeed(0f);
                HoldSleepingPoseWhenReady(); // 눕기 동작이 끝난 자세에서 멈춰 실제로 몇 초 동안 자는 모습이 유지되게 한다.
                Animate(false);
                if (sleepTimer <= 0f)
                {
                    WakeFromSleep();
                    pauseTimer = 0f; // 잠에서 깬 뒤에도 서서 기다리지 않고 바로 다음 생활 지점으로 움직인다.
                    PickTarget(); // 잠에서 깨면 다시 안전 경로의 다음 지점으로 이동한다.
                }
                return;
            }

            if (playPartner != null)
            {
                UpdatePlayTogether();
                return;
            }

            if (pauseTimer > 0f)
            {
                // pauseTimer는 이제 호출 후 반응이나 명시적인 상호작용처럼 이유가 보이는 짧은 정지에만 사용한다.
                // 일반 경유지에서는 이 타이머를 설정하지 않으므로 방 한가운데에서 이유 없이 멈췄다가 다시 걷는 모습이 나오지 않는다.
                pauseTimer -= Time.deltaTime;
                IsMoving = false;
                SetAnimatorSpeed(0f);
                TryPlayIdleAction();
                Animate(false);
                return;
            }

            MoveTowardCurrentTarget(runningToTarget ? runSpeed : walkSpeed, runningToTarget ? 1f : 0.48f, false);
        }

        private void MoveTowardCurrentTarget(float moveSpeed, float animatorSpeed, bool playing)
        {
            if (TryEscapeFurniture(moveSpeed)) // 이미 가구 금지 반경 안에 들어간 경우에는 목표를 계속 바꾸지 말고 먼저 한 방향으로 빠져나온다.
                return; // 탈출 중인 프레임에는 일반 경로 이동을 섞지 않아 좌우로 덜덜 떠는 현상을 막는다.

            Vector3 flatPosition = transform.localPosition;
            flatPosition.y = 0f;
            Vector3 difference = target - flatPosition;
            difference.y = 0f;

            if (difference.sqrMagnitude <= 0.04f)
            {
                if (playing)
                {
                    PickTarget(true); // 장난 중에는 멈추지 않고 다음 안전 지점을 이어서 달린다.
                    return;
                }
                ChooseAmbientActionAtWaypoint();
                return;
            }

            Vector3 direction = difference.normalized;
            Vector3 worldDirection = transform.parent != null ? transform.parent.TransformDirection(direction) : direction;
            worldDirection += GetDogSeparationDirection() * 0.65f; // 두 마리가 같은 길을 쓸 때 몸이 포개지는 것을 완화한다.
            worldDirection = MushLobbyFurnitureObstacle.FindOpenDirection(
                transform.position, worldDirection, 0.72f, furnitureClearance); // 실제 설치된 가구 Bounds까지 한 번 더 피한다.
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                PickTarget(); // 현재 방향이 완전히 막혔으면 다른 안전 지점을 고르고 다음 프레임에 즉시 재시도한다.
                pauseTimer = 0f; // 장애물을 만났다고 제자리 정지 시간을 만들지 않아 끼임처럼 보이는 멈춤을 줄인다.
                IsMoving = false;
                SetAnimatorSpeed(0f);
                Animate(false);
                return;
            }

            direction = transform.parent != null
                ? transform.parent.InverseTransformDirection(worldDirection).normalized
                : worldDirection.normalized;
            transform.localPosition += direction * (moveSpeed * Time.deltaTime);
            Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, facing, turnSpeed * Time.deltaTime);
            IsMoving = true;
            SetAnimatorSpeed(animatorSpeed);
            Animate(true);
        }

        private bool TryEscapeFurniture(float requestedSpeed)
        {
            if (!MushLobbyFurnitureObstacle.TryGetEscapeDirection(
                    transform.position,
                    furnitureClearance + 0.04f,
                    out Vector3 escapeDirection,
                    out float penetrationDepth))
                return false; // 현재 가구 안에 들어가 있지 않으면 평소 목표 지점 이동을 그대로 사용한다.

            float escapeSpeed = Mathf.Max(requestedSpeed, walkSpeed * 1.45f) +
                                Mathf.Clamp(penetrationDepth * 3.2f, 0f, 1.15f); // 깊게 끼었을수록 조금 더 빠르게 한 방향으로 밀어내 오래 떨지 않게 한다.
            transform.position += escapeDirection * (escapeSpeed * Time.deltaTime); // 가구 중심의 반대 방향으로 실제 개 루트를 이동시킨다.
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(escapeDirection, Vector3.up),
                turnSpeed * Time.deltaTime); // 빠져나오는 방향으로 몸도 자연스럽게 돌린다.
            IsMoving = true; // 탈출 역시 실제 이동 중이므로 절차식 다리 움직임을 유지한다.
            SetAnimatorSpeed(0.68f); // 걷기보다 조금 빠른 발놀림으로 가구에서 빠져나오는 모습을 보여 준다.
            Animate(true); // Animator가 없는 현재 프로토타입 모델에서도 다리가 멈춘 채 미끄러지지 않게 한다.
            return true; // 이 프레임의 이동은 탈출이 처리했음을 호출 측에 알린다.
        }

        private Vector3 GetDogSeparationDirection()
        {
            Vector3 separation = Vector3.zero;
            foreach (MushLobbyDogRoamer other in ActiveDogs)
            {
                if (other == null || other == this) continue;
                Vector3 away = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
                float sqrDistance = away.sqrMagnitude;
                if (sqrDistance <= 0.0001f || sqrDistance > 0.70f * 0.70f) continue;
                separation += away.normalized * (1f - Mathf.Sqrt(sqrDistance) / 0.70f);
            }
            return separation;
        }

        private void ChooseAmbientActionAtWaypoint()
        {
            IsMoving = false;
            SetAnimatorSpeed(0f);
            Animate(false);

            float choice = Random.value;
            if (choice < sleepChance)
            {
                BeginSleep();
                return;
            }
            if (choice < sleepChance + playChance && TryBeginPlayTogether())
                return;

            PickTarget(); // 잠자기나 장난이 선택되지 않았으면 다음 안전 지점으로 즉시 이어서 이동한다.
            pauseTimer = 0f; // 예전 0.8~2.2초 무행동 정지를 제거해 "AI가 멈춘 것 같은" 장면을 없앤다.
        }

        private void BeginSleep()
        {
            sleepTimer = Random.Range(sleepDuration.x, sleepDuration.y); // 이 시간이 끝나거나 B 호출이 오기 전까지 누워 쉰다.
            sleepPoseFrozen = false;
            if (animator != null) animator.speed = 1f; // 눕기 시작 전에는 Animator 재생을 반드시 되살린다.
            pauseTimer = 0f;
            runningToTarget = false;
            SetAnimatorSpeed(0f);
            TriggerAnimation("LieDown");
        }

        private void HoldSleepingPoseWhenReady()
        {
            if (sleepPoseFrozen || animator == null || animator.runtimeAnimatorController == null) return;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("LieDown") || state.normalizedTime < 0.82f) return;
            animator.speed = 0f; // 눕기 클립이 거의 끝난 자세에서 전체 Animator를 멈춰 수면 자세를 유지한다.
            sleepPoseFrozen = true;
        }

        private void WakeFromSleep()
        {
            sleepTimer = 0f;
            sleepPoseFrozen = false;
            if (animator == null) return;
            animator.speed = 1f; // 호출되거나 시간이 끝나면 즉시 다시 애니메이션이 진행되게 한다.
            if (animator.runtimeAnimatorController != null)
                animator.Play("Locomotion", 0, 0f);
        }

        private bool TryBeginPlayTogether()
        {
            if (socialCooldown > 0f) return false;
            foreach (MushLobbyDogRoamer other in ActiveDogs)
            {
                if (other == null || other == this || other.called || other.sleepTimer > 0f ||
                    other.playPartner != null || other.reactionTimer > 0f || other.socialCooldown > 0f)
                    continue;

                float duration = Random.Range(playDuration.x, playDuration.y);
                playPartner = other;
                playLeader = true;
                playTimer = duration;
                other.playPartner = this;
                other.playLeader = false;
                other.playTimer = duration;
                runningToTarget = true;
                other.runningToTarget = true;
                PickTarget(true);
                other.target = other.transform.parent != null
                    ? other.transform.parent.InverseTransformPoint(transform.position)
                    : transform.position;
                return true;
            }
            return false;
        }

        private void UpdatePlayTogether()
        {
            if (playPartner == null) return;
            playTimer -= Time.deltaTime;
            if (playTimer <= 0f || playPartner.called)
            {
                BreakPlayPair(true);
                return;
            }

            if (!playLeader)
            {
                Vector3 chaseWorld = playPartner.transform.position - playPartner.transform.forward * 0.72f;
                chaseWorld += playPartner.transform.right * Mathf.Sin(animationTime * 3.2f) * 0.28f;
                target = transform.parent != null ? transform.parent.InverseTransformPoint(chaseWorld) : chaseWorld;
                target.y = 0f;
            }
            MoveTowardCurrentTarget(runSpeed * (playLeader ? 1f : 1.08f), 1f, true);
        }

        private void BreakPlayPair(bool celebrate)
        {
            MushLobbyDogRoamer other = playPartner;
            playPartner = null;
            playLeader = false;
            playTimer = 0f;
            socialCooldown = socialCooldownDuration;
            if (celebrate)
            {
                tailWagTimer = Mathf.Max(tailWagTimer, 1.5f);
                TriggerAnimation("TailWag");
            }
            PickTarget();
            pauseTimer = 0f; // 장난이 끝난 뒤에도 우두커니 서지 않고 각자 다음 생활 경로로 바로 흩어진다.

            if (other != null && other.playPartner == this)
            {
                other.playPartner = null;
                other.playLeader = false;
                other.playTimer = 0f;
                other.socialCooldown = other.socialCooldownDuration;
                if (celebrate)
                {
                    other.tailWagTimer = Mathf.Max(other.tailWagTimer, 1.5f);
                    other.TriggerAnimation("TailWag");
                }
                other.PickTarget();
                other.pauseTimer = 0f; // 상대 개도 장난 종료 직후 바로 다음 생활 행동으로 이어 간다.
            }
        }

        private void LateUpdate()
        {
            if (poseCorrectionFrames > 0)
            {
                OrientVisualFromGeometry();
                poseCorrectionFrames--;
            }
            bool animatorReady = animator != null && animator.isActiveAndEnabled &&
                                 animator.runtimeAnimatorController != null && animator.avatar != null;
            if (sleepTimer <= 0f || animatorReady)
                SnapPawsToFloor(); // 절차식 수면 중에는 몸을 낮춘 자세를 바닥 보정이 다시 들어 올리지 않게 한다.
            if (poseCorrectionFrames > 0 && visualRoot != null)
            {
                visualRestPosition = visualRoot.localPosition;
                visualRestRotation = visualRoot.localRotation;
            }
        }

        public void CallTo(Transform newTarget)
        {
            if (playPartner != null) BreakPlayPair(false); // 장난 중 호출되면 둘의 놀이를 즉시 끝낸다.
            if (sleepTimer > 0f || sleepPoseFrozen) WakeFromSleep(); // 자는 중이라도 호출은 바로 깨운다.
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

            // 호출 버튼(B 또는 PC 테스트용 Space)을 누른 순간의 위치를 고정한다. WASD는
            // 카메라 회전만 바꾸므로 이미 호출된 개의 목적지는 따라 움직이지 않는다.
            calledDestinationWorld = callTarget.position + forward * callDistance + right * callSideOffset;
            calledDestinationWorld.y = transform.position.y;
            calledLookPointWorld = callTarget.position;
            calledLookPointWorld.y = transform.position.y;
        }

        public void ResumeRoaming()
        {
            called = false;
            sleepTimer = 0f;
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

        private void PickTarget(bool forceRun = false)
        {
            if (SafeRoutePoints.Length == 0) return;

            if (routeIndex < 0)
            {
                float bestDistance = float.PositiveInfinity;
                for (int index = 0; index < SafeRoutePoints.Length; index++)
                {
                    float distance = (SafeRoutePoints[index] - transform.localPosition).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    routeIndex = index;
                }
            }

            int direction = Random.value < 0.5f ? -1 : 1;
            int step = Random.value < 0.22f ? 2 : 1;
            for (int attempt = 0; attempt < SafeRoutePoints.Length; attempt++)
            {
                int candidateIndex = (routeIndex + direction * step + SafeRoutePoints.Length) % SafeRoutePoints.Length;
                Vector3 candidate = SafeRoutePoints[candidateIndex];
                Vector3 worldCandidate = transform.parent != null ? transform.parent.TransformPoint(candidate) : candidate;
                if (!MushLobbyFurnitureObstacle.IsBlocked(worldCandidate, furnitureClearance + 0.12f))
                {
                    routeIndex = candidateIndex;
                    target = candidate;
                    runningToTarget = forceRun || Random.value < randomRunChance;
                    return;
                }
                direction = -direction;
                step = 1;
            }

            target = transform.localPosition; // 모든 후보가 막힌 극단적인 경우에는 가구를 뚫는 대신 제자리에서 다시 판단한다.
            target.y = 0f;
            runningToTarget = false;
        }

        private void Animate(bool walking)
        {
            bool animatorReady = animator != null && animator.isActiveAndEnabled &&
                                 animator.runtimeAnimatorController != null && animator.avatar != null;
            if (animatorReady)
                return;

            bool proceduralSleeping = sleepTimer > 0f;
            if (visualRoot != null)
            {
                Vector3 desiredPosition = proceduralSleeping
                    ? visualRestPosition + Vector3.down * proceduralSleepBodyDrop // 수면 때만 몸을 낮추되 발이 바닥 아래로 잠길 정도로 과하게 내리지 않는다.
                    : visualRestPosition; // 걷기/달리기/대기 중에는 Awake에서 확보한 정상 접지 높이를 그대로 사용한다.
                visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, desiredPosition, Time.deltaTime * 5f); // 수면과 기상 사이를 갑자기 튀지 않게 부드럽게 보간한다.
                visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, visualRestRotation, Time.deltaTime * 7f);
            }

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
                float runBlend = Mathf.InverseLerp(0.48f, 1f, fallbackLocomotionSpeed); // 걷기 0.48, 달리기 1.0 사이를 절차식 보폭에도 반영한다.
                float legRate = Mathf.Lerp(8.5f, 15.5f, runBlend);
                float legSwing = Mathf.Lerp(15f, 24f, runBlend);
                float sleepFold = index < 2 ? 52f : -46f;
                float targetAngle = proceduralSleeping
                    ? sleepFold
                    : walking ? Mathf.Sin(animationTime * legRate + phase) * legSwing : 0f;
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

        private void CacheGroundSurfaces()
        {
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None); // 현재 로비에서 활성화된 Renderer를 한 번만 가져온다.
            List<Renderer> surfaces = new(); // 실제로 접지 높이에 사용할 바닥 계열 Renderer만 추려서 저장한다.

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue; // 파괴 중인 Renderer 같은 예외 참조는 건너뛴다.

                string surfaceName = renderer.gameObject.name; // FBX 파츠 이름으로 바닥, 판재, 러그를 구분한다.
                bool isFloor = surfaceName == "ENV_FloorBase" || surfaceName.StartsWith("ENV_FloorPlank_"); // 오두막 기본 바닥과 개별 판재를 접지 대상으로 인정한다.
                bool isRug = surfaceName == "PROP_CenterRug"; // 러그는 바닥보다 조금 높으므로 별도 표면으로 반드시 포함한다.
                if (isFloor || isRug)
                    surfaces.Add(renderer); // 현재 개가 올라설 수 있는 표면만 캐시에 추가한다.
            }

            groundSurfaceRenderers = surfaces.ToArray(); // 이후 SnapPawsToFloor에서는 이 작은 배열만 검사한다.
        }

        private float ResolveGroundY()
        {
            float resolvedY = transform.position.y; // 표면을 찾지 못한 경우에는 개 루트의 기본 Y를 안전한 바닥 높이로 사용한다.
            Vector3 dogPosition = transform.position; // 현재 개의 월드 X/Z가 어떤 바닥 Renderer 위에 있는지 검사한다.

            if (groundSurfaceRenderers != null)
            {
                foreach (Renderer surface in groundSurfaceRenderers)
                {
                    if (surface == null || !surface.enabled) continue; // 비활성화되거나 삭제된 표면은 접지 후보에서 제외한다.

                    Bounds bounds = surface.bounds; // 렌더러의 실제 월드 Bounds를 사용하므로 러그 두께와 FBX 오프셋을 따로 추측할 필요가 없다.
                    const float edgeTolerance = 0.04f; // 경계선에서 한 프레임씩 바닥 높이가 바뀌는 것을 줄이기 위한 작은 여유다.
                    bool insideX = dogPosition.x >= bounds.min.x - edgeTolerance && dogPosition.x <= bounds.max.x + edgeTolerance; // 개가 표면의 가로 범위 안에 있는지 확인한다.
                    bool insideZ = dogPosition.z >= bounds.min.z - edgeTolerance && dogPosition.z <= bounds.max.z + edgeTolerance; // 개가 표면의 깊이 범위 안에 있는지 확인한다.
                    if (!insideX || !insideZ) continue; // 현재 위치 아래가 아닌 표면은 무시한다.

                    resolvedY = Mathf.Max(resolvedY, bounds.max.y); // 겹치는 바닥과 러그 중 가장 높은 윗면을 실제 발바닥 높이로 선택한다.
                }
            }

            return resolvedY + pawGroundClearance; // 계산된 표면보다 몇 mm만 위에 두어 발 메시의 Z-fighting/파묻힘을 막는다.
        }

        private void SnapPawsToFloor()
        {
            if (visualRoot == null)
                return; // 보이는 개 모델이 아직 준비되지 않았다면 접지 보정을 할 대상이 없다.

            string[] pawNames =
            {
                "Front_L_Paw",
                "Front_R_Paw",
                "Rear_L_Paw",
                "Rear_R_Paw"
            }; // 네 발의 렌더러 최저점을 비교해 실제로 가장 낮은 발을 기준으로 삼는다.

            float lowestPaw = float.PositiveInfinity; // 아직 발을 찾기 전이므로 가장 큰 값에서 시작한다.
            bool foundPaw = false; // 네 발 이름을 하나도 못 찾았을 때 잘못된 위치 보정을 하지 않기 위한 플래그다.
            for (int index = 0; index < pawNames.Length; index++)
            {
                Transform paw = FindPart(visualRoot, pawNames[index]); // 현재 품종 모델에서 해당 발 파츠를 이름으로 찾는다.
                if (paw == null)
                    continue; // 특정 파츠가 없는 모델이라면 나머지 발만으로 계산한다.

                Renderer renderer = paw.GetComponent<Renderer>(); // 발 오브젝트 자체의 Renderer를 먼저 찾는다.
                if (renderer == null)
                    renderer = paw.GetComponentInChildren<Renderer>(true); // Renderer가 자식에 들어 있는 FBX 구조도 처리한다.

                float pawBottom = renderer != null ? renderer.bounds.min.y : paw.position.y; // Renderer가 있으면 메시의 실제 최저점, 없으면 Transform 위치를 대신 사용한다.
                lowestPaw = Mathf.Min(lowestPaw, pawBottom); // 네 발 중 가장 아래에 있는 발바닥 높이를 누적한다.
                foundPaw = true; // 최소 하나의 발 위치를 정상적으로 읽었다.
            }

            if (!foundPaw)
                return; // 발을 전혀 찾지 못했으면 모델을 임의로 움직이지 않는다.

            float floorY = ResolveGroundY(); // 개가 현재 서 있는 위치에서 바닥 판재 또는 러그의 실제 윗면 높이를 구한다.
            visualRoot.position += Vector3.up * (floorY - lowestPaw); // 가장 낮은 발바닥을 그 윗면에 맞추도록 개 비주얼 전체를 함께 이동한다.
        }

        private void UpdateCalledMovement()
        {
            if (TryEscapeFurniture(runSpeed)) // 호출 직전에 가구 옆에 끼어 있었어도 B 호출 방향과 회피 방향을 번갈아 선택하며 진동하지 않게 먼저 빠져나온다.
                return; // 가구에서 벗어난 다음 프레임부터 다시 플레이어 호출 지점으로 이동한다.

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
                float distanceToPlayer = difference.magnitude;
                float callMoveSpeed = distanceToPlayer > 2.1f ? runSpeed : walkSpeed * 1.35f; // 멀리서 부르면 뛰어오고 가까워지면 걷기로 줄인다.
                transform.position += direction * (callMoveSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(direction, Vector3.up),
                    turnSpeed * Time.deltaTime);
                IsMoving = true;
                SetAnimatorSpeed(distanceToPlayer > 2.1f ? 1f : 0.58f);
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
            fallbackLocomotionSpeed = speed; // Animator가 없어도 절차식 다리 애니메이션에서 같은 이동 단계를 사용한다.
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetFloat("Speed", speed, 0.10f, Time.deltaTime);
        }

        private void TriggerAnimation(string parameter)
        {
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetTrigger(parameter);
        }
    }
}
