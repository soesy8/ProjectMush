using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace Mush.Lobby.Editor
{
    [InitializeOnLoad]
    public static class MushPmLobbySceneInstaller
    {
        private const string ScenePath = "Assets/Art/Scenes/PM_Lobby.unity";
        private const string RigPath = "Assets/VR/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Variant.prefab";
        private const string MarkerName = "Mush PM Lobby Direct Setup Revision 3";
        private const string OldLobbyPath = "Assets/Mush/Scenes/MushLobby.unity";

        static MushPmLobbySceneInstaller()
        {
            EditorApplication.delayCall += Install;
        }

        private static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying || !File.Exists(ScenePath))
                return;

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            if (FindTransform(scene, MarkerName) != null)
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
                return;
            }

            SceneManager.SetActiveScene(scene);
            try
            {
                GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
                if (rigPrefab == null)
                    throw new InvalidOperationException("PM_Lobby XR rig prefab was not found.");

                Transform existingRig = FindTransform(scene, "Seated XR Player - PM Lobby");
                GameObject rig = existingRig != null
                    ? existingRig.gameObject
                    : (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
                rig.name = "Seated XR Player - PM Lobby";
                rig.transform.SetPositionAndRotation(new Vector3(0f, 0f, 4.25f), Quaternion.Euler(0f, 180f, 0f));
                if (rig.GetComponent<MushSeatedRigLock>() == null)
                    rig.AddComponent<MushSeatedRigLock>();

                Transform locomotion = Find(rig.transform, "Locomotion");
                if (locomotion != null)
                    locomotion.gameObject.SetActive(false);
                Transform gaze = Find(rig.transform, "Gaze Interactor");
                if (gaze != null)
                    gaze.gameObject.SetActive(false);

                Camera camera = rig.GetComponentInChildren<Camera>(true);
                if (camera == null)
                    throw new InvalidOperationException("PM_Lobby XR rig camera was not found.");
                camera.gameObject.tag = "MainCamera";
                camera.nearClipPlane = 0.04f;
                camera.farClipPlane = 80f;
                camera.fieldOfView = 84f;
                MushDesktopSeatedLook look = rig.GetComponent<MushDesktopSeatedLook>() ?? rig.AddComponent<MushDesktopSeatedLook>();
                look.Configure(camera.transform);
                MushLobbyFixedRayVisuals fixedRays = rig.GetComponent<MushLobbyFixedRayVisuals>() ?? rig.AddComponent<MushLobbyFixedRayVisuals>();
                fixedRays.Configure(4.5f); // 텔레포트 포물선을 끄고 로비 선택용 직선 레이를 양손에 고정한다.

                BakeLobbyContent(scene, camera, rig.transform);

                Transform preview = FindTransform(scene, "PreviewCamera");
                if (preview != null)
                    preview.gameObject.SetActive(false);

                GameObject marker = new(MarkerName) { hideFlags = HideFlags.HideInHierarchy };
                SceneManager.MoveGameObjectToScene(marker, scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[Mush] PM_Lobby에 좌식 XR 리그와 런타임 로비 연결을 설치했습니다.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                if (opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void BakeLobbyContent(Scene targetScene, Camera camera, Transform rig)
        {
            Scene oldScene = EditorSceneManager.OpenScene(OldLobbyPath, OpenSceneMode.Additive);
            try
            {
                MushLobbyController source = FindComponent<MushLobbyController>(oldScene);
                if (source == null)
                    throw new InvalidOperationException("기존 로비 컨트롤러를 찾지 못했습니다.");

                Transform lobby = FindTransform(targetScene, "Lobby");
                if (lobby == null)
                    throw new InvalidOperationException("PM_Lobby의 Lobby 루트를 찾지 못했습니다.");

                Transform oldSystems = Find(lobby, "PM Lobby Systems");
                if (oldSystems != null)
                    UnityEngine.Object.DestroyImmediate(oldSystems.gameObject);
                GameObject systemsObject = new("PM Lobby Systems");
                systemsObject.transform.SetParent(lobby, false);
                Transform systems = systemsObject.transform;

                SerializedObject serialized = new(source);
                GameObject mapPanel = CloneReference(serialized, "mapPanel", systems, targetScene);
                GameObject shopPanel = CloneReference(serialized, "shopPanel", systems, targetScene);
                GameObject housingPanel = CloneReference(serialized, "housingPanel", systems, targetScene);
                TextMesh mapStatus = CloneTextReference(serialized, "mapStatusText", mapPanel);
                TextMesh shopStatus = CloneTextReference(serialized, "shopStatusText", shopPanel);
                TextMesh housingStatus = CloneTextReference(serialized, "housingStatusText", housingPanel);

                MushLobbyDogRoamer[] sourceDogs = source.GetComponentsInParent<Transform>(true)[0]
                    .root.GetComponentsInChildren<MushLobbyDogRoamer>(true);
                MushLobbyDogRoamer[] dogs = new MushLobbyDogRoamer[Mathf.Min(2, sourceDogs.Length)];
                for (int index = 0; index < dogs.Length; index++)
                {
                    GameObject clone = UnityEngine.Object.Instantiate(sourceDogs[index].gameObject, systems);
                    clone.name = index == 0 ? "Mochi - Husky" : "Bori - Malamute";
                    dogs[index] = clone.GetComponent<MushLobbyDogRoamer>();
                    clone.transform.localPosition = index == 0 ? new Vector3(-0.8f, 0f, 0.3f) : new Vector3(0.8f, 0f, -0.3f);
                    ConfigureDog(dogs[index], camera, rig, index == 0 ? -0.42f : 0.42f);
                }

                GameObject[] furniture = CloneArray(serialized, "placedFurniture", systems, targetScene);
                GameObject stateObject = new("Lobby Game State");
                stateObject.transform.SetParent(lobby, false);
                MushLobbyController controller = stateObject.AddComponent<MushLobbyController>();
                controller.SetKoreanFont(AssetDatabase.LoadAssetAtPath<Font>("Assets/UI_Panel_Sample/Font/HS두꺼비체.ttf"));
                controller.Configure(camera, null, mapPanel, shopPanel, housingPanel,
                    mapStatus, shopStatus, housingStatus, Array.Empty<GameObject>(), furniture, dogs);

                CreateHotspot(targetScene, systems, "PictureFrame_Unity_MaterialSeparated", "지도", controller, MushLobbyAction.OpenMapBoard);
                CreateHotspot(targetScene, systems, "DisplayCabinet_Unity_MaterialSeparated", "상점", controller, MushLobbyAction.OpenShop);
                CreateHotspot(targetScene, systems, "Chest", "하우징", controller, MushLobbyAction.OpenHousing);
                CreateHotspot(targetScene, systems, "Mush_Sledge", "커스터마이징", controller, MushLobbyAction.OpenCustomization);
                PlacePanel(mapPanel, camera.transform, new Vector3(0f, 0f, 2.2f));
                PlacePanel(shopPanel, FindTransform(targetScene, "DisplayCabinet_Unity_MaterialSeparated"), new Vector3(0f, 1.6f, 0f));
                PlacePanel(housingPanel, FindTransform(targetScene, "Chest"), new Vector3(0f, 1.35f, 0f));

                CloneHud(oldScene, targetScene, camera);
                BakeBowlsAndFeeding(oldScene, targetScene, lobby);
                BakePauseUi(targetScene, camera);

                Transform fireplace = FindTransform(targetScene, "Fireplace");
                if (fireplace != null && fireplace.GetComponent<MushLobbyFireplaceVfx>() == null)
                    fireplace.gameObject.AddComponent<MushLobbyFireplaceVfx>();
                Transform bed = FindTransform(targetScene, "DogBed_Unity_MaterialSeparated");
                if (bed != null && bed.GetComponent<MushLobbyDogBedSpot>() == null)
                    bed.gameObject.AddComponent<MushLobbyDogBedSpot>();

                mapPanel?.SetActive(false);
                shopPanel?.SetActive(false);
                housingPanel?.SetActive(false);
            }
            finally
            {
                EditorSceneManager.CloseScene(oldScene, true);
            }
        }

        private static GameObject CloneReference(SerializedObject source, string property, Transform parent, Scene scene)
        {
            GameObject original = source.FindProperty(property)?.objectReferenceValue as GameObject;
            if (original == null)
                return null;
            GameObject clone = UnityEngine.Object.Instantiate(original, parent);
            clone.name = original.name;
            return clone;
        }

        private static TextMesh CloneTextReference(SerializedObject source, string property, GameObject clonedPanel)
        {
            TextMesh original = source.FindProperty(property)?.objectReferenceValue as TextMesh;
            if (original == null || clonedPanel == null)
                return null;
            foreach (TextMesh text in clonedPanel.GetComponentsInChildren<TextMesh>(true))
                if (text.name == original.name)
                    return text;
            return null;
        }

        private static GameObject[] CloneArray(SerializedObject source, string property, Transform parent, Scene scene)
        {
            SerializedProperty array = source.FindProperty(property);
            if (array == null || !array.isArray)
                return Array.Empty<GameObject>();
            GameObject[] result = new GameObject[array.arraySize];
            for (int index = 0; index < array.arraySize; index++)
            {
                GameObject original = array.GetArrayElementAtIndex(index).objectReferenceValue as GameObject;
                if (original == null)
                    continue;
                result[index] = UnityEngine.Object.Instantiate(original, parent);
                result[index].name = original.name;
            }
            return result;
        }

        private static void CreateHotspot(Scene scene, Transform parent, string targetName, string label,
            MushLobbyController controller, MushLobbyAction action)
        {
            Transform target = FindTransform(scene, targetName);
            if (target == null)
                return;
            Bounds bounds = BoundsOf(target);
            GameObject hotspot = new(label + " Interaction");
            hotspot.transform.SetParent(parent, true);
            hotspot.transform.position = bounds.center;
            BoxCollider box = hotspot.AddComponent<BoxCollider>();
            box.size = new Vector3(Mathf.Max(0.5f, bounds.size.x), Mathf.Max(0.5f, bounds.size.y), Mathf.Max(0.35f, bounds.size.z));
            box.isTrigger = true;
            hotspot.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            MushLobbyInteractable interactable = hotspot.AddComponent<MushLobbyInteractable>();
            GameObject hoverPanelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI_Panel_Sample/Prefab/LobbyMenuUI.prefab");
            interactable.Configure(controller, action, null, hoverPanelPrefab);
        }

        private static void PlacePanel(GameObject panel, Transform anchor, Vector3 localPosition)
        {
            if (panel == null || anchor == null)
                return;
            panel.transform.SetParent(anchor, false);
            panel.transform.localPosition = localPosition;
            panel.transform.localRotation = Quaternion.identity;
        }

        private static void CloneHud(Scene oldScene, Scene targetScene, Camera camera)
        {
            MushLobbyStaminaHud sourceHud = FindComponent<MushLobbyStaminaHud>(oldScene);
            if (sourceHud == null)
                return;
            Canvas sourceCanvas = sourceHud.GetComponentInParent<Canvas>();
            if (sourceCanvas == null)
                return;
            GameObject hud = UnityEngine.Object.Instantiate(sourceCanvas.gameObject);
            hud.name = "Lobby Dog Status UI";
            SceneManager.MoveGameObjectToScene(hud, targetScene);
            Canvas canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = camera;
        }

        private static void BakeBowlsAndFeeding(Scene oldScene, Scene targetScene, Transform lobby)
        {
            Transform bowl = FindTransform(targetScene, "DogFoodBowl_Unity_MaterialSeparated");
            if (bowl == null)
                return;
            GameObject second = UnityEngine.Object.Instantiate(bowl.gameObject, bowl.parent);
            second.name = "DogFoodBowl_Second_Blue";
            second.transform.position = bowl.position + bowl.right * 0.72f;
            Material blueMaterial = null;
            Renderer firstRenderer = second.GetComponentInChildren<Renderer>(true);
            if (firstRenderer != null && firstRenderer.sharedMaterial != null)
            {
                const string materialFolder = "Assets/Mush/GeneratedMaterials";
                if (!AssetDatabase.IsValidFolder(materialFolder))
                    AssetDatabase.CreateFolder("Assets/Mush", "GeneratedMaterials");
                const string materialPath = materialFolder + "/PM_Lobby_Blue_Dog_Bowl.mat";
                blueMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (blueMaterial == null)
                {
                    blueMaterial = new Material(firstRenderer.sharedMaterial)
                    {
                        name = "PM Lobby Blue Dog Bowl",
                        color = Color.Lerp(firstRenderer.sharedMaterial.color, new Color(0.25f, 0.52f, 0.95f), 0.65f)
                    };
                    AssetDatabase.CreateAsset(blueMaterial, materialPath);
                }
            }
            foreach (Renderer renderer in second.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                if (blueMaterial != null && materials.Length > 0)
                    materials[0] = blueMaterial;
                renderer.sharedMaterials = materials;
            }

            MushLobbyFeedingStation source = FindComponent<MushLobbyFeedingStation>(oldScene);
            if (source == null)
                return;
            GameObject stationObject = UnityEngine.Object.Instantiate(source.gameObject, lobby);
            stationObject.name = "Mush Dog Feeding Station";
            stationObject.transform.position = (bowl.position + second.transform.position) * 0.5f;
            Transform left = Find(stationObject.transform, "Left Dog Food Bowl");
            Transform right = Find(stationObject.transform, "Right Dog Food Bowl");
            if (left != null) UnityEngine.Object.DestroyImmediate(left.gameObject);
            if (right != null) UnityEngine.Object.DestroyImmediate(right.gameObject);
            bowl.name = "Left Dog Food Bowl";
            second.name = "Right Dog Food Bowl";
            bowl.SetParent(stationObject.transform, true);
            second.transform.SetParent(stationObject.transform, true);
            bowl.localPosition = new Vector3(-1.18f, 0f, 0f);
            second.transform.localPosition = new Vector3(-0.5f, 0f, 0f);
            Transform canister = Find(stationObject.transform, "Dog Food Canister");
            if (canister != null) canister.localPosition = new Vector3(0.36f, -0.116f, 0f);
            Transform canisterStand = Find(stationObject.transform, "Canister Stand");
            if (canisterStand != null) canisterStand.localPosition = new Vector3(0.36f, -0.461f, 0f);
        }

        private static void BakePauseUi(Scene scene, Camera camera)
        {
            GameObject canvasObject = new("Lobby Pause UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject pausePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI_Panel_Sample/Prefab/PauseUI.prefab");
            GameObject optionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI_Panel_Sample/Prefab/OptionUI.prefab");
            GameObject pause = pausePrefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(pausePrefab, canvasObject.transform) : null;
            GameObject options = optionPrefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(optionPrefab, canvasObject.transform) : null;
            if (pause != null)
            {
                pause.name = "Lobby Pause Panel";
                RenameButton(pause.transform, "Button_Resume", "돌아가기");
                RenameButton(pause.transform, "Button_Lobby", "타이틀로", "Button_Title");
                RenameButton(pause.transform, "Button_Option", "옵션");
                RenameButton(pause.transform, "Button_Quit", "나가기");
                Transform recover = Find(pause.transform, "Button_Recover");
                if (recover != null) recover.gameObject.SetActive(false);
                pause.SetActive(false);
            }
            if (options != null)
            {
                options.name = "Lobby Option Panel";
                options.SetActive(false);
            }
            MushLobbyPauseMenu menu = canvasObject.AddComponent<MushLobbyPauseMenu>();
            menu.ConfigureAuthored(camera, pause, options, canvas);
            EditorUtility.SetDirty(menu);
        }

        private static void RenameButton(Transform root, string buttonName, string label, string renamed = null)
        {
            Transform button = Find(root, buttonName);
            if (button == null) return;
            if (!string.IsNullOrEmpty(renamed)) button.name = renamed;
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label;
        }

        private static Bounds BoundsOf(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new(root.position, Vector3.one * 0.5f);
            bool ready = false;
            foreach (Renderer renderer in renderers)
            {
                if (!ready) { bounds = renderer.bounds; ready = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        private static void ConfigureDog(MushLobbyDogRoamer dog, Camera camera, Transform rig, float sideOffset)
        {
            if (dog == null) return;
            dog.ConfigureCharacter(null, camera != null ? camera.transform : null, sideOffset);
            Transform head = FindContaining(dog.transform, "Head");
            Transform leftEye = FindContaining(dog.transform, "Eye_L") ?? FindContaining(dog.transform, "Left Eye");
            Transform rightEye = FindContaining(dog.transform, "Eye_R") ?? FindContaining(dog.transform, "Right Eye");
            Transform mouth = FindContaining(dog.transform, "Mouth");
            MushLobbyDogExpression expression = dog.GetComponent<MushLobbyDogExpression>();
            expression?.Configure(dog, head, leftEye, rightEye, mouth, camera);
            Transform leftHand = Find(rig, "Left Controller");
            Transform rightHand = Find(rig, "Right Controller");
            MushLobbyDogInteraction interaction = dog.GetComponent<MushLobbyDogInteraction>();
            interaction?.Configure(dog, head, leftHand, rightHand);
        }

        private static Transform FindContaining(Transform root, string fragment)
        {
            if (root == null) return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            return null;
        }

        private static Transform FindTransform(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = Find(root.transform, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name)
                    return child;
            return null;
        }
    }
}
