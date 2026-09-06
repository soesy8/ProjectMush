using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Keyboard = UnityEngine.InputSystem.Keyboard;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyStationNavigator : MonoBehaviour
    {
        private const float LocomotionDeadzone = 0.20f;
        private const float LobbyMoveSpeed = 1.20f;
        private const float SnapTurnAngle = 30f;
        private const float ChairUseDistance = 2.20f;
        private static readonly Vector3 FireplaceSeatPosition = new(-2.80f, 0f, -4.45f);
        private const float FireplaceYaw = 180f;

        private readonly struct Station
        {
            public readonly string label;
            public readonly Vector3 position;
            public readonly float yaw;

            public Station(string newLabel, Vector3 newPosition, float newYaw)
            {
                label = newLabel;
                position = newPosition;
                yaw = newYaw;
            }
        }

        private static readonly Station[] Stations =
        {
            new("중앙 좌석", new Vector3(0f, 0f, 2.00f), 180f),
            new("개 놀이", new Vector3(2.55f, 0f, 1.45f), 225f),
            new("개 먹이주기", new Vector3(2.65f, 0f, 0.95f), 180f),
            new("지도", new Vector3(0f, 0f, -4.35f), 180f),
            new("상점", new Vector3(2.20f, 0f, -2.45f), 90f),
            new("집 꾸미기", new Vector3(-2.20f, 0f, -2.45f), 270f),
            new("벽난로", new Vector3(-2.80f, 0f, -3.25f), FireplaceYaw), // 먼저 의자 뒤쪽에서 벽난로와 의자를 보고, 의자를 선택해야 실제 좌석으로 들어간다.
        };

        private readonly List<Material> ownedMaterials = new();
        private readonly List<MushLobbyStationButton> buttons = new();
        private Camera lobbyCamera;
        private MushLobbyController controller;
        private MushSeatedRigLock seatedRig;
        private MushDesktopSeatedLook desktopLook;
        private Transform menuRoot;
        private Material buttonMaterial;
        private Material selectedMaterial;
        private int selectedIndex;
        private int currentStationIndex;
        private bool stickClickWasPressed;
        private bool triggerWasPressed;
        private bool seatedAtFireplace;
        private bool rightStickSnapReady = true;
        private bool locomotionWasActive;

        public bool IsMenuOpen => menuRoot != null && menuRoot.gameObject.activeSelf;
        public bool IsSeatedAtFireplace => seatedAtFireplace;

        public static MushLobbyStationNavigator Install(Camera camera, MushLobbyController owner, Transform lobbyRoot)
        {
            if (camera == null || owner == null)
                return null;

            MushLobbyStationNavigator existing = owner.GetComponent<MushLobbyStationNavigator>();
            if (existing == null)
                existing = owner.gameObject.AddComponent<MushLobbyStationNavigator>();
            existing.Configure(camera, owner, lobbyRoot);
            return existing;
        }

        private void Configure(Camera camera, MushLobbyController owner, Transform lobbyRoot)
        {
            lobbyCamera = camera;
            controller = owner;
            seatedRig = camera.GetComponentInParent<MushSeatedRigLock>();
            desktopLook = camera.GetComponentInParent<MushDesktopSeatedLook>();
            if (menuRoot == null)
                menuRoot = FindDescendant(lobbyRoot != null ? lobbyRoot : owner.transform.root, "Lobby Station Travel Menu");
            if (menuRoot == null)
                BuildMenu(lobbyRoot != null ? lobbyRoot : owner.transform.root);
            else
                CacheMenuReferences();
            EnsureChairSeatInteraction(lobbyRoot != null ? lobbyRoot : owner.transform.root);
        }

        private void CacheMenuReferences()
        {
            MushLobbyStationButton[] sceneButtons = menuRoot.GetComponentsInChildren<MushLobbyStationButton>(true);
            buttons.Clear();
            for (int index = 0; index < sceneButtons.Length; index++)
            {
                MushLobbyStationButton button = sceneButtons[index];
                button.Configure(this, index, button.GetComponent<Renderer>());
                buttons.Add(button);
            }
            if (sceneButtons.Length > 0)
                buttonMaterial = sceneButtons[0].GetComponent<Renderer>()?.sharedMaterial;
            selectedMaterial = buttonMaterial;
        }

        private void Update()
        {
            if (!XRSettings.isDeviceActive)
            {
                stickClickWasPressed = false;
                triggerWasPressed = false;
                rightStickSnapReady = true;
                HandleDesktopLocomotion();
                return;
            }

            InputDevice leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool stickPressed = leftController.isValid &&
                                leftController.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stickValue) &&
                                stickValue;
            bool menuToggledThisFrame = stickPressed && !stickClickWasPressed;
            if (menuToggledThisFrame)
                ToggleMenu();
            stickClickWasPressed = stickPressed;

            bool triggerPressed = leftController.isValid &&
                                  leftController.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerValue) &&
                                  triggerValue;
            if (!IsMenuOpen)
            {
                triggerWasPressed = triggerPressed;
                if (!menuToggledThisFrame)
                    HandleVrLocomotion(leftController);
                else
                    locomotionWasActive = false;
                return;
            }

            locomotionWasActive = false;
            rightStickSnapReady = true;
            if (leftController.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis) && axis.sqrMagnitude >= 0.20f)
            {
                float angle = Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg;
                if (angle < 0f)
                    angle += 360f;
                float sectorAngle = 360f / Stations.Length;
                selectedIndex = Mathf.RoundToInt(Mathf.Repeat(90f - angle, 360f) / sectorAngle) % Stations.Length;
                RefreshSelectionVisuals();
            }

            if (triggerPressed && !triggerWasPressed)
                TravelTo(selectedIndex);
            triggerWasPressed = triggerPressed;
        }

        private void HandleDesktopLocomotion()
        {
            if (IsMenuOpen || MushLobbyFeedDispenser.IsDesktopCanisterHeld)
            {
                locomotionWasActive = false;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            Vector2 moveInput = Vector2.zero;
            if (keyboard.aKey.isPressed) moveInput.x -= 1f;
            if (keyboard.dKey.isPressed) moveInput.x += 1f;
            if (keyboard.wKey.isPressed) moveInput.y += 1f;
            if (keyboard.sKey.isPressed) moveInput.y -= 1f;
            ApplyLocomotion(moveInput);
        }

        private void HandleVrLocomotion(InputDevice leftController)
        {
            Vector2 moveInput = Vector2.zero;
            if (leftController.isValid)
                leftController.TryGetFeatureValue(CommonUsages.primary2DAxis, out moveInput);
            ApplyLocomotion(moveInput);

            InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            Vector2 turnInput = Vector2.zero;
            if (rightController.isValid)
                rightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out turnInput);
            if (Mathf.Abs(turnInput.x) <= 0.35f)
                rightStickSnapReady = true;
            if (!rightStickSnapReady || Mathf.Abs(turnInput.x) < 0.75f || seatedRig == null)
                return;

            seatedRig.SnapTurnAroundCamera(lobbyCamera != null ? lobbyCamera.transform : null, Mathf.Sign(turnInput.x) * SnapTurnAngle);
            rightStickSnapReady = false;
            LeaveFixedStationState();
        }

        private void ApplyLocomotion(Vector2 input)
        {
            if (seatedRig == null || lobbyCamera == null || input.sqrMagnitude < LocomotionDeadzone * LocomotionDeadzone)
            {
                locomotionWasActive = false;
                return;
            }

            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 forward = desktopLook != null && !XRSettings.isDeviceActive
                ? desktopLook.CurrentWorldViewDirection
                : lobbyCamera.transform.forward;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(seatedRig.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 displacement = (forward * input.y + right * input.x) * (LobbyMoveSpeed * Time.deltaTime);
            if (!seatedRig.TryMoveSeat(displacement, lobbyCamera.transform))
            {
                locomotionWasActive = false;
                return;
            }

            if (!locomotionWasActive)
                controller?.ClosePanelsForTravel();
            locomotionWasActive = true;
            LeaveFixedStationState();
        }

        private void LeaveFixedStationState()
        {
            currentStationIndex = -1;
            seatedAtFireplace = false;
        }

        public void ToggleMenu()
        {
            if (menuRoot == null)
                return;
            SetMenuOpen(!IsMenuOpen);
        }

        public void CloseMenu()
        {
            SetMenuOpen(false);
        }

        private void SetMenuOpen(bool open)
        {
            if (menuRoot == null)
                return;

            if (open)
            {
                controller?.ClosePanelsForTravel();
                controller?.SetStationMenuVisible(true);
                selectedIndex = currentStationIndex >= 0
                    ? currentStationIndex
                    : FindNearestStationIndex(); // 직접 걸어온 뒤 메뉴를 열어도 유효한 가까운 장소가 선택된 상태로 시작한다.
                PlaceMenuInFrontOfPlayer();
                menuRoot.gameObject.SetActive(true);
                RefreshSelectionVisuals();
            }
            else
            {
                menuRoot.gameObject.SetActive(false);
                controller?.SetStationMenuVisible(false);
            }
        }

        private int FindNearestStationIndex()
        {
            if (lobbyCamera == null)
                return 0;

            Vector3 cameraPosition = lobbyCamera.transform.position;
            int nearestIndex = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < Stations.Length; index++)
            {
                Vector3 difference = Stations[index].position - cameraPosition;
                difference.y = 0f;
                if (difference.sqrMagnitude >= nearestDistance)
                    continue;
                nearestDistance = difference.sqrMagnitude;
                nearestIndex = index;
            }
            return nearestIndex;
        }

        public bool TryHandleDesktopClick(Ray pointerRay)
        {
            if (!IsMenuOpen)
                return false;

            RaycastHit[] hits = Physics.RaycastAll(
                pointerRay,
                8f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            float nearestDistance = float.PositiveInfinity;
            MushLobbyStationButton nearestButton = null;
            foreach (RaycastHit hit in hits)
            {
                MushLobbyStationButton candidate = hit.collider.GetComponentInParent<MushLobbyStationButton>();
                if (candidate == null || hit.distance >= nearestDistance)
                    continue;
                nearestDistance = hit.distance;
                nearestButton = candidate;
            }

            if (nearestButton != null)
                nearestButton.Trigger();
            return true; // 메뉴가 열려 있으면 빈 공간 클릭도 뒤쪽 로비 물체로 통과시키지 않는다.
        }

        public void TravelTo(int index)
        {
            if (index < 0 || index >= Stations.Length || seatedRig == null || lobbyCamera == null)
                return;

            Station station = Stations[index];
            MoveToStationPose(station.position, station.yaw);
            seatedAtFireplace = false; // 벽난로 항목을 다시 골라도 우선 의자 뒤 관람 위치로 돌아온다.
            currentStationIndex = index;
            selectedIndex = index;
            SetMenuOpen(false);
            controller?.ClosePanelsForTravel();
            Physics.SyncTransforms();
        }

        public void TrySitAtFireplace()
        {
            if (seatedAtFireplace || lobbyCamera == null)
                return;

            Vector3 cameraToSeat = FireplaceSeatPosition - lobbyCamera.transform.position;
            cameraToSeat.y = 0f;
            if (cameraToSeat.sqrMagnitude > ChairUseDistance * ChairUseDistance)
                return; // 방 반대편에서 의자를 눌러 순간이동하지 않고, 직접 걸어 의자 근처에 온 경우에만 앉는다.

            MoveToStationPose(FireplaceSeatPosition, FireplaceYaw);
            seatedAtFireplace = true;
            controller?.ClosePanelsForTravel();
            Physics.SyncTransforms();
        }

        private void MoveToStationPose(Vector3 cameraWorldPosition, float yaw)
        {
            if (seatedRig == null || lobbyCamera == null)
                return;

            Quaternion targetRotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 cameraLocal = seatedRig.transform.InverseTransformPoint(lobbyCamera.transform.position);
            Vector3 rotatedCameraOffset = targetRotation * new Vector3(cameraLocal.x, 0f, cameraLocal.z);
            Vector3 rigPosition = cameraWorldPosition - rotatedCameraOffset;
            rigPosition.y = cameraWorldPosition.y;

            seatedRig.MoveSeat(rigPosition, targetRotation);
            desktopLook?.RecenterView();
        }

        private void EnsureChairSeatInteraction(Transform lobbyRoot)
        {
            Transform chair = FindDescendant(lobbyRoot, "Placed Housing Chair");
            if (chair == null || !chair.gameObject.activeInHierarchy)
                return; // 의자를 장착하지 않은 상태에서는 앉기 대상도 만들지 않는다.

            BoxCollider selectionCollider = chair.GetComponent<BoxCollider>();
            if (selectionCollider == null)
                selectionCollider = chair.gameObject.AddComponent<BoxCollider>();
            if (TryCalculateLocalRendererBounds(chair, out Bounds localBounds))
            {
                selectionCollider.center = localBounds.center;
                selectionCollider.size = localBounds.size + new Vector3(0.06f, 0.06f, 0.06f);
            }
            selectionCollider.isTrigger = true; // 공이나 개를 밀지 않는 마우스/VR 선택 범위로만 사용한다.

            if (chair.GetComponent<XRSimpleInteractable>() == null)
                chair.gameObject.AddComponent<XRSimpleInteractable>();
            MushLobbyChairSeatInteractable chairSeat = chair.GetComponent<MushLobbyChairSeatInteractable>();
            if (chairSeat == null)
                chairSeat = chair.gameObject.AddComponent<MushLobbyChairSeatInteractable>();
            chairSeat.Configure(this);
        }

        private static bool TryCalculateLocalRendererBounds(Transform root, out Bounds result)
        {
            result = new Bounds(Vector3.zero, Vector3.zero);
            bool initialized = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                Bounds world = renderer.bounds;
                for (int x = 0; x <= 1; x++)
                for (int y = 0; y <= 1; y++)
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 corner = new(
                        x == 0 ? world.min.x : world.max.x,
                        y == 0 ? world.min.y : world.max.y,
                        z == 0 ? world.min.z : world.max.z);
                    Vector3 localCorner = root.InverseTransformPoint(corner);
                    if (!initialized)
                    {
                        result = new Bounds(localCorner, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(localCorner);
                    }
                }
            }
            return initialized;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null)
                return null;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == objectName)
                    return candidate;
            }
            return null;
        }

        private void PlaceMenuInFrontOfPlayer()
        {
            Vector3 forward = Vector3.ProjectOnPlane(lobbyCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(seatedRig.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            menuRoot.position = lobbyCamera.transform.position + forward * 1.25f - Vector3.up * 0.06f;
            menuRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void BuildMenu(Transform parent)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material background = CreateMaterial(shader, "Mush Station Menu Background", new Color(0.035f, 0.045f, 0.06f), 0.14f);
            buttonMaterial = CreateMaterial(shader, "Mush Station Menu Button", new Color(0.28f, 0.13f, 0.065f), 0.20f);
            selectedMaterial = CreateMaterial(shader, "Mush Station Menu Selected", new Color(0.92f, 0.48f, 0.10f), 0.30f);

            GameObject rootObject = new("Lobby Station Travel Menu");
            menuRoot = rootObject.transform;
            menuRoot.SetParent(parent, true);

            CreateCube("Menu Background", menuRoot, Vector3.zero, new Vector3(1.56f, 1.18f, 0.045f), background, true);
            CreateText("Title", menuRoot, new Vector3(0f, 0.49f, -0.041f), 0.014f, "이동할 장소");
            CreateText("Guide", menuRoot, new Vector3(0f, -0.50f, -0.041f), 0.0065f, "왼쪽 스틱 방향 + 트리거   ·   PC: 마우스 클릭");

            const float radiusX = 0.54f;
            const float radiusY = 0.32f;
            float stationAngleStep = 360f / Stations.Length;
            for (int index = 0; index < Stations.Length; index++)
            {
                float angle = (90f - index * stationAngleStep) * Mathf.Deg2Rad;
                Vector3 localPosition = new(
                    Mathf.Cos(angle) * radiusX,
                    Mathf.Sin(angle) * radiusY - 0.01f,
                    -0.038f);
                GameObject buttonObject = CreateCube(
                    "Station Button - " + Stations[index].label,
                    menuRoot,
                    localPosition,
                    new Vector3(0.36f, 0.13f, 0.055f),
                    buttonMaterial,
                    true);
                buttonObject.AddComponent<XRSimpleInteractable>();
                MushLobbyStationButton button = buttonObject.AddComponent<MushLobbyStationButton>();
                button.Configure(this, index, buttonObject.GetComponent<Renderer>());
                buttons.Add(button);
                CreateText(
                    "Station Label - " + Stations[index].label,
                    menuRoot,
                    localPosition + new Vector3(0f, 0f, -0.031f),
                    0.0078f,
                    Stations[index].label);
            }

            menuRoot.gameObject.SetActive(false);
        }

        private void RefreshSelectionVisuals()
        {
            for (int index = 0; index < buttons.Count; index++)
                buttons[index]?.SetSelected(index == selectedIndex, buttonMaterial, selectedMaterial);
        }

        private Material CreateMaterial(Shader shader, string materialName, Color color, float smoothness)
        {
            Material material = new(shader) { name = materialName };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            ownedMaterials.Add(material);
            return material;
        }

        private static GameObject CreateCube(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool trigger)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = localScale;
            if (cube.TryGetComponent(out Renderer renderer))
                renderer.sharedMaterial = material;
            if (cube.TryGetComponent(out Collider collider))
                collider.isTrigger = trigger;
            return cube;
        }

        private static void CreateText(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            float characterSize,
            string value)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = localPosition;
            textObject.transform.localRotation = Quaternion.identity;
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = value;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = characterSize;
            text.color = Color.white;
            Font font = MushLobbyController.ActiveKoreanFont;
            if (font == null)
                return;
            text.font = font;
            if (textObject.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = font.material;
        }

        private void OnDestroy()
        {
            foreach (Material material in ownedMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
            ownedMaterials.Clear();
        }
    }

}
