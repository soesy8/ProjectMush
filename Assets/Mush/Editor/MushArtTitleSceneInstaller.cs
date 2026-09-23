using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class MushArtTitleSceneInstaller
{
    private const string TitleScenePath = "Assets/Art/Scenes/Title.unity";
    private const string OldTitleScenePath = "Assets/Mush/Scenes/MushTitle.unity";
    private const string OptionPrefabPath = "Assets/UI_Panel_Sample/Prefab/OptionUI.prefab";

    static MushArtTitleSceneInstaller()
    {
        EditorApplication.delayCall += InstallIfNeeded;
    }

    [MenuItem("Mush/Title/Connect Art Title Scene")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene targetScene = SceneManager.GetSceneByPath(TitleScenePath);
        bool openedTemporarily = !targetScene.IsValid() || !targetScene.isLoaded;
        if (openedTemporarily)
            targetScene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);

        Canvas canvas = FindInScene<Canvas>(targetScene, item => item.name == "Canvas");
        Camera camera = FindInScene<Camera>(targetScene, _ => true);
        if (canvas == null || camera == null)
            throw new InvalidOperationException("Art Title scene must contain its authored Canvas and Camera.");

        camera.gameObject.tag = "MainCamera";

        GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
            raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();

        Transform titlePanel = FindChild(canvas.transform, "TitleMenuUI");
        if (titlePanel == null)
            throw new InvalidOperationException("The authored TitleMenuUI was not found under the Art Title Canvas.");

        Transform optionPanel = FindChild(canvas.transform, "OptionUI");
        if (optionPanel == null)
        {
            GameObject optionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OptionPrefabPath);
            if (optionPrefab == null)
                throw new InvalidOperationException("OptionUI prefab could not be loaded.");

            GameObject optionInstance = (GameObject)PrefabUtility.InstantiatePrefab(optionPrefab, targetScene);
            optionInstance.transform.SetParent(canvas.transform, false);
            optionPanel = optionInstance.transform;
            optionInstance.SetActive(false);
        }

        MushSceneUI sceneUi = canvas.GetComponent<MushSceneUI>();
        if (sceneUi == null)
            sceneUi = canvas.gameObject.AddComponent<MushSceneUI>();

        SerializedObject sceneUiData = new(sceneUi);
        sceneUiData.FindProperty("ride").objectReferenceValue = null;
        sceneUiData.FindProperty("titlePanel").objectReferenceValue = titlePanel.gameObject;
        sceneUiData.FindProperty("pausePanel").objectReferenceValue = null;
        sceneUiData.FindProperty("optionPanel").objectReferenceValue = optionPanel.gameObject;
        sceneUiData.FindProperty("message").objectReferenceValue = null;
        sceneUiData.FindProperty("master").objectReferenceValue = FindSlider(optionPanel, "01_MasterVolume");
        sceneUiData.FindProperty("music").objectReferenceValue = FindSlider(optionPanel, "02_BGM");
        sceneUiData.FindProperty("effects").objectReferenceValue = FindSlider(optionPanel, "03_SE");
        sceneUiData.FindProperty("masterCount").objectReferenceValue = FindText(optionPanel, "01_MasterVolume", "Count");
        sceneUiData.FindProperty("musicCount").objectReferenceValue = FindText(optionPanel, "02_BGM", "Count");
        sceneUiData.FindProperty("effectsCount").objectReferenceValue = FindText(optionPanel, "03_SE", "Count");
        sceneUiData.ApplyModifiedPropertiesWithoutUndo();

        MushCanvasQuestInput questInput = canvas.GetComponent<MushCanvasQuestInput>();
        if (questInput == null)
            questInput = canvas.gameObject.AddComponent<MushCanvasQuestInput>();
        SerializedObject questInputData = new(questInput);
        questInputData.FindProperty("canvas").objectReferenceValue = canvas;
        questInputData.FindProperty("raycaster").objectReferenceValue = raycaster;
        questInputData.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(camera);
        EditorUtility.SetDirty(canvas);
        EditorUtility.SetDirty(canvas.gameObject);
        EditorSceneManager.MarkSceneDirty(targetScene);
        EditorSceneManager.SaveScene(targetScene);
        ReplaceBuildTitleScene();

        if (openedTemporarily)
            EditorSceneManager.CloseScene(targetScene, true);

        Debug.Log("[Mush] Connected the authored UI in Assets/Art/Scenes/Title.unity and made it the build title scene.");
    }

    private static void InstallIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            !System.IO.File.Exists(TitleScenePath))
            return;

        bool buildAlreadyUsesArtTitle = EditorBuildSettings.scenes.Length > 0 &&
                                        EditorBuildSettings.scenes[0].path == TitleScenePath &&
                                        EditorBuildSettings.scenes.All(scene => scene.path != OldTitleScenePath);
        if (buildAlreadyUsesArtTitle)
            return;

        Install();
    }

    private static void ReplaceBuildTitleScene()
    {
        EditorBuildSettingsScene[] remaining = EditorBuildSettings.scenes
            .Where(scene => scene.path != OldTitleScenePath && scene.path != TitleScenePath)
            .ToArray();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(TitleScenePath, true) }
            .Concat(remaining)
            .ToArray();
    }

    private static T FindInScene<T>(Scene scene, Func<T, bool> predicate) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true))
                if (predicate(component))
                    return component;
        return null;
    }

    private static Transform FindChild(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName)
                return child;
        return null;
    }

    private static Slider FindSlider(Transform optionPanel, string groupName)
    {
        Transform group = FindChild(optionPanel, groupName);
        return group != null ? group.GetComponentInChildren<Slider>(true) : null;
    }

    private static TMP_Text FindText(Transform optionPanel, string groupName, string textName)
    {
        Transform group = FindChild(optionPanel, groupName);
        if (group == null)
            return null;
        foreach (TMP_Text text in group.GetComponentsInChildren<TMP_Text>(true))
            if (text.name == textName)
                return text;
        return null;
    }
}
