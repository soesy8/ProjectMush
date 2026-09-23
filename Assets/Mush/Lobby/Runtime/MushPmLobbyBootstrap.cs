using System;
using Mush.Customization;
using Mush.Quest;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class MushPmLobbyBootstrap : MonoBehaviour
    {
        private const string SceneName = "PM_Lobby";
        private Camera lobbyCamera;
        private Font font;
        private TextMesh dogStatus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (scene.name != SceneName || FindInScene<MushLobbyController>(scene) != null ||
                FindInScene<MushPmLobbyBootstrap>(scene) != null)
                return;
            GameObject bootstrap = new("Mush PM Lobby Runtime");
            SceneManager.MoveGameObjectToScene(bootstrap, scene);
            bootstrap.AddComponent<MushPmLobbyBootstrap>();
        }

        private void Awake()
        {
            lobbyCamera = FindLobbyCamera(gameObject.scene);
            if (lobbyCamera == null)
            {
                Debug.LogError("[Mush] PM_Lobby에 사용할 카메라가 없습니다.");
                enabled = false;
                return;
            }

            lobbyCamera.gameObject.tag = "MainCamera";
            lobbyCamera.nearClipPlane = 0.04f;
            lobbyCamera.farClipPlane = 80f;
            font = Mush.UI.MushUiPanelSkin.ThemeFont;

            MushQuestTrackedInputRig questRig = FindInScene<MushQuestTrackedInputRig>(gameObject.scene);
            if (questRig == null && XRSettings.isDeviceActive)
                questRig = MushQuestTrackedInputRig.InstallForCamera(lobbyCamera);
            questRig?.SetRayEnabled(true);

            Transform leftHand = FindByName(gameObject.scene, "Left Controller") ?? questRig?.LeftController;
            Transform rightHand = FindByName(gameObject.scene, "Right Controller") ?? questRig?.RightController;
            EnsureDesktopLook(lobbyCamera);

            GameObject mapPanel = CreateMapPanel(lobbyCamera.transform, out TextMesh mapStatus);
            GameObject controllerObject = new("Lobby Game State");
            Transform artLobbyRoot = FindByName(gameObject.scene, "Lobby") ?? transform;
            controllerObject.transform.SetParent(artLobbyRoot, false); // 기존 로비 기능이 새 아트 벽난로·밥그릇·침대를 같은 루트에서 찾게 한다.
            controllerObject.SetActive(false);
            MushLobbyController controller = controllerObject.AddComponent<MushLobbyController>();
            controller.SetKoreanFont(font);
            foreach (MushLobbyInteractable interactable in mapPanel.GetComponentsInChildren<MushLobbyInteractable>(true))
                interactable.SetController(controller);

            CreateArtHotspot("PictureFrame_Unity_MaterialSeparated", "지도", MushLobbyAction.OpenMapBoard, controller);
            CreateArtHotspot("DisplayCabinet_Unity_MaterialSeparated", "상점", MushLobbyAction.OpenShop, controller);
            CreateArtHotspot("Chest", "하우징", MushLobbyAction.OpenHousing, controller);
            CreateArtHotspot("Mush_Sledge", "커스터마이징", MushLobbyAction.OpenCustomization, controller);

            MushLobbyDogRoamer[] dogs = CreateDogs(leftHand, rightHand);
            GameObject[] furniture = CreateHousingSlots();
            dogStatus = CreateDogStatus(lobbyCamera.transform);

            controller.Configure(
                lobbyCamera, null,
                mapPanel, null, null,
                mapStatus, null, null,
                Array.Empty<GameObject>(), furniture, dogs);
            controllerObject.SetActive(true);

            MushLobbyNavMeshRuntime navigation = gameObject.AddComponent<MushLobbyNavMeshRuntime>();
            navigation.Configure(new Vector3(0f, -0.06f, -1.25f), new Vector3(8.7f, 0.12f, 10.2f));

            Transform bed = FindByName(gameObject.scene, "DogBed_Unity_MaterialSeparated");
            if (bed != null && bed.GetComponent<MushLobbyDogBedSpot>() == null)
                bed.gameObject.AddComponent<MushLobbyDogBedSpot>();

            MushLobbyPauseMenu pause = gameObject.AddComponent<MushLobbyPauseMenu>();
            pause.Configure(lobbyCamera, font);
            RefreshDogStatus();
        }

        private void LateUpdate() => RefreshDogStatus();

        private void RefreshDogStatus()
        {
            if (dogStatus == null)
                return;
            dogStatus.text = $"첫째 체력 {Mathf.RoundToInt(MushGameSave.GetDogStamina(0))}   " +
                             $"둘째 체력 {Mathf.RoundToInt(MushGameSave.GetDogStamina(1))}\n" +
                             $"첫째 기분 {Condition(MushGameSave.GetDogCondition(0))}   " +
                             $"둘째 기분 {Condition(MushGameSave.GetDogCondition(1))}";
        }

        private static string Condition(MushDogCondition condition) => condition switch
        {
            MushDogCondition.Good => "좋음",
            MushDogCondition.Bad => "나쁨",
            _ => "보통",
        };

        private void CreateArtHotspot(
            string objectName,
            string label,
            MushLobbyAction action,
            MushLobbyController controller)
        {
            Transform art = FindByName(gameObject.scene, objectName);
            if (art == null)
            {
                Debug.LogWarning("[Mush] PM_Lobby 오브젝트를 찾지 못했습니다: " + objectName);
                return;
            }

            Bounds bounds = CalculateBounds(art);
            GameObject hotspot = new(label + " Interaction");
            hotspot.transform.SetParent(transform, true);
            hotspot.transform.position = bounds.center;
            BoxCollider collider = hotspot.AddComponent<BoxCollider>();
            collider.size = new Vector3(
                Mathf.Max(0.45f, bounds.size.x),
                Mathf.Max(0.45f, bounds.size.y),
                Mathf.Max(0.35f, bounds.size.z));
            collider.isTrigger = true;
            XRSimpleInteractable xr = hotspot.AddComponent<XRSimpleInteractable>();
            xr.selectMode = InteractableSelectMode.Single;
            MushLobbyInteractable interactable = hotspot.AddComponent<MushLobbyInteractable>();
            interactable.Configure(controller, action);

            TextMesh text = CreateText(label, hotspot.transform, font, 0.08f, Color.white);
            text.transform.position = bounds.center + Vector3.up * (bounds.extents.y + 0.22f);
            text.transform.rotation = Quaternion.LookRotation(text.transform.position - lobbyCamera.transform.position, Vector3.up);
        }

        private GameObject CreateMapPanel(Transform cameraTransform, out TextMesh status)
        {
            GameObject panel = new("지도 Panel");
            panel.transform.SetParent(cameraTransform, false);
            panel.transform.localPosition = new Vector3(0f, 0f, 2.25f);
            Material background = CreateMaterial("Map Panel", new Color(0.055f, 0.075f, 0.10f));
            Material button = CreateMaterial("Map Button", new Color(0.23f, 0.34f, 0.45f));
            CreateCube("Panel Back", panel.transform, Vector3.zero, new Vector3(2.9f, 1.65f, 0.06f), background, false);
            CreateText("지도", panel.transform, font, 0.065f, Color.white).transform.localPosition = new Vector3(0f, 0.60f, -0.045f);
            status = CreateText("맵을 선택해 출발하세요", panel.transform, font, 0.031f, new Color(1f, 0.82f, 0.45f));
            status.transform.localPosition = new Vector3(0f, 0.30f, -0.045f);
            CreatePanelButton(panel.transform, "기본 설원", new Vector3(-0.88f, -0.10f, -0.06f), MushLobbyAction.SelectSnowfield, button);
            CreatePanelButton(panel.transform, "나무 숲", new Vector3(0f, -0.10f, -0.06f), MushLobbyAction.SelectForest, button);
            CreatePanelButton(panel.transform, "급커브맵", new Vector3(0.88f, -0.10f, -0.06f), MushLobbyAction.SelectSharpCurve, button);
            CreatePanelButton(panel.transform, "닫기", new Vector3(0f, -0.60f, -0.06f), MushLobbyAction.ClosePanel, button);
            panel.SetActive(false);
            return panel;
        }

        private void CreatePanelButton(
            Transform parent,
            string label,
            Vector3 position,
            MushLobbyAction action,
            Material material)
        {
            GameObject button = CreateCube(label + " Button", parent, position,
                label == "닫기" ? new Vector3(0.75f, 0.25f, 0.10f) : new Vector3(0.76f, 0.36f, 0.10f), material, true);
            XRSimpleInteractable xr = button.AddComponent<XRSimpleInteractable>();
            xr.selectMode = InteractableSelectMode.Single;
            MushLobbyInteractable interactable = button.AddComponent<MushLobbyInteractable>();
            interactable.Configure(null, action, button.GetComponent<Renderer>());
            CreateText(label, button.transform, font, 0.035f, Color.white).transform.localPosition = new Vector3(0f, 0f, -0.56f);
        }

        private MushLobbyDogRoamer[] CreateDogs(Transform leftHand, Transform rightHand)
        {
            MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
            if (catalog == null || catalog.husky == null || catalog.malamute == null)
                return Array.Empty<MushLobbyDogRoamer>();
            return new[]
            {
                CreateDog("Mochi - Husky", catalog.husky, false, new Vector3(-0.75f, 0f, 0.45f), leftHand, rightHand, -0.42f),
                CreateDog("Bori - Malamute", catalog.malamute, true, new Vector3(0.75f, 0f, -0.25f), leftHand, rightHand, 0.42f),
            };
        }

        private MushLobbyDogRoamer CreateDog(
            string name,
            GameObject prefab,
            bool malamute,
            Vector3 position,
            Transform leftHand,
            Transform rightHand,
            float callOffset)
        {
            GameObject root = new(name);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = position;
            GameObject visualObject = Instantiate(prefab, root.transform);
            visualObject.name = malamute ? "Dog Visual - LowPoly Malamute" : "Dog Visual - LowPoly Husky";
            visualObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visualObject.transform.localScale = Vector3.one * (malamute ? 0.40f : 0.39f);

            string prefix = malamute ? "Malamute_" : "Husky_";
            Transform head = FindContaining(visualObject.transform, prefix + "Head");
            Transform leftEye = FindContaining(visualObject.transform, prefix + "Eye_L");
            Transform rightEye = FindContaining(visualObject.transform, prefix + "Eye_R");
            Transform mouth = FindContaining(visualObject.transform, prefix + "Mouth");
            Transform tail = FindContaining(visualObject.transform, prefix + "Tail");

            if (head != null)
            {
                SphereCollider collider = head.GetComponent<SphereCollider>() ?? head.gameObject.AddComponent<SphereCollider>();
                collider.radius = malamute ? 0.53f : 0.49f;
            }

            MushLobbyDogRoamer roamer = root.AddComponent<MushLobbyDogRoamer>();
            roamer.Configure(visualObject.transform, tail, new Vector2(-4.0f, -4.8f), new Vector2(4.0f, 2.0f));
            roamer.ConfigureCharacter(null, lobbyCamera.transform, callOffset);
            MushLobbyDogExpression expression = root.AddComponent<MushLobbyDogExpression>();
            expression.Configure(roamer, head, leftEye, rightEye, mouth, lobbyCamera);
            if (root.GetComponent<XRSimpleInteractable>() == null)
                root.AddComponent<XRSimpleInteractable>();
            MushLobbyDogInteraction interaction = root.AddComponent<MushLobbyDogInteraction>();
            interaction.Configure(roamer, head, leftHand, rightHand);
            return roamer;
        }

        private GameObject[] CreateHousingSlots()
        {
            GameObject[] slots = new GameObject[3];
            for (int index = 0; index < slots.Length; index++)
            {
                slots[index] = new GameObject("PM Lobby Housing Slot " + (index + 1));
                slots[index].transform.SetParent(transform, false);
                slots[index].transform.SetLocalPositionAndRotation(
                    MushHousingLayout.Position(index), MushHousingLayout.Rotation(index));
                slots[index].SetActive(false);
            }
            return slots;
        }

        private TextMesh CreateDogStatus(Transform cameraTransform)
        {
            TextMesh text = CreateText("Dog Status", cameraTransform, font, 0.030f, Color.white);
            text.anchor = TextAnchor.UpperLeft;
            text.alignment = TextAlignment.Left;
            text.transform.localPosition = new Vector3(-0.92f, 0.48f, 1.65f);
            return text;
        }

        private static Camera FindLobbyCamera(Scene scene)
        {
            Camera preview = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera.name == "Main Camera" && camera.gameObject.activeInHierarchy)
                    return camera;
                if (camera.name == "PreviewCamera")
                    preview = camera;
            }
            return preview;
        }

        private static void EnsureDesktopLook(Camera camera)
        {
            MushDesktopSeatedLook look = camera.GetComponentInParent<MushDesktopSeatedLook>();
            if (look == null)
                look = camera.gameObject.AddComponent<MushDesktopSeatedLook>();
            look.Configure(camera.transform);
        }

        private static Bounds CalculateBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new(root.position, Vector3.one * 0.5f);
            bool initialized = false;
            foreach (Renderer renderer in renderers)
            {
                if (!initialized) { bounds = renderer.bounds; initialized = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static GameObject CreateCube(
            string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool keepCollider)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
                Destroy(cube.GetComponent<Collider>());
            return cube;
        }

        private static TextMesh CreateText(
            string content, Transform parent, Font font, float characterSize, Color color)
        {
            GameObject textObject = new(content + " Text");
            textObject.transform.SetParent(parent, false);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = content;
            text.font = font;
            text.fontSize = 64;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            if (font != null)
                text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return text;
        }

        private static Material CreateMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Material material = new(shader) { name = name, color = color };
            return material;
        }

        private static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name)
                    return child;
            return null;
        }

        private static Transform FindContaining(Transform root, string fragment)
        {
            if (root == null)
                return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return child;
            return null;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }
    }
}
