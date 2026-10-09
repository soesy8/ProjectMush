using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mush.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Mush.Testing.TrackTest.Editor
{
    public static class TrackTestSetup
    {
        public const string Root = "Assets/Test/TrackTest";
        public const string SourceScene = "Assets/Scenes/Track_v2.unity";
        public const string TestScene = TrackTestAvoidance.ScenePath;
        private const string GeneratedAsset = "Assets/Mush/GeneratedMaps/Track_v2_AuthoringAssets.asset";

        [MenuItem("Mush/TrackTest/Open copied avoidance test")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            EnsureCleanScenes();
            EditorSceneManager.OpenScene(TestScene, OpenSceneMode.Single);
        }

        // One-time setup. Never saves an original asset or changes Build Settings.
        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            EnsureCleanScenes();
            if (File.Exists(TestScene)) throw new InvalidOperationException("Test scene already exists; refusing to overwrite it.");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Data");

            var copied = new Dictionary<string, string>();
            foreach (string source in AssetDatabase.GetDependencies(SourceScene, false).Where(p => p.EndsWith(".prefab")))
            {
                string destination = Root + "/Prefabs/" + Path.GetFileName(source);
                if (copied.Values.Contains(destination)) throw new InvalidOperationException("Duplicate prefab file name: " + source);
                Copy(source, destination);
                copied.Add(source, destination);
            }
            Copy(GeneratedAsset, Root + "/Data/Track_v2_AuthoringAssets.asset");
            copied.Add(GeneratedAsset, Root + "/Data/Track_v2_AuthoringAssets.asset");

            foreach (string name in new[] { "Tree01", "Tree02", "Rock01", "Rock02", "Rock03" })
                AddObstacle(Root + "/Prefabs/" + name + ".prefab", name.StartsWith("Tree"));

            Copy(SourceScene, TestScene);
            Scene scene = EditorSceneManager.OpenScene(TestScene, OpenSceneMode.Single);
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var prefabGroups = transforms.Select(t => t.gameObject)
                .Where(PrefabUtility.IsOutermostPrefabInstanceRoot)
                .GroupBy(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot).ToArray();
            int replaced = 0;
            foreach (var group in prefabGroups)
            {
                if (!copied.TryGetValue(group.Key, out string destination)) continue;
                GameObject[] instances = group.ToArray();
                PrefabUtility.ReplacePrefabAssetOfPrefabInstances(instances,
                    AssetDatabase.LoadAssetAtPath<GameObject>(destination), InteractionMode.AutomatedAction);
                replaced += instances.Length;
            }
            RemapSceneReferences(scene, copied);

            var oldFollow = Components<Mush.Testing.TrackDogLedSledFollowTest>(scene).Single();
            var oldMouse = Components<Mush.Testing.TrackMouseLookTest>(scene).Single();
            var follower = Replace<Mush.Testing.TrackDogLedSledFollowTest, TrackDogLedSledFollowTest>(oldFollow);
            Replace<Mush.Testing.TrackMouseLookTest, TrackMouseLookTest>(oldMouse);
            var ride = Components<MushMapRideBootstrap>(scene).Single();
            var controller = Components<MushSledKeyboardController>(scene).Single();
            var course = Components<MushCurvedMapRuntime>(scene).Single();

            // Start/periodic/pause autosave belongs to the real game, not this copy.
            foreach (var ui in Components<MushSceneUI>(scene)) ui.enabled = false;
            var rideSettings = new SerializedObject(ride);
            rideSettings.FindProperty("baseStaminaDrain").floatValue = 0f;
            rideSettings.FindProperty("deliveryTimeLimitSeconds").floatValue = 86400f;
            rideSettings.ApplyModifiedPropertiesWithoutUndo();

            GameObject demo = CreateDemonstration(scene, course, controller);
            var avoidance = controller.gameObject.AddComponent<TrackTestAvoidance>();
            SetReferences(avoidance, ("ride", ride), ("controller", controller), ("course", course),
                ("demonstrationObstacles", demo));
            var session = controller.gameObject.AddComponent<TrackTestSession>();
            SetReferences(session, ("ride", ride), ("controller", controller), ("follower", follower));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, TestScene)) throw new IOException("Could not save the copied scene.");
            Selection.activeGameObject = controller.gameObject;
            return $"Created {TestScene}; copied {copied.Count - 1} prefabs and generated mesh data; replaced {replaced} scene prefab instances.";
        }

        public static string Validate()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != TestScene) throw new InvalidOperationException("Open the copied test scene first.");
            var obstacles = Components<TrackTestObstacle>(scene).ToArray();
            var failures = new List<string>();
            if (Components<TrackTestAvoidance>(scene).Count() != 1) failures.Add("Avoidance controller count");
            if (Components<TrackDogLedSledFollowTest>(scene).Count() != 1) failures.Add("Copied follower count");
            if (Components<TrackMouseLookTest>(scene).Count() != 1) failures.Add("Copied mouse-look count");
            if (Components<Mush.Testing.TrackDogLedSledFollowTest>(scene).Any()) failures.Add("Original follower still attached");
            if (Components<Mush.Testing.TrackMouseLookTest>(scene).Any()) failures.Add("Original mouse-look still attached");
            if (Components<MushSceneUI>(scene).Any(ui => ui.enabled)) failures.Add("Game autosave UI still enabled");
            foreach (var obstacle in obstacles)
            {
                if (!obstacle.Body.isTrigger || obstacle.gameObject.layer != 2 || obstacle.Radius <= 0f)
                    failures.Add("Invalid obstacle: " + obstacle.name);
                string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(obstacle);
                if (!prefab.StartsWith(Root + "/")) failures.Add("Original obstacle prefab: " + obstacle.name);
            }
            foreach (var component in Components<Component>(scene))
                if (component == null) failures.Add("Missing script");
            if (obstacles.Length < 1156) failures.Add("Missing copied obstacles");
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures.Take(20)));
            return $"PASS: {obstacles.Length} query-only obstacle instances, copied follow/look scripts, test controls, autosave UI disabled.";
        }

        private static void EnsureCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("An open scene has unsaved changes; no scene was saved or closed.");
        }

        private static void EnsureFolder(string path)
        {
            if (!path.StartsWith(Root + "/")) throw new InvalidOperationException("Write outside test folder refused.");
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void Copy(string source, string destination)
        {
            if (!destination.StartsWith(Root + "/") || File.Exists(destination))
                throw new InvalidOperationException("Unsafe/occupied destination: " + destination);
            if (!AssetDatabase.CopyAsset(source, destination)) throw new IOException("Copy failed: " + source);
        }

        private static void AddObstacle(string path, bool tree)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Bounds bounds = default;
                bool initialized = false;
                foreach (var filter in contents.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    Bounds mesh = filter.sharedMesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 vertex = mesh.center + Vector3.Scale(mesh.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        Vector3 point = contents.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                        if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                if (!initialized) throw new InvalidOperationException("No obstacle mesh: " + path);
                float radius = Mathf.Max(0.15f, Mathf.Max(bounds.extents.x, bounds.extents.z) * (tree ? 0.18f : 0.9f));
                float height = tree ? Mathf.Clamp(bounds.size.y, 1.8f, 3.5f) : Mathf.Max(bounds.size.y, radius * 2f);
                Vector3 center = new Vector3(bounds.center.x, bounds.min.y + height * 0.5f, bounds.center.z);
                var obstacle = contents.AddComponent<TrackTestObstacle>();
                obstacle.Configure(center, radius, height);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static IEnumerable<T> Components<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));

        private static TNew Replace<TOld, TNew>(TOld old) where TOld : MonoBehaviour where TNew : MonoBehaviour
        {
            TNew replacement = old.gameObject.AddComponent<TNew>();
            foreach (FieldInfo field in typeof(TOld).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.IsInitOnly || (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)) continue;
                FieldInfo target = typeof(TNew).GetField(field.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (target != null && target.FieldType == field.FieldType) target.SetValue(replacement, field.GetValue(old));
            }
            replacement.enabled = old.enabled;
            Object.DestroyImmediate(old);
            return replacement;
        }

        private static void SetReferences(Object target, params (string name, Object value)[] fields)
        {
            var serialized = new SerializedObject(target);
            foreach (var field in fields) serialized.FindProperty(field.name).objectReferenceValue = field.value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RemapSceneReferences(Scene scene, Dictionary<string, string> copies)
        {
            var replacements = new Dictionary<Object, Object>();
            foreach (var pair in copies)
            {
                var targets = new Dictionary<long, Object>();
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(pair.Value))
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long id)) targets[id] = asset;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(pair.Key))
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long id) && targets.TryGetValue(id, out Object target))
                        replacements[asset] = target;
            }
            foreach (var component in Components<Component>(scene))
            {
                if (component == null) continue;
                var serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                bool changed = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null) continue;
                    if (!replacements.TryGetValue(property.objectReferenceValue, out Object target)) continue;
                    property.objectReferenceValue = target;
                    changed = true;
                }
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static GameObject CreateDemonstration(Scene scene, MushCurvedMapRuntime course, MushSledKeyboardController controller)
        {
            var group = new GameObject("TrackTest Demo Obstacles (F9 toggle)");
            SceneManager.MoveGameObjectToScene(group, scene);
            course.TryGetRoutePose(0f, out _, out _, out _);
            float[] distances = { 24f, 58f, 92f };
            float[] sides = { 0f, 1.1f, -1.1f };
            string[] names = { "Tree01", "Rock01", "Tree02" };
            for (int i = 0; i < distances.Length; i++)
            {
                float progress = distances[i] / Mathf.Max(1f, course.LengthMeters);
                if (!course.TryGetRoutePose(progress, out Vector3 position, out _, out Vector3 forward))
                    throw new InvalidOperationException("Cannot sample copied route.");
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                position += right * sides[i];
                if (i == 0)
                {
                    var dogs = controller.GetComponentsInChildren<MushRideDog>(true);
                    Vector3 leader = dogs.Length > 0
                        ? dogs.Aggregate(Vector3.zero, (sum, dog) => sum + dog.transform.position) / dogs.Length
                        : controller.transform.position;
                    forward = controller.transform.forward;
                    position = leader + forward * distances[i];
                }
                if (course.TryGetCourseSurface(position, out Vector3 surface, out _, out _, out _)) position.y = surface.y;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + names[i] + ".prefab");
                var prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                prop.name = "Demo " + (i + 1) + " " + names[i];
                prop.transform.SetParent(group.transform, true);
                prop.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up)));
                prop.transform.localScale = Vector3.one * (names[i].StartsWith("Tree") ? 1.5f : 1f);
                if (i == 0)
                {
                    Vector3 offset = position - prop.GetComponent<TrackTestObstacle>().Center;
                    offset.y = 0f;
                    prop.transform.position += offset;
                }
            }
            return group;
        }
    }
}
