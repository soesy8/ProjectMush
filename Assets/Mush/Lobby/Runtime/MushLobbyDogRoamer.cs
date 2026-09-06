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
        [SerializeField, HideInInspector] private bool sceneAuthoredVisual;
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
        private Transform head;
        private Quaternion headRestLocalRotation;
        private Quaternion tailRestRotation;
        private Transform[] fallbackLegs;
        private Quaternion[] fallbackLegRestRotations;
        private int poseCorrectionFrames = 8;
        private bool called;
        private bool reachedCallPoint;
        private float callWaitTimer;
        private Vector3 calledDestinationWorld;
        private Vector3 calledLookPointWorld;
        private int lastRoamZone = -1; // 직전에 고른 생활 구역을 기억해 같은 앞쪽 구역만 연속으로 뽑는 현상을 줄인다.
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
        private MushLobbyFireplaceRestSpot reservedFireplaceRest; // 이번 수면 행동에서 이 개가 예약한 벽난로 앞 자리다.
        private int reservedFireplaceSlot = -1; // 벽난로 앞 두 자리 중 어느 쪽을 예약했는지 기억한다.
        private bool walkingToBed; // 내비메시를 따라 침대 앞 접근 지점으로 이동 중인지 나타낸다.
        private bool enteringBed; // NavMeshObstacle 바깥 접근 지점에서 침대 중심까지 짧게 들어가는 중인지 나타낸다.
        private bool leavingBed; // 수면 후 침대 중심에서 다시 내비메시 접근 지점으로 나오는 중인지 나타낸다.
        private Vector3 bedApproachWorld; // NavMeshAgent가 정상적으로 도착할 수 있는 침대 바깥 접근 지점이다.
        private Vector3 bedSleepWorld; // 실제로 누울 침대 중심의 월드 XZ 지점이다.
        private Quaternion bedSleepRotation = Quaternion.identity; // 침대 위에서 누웠을 때 바라볼 방향이다.
        private float sleepSurfaceLift; // 바닥이 아니라 침대 윗면에 몸이 올라가 보이도록 비주얼 루트를 들어 올리는 높이다.
        private MushLobbyFetchBall fetchBall; // 현재 이 개가 담당해 물어오고 있는 공이다.
        private bool returningFetchBall; // 공을 문 뒤 플레이어 앞 반환 지점으로 돌아가는 단계인지 나타낸다.
        private bool waitingForFetchTake; // 플레이어 앞에 도착해 공을 문 채 직접 가져가기를 기다리는 단계다.
        private bool watchingHeldBall; // 플레이어가 든 공을 바라보며 출발을 기다리는 단계다.
        private bool followingFetchReturn; // 다른 개가 공을 잡은 뒤 천천히 플레이어 쪽으로 합류하는 단계다.
        private Transform fetchCarrySocket; // 모델이 바뀌어도 공을 입 근처에 붙일 수 있도록 런타임에 만드는 소켓이다.
        private float ballExcitementPhase; // 여러 개가 완전히 동시에 뛰지 않도록 개별 폴짝 위상을 둔다.
        private Transform lapTarget; // 벽난로 의자에서 무릎 위치를 계산할 플레이어 카메라다.
        private bool lapApproachReached; // 의자 옆의 안전한 바닥 접근점까지 도착했는지 나타낸다.
        private bool mountingLap; // 바닥 접근점에서 무릎으로 올라오는 짧은 전환 중인지 나타낸다.
        private bool sittingOnLap; // 실제 무릎 위치에 도착해 앉아 있는 상태다.
        private Vector3 lapApproachWorld; // 의자를 관통하지 않고 올라갈 수 있는 좌우 바닥 지점이다.
        private Vector3 lapWorld; // 호출 순간 계산한 실제 무릎 위 루트 위치다.
        private Vector3 lapMountStart; // 올라오는 포물선 보간의 시작 위치다.
        private Quaternion lapRotation = Quaternion.identity; // 무릎 위에서 플레이어와 같은 방향을 바라보는 회전이다.
        private float lapMountProgress; // 0~1 사이의 무릎 탑승 진행도다.
        private float lapNextLookTime; // 무릎 위에서 다음에 플레이어를 돌아볼 시각이다.
        private float lapLookEndTime; // 플레이어를 바라보는 짧은 동작이 끝날 시각이다.
        private bool lapLookingAtPlayer;
        private MushLobbyFeedingStation feedingStation; // 현재 이 개에게 밥그릇을 배정한 급식소다.
        private int feedingBowlIndex = -1;
        private Vector3 feedingWorld;
        private Quaternion feedingRotation = Quaternion.identity;
        private float feedingTimer;
        private bool eatingFood;

        private static readonly List<MushLobbyDogRoamer> ActiveDogs = new(); // 로비에 살아 있는 개들을 모아 두 마리 상호작용에 사용한다.

        // NavMesh가 실제 장애물 회피를 담당하므로 더 이상 11개의 고정 점을 시계/반시계로 도는 경로를 사용하지 않는다.
        // 아래 각 Vector4는 (xMin, xMax, zMin, zMax) 형태의 생활 구역이며, 목적지는 매번 구역 안에서 새 좌표를 뽑는다.
        private static readonly Vector4[] RoamZones =
        {
            new(-3.15f, -1.05f, -1.45f, 0.45f), // 플레이어 기준 가까운 왼쪽 구역이다. 항상 여기만 머물지 않도록 lastRoamZone과 함께 사용한다.
            new(1.05f, 3.15f, -1.45f, 0.45f), // 플레이어 기준 가까운 오른쪽 구역이다.
            new(-3.15f, -0.45f, -3.35f, -1.75f), // 산장 중간 왼쪽 넓은 생활 구역이다.
            new(0.45f, 3.15f, -3.35f, -1.75f), // 산장 중간 오른쪽 넓은 생활 구역이다.
            new(-3.10f, -0.20f, -5.20f, -3.65f), // 의자/탁자 코너보다 중앙 쪽에 남겨 둔 깊은 왼쪽 구역이다.
            new(0.20f, 3.10f, -5.20f, -3.65f), // 개 침대 코너를 피해 다닐 수 있는 깊은 오른쪽 구역이다.
            new(-1.55f, 1.55f, -4.80f, -0.85f), // 방 중앙을 앞뒤로 길게 가로지르는 구역으로, 두 마리가 같은 원을 도는 느낌을 깨준다.
        };

        public bool IsMoving { get; private set; }
        public bool IsFetching => fetchBall != null;
        public bool IsFeeding => feedingStation != null;
        public bool IsRestingAtFireplace => reservedFireplaceRest != null && sleepTimer > 0f; // 벽난로 앞에서 누운 채 쓰다듬기 반응을 분리할 때 사용한다.
        public bool IsOnLap => sittingOnLap;
        public bool IsInLapRoutine => lapTarget != null;
        public Transform VisualRoot => visualRoot != null ? visualRoot : transform;
        public bool HasSceneAuthoredVisual => sceneAuthoredVisual && visualRoot != null;

        /// <summary>
        /// Creates and prepares this dog's visible model in edit mode. Once
        /// saved, Awake keeps the authored transform instead of moving the dog
        /// to a camera-relative runtime preview position.
        /// </summary>
        public void BakeVisualIntoScene()
        {
            if (Application.isPlaying)
                return;

            EnsureRuntimeVisual();
            CacheVisualParts();
            CacheGroundSurfaces();
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
            EnsureAnimatorForAmbientLife();
            EnsureNavMeshAgent();
            sceneAuthoredVisual = visualRoot != null;
        }

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
            if (!sceneAuthoredVisual)
            {
                PlaceInFrontOfLobbyCamera();
                OrientVisualFromGeometry();
                NormalizeVisualBounds();
            }
            BuildLegPivots();
            if (!sceneAuthoredVisual)
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
            ReleaseReservedRestSpot(); // 씬 전환 중 휴식 자리를 예약한 채 사라져 다른 개가 영원히 못 쓰는 상태를 막는다.
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
            head = FindPart(visualRoot, prefix + "Head") ?? FindPart(visualRoot, "Head");
            if (head != null)
                headRestLocalRotation = head.localRotation;
            if (tail == null)
                tail = FindPart(visualRoot, prefix + "Tail") ?? FindPart(visualRoot, "Tail");
            ballExcitementPhase = Mathf.Abs(GetInstanceID() % 17) * 0.37f;
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
            head = FindPart(visualRoot, prefix + "Head");
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
                headCollider.isTrigger = true; // 쓰다듬기/마우스 판정용이며 다른 개나 소품을 물리적으로 밀 필요는 없다.
            }

            MushLobbyDogExpression expression = GetComponent<MushLobbyDogExpression>();
            if (expression == null)
                expression = gameObject.AddComponent<MushLobbyDogExpression>();
            Camera camera = null;
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    camera = root.GetComponentInChildren<Camera>(true);
                    if (camera != null)
                        break;
                }
            }
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

            if (fetchBall != null)
            {
                UpdateFetchMovement(); // 공 놀이는 호출·수면·일반 배회보다 우선하며 기존 NavMesh 이동을 그대로 사용한다.
                return;
            }

            if (lapTarget != null)
            {
                UpdateLapMovement(); // 벽난로 무릎 호출은 일반 호출·수면·배회보다 우선하며 좌석을 떠날 때까지 유지한다.
                return;
            }

            if (feedingStation != null)
            {
                UpdateFeedingMovement(); // 사료가 배정된 동안에는 다른 배회·호출 행동보다 밥그릇으로 가서 먹는 행동을 우선한다.
                return;
            }

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
                    if (HasReservedRestSpot())
                    {
                        leavingBed = true; // 침대나 벽난로 앞에서 잤다면 바로 경로를 잡지 말고 먼저 접근 지점으로 걸어나온다.
                        StopNavAgent(true); // 마지막 퇴장 구간 동안 Agent를 잠시 끈다.
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
            worldDirection += GetDogSeparationDirection() * 0.30f; // NavMesh가 없는 예외 상황에서도 여러 마리가 서로를 과하게 밀지 않고 살짝 비켜 간다.
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
            const float separationRange = 0.52f;
            foreach (MushLobbyDogRoamer other in ActiveDogs)
            {
                if (other == null || other == this) continue;
                Vector3 away = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
                float sqrDistance = away.sqrMagnitude;
                if (sqrDistance <= 0.0001f || sqrDistance > separationRange * separationRange) continue;
                separation += away.normalized * (1f - Mathf.Sqrt(sqrDistance) / separationRange);
            }
            return Vector3.ClampMagnitude(separation, 0.45f);
        }

        private void ChooseAmbientActionAtWaypoint()
        {
            IsMoving = false;
            SetAnimatorSpeed(0f);
            Animate(false);

            float choice = Random.value;
            if (choice < sleepChance && TryStartRestSleepJourney())
                return; // 잠은 아무 길바닥에서 시작하지 않고 침대 또는 벽난로 앞의 예약된 자리에서만 시작한다.
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
                    other.walkingToBed || other.enteringBed || other.leavingBed || other.HasReservedRestSpot() ||
                    other.playPartner != null || other.reactionTimer > 0f || other.socialCooldown > 0f ||
                    other.feedingStation != null)
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
            if (sleepTimer <= 0f && !enteringBed && !leavingBed && !watchingHeldBall && lapTarget == null && !eatingFood)
                SnapPawsToFloor(); // 침대 위 수면/진입/퇴장 중에는 바닥 접지가 비주얼을 침대 아래로 끌어내리지 않게 한다.
            if (poseCorrectionFrames > 0 && visualRoot != null)
            {
                visualRestPosition = visualRoot.localPosition;
                visualRestRotation = visualRoot.localRotation;
            }
            if (watchingHeldBall && fetchBall != null)
                ApplyHeldBallAttentionVisuals(); // 몸/Animator 보정이 끝난 뒤 폴짝과 고개 추적을 적용해야 모자 추적기가 같은 최종 자세를 따라간다.
            if (sittingOnLap)
                ApplyLapHeadLook(); // 몸은 무릎을 가로질러 그대로 둔 채 머리만 가끔 플레이어 쪽으로 돌린다.
            else if (eatingFood)
                ApplyFeedingHeadPose(); // 임시 모델도 사료를 먹을 때 머리를 그릇 쪽으로 숙여 서 있기만 하는 모습이 되지 않게 한다.
        }

        public void CallTo(Transform newTarget)
        {
            if (fetchBall != null || feedingStation != null)
                return; // 물어오는 중에는 일반 호출이 공 담당 상태를 중간에 덮어쓰지 않게 한다.
            if (lapTarget != null)
                ClearLapState(true); // 일반 호출로 바뀌면 먼저 의자 옆 바닥으로 안전하게 내려놓는다.
            if (playPartner != null) BreakPlayPair(false); // 장난 중 호출되면 둘의 놀이를 즉시 끝낸다.
            if (walkingToBed)
            {
                walkingToBed = false; // 침대로 가던 중 호출되면 수면 계획을 취소한다.
                ReleaseReservedRestSpot(); // 다른 개가 휴식 자리를 사용할 수 있게 예약도 즉시 푼다.
            }
            if (sleepTimer > 0f || sleepPoseFrozen)
            {
                WakeFromSleep(); // 침대에서 자고 있어도 호출을 받으면 바로 깬다.
                if (HasReservedRestSpot())
                {
                    leavingBed = true; // 다만 휴식 위치에서 바로 NavMeshAgent를 켜지 않고 먼저 접근점까지 빠져나오게 한다.
                    StopNavAgent(true); // 수동 퇴장 중 Agent가 생성되며 튀는 문제를 막는다.
                }
            }
            if (enteringBed)
            {
                enteringBed = false; // 침대에 들어가는 도중 호출되면 방향을 되돌린다.
                leavingBed = HasReservedRestSpot(); // 예약한 휴식 자리가 남아 있으면 접근 지점까지 먼저 빠져나온다.
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

        public bool CallToLap(Transform newTarget)
        {
            if (newTarget == null || fetchBall != null || feedingStation != null)
                return false;
            if (lapTarget == newTarget)
                return true; // 이미 같은 무릎으로 오거나 앉아 있으면 중복 탑승을 시작하지 않는다.

            if (lapTarget != null)
                ClearLapState(true);
            if (playPartner != null)
                BreakPlayPair(false);
            if (sleepTimer > 0f || sleepPoseFrozen)
                WakeFromSleep();
            ReleaseReservedRestSpot();

            called = false;
            reachedCallPoint = false;
            callWaitTimer = 0f;
            reactionTimer = 0f;
            pauseTimer = 0f;
            lapTarget = newTarget;
            CaptureLapPose();
            EnsureNavMeshAgentOnCurrentPosition();
            hasNavDestination = false;
            return true;
        }

        public void LeaveLap()
        {
            if (lapTarget == null)
                return;
            ClearLapState(true);
            PickTarget();
            pauseTimer = 0f;
        }

        private void CaptureLapPose()
        {
            // Head/camera yaw changes independently while seated.  Using it as the
            // lap axis left the dog hanging beside the chair as soon as the player
            // looked toward the fireplace.  The seated rig is the stable body/chair
            // reference; only the headset height is used for the vertical offset.
            MushSeatedRigLock seatedBody = lapTarget.GetComponentInParent<MushSeatedRigLock>();
            Transform bodyReference = seatedBody != null ? seatedBody.transform : lapTarget;
            Vector3 forward = Vector3.ProjectOnPlane(bodyReference.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(bodyReference.right, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.back;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;

            float sideDot = Vector3.Dot(transform.position - lapTarget.position, right);
            float sideSign = Mathf.Abs(sideDot) > 0.05f
                ? Mathf.Sign(sideDot)
                : (Mathf.Max(0, ActiveDogs.IndexOf(this)) % 2 == 0 ? -1f : 1f);
            Vector3 cameraFloor = bodyReference.position;
            cameraFloor.y = transform.position.y;
            lapApproachWorld = cameraFloor + right * (sideSign * 0.90f) + forward * 0.04f;

            lapWorld = bodyReference.position + forward * 0.28f;
            lapWorld.y = Mathf.Max(cameraFloor.y + 0.34f, lapTarget.position.y - 1.28f); // 의자 앞 공중이 아니라 좌석 바로 위의 실제 허벅지 높이로 내린다.
            Vector3 acrossLap = right * (sideSign < 0f ? 1f : -1f);
            lapRotation = Quaternion.LookRotation(acrossLap, Vector3.up); // 개의 몸이 플레이어 정면을 막지 않고 양쪽 무릎을 가로지르도록 옆으로 눕힌다.
            lapApproachReached = false;
            mountingLap = false;
            sittingOnLap = false;
            lapMountProgress = 0f;
            lapLookingAtPlayer = false;
            lapNextLookTime = Time.time + Random.Range(1.2f, 2.8f);
        }

        private void UpdateLapMovement()
        {
            if (!lapApproachReached)
            {
                bool arrived;
                if (!TryNavigateToWorld(lapApproachWorld, runSpeed, 0.85f, 0.24f, out arrived))
                    MoveDirectlyToWorld(lapApproachWorld, runSpeed, 0.85f, 0.24f, out arrived);
                if (!arrived)
                    return;

                lapApproachReached = true;
                mountingLap = true;
                lapMountStart = transform.position;
                lapMountProgress = 0f;
                StopNavAgent(true);
            }

            if (mountingLap)
            {
                lapMountProgress = Mathf.Clamp01(lapMountProgress + Time.deltaTime / 0.72f);
                float smooth = Mathf.SmoothStep(0f, 1f, lapMountProgress);
                Vector3 arcPosition = Vector3.Lerp(lapMountStart, lapWorld, smooth);
                arcPosition += Vector3.up * (Mathf.Sin(lapMountProgress * Mathf.PI) * 0.16f);
                transform.position = arcPosition;
                transform.rotation = Quaternion.Slerp(transform.rotation, lapRotation, Time.deltaTime * 8f);
                IsMoving = true;
                SetAnimatorSpeed(0f);
                Animate(false);
                if (lapMountProgress < 1f)
                    return;

                mountingLap = false;
                sittingOnLap = true;
                transform.SetPositionAndRotation(lapWorld, lapRotation);
                tailWagTimer = Mathf.Max(tailWagTimer, 1.8f);
                TriggerAnimation("LieDown");
            }

            transform.SetPositionAndRotation(lapWorld, lapRotation);
            IsMoving = false;
            SetAnimatorSpeed(0f);
            Animate(false);
        }

        private void ApplyLapHeadLook()
        {
            if (head == null || lapTarget == null)
                return;

            if (!lapLookingAtPlayer && Time.time >= lapNextLookTime)
            {
                lapLookingAtPlayer = true;
                lapLookEndTime = Time.time + Random.Range(1.3f, 2.2f);
            }
            else if (lapLookingAtPlayer && Time.time >= lapLookEndTime)
            {
                lapLookingAtPlayer = false;
                lapNextLookTime = Time.time + Random.Range(2.4f, 4.8f);
            }

            Quaternion restWorldRotation = head.parent != null
                ? head.parent.rotation * headRestLocalRotation
                : headRestLocalRotation;
            Quaternion desiredWorldRotation = restWorldRotation;
            if (lapLookingAtPlayer)
            {
                Vector3 towardPlayer = Vector3.ProjectOnPlane(lapTarget.position - head.position, Vector3.up);
                if (towardPlayer.sqrMagnitude > 0.001f)
                {
                    float lookYaw = Mathf.Clamp(
                        Vector3.SignedAngle(transform.forward, towardPlayer.normalized, Vector3.up),
                        -58f,
                        58f);
                    desiredWorldRotation = Quaternion.AngleAxis(lookYaw, Vector3.up) * restWorldRotation;
                }
            }

            float blend = 1f - Mathf.Exp(-5.5f * Time.deltaTime);
            head.rotation = Quaternion.Slerp(head.rotation, desiredWorldRotation, blend);
        }

        private void ApplyFeedingHeadPose()
        {
            if (head == null)
                return;

            Quaternion restWorldRotation = head.parent != null
                ? head.parent.rotation * headRestLocalRotation
                : headRestLocalRotation;
            float eatingNod = 27f + Mathf.Sin(animationTime * 4.8f) * 4f;
            Quaternion desiredWorldRotation = Quaternion.AngleAxis(eatingNod, transform.right) * restWorldRotation;
            float blend = 1f - Mathf.Exp(-7f * Time.deltaTime);
            head.rotation = Quaternion.Slerp(head.rotation, desiredWorldRotation, blend);
        }

        private void ClearLapState(bool returnToFloor)
        {
            if (lapTarget == null)
                return;

            if (returnToFloor)
            {
                transform.position = lapApproachWorld;
                transform.rotation = lapRotation;
            }
            lapTarget = null;
            lapApproachReached = false;
            mountingLap = false;
            sittingOnLap = false;
            lapMountProgress = 0f;
            lapLookingAtPlayer = false;
            if (head != null)
                head.localRotation = headRestLocalRotation;
            if (animator != null)
                animator.speed = 1f;
            IsMoving = false;
            SetAnimatorSpeed(0f);
            Animate(false);
            EnsureNavMeshAgentOnCurrentPosition();
            hasNavDestination = false;
        }

        public bool TryBeginFeeding(
            MushLobbyFeedingStation station,
            int bowlIndex,
            Vector3 eatingPosition,
            Quaternion eatingFacing)
        {
            if (station == null || fetchBall != null || lapTarget != null || feedingStation != null)
                return false;

            PrepareForBallActivity(); // 공 전용 이름이지만 수면·장난·호출을 안전하게 정리하는 공통 행동 초기화다.
            feedingStation = station;
            feedingBowlIndex = bowlIndex;
            feedingWorld = eatingPosition;
            feedingWorld.y = transform.position.y;
            feedingRotation = eatingFacing;
            feedingTimer = 0f;
            eatingFood = false;
            EnsureNavMeshAgentOnCurrentPosition();
            hasNavDestination = false;
            return true;
        }

        private void UpdateFeedingMovement()
        {
            if (!eatingFood)
            {
                if (!TryNavigateToWorld(feedingWorld, runSpeed, 0.82f, 0.24f, out bool arrived))
                    MoveDirectlyToWorld(feedingWorld, runSpeed, 0.82f, 0.24f, out arrived);
                if (!arrived)
                    return;

                eatingFood = true;
                feedingTimer = 4.2f;
                StopNavAgent(false);
                transform.rotation = feedingRotation;
                PlayEat();
                GetComponent<MushLobbyDogExpression>()?.ShowLoveCelebration(); // 플레이어를 보며 먹기 시작할 때 머리 위로 하트가 올라온다.
            }

            feedingTimer -= Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation, feedingRotation, Time.deltaTime * 8f);
            IsMoving = false;
            SetAnimatorSpeed(0f);
            Animate(false);
            if (feedingTimer > 0f)
                return;

            MushLobbyFeedingStation completedStation = feedingStation;
            int completedBowl = feedingBowlIndex;
            feedingStation = null;
            feedingBowlIndex = -1;
            eatingFood = false;
            feedingTimer = 0f;
            if (head != null)
                head.localRotation = headRestLocalRotation; // 식사가 끝난 뒤 고개를 숙인 절차식 자세가 다음 행동까지 남지 않게 한다.
            completedStation?.CompleteFeeding(completedBowl, this);
            ResumeRoaming();
            Celebrate();
        }

        public bool TryBeginFetch(MushLobbyFetchBall ball)
        {
            if (ball == null || feedingStation != null || (fetchBall != null && fetchBall != ball))
                return false;

            PrepareForBallActivity();
            fetchBall = ball;
            returningFetchBall = false;
            waitingForFetchTake = false;
            watchingHeldBall = false;
            followingFetchReturn = false;
            tailWagTimer = Mathf.Max(tailWagTimer, 2f);
            EnsureNavMeshAgentOnCurrentPosition();
            hasNavDestination = false;
            return true;
        }

        public void WatchHeldBall(MushLobbyFetchBall ball)
        {
            if (ball == null || feedingStation != null)
                return;

            PrepareForBallActivity();
            fetchBall = ball;
            returningFetchBall = false;
            waitingForFetchTake = false;
            watchingHeldBall = true;
            followingFetchReturn = false;
            tailWagTimer = Mathf.Max(tailWagTimer, 0.35f);
            StopNavAgent(false);
            hasNavDestination = false;
        }

        public void FollowFetchWinner(MushLobbyFetchBall ball)
        {
            if (ball == null || feedingStation != null || (fetchBall != null && fetchBall != ball))
                return;

            PrepareForBallActivity();
            fetchBall = ball;
            returningFetchBall = false;
            waitingForFetchTake = false;
            watchingHeldBall = false;
            followingFetchReturn = true;
            EnsureNavMeshAgentOnCurrentPosition();
            hasNavDestination = false;
        }

        public void CancelFetch(MushLobbyFetchBall ball)
        {
            if (fetchBall == null || fetchBall != ball)
                return;

            fetchBall = null;
            returningFetchBall = false;
            waitingForFetchTake = false;
            watchingHeldBall = false;
            followingFetchReturn = false;
            ResumeRoaming();
        }

        private void UpdateFetchMovement()
        {
            if (fetchBall == null)
                return;

            if (watchingHeldBall)
            {
                UpdateHeldBallAttention();
                return;
            }

            if (followingFetchReturn)
            {
                UpdateFetchFollower();
                return;
            }

            if (!returningFetchBall)
            {
                // 던진 직후부터 전부 공 아래의 NavMesh 지점으로 달려간다. 실제 물기는
                // 공이 바닥에 닿고 안정된 뒤에만 허용해 공중으로 솟는 현상은 막는다.
                Vector3 targetPosition = fetchBall.FetchTargetPosition;
                MoveForFetch(targetPosition, runSpeed * 1.14f, 1f, 0.28f, out bool reachedBall);
                float ballDistance = Vector3.ProjectOnPlane(
                    fetchBall.transform.position - transform.position,
                    Vector3.up).magnitude;
                if (!fetchBall.CanBePickedUp)
                    return;
                if (!reachedBall && ballDistance > 0.44f)
                    return;

                Transform carrySocket = GetOrCreateFetchCarrySocket();
                if (carrySocket == null)
                {
                    CancelFetch(fetchBall);
                    return;
                }

                if (!fetchBall.TryAttachToDog(this, carrySocket))
                    return; // 같은 프레임에 다른 개가 먼저 물었으면 공 쪽에서 느린 합류 상태로 전환한다.

                returningFetchBall = true;
                waitingForFetchTake = false;
                tailWagTimer = Mathf.Max(tailWagTimer, 3f);
                hasNavDestination = false;
                return;
            }

            if (waitingForFetchTake)
            {
                WaitForPlayerToTakeFetchBall();
                return;
            }

            Vector3 returnTarget = fetchBall.ReturnWorldPosition;
            MoveForFetch(returnTarget, runSpeed * 1.35f, 1f, 0.34f, out bool reachedPlayer);
            float returnDistance = Vector3.ProjectOnPlane(
                returnTarget - transform.position,
                Vector3.up).magnitude;
            if (!reachedPlayer && returnDistance > 0.48f)
                return;

            waitingForFetchTake = true;
            StopNavAgent(false);
            hasNavDestination = false;
            WaitForPlayerToTakeFetchBall();
        }

        private void WaitForPlayerToTakeFetchBall()
        {
            if (fetchBall == null)
                return;

            Vector3 towardPlayer = Vector3.ProjectOnPlane(
                fetchBall.PlayerWorldPosition - transform.position,
                Vector3.up);
            if (towardPlayer.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(towardPlayer.normalized, Vector3.up),
                    turnSpeed * Time.deltaTime);
            }

            tailWagTimer = Mathf.Max(tailWagTimer, 0.25f);
            IsMoving = false;
            SetAnimatorSpeed(0f);
            Animate(false);
        }

        private void UpdateHeldBallAttention()
        {
            StopNavAgent(false);
            Vector3 towardBall = fetchBall.transform.position - transform.position;
            Vector3 flatTowardBall = Vector3.ProjectOnPlane(towardBall, Vector3.up);
            if (flatTowardBall.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(flatTowardBall.normalized, Vector3.up),
                    turnSpeed * Time.deltaTime);
            }

            IsMoving = false;
            SetAnimatorSpeed(0f);
            tailWagTimer = Mathf.Max(tailWagTimer, 0.25f);
            Animate(false);
        }

        private void ApplyHeldBallAttentionVisuals()
        {
            // 모델 자체를 작게 들어 올려 현재 임시 모델에도 확실히 보이는 들뜬 폴짝 동작을 준다.
            if (visualRoot != null)
            {
                float wave = Mathf.Max(0f, Mathf.Sin(animationTime * 7.2f + ballExcitementPhase));
                float hopHeight = wave * wave * 0.045f; // 모자/머리 소켓이 한 프레임 어긋나도 파츠 안으로 깊게 들어가지 않는 작은 높이로 제한한다.
                Vector3 hopTarget = visualRestPosition + Vector3.up * hopHeight;
                visualRoot.localPosition = Vector3.Lerp(
                    visualRoot.localPosition,
                    hopTarget,
                    Time.deltaTime * 18f);
            }

            // 머리 메시/본만 별도로 돌리면 런타임 모자가 사용하는 독립 추적 좌표와 충돌해
            // 공을 든 동안 모자가 몸 안이나 시야 밖으로 이동한다. 몸 전체가 이미 매 프레임
            // 공을 향하므로 여기서는 머리 파츠를 따로 회전하지 않고 장착 상태를 보존한다.
        }

        private void UpdateFetchFollower()
        {
            Vector3 playerPoint = fetchBall.ReturnWorldPosition;
            Vector3 outward = Vector3.ProjectOnPlane(
                playerPoint - fetchBall.PlayerWorldPosition,
                Vector3.up);
            if (outward.sqrMagnitude < 0.01f)
                outward = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
            if (outward.sqrMagnitude < 0.01f)
                outward = Vector3.forward;
            outward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;

            int dogIndex = Mathf.Max(0, ActiveDogs.IndexOf(this));
            float sideSign = dogIndex % 2 == 0 ? -1f : 1f;
            float sideDistance = 0.46f + (dogIndex / 2) * 0.30f;
            Vector3 joinTarget = playerPoint + outward * 0.34f + right * (sideSign * sideDistance);

            MoveForFetch(joinTarget, runSpeed * 1.15f, 0.92f, 0.28f, out bool reachedJoinPoint);
            if (!reachedJoinPoint)
                return;

            Vector3 towardPlayer = Vector3.ProjectOnPlane(
                fetchBall.PlayerWorldPosition - transform.position,
                Vector3.up);
            if (towardPlayer.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(towardPlayer.normalized, Vector3.up),
                    turnSpeed * Time.deltaTime);
            }
            tailWagTimer = Mathf.Max(tailWagTimer, 0.25f);
            IsMoving = false;
            SetAnimatorSpeed(0f);
            Animate(false);
        }

        private void PrepareForBallActivity()
        {
            if (lapTarget != null)
                ClearLapState(true); // 무릎에 있던 개로 공놀이를 시작하면 먼저 의자 옆 바닥으로 내려놓는다.
            if (playPartner != null)
                BreakPlayPair(false);
            if (sleepTimer > 0f || sleepPoseFrozen)
                WakeFromSleep();
            ReleaseReservedRestSpot();
            called = false;
            reachedCallPoint = false;
            callWaitTimer = 0f;
            reactionTimer = 0f;
            pauseTimer = 0f;
        }

        public void CompleteFetchHandoff(MushLobbyFetchBall ball, Transform playerTarget)
        {
            if (ball == null || fetchBall != ball)
                return;

            fetchBall = null;
            returningFetchBall = false;
            waitingForFetchTake = false;
            watchingHeldBall = false;
            followingFetchReturn = false;
            EnterBallInteractionWait(playerTarget);
        }

        public void EndBallGameAndWait(MushLobbyFetchBall ball, Transform playerTarget)
        {
            if (ball == null || fetchBall != ball)
                return;

            fetchBall = null;
            returningFetchBall = false;
            waitingForFetchTake = false;
            watchingHeldBall = false;
            followingFetchReturn = false;
            EnterBallInteractionWait(playerTarget);
        }

        private void EnterBallInteractionWait(Transform playerTarget)
        {
            if (playerTarget != null)
                callTarget = playerTarget;

            called = callTarget != null;
            reachedCallPoint = called;
            callWaitTimer = called ? unpettedCallWait : 0f;
            pauseTimer = 0f;
            hasNavDestination = false;
            StopNavAgent(false);

            if (called)
            {
                calledDestinationWorld = transform.position;
                calledLookPointWorld = callTarget.position;
                calledLookPointWorld.y = transform.position.y;
                tailWagTimer = Mathf.Max(tailWagTimer, 1.2f);
                IsMoving = false;
                SetAnimatorSpeed(0f);
                Animate(false);
                return;
            }

            ResumeRoaming();
        }

        private void MoveForFetch(
            Vector3 worldDestination,
            float moveSpeed,
            float animatorSpeed,
            float stoppingDistance,
            out bool arrived)
        {
            if (TryNavigateToWorld(worldDestination, moveSpeed, animatorSpeed, stoppingDistance, out arrived))
                return;

            MoveDirectlyToWorld(worldDestination, moveSpeed, animatorSpeed, stoppingDistance, out arrived);
        }

        private Transform GetOrCreateFetchCarrySocket()
        {
            if (fetchCarrySocket != null)
                return fetchCarrySocket;

            Transform mouth = FindPart(visualRoot, "Mouth") ??
                              FindPart(visualRoot, "Muzzle") ??
                              FindPart(visualRoot, "Nose") ??
                              FindPart(visualRoot, "Head");
            Transform parent = mouth != null ? mouth : visualRoot != null ? visualRoot : transform;
            if (parent == null)
                return null;

            Vector3 mouthCenter = parent.position;
            Renderer mouthRenderer = parent.GetComponent<Renderer>();
            if (mouthRenderer == null)
                mouthRenderer = parent.GetComponentInChildren<Renderer>(true);
            if (mouthRenderer != null)
                mouthCenter = mouthRenderer.bounds.center;

            GameObject socket = new("Fetch Ball Mouth Socket");
            fetchCarrySocket = socket.transform;
            fetchCarrySocket.SetParent(parent, true);
            fetchCarrySocket.position = mouthCenter + transform.forward * 0.11f - Vector3.up * 0.025f;
            fetchCarrySocket.rotation = transform.rotation;
            return fetchCarrySocket;
        }

        private void CaptureCalledDestination()
        {
            Vector3 forward = Vector3.ProjectOnPlane(callTarget.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(callTarget.right, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;

            // 호출 버튼(B 또는 PC 테스트용 Space)을 누른 순간의 위치를 고정한다. 이후
            // WASD로 플레이어가 이동해도 이미 호출된 개의 목적지는 따라 움직이지 않는다.
            calledDestinationWorld = callTarget.position + forward * callDistance + right * callSideOffset;
            calledDestinationWorld.y = transform.position.y;
            calledLookPointWorld = callTarget.position;
            calledLookPointWorld.y = transform.position.y;
        }

        public void ResumeRoaming()
        {
            if (lapTarget != null)
                ClearLapState(true);
            called = false;
            fetchBall = null;
            returningFetchBall = false;
            waitingForFetchTake = false;
            watchingHeldBall = false;
            followingFetchReturn = false;
            sleepTimer = 0f;
            reachedCallPoint = false;
            callWaitTimer = 0f;
            ReleaseReservedRestSpot(); // 호출 종료가 수면 행동과 겹친 예외 상황에서도 예약을 남기지 않는다.
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
            if (IsRestingAtFireplace)
                sleepTimer = Mathf.Max(sleepTimer, 4.5f); // 손이 닿아 있는 동안 갑자기 일어나 떠나지 않고 누운 상태를 조금 더 유지한다.
        }

        public void PlayRestingPet()
        {
            tailWagTimer = Mathf.Max(tailWagTimer, 1.45f); // 벽난로 앞에서는 서는 Pet 애니메이션 대신 누운 자세와 꼬리 반응만 유지한다.
            if (sittingOnLap)
            {
                lapLookingAtPlayer = true; // 무릎 위에서 쓰다듬으면 몸 전체를 돌리지 않고 고개만 플레이어 쪽으로 반응한다.
                lapLookEndTime = Time.time + 1.8f;
            }
            SetAnimatorSpeed(0f);
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
            Vector3 currentLocal = transform.localPosition; // 현재 위치와 충분히 떨어진 목적지만 선택하기 위해 개의 로컬 위치를 저장한다.
            currentLocal.y = 0f; // 생활 구역 선택은 바닥 XZ 평면에서만 계산한다.
            const float minimumTravelDistance = 1.15f; // 너무 가까운 점을 연속으로 뽑아 제자리에서 빙글도는 모습을 막는 최소 이동 거리다.

            for (int attempt = 0; attempt < 24; attempt++)
            {
                int zoneIndex = Random.Range(0, RoamZones.Length); // 매번 방 전체의 여러 생활 구역 중 하나를 무작위로 고른다.
                if (RoamZones.Length > 1 && zoneIndex == lastRoamZone && attempt < 12)
                    continue; // 처음 여러 번은 직전 구역을 다시 고르지 않아 앞쪽 한 구역에서 계속 원을 그리는 현상을 줄인다.

                Vector4 zone = RoamZones[zoneIndex]; // 선택한 구역의 X/Z 최소·최대 범위를 가져온다.
                Vector3 candidate = new(
                    Random.Range(zone.x, zone.y),
                    0f,
                    Random.Range(zone.z, zone.w)); // 고정 경유지가 아니라 구역 내부의 실제 임의 좌표를 새 목적지 후보로 만든다.

                if ((candidate - currentLocal).sqrMagnitude < minimumTravelDistance * minimumTravelDistance)
                    continue; // 현재 위치 바로 옆을 뽑았으면 다시 골라 짧은 왕복과 제자리 회전을 막는다.

                Vector3 worldCandidate = transform.parent != null
                    ? transform.parent.TransformPoint(candidate)
                    : candidate; // NavMesh와 가구 Bounds 검사는 월드 좌표를 사용하므로 후보를 월드로 변환한다.

                if (MushLobbyFurnitureObstacle.IsBlocked(worldCandidate, furnitureClearance + 0.12f))
                    continue; // 의자·탁자·상점·집꾸미기·침대 같은 실제 가구 영역 안을 목적지로 고르지 않는다.

                if (MushLobbyNavMeshRuntime.IsReady && NavMesh.SamplePosition(worldCandidate, out NavMeshHit hit, 0.90f, NavMesh.AllAreas))
                {
                    worldCandidate = hit.position; // 후보 주변의 실제 걸을 수 있는 NavMesh 지점으로 스냅한다.
                    candidate = transform.parent != null
                        ? transform.parent.InverseTransformPoint(worldCandidate)
                        : worldCandidate; // Agent와 기존 target 필드가 같은 좌표계를 사용하도록 다시 로컬로 되돌린다.
                    candidate.y = 0f; // 루트 높이는 기존 접지 코드가 관리하므로 목적지는 XZ만 사용한다.
                }
                else if (MushLobbyNavMeshRuntime.IsReady)
                {
                    continue; // 내비메시가 준비됐는데 후보 주변에 길이 없다면 그 점은 버리고 다른 구역을 다시 뽑는다.
                }

                float travelDistance = Vector3.Distance(currentLocal, candidate); // 이번 이동이 방을 가로지르는 긴 이동인지 계산한다.
                target = candidate; // 모든 검사를 통과한 무작위 좌표를 실제 이동 목적지로 확정한다.
                lastRoamZone = zoneIndex; // 다음 목적지에서는 같은 구역을 우선 피할 수 있도록 이번 구역을 기억한다.
                float distanceRunChance = travelDistance >= 3.0f ? 0.68f : randomRunChance; // 먼 구역으로 갈 때는 뛰는 빈도를 높여 걷기만 반복되는 느낌을 줄인다.
                runningToTarget = forceRun || Random.value < distanceRunChance; // 장난 리더는 항상 달리고 일반 생활은 거리와 확률에 따라 걷기/달리기를 섞는다.
                return; // 유효한 목적지를 하나 찾았으므로 이번 선택을 끝낸다.
            }

            Vector3 fallbackWorld = transform.parent != null
                ? transform.parent.TransformPoint(new Vector3(0f, 0f, -2.85f))
                : new Vector3(0f, 0f, -2.85f); // 아주 드물게 모든 무작위 후보가 막혔을 때 사용할 방 중앙 안전 지점을 준비한다.
            if (NavMesh.SamplePosition(fallbackWorld, out NavMeshHit fallbackHit, 1.5f, NavMesh.AllAreas))
            {
                target = transform.parent != null
                    ? transform.parent.InverseTransformPoint(fallbackHit.position)
                    : fallbackHit.position; // 중앙 근처의 실제 NavMesh 지점을 마지막 안전 목적지로 사용한다.
                target.y = 0f; // 바닥 높이는 접지/Agent가 처리하므로 XZ만 유지한다.
                runningToTarget = forceRun || Random.value < randomRunChance; // fallback에서도 걷기/달리기 변화는 유지한다.
                return;
            }

            target = currentLocal; // 내비메시 자체가 없는 비정상 상황에서만 현재 위치를 임시 목표로 남긴다.
            runningToTarget = false; // 길이 없는데 달리기 애니메이션만 재생되는 상황을 막는다.
        }

        private void Animate(bool walking)
        {
            bool animatorReady = animator != null && animator.isActiveAndEnabled &&
                                 animator.runtimeAnimatorController != null && animator.avatar != null;
            bool proceduralSleeping = sleepTimer > 0f;
            bool proceduralLapSitting = sittingOnLap && !animatorReady;
            bool proceduralFeeding = eatingFood && !animatorReady;
            if (visualRoot != null)
            {
                float bedLift = (sleepTimer > 0f || enteringBed || leavingBed) ? sleepSurfaceLift : 0f; // 침대 행동 동안만 바닥보다 높은 침대 윗면 오프셋을 적용한다.
                Vector3 desiredPosition = proceduralSleeping && !animatorReady
                    ? visualRestPosition + Vector3.up * bedLift + Vector3.down * proceduralSleepBodyDrop // 전용 Animator가 없으면 침대 위 높이를 유지하면서 절차식 눕기 자세만큼 몸을 낮춘다.
                    : proceduralLapSitting
                        ? visualRestPosition + Vector3.down * 0.14f // Animator가 없는 임시 모델도 무릎 위에서 몸을 충분히 낮춰 공중에 선 모습이 되지 않게 한다.
                        : proceduralFeeding
                            ? visualRestPosition + Vector3.down * 0.045f // 먹는 동안 몸도 아주 조금 낮춰 고개 숙임과 연결한다.
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
                float lapFold = index < 2 ? 44f : -48f;
                float targetAngle = proceduralSleeping
                    ? sleepFold
                    : proceduralLapSitting
                        ? lapFold
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
                Transform existingPivot = FindExactChild(visualRoot, "Walk Pivot " + index);
                if (existingPivot != null)
                {
                    fallbackLegs[index] = existingPivot;
                    fallbackLegRestRotations[index] = existingPivot.localRotation;
                    continue;
                }

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

        private static Transform FindExactChild(Transform root, string objectName)
        {
            if (root == null)
                return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName)
                    return child;
            }
            return null;
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
            bodyCollider.isTrigger = true; // 실제 밀어내기는 NavMeshAgent가 담당하고 이 캡슐은 클릭/상호작용 범위로만 사용한다.
            bodyCollider.direction = 1;
            bodyCollider.center = transform.InverseTransformPoint(bounds.center);
            bodyCollider.height = Mathf.Max(0.4f, bounds.size.y);
            bodyCollider.radius = Mathf.Max(0.18f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.78f);

            foreach (Collider interactionCollider in visualRoot.GetComponentsInChildren<Collider>(true))
            {
                if (interactionCollider != null)
                    interactionCollider.isTrigger = true; // 씬에 미리 들어 있던 머리 콜라이더도 물리 충돌 없이 판정용으로 통일한다.
            }
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
            navAgent.radius = 0.21f; // 6마리까지 같은 실내를 쓸 수 있도록 겹침 방지에 필요한 최소 여유만 둔다.
            navAgent.height = 0.90f; // 개 높이에 맞춰 Agent 캡슐을 설정한다.
            navAgent.baseOffset = 0f; // 개 루트와 바닥 내비메시 높이를 직접 일치시키므로 별도 수직 오프셋을 두지 않는다.
            navAgent.speed = walkSpeed; // 기본 배회 속도는 기존 걷기 속도와 일치시킨다.
            navAgent.angularSpeed = 540f; // 좁은 실내에서 코너를 부드럽지만 답답하지 않게 돌 수 있는 회전 속도다.
            navAgent.acceleration = 3.2f; // 회피 방향이 바뀔 때 옆으로 갑자기 밀려나는 느낌을 줄인다.
            navAgent.stoppingDistance = 0.18f; // 생활 경유지에서 너무 멀리 떨어져 멈추지 않게 작은 정지 거리를 사용한다.
            navAgent.autoBraking = true; // 단일 목표마다 자연스럽게 감속해 가구에 박히는 느낌을 줄인다.
            navAgent.autoRepath = true; // 하우징 교체로 carving 영역이 바뀌면 자동으로 새 경로를 찾게 한다.
            navAgent.updateRotation = false; // 개 방향은 기존 스크립트가 desiredVelocity 기준으로 부드럽게 회전시켜 비주얼과 일치시킨다.
            navAgent.updateUpAxis = false; // 평평한 로비에서 Agent가 모델의 해부학 축을 건드리지 않게 한다.
            navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance; // 과도한 측면 밀림은 줄이면서 실내 교차 회피는 유지한다.
            int dogIndex = Mathf.Max(0, ActiveDogs.IndexOf(this));
            navAgent.avoidancePriority = 40 + (dogIndex * 7) % 27; // 최대 6마리가 서로 다른 우선순위를 가져 같은 자리에서 힘겨루기하지 않게 한다.
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

        private bool TryStartRestSleepJourney()
        {
            bool preferFireplace = Random.value < 0.5f; // 침대가 있어도 벽난로 앞 휴식이 무작위로 섞이게 한다.
            bool reserved = preferFireplace
                ? TryReserveFireplaceRest() || TryReserveDogBed()
                : TryReserveDogBed() || TryReserveFireplaceRest();
            if (!reserved)
                return false; // 모든 침대와 벽난로 앞 자리가 사용 중이면 이번에는 다른 생활 행동을 고른다.

            walkingToBed = true; // 먼저 휴식 자리 바깥 접근 지점까지 내비메시로 이동한다.
            enteringBed = false; // 아직 실제로 누울 위치로 들어가는 단계는 아니다.
            leavingBed = false; // 수면 전이므로 퇴장 상태도 아니다.
            runningToTarget = false; // 침대에는 뛰어들지 않고 걷기로 접근한다.
            sleepSurfaceLift = 0f; // 침대 윗면 또는 벽난로 앞 바닥 높이는 들어가기 직전에 계산한다.
            return true; // 이번 생활 행동이 예약 수면 루틴으로 전환되었음을 알린다.
        }

        private bool TryReserveDogBed()
        {
            return MushLobbyDogBedSpot.TryReserveNearest(
                this,
                out reservedBed,
                out bedApproachWorld,
                out bedSleepWorld,
                out bedSleepRotation);
        }

        private bool TryReserveFireplaceRest()
        {
            return MushLobbyFireplaceRestSpot.TryReserveRandom(
                this,
                out reservedFireplaceRest,
                out reservedFireplaceSlot,
                out bedApproachWorld,
                out bedSleepWorld,
                out bedSleepRotation);
        }

        private void UpdateWalkToBed()
        {
            if (!HasValidReservedRestSpot())
            {
                walkingToBed = false; // 하우징 교체 등으로 휴식 자리가 사라졌다면 수면 루틴을 즉시 취소한다.
                ReleaseReservedRestSpot(); // 혹시 남은 예약 정보도 정리한다.
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
                sleepSurfaceLift = Mathf.Max(0f, GetReservedRestSurfaceY() - ResolveGroundY()); // 침대 윗면은 올리고 벽난로 앞에서는 바닥 높이를 유지한다.
                return;
            }

            // 내비메시를 사용할 수 없는 아주 예외적인 상태에서도 침대 기능 자체가 멈추지는 않게 기존 직접 이동을 최소 안전망으로 사용한다.
            MoveDirectlyToWorld(bedApproachWorld, walkSpeed, 0.48f, 0.16f, out bool fallbackArrived);
            if (fallbackArrived)
            {
                walkingToBed = false; // 접근점에 도착했으므로 수동 진입 단계로 넘어간다.
                enteringBed = true; // 침대 중심으로 들어갈 준비를 한다.
                StopNavAgent(true); // 혹시 Agent가 반쯤 활성화돼 있다면 확실히 끈다.
                sleepSurfaceLift = Mathf.Max(0f, GetReservedRestSurfaceY() - ResolveGroundY()); // 휴식 자리의 실제 높이를 동일하게 계산한다.
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
                ReleaseReservedRestSpot(); // 다른 개가 이 침대나 벽난로 앞 자리를 사용할 수 있도록 예약을 해제한다.
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

        private bool HasReservedRestSpot()
        {
            return reservedBed != null || reservedFireplaceRest != null;
        }

        private bool HasValidReservedRestSpot()
        {
            if (reservedBed != null)
                return reservedBed.isActiveAndEnabled;
            return reservedFireplaceRest != null &&
                   reservedFireplaceRest.isActiveAndEnabled &&
                   reservedFireplaceRest.IsReservedBy(this, reservedFireplaceSlot);
        }

        private float GetReservedRestSurfaceY()
        {
            if (reservedBed != null)
                return reservedBed.SurfaceY;
            return reservedFireplaceRest != null ? reservedFireplaceRest.SurfaceY : ResolveGroundY();
        }

        private void ReleaseReservedRestSpot()
        {
            if (reservedBed != null)
                reservedBed.Release(this); // 이 개가 예약한 침대만 안전하게 해제한다.
            if (reservedFireplaceRest != null)
                reservedFireplaceRest.Release(this, reservedFireplaceSlot); // 이 개가 예약한 벽난로 앞 자리만 안전하게 해제한다.
            reservedBed = null; // 이후 상태 검사에서 이전 침대를 다시 참조하지 않게 비운다.
            reservedFireplaceRest = null;
            reservedFireplaceSlot = -1;
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
