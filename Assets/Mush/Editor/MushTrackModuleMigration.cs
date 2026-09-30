using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Bakes the Track_v2 road and roadside art onto the existing forest routes.</summary>
[InitializeOnLoad]
public static class MushTrackModuleMigration
{
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/Tree.unity",
        "Assets/Scenes/SharpCurve.unity",
    };
    private const string KaiPath = "Assets/Art/Prefabs/Dog/KAI_Model.prefab";
    private const string LumiPath = "Assets/Art/Prefabs/Dog/LUMI_Model.prefab";

    static MushTrackModuleMigration()
    {
        EditorApplication.delayCall += UpgradeScenes;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += UpgradeScenes;
        };
    }

    private static void UpgradeScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;
        if (EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += UpgradeScenes;
            return;
        }

        Scene previous = SceneManager.GetActiveScene();
        foreach (string path in ScenePaths)
        {
            Scene scene = EditorSceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty)
                continue;

            try
            {
                if (opened)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                MushTrackAuthoring authoring = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    authoring = root.GetComponentInChildren<MushTrackAuthoring>(true);
                    if (authoring != null) break;
                }

                Transform map = authoring != null ? authoring.ResolveMapRoot() : null;
                Transform generated = map != null ? map.Find(MushCurvedMapRuntime.GeneratedWorldRootName) : null;
                Transform road = generated != null ? generated.Find(MushCurvedMapRuntime.DeformedRoadRootName) : null;
                if (generated == null)
                    continue;

                bool changed = UpgradeDogModels(map);
                if (road == null || road.childCount == 0)
                {
                    MushTrackEditorWorldPreview.EnsureEditableMapReady(authoring, false);
                    changed = true;
                }
                if (changed)
                {
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"[Mush] Applied Track_v2 dogs and road modules to {scene.name}; kept its route.");
                }
            }
            finally
            {
                if (opened && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
        if (previous.IsValid() && previous.isLoaded)
            SceneManager.SetActiveScene(previous);
    }

    private static bool UpgradeDogModels(Transform map)
    {
        bool changed = false;
        foreach (MushRideDog dog in map.GetComponentsInChildren<MushRideDog>(true))
        {
            Transform visual = dog.Visual;
            if (visual == null)
                continue;

            string path = dog.UseMalamuteAccessories ? LumiPath : KaiPath;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;
            if (visual.childCount == 1 && visual.GetChild(0).name == prefab.name)
                continue;

            for (int index = visual.childCount - 1; index >= 0; index--)
                Object.DestroyImmediate(visual.GetChild(index).gameObject);

            visual.localRotation = Quaternion.identity;
            visual.localPosition = new Vector3(0f, -0.041688144f, -0.10464156f);
            visual.localScale = Vector3.one * 1.0422033f;
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, visual.gameObject.scene);
            model.transform.SetParent(visual, false);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            changed = true;
        }
        return changed;
    }
}
