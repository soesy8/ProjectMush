using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mush.Lobby.Editor
{
    [InitializeOnLoad]
    public static class MushLobbyDogAnimationRepair
    {
        private const string ScenePath = "Assets/Scenes/MushLobby.unity";
        private const string HuskyPath = "Assets/Scenes/Mush_Husky_LowPoly_V6.fbx";
        private const string MalamutePath = "Assets/Scenes/Mush_Malamute_LowPoly_Simple_V1.fbx";
        private const string MarkerName = "Mush Lobby Revision 5 - Dog Avatar Animation Repair";

        static MushLobbyDogAnimationRepair()
        {
            EditorApplication.delayCall += Apply;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += Apply;
            };
        }

        [MenuItem("Mush/Lobby/Repair Dog Animation And Eyes")]
        public static void Apply()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
                !File.Exists(ScenePath) ||
                AssetDatabase.LoadAssetAtPath<GameObject>(HuskyPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(MalamutePath) == null)
                return;

            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            if (!scene.IsValid() || !scene.isLoaded || Find(scene, MarkerName) != null)
            {
                if (opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
                return;
            }

            SceneManager.SetActiveScene(scene);
            try
            {
                RepairDog(scene, "Mochi - Husky", HuskyPath, "Assets/MushLobby/Dogs/Animator/MushHusky.controller");
                RepairDog(scene, "Bori - Malamute", MalamutePath, "Assets/MushLobby/Dogs/Animator/MushMalamute.controller");
                SoftenEyeMaterial("Assets/MushLobby/Dogs/Materials/HuskySclera.mat", new Color(0.085f, 0.095f, 0.105f));
                SoftenEyeMaterial("Assets/MushLobby/Dogs/Materials/HuskyIris.mat", new Color(0.18f, 0.39f, 0.56f));
                SoftenEyeMaterial("Assets/MushLobby/Dogs/Materials/MalamuteSclera.mat", new Color(0.075f, 0.068f, 0.060f));
                SoftenEyeMaterial("Assets/MushLobby/Dogs/Materials/MalamuteIris.mat", new Color(0.25f, 0.12f, 0.045f));

                GameObject marker = new GameObject(MarkerName) { hideFlags = HideFlags.HideInHierarchy };
                SceneManager.MoveGameObjectToScene(marker, scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[Mush] Repaired dog Avatars, forced locomotion initialization, and softened the eye colors.");
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

        private static void RepairDog(Scene scene, string dogName, string modelPath, string controllerPath)
        {
            GameObject dog = Find(scene, dogName);
            if (dog == null)
                throw new InvalidOperationException("Could not find " + dogName + ".");

            Animator animator = dog.GetComponentInChildren<Animator>(true);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (animator == null || avatar == null || controller == null)
                throw new InvalidOperationException("Animator assets are incomplete for " + dogName + ".");

            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(animator);
        }

        private static void SoftenEyeMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                return;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.42f);
            EditorUtility.SetDirty(material);
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
    }
}
