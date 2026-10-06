using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mush.Lobby;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Authors static UI assets and checks the ArtOpt scene bindings without entering Play Mode.</summary>
public static class ArtOptUiValidation
{
    private static readonly string[] Scenes =
    {
        "Title", "PM_Lobby", "MushStore", "MushHousing", "Track_v2", "Tree", "SharpCurve"
    };

    [MenuItem("Mush/ArtOpt/Author and Validate UI")]
    public static void Run()
    {
        try
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run this authoring command in Edit Mode.");
            AuthorImageTemplates();
            AuthorResultPanels();
            foreach (string name in Scenes)
            {
                Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity", OpenSceneMode.Single);
                ValidateScene(scene);
                Debug.Log("[ArtOpt UI] PASS: " + name);
                if (name == "Title")
                {
                    CaptureUi(scene, "Title");
                    GameObject options = (GameObject)new SerializedObject(Components<MushSceneUI>(scene).Single())
                        .FindProperty("optionPanel").objectReferenceValue;
                    options.SetActive(true);
                    CaptureUi(scene, "TitleOptions");
                }
                if (name == "PM_Lobby")
                {
                    CaptureUi(scene, "Lobby");
                    GameObject map = (GameObject)new SerializedObject(Components<MushLobbyController>(scene).Single())
                        .FindProperty("mapPanel").objectReferenceValue;
                    map.SetActive(true);
                    CaptureUi(scene, "MapMenu");
                }
                if (name == "Track_v2") CaptureUi(scene, "Ride");
            }
            File.WriteAllText("Logs/ArtOptUiValidation-result.txt", "PASS: compiled; static UI templates authored; all 7 build scenes validated.\n");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ArtOptUiValidation-result.txt", "FAIL: " + exception);
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    private static void AuthorImageTemplates()
    {
        Color[] palette =
        {
            new(0.055f, 0.075f, 0.105f, 0.93f), new(0.16f, 0.23f, 0.31f, 0.96f),
            new(0.82f, 0.40f, 0.08f, 0.98f), new(0.16f, 0.48f, 0.30f, 0.98f),
            new(0.065f, 0.09f, 0.10f, 1f), new(0.12f, 0.18f, 0.19f, 1f),
            new(0.22f, 0.31f, 0.32f, 1f), new(0.80f, 0.48f, 0.19f, 1f),
            new(0.94f, 0.91f, 0.82f, 1f)
        };
        const string folder = "Assets/Mush/Resources/ArtOptUI/Images";
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        foreach (Color color in palette)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color);
            string file = folder + "/" + key + ".prefab";
            if (File.Exists(file)) continue;
            GameObject template = new("UI Image " + key, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            template.layer = 5;
            try
            {
                RectTransform rect = template.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(100f, 100f);
                template.GetComponent<UnityEngine.UI.Image>().color = color;
                if (PrefabUtility.SaveAsPrefabAsset(template, file) == null) throw new InvalidOperationException("Could not save " + file);
            }
            finally { UnityEngine.Object.DestroyImmediate(template); }
        }
    }

    private static void AuthorResultPanels()
    {
        Scene track = EditorSceneManager.OpenScene("Assets/Scenes/Track_v2.unity", OpenSceneMode.Single);
        MushMapRideBootstrap ride = Components<MushMapRideBootstrap>(track).Single();
        GameObject sourcePanel = new SerializedObject(ride).FindProperty("resultPanel").objectReferenceValue as GameObject;
        Require(sourcePanel != null, "Track_v2 must contain the imported Delivery Result Panel.");
        const string prefabPath = "Assets/Mush/Runtime/UI/ArtOptResultPanel.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) prefab = PrefabUtility.SaveAsPrefabAsset(sourcePanel, prefabPath);
        Require(prefab != null, "Result UI prefab could not be authored.");
        foreach (string name in new[] { "Tree", "SharpCurve" })
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity", OpenSceneMode.Single);
            MushMapRideBootstrap owner = Components<MushMapRideBootstrap>(scene).Single();
            SerializedObject data = new(owner);
            SerializedProperty panel = data.FindProperty("resultPanel");
            if (panel.objectReferenceValue != null) continue;
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "Delivery Result Panel";
            instance.transform.SetParent(owner.transform, false);
            instance.SetActive(false);
            panel.objectReferenceValue = instance;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
    }

