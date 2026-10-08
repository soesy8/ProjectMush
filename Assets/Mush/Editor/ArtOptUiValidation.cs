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

    [MenuItem("Mush/ArtOpt/Validate Stamina Fixes")]
    public static void ValidateStaminaFixes()
    {
        Require(!Application.isPlaying, "Run stamina validation in Edit Mode.");
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/PM_Lobby.unity", OpenSceneMode.Single);
        ValidateLobbyStamina(Components<MushLobbyStaminaHud>(scene).Single());
        ValidateSceneStartStamina();
        Debug.Log("[Mush Stamina] PASS: both portraits, 30/70 boundaries and saved scene entry stamina.");
    }

    [MenuItem("Mush/ArtOpt/Validate Track Stamina")]
    public static void ValidateTrackStamina()
    {
        Require(!Application.isPlaying, "Run track stamina validation in Edit Mode.");
        Scene scene = SceneManager.GetActiveScene();
        Require(scene.name == "Track_v2", "Open Track_v2 before running track stamina validation.");
        ValidateRideHud(Components<MushRideHud>(scene).Single(h => h.enabled));
        Debug.Log("[Mush Track Stamina] PASS: individual bars, both portraits, 30/70 boundaries and authored layout.");
    }

    [MenuItem("Mush/ArtOpt/Validate Scene Start Stamina")]
    public static void ValidateSceneStartStamina()
    {
        Require(!Application.isPlaying, "Run scene start stamina validation in Edit Mode.");
        FieldInfo current = typeof(MushGameSave).GetField("current", BindingFlags.NonPublic | BindingFlags.Static);
        object original = current.GetValue(null);
        try
        {
            MushGameSave.Data sample = new() { gold = 1234 };
            sample.dogs[0].stamina = 83.25f;
            sample.dogs[0].condition = MushDogCondition.Good;
            sample.dogs[1].stamina = 47.5f;
            current.SetValue(null, sample);
            MushGameSave.CaptureSceneStartStamina();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                MushGameSave.ConsumeStamina(90f);
                // Exercise the save payload round trip without touching the user's save files.
                sample = JsonUtility.FromJson<MushGameSave.Data>(JsonUtility.ToJson(MushGameSave.Current));
                current.SetValue(null, sample);
                Require(MushGameSave.RestoreSceneStartStamina(), "A serialized scene start snapshot must restore.");
                Require(MushGameSave.GetDogStamina(0) == 83.25f && MushGameSave.GetDogStamina(1) == 47.5f &&
                        sample.stamina == 65.375f && sample.gold == 1234,
                    "Retries must restore each dog's exact entry stamina and preserve gold.");
                Require(MushGameSave.GetDogCondition(0) == MushDogCondition.Good &&
                        MushGameSave.GetDogCondition(1) == MushDogCondition.Normal,
                    "Restoring entry stamina must also clear the exhaustion acquired during the ride.");
            }

            // Entering the next lobby/track replaces the snapshot with that scene's starting state.
            MushGameSave.ConsumeStamina(12.5f);
            MushGameSave.CaptureSceneStartStamina();
            MushGameSave.ConsumeStamina(10f);
            Require(MushGameSave.RestoreSceneStartStamina() && MushGameSave.GetDogStamina(0) == 70.75f &&
                    MushGameSave.GetDogStamina(1) == 35f,
                "The next scene must capture its own entry stamina instead of reusing the previous scene.");

            // Feeding changes live values, but cannot change a previously captured entry snapshot.
            sample.dogs[0].stamina = 100f;
            sample.dogs[1].stamina = 100f;
            Require(MushGameSave.RestoreSceneStartStamina() && MushGameSave.GetDogStamina(0) == 70.75f &&
                    MushGameSave.GetDogStamina(1) == 35f,
                "Live stamina changes must not mutate the scene entry snapshot.");

            sample.sceneStartDogs = null;
            Require(!MushGameSave.RestoreSceneStartStamina() && MushGameSave.GetDogStamina(0) == 70.75f &&
                    MushGameSave.GetDogStamina(1) == 35f,
                "Legacy saves without a snapshot must retain their saved stamina.");
            MushGameSave.CaptureSceneStartStamina();
            sample.sceneStartDogs[1].stamina = float.NaN;
            sample.dogs[0].stamina = 15f;
            Require(!MushGameSave.RestoreSceneStartStamina() && MushGameSave.GetDogStamina(0) == 15f,
                "An invalid snapshot must not partially overwrite live stamina.");

            sample = new MushGameSave.Data();
            current.SetValue(null, sample);
            MushGameSave.CaptureSceneStartStamina();
            MushGameSave.ConsumeStamina(30f);
            Require(MushGameSave.RestoreSceneStartStamina() && MushGameSave.GetDogStamina(0) == 100f &&
                    MushGameSave.GetDogStamina(1) == 100f,
                "A new game's default entry stamina must remain 100 for both dogs.");
            Debug.Log("[Mush Stamina] PASS: scene entry snapshots, repeated retry, serialization, legacy saves and new game defaults.");
        }
        finally
        {
            current.SetValue(null, original);
        }
    }

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
            string[] controls = { "TitleUI_Start", "TitleUI_Continue", "TitleUI_Credit", "TitleUI_Option", "TitleUI_Quit" };
            foreach (string control in controls)
                Require(Components<Button>(scene).Any(b => b.name == control), "Missing title button: " + control);
            MushTitleCredits credits = Components<MushTitleCredits>(scene).Single();
            RequireReferences(credits, "creditButton", "viewport", "content");
            TMP_Text creditText = (TMP_Text)new SerializedObject(credits).FindProperty("content").objectReferenceValue;
            Require(!string.IsNullOrWhiteSpace(creditText.text), "Title credits content must not be empty.");
        }
        else if (scene.name == "PM_Lobby")
        {
            MushLobbyController owner = Components<MushLobbyController>(scene).Single();
            RequireReferences(owner, "lobbyCamera", "mapPanel", "shopPanel", "housingPanel");
            MushLobbyStaminaHud hud = Components<MushLobbyStaminaHud>(scene).Single();
            RequireReferences(hud, "staminaFill", "staminaText", "conditionText", "secondStaminaFill", "secondStaminaText", "secondConditionText");
            ValidateLobbyStamina(hud);
            MushLobbyMapPanel menu = Components<MushLobbyMapPanel>(scene).Single();
            RequireReferences(menu, "controller", "canvas", "message", "closeButton", "startButton");
            SerializedObject map = new(menu);
            UnityEngine.UI.Image[] mapFills = Enumerable.Range(0, 2).Select(i =>
                (UnityEngine.UI.Image)map.FindProperty("dogStaminaFills").GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            Require(mapFills.All(fill => fill != null), "Map menu dog stamina gauges must be bound.");
            RequireArtworkUnchanged(menu.gameObject, menu.RefreshRecords, mapFills);
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
            RequireReferences(hud, "ride", "timer", "trackRoot", "progressRoot", "progress", "progressIcon",
                "staminaFill", "staminaText");
            if (!new SerializedObject(hud).FindProperty("useTeamStamina").boolValue)
                RequireReferences(hud, "secondStaminaFill", "secondStaminaText");
            ValidateRideHud(hud);
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

    private static void ValidateRideHud(MushRideHud hud)
    {
        SerializedObject data = new(hud);
        bool unified = data.FindProperty("useTeamStamina").boolValue;
        UnityEngine.UI.Image first = (UnityEngine.UI.Image)data.FindProperty("staminaFill").objectReferenceValue;
        UnityEngine.UI.Image second = (UnityEngine.UI.Image)data.FindProperty("secondStaminaFill").objectReferenceValue;
        UnityEngine.UI.Image progress = (UnityEngine.UI.Image)data.FindProperty("progress").objectReferenceValue;
        RectTransform marker = (RectTransform)data.FindProperty("progressIcon").objectReferenceValue;
        TMP_Text firstText = (TMP_Text)data.FindProperty("staminaText").objectReferenceValue;
        TMP_Text secondText = (TMP_Text)data.FindProperty("secondStaminaText").objectReferenceValue;
        UnityEngine.UI.Image firstPortrait = (UnityEngine.UI.Image)data.FindProperty("firstPortrait").objectReferenceValue;
        UnityEngine.UI.Image secondPortrait = (UnityEngine.UI.Image)data.FindProperty("secondPortrait").objectReferenceValue;
        if (hud.gameObject.scene.name == "Track_v2")
        {
            Require(!unified, "Track_v2 must show individual dog stamina.");
            RequireReferences(hud, "firstPortrait", "firstNormalPortrait", "firstGoodPortrait", "firstBadPortrait",
                "secondPortrait", "secondNormalPortrait", "secondGoodPortrait", "secondBadPortrait");
        }
        GameObject track = (GameObject)data.FindProperty("trackRoot").objectReferenceValue;
        Vector2 panelSize = track.transform.Find("TrackUI_Panel").GetComponent<RectTransform>().sizeDelta;
        Require(Vector2.Distance(panelSize, new Vector2(272.58f, 156.672f)) < 0.001f,
            "Track HUD must match the authored size of 272.58 by 156.672.");
        foreach (UnityEngine.UI.Image fill in new[] { first, second, progress }.Where(fill => fill != null))
            Require(fill.type == UnityEngine.UI.Image.Type.Filled &&
                    fill.fillMethod == UnityEngine.UI.Image.FillMethod.Horizontal && fill.fillOrigin == 0,
                "Track HUD gauges must fill horizontally from the left.");

        FieldInfo current = typeof(MushGameSave).GetField("current", BindingFlags.NonPublic | BindingFlags.Static);
        object original = current.GetValue(null);
        try
        {
            MushGameSave.Data sample = new();
            current.SetValue(null, sample);
            Invoke(hud, "OnEnable");
            RequireArtworkUnchanged(track, () =>
            {
                foreach (int value in new[] { 0, 25, 50, 75, 100 })
                {
                    sample.dogs[0].stamina = value;
                    sample.dogs[1].stamina = 100 - value;
                    Invoke(hud, "LateUpdate");
                    if (unified)
                        Require(Mathf.Approximately(first.fillAmount, 0.5f) && firstText.text == "50 / 100",
                            "The unified stamina gauge must show the team average.");
                    else
                    {
                        Require(Mathf.Approximately(first.fillAmount, value / 100f) &&
                                Mathf.Approximately(second.fillAmount, (100 - value) / 100f),
                            "Track stamina bars must show each dog's actual stamina.");
                        Require(firstText.text == $"{value} / 100" && secondText.text == $"{100 - value} / 100",
                            "Track dog stamina labels are incorrect.");
                    }
                }
                if (unified)
                {
                    Require(track.GetComponentsInChildren<UnityEngine.UI.Image>().Count(image =>
                        image.type == UnityEngine.UI.Image.Type.Filled) == 1,
                        "The unified track HUD must show only one stamina gauge.");
                    foreach (int value in new[] { 0, 25, 50, 75, 100 })
                    {
                        sample.dogs[0].stamina = sample.dogs[1].stamina = value;
                        Invoke(hud, "LateUpdate");
                        Require(Mathf.Approximately(first.fillAmount, value / 100f) &&
                                firstText.text == $"{value} / 100",
                            "The unified gauge must cover the full stamina range.");
                    }
                }
                sample.dogs[0].stamina = 33.75f;
                sample.dogs[1].stamina = 0f;
                Invoke(hud, "LateUpdate");
                Require(Mathf.Approximately(first.fillAmount, unified ? 0.16875f : 0.3375f),
                    "Track health fill must update between whole numbers.");
                if (!unified && firstPortrait != null && secondPortrait != null)
                {
                    foreach (var (value, firstState, secondState) in new[]
                    {
                        (0f, "Low", "High"), (29.99f, "Low", "High"),
                        (30f, "Normal", "High"), (69.99f, "Normal", "Normal"),
                        (70f, "High", "Normal"), (100f, "High", "Low"),
                        (70f, "High", "Normal"), (30f, "Normal", "High"), (0f, "Low", "High")
                    })
                    {
                        sample.dogs[0].stamina = value;
                        sample.dogs[1].stamina = 100 - value;
                        Invoke(hud, "LateUpdate");
                        Require(Mathf.Approximately(first.fillAmount, value / 100f) &&
                                Mathf.Approximately(second.fillAmount, (100 - value) / 100f) &&
                                firstText.text == $"{Mathf.FloorToInt(value)} / 100" &&
                                secondText.text == $"{Mathf.FloorToInt(100 - value)} / 100",
                            "Track gauges and labels must follow each dog's stamina at condition boundaries.");
                        Require(AssetDatabase.GetAssetPath(firstPortrait.sprite) == $"Assets/Art/Textures/UI/T_KAI_{firstState}.png" &&
                                AssetDatabase.GetAssetPath(secondPortrait.sprite) == $"Assets/Art/Textures/UI/T_LUMI_{secondState}.png",
                            "Track portraits must use each dog's own stamina and the shared 30/70 thresholds.");
                        Require(sample.dogs.All(dog => dog.condition == MushDogCondition.Normal),
                            "Track portrait updates must not change gameplay mood.");
                    }
                }
            }, new[] { first, second }, new[] { firstPortrait, secondPortrait });

            MethodInfo updateProgress = typeof(MushRideHud).GetMethod("RefreshProgress", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (float value in new[] { -1f, 0f, 0.25f, 0.5f, 0.75f, 1f, 2f })
            {
                updateProgress.Invoke(hud, new object[] { value });
                float expected = Mathf.Clamp01(value);
                Rect rect = progress.rectTransform.rect;
                Vector3 worldEdge = progress.rectTransform.TransformPoint(new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, expected), rect.center.y, 0f));
                float markerX = marker.parent.InverseTransformPoint(worldEdge).x;
                Require(Mathf.Approximately(progress.fillAmount, expected) && Mathf.Abs(marker.localPosition.x - markerX) < 0.001f,
                    "Route fill and marker must follow progress from start through finish.");
            }
        }
        finally
        {
            current.SetValue(null, original);
            Invoke(hud, "OnEnable");
            Invoke(hud, "LateUpdate");
        }
    }

    private static void ValidateLobbyStamina(MushLobbyStaminaHud hud)
    {
        SerializedObject data = new(hud);
        UnityEngine.UI.Image firstFill = (UnityEngine.UI.Image)data.FindProperty("staminaFill").objectReferenceValue;
        UnityEngine.UI.Image secondFill = (UnityEngine.UI.Image)data.FindProperty("secondStaminaFill").objectReferenceValue;
        TMP_Text firstText = (TMP_Text)data.FindProperty("staminaText").objectReferenceValue;
        TMP_Text secondText = (TMP_Text)data.FindProperty("secondStaminaText").objectReferenceValue;
        TMP_Text firstConditionText = (TMP_Text)data.FindProperty("conditionText").objectReferenceValue;
        TMP_Text secondConditionText = (TMP_Text)data.FindProperty("secondConditionText").objectReferenceValue;
        Require(firstConditionText != null && secondConditionText != null,
            "Both lobby condition labels must be bound.");
        MushDogConditionIcon firstIcon = (MushDogConditionIcon)data.FindProperty("conditionIcon").objectReferenceValue;
        MushDogConditionIcon secondIcon = (MushDogConditionIcon)data.FindProperty("secondConditionIcon").objectReferenceValue;
        foreach (UnityEngine.UI.Image fill in new[] { firstFill, secondFill })
            Require(fill.type == UnityEngine.UI.Image.Type.Filled &&
                    fill.fillMethod == UnityEngine.UI.Image.FillMethod.Horizontal && fill.fillOrigin == 0,
                "Lobby stamina bars must fill horizontally from the left.");
        Require(hud.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text == "스테미너"),
            "Lobby stamina heading is missing.");

        FieldInfo current = typeof(MushGameSave).GetField("current", BindingFlags.NonPublic | BindingFlags.Static);
        object original = current.GetValue(null);
        try
        {
            MushGameSave.Data sample = new();
            current.SetValue(null, sample);
            RequireArtworkUnchanged(hud.transform.root.gameObject, () =>
            {
                foreach (var (value, firstState, secondState) in new[]
                {
                    (0f, "Low", "High"), (29.99f, "Low", "High"),
                    (30f, "Normal", "High"), (69.99f, "Normal", "Normal"),
                    (70f, "High", "Normal"), (100f, "High", "Low"),
                    (30f, "Normal", "High"), (0f, "Low", "High")
                })
                {
                    sample.dogs[0].stamina = value;
                    sample.dogs[1].stamina = 100 - value;
                    Invoke(hud, "Refresh");
                    Require(Mathf.Approximately(firstFill.fillAmount, Mathf.FloorToInt(value) / 100f) &&
                            Mathf.Approximately(secondFill.fillAmount, Mathf.FloorToInt(100 - value) / 100f),
                        "Lobby stamina bars do not match the individual dog values.");
                    Require(firstText.text == $"카이  {Mathf.FloorToInt(value)} / 100" &&
                            secondText.text == $"루미  {Mathf.FloorToInt(100 - value)} / 100",
                        "Lobby dog names or stamina values are incorrect.");
                    RequireStaminaPortrait(firstIcon, "T_KAI_", firstState);
                    RequireStaminaPortrait(secondIcon, "T_LUMI_", secondState);
                    Require(firstConditionText.text == (firstState switch
                            { "High" => "좋음", "Low" => "나쁨", _ => "평범" }) &&
                            secondConditionText.text == (secondState switch
                            { "High" => "좋음", "Low" => "나쁨", _ => "평범" }),
                        "Lobby condition labels must match the stamina portraits.");
                    Require(sample.dogs.All(dog => dog.condition == MushDogCondition.Normal),
                        "Portrait updates must not change the dogs' gameplay mood.");
                }
            }, firstFill, secondFill);
        }
        finally
        {
            current.SetValue(null, original);
            Invoke(hud, "OnEnable");
        }
    }

    private static void RequireArtworkUnchanged(GameObject root, Action updateValues, params UnityEngine.UI.Image[] changingFills)
    {
        RequireArtworkUnchanged(root, updateValues, changingFills, Array.Empty<UnityEngine.UI.Image>());
    }

    private static void RequireArtworkUnchanged(GameObject root, Action updateValues,
        UnityEngine.UI.Image[] changingFills, UnityEngine.UI.Image[] changingPortraits)
    {
        UnityEngine.UI.Image[] images = root.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        UnityEngine.UI.Image[] portraits = root.GetComponentsInChildren<MushDogConditionIcon>(true)
            .Select(icon => (UnityEngine.UI.Image)new SerializedObject(icon).FindProperty("conditionArtwork").objectReferenceValue)
            .Concat(changingPortraits).Where(image => image != null).ToArray();
        string[] before = images.Select(image => ImageAppearance(image, changingFills.Contains(image), portraits.Contains(image))).ToArray();
        updateValues();
        Require(before.SequenceEqual(images.Select(image => ImageAppearance(image, changingFills.Contains(image), portraits.Contains(image)))),
            root.name + ": updating values changed the authored UI artwork.");
    }

    private static void RequireStaminaPortrait(MushDogConditionIcon icon, string dogPrefix, string expectedState)
    {
        SerializedObject data = new(icon);
        string property = expectedState switch
        {
            "High" => "goodArtwork", "Low" => "badArtwork", _ => "normalArtwork"
        };
        Sprite expected = (Sprite)data.FindProperty(property).objectReferenceValue;
        UnityEngine.UI.Image image = (UnityEngine.UI.Image)data.FindProperty("conditionArtwork").objectReferenceValue;
        Require(expected != null && image != null && image.sprite == expected &&
                AssetDatabase.GetAssetPath(expected) == $"Assets/Art/Textures/UI/{dogPrefix}{expectedState}.png",
            $"{dogPrefix} portrait must show {expectedState}.");
    }

    private static string ImageAppearance(UnityEngine.UI.Image image, bool changingFill, bool changingPortrait) =>
        (changingPortrait ? "portrait" : image.sprite?.GetInstanceID().ToString()) + "|" + image.color + "|" + image.type + "|" + (changingFill ? "stamina" : image.fillAmount.ToString()) + "|" +
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
