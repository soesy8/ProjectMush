using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Persists editor-generated resources before the scene references are saved.</summary>
[InitializeOnLoad]
public static class MushSceneAuthoringMigration
{
    private static bool saving;

    static MushSceneAuthoringMigration()
    {
        EditorSceneManager.sceneSaving += BeforeSceneSave;
    }

    private static void BeforeSceneSave(Scene scene, string path)
    {
        if (saving || EditorApplication.isPlaying) return;
        MushTrackEditorWorldPreview.FlushPending();
        Persist(scene, path);
    }

    public static void Persist(Scene scene, string scenePath = null)
    {
        if (saving || !scene.IsValid() || !scene.isLoaded) return;
        saving = true;
        try
        {
            var resources = new HashSet<Object>();
            bool hasTrack = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                hasTrack |= root.GetComponentInChildren<MushTrackAuthoring>(true) != null;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true)) Add(resources, filter.sharedMesh);
                foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true)) Add(resources, collider.sharedMesh);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in renderer.sharedMaterials) Add(resources, material);
            }
            if (!hasTrack || resources.Count == 0) return;
            const string folder = "Assets/Mush/GeneratedMaps";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Mush", "GeneratedMaps");
            string name = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(scenePath) ? scene.path : scenePath);
            if (string.IsNullOrEmpty(name)) name = "UnsavedTrack";
            string path = $"{folder}/{name}_AuthoringAssets.asset";
            MushBakedMapAssetContainer container = AssetDatabase.LoadAssetAtPath<MushBakedMapAssetContainer>(path);
            foreach (Object resource in resources)
            {
                if (AssetDatabase.Contains(resource))
                {
                    if (resource is Mesh && AssetDatabase.GetAssetPath(resource).StartsWith(folder + "/", StringComparison.Ordinal))
                        AssetDatabase.SaveAssetIfDirty(resource);
                    continue;
                }
                if (container == null)
                {
                    container = ScriptableObject.CreateInstance<MushBakedMapAssetContainer>();
                    AssetDatabase.CreateAsset(container, path);
                }
                resource.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(resource, container);
                EditorUtility.SetDirty(resource);
            }
            if (container != null) AssetDatabase.SaveAssetIfDirty(container);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (MushCurvedMapRuntime runtime in root.GetComponentsInChildren<MushCurvedMapRuntime>(true))
                    runtime.ReleaseBakedResourceOwnership();
        }
        finally { saving = false; }
    }

    private static void Add(HashSet<Object> resources, Object resource)
    {
        if (resource != null) resources.Add(resource);
    }

    [MenuItem("Mush/Maps/Save Editable Scene Content")]
    public static void MigrateOpenScene()
    {
        Migrate(SceneManager.GetActiveScene());
    }

    public static void RunBatch()
    {
        foreach (string name in new[] { "snow", "Tree", "SharpCurve" })
        {
            Scene scene = EditorSceneManager.OpenScene($"Assets/Mush/Scenes/{name}.unity", OpenSceneMode.Single);
            Migrate(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scene.path);
            Debug.Log($"[Mush Authoring] Migrated and saved {scene.path}");
        }
        AssetDatabase.SaveAssets();
    }

    private static void Migrate(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MushMapRideBootstrap bootstrap in root.GetComponentsInChildren<MushMapRideBootstrap>(true))
            {
                bootstrap.MigrateSceneDogs();
                EditorUtility.SetDirty(bootstrap);
            }
            foreach (MushTrackAuthoring authoring in root.GetComponentsInChildren<MushTrackAuthoring>(true))
            {
                Transform mapRoot = authoring.ResolveMapRoot();
                MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
                if (runtime == null)
                {
                    foreach (GameObject candidate in scene.GetRootGameObjects())
                    {
                        MushCurvedMapRuntime found = candidate.GetComponentInChildren<MushCurvedMapRuntime>(true);
                        if (found == null) continue;
                        if (runtime != null) throw new InvalidOperationException("여러 맵 중 편집 대상이 명확하지 않습니다.");
                        runtime = found;
                    }
                    if (runtime == null) runtime = mapRoot.gameObject.AddComponent<MushCurvedMapRuntime>();
                    authoring.SetMapRoot(runtime.transform);
                    EditorUtility.SetDirty(authoring);
                }
                runtime.RebuildSceneCourseGeometry();
                EditorUtility.SetDirty(runtime);
            }
        }
        Persist(scene);
        EditorSceneManager.MarkSceneDirty(scene);
    }
}
