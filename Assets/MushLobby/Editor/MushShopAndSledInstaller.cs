using System;
using System.IO;
using Mush.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Store.Editor
{
    [InitializeOnLoad]
    public static class MushShopAndSledInstaller
    {
        private const string LobbyScenePath = "Assets/Scenes/MushLobby.unity";
        private const string MaterialRoot = "Assets/MushStore/Materials";
        private const string LobbyMarker = "Mush Lobby Revision 6 - Large 3D Model Shop";
        private static int stableEditFrames;

        private struct ShopItemSpec
        {
            public string id;
            public string label;
            public string path;

            public ShopItemSpec(string newId, string newLabel, string newPath)
            {
                id = newId;
                label = newLabel;
                path = newPath;
            }
        }

        static MushShopAndSledInstaller()
        {
            QueueAutomaticApply();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    QueueAutomaticApply();
                else
                    CancelAutomaticApply();
            };
        }

        private static void QueueAutomaticApply()
        {
            stableEditFrames = 0;
            EditorApplication.update -= ApplyAfterStableEditFrames;
            EditorApplication.update += ApplyAfterStableEditFrames;
        }

        private static void CancelAutomaticApply()
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

            // Scene APIs can still reject calls during the same callback that
            // reports EnteredEditMode. Wait for several ordinary editor frames.
            if (++stableEditFrames < 3)
                return;

            CancelAutomaticApply();
            ApplyAll();
        }

        [MenuItem("Mush/Store/Install Large Model Shop And Sled")]
        public static void ApplyAll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EnsureFolder(MaterialRoot);
            AssetDatabase.SaveAssets();
        }

        private static void ApplyLobbyShop()
        {
            if (!File.Exists(LobbyScenePath))
                return;

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(LobbyScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Additive);

            if (!scene.IsValid() || !scene.isLoaded || Find(scene, LobbyMarker) != null)
            {
                if (opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
                return;
            }

            SceneManager.SetActiveScene(scene);
            try
            {
                MushLobbyController controller = FindComponent<MushLobbyController>(scene);
                GameObject panel = Find(scene, "MONEY BAG SHOP Panel");
                if (controller == null || panel == null)
                    throw new InvalidOperationException("The existing lobby shop could not be found.");

                ClearChildren(panel.transform);
                panel.transform.localPosition = new Vector3(0f, 1.47f, -0.72f);
                panel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                panel.transform.localScale = Vector3.one;

                Material background = GetMaterial("ShopPanelBack", new Color(0.055f, 0.035f, 0.022f), 0.18f);
                Material header = GetMaterial("ShopHeader", new Color(0.63f, 0.28f, 0.065f), 0.28f);
                Material tile = GetMaterial("ShopItemTile", new Color(0.16f, 0.095f, 0.052f), 0.20f);
                Material footer = GetMaterial("ShopFooterButton", new Color(0.42f, 0.17f, 0.045f), 0.22f);

                CreatePrimitive("Large Shop Back", PrimitiveType.Cube, panel.transform,
                    Vector3.zero, new Vector3(4.65f, 3.15f, 0.07f), Vector3.zero, background, false);
                CreatePrimitive("Large Shop Header", PrimitiveType.Cube, panel.transform,
                    new Vector3(0f, 1.34f, -0.05f), new Vector3(4.25f, 0.30f, 0.07f), Vector3.zero, header, false);
                CreateText("Shop Title", panel.transform, new Vector3(0f, 1.34f, -0.095f), 0.038f,
                    Color.white, "머쉬 모형 상점");
                TextMesh status = CreateText("Status", panel.transform, new Vector3(0f, 1.08f, -0.095f), 0.020f,
                    new Color(1f, 0.82f, 0.43f), "원하는 모형을 눌러 받으세요");

                ShopItemSpec[] specs =
                {
                    new ShopItemSpec("furniture_table", "작은 탁자", "Assets/Scenes/Mush_Furniture_SmallTable.fbx"),
                    new ShopItemSpec("furniture_chair", "포근한 의자", "Assets/Scenes/Mush_Furniture_CozyChair.fbx"),
                    new ShopItemSpec("furniture_dog_bed", "개 침대", "Assets/Scenes/Mush_Furniture_DogBed.fbx"),
                    new ShopItemSpec("sled_natural", "기본 썰매", "Assets/Scenes/Mush_Sled_Natural.fbx"),
                    new ShopItemSpec("sled_red", "빨간 썰매", "Assets/Scenes/Mush_Sled_Red.fbx"),
                    new ShopItemSpec("sled_blue", "파란 썰매", "Assets/Scenes/Mush_Sled_Blue.fbx"),
                    new ShopItemSpec("sled_black", "검은 썰매", "Assets/Scenes/Mush_Sled_Black.fbx"),
                    new ShopItemSpec("sled_santa", "산타 썰매", "Assets/Scenes/Mush_Sled_Santa.fbx"),
                    new ShopItemSpec("sled_lantern", "앞 등불", "Assets/Scenes/Mush_Sled_FrontLantern.fbx")
                };

                for (int index = 0; index < specs.Length; index++)
                {
                    int row = index / 3;
                    int column = index % 3;
                    Vector3 position = new Vector3(-1.43f + column * 1.43f, 0.67f - row * 0.74f, -0.075f);
                    CreateShopTile(panel.transform, position, tile, controller, specs[index]);
                }

                CreateFooterButton("개 목도리", panel.transform, new Vector3(-1.35f, -1.35f, -0.075f),
                    controller, MushLobbyAction.BuyScarf, footer);
                CreateFooterButton("나무 숲 지도", panel.transform, new Vector3(0f, -1.35f, -0.075f),
                    controller, MushLobbyAction.BuyForest, footer);
                CreateFooterButton("닫기", panel.transform, new Vector3(1.35f, -1.35f, -0.075f),
                    controller, MushLobbyAction.ClosePanel, header);

                controller.SetShopPanel(panel, status);
                EditorUtility.SetDirty(controller);
                panel.SetActive(false);

                GameObject marker = new GameObject(LobbyMarker) { hideFlags = HideFlags.HideInHierarchy };
                SceneManager.MoveGameObjectToScene(marker, scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Mush] Installed the large 3D model acquisition shop.");
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

        private static void CreateShopTile(
            Transform parent,
            Vector3 position,
            Material tileMaterial,
            MushLobbyController controller,
            ShopItemSpec spec)
        {
            GameObject tile = new GameObject(spec.label + " Shop Tile");
            tile.transform.SetParent(parent, false);
            tile.transform.localPosition = position;
            tile.transform.localRotation = Quaternion.identity;
            CreatePrimitive("Tile Back", PrimitiveType.Cube, tile.transform,
                Vector3.zero, new Vector3(1.22f, 0.62f, 0.08f), Vector3.zero, tileMaterial, false);
            BoxCollider collider = tile.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.22f, 0.62f, 0.08f);
            XRSimpleInteractable xr = tile.AddComponent<XRSimpleInteractable>();
            xr.selectMode = InteractableSelectMode.Single;

            Transform preview = CreatePreviewModel(tile.transform, spec.path);
            CreateText("Item Label", tile.transform, new Vector3(0f, -0.205f, -0.058f), 0.020f,
                Color.white, spec.label);
            TextMesh state = CreateText("Acquire State", tile.transform, new Vector3(0f, -0.265f, -0.059f), 0.014f,
                new Color(1f, 0.72f, 0.22f), "눌러서 받기");

            MushLobbyShopItem item = tile.AddComponent<MushLobbyShopItem>();
            item.Configure(controller, spec.id, spec.label, state, preview);
        }

        private static Transform CreatePreviewModel(Transform tile, string path)
        {
            GameObject preview = new GameObject("Rotating Model Preview");
            preview.transform.SetParent(tile, false);
            preview.transform.localPosition = new Vector3(0f, 0.065f, -0.13f);
            preview.transform.localRotation = Quaternion.Euler(8f, 30f, 0f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return preview.transform;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, tile.gameObject.scene);
            instance.name = Path.GetFileNameWithoutExtension(path) + " Preview";
            instance.transform.SetParent(preview.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            ApplyModelMaterials(instance);

            Bounds bounds = CalculateLocalBounds(preview.transform, instance);
            float widthScale = bounds.size.x > 0.0001f ? 0.78f / bounds.size.x : 1f;
            float heightScale = bounds.size.y > 0.0001f ? 0.34f / bounds.size.y : 1f;
            float scale = Mathf.Min(widthScale, heightScale);
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition = -bounds.center * scale;
            return preview.transform;
        }

        private static GameObject InstantiateNormalizedModel(
            string name,
            string path,
            Transform parent,
            float scale,
            Vector3 position)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = position;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one * scale;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return holder;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            instance.name = Path.GetFileNameWithoutExtension(path);
            instance.transform.SetParent(holder.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            Bounds bounds = CalculateLocalBounds(holder.transform, instance);
            instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            return holder;
        }

        private static Bounds CalculateLocalBounds(Transform relativeTo, GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Renderer renderer in renderers)
            {
                Bounds worldBounds = renderer.bounds;
                Vector3 min = worldBounds.min;
                Vector3 max = worldBounds.max;
                for (int x = 0; x <= 1; x++)
                for (int y = 0; y <= 1; y++)
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 world = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                    Vector3 local = relativeTo.InverseTransformPoint(world);
                    if (!initialized)
                    {
                        result = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }
            return result;
        }

        private static void ApplyModelMaterials(GameObject root)
        {
            if (root == null)
                return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    string source = materials[index] != null ? materials[index].name.ToLowerInvariant() : string.Empty;
                    materials[index] = MaterialFor(source);
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Material MaterialFor(string source)
        {
            if (source.Contains("lantern_glass"))
                return GetMaterial("LanternGlass", new Color(1f, 0.46f, 0.08f), 0.58f, new Color(1f, 0.24f, 0.02f) * 2.4f);
            if (source.Contains("lantern_frame"))
                return GetMaterial("LanternFrame", new Color(0.12f, 0.075f, 0.035f), 0.42f);
            if (source.Contains("santa_gold"))
                return GetMaterial("SantaGold", new Color(0.92f, 0.57f, 0.08f), 0.62f);
            if (source.Contains("santa_cream"))
                return GetMaterial("SantaCream", new Color(0.92f, 0.82f, 0.63f), 0.25f);
            if (source.Contains("santa_red"))
                return GetMaterial("SantaRed", new Color(0.72f, 0.025f, 0.035f), 0.28f);
            if (source.Contains("redfabric"))
                return GetMaterial("FurnitureRedFabric", new Color(0.62f, 0.045f, 0.055f), 0.18f);
            if (source.Contains("furniture_green"))
                return GetMaterial("FurnitureGreen", new Color(0.12f, 0.36f, 0.19f), 0.20f);
            if (source.Contains("furniture_cream"))
                return GetMaterial("FurnitureCream", new Color(0.82f, 0.68f, 0.46f), 0.22f);
            if (source.Contains("sled_red"))
                return GetMaterial("SledRed", new Color(0.72f, 0.035f, 0.04f), 0.30f);
            if (source.Contains("sled_blue"))
                return GetMaterial("SledBlue", new Color(0.045f, 0.24f, 0.67f), 0.34f);
            if (source.Contains("sled_black"))
                return GetMaterial("SledBlack", new Color(0.025f, 0.030f, 0.038f), 0.38f);
            if (source.Contains("darkmetal"))
                return GetMaterial("SledDarkMetal", new Color(0.075f, 0.085f, 0.095f), 0.58f);
            if (source.Contains("metal"))
                return GetMaterial("SledMetal", new Color(0.35f, 0.40f, 0.44f), 0.72f);
            if (source.Contains("woodlight"))
                return GetMaterial("SledWoodLight", new Color(0.62f, 0.32f, 0.105f), 0.24f);
            if (source.Contains("wood"))
                return GetMaterial("SledWood", new Color(0.34f, 0.15f, 0.055f), 0.22f);
            return GetMaterial("ModelFallback", new Color(0.48f, 0.30f, 0.16f), 0.22f);
        }

        private static void CreateFooterButton(
            string label,
            Transform parent,
            Vector3 position,
            MushLobbyController controller,
            MushLobbyAction action,
            Material material)
        {
            GameObject button = CreatePrimitive(label + " Button", PrimitiveType.Cube, parent,
                position, new Vector3(1.05f, 0.25f, 0.09f), Vector3.zero, material, true);
            XRSimpleInteractable xr = button.AddComponent<XRSimpleInteractable>();
            xr.selectMode = InteractableSelectMode.Single;
            MushLobbyInteractable interactable = button.AddComponent<MushLobbyInteractable>();
            interactable.Configure(controller, action, button.GetComponent<Renderer>());
            CreateText("Label", parent, position + new Vector3(0f, 0f, -0.055f), 0.025f, Color.white, label);
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Vector3 localEuler,
            Material material,
            bool keepCollider)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = Quaternion.Euler(localEuler);
            gameObject.transform.localScale = localScale;
            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            if (!keepCollider && gameObject.GetComponent<Collider>() is Collider collider)
                UnityEngine.Object.DestroyImmediate(collider);
            return gameObject;
        }

        private static TextMesh CreateText(
            string name,
            Transform parent,
            Vector3 localPosition,
            float characterSize,
            Color color,
            string text)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = localPosition;
            textObject.transform.localRotation = Quaternion.identity;
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = characterSize;
            textMesh.fontSize = 48;
            textMesh.color = color;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                textMesh.font = font;
                textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return textMesh;
        }

        private static Material GetMaterial(string name, Color color, float smoothness, Color? emission = null)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(index).gameObject);
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }

        private static GameObject Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = Find(root.transform, name);
                if (found != null)
                    return found.gameObject;
            }
            return null;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform child in root)
            {
                Transform found = Find(child, name);
                if (found != null)
                    return found;
            }
            return null;
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
    }
}
