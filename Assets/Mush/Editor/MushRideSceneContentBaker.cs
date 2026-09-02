using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Materializes the playable sled and dog team into each gameplay scene.
/// The runtime bootstrap keeps input and animation responsibilities, while
/// the visible hierarchy remains ordinary, editable scene content.
/// </summary>
public static class MushRideSceneContentBaker
{
    private static readonly string[] GameplayScenePaths =
    {
        "Assets/Scenes/snow.unity",
        "Assets/Scenes/Tree.unity",
        "Assets/Scenes/SharpCurve.unity",
    };

    public static void BakeFromCommandLine()
    {
        BakeAll(false);
    }

    private static void BakeAll(bool restorePreviousScene)
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene previousScene = SceneManager.GetActiveScene();
        int bakedSceneCount = 0;

        try
        {
            for (int index = 0; index < GameplayScenePaths.Length; index++)
            {
                string scenePath = GameplayScenePaths[index];
                Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                SceneManager.SetActiveScene(scene);
                MushMapRideBootstrap bootstrap = FindBootstrap(scene);
                MushTrackAuthoring authoring = FindAuthoring(scene);
                if (bootstrap == null || authoring == null)
                {
                    if (opened && scene != previousScene)
                        EditorSceneManager.CloseScene(scene, true);
                    continue;
                }

                Transform mapRoot = authoring.ResolveMapRoot();
                if (mapRoot == null || mapRoot.Find(MushCurvedMapRuntime.RideTeamRootName) != null)
                {
                    if (opened && scene != previousScene)
                        EditorSceneManager.CloseScene(scene, true);
                    continue;
                }

                bootstrap.BakeRideTeamIntoScene();
                if (mapRoot.Find(MushCurvedMapRuntime.RideTeamRootName) == null)
                    throw new MissingReferenceException(
                        $"Ride team was not created for {scenePath}.");

                EditorUtility.SetDirty(bootstrap);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                bakedSceneCount++;
                if (opened && scene != previousScene)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
        finally
        {
            if (restorePreviousScene && previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            AssetDatabase.SaveAssets();
        }

        if (bakedSceneCount > 0)
            Debug.Log($"[Mush] Saved sled and dog teams in {bakedSceneCount} gameplay scene(s).");
    }

    private static MushMapRideBootstrap FindBootstrap(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            MushMapRideBootstrap found = root.GetComponentInChildren<MushMapRideBootstrap>(true);
            if (found != null)
                return found;
        }
        return null;
    }

    private static MushTrackAuthoring FindAuthoring(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            MushTrackAuthoring found = root.GetComponentInChildren<MushTrackAuthoring>(true);
            if (found != null)
                return found;
        }
        return null;
    }
}
