using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
        private NavMeshAgent navAgent; // 가구와 다른 개를 실제 경로 수준에서 피하면서 이동할 Unity NavMeshAgent다.
        private Vector3 lastNavDestination; // 같은 목적지를 매 프레임 다시 SetDestination하지 않도록 마지막 내비메시 목적지를 저장한다.
        private bool hasNavDestination; // lastNavDestination에 유효한 값이 들어 있는지 나타낸다.
        private MushLobbyDogBedSpot reservedBed; // 이번 수면 행동에서 이 개가 예약한 개 침대다.
        private bool walkingToBed; // 내비메시를 따라 침대 앞 접근 지점으로 이동 중인지 나타낸다.
        private bool enteringBed; // NavMeshObstacle 바깥 접근 지점에서 침대 중심까지 짧게 들어가는 중인지 나타낸다.
        private bool leavingBed; // 수면 후 침대 중심에서 다시 내비메시 접근 지점으로 나오는 중인지 나타낸다.
        private Vector3 bedApproachWorld; // NavMeshAgent가 정상적으로 도착할 수 있는 침대 바깥 접근 지점이다.
        private Vector3 bedSleepWorld; // 실제로 누울 침대 중심의 월드 XZ 지점이다.
        private Quaternion bedSleepRotation = Quaternion.identity; // 침대 위에서 누웠을 때 바라볼 방향이다.
        private float sleepSurfaceLift; // 바닥이 아니라 침대 윗면에 몸이 올라가 보이도록 비주얼 루트를 들어 올리는 높이다.

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
            EnsureNavMeshAgent(); // 로비 전용 내비메시가 준비되어 있으면 이 개를 Agent에 올려 가구와 다른 개를 실제 경로로 피하게 한다.
            PickTarget();
            pauseTimer = 0f; // 시작 직후 이유 없이 멀뚱히 서 있지 않고 첫 안전 지점으로 바로 이동한다.
            nextIdleActionTime = Time.time + Random.Range(3.5f, 7f);
        }

        private void OnDestroy()
        {
            ActiveDogs.Remove(this); // 씬을 나가거나 개가 제거될 때 정적 목록에 죽은 참조가 남지 않게 정리한다.
            ReleaseReservedBed(); // 씬 전환 중 침대를 예약한 채 사라져 다른 개가 영원히 못 쓰는 상태를 막는다.
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

            if (enteringBed || leavingBed)
            {
                UpdateBedTransition(); // 침대 장애물 안팎을 드나드는 마지막 짧은 구간은 Agent를 끄고 직접 이동한다.
                return;
            }

            if (called && callTarget != null)
            {
                UpdateCalledMovement(); // 일반 배회나 놀이보다 호출을 우선하되, 침대에서 나오는 중이면 먼저 안전하게 밖으로 나온다.
                return;
            }

            if (walkingToBed)
            {
                UpdateWalkToBed(); // 잠자기를 선택한 개는 다른 경유지를 고르지 않고 예약한 침대 접근 지점까지 내비메시로 이동한다.
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
                    WakeFromSleep(); // Animator와 절차식 수면 자세를 정상 상태로 되돌린다.
                    pauseTimer = 0f; // 잠에서 깬 뒤에도 의미 없는 대기시간은 두지 않는다.
                    if (reservedBed != null)
                    {
                        leavingBed = true; // 침대에서 잤다면 바로 경로를 잡지 말고 먼저 침대 바깥 접근 지점으로 걸어나온다.
                        StopNavAgent(true); // 침대 자체는 carving 장애물이므로 마지막 퇴장 구간 동안 Agent를 잠시 끈다.
                    }
                    else
                    {
                        PickTarget(); // 침대가 사라진 예외 상황이라면 바로 일반 배회 경로로 복귀한다.
                    }
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
            Vector3 worldTarget = transform.parent != null ? transform.parent.TransformPoint(target) : target; // 기존 안전 경로의 로컬 좌표를 실제 NavMesh 목적지 월드 좌표로 바꾼다.
            if (TryNavigateToWorld(worldTarget, moveSpeed, animatorSpeed, 0.18f, out bool navArrived))
            {
                if (navArrived)
                {
                    if (playing)
                        PickTarget(true); // 장난 중에는 도착 즉시 다음 추격 경유지로 이어 간다.
                    else
                        ChooseAmbientActionAtWaypoint(); // 일반 배회에서는 도착한 순간 잠/놀이/다음 이동 중 하나를 선택한다.
                }
                return; // NavMeshAgent가 정상 작동하면 예전 수동 회피 이동은 전혀 섞지 않는다.
            }

            if (TryEscapeFurniture(moveSpeed)) // NavMesh가 없는 예외 상황에서만 기존 프로토타입 회피를 안전망으로 남긴다.
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
            if (choice < sleepChance && TryStartBedSleepJourney())
                return; // 잠은 아무 길바닥에서 시작하지 않고 장착된 개 침대를 예약할 수 있을 때만 선택한다.
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
                    other.walkingToBed || other.enteringBed || other.leavingBed || other.reservedBed != null ||
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
            if (sleepTimer <= 0f && !enteringBed && !leavingBed)
                SnapPawsToFloor(); // 침대 위 수면/진입/퇴장 중에는 바닥 접지가 비주얼을 침대 아래로 끌어내리지 않게 한다.
            if (poseCorrectionFrames > 0 && visualRoot != null)
            {
                visualRestPosition = visualRoot.localPosition;
                visualRestRotation = visualRoot.localRotation;
            }
        }

        public void CallTo(Transform newTarget)
        {
            if (playPartner != null) BreakPlayPair(false); // 장난 중 호출되면 둘의 놀이를 즉시 끝낸다.
            if (walkingToBed)
            {
                walkingToBed = false; // 침대로 가던 중 호출되면 수면 계획을 취소한다.
                ReleaseReservedBed(); // 다른 개가 침대를 사용할 수 있게 예약도 즉시 푼다.
            }
            if (sleepTimer > 0f || sleepPoseFrozen)
            {
                WakeFromSleep(); // 침대에서 자고 있어도 호출을 받으면 바로 깬다.
                if (reservedBed != null)
                {
                    leavingBed = true; // 다만 침대 안에서 바로 NavMeshAgent를 켜지 않고 먼저 입구까지 빠져나오게 한다.
                    StopNavAgent(true); // carving 영역 내부에서 Agent가 생성되며 튀는 문제를 막는다.
                }
            }
            if (enteringBed)
            {
                enteringBed = false; // 침대에 들어가는 도중 호출되면 방향을 되돌린다.
                leavingBed = reservedBed != null; // 예약한 침대가 남아 있으면 접근 지점까지 먼저 빠져나온다.
                StopNavAgent(true); // 수동 퇴장 구간 동안 Agent는 꺼 둔다.
            }
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
            ReleaseReservedBed(); // 호출 종료가 침대 행동과 겹친 예외 상황에서도 예약을 남기지 않는다.
            EnsureNavMeshAgentOnCurrentPosition(); // 현재 위치를 가까운 NavMesh에 다시 붙여 이후 경로가 안정적으로 이어지게 한다.
            PickTarget();
            pauseTimer = 0f; // 호출이 끝난 뒤에도 이유 없는 멀뚱한 정지를 만들지 않는다.
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
            bool proceduralSleeping = sleepTimer > 0f;
            if (visualRoot != null)
            {
                float bedLift = (sleepTimer > 0f || enteringBed || leavingBed) ? sleepSurfaceLift : 0f; // 침대 행동 동안만 바닥보다 높은 침대 윗면 오프셋을 적용한다.
                Vector3 desiredPosition = proceduralSleeping && !animatorReady
                    ? visualRestPosition + Vector3.up * bedLift + Vector3.down * proceduralSleepBodyDrop // 전용 Animator가 없으면 침대 위 높이를 유지하면서 절차식 눕기 자세만큼 몸을 낮춘다.
                    : visualRestPosition + Vector3.up * bedLift; // 실제 LieDown 애니메이션이 있으면 애니메이션 자세는 그대로 두고 전체 모델 높이만 침대 윗면으로 올린다.
                visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, desiredPosition, Time.deltaTime * 5f); // 수면과 기상 사이를 갑자기 튀지 않게 부드럽게 보간한다.
                visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, visualRestRotation, Time.deltaTime * 7f);
            }

            if (animatorReady)
                return; // 실제 Animator가 준비된 모델은 여기까지의 침대 높이 보정만 받고 다리 절차 애니메이션은 건드리지 않는다.

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
                bool isFloor = surfaceName == "ENV_FloorBase" || surfaceName.StartsWith("ENV_FloorPlank_") ||
                               surfaceName == "Cabin Floor Base" || surfaceName.StartsWith("Cabin Floor Plank "); // 구형 FBX 바닥뿐 아니라 새 절차 산장의 바닥/판재도 실제 접지 표면으로 인정한다.
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
            Vector3 currentWorld = transform.position;
            float navDistanceToPlayer = Vector3.ProjectOnPlane(calledDestinationWorld - currentWorld, Vector3.up).magnitude; // 호출 목적지까지 남은 수평 거리를 계산해 달리기/걷기 속도를 고른다.
            float navCallSpeed = navDistanceToPlayer > 2.1f ? runSpeed : walkSpeed * 1.35f; // 멀면 달리고 가까우면 속도를 낮춰 주인님 앞에서 급정지하지 않게 한다.
            float navAnimatorSpeed = navDistanceToPlayer > 2.1f ? 1f : 0.58f; // 실제 이동 속도와 절차식/Animator 보폭도 같은 단계로 맞춘다.
            if (!reachedCallPoint && TryNavigateToWorld(calledDestinationWorld, navCallSpeed, navAnimatorSpeed, 0.26f, out bool navArrived))
            {
                if (!navArrived)
                    return; // NavMeshAgent가 경로를 따라오는 중이면 가구 회피 수동 이동을 섞지 않는다.
                reachedCallPoint = true; // 내비메시 목적지에 도착했으면 기존 호출 대기 상태로 넘어간다.
                callWaitTimer = unpettedCallWait; // 쓰다듬지 않으면 이 시간 뒤 다시 배회한다.
                StopNavAgent(false); // 주인님 앞에서 기다리는 동안 Agent 이동만 정지하고 컴포넌트는 유지한다.
            }

            if (!reachedCallPoint && TryEscapeFurniture(runSpeed)) // 내비메시를 사용할 수 없는 예외 환경에서만 기존 탈출 로직을 사용한다.
                return; // 가구에서 벗어난 다음 프레임부터 다시 플레이어 호출 지점으로 이동한다.

            currentWorld = transform.position;
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

        private void EnsureNavMeshAgent()
        {
            if (!MushLobbyNavMeshRuntime.IsReady)
                return; // 로비 내비메시가 아직 준비되지 않았다면 기존 수동 이동을 안전망으로 사용한다.

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                return; // 현재 시작점 근처에 내비메시가 없으면 Agent를 억지로 켜서 오류를 만들지 않는다.

            transform.position = hit.position; // Agent를 추가하기 전에 먼저 실제 내비메시 위 좌표로 루트를 맞춘다.
            navAgent = GetComponent<NavMeshAgent>(); // 이미 붙어 있는 Agent가 있다면 중복 생성하지 않고 재사용한다.
            if (navAgent == null)
                navAgent = gameObject.AddComponent<NavMeshAgent>(); // 로비 개에게 Unity 내비메시 이동/회피 기능을 실제로 추가한다.

            navAgent.agentTypeID = 0; // 런타임 바닥 내비메시가 사용하는 기본 Agent Type과 일치시킨다.
            navAgent.radius = 0.28f; // 두 개와 가구 사이에 실제 몸통 여유가 생기도록 개 크기에 맞춘 반지름을 사용한다.
            navAgent.height = 0.90f; // 개 높이에 맞춰 Agent 캡슐을 설정한다.
            navAgent.baseOffset = 0f; // 개 루트와 바닥 내비메시 높이를 직접 일치시키므로 별도 수직 오프셋을 두지 않는다.
            navAgent.speed = walkSpeed; // 기본 배회 속도는 기존 걷기 속도와 일치시킨다.
            navAgent.angularSpeed = 540f; // 좁은 실내에서 코너를 부드럽지만 답답하지 않게 돌 수 있는 회전 속도다.
            navAgent.acceleration = 4.5f; // 출발/정지 때 순간이동처럼 보이지 않도록 적당한 가속을 사용한다.
            navAgent.stoppingDistance = 0.18f; // 생활 경유지에서 너무 멀리 떨어져 멈추지 않게 작은 정지 거리를 사용한다.
            navAgent.autoBraking = true; // 단일 목표마다 자연스럽게 감속해 가구에 박히는 느낌을 줄인다.
            navAgent.autoRepath = true; // 하우징 교체로 carving 영역이 바뀌면 자동으로 새 경로를 찾게 한다.
            navAgent.updateRotation = false; // 개 방향은 기존 스크립트가 desiredVelocity 기준으로 부드럽게 회전시켜 비주얼과 일치시킨다.
            navAgent.updateUpAxis = false; // 평평한 로비에서 Agent가 모델의 해부학 축을 건드리지 않게 한다.
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance; // 두 마리가 마주칠 때 서로 통과하지 않고 적극적으로 피하게 한다.
            navAgent.avoidancePriority = name.IndexOf("Mochi", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 45 : 55; // 두 개가 정면에서 만났을 때 우선순위가 완전히 같아 교착되는 것을 줄인다.
            navAgent.Warp(hit.position); // 설정이 끝난 Agent의 내부 시뮬레이션 위치도 현재 NavMesh 위치와 정확히 맞춘다.
            navAgent.isStopped = false; // 첫 배회 목적지를 바로 따라갈 수 있게 이동 가능 상태로 둔다.
            hasNavDestination = false; // 아직 목적지를 전달하지 않았으므로 다음 이동에서 SetDestination을 실행하게 한다.
        }

        private void EnsureNavMeshAgentOnCurrentPosition()
        {
            if (navAgent == null)
                EnsureNavMeshAgent(); // 아직 Agent가 없으면 로비 내비메시 준비 상태를 확인해 새로 만든다.
            if (navAgent == null)
                return; // 내비메시 자체가 없으면 기존 수동 이동으로 계속 동작한다.

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                return; // 현재 위치 근처에 유효한 NavMesh가 없으면 Agent를 억지로 켜서 "Failed to create agent"류 경고를 만들지 않는다.

            transform.position = hit.position; // Agent를 켜기 전에 개 루트를 실제 NavMesh 위로 먼저 옮긴다.
            if (!navAgent.enabled)
                navAgent.enabled = true; // 침대 안에서는 Agent를 꺼두므로, 안전한 NavMesh 좌표로 나온 뒤에만 다시 켠다.
            navAgent.Warp(hit.position); // Agent 내부 위치도 같은 지점으로 동기화한다.
            navAgent.isStopped = false; // 이후 SetDestination이 즉시 동작하도록 정지를 해제한다.
            hasNavDestination = false; // 이전 침대/호출 목적지는 버리고 새 목적지를 받게 한다.
        }

        private bool TryNavigateToWorld(Vector3 worldDestination, float moveSpeed, float animatorSpeed, float stoppingDistance, out bool arrived)
        {
            arrived = false; // 기본값은 아직 목적지에 도착하지 않은 상태다.
            if (navAgent == null || !navAgent.enabled || !navAgent.isOnNavMesh)
            {
                EnsureNavMeshAgentOnCurrentPosition(); // 씬 시작 직후나 침대 퇴장 직후라면 현재 위치를 내비메시에 다시 붙인다.
                if (navAgent == null || !navAgent.enabled || !navAgent.isOnNavMesh)
                    return false; // 그래도 사용할 수 없으면 호출 측이 기존 수동 이동을 사용하게 한다.
            }

            if (!NavMesh.SamplePosition(worldDestination, out NavMeshHit destinationHit, 0.85f, NavMesh.AllAreas))
                return false; // 목표가 벽/가구 carving 안쪽이라 유효한 내비메시 위치를 찾지 못하면 잘못된 경로를 요청하지 않는다.

            Vector3 sampledDestination = destinationHit.position; // 실제 걸을 수 있는 가장 가까운 내비메시 좌표를 목적지로 사용한다.
            navAgent.speed = moveSpeed; // 걷기/달리기/호출 상황에 맞게 Agent의 최대 속도를 즉시 바꾼다.
            navAgent.stoppingDistance = stoppingDistance; // 행동별로 필요한 도착 여유를 적용한다.
            navAgent.isStopped = false; // 이전 대기/반응에서 멈춰 있었더라도 이번 이동은 다시 시작한다.

            if (!hasNavDestination || (lastNavDestination - sampledDestination).sqrMagnitude > 0.03f * 0.03f)
            {
                navAgent.SetDestination(sampledDestination); // 목적지가 실제로 바뀌었을 때만 새 경로를 계산한다.
                lastNavDestination = sampledDestination; // 다음 프레임의 중복 경로 요청을 막기 위해 저장한다.
                hasNavDestination = true; // 현재 유효한 목적지가 있다는 것을 표시한다.
            }

            if (!navAgent.pathPending && navAgent.pathStatus == NavMeshPathStatus.PathInvalid)
                return false; // 경로가 완전히 불가능하면 기존 안전망 로직이 다른 목표를 선택하게 한다.

            if (!navAgent.pathPending && navAgent.remainingDistance <= stoppingDistance + 0.06f)
            {
                arrived = true; // Agent가 목적지 허용 범위에 들어오면 도착으로 판정한다.
                StopNavAgent(false); // 다음 행동을 결정할 동안 경로를 따라 미세하게 흔들리지 않도록 이동을 정지한다.
                IsMoving = false; // 절차식 다리도 정지시킨다.
                SetAnimatorSpeed(0f); // 실제 Animator가 있다면 Locomotion 블렌드도 0으로 내린다.
                Animate(false); // Animator가 없는 모델의 다리도 중립 자세로 복귀시킨다.
                return true; // NavMeshAgent가 정상적으로 이 이동을 처리했음을 반환한다.
            }

            Vector3 velocity = Vector3.ProjectOnPlane(navAgent.desiredVelocity, Vector3.up); // Unity 회피까지 반영된 실제 희망 속도를 가져온다.
            if (velocity.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(velocity.normalized, Vector3.up),
                    turnSpeed * Time.deltaTime); // 경로 코너와 다른 개 회피 방향을 따라 몸을 자연스럽게 돌린다.
            }

            IsMoving = velocity.sqrMagnitude > 0.0025f; // Agent가 실제로 움직일 때만 걷기/달리기 애니메이션을 재생한다.
            SetAnimatorSpeed(IsMoving ? animatorSpeed : 0f); // 경로 계산 대기나 순간 정지 때 다리가 헛돌지 않게 한다.
            Animate(IsMoving); // 현재 모델 방식에 맞춰 실제 다리/꼬리 움직임을 갱신한다.
            return true; // 현재 이동은 NavMeshAgent가 담당했다.
        }

        private void StopNavAgent(bool disableAgent)
        {
            if (navAgent == null)
                return; // Agent가 없는 fallback 환경에서는 아무것도 할 필요가 없다.

            if (navAgent.enabled && navAgent.isOnNavMesh)
            {
                navAgent.isStopped = true; // 대기/수면/침대 진입 중에는 현재 경로 이동을 확실히 멈춘다.
                navAgent.ResetPath(); // carving이 바뀐 뒤 예전 경로가 다시 살아나는 일을 막기 위해 현재 경로를 비운다.
            }
            hasNavDestination = false; // 다음 이동 시 새 목적지를 강제로 계산하게 한다.
            if (disableAgent)
                navAgent.enabled = false; // 침대 중심처럼 NavMesh 바깥으로 직접 이동할 때만 컴포넌트를 잠시 끈다.
        }

        private bool TryStartBedSleepJourney()
        {
            if (!MushLobbyDogBedSpot.TryReserveNearest(this, out reservedBed, out bedApproachWorld, out bedSleepWorld, out bedSleepRotation))
                return false; // 장착된 개 침대가 없거나 다른 개가 이미 쓰고 있으면 이번에는 잠 대신 다른 생활 행동을 고른다.

            walkingToBed = true; // 먼저 침대 바깥 접근 지점까지 내비메시로 이동한다.
            enteringBed = false; // 아직 침대 안으로 들어가는 단계는 아니다.
            leavingBed = false; // 수면 전이므로 퇴장 상태도 아니다.
            runningToTarget = false; // 침대에는 뛰어들지 않고 걷기로 접근한다.
            sleepSurfaceLift = 0f; // 실제 침대 윗면 높이는 들어가기 직전에 다시 계산한다.
            return true; // 이번 생활 행동이 침대 수면 루틴으로 전환되었음을 알린다.
        }

        private void UpdateWalkToBed()
        {
            if (reservedBed == null || !reservedBed.isActiveAndEnabled)
            {
                walkingToBed = false; // 하우징 교체 등으로 침대가 사라졌다면 수면 루틴을 즉시 취소한다.
                ReleaseReservedBed(); // 혹시 남은 예약 정보도 정리한다.
                PickTarget(); // 일반 생활 경로로 돌아간다.
                return;
            }

            if (TryNavigateToWorld(bedApproachWorld, walkSpeed, 0.48f, 0.16f, out bool arrived))
            {
                if (!arrived)
                    return; // 침대 앞까지는 Unity NavMesh가 가구/다른 개를 피해 안전하게 이동시킨다.

                walkingToBed = false; // 내비메시 구간이 끝났음을 표시한다.
                enteringBed = true; // 이제 짧은 마지막 진입 구간으로 전환한다.
                StopNavAgent(true); // 침대는 carving 장애물이므로 중심으로 들어가는 동안 Agent를 잠시 끈다.
                sleepSurfaceLift = Mathf.Max(0f, reservedBed.SurfaceY - ResolveGroundY()); // 현재 바닥과 침대 윗면 높이 차이를 계산해 비주얼을 침대 위에 올린다.
                return;
            }

            // 내비메시를 사용할 수 없는 아주 예외적인 상태에서도 침대 기능 자체가 멈추지는 않게 기존 직접 이동을 최소 안전망으로 사용한다.
            MoveDirectlyToWorld(bedApproachWorld, walkSpeed, 0.48f, 0.16f, out bool fallbackArrived);
            if (fallbackArrived)
            {
                walkingToBed = false; // 접근점에 도착했으므로 수동 진입 단계로 넘어간다.
                enteringBed = true; // 침대 중심으로 들어갈 준비를 한다.
                StopNavAgent(true); // 혹시 Agent가 반쯤 활성화돼 있다면 확실히 끈다.
                sleepSurfaceLift = Mathf.Max(0f, reservedBed.SurfaceY - ResolveGroundY()); // 침대 높이를 동일하게 계산한다.
            }
        }

        private void UpdateBedTransition()
        {
            Vector3 destination = leavingBed ? bedApproachWorld : bedSleepWorld; // 들어갈 때는 침대 중심, 나올 때는 내비메시 접근 지점을 목표로 한다.
            Quaternion desiredRotation = enteringBed ? bedSleepRotation : transform.rotation; // 침대에 들어갈 때만 누울 방향으로 천천히 몸을 돌린다.
            float transitionSpeed = walkSpeed * 0.78f; // 침대에 오르내리는 짧은 구간은 일반 걷기보다 조금 천천히 움직인다.

            Vector3 flatDifference = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up); // 루트의 Y는 바닥 기준으로 유지하고 XZ만 움직인다.
            if (flatDifference.sqrMagnitude > 0.015f * 0.015f)
            {
                Vector3 direction = flatDifference.normalized; // 침대 중심/접근점으로 향하는 수평 방향을 구한다.
                transform.position += direction * (transitionSpeed * Time.deltaTime); // carving 영역 안팎의 마지막 짧은 거리만 직접 이동한다.
                Quaternion facing = enteringBed ? desiredRotation : Quaternion.LookRotation(direction, Vector3.up); // 진입 시에는 눕는 방향, 퇴장 시에는 나가는 방향을 바라본다.
                transform.rotation = Quaternion.Slerp(transform.rotation, facing, turnSpeed * Time.deltaTime); // 갑자기 방향이 튀지 않게 회전한다.
                IsMoving = true; // 짧은 진입/퇴장도 걷기 동작으로 보이게 한다.
                SetAnimatorSpeed(0.42f); // 천천히 발을 옮기는 정도의 Locomotion 속도를 사용한다.
                Animate(true); // 절차식 모델도 발이 멈춘 채 미끄러지지 않게 한다.
                return;
            }

            transform.position = new Vector3(destination.x, transform.position.y, destination.z); // 도착 순간 XZ를 정확히 고정해 침대 가장자리에서 미세하게 떨지 않게 한다.
            IsMoving = false; // 진입/퇴장 이동이 끝났다.
            SetAnimatorSpeed(0f); // 다리 동작을 멈춘다.

            if (enteringBed)
            {
                enteringBed = false; // 침대 중심 진입을 마쳤다.
                transform.rotation = bedSleepRotation; // 수면 시작 자세가 매번 같은 방향으로 놓이게 한다.
                BeginSleep(); // 이제서야 실제 LieDown/수면 타이머를 시작한다.
                return;
            }

            if (leavingBed)
            {
                leavingBed = false; // 침대 바깥 접근점까지 안전하게 나왔다.
                sleepSurfaceLift = 0f; // 비주얼 높이를 다시 일반 바닥 기준으로 되돌린다.
                ReleaseReservedBed(); // 다른 개가 이 침대를 사용할 수 있도록 예약을 해제한다.
                EnsureNavMeshAgentOnCurrentPosition(); // 접근점의 실제 NavMesh 위치에 Agent를 다시 올린다.
                if (called && callTarget != null)
                    return; // B 호출 때문에 깬 경우 다음 프레임부터 기존 호출 목적지로 바로 간다.
                PickTarget(); // 자연 수면 종료라면 일반 생활 경로를 다시 선택한다.
            }
        }

        private void MoveDirectlyToWorld(Vector3 worldDestination, float speed, float animatorSpeed, float stoppingDistance, out bool arrived)
        {
            Vector3 difference = Vector3.ProjectOnPlane(worldDestination - transform.position, Vector3.up); // fallback 직접 이동에서도 높이는 건드리지 않는다.
            arrived = difference.sqrMagnitude <= stoppingDistance * stoppingDistance; // 지정한 정지 거리 안이면 도착으로 판정한다.
            if (arrived)
            {
                IsMoving = false; // 도착한 프레임에는 이동 애니메이션을 정지한다.
                SetAnimatorSpeed(0f); // Animator 이동 블렌드를 끈다.
                Animate(false); // 절차식 다리도 중립으로 되돌린다.
                return;
            }

            Vector3 direction = difference.normalized; // 목표까지의 수평 방향을 구한다.
            transform.position += direction * (speed * Time.deltaTime); // NavMesh가 없는 예외 환경에서만 직접 이동한다.
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction, Vector3.up), turnSpeed * Time.deltaTime); // 이동 방향으로 몸을 돌린다.
            IsMoving = true; // 실제 이동 중임을 기록한다.
            SetAnimatorSpeed(animatorSpeed); // 걷기/달리기 단계에 맞춘 애니메이터 값을 넣는다.
            Animate(true); // 절차식 다리 움직임도 재생한다.
        }

        private void ReleaseReservedBed()
        {
            if (reservedBed != null)
                reservedBed.Release(this); // 이 개가 예약한 침대만 안전하게 해제한다.
            reservedBed = null; // 이후 상태 검사에서 이전 침대를 다시 참조하지 않게 비운다.
            walkingToBed = false; // 남아 있는 수면 접근 상태도 함께 초기화한다.
            enteringBed = false; // 남아 있는 진입 상태도 초기화한다.
            leavingBed = false; // 남아 있는 퇴장 상태도 초기화한다.
            sleepSurfaceLift = 0f; // 침대 높이 보정도 일반 바닥 기준으로 되돌린다.
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
