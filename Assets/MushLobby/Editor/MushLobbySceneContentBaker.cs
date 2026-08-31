using System.Collections.Generic;
using Mush.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mush.Lobby.Editor
{
    /// <summary>
    /// Materializes lobby interaction visuals once into MushLobby.unity. Runtime
    /// scripts still own input and animation, but no longer need to invent the
    /// visible room contents when play mode starts.
    /// </summary>
    [InitializeOnLoad]
    public static class MushLobbySceneContentBaker
    {
        private const string ScenePath = "Assets/Scenes/MushLobby.unity";
        private const string GeneratedFolder = "Assets/MushLobby/Generated";
        private const string AssetPath = GeneratedFolder + "/MushLobby_RuntimeContentAssets.asset";
        private const string MaterialFolder = "Assets/MushLobby/Materials";
        private const string MarkerName = "Mush Lobby Scene Content Baked";
        private static bool queued;

        static MushLobbySceneContentBaker()
        {
            EditorApplication.delayCall += ApplyAutomatically;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += ApplyAutomatically;
            };
        }

        [MenuItem("Mush/Lobby/Bake Runtime Visual Content Into Scene")]
        public static void BakeFromMenu()
        {
            Apply(true);
        }

        public static void BakeFromCommandLine()
        {
            Apply(true);
        }

        private static void ApplyAutomatically()
        {
            if (queued || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                Apply(false);
            };
        }

        private static void Apply(bool force)
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            MushLobbyDogRoamer[] dogsForStatus = FindComponents<MushLobbyDogRoamer>(scene);
            bool missingDogVisual = false;
            foreach (MushLobbyDogRoamer dog in dogsForStatus)
                missingDogVisual |= dog == null || !dog.HasSceneAuthoredVisual;
            bool missingCareVisualMaterial = HasMissingCareVisualMaterial(scene);

            if (!force && FindInScene(scene, MarkerName) != null && !missingDogVisual && !missingCareVisualMaterial)
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
                return;
            }

            SceneManager.SetActiveScene(scene);
            try
            {
                Camera camera = FindComponent<Camera>(scene);
                MushLobbyDogRoamer[] dogs = FindComponents<MushLobbyDogRoamer>(scene);
                MushLobbyController controller = FindComponent<MushLobbyController>(scene);
                Transform lobbyRoot = controller != null ? controller.transform.parent : null;
                if (lobbyRoot == null)
                    lobbyRoot = FindInScene(scene, "Mush Lobby Prototype")?.transform;

                if (lobbyRoot == null || controller == null)
                    return;

                foreach (MushLobbyDogRoamer dog in dogs)
                    dog.BakeVisualIntoScene();

                MushLobbyFireplaceVfx.Install(lobbyRoot);
                MushLobbyFireplaceRestSpot.Install(lobbyRoot);
                MushLobbyFetchBall.Install(camera, dogs, lobbyRoot);
                MushLobbyFeedingStation.Install(camera, dogs, lobbyRoot);
                MushLobbyStationNavigator.Install(camera, controller, lobbyRoot);
                RepairCareVisualMaterials(scene);

                GameObject marker = new(MarkerName) { hideFlags = HideFlags.HideInHierarchy };
                SceneManager.MoveGameObjectToScene(marker, scene);
                PersistSceneResources(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[Mush] Baked lobby visual/runtime-created content into MushLobby.unity.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void PersistSceneResources(Scene scene)
        {
            EnsureFolder(GeneratedFolder);
            AssetDatabase.DeleteAsset(AssetPath);
            MushBakedMapAssetContainer container = ScriptableObject.CreateInstance<MushBakedMapAssetContainer>();
            container.name = "MushLobby Runtime Content Assets";
            AssetDatabase.CreateAsset(container, AssetPath);

            HashSet<Object> resources = new();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    AddMaterialAndTexture(resources, material);

                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.sharedMaterial != null && !EditorUtility.IsPersistent(collider.sharedMaterial))
                        resources.Add(collider.sharedMaterial);
                }
            }

            int index = 0;
            foreach (Object resource in resources)
            {
                if (resource == null || EditorUtility.IsPersistent(resource))
                    continue;
                resource.name = $"{index++:D3}_{resource.name}";
                AssetDatabase.AddObjectToAsset(resource, container);
            }
        }

        private static void AddMaterialAndTexture(HashSet<Object> resources, Material material)
        {
            if (material == null || EditorUtility.IsPersistent(material))
                return;
            resources.Add(material);
            if (material.mainTexture != null && !EditorUtility.IsPersistent(material.mainTexture))
                resources.Add(material.mainTexture);
        }

        private static bool HasMissingCareVisualMaterial(Scene scene)
        {
            string[] roots =
            {
                "Mush Dog Feeding Station",
                "Dog Fetch Ball Stand",
                "Dog Fetch Ball",
            };

            foreach (string rootName in roots)
            {
                GameObject root = FindInScene(scene, rootName);
                if (root == null)
                    continue;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (NeedsMaterialRepair(renderer))
                        return true;
                }
            }
            return false;
        }

        private static void RepairCareVisualMaterials(Scene scene)
        {
            GameObject feeding = FindInScene(scene, "Mush Dog Feeding Station");
            if (feeding != null)
            {
                Material bowl = GetOrCreateCareMaterial("MushCare_Bowl", new Color(0.30f, 0.13f, 0.055f), 0.18f);
                Material bowlRim = GetOrCreateCareMaterial("MushCare_BowlRim", new Color(0.78f, 0.55f, 0.25f), 0.18f);
                Material canister = GetOrCreateCareMaterial("MushCare_Canister", new Color(0.48f, 0.25f, 0.085f), 0.18f);
                Material canisterLid = GetOrCreateCareMaterial("MushCare_CanisterLid", new Color(0.88f, 0.60f, 0.20f), 0.18f);
                Material canisterStand = GetOrCreateCareMaterial("MushCare_CanisterStand", new Color(0.20f, 0.10f, 0.045f), 0.18f);
                Material foodParticles = GetOrCreateCareMaterial(
                    "MushCare_FoodParticles",
                    new Color(0.56f, 0.30f, 0.08f),
                    0f,
                    "Universal Render Pipeline/Particles/Unlit");

                foreach (Renderer renderer in feeding.GetComponentsInChildren<Renderer>(true))
                {
                    if (!NeedsMaterialRepair(renderer))
                        continue;

                    string objectName = renderer.gameObject.name;
                    Material replacement;
                    if (renderer is ParticleSystemRenderer)
                        replacement = foodParticles;
                    else if (objectName.StartsWith("Bowl Rim", System.StringComparison.Ordinal))
                        replacement = bowlRim;
                    else if (objectName == "Canister Body")
                        replacement = canister;
                    else if (objectName == "Canister Stand")
                        replacement = canisterStand;
                    else if (objectName == "Canister Lid" || objectName == "Canister Pour Lip" ||
                             objectName.StartsWith("Canister Food Mark", System.StringComparison.Ordinal))
                        replacement = canisterLid;
                    else
                        replacement = bowl;

                    renderer.sharedMaterial = replacement;
                    EditorUtility.SetDirty(renderer);
                }
            }

            GameObject fetchStand = FindInScene(scene, "Dog Fetch Ball Stand");
            if (fetchStand != null)
            {
                Material fetchWood = GetOrCreateCareMaterial("MushCare_FetchWood", new Color(0.34f, 0.15f, 0.055f), 0.18f);
                Material fetchRim = GetOrCreateCareMaterial("MushCare_FetchRim", new Color(0.56f, 0.29f, 0.095f), 0.20f);
                foreach (Renderer renderer in fetchStand.GetComponentsInChildren<Renderer>(true))
                {
                    if (!NeedsMaterialRepair(renderer))
                        continue;
                    renderer.sharedMaterial = renderer.gameObject.name == "Stand Base" ? fetchWood : fetchRim;
                    EditorUtility.SetDirty(renderer);
                }
            }

            GameObject fetchBall = FindInScene(scene, "Dog Fetch Ball");
            if (fetchBall != null)
            {
                Material ball = GetOrCreateCareMaterial("MushCare_FetchBall", new Color(1f, 0.30f, 0.045f), 0.26f);
                foreach (Renderer renderer in fetchBall.GetComponentsInChildren<Renderer>(true))
                {
                    if (!NeedsMaterialRepair(renderer))
                        continue;
                    renderer.sharedMaterial = ball;
                    EditorUtility.SetDirty(renderer);
                }
            }
        }

        private static bool NeedsMaterialRepair(Renderer renderer)
        {
            if (renderer == null)
                return false;
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                return true;
            foreach (Material material in materials)
            {
                if (material == null || material.shader == null || !material.shader.isSupported ||
                    material.shader.name == "Hidden/InternalErrorShader")
                    return true;
            }
            return false;
        }

        private static Material GetOrCreateCareMaterial(
            string assetName,
            Color color,
            float smoothness,
            string shaderName = "Universal Render Pipeline/Lit")
        {
            string path = $"{MaterialFolder}/{assetName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException($"Required Unity 6 URP shader was not found: {shaderName}");

            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader == null || !material.shader.isSupported || material.shader.name != shaderName)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder("Assets/MushLobby"))
                AssetDatabase.CreateFolder("Assets", "MushLobby");
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder("Assets/MushLobby", "Generated");
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == name)
                        return child.gameObject;
                }
            }
            return null;
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

        private static T[] FindComponents<T>(Scene scene) where T : Component
        {
            List<T> found = new();
            foreach (GameObject root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
            return found.ToArray();
        }
    }
}
