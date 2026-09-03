using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace Mush.Lobby
{
    /// <summary>
    /// Runtime fetch-ball prototype for the seated lobby. The ball is grabbed
    /// directly with a nearby Quest grip and launches from pull distance when
    /// that grip is released. Desktop uses a short hold-to-charge throw.
    /// </summary>
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class MushLobbyFetchBall : MonoBehaviour
    {
        private enum BallState
        {
            OnStand,
            Held,
            Thrown,
            Loose,
            Carried,
        }

        private const float BallRadius = 0.09f;
        private const float DirectGrabDistance = 0.25f;
        private const float PullDeadZone = 0.05f;
        private const float FullPullDistance = 0.35f;
        private const float MinimumThrowSpeed = 3f;
        private const float MaximumThrowSpeed = 10f;
        private const float MaximumCollisionSpeed = 11f;
        private const float MaximumSpinSpeed = 18f;
        private const float MaximumDepenetrationSpeed = 1.5f;
        private const float FetchTimeout = 15f;
        private const float LooseResetDelay = 10f;

        private readonly List<Material> ownedMaterials = new();
        private Camera lobbyCamera;
        private MushDesktopSeatedLook desktopLook;
        private Transform seatedBasis;
        private MushLobbyDogRoamer[] dogs;
        private Transform leftHand;
        private Transform rightHand;
        private Rigidbody ballBody;
        private Collider ballCollider;
        private PhysicsMaterial ballPhysicsMaterial;
        private Vector3 standPosition;
        private Vector3 returnPosition;
        private Transform heldHand;
        private Transform carrySocket;
        private MushLobbyDogRoamer assignedDog;
        private Vector3 grabOrigin;
        private Vector3 desktopAimDirection;
        private bool heldByLeftHand;
        private bool desktopHeld;
        private bool desktopAwaitingGrabRelease;
        private bool desktopCharging;
        private bool vrStopButtonWasPressed;
        private bool waitForGripReleaseAfterStop;
        private bool touchedGround;
        private float stateElapsed;
        private float fetchElapsed;
        private float idleElapsed;
        private float desktopHoldElapsed;
        private float collisionEnableDelay;
        private float stableContactElapsed;
        private BallState state;

        public bool CanBePickedUp
        {
            get
            {
                if (state != BallState.Thrown || !touchedGround ||
                    (ballBody != null && ballBody.linearVelocity.sqrMagnitude > 9f && stateElapsed < 1.5f))
                    return false;

                // A horizontal top face on furniture used to count as "ground".
                // Only let the dog collect the ball when its centre is actually
                // close to the lobby's walkable floor/NavMesh height.
                return IsNearWalkableFloor();
            }
        }

        public Vector3 FetchTargetPosition
        {
            get
            {
                if (MushLobbyNavMeshRuntime.IsReady &&
                    NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 1.2f, NavMesh.AllAreas))
                    return hit.position;
                return transform.position;
            }
        }

        public Vector3 ReturnWorldPosition => ResolveReturnWorldPosition();
        public Vector3 PlayerWorldPosition => lobbyCamera != null
            ? lobbyCamera.transform.position
            : ResolveReturnWorldPosition();

        public static MushLobbyFetchBall Install(
            Camera camera,
            MushLobbyDogRoamer[] lobbyDogs,
            Transform lobbyRoot)
        {
            MushLobbyFetchBall existing = FindFirstObjectByType<MushLobbyFetchBall>();
            if (existing != null)
            {
                Transform sceneRoot = lobbyRoot != null ? lobbyRoot : existing.transform.root;
                existing.Configure(
                    camera,
                    lobbyDogs,
                    sceneRoot,
                    existing.GetComponent<Rigidbody>(),
                    existing.transform.position,
                    existing.transform.position);
                return existing;
            }
            if (camera == null)
                return null;

            Transform root = lobbyRoot != null ? lobbyRoot : camera.transform.root;
            MushSeatedRigLock seatedRig = camera.GetComponentInParent<MushSeatedRigLock>();
            Transform placementBasis = seatedRig != null ? seatedRig.transform : camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(placementBasis.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.back;

            Vector3 seatOrigin = camera.transform.position;
            if (seatedRig != null)
            {
                seatOrigin.x = seatedRig.transform.position.x;
                seatOrigin.z = seatedRig.transform.position.z;
            }
            // 공 거치대는 최초 좌석 옆이 아니라 오른쪽 뒤편의 전용 개 놀이
            // 지점에 고정한다. 중앙 바닥은 개와 공이 오갈 수 있게 비워 둔다.
            Vector3 standFloorPoint = root.TransformPoint(new Vector3(3.15f, 0f, 0.90f));
            float floorY = ResolveGroundHeight(standFloorPoint, root.position.y);
            standFloorPoint.y = floorY;
            Vector3 ballStandPosition = standFloorPoint + Vector3.up * 0.83f;

            Vector3 dogReturnPoint = seatOrigin + forward * 1.22f;
            dogReturnPoint.y = ResolveGroundHeight(dogReturnPoint, floorY) + BallRadius + 0.012f;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material wood = CreateMaterial(shader, "Mush Fetch Stand Wood", new Color(0.34f, 0.15f, 0.055f), 0.18f);
            Material rim = CreateMaterial(shader, "Mush Fetch Stand Rim", new Color(0.56f, 0.29f, 0.095f), 0.20f);
            Material ballMaterial = CreateMaterial(shader, "Mush Fetch Ball Orange", new Color(1f, 0.30f, 0.045f), 0.26f);

            GameObject stand = new("Dog Fetch Ball Stand");
            stand.transform.SetParent(root, true);
            stand.transform.position = standFloorPoint;
            CreatePrimitive("Stand Base", PrimitiveType.Cube, stand.transform,
                new Vector3(0f, 0.34f, 0f), new Vector3(0.30f, 0.68f, 0.30f), wood, true);
            CreatePrimitive("Stand Top", PrimitiveType.Cube, stand.transform,
                new Vector3(0f, 0.71f, 0f), new Vector3(0.42f, 0.08f, 0.42f), rim, true);
            CreatePrimitive("Basket Left Rim", PrimitiveType.Cube, stand.transform,
                new Vector3(-0.18f, 0.79f, 0f), new Vector3(0.055f, 0.10f, 0.36f), rim, true);
            CreatePrimitive("Basket Right Rim", PrimitiveType.Cube, stand.transform,
                new Vector3(0.18f, 0.79f, 0f), new Vector3(0.055f, 0.10f, 0.36f), rim, true);
            CreatePrimitive("Basket Front Rim", PrimitiveType.Cube, stand.transform,
                new Vector3(0f, 0.79f, -0.18f), new Vector3(0.31f, 0.10f, 0.055f), rim, true);
            CreatePrimitive("Basket Rear Rim", PrimitiveType.Cube, stand.transform,
                new Vector3(0f, 0.79f, 0.18f), new Vector3(0.31f, 0.10f, 0.055f), rim, true);
            stand.AddComponent<MushLobbyFurnitureObstacle>();

            GameObject ball = CreatePrimitive(
                "Dog Fetch Ball",
                PrimitiveType.Sphere,
                root,
                root.InverseTransformPoint(ballStandPosition),
                Vector3.one * (BallRadius * 2f),
                ballMaterial,
                true);
            ball.transform.SetParent(root, true);
            ball.transform.position = ballStandPosition;
            Rigidbody body = ball.AddComponent<Rigidbody>();
            body.mass = 0.24f;
            body.useGravity = true;
            body.linearDamping = 0.32f;
            body.angularDamping = 1.4f;
            body.maxLinearVelocity = MaximumCollisionSpeed;
            body.maxAngularVelocity = MaximumSpinSpeed;
            body.maxDepenetrationVelocity = MaximumDepenetrationSpeed;
            body.sleepThreshold = 0.025f;
            // This ball is repositioned directly while held. Rigidbody interpolation
            // would blend a release frame back toward the previous held/stand pose,
            // making a correct throw look as if it flew sideways or at the player.
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            MushLobbyFetchBall fetchBall = ball.AddComponent<MushLobbyFetchBall>();
            fetchBall.Configure(camera, lobbyDogs, root, body, ballStandPosition, dogReturnPoint);
            fetchBall.IgnoreCollisionsWith(stand.transform);
            fetchBall.ownedMaterials.Add(wood);
            fetchBall.ownedMaterials.Add(rim);
            fetchBall.ownedMaterials.Add(ballMaterial);
            return fetchBall;
        }

        private void Configure(
            Camera camera,
            MushLobbyDogRoamer[] lobbyDogs,
            Transform searchRoot,
            Rigidbody body,
            Vector3 newStandPosition,
            Vector3 newReturnPosition)
        {
            lobbyCamera = camera;
            desktopLook = camera != null
                ? camera.GetComponentInParent<MushDesktopSeatedLook>()
                : null;
            seatedBasis = camera != null
                ? camera.GetComponentInParent<MushSeatedRigLock>()?.transform
                : null;
            dogs = lobbyDogs;
            ballBody = body;
            ballCollider = GetComponent<Collider>();
            ConfigureStableCollisionMaterial();
            standPosition = newStandPosition;
            returnPosition = newReturnPosition;
            IgnorePlayerRigCollisions(camera);
            IgnoreDogCollisions();
            leftHand = FindNamedTransform(searchRoot, "Lobby Left Hand", "Left Controller", "Left Hand Model");
            rightHand = FindNamedTransform(searchRoot, "Lobby Right Hand", "Right Controller", "Right Hand Model");
            ReturnToStand();
        }

        private void IgnorePlayerRigCollisions(Camera camera)
        {
            if (camera == null || ballCollider == null)
                return;

            MushSeatedRigLock seatedRig = camera.GetComponentInParent<MushSeatedRigLock>();
            if (seatedRig == null)
                return;

            foreach (Collider playerCollider in seatedRig.GetComponentsInChildren<Collider>(true))
            {
                if (playerCollider != null && playerCollider != ballCollider)
                    Physics.IgnoreCollision(ballCollider, playerCollider, true);
            }
        }

        private void IgnoreCollisionsWith(Transform ignoredRoot)
        {
            if (ballCollider == null || ignoredRoot == null)
                return;

            foreach (Collider ignoredCollider in ignoredRoot.GetComponentsInChildren<Collider>(true))
            {
                if (ignoredCollider != null && ignoredCollider != ballCollider)
                    Physics.IgnoreCollision(ballCollider, ignoredCollider, true);
            }
        }

        private void IgnoreDogCollisions()
        {
            if (dogs == null)
                return;

            foreach (MushLobbyDogRoamer dog in dogs)
            {
                if (dog != null)
                    IgnoreCollisionsWith(dog.transform);
            }
        }

        private void ConfigureStableCollisionMaterial()
        {
            if (ballCollider == null)
                return;

            // Runtime primitives otherwise inherit Unity's default contact
            // response. A small, fast sphere can repeatedly gain visible
            // separation speed when it touches floor/furniture edges. Force a
            // non-bouncy, moderately grippy contact for this ball only.
            ballPhysicsMaterial = new PhysicsMaterial("Mush Fetch Ball Stable Physics")
            {
                bounciness = 0f,
                staticFriction = 0.45f,
                dynamicFriction = 0.32f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
                frictionCombine = PhysicsMaterialCombine.Average,
            };
            ballCollider.sharedMaterial = ballPhysicsMaterial;
        }

        private void Update()
        {
            bool stopRequested = ReadStopRequest();
            if (stopRequested && state != BallState.OnStand)
            {
                waitForGripReleaseAfterStop = XRSettings.isDeviceActive;
                ReturnToStand();
                return;
            }

            if (XRSettings.isDeviceActive)
                UpdateVrGrab();
            else
                UpdateDesktopGrab();

            if (collisionEnableDelay > 0f)
            {
                collisionEnableDelay -= Time.deltaTime;
                if (collisionEnableDelay <= 0f && ballCollider != null)
                    ballCollider.enabled = true;
            }

            if (state == BallState.Thrown)
            {
                fetchElapsed += Time.deltaTime;
                if (fetchElapsed >= FetchTimeout)
                {
                    ReturnToStand();
                    return;
                }
            }

            if (state == BallState.Carried)
            {
                if (carrySocket != null)
                {
                    transform.SetPositionAndRotation(carrySocket.position, carrySocket.rotation);
                    return;
                }

                ReturnToStand();
                return;
            }

            if (state == BallState.Held || state == BallState.OnStand)
                return;

            stateElapsed += Time.deltaTime;
            if (transform.position.y < standPosition.y - 3f ||
                Vector3.Distance(transform.position, standPosition) > 14f)
            {
                ReturnToStand();
                return;
            }

            if (state == BallState.Thrown)
                return;

            bool nearlyStopped = ballBody == null ||
                                 (ballBody.linearVelocity.sqrMagnitude < 0.025f &&
                                  ballBody.angularVelocity.sqrMagnitude < 0.25f);
            idleElapsed = nearlyStopped ? idleElapsed + Time.deltaTime : 0f;
            if (idleElapsed >= LooseResetDelay)
                ReturnToStand();
        }

        private void LateUpdate()
        {
            if (state == BallState.Carried && carrySocket != null)
                transform.SetPositionAndRotation(carrySocket.position, carrySocket.rotation);
        }

        private void UpdateVrGrab()
        {
            if (leftHand == null || rightHand == null)
            {
                Transform searchRoot = lobbyCamera != null ? lobbyCamera.transform.root : transform.root;
                leftHand ??= FindNamedTransform(searchRoot, "Lobby Left Hand", "Left Controller", "Left Hand Model");
                rightHand ??= FindNamedTransform(searchRoot, "Lobby Right Hand", "Right Controller", "Right Hand Model");
            }

            bool leftGrip = ReadGrip(XRNode.LeftHand);
            bool rightGrip = ReadGrip(XRNode.RightHand);
            if (waitForGripReleaseAfterStop)
            {
                if (!leftGrip && !rightGrip)
                    waitForGripReleaseAfterStop = false;
                return;
            }

            if (state == BallState.Held && !desktopHeld)
            {
                bool activeGrip = heldByLeftHand ? leftGrip : rightGrip;
                if (!activeGrip)
                    ReleaseVrThrow();
                else if (heldHand != null)
                    transform.position = heldHand.position + heldHand.forward * 0.075f;
                return;
            }

            if (state == BallState.Held)
                return;

            float leftDistance = leftHand != null ? Vector3.Distance(leftHand.position, transform.position) : float.PositiveInfinity;
            float rightDistance = rightHand != null ? Vector3.Distance(rightHand.position, transform.position) : float.PositiveInfinity;
            bool leftCanGrab = leftGrip && CanGrabFromHand(leftHand, leftDistance);
            bool rightCanGrab = rightGrip && CanGrabFromHand(rightHand, rightDistance);
            if (leftCanGrab && (!rightCanGrab || leftDistance <= rightDistance))
                BeginVrGrab(leftHand, true);
            else if (rightCanGrab)
                BeginVrGrab(rightHand, false);
        }

        private bool CanGrabFromHand(Transform hand, float distance)
        {
            if (hand == null)
                return false;
            if (distance <= DirectGrabDistance)
                return true;
            if (state == BallState.Carried || distance > 1.30f)
                return false;

            // The waist-high stand is visible but can sit just beyond a
            // comfortable seated arm reach. Pointing either hand at the ball
            // and pressing Grip therefore also snaps it into that hand; UI
            // panels still use Trigger and are unaffected.
            Vector3 directionToBall = (transform.position - hand.position).normalized;
            return Vector3.Dot(hand.forward, directionToBall) >= 0.96f;
        }

        private void BeginVrGrab(Transform hand, bool left)
        {
            if (hand == null)
                return;

            PrepareAssignedDogForPlayerGrab();
            heldHand = hand;
            heldByLeftHand = left;
            desktopHeld = false;
            desktopAwaitingGrabRelease = false;
            desktopCharging = false;
            grabOrigin = hand.position;
            SetHeldPhysics();
            transform.position = hand.position + hand.forward * 0.075f;
        }

        private void ReleaseVrThrow()
        {
            if (heldHand == null)
            {
                ReleaseLoose(transform.position);
                return;
            }

            Vector3 pullVector = grabOrigin - heldHand.position;
            float pullDistance = pullVector.magnitude;
            if (pullDistance < PullDeadZone)
            {
                Vector3 dropForward = lobbyCamera != null ? lobbyCamera.transform.forward : heldHand.forward;
                ReleaseLoose(heldHand.position + dropForward.normalized * 0.12f);
                return;
            }

            float strength = Mathf.InverseLerp(PullDeadZone, FullPullDistance, pullDistance);
            float launchSpeed = Mathf.Lerp(MinimumThrowSpeed, MaximumThrowSpeed, strength);
            Vector3 launchDirection = pullVector.normalized;
            Vector3 launchPosition = heldHand.position + launchDirection * 0.18f;
            ReleaseThrow(launchPosition, launchDirection * launchSpeed);
        }

        private void UpdateDesktopGrab()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || lobbyCamera == null)
                return;

            Ray pointerRay = lobbyCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (!desktopHeld && mouse.leftButton.wasPressedThisFrame &&
                PointerHitsBall(pointerRay))
            {
                PrepareAssignedDogForPlayerGrab();
                desktopHeld = true;
                desktopAwaitingGrabRelease = true;
                desktopCharging = false;
                desktopHoldElapsed = 0f;
                heldHand = null;
                SetHeldPhysics();
            }

            if (!desktopHeld)
                return;

            // Tracked Pose Driver can reset the camera Transform during Update,
            // while MushDesktopSeatedLook applies the visible desktop rotation in
            // LateUpdate. Read the latter's stored yaw/pitch instead of the stale XR
            // camera direction so the throw matches the direction shown on screen.
            desktopAimDirection = desktopLook != null
                ? desktopLook.CurrentWorldViewDirection
                : lobbyCamera.transform.forward.normalized;
            transform.position = lobbyCamera.transform.position
                                 + desktopAimDirection * 0.70f;

            // The click that picked up the ball must finish without also throwing
            // it. A separate second press is used to charge the desktop throw.
            if (desktopAwaitingGrabRelease)
            {
                if (mouse.leftButton.wasReleasedThisFrame)
                    desktopAwaitingGrabRelease = false;
                return;
            }

            if (!desktopCharging && mouse.leftButton.wasPressedThisFrame)
            {
                desktopCharging = true;
                desktopHoldElapsed = 0f;
            }
            if (!desktopCharging)
                return;

            desktopHoldElapsed += Time.deltaTime;
            if (!mouse.leftButton.wasReleasedThisFrame)
                return;

            float strength = Mathf.Clamp01(desktopHoldElapsed / 0.9f);
            float speed = Mathf.Lerp(MinimumThrowSpeed, MaximumThrowSpeed, strength);
            ReleaseThrow(
                transform.position + desktopAimDirection * 0.12f,
                desktopAimDirection * speed,
                0.18f);
        }

        private bool PointerHitsBall(Ray pointerRay)
        {
            if (ballCollider == null || !ballCollider.enabled)
                return false;

            RaycastHit[] hits = Physics.RaycastAll(
                pointerRay,
                12f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == ballCollider)
                    return true;
            }
            return false;
        }

        private void SetHeldPhysics()
        {
            state = BallState.Held;
            stateElapsed = 0f;
            fetchElapsed = 0f;
            idleElapsed = 0f;
            stableContactElapsed = 0f;
            touchedGround = false;
            carrySocket = null;
            if (ballBody != null)
            {
                if (!ballBody.isKinematic)
                {
                    ballBody.linearVelocity = Vector3.zero;
                    ballBody.angularVelocity = Vector3.zero;
                }
                ballBody.isKinematic = true;
            }
            if (ballCollider != null)
                ballCollider.enabled = false;
            NotifyAllDogsWatching();
        }

        private void ReleaseThrow(Vector3 position, Vector3 velocity, float collisionDelay = 0f)
        {
            heldHand = null;
            desktopHeld = false;
            desktopAwaitingGrabRelease = false;
            desktopCharging = false;
            state = BallState.Thrown;
            stateElapsed = 0f;
            fetchElapsed = 0f;
            idleElapsed = 0f;
            stableContactElapsed = 0f;
            touchedGround = false;
            collisionEnableDelay = Mathf.Max(0f, collisionDelay);
            transform.position = position;
            EnableDynamicPhysics(velocity, collisionEnableDelay <= 0f);
            StartAllDogsFetchRace();
        }

        private void ReleaseLoose(Vector3 position)
        {
            EndAllDogBallActivity();
            heldHand = null;
            desktopHeld = false;
            desktopAwaitingGrabRelease = false;
            desktopCharging = false;
            state = BallState.Loose;
            stateElapsed = 0f;
            fetchElapsed = 0f;
            idleElapsed = 0f;
            stableContactElapsed = 0f;
            touchedGround = false;
            collisionEnableDelay = 0f;
            transform.position = position;
            EnableDynamicPhysics(Vector3.zero);
        }

        private void EnableDynamicPhysics(Vector3 velocity, bool enableCollider = true)
        {
            if (ballCollider != null)
                ballCollider.enabled = enableCollider;
            if (ballBody == null)
                return;

            ballBody.interpolation = RigidbodyInterpolation.None;
            ballBody.position = transform.position;
            ballBody.isKinematic = false;
            ballBody.linearVelocity = Vector3.ClampMagnitude(velocity, MaximumCollisionSpeed);
            ballBody.angularVelocity = velocity.sqrMagnitude > 0.01f
                ? Vector3.Cross(Vector3.up, velocity.normalized) * Mathf.Min(velocity.magnitude * 1.2f, MaximumSpinSpeed)
                : Vector3.zero;
            ballBody.WakeUp();
        }

        private void StartAllDogsFetchRace()
        {
            assignedDog = null;
            if (dogs == null)
                return;

            foreach (MushLobbyDogRoamer dog in dogs)
            {
                if (dog != null)
                    dog.TryBeginFetch(this);
            }
        }

        public bool TryAttachToDog(MushLobbyDogRoamer dog, Transform socket)
        {
            if (dog == null || socket == null || state != BallState.Thrown ||
                assignedDog != null || !CanBePickedUp)
                return false;

            assignedDog = dog; // 가장 먼저 이 메서드에 성공한 한 마리만 공을 문 승자가 된다.
            state = BallState.Carried;
            stateElapsed = 0f;
            idleElapsed = 0f;
            carrySocket = socket;
            collisionEnableDelay = 0f;
            if (ballBody != null)
            {
                if (!ballBody.isKinematic)
                {
                    ballBody.linearVelocity = Vector3.zero;
                    ballBody.angularVelocity = Vector3.zero;
                }
                ballBody.isKinematic = true;
            }
            if (ballCollider != null)
                ballCollider.enabled = true;
            transform.SetPositionAndRotation(socket.position, socket.rotation);
            NotifyFetchFollowers(dog);
            return true;
        }

        private void PrepareAssignedDogForPlayerGrab()
        {
            if (state != BallState.Carried || assignedDog == null)
            {
                CancelAllDogsForRegrab();
                return;
            }

            MushLobbyDogRoamer dog = assignedDog;
            assignedDog = null;
            carrySocket = null;
            dog.CompleteFetchHandoff(this, lobbyCamera != null ? lobbyCamera.transform : null);
        }

        public void DeliverFromDog(MushLobbyDogRoamer dog)
        {
            if (dog == null || dog != assignedDog)
                return;

            assignedDog = null;
            carrySocket = null;
            state = BallState.Loose;
            stateElapsed = 0f;
            fetchElapsed = 0f;
            idleElapsed = 0f;
            stableContactElapsed = 0f;
            touchedGround = true;
            returnPosition = ResolveReturnWorldPosition();
            transform.position = returnPosition;
            if (ballBody != null)
            {
                if (!ballBody.isKinematic)
                {
                    ballBody.linearVelocity = Vector3.zero;
                    ballBody.angularVelocity = Vector3.zero;
                }
                ballBody.position = returnPosition;
                ballBody.isKinematic = true;
            }
            if (ballCollider != null)
                ballCollider.enabled = true;
        }

        private void NotifyAllDogsWatching()
        {
            if (dogs == null)
                return;

            foreach (MushLobbyDogRoamer dog in dogs)
            {
                if (dog != null)
                    dog.WatchHeldBall(this);
            }
        }

        private void NotifyFetchFollowers(MushLobbyDogRoamer winner)
        {
            if (dogs == null)
                return;

            foreach (MushLobbyDogRoamer dog in dogs)
            {
                if (dog != null && dog != winner)
                    dog.FollowFetchWinner(this);
            }
        }

        private void CancelAllDogsForRegrab()
        {
            assignedDog = null;
            carrySocket = null;
            if (dogs == null)
                return;

            foreach (MushLobbyDogRoamer dog in dogs)
                dog?.CancelFetch(this);
        }

        private void EndAllDogBallActivity()
        {
            assignedDog = null;
            carrySocket = null;
            if (dogs == null)
                return;

            Transform playerTarget = lobbyCamera != null ? lobbyCamera.transform : null;
            foreach (MushLobbyDogRoamer dog in dogs)
                dog?.EndBallGameAndWait(this, playerTarget);
        }

        private void ReturnToStand()
        {
            bool wasBallActivity = state != BallState.OnStand;
            if (wasBallActivity)
                EndAllDogBallActivity();
            heldHand = null;
            desktopHeld = false;
            desktopAwaitingGrabRelease = false;
            desktopCharging = false;
            carrySocket = null;
            state = BallState.OnStand;
            stateElapsed = 0f;
            fetchElapsed = 0f;
            idleElapsed = 0f;
            stableContactElapsed = 0f;
            touchedGround = false;
            collisionEnableDelay = 0f;
            transform.SetPositionAndRotation(standPosition, Quaternion.identity);
            if (ballCollider != null)
                ballCollider.enabled = true;
            if (ballBody != null)
            {
                if (!ballBody.isKinematic)
                {
                    ballBody.linearVelocity = Vector3.zero;
                    ballBody.angularVelocity = Vector3.zero;
                }
                ballBody.isKinematic = true;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if ((state != BallState.Thrown && state != BallState.Loose) || ballBody == null)
                return;

            StabilizeCollision(collision, true);
        }

        private void OnCollisionStay(Collision collision)
        {
            if ((state != BallState.Thrown && state != BallState.Loose) || ballBody == null)
                return;

            StabilizeCollision(collision, false);
        }

        private void OnCollisionExit(Collision collision)
        {
            stableContactElapsed = 0f;
        }

        private void StabilizeCollision(Collision collision, bool firstContact)
        {
            Vector3 stableNormal = Vector3.zero;
            bool hasUpwardContact = false;
            for (int index = 0; index < collision.contactCount; index++)
            {
                Vector3 contactNormal = collision.GetContact(index).normal;
                stableNormal += contactNormal;
                if (contactNormal.y > 0.45f)
                    hasUpwardContact = true;
            }

            bool stableFloorContact = hasUpwardContact && IsNearWalkableFloor();
            if (stableFloorContact)
                touchedGround = true;

            // Wall, ceiling and beam contacts must be allowed to separate under
            // gravity. Reapplying the floor damping there removes the falling
            // velocity and can pin the ball in mid-air.
            if (!firstContact && !stableFloorContact)
            {
                stableContactElapsed = 0f;
                return;
            }

            if (stableNormal.sqrMagnitude > 0.001f)
            {
                stableNormal.Normalize();
                Vector3 velocity = Vector3.ClampMagnitude(ballBody.linearVelocity, MaximumCollisionSpeed);
                float separatingSpeed = Vector3.Dot(velocity, stableNormal);
                if (separatingSpeed > 0f)
                    velocity -= stableNormal * separatingSpeed * (firstContact ? 0.90f : 1f);

                // Preserve enough tangential motion to roll, but remove the
                // contact energy that caused repeated shaking and ricochets.
                ballBody.linearVelocity = velocity * (firstContact ? 0.78f : 0.96f);
                ballBody.angularVelocity = Vector3.ClampMagnitude(
                    ballBody.angularVelocity * (firstContact ? 0.55f : 0.92f),
                    MaximumSpinSpeed);
            }

            bool slowContact = stableFloorContact &&
                               ballBody.linearVelocity.sqrMagnitude < 0.16f &&
                               ballBody.angularVelocity.sqrMagnitude < 2.25f;
            stableContactElapsed = slowContact
                ? stableContactElapsed + Time.fixedDeltaTime
                : 0f;
            if (stableContactElapsed >= 0.12f)
            {
                ballBody.linearVelocity = Vector3.zero;
                ballBody.angularVelocity = Vector3.zero;
                ballBody.Sleep();
            }
        }

        private bool IsNearWalkableFloor()
        {
            if (MushLobbyNavMeshRuntime.IsReady &&
                NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 1.2f, NavMesh.AllAreas))
            {
                float expectedBallCenterY = hit.position.y + BallRadius;
                return Mathf.Abs(transform.position.y - expectedBallCenterY) <= 0.18f;
            }

            // Lobby floor is flat; this fallback is only used during the brief
            // frame before its runtime NavMesh has finished building.
            return Mathf.Abs(transform.position.y - ResolveReturnWorldPosition().y) <= 0.18f;
        }

        private Vector3 ResolveReturnWorldPosition()
        {
            if (seatedBasis == null)
                return returnPosition;

            Vector3 forward = Vector3.ProjectOnPlane(seatedBasis.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.back;

            Vector3 target = seatedBasis.position + forward * 1.22f;
            target.y = returnPosition.y;
            return target;
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying)
                return;
            foreach (Material material in ownedMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
            ownedMaterials.Clear();
            if (ballPhysicsMaterial != null)
                Destroy(ballPhysicsMaterial);
        }

        private static bool ReadGrip(XRNode node)
        {
            UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid &&
                   device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool pressed) &&
                   pressed;
        }

        private bool ReadStopRequest()
        {
            if (!XRSettings.isDeviceActive)
            {
                vrStopButtonWasPressed = false;
                Mouse mouse = Mouse.current;
                return mouse != null && mouse.rightButton.wasPressedThisFrame;
            }

            UnityEngine.XR.InputDevice leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool pressed = leftController.isValid &&
                           leftController.TryGetFeatureValue(
                               UnityEngine.XR.CommonUsages.secondaryButton,
                               out bool secondaryPressed) &&
                           secondaryPressed;
            bool pressedThisFrame = pressed && !vrStopButtonWasPressed;
            vrStopButtonWasPressed = pressed;
            return pressedThisFrame;
        }

        private static Transform FindNamedTransform(Transform root, params string[] names)
        {
            if (root == null)
                return null;

            foreach (string targetName in names)
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == targetName)
                        return child;
                }
            }
            return null;
        }

        private static float ResolveGroundHeight(Vector3 point, float fallback)
        {
            Vector3 origin = new(point.x, Mathf.Max(point.y, fallback) + 4f, point.z);
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                10f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float highestWalkable = fallback;
            float maximumFloorHeight = Mathf.Max(fallback + 0.35f, point.y - 0.25f);
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                // A ray started above the cabin also sees the roof first. Only
                // upward-facing surfaces below seated hand/head height can be
                // the floor or rug on which the stand and returned ball sit.
                if (hit.normal.y < 0.55f || hit.point.y > maximumFloorHeight)
                    continue;
                if (found && hit.point.y <= highestWalkable)
                    continue;
                highestWalkable = hit.point.y;
                found = true;
            }
            return found ? highestWalkable : fallback;
        }

        private static Material CreateMaterial(Shader shader, string materialName, Color color, float smoothness)
        {
            if (shader == null)
                return null;
            Material material = new(shader) { name = materialName, color = color };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static GameObject CreatePrimitive(
            string objectName,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool keepCollider)
        {
            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = objectName;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            if (!keepCollider && primitive.TryGetComponent(out Collider collider))
                Destroy(collider);
            return primitive;
        }
    }
}
