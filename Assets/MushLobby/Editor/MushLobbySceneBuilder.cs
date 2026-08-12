using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mush.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby.Editor
{
    [InitializeOnLoad]
    public static class MushLobbySceneBuilder
    {
        private const string LobbyRoot = "Assets/MushLobby";
        private const string MaterialRoot = LobbyRoot + "/Materials";
        private const string ScenePath = "Assets/Scenes/MushLobby.unity";
        private const string LobbyModelPath = "Assets/Scenes/Mush_Lobby.fbx";
        private const string XrRigPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Variant.prefab";
        private const string LeftHandPath = "Assets/Oculus Hands/Prefabs/Left Hand Model.prefab";
        private const string RightHandPath = "Assets/Oculus Hands/Prefabs/Right Hand Model.prefab";
        private const string AxisRevisionMarker = "Mush Lobby Revision 3 - Axis Desktop Look Free Dogs";
        private static int stableEditFrames;

        private struct PanelButtonSpec
        {
            public string label;
            public MushLobbyAction action;

            public PanelButtonSpec(string newLabel, MushLobbyAction newAction)
            {
                label = newLabel;
                action = newAction;
            }
        }

        static MushLobbySceneBuilder()
        {
            QueueAutomaticMaintenance();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    QueueAutomaticMaintenance();
                else
                    CancelAutomaticMaintenance();
            };
        }

        private static void QueueAutomaticMaintenance()
        {
            stableEditFrames = 0;
            EditorApplication.update -= ApplyAfterStableEditFrames;
            EditorApplication.update += ApplyAfterStableEditFrames;
        }

        private static void CancelAutomaticMaintenance()
        {
            stableEditFrames = 0;
            EditorApplication.update -= ApplyAfterStableEditFrames;
        }

        private static void ApplyAfterStableEditFrames()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlaying ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                stableEditFrames = 0;
                return;
            }

            if (++stableEditFrames < 3)
                return;

            CancelAutomaticMaintenance();
            EnsureSceneExists();
            ApplyAxisRevision();
        }

        [MenuItem("Mush/Lobby/Create Seated Lobby Prototype")]
        public static void CreateFromMenu()
        {
            if (File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Mush Lobby", "MushLobby scene already exists.", "OK");
                return;
            }

            BuildScene();
        }

        public static void BuildFromCommandLine()
        {
            BuildScene();
        }

        private static void EnsureSceneExists()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!File.Exists(ScenePath) && AssetDatabase.LoadAssetAtPath<GameObject>(LobbyModelPath) != null)
                BuildScene();
        }

        private static void ApplyAxisRevision()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath))
                return;

            Scene previousScene = SceneManager.GetActiveScene();
            Scene targetScene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForRevision = !targetScene.IsValid() || !targetScene.isLoaded;
            if (openedForRevision)
                targetScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            if (!targetScene.IsValid() || !targetScene.isLoaded || FindInScene(targetScene, AxisRevisionMarker) != null)
            {
                if (openedForRevision && targetScene.IsValid() && targetScene.isLoaded)
                    EditorSceneManager.CloseScene(targetScene, true);
                return;
            }

            SceneManager.SetActiveScene(targetScene);
            try
            {
                SetTransform(targetScene, "Seated XR Player - No Locomotion", new Vector3(0f, 0f, 1.72f), new Vector3(0f, 180f, 0f));
                GameObject seatedRig = FindInScene(targetScene, "Seated XR Player - No Locomotion");
                if (seatedRig != null)
                {
                    MushDesktopSeatedLook desktopLook = seatedRig.GetComponent<MushDesktopSeatedLook>();
                    if (desktopLook == null)
                        desktopLook = seatedRig.AddComponent<MushDesktopSeatedLook>();
                    Camera seatedCamera = seatedRig.GetComponentInChildren<Camera>(true);
                    desktopLook.Configure(seatedCamera != null ? seatedCamera.transform : null);
                }
                SetTransform(targetScene, "INT_MoneyBag Scene Position", new Vector3(-0.75f, 0f, 0.40f), Vector3.zero);
                SetTransform(targetScene, "PROP_MoneyBagStool Scene Position", new Vector3(-0.75f, 0f, 0.40f), Vector3.zero);
                SetTransform(targetScene, "INT_DogBowl Scene Position", new Vector3(0f, 0f, -0.55f), Vector3.zero);
                SetTransform(targetScene, "Map Board Interaction", new Vector3(1.78f, 1.58f, -2.22f), Vector3.zero);
                SetTransform(targetScene, "Money Bag Interaction", new Vector3(-2.40f, 0.61f, -0.05f), Vector3.zero);
                SetTransform(targetScene, "Housing Chest Interaction", new Vector3(-2.15f, 0.56f, -1.22f), Vector3.zero);
                SetTransform(targetScene, "Lobby Status Board", new Vector3(0f, 2.98f, -2.31f), Vector3.zero);
                SetTransform(targetScene, "Lobby Status", new Vector3(0f, 2.98f, -2.25f), new Vector3(0f, 180f, 0f));
                SetTransform(targetScene, "Fireplace Light", new Vector3(-1.87f, 0.82f, -1.78f), Vector3.zero);

                foreach (string panelName in new[] { "MAP BOARD Panel", "MONEY BAG SHOP Panel", "HOUSE FLOOR PLAN Panel" })
                    SetTransform(targetScene, panelName, new Vector3(0f, 1.37f, -0.08f), new Vector3(0f, 180f, 0f));

                SetTransform(targetScene, "Mochi - Gray Husky", new Vector3(-0.72f, 0f, 0.35f), Vector3.zero);
                SetTransform(targetScene, "Bori - Brown Husky", new Vector3(0.63f, 0f, -0.55f), Vector3.zero);
                SetTransform(targetScene, "Housing Slot 1 - Stool", new Vector3(-0.88f, 0f, -0.95f), Vector3.zero);
                SetTransform(targetScene, "Housing Slot 2 - Plant", new Vector3(0f, 0f, -1.10f), Vector3.zero);
                SetTransform(targetScene, "Housing Slot 3 - Side Table", new Vector3(0.88f, 0f, -0.95f), Vector3.zero);

                SetLabel(targetScene, "MAPS Label", 0.18f);
                SetLabel(targetScene, "SHOP Label", 0.48f);
                SetLabel(targetScene, "HOUSE Label", 0.55f);

                foreach (string dogName in new[] { "Mochi - Gray Husky", "Bori - Brown Husky" })
                {
                    GameObject dog = FindInScene(targetScene, dogName);
                    if (dog == null)
                        continue;
                    MushLobbyDogRoamer roamer = dog.GetComponent<MushLobbyDogRoamer>();
                    if (roamer != null)
                        roamer.Configure(FindChild(dog.transform, "Dog Visual"), FindChild(dog.transform, "Tail"), new Vector2(-2.15f, -1.65f), new Vector2(2.05f, 0.90f));
                }

                GameObject oldGable = FindInScene(targetScene, "Back Gable Fill - Scene Fix");
                if (oldGable != null)
                    Object.DestroyImmediate(oldGable);
                GameObject cabin = FindInScene(targetScene, "Mush Lobby Cabin");
                Material wall = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + "/CabinWall.mat");
                if (cabin != null && wall != null)
                    CreateGableFill(cabin.transform, wall);

                GameObject marker = new GameObject(AxisRevisionMarker);
                marker.hideFlags = HideFlags.HideInHierarchy;
                SceneManager.MoveGameObjectToScene(marker, targetScene);
                EditorSceneManager.MarkSceneDirty(targetScene);
                EditorSceneManager.SaveScene(targetScene);
                Debug.Log("[Mush] Corrected MushLobby FBX axis direction and seated view.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (openedForRevision && targetScene.IsValid() && targetScene.isLoaded)
                    EditorSceneManager.CloseScene(targetScene, true);
            }
        }

        private static void SetTransform(Scene scene, string objectName, Vector3 localPosition, Vector3 localEuler)
        {
            GameObject target = FindInScene(scene, objectName);
            if (target == null)
                return;
            target.transform.localPosition = localPosition;
            target.transform.localRotation = Quaternion.Euler(localEuler);
        }

        private static void SetLabel(Scene scene, string objectName, float localZ)
        {
            GameObject label = FindInScene(scene, objectName);
            if (label == null)
                return;
            Vector3 position = label.transform.localPosition;
            label.transform.localPosition = new Vector3(position.x, position.y, localZ);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        private static void BuildScene()
        {
            EnsureFolder(MaterialRoot);

            Scene previousScene = SceneManager.GetActiveScene();
            bool replaceEmptyUntitledScene = string.IsNullOrEmpty(previousScene.path) && !previousScene.isDirty;
            Scene lobbyScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replaceEmptyUntitledScene ? NewSceneMode.Single : NewSceneMode.Additive);
            lobbyScene.name = "MushLobby";
            SceneManager.SetActiveScene(lobbyScene);

            try
            {
                Material wall = GetMaterial("CabinWall", new Color(0.53f, 0.30f, 0.15f), 0.20f);
                Material darkWood = GetMaterial("CabinDarkWood", new Color(0.20f, 0.085f, 0.035f), 0.16f);
                Material floorWood = GetMaterial("CabinFloor", new Color(0.43f, 0.22f, 0.09f), 0.22f);
                Material cream = GetMaterial("CabinCream", new Color(0.82f, 0.70f, 0.50f), 0.26f);
                Material stone = GetMaterial("FireplaceStone", new Color(0.32f, 0.29f, 0.28f), 0.15f);
                Material charcoal = GetMaterial("FireplaceInner", new Color(0.055f, 0.04f, 0.035f), 0.08f);
                Material fire = GetMaterial("FireGlow", new Color(1f, 0.30f, 0.025f), 0.24f, Color.white * 2.6f);
                Material rug = GetMaterial("LobbyRug", new Color(0.35f, 0.055f, 0.045f), 0.20f);
                Material gold = GetMaterial("LobbyGold", new Color(0.90f, 0.57f, 0.08f), 0.38f);
                Material glass = GetMaterial("WindowBlue", new Color(0.27f, 0.55f, 0.68f), 0.72f);
                Material panel = GetMaterial("PanelBackground", new Color(0.09f, 0.055f, 0.032f), 0.16f);
                Material button = GetMaterial("PanelButton", new Color(0.58f, 0.30f, 0.10f), 0.24f);
                Material accent = GetMaterial("PanelAccent", new Color(0.95f, 0.58f, 0.10f), 0.30f);

                BuildLighting();

                GameObject sceneRoot = new GameObject("Mush Lobby Prototype");
                GameObject model = InstantiateLobbyModel(sceneRoot.transform);
                if (model == null)
                    throw new FileNotFoundException("Mush_Lobby.fbx could not be loaded.", LobbyModelPath);

                ColorCabinModel(model.transform, wall, darkWood, floorWood, cream, stone, charcoal, fire, rug, gold, glass);
                GroupAndOffset(model.transform, "INT_MoneyBag", new Vector3(-0.75f, 0f, 0.40f));
                GroupAndOffset(model.transform, "PROP_MoneyBagStool", new Vector3(-0.75f, 0f, 0.40f));
                GroupAndOffset(model.transform, "INT_DogBowl", new Vector3(0f, 0f, -0.55f));
                AddEnvironmentColliders(model.transform);
                CreateGableFill(model.transform, wall);

                GameObject xrRig = BuildXrRig(sceneRoot.transform, out Camera camera);

                GameObject controllerObject = new GameObject("Lobby Game State");
                controllerObject.transform.SetParent(sceneRoot.transform, false);
                MushLobbyController controller = controllerObject.AddComponent<MushLobbyController>();
                controller.SetKoreanFont(AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/Hakgyoansim_PosterB.ttf"));

                EnsureInteractionManager(sceneRoot.transform, xrRig);

                GameObject mapHotspot = CreateHotspot(
                    "Map Board Interaction", sceneRoot.transform, new Vector3(1.78f, 1.58f, -2.22f),
                    new Vector3(1.65f, 1.25f, 0.30f), controller, MushLobbyAction.OpenMapBoard);
                GameObject shopHotspot = CreateHotspot(
                    "Money Bag Interaction", sceneRoot.transform, new Vector3(-2.40f, 0.61f, -0.05f),
                    new Vector3(0.95f, 1.22f, 0.90f), controller, MushLobbyAction.OpenShop);
                GameObject housingHotspot = CreateHotspot(
                    "Housing Chest Interaction", sceneRoot.transform, new Vector3(-2.15f, 0.56f, -1.22f),
                    new Vector3(1.25f, 1.15f, 0.95f), controller, MushLobbyAction.OpenHousing);
                CreateLabel("지도", mapHotspot.transform, new Vector3(0f, 0.78f, 0.18f), 0.038f, new Color(1f, 0.76f, 0.25f));
                CreateLabel("상점", shopHotspot.transform, new Vector3(0f, 0.77f, 0.48f), 0.036f, new Color(1f, 0.76f, 0.25f));
                CreateLabel("집 꾸미기", housingHotspot.transform, new Vector3(0f, 0.72f, 0.55f), 0.034f, new Color(1f, 0.76f, 0.25f));

                GameObject statusBoard = CreatePrimitive(
                    "Lobby Status Board", PrimitiveType.Cube, sceneRoot.transform,
                    new Vector3(0f, 2.98f, -2.31f), new Vector3(3.15f, 0.64f, 0.08f), Vector3.zero, darkWood, false);
                TextMesh lobbyStatus = CreateText(
                    "Lobby Status", sceneRoot.transform, new Vector3(0f, 2.98f, -2.25f), 0.026f,
                    TextAnchor.MiddleCenter, new Color(1f, 0.80f, 0.42f), "머쉬 산장");
                lobbyStatus.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                GameObject mapPanel = CreatePanel(
                    "맵 게시판", controller, sceneRoot.transform, panel, button, accent,
                    new[]
                    {
                        new PanelButtonSpec("기본 설원", MushLobbyAction.SelectSnowfield),
                        new PanelButtonSpec("나무 숲", MushLobbyAction.SelectForest)
                    }, out TextMesh mapStatus);

                GameObject shopPanel = CreatePanel(
                    "주머니 상점", controller, sceneRoot.transform, panel, button, accent,
                    new[]
                    {
                        new PanelButtonSpec("개 목도리 30골드", MushLobbyAction.BuyScarf),
                        new PanelButtonSpec("나무 숲 지도", MushLobbyAction.BuyForest)
                    }, out TextMesh shopStatus);

                GameObject housingPanel = CreatePanel(
                    "집 꾸미기", controller, sceneRoot.transform, panel, button, accent,
                    new[]
                    {
                        new PanelButtonSpec("공간 1", MushLobbyAction.HousingSlotA),
                        new PanelButtonSpec("공간 2", MushLobbyAction.HousingSlotB),
                        new PanelButtonSpec("공간 3", MushLobbyAction.HousingSlotC)
                    }, out TextMesh housingStatus, true);

                BuildDogTeam(sceneRoot.transform, out MushLobbyDogRoamer[] dogs, out GameObject[] scarves);
                GameObject[] furniture = BuildHousingFurniture(sceneRoot.transform);

                controller.Configure(
                    camera, lobbyStatus,
                    mapPanel, shopPanel, housingPanel,
                    mapStatus, shopStatus, housingStatus,
                    scarves, furniture, dogs);

                mapPanel.SetActive(false);
                shopPanel.SetActive(false);
                housingPanel.SetActive(false);
                foreach (GameObject scarf in scarves) scarf.SetActive(false);
                foreach (GameObject item in furniture) item.SetActive(false);

                EditorSceneManager.SaveScene(lobbyScene, ScenePath);
                PutLobbyFirstInBuildSettings();
                AssetDatabase.SaveAssets();
                Debug.Log("[Mush] Created seated lobby prototype: " + ScenePath);
            }
            finally
            {
                if (!replaceEmptyUntitledScene && previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);

                if (!replaceEmptyUntitledScene && lobbyScene.IsValid() && lobbyScene.isLoaded)
                    EditorSceneManager.CloseScene(lobbyScene, true);
            }
        }

        private static void BuildLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.33f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.18f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.065f, 0.04f);
            RenderSettings.fog = false;

            GameObject moonlight = new GameObject("Soft Window Light");
            Light directional = moonlight.AddComponent<Light>();
            directional.type = LightType.Directional;
            directional.color = new Color(0.72f, 0.82f, 1f);
            directional.intensity = 0.75f;
            directional.shadows = LightShadows.Soft;
            moonlight.transform.rotation = Quaternion.Euler(38f, -18f, 0f);

            GameObject firelight = new GameObject("Fireplace Light");
            firelight.transform.position = new Vector3(-1.87f, 0.82f, -1.78f);
            Light point = firelight.AddComponent<Light>();
            point.type = LightType.Point;
            point.color = new Color(1f, 0.38f, 0.10f);
            point.intensity = 2.8f;
            point.range = 5.2f;
            point.shadows = LightShadows.Soft;
        }

        private static GameObject InstantiateLobbyModel(Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyModelPath);
            if (prefab == null)
                return null;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = "Mush Lobby Cabin";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return instance;
        }

        private static void ColorCabinModel(
            Transform model, Material wall, Material darkWood, Material floorWood, Material cream,
            Material stone, Material charcoal, Material fire, Material rug, Material gold, Material glass)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.gameObject.name;
                Material material = wall;

                if (name.Contains("Beam") || name.Contains("Roof") || name.Contains("Ridge")) material = darkWood;
                else if (name.Contains("Floor")) material = floorWood;
                else if (name.Contains("MapBoard")) material = name.Contains("Paper") ? cream : darkWood;
                else if (name.Contains("DogBowl_Food")) material = darkWood;
                else if (name.Contains("DogBowl")) material = cream;
                else if (name.Contains("HousingChest_Band") || name.Contains("HousingChest_Lock")) material = gold;
                else if (name.Contains("HousingChest")) material = darkWood;
                else if (name.Contains("MoneyBag")) material = name.Contains("Stool") ? darkWood : gold;
                else if (name.Contains("CenterRug")) material = rug;
                else if (name.Contains("FireFlame")) material = fire;
                else if (name.Contains("FireLog")) material = darkWood;
                else if (name.Contains("Fireplace_Inner")) material = charcoal;
                else if (name.Contains("Fireplace")) material = stone;
                else if (name.Contains("WindowGlass")) material = glass;
                else if (name.Contains("WindowFrame")) material = darkWood;

                renderer.sharedMaterial = material;
            }
        }

        private static void GroupAndOffset(Transform model, string namePrefix, Vector3 offset)
        {
            List<Transform> matches = model.GetComponentsInChildren<Transform>(true)
                .Where(item => item != model && item.name.StartsWith(namePrefix))
                .ToList();
            if (matches.Count == 0)
                return;

            GameObject group = new GameObject(namePrefix + " Scene Position");
            group.transform.SetParent(model, false);
            foreach (Transform item in matches)
                item.SetParent(group.transform, true);
            group.transform.localPosition += offset;
        }

        private static void AddEnvironmentColliders(Transform model)
        {
            string[] collidablePrefixes = { "ENV_FloorBase", "ENV_LeftWall", "ENV_RightWall", "ENV_BackWall" };
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            {
                if (!collidablePrefixes.Any(prefix => child.name.StartsWith(prefix)))
                    continue;
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter == null || child.GetComponent<Collider>() != null)
                    continue;
                MeshCollider collider = child.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
        }

        private static void CreateGableFill(Transform parent, Material material)
        {
            Mesh mesh = GetGableMesh();
            GameObject gable = new GameObject("Back Gable Fill - Scene Fix");
            gable.transform.SetParent(parent, false);
            MeshFilter filter = gable.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = gable.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
        }

        private static Mesh GetGableMesh()
        {
            const string path = MaterialRoot + "/BackGableV2.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                return existing;

            Mesh mesh = new Mesh { name = "Mush Back Gable" };
            mesh.vertices = new[]
            {
                new Vector3(-2.96f, 2.62f, -2.47f),
                new Vector3(0f, 4.34f, -2.47f),
                new Vector3(2.96f, 2.62f, -2.47f),
                new Vector3(-2.96f, 2.62f, -2.57f),
                new Vector3(0f, 4.34f, -2.57f),
                new Vector3(2.96f, 2.62f, -2.57f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1,
                3, 4, 5,
                0, 3, 4, 0, 4, 1,
                1, 4, 5, 1, 5, 2,
                2, 5, 3, 2, 3, 0
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static GameObject BuildXrRig(Transform parent, out Camera camera)
        {
            camera = null;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPath);
            GameObject rig;

            if (prefab != null)
            {
                rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }
            else
            {
                rig = new GameObject("XR Player Fallback");
                rig.transform.SetParent(parent, false);
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.transform.SetParent(rig.transform, false);
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                cameraObject.tag = "MainCamera";
            }

            rig.name = "Seated XR Player - No Locomotion";
            rig.transform.localPosition = new Vector3(0f, 0f, 1.72f);
            rig.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            rig.AddComponent<MushSeatedRigLock>();

            Transform locomotion = FindChild(rig.transform, "Locomotion");
            if (locomotion != null)
                locomotion.gameObject.SetActive(false);

            Transform leftController = FindChild(rig.transform, "Left Controller");
            Transform rightController = FindChild(rig.transform, "Right Controller");
            if (leftController != null)
            {
                HideControllerMeshes(leftController);
                AddHand(leftController, LeftHandPath, "Lobby Left Hand");
            }
            if (rightController != null)
            {
                HideControllerMeshes(rightController);
                AddHand(rightController, RightHandPath, "Lobby Right Hand");
            }

            camera = rig.GetComponentInChildren<Camera>(true);
            if (camera != null)
            {
                // The prefab's Camera Offset already supplies seated eye height.
                camera.transform.localPosition = Vector3.zero;
                camera.transform.localRotation = Quaternion.identity;
                camera.nearClipPlane = 0.04f;
                camera.farClipPlane = 60f;
                camera.fieldOfView = 84f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.11f, 0.15f);
                camera.gameObject.tag = "MainCamera";
            }

            MushDesktopSeatedLook desktopLook = rig.AddComponent<MushDesktopSeatedLook>();
            desktopLook.Configure(camera != null ? camera.transform : null);

            return rig;
        }

        private static void EnsureInteractionManager(Transform parent, GameObject rig)
        {
            if (rig != null && rig.GetComponentInChildren<XRInteractionManager>(true) != null)
                return;

            GameObject managerObject = new GameObject("XR Interaction Manager");
            managerObject.transform.SetParent(parent, false);
            managerObject.AddComponent<XRInteractionManager>();
        }

        private static GameObject CreateHotspot(
            string name, Transform parent, Vector3 position, Vector3 size,
            MushLobbyController controller, MushLobbyAction action)
        {
            GameObject hotspot = new GameObject(name);
            hotspot.transform.SetParent(parent, false);
            hotspot.transform.localPosition = position;
            BoxCollider collider = hotspot.AddComponent<BoxCollider>();
            collider.size = size;
            hotspot.AddComponent<XRSimpleInteractable>();
            MushLobbyInteractable interactable = hotspot.AddComponent<MushLobbyInteractable>();
            interactable.Configure(controller, action);
            return hotspot;
        }

        private static GameObject CreatePanel(
            string title, MushLobbyController controller, Transform parent,
            Material backgroundMaterial, Material buttonMaterial, Material accentMaterial,
            PanelButtonSpec[] buttons, out TextMesh statusText, bool floorPlan = false)
        {
            GameObject root = new GameObject(title + " Panel");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 1.37f, -0.08f);
            root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            CreatePrimitive("Panel Back", PrimitiveType.Cube, root.transform,
                Vector3.zero, new Vector3(2.85f, 1.62f, 0.06f), Vector3.zero, backgroundMaterial, false);
            CreatePrimitive("Panel Header", PrimitiveType.Cube, root.transform,
                new Vector3(0f, 0.61f, -0.045f), new Vector3(2.55f, 0.24f, 0.06f), Vector3.zero, accentMaterial, false);
            CreateText("Title", root.transform, new Vector3(0f, 0.61f, -0.085f), 0.034f,
                TextAnchor.MiddleCenter, Color.white, title);

            statusText = CreateText("Status", root.transform, new Vector3(0f, 0.29f, -0.085f), 0.020f,
                TextAnchor.MiddleCenter, new Color(1f, 0.83f, 0.50f), "상태");

            float spacing = buttons.Length == 3 ? 0.84f : 0.98f;
            float startX = -spacing * (buttons.Length - 1) * 0.5f;
            float y = floorPlan ? -0.18f : -0.14f;
            for (int index = 0; index < buttons.Length; index++)
            {
                CreatePanelButton(
                    buttons[index].label, root.transform,
                    new Vector3(startX + index * spacing, y, -0.075f),
                    buttons.Length == 3 ? new Vector3(0.70f, 0.34f, 0.10f) : new Vector3(0.88f, 0.34f, 0.10f),
                    controller, buttons[index].action, buttonMaterial);
            }

            CreatePanelButton(
                "닫기", root.transform, new Vector3(0f, -0.61f, -0.075f),
                new Vector3(0.72f, 0.24f, 0.10f), controller, MushLobbyAction.ClosePanel, accentMaterial);

            return root;
        }

        private static void CreatePanelButton(
            string label, Transform parent, Vector3 localPosition, Vector3 scale,
            MushLobbyController controller, MushLobbyAction action, Material material)
        {
            GameObject button = CreatePrimitive(
                label + " Button", PrimitiveType.Cube, parent,
                localPosition, scale, Vector3.zero, material, true);
            XRSimpleInteractable xrInteractable = button.AddComponent<XRSimpleInteractable>();
            xrInteractable.selectMode = InteractableSelectMode.Single;
            MushLobbyInteractable interactable = button.AddComponent<MushLobbyInteractable>();
            interactable.Configure(controller, action, button.GetComponent<Renderer>());
            CreateText("Label", parent, localPosition + new Vector3(0f, 0f, -0.065f), 0.030f,
                TextAnchor.MiddleCenter, Color.white, label);
        }

        private static void BuildDogTeam(
            Transform parent, out MushLobbyDogRoamer[] dogs, out GameObject[] scarves)
        {
            Material gray = GetMaterial("LobbyDogGray", new Color(0.24f, 0.29f, 0.34f), 0.25f);
            Material brown = GetMaterial("LobbyDogBrown", new Color(0.48f, 0.25f, 0.12f), 0.24f);
            Material white = GetMaterial("LobbyDogWhite", new Color(0.88f, 0.86f, 0.80f), 0.25f);
            Material black = GetMaterial("LobbyDogBlack", new Color(0.025f, 0.025f, 0.03f), 0.16f);
            Material red = GetMaterial("LobbyDogScarf", new Color(0.72f, 0.045f, 0.035f), 0.24f);

            MushLobbyDogRoamer left = BuildLobbyDog(
                "Mochi - Gray Husky", parent, new Vector3(-0.72f, 0f, 0.35f), gray, white, black, red,
                out GameObject leftScarf);
            MushLobbyDogRoamer right = BuildLobbyDog(
                "Bori - Brown Husky", parent, new Vector3(0.63f, 0f, -0.55f), brown, white, black, red,
                out GameObject rightScarf);

            dogs = new[] { left, right };
            scarves = new[] { leftScarf, rightScarf };
        }

        private static MushLobbyDogRoamer BuildLobbyDog(
            string name, Transform parent, Vector3 position,
            Material coat, Material white, Material black, Material scarfMaterial,
            out GameObject scarf)
        {
            GameObject dog = new GameObject(name);
            dog.transform.SetParent(parent, false);
            dog.transform.localPosition = position;

            GameObject visual = new GameObject("Dog Visual");
            visual.transform.SetParent(dog.transform, false);
            visual.transform.localScale = Vector3.one * 0.58f;

            CreatePrimitive("Body", PrimitiveType.Capsule, visual.transform,
                new Vector3(0f, 0.78f, 0f), new Vector3(0.50f, 0.72f, 0.50f), new Vector3(90f, 0f, 0f), coat, false);
            CreatePrimitive("Chest", PrimitiveType.Sphere, visual.transform,
                new Vector3(0f, 0.82f, 0.45f), new Vector3(0.43f, 0.55f, 0.40f), Vector3.zero, white, false);
            CreatePrimitive("Head", PrimitiveType.Sphere, visual.transform,
                new Vector3(0f, 1.25f, 0.73f), new Vector3(0.48f, 0.49f, 0.48f), Vector3.zero, coat, false);
            CreatePrimitive("Muzzle", PrimitiveType.Sphere, visual.transform,
                new Vector3(0f, 1.15f, 1.04f), new Vector3(0.31f, 0.24f, 0.34f), Vector3.zero, white, false);
            CreatePrimitive("Nose", PrimitiveType.Sphere, visual.transform,
                new Vector3(0f, 1.17f, 1.23f), new Vector3(0.14f, 0.10f, 0.11f), Vector3.zero, black, false);
            CreatePrimitive("Left Ear", PrimitiveType.Cube, visual.transform,
                new Vector3(-0.18f, 1.57f, 0.70f), new Vector3(0.17f, 0.33f, 0.14f), new Vector3(-8f, 0f, -12f), coat, false);
            CreatePrimitive("Right Ear", PrimitiveType.Cube, visual.transform,
                new Vector3(0.18f, 1.57f, 0.70f), new Vector3(0.17f, 0.33f, 0.14f), new Vector3(-8f, 0f, 12f), coat, false);

            for (int side = -1; side <= 1; side += 2)
            {
                CreatePrimitive(side < 0 ? "Left Eye" : "Right Eye", PrimitiveType.Sphere, visual.transform,
                    new Vector3(side * 0.17f, 1.33f, 1.06f), new Vector3(0.072f, 0.072f, 0.052f), Vector3.zero, black, false);
                for (int row = -1; row <= 1; row += 2)
                {
                    float z = row * 0.36f;
                    CreatePrimitive("Leg", PrimitiveType.Cylinder, visual.transform,
                        new Vector3(side * 0.23f, 0.38f, z), new Vector3(0.105f, 0.30f, 0.105f), Vector3.zero, coat, false);
                    CreatePrimitive("Paw", PrimitiveType.Sphere, visual.transform,
                        new Vector3(side * 0.23f, 0.10f, z + 0.04f), new Vector3(0.16f, 0.10f, 0.21f), Vector3.zero, white, false);
                }
            }

            GameObject tail = CreatePrimitive("Tail", PrimitiveType.Capsule, visual.transform,
                new Vector3(0f, 1.05f, -0.74f), new Vector3(0.18f, 0.46f, 0.18f), new Vector3(-50f, 0f, 0f), coat, false);

            scarf = new GameObject("Equipped Scarf");
            scarf.transform.SetParent(visual.transform, false);
            CreatePrimitive("Scarf Collar", PrimitiveType.Cylinder, scarf.transform,
                new Vector3(0f, 1.02f, 0.48f), new Vector3(0.36f, 0.045f, 0.36f), Vector3.zero, scarfMaterial, false);
            CreatePrimitive("Scarf Tail", PrimitiveType.Cube, scarf.transform,
                new Vector3(0.22f, 0.83f, 0.43f), new Vector3(0.16f, 0.38f, 0.07f), new Vector3(0f, 0f, -18f), scarfMaterial, false);

            MushLobbyDogRoamer roamer = dog.AddComponent<MushLobbyDogRoamer>();
            roamer.Configure(visual.transform, tail.transform, new Vector2(-2.15f, -1.65f), new Vector2(2.05f, 0.90f));
            return roamer;
        }

        private static GameObject[] BuildHousingFurniture(Transform parent)
        {
            Material wood = GetMaterial("HousingWood", new Color(0.36f, 0.16f, 0.055f), 0.22f);
            Material green = GetMaterial("HousingGreen", new Color(0.10f, 0.36f, 0.18f), 0.18f);
            Material blue = GetMaterial("HousingBlue", new Color(0.10f, 0.30f, 0.55f), 0.26f);

            GameObject stool = new GameObject("Housing Slot 1 - Stool");
            stool.transform.SetParent(parent, false);
            stool.transform.localPosition = new Vector3(-0.88f, 0f, -0.95f);
            CreatePrimitive("Seat", PrimitiveType.Cylinder, stool.transform,
                new Vector3(0f, 0.38f, 0f), new Vector3(0.40f, 0.09f, 0.40f), Vector3.zero, wood, false);
            for (int side = -1; side <= 1; side += 2)
                CreatePrimitive("Leg", PrimitiveType.Cube, stool.transform,
                    new Vector3(side * 0.22f, 0.19f, 0f), new Vector3(0.09f, 0.38f, 0.09f), Vector3.zero, wood, false);

            GameObject plant = new GameObject("Housing Slot 2 - Plant");
            plant.transform.SetParent(parent, false);
            plant.transform.localPosition = new Vector3(0f, 0f, -1.10f);
            CreatePrimitive("Pot", PrimitiveType.Cylinder, plant.transform,
                new Vector3(0f, 0.22f, 0f), new Vector3(0.30f, 0.23f, 0.30f), Vector3.zero, blue, false);
            CreatePrimitive("Leaves", PrimitiveType.Sphere, plant.transform,
                new Vector3(0f, 0.63f, 0f), new Vector3(0.43f, 0.50f, 0.43f), Vector3.zero, green, false);

            GameObject table = new GameObject("Housing Slot 3 - Side Table");
            table.transform.SetParent(parent, false);
            table.transform.localPosition = new Vector3(0.88f, 0f, -0.95f);
            CreatePrimitive("Top", PrimitiveType.Cube, table.transform,
                new Vector3(0f, 0.48f, 0f), new Vector3(0.65f, 0.12f, 0.48f), Vector3.zero, wood, false);
            CreatePrimitive("Base", PrimitiveType.Cube, table.transform,
                new Vector3(0f, 0.23f, 0f), new Vector3(0.16f, 0.46f, 0.16f), Vector3.zero, wood, false);

            return new[] { stool, plant, table };
        }

        private static void AddHand(Transform controller, string path, string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return;
            GameObject hand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, controller);
            hand.name = name;
            hand.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            hand.transform.localScale = Vector3.one;
        }

        private static void HideControllerMeshes(Transform controller)
        {
            foreach (MeshRenderer renderer in controller.GetComponentsInChildren<MeshRenderer>(true))
                renderer.enabled = false;
            foreach (SkinnedMeshRenderer renderer in controller.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.enabled = false;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child;
            }
            return null;
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform match = FindChild(root.transform, name);
                if (match != null)
                    return match.gameObject;
            }
            return null;
        }

        private static GameObject CreatePrimitive(
            string name, PrimitiveType type, Transform parent,
            Vector3 localPosition, Vector3 localScale, Vector3 localEuler,
            Material material, bool keepCollider)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = Quaternion.Euler(localEuler);
            primitive.transform.localScale = localScale;
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            if (!keepCollider)
            {
                Collider collider = primitive.GetComponent<Collider>();
                if (collider != null)
                    Object.DestroyImmediate(collider);
            }
            return primitive;
        }

        private static void CreateLabel(
            string text, Transform parent, Vector3 localPosition, float characterSize, Color color)
        {
            TextMesh label = CreateText(text + " Label", parent, localPosition, characterSize, TextAnchor.MiddleCenter, color, text);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        private static TextMesh CreateText(
            string name, Transform parent, Vector3 localPosition, float characterSize,
            TextAnchor anchor, Color color, string text)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = localPosition;
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = anchor;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 64;
            textMesh.characterSize = characterSize;
            textMesh.color = color;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                textMesh.font = font;
                textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return textMesh;
        }

        private static Material GetMaterial(
            string name, Color color, float smoothness, Color? emission = null)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        private static void PutLobbyFirstInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(scene => scene.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
