using System;
using System.IO;
using Mush.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mush.Lobby.Editor
{
    /// <summary>
    /// Installs the code-generated low-poly dogs into the two preserved lobby
    /// dog roots. Their separated meshes are animated procedurally at runtime.
    /// </summary>
    [InitializeOnLoad]
    public static class MushLowPolyLobbyDogInstaller
    {
        private const string ScenePath = "Assets/Scenes/MushLobby.unity";
        private const string HuskyPath = "Assets/MushLobby/Dogs/Models/Mush_LowPoly_Husky.fbx";
        private const string MalamutePath = "Assets/MushLobby/Dogs/Models/Mush_LowPoly_Malamute.fbx";
        private const string RevisionMarker = "Mush Lobby Revision 6 - Procedural LowPoly Dogs";

        static MushLowPolyLobbyDogInstaller()
        {
            EditorApplication.delayCall += ApplyAutomatically;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += ApplyAutomatically;
            };
        }

        [MenuItem("Mush/Lobby/Install Procedural Low-Poly Dogs")]
        public static void ApplyFromMenu()
        {
            Apply();
        }

        private static void ApplyAutomatically()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ApplyAutomatically;
                return;
            }

            Apply();
        }

        private static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying ||
                !File.Exists(ScenePath))
                return;

            bool importerChanged = EnsureBakedAxisConversion(HuskyPath) |
                                   EnsureBakedAxisConversion(MalamutePath);
            if (importerChanged)
            {
                EditorApplication.delayCall += ApplyAutomatically;
                return;
            }

            GameObject huskyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HuskyPath);
            GameObject malamutePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MalamutePath);
            if (huskyPrefab == null || malamutePrefab == null)
            {
                EditorApplication.delayCall += ApplyAutomatically;
                return;
            }

            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            if (!scene.IsValid() || !scene.isLoaded)
                return;

            if (Find(scene, RevisionMarker) != null)
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
                return;
            }

            SceneManager.SetActiveScene(scene);
            try
            {
                Camera lobbyCamera = FindComponent<Camera>(scene);
                Transform leftHand = FindTransform(scene, "Left Controller") ??
                                     FindTransform(scene, "Lobby Left Hand");
                Transform rightHand = FindTransform(scene, "Right Controller") ??
                                      FindTransform(scene, "Lobby Right Hand");

                GameObject huskyRoot = Find(scene, "Mochi - Husky");
                GameObject malamuteRoot = Find(scene, "Bori - Malamute");
                if (huskyRoot == null || malamuteRoot == null)
                    throw new InvalidOperationException("The preserved Mochi/Bori lobby roots were not found.");

                MushLobbyDogRoamer husky = InstallDog(
                    scene, huskyRoot, huskyPrefab, false, lobbyCamera, leftHand, rightHand, -0.42f);
                MushLobbyDogRoamer malamute = InstallDog(
                    scene, malamuteRoot, malamutePrefab, true, lobbyCamera, leftHand, rightHand, 0.42f);

                MushLobbyController controller = FindComponent<MushLobbyController>(scene);
                if (controller != null)
                {
                    controller.SetDogs(new[] { husky, malamute });
                    EditorUtility.SetDirty(controller);
                }

                GameObject marker = new GameObject(RevisionMarker)
                {
                    hideFlags = HideFlags.HideInHierarchy
                };
                SceneManager.MoveGameObjectToScene(marker, scene);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[Mush] Installed Mochi and Bori with procedural roaming, pet reactions, and hearts.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool EnsureBakedAxisConversion(string assetPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null || importer.bakeAxisConversion)
                return false;

            importer.bakeAxisConversion = true;
            importer.SaveAndReimport();
            return true;
        }

        private static MushLobbyDogRoamer InstallDog(
            Scene scene,
            GameObject dogRoot,
            GameObject modelPrefab,
            bool malamute,
            Camera lobbyCamera,
            Transform leftHand,
            Transform rightHand,
            float callSideOffset)
        {
            RemovePreviousVisual(dogRoot.transform);

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, scene);
            visual.name = malamute ? "Dog Visual - LowPoly Malamute" : "Dog Visual - LowPoly Husky";
            visual.transform.SetParent(dogRoot.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * (malamute ? 0.40f : 0.39f);

            string prefix = malamute ? "Malamute_" : "Husky_";
            Transform head = FindContaining(visual.transform, prefix + "Head");
            Transform leftEye = FindContaining(visual.transform, prefix + "Eye_L");
            Transform rightEye = FindContaining(visual.transform, prefix + "Eye_R");
            Transform mouth = FindContaining(visual.transform, prefix + "Mouth");
            Transform tail = FindContaining(visual.transform, prefix + "Tail");

            if (head == null || leftEye == null || rightEye == null || mouth == null || tail == null)
                throw new InvalidOperationException("Low-poly dog parts are incomplete for " + dogRoot.name + ".");

            foreach (Collider rootCollider in dogRoot.GetComponents<Collider>())
                rootCollider.enabled = false;

            SphereCollider headCollider = head.GetComponent<SphereCollider>();
            if (headCollider == null)
                headCollider = head.gameObject.AddComponent<SphereCollider>();
            headCollider.center = Vector3.zero;
            headCollider.radius = malamute ? 0.53f : 0.49f;
            headCollider.isTrigger = false;

            MushLobbyDogRoamer roamer = dogRoot.GetComponent<MushLobbyDogRoamer>();
            if (roamer == null)
                roamer = dogRoot.AddComponent<MushLobbyDogRoamer>();
            roamer.Configure(visual.transform, tail,
                new Vector2(-3.25f, -5.20f), new Vector2(3.25f, 0.85f));
            roamer.ConfigureCharacter(null, lobbyCamera != null ? lobbyCamera.transform : null, callSideOffset);

            MushLobbyDogExpression expression = dogRoot.GetComponent<MushLobbyDogExpression>();
            if (expression == null)
                expression = dogRoot.AddComponent<MushLobbyDogExpression>();
            expression.Configure(roamer, head, leftEye, rightEye, mouth, lobbyCamera);

            MushLobbyDogInteraction interaction = dogRoot.GetComponent<MushLobbyDogInteraction>();
            if (interaction == null)
                interaction = dogRoot.AddComponent<MushLobbyDogInteraction>();
            interaction.Configure(roamer, head, leftHand, rightHand);

            EditorUtility.SetDirty(dogRoot);
            EditorUtility.SetDirty(roamer);
            EditorUtility.SetDirty(expression);
            EditorUtility.SetDirty(interaction);
            return roamer;
        }

        private static void RemovePreviousVisual(Transform dogRoot)
        {
            for (int index = dogRoot.childCount - 1; index >= 0; index--)
            {
                Transform child = dogRoot.GetChild(index);
                if (child.name.StartsWith("Dog Visual", StringComparison.OrdinalIgnoreCase))
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
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
            Transform transform = FindTransform(scene, name);
            return transform != null ? transform.gameObject : null;
        }

        private static Transform FindTransform(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == name)
                        return child;
                }
            }
            return null;
        }

        private static Transform FindContaining(Transform root, string fragment)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            }
            return null;
        }
    }
}
