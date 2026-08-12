using System;
using System.IO;
using System.Linq;
using Mush.Lobby;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby.Editor
{
    [InitializeOnLoad]
    public static class MushLobbyDogModelInstaller
    {
        private const string ScenePath = "Assets/Scenes/MushLobby.unity";
        private const string HuskyPath = "Assets/Scenes/Mush_Husky_LowPoly_V6.fbx";
        private const string MalamutePath = "Assets/Scenes/Mush_Malamute_LowPoly_Simple_V1.fbx";
        private const string DogRoot = "Assets/MushLobby/Dogs";
        private const string MaterialRoot = DogRoot + "/Materials";
        private const string AnimatorRoot = DogRoot + "/Animator";
        private const string RevisionMarker = "Mush Lobby Revision 4 - FBX Husky Malamute Team";

        static MushLobbyDogModelInstaller()
        {
            EditorApplication.delayCall += ApplyRevision;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += ApplyRevision;
            };
        }

        [MenuItem("Mush/Lobby/Install FBX Husky And Malamute")]
        public static void ApplyRevision()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
                !File.Exists(ScenePath) ||
                AssetDatabase.LoadAssetAtPath<GameObject>(HuskyPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(MalamutePath) == null)
                return;

            EnsureFolder(MaterialRoot);
            EnsureFolder(AnimatorRoot);
            EnsureAnimationImport(HuskyPath);
            EnsureAnimationImport(MalamutePath);

            AnimatorController huskyController = EnsureAnimatorController(
                AnimatorRoot + "/MushHusky.controller", HuskyPath);
            AnimatorController malamuteController = EnsureAnimatorController(
                AnimatorRoot + "/MushMalamute.controller", MalamutePath);
            if (huskyController == null || malamuteController == null)
            {
                Debug.LogWarning("[Mush] Dog animation clips have not finished importing yet.");
                EditorApplication.delayCall += ApplyRevision;
                return;
            }

            Scene previousScene = SceneManager.GetActiveScene();
            Scene targetScene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForRevision = !targetScene.IsValid() || !targetScene.isLoaded;
            if (openedForRevision)
                targetScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            if (!targetScene.IsValid() || !targetScene.isLoaded || FindInScene(targetScene, RevisionMarker) != null)
            {
                if (openedForRevision && targetScene.IsValid() && targetScene.isLoaded)
                    EditorSceneManager.CloseScene(targetScene, true);
                return;
            }

            SceneManager.SetActiveScene(targetScene);
            try
            {
                Camera lobbyCamera = FindComponentInScene<Camera>(targetScene);
                Transform leftHand = FindNamedTransform(targetScene, "Left Controller", "Lobby Left Hand");
                Transform rightHand = FindNamedTransform(targetScene, "Right Controller", "Lobby Right Hand");

                GameObject husky = FindInScene(targetScene, "Mochi - Gray Husky");
                GameObject malamute = FindInScene(targetScene, "Bori - Brown Husky");
                if (husky == null || malamute == null)
                    throw new InvalidOperationException("The two existing lobby dog roots could not be found.");

                DogInstallResult huskyResult = InstallDog(
                    husky, HuskyPath, huskyController, false, lobbyCamera, leftHand, rightHand, -0.42f);
                DogInstallResult malamuteResult = InstallDog(
                    malamute, MalamutePath, malamuteController, true, lobbyCamera, leftHand, rightHand, 0.42f);

                MushLobbyController lobbyController = FindComponentInScene<MushLobbyController>(targetScene);
                if (lobbyController != null)
                {
                    lobbyController.SetDogs(new[] { huskyResult.roamer, malamuteResult.roamer });
                    lobbyController.SetDogScarves(new[] { huskyResult.redBandana, malamuteResult.redBandana });
                    EditorUtility.SetDirty(lobbyController);
                }

                GameObject marker = new GameObject(RevisionMarker) { hideFlags = HideFlags.HideInHierarchy };
                SceneManager.MoveGameObjectToScene(marker, targetScene);
                EditorSceneManager.MarkSceneDirty(targetScene);
                EditorSceneManager.SaveScene(targetScene);
                Debug.Log("[Mush] Installed the animated FBX husky, malamute, and eight socket accessories in MushLobby.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
                if (openedForRevision && targetScene.IsValid() && targetScene.isLoaded)
                    EditorSceneManager.CloseScene(targetScene, true);
            }
        }

        private struct DogInstallResult
        {
            public MushLobbyDogRoamer roamer;
            public GameObject redBandana;
        }

        private static DogInstallResult InstallDog(
            GameObject dogRoot,
            string modelPath,
            RuntimeAnimatorController controller,
            bool malamute,
            Camera lobbyCamera,
            Transform leftHand,
            Transform rightHand,
            float sideOffset)
        {
            Transform oldVisual = FindDescendant(dogRoot.transform, "Dog Visual");
            if (oldVisual != null)
                UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);

            dogRoot.name = malamute ? "Bori - Malamute" : "Mochi - Husky";

            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, dogRoot.scene);
            visual.name = malamute ? "Dog Visual - Malamute FBX" : "Dog Visual - Husky FBX";
            visual.transform.SetParent(dogRoot.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * (malamute ? 0.43f : 0.45f);
            ApplyDogMaterials(visual, malamute);

            Animator animator = visual.GetComponent<Animator>();
            if (animator == null)
                animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = LoadAvatar(modelPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Transform head = FindDescendant(visual.transform, "Head");
            Transform headSocket = FindDescendant(visual.transform, "Socket.Head");
            Transform neckSocket = FindDescendant(visual.transform, "Socket.Neck");
            Transform tail = FindDescendant(visual.transform, "Tail.03") ??
                             FindDescendantContaining(visual.transform, "Tail.001");

            string prefix = malamute ? "Mush_Malamute_" : "Mush_Husky_";
            GameObject fedora = InstallAccessory(prefix + "Fedora.fbx", headSocket, "Fedora", malamute);
            GameObject santaHat = InstallAccessory(prefix + "SantaHat.fbx", headSocket, "Santa Hat", malamute);
            GameObject redBandana = InstallAccessory(prefix + "RedBandana.fbx", neckSocket, "Red Bandana", malamute);
            GameObject purpleScarf = InstallAccessory(prefix + "PurpleScarf.fbx", neckSocket, "Purple Scarf", malamute);
            SetActive(fedora, false);
            SetActive(santaHat, false);
            SetActive(redBandana, false);
            SetActive(purpleScarf, false);

            MushLobbyDogRoamer roamer = dogRoot.GetComponent<MushLobbyDogRoamer>();
            if (roamer == null)
                roamer = dogRoot.AddComponent<MushLobbyDogRoamer>();
            roamer.Configure(visual.transform, tail, new Vector2(-2.15f, -1.65f), new Vector2(2.05f, 0.90f));
            roamer.ConfigureCharacter(animator, lobbyCamera != null ? lobbyCamera.transform : null, sideOffset);

            CapsuleCollider collider = dogRoot.GetComponent<CapsuleCollider>();
            if (collider == null)
                collider = dogRoot.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.48f, 0f);
            collider.radius = malamute ? 0.34f : 0.31f;
            collider.height = malamute ? 0.98f : 0.91f;
            collider.direction = 1;

            XRSimpleInteractable xrInteractable = dogRoot.GetComponent<XRSimpleInteractable>();
            if (xrInteractable == null)
                xrInteractable = dogRoot.AddComponent<XRSimpleInteractable>();

            MushLobbyDogInteraction interaction = dogRoot.GetComponent<MushLobbyDogInteraction>();
            if (interaction == null)
                interaction = dogRoot.AddComponent<MushLobbyDogInteraction>();
            interaction.Configure(roamer, head != null ? head : headSocket, leftHand, rightHand);

            EditorUtility.SetDirty(dogRoot);
            EditorUtility.SetDirty(roamer);
            EditorUtility.SetDirty(interaction);
            return new DogInstallResult { roamer = roamer, redBandana = redBandana };
        }

        private static GameObject InstallAccessory(
            string fileName,
            Transform socket,
            string displayName,
            bool malamute)
        {
            if (socket == null)
            {
                Debug.LogWarning("[Mush] Missing dog socket for " + displayName + ".");
                return null;
            }

            string path = "Assets/Scenes/" + fileName;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning("[Mush] Missing accessory FBX: " + path);
                return null;
            }

            GameObject accessory = (GameObject)PrefabUtility.InstantiatePrefab(prefab, socket.gameObject.scene);
            accessory.name = displayName;
            accessory.transform.SetParent(socket, false);
            accessory.transform.localPosition = Vector3.zero;
            accessory.transform.localRotation = Quaternion.identity;
            accessory.transform.localScale = Vector3.one;
            ApplyAccessoryMaterials(accessory, malamute);
            return accessory;
        }

        private static void EnsureAnimationImport(string modelPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
                return;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            bool changed = false;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
                changed = clips != null && clips.Length > 0;
            }

            if (clips == null)
                return;

            foreach (ModelImporterClipAnimation clip in clips)
            {
                bool loop = clip.name.EndsWith("Idle", StringComparison.OrdinalIgnoreCase) ||
                            clip.name.EndsWith("Walk", StringComparison.OrdinalIgnoreCase) ||
                            clip.name.EndsWith("Run", StringComparison.OrdinalIgnoreCase) ||
                            clip.name.EndsWith("TailWag", StringComparison.OrdinalIgnoreCase);
                if (clip.loopTime != loop || clip.loopPose != loop)
                {
                    clip.loopTime = loop;
                    clip.loopPose = loop;
                    changed = true;
                }
            }

            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        private static AnimatorController EnsureAnimatorController(string controllerPath, string modelPath)
        {
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (existing != null)
                return existing;

            AnimationClip idle = FindClip(modelPath, "Idle");
            AnimationClip walk = FindClip(modelPath, "Walk");
            AnimationClip run = FindClip(modelPath, "Run");
            if (idle == null || walk == null || run == null)
                return null;

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            foreach (string trigger in new[] { "Eat", "Sit", "LieDown", "TailWag", "HeadTilt", "Pet", "Happy" })
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState childState in stateMachine.states.ToArray())
                stateMachine.RemoveState(childState.state);

            BlendTree locomotionTree = new BlendTree
            {
                name = "Dog Locomotion",
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(locomotionTree, controller);
            locomotionTree.AddChild(idle, 0f);
            locomotionTree.AddChild(walk, 0.48f);
            locomotionTree.AddChild(run, 1f);

            AnimatorState locomotion = stateMachine.AddState("Locomotion");
            locomotion.motion = locomotionTree;
            stateMachine.defaultState = locomotion;

            AddReaction(stateMachine, locomotion, modelPath, "Eat", "Eat");
            AddReaction(stateMachine, locomotion, modelPath, "Sit", "Sit");
            AddReaction(stateMachine, locomotion, modelPath, "LieDown", "LieDown");
            AddReaction(stateMachine, locomotion, modelPath, "Tail Wag", "TailWag");
            AddReaction(stateMachine, locomotion, modelPath, "Head Tilt", "HeadTilt");
            AddReaction(stateMachine, locomotion, modelPath, "Pet Eyes Close", "Pet", "PetEyesClose");
            AddReaction(stateMachine, locomotion, modelPath, "Happy Pet", "Happy", "HappyPet");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void AddReaction(
            AnimatorStateMachine stateMachine,
            AnimatorState locomotion,
            string modelPath,
            string stateName,
            string trigger,
            string clipSuffix = null)
        {
            AnimationClip clip = FindClip(modelPath, clipSuffix ?? trigger);
            if (clip == null)
                return;

            AnimatorState reaction = stateMachine.AddState(stateName);
            reaction.motion = clip;
            AnimatorStateTransition enter = stateMachine.AddAnyStateTransition(reaction);
            enter.hasExitTime = false;
            enter.duration = 0.12f;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

            AnimatorStateTransition exit = reaction.AddTransition(locomotion);
            exit.hasExitTime = true;
            exit.exitTime = 0.92f;
            exit.duration = 0.14f;
        }

        private static AnimationClip FindClip(string modelPath, string suffix)
        {
            return AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase) &&
                                        clip.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static Avatar LoadAvatar(string modelPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
        }

        private static void ApplyDogMaterials(GameObject root, bool malamute)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    string sourceName = materials[index] != null ? materials[index].name.ToLowerInvariant() : string.Empty;
                    materials[index] = DogMaterialFor(sourceName, malamute);
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Material DogMaterialFor(string sourceName, bool malamute)
        {
            string breed = malamute ? "Malamute" : "Husky";
            if (sourceName.Contains("blueiris"))
                return GetMaterial(breed + "Iris", malamute ? new Color(0.31f, 0.13f, 0.045f) : new Color(0.10f, 0.43f, 0.82f), 0.72f);
            if (sourceName.Contains("sclera"))
                return GetMaterial(breed + "Sclera", new Color(0.91f, 0.93f, 0.91f), 0.64f);
            if (sourceName.Contains("pupil") || sourceName.Contains("nose"))
                return GetMaterial(breed + "Black", new Color(0.012f, 0.014f, 0.018f), 0.58f);
            if (sourceName.Contains("eyerim") || sourceName.Contains("mouthinside"))
                return GetMaterial(breed + "DarkDetail", new Color(0.035f, 0.027f, 0.027f), 0.28f);
            if (sourceName.Contains("tongue"))
                return GetMaterial(breed + "Tongue", new Color(0.78f, 0.27f, 0.34f), 0.38f);
            if (sourceName.Contains("darkgrey"))
                return GetMaterial(breed + "DarkCoat", malamute ? new Color(0.105f, 0.12f, 0.13f) : new Color(0.15f, 0.18f, 0.22f), 0.25f);
            if (sourceName.Contains("white"))
                return GetMaterial(breed + "LightCoat", malamute ? new Color(0.82f, 0.77f, 0.67f) : new Color(0.88f, 0.90f, 0.91f), 0.25f);
            return GetMaterial(breed + "MainCoat", malamute ? new Color(0.25f, 0.20f, 0.16f) : new Color(0.32f, 0.37f, 0.42f), 0.25f);
        }

        private static void ApplyAccessoryMaterials(GameObject root, bool malamute)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    string name = materials[index] != null ? materials[index].name.ToLowerInvariant() : string.Empty;
                    if (name.Contains("darkband"))
                        materials[index] = GetMaterial("FedoraDarkBand", new Color(0.075f, 0.045f, 0.025f), 0.30f);
                    else if (name.Contains("fedora"))
                        materials[index] = GetMaterial("FedoraBrown", malamute ? new Color(0.40f, 0.20f, 0.075f) : new Color(0.48f, 0.25f, 0.09f), 0.28f);
                    else if (name.Contains("whitefur"))
                        materials[index] = GetMaterial("SantaWhiteFur", new Color(0.94f, 0.93f, 0.88f), 0.20f);
                    else if (name.Contains("santa"))
                        materials[index] = GetMaterial("SantaRed", new Color(0.72f, 0.025f, 0.025f), 0.24f);
                    else if (name.Contains("darkred"))
                        materials[index] = GetMaterial("BandanaDarkRed", new Color(0.34f, 0.012f, 0.018f), 0.22f);
                    else if (name.Contains("bandana"))
                        materials[index] = GetMaterial("BandanaRed", new Color(0.78f, 0.035f, 0.045f), 0.26f);
                    else if (name.Contains("knit"))
                        materials[index] = GetMaterial("ScarfPurpleKnit", new Color(0.28f, 0.08f, 0.40f), 0.18f);
                    else
                        materials[index] = GetMaterial("ScarfPurple", new Color(0.53f, 0.18f, 0.70f), 0.22f);
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Material GetMaterial(string name, Color color, float smoothness)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindDescendant(root.transform, name);
                if (found != null)
                    return found.gameObject;
            }
            return null;
        }

        private static Transform FindNamedTransform(Scene scene, params string[] names)
        {
            foreach (string name in names)
            {
                GameObject found = FindInScene(scene, name);
                if (found != null)
                    return found.transform;
            }
            return null;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Transform FindDescendantContaining(Transform root, string fragment)
        {
            if (root.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendantContaining(child, fragment);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
                target.SetActive(active);
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