    private static void ValidateScene(Scene scene)
    {
        foreach (Transform item in Components<Transform>(scene))
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) == 0,
                scene.name + ": missing script on " + item.name);
        if (scene.name is "MushStore" or "MushHousing") return;

        Require(Components<EventSystem>(scene).Count(e => e.isActiveAndEnabled) == 1,
            scene.name + ": expected exactly one active EventSystem.");
        foreach (Canvas canvas in Components<Canvas>(scene).Where(c => c.transform.parent == null))
            Require(canvas.GetComponent<GraphicRaycaster>() != null || canvas.GetComponentsInChildren<Button>(true).Length == 0,
                scene.name + ": interactive Canvas has no GraphicRaycaster.");

        if (scene.name == "Title")
        {
            MushSceneUI ui = Components<MushSceneUI>(scene).Single();
            RequireReferences(ui, "titlePanel", "optionPanel", "master", "music", "effects", "masterCount", "musicCount", "effectsCount");
            string[] controls = { "TitleUI_Start", "TitleUI_Continue", "TitleUI_Option", "TitleUI_Quit" };
            foreach (string control in controls)
                Require(Components<Button>(scene).Any(b => b.name == control), "Missing title button: " + control);
        }
        else if (scene.name == "PM_Lobby")
        {
            MushLobbyController owner = Components<MushLobbyController>(scene).Single();
            RequireReferences(owner, "lobbyCamera", "mapPanel", "shopPanel", "housingPanel");
            MushLobbyStaminaHud hud = Components<MushLobbyStaminaHud>(scene).Single();
            RequireReferences(hud, "staminaText", "conditionText", "secondStaminaText", "secondConditionText");
            RequireArtworkUnchanged(hud.transform.root.gameObject, () => Invoke(hud, "Refresh"));
            MushLobbyMapPanel menu = Components<MushLobbyMapPanel>(scene).Single();
            RequireReferences(menu, "controller", "canvas", "message", "closeButton");
            RequireArtworkUnchanged(menu.gameObject, menu.RefreshRecords);
            SerializedObject map = new(menu);
            foreach (string property in new[] { "records", "labels", "courseButtons" })
            {
                SerializedProperty array = map.FindProperty(property);
                Require(array.arraySize == 3, "Map menu must contain 3 " + property);
                for (int i = 0; i < 3; i++) Require(array.GetArrayElementAtIndex(i).objectReferenceValue != null, "Map menu has an unbound " + property);
            }
            foreach (MushLobbyInteractable target in Components<MushLobbyInteractable>(scene))
            {
                SerializedObject item = new(target);
                int action = item.FindProperty("action").enumValueIndex;
                if (action <= 3) RequireReferences(target, "hoverPanelCanvas");
            }
        }
        else
        {
            MushMapRideBootstrap ride = Components<MushMapRideBootstrap>(scene).Single();
            RequireReferences(ride, "resultPanel");
            MushRideHud hud = Components<MushRideHud>(scene).Single(h => h.enabled);
            RequireReferences(hud, "ride", "timer", "trackRoot", "progressRoot");
            RequireArtworkUnchanged(hud.gameObject, () => Invoke(hud, "LateUpdate"));
            TMP_Text timer = (TMP_Text)new SerializedObject(hud).FindProperty("timer").objectReferenceValue;
            Require(!string.IsNullOrWhiteSpace(timer.text), scene.name + ": timer value was not updated.");
            MushSceneUI ui = Components<MushSceneUI>(scene).Single();
            RequireReferences(ui, "ride", "pausePanel", "optionPanel", "master", "music", "effects");
        }
    }

    private static T[] Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static void Invoke(object owner, string method) =>
        owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);

    private static void RequireArtworkUnchanged(GameObject root, Action updateValues)
    {
        UnityEngine.UI.Image[] images = root.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        string[] before = images.Select(ImageAppearance).ToArray();
        updateValues();
        Require(before.SequenceEqual(images.Select(ImageAppearance)), root.name + ": updating values changed the authored UI artwork.");
    }

    private static string ImageAppearance(UnityEngine.UI.Image image) =>
        image.sprite?.GetInstanceID() + "|" + image.color + "|" + image.type + "|" + image.fillAmount + "|" +
        image.rectTransform.anchoredPosition + "|" + image.rectTransform.sizeDelta + "|" + image.rectTransform.localScale;

    private static void RequireReferences(UnityEngine.Object owner, params string[] names)
    {
        SerializedObject data = new(owner);
        foreach (string name in names)
            Require(data.FindProperty(name)?.objectReferenceValue != null, owner.name + ": unbound " + name);
    }

    private static void CaptureUi(Scene scene, string label)
    {
        Camera camera = Components<Camera>(scene).FirstOrDefault(c => c.CompareTag("MainCamera"));
        if (camera == null) return;
        const int width = 1920, height = 1080;
        RenderTexture target = new(width, height, 24);
        Texture2D pixels = new(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        var canvases = Components<Canvas>(scene).Where(c => c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        try
        {
            foreach (Canvas canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = Mathf.Max(camera.nearClipPlane + 0.25f, 1f);
            }
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            pixels.Apply();
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/ArtOptUI-" + label + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            foreach (Canvas canvas in canvases) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
