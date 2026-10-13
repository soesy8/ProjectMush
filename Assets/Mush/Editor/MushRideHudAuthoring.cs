#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Places the route gauge inside the sled's authored instrument panel.</summary>
public static class MushRideHudAuthoring
{
    public const float PanelWidth = 272.58f;
    public const float PanelHeight = 195.472f;
    private const float AddedHeight = 38.8f;
    private const float ProgressBackgroundHeight = 28.8f;
    private const float ProgressFillHeight = 15.6f;
    private const float IconBottomInset = 4f;
    // Transparent rows below each visible tip, expressed at the unchanged 42 px icon size.
    private const float MarkerBottomPadding = 42f * 29f / 1024f;
    private const float GoalBottomPadding = 42f * 82f / 1254f;
    private const string FlagPath = "Assets/Art/Textures/UI/T_UI_GoalFlag.png";
    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/Track_v2.unity", "Assets/Scenes/Tree.unity", "Assets/Scenes/SharpCurve.unity"
    };

    [MenuItem("Mush/UI/Integrate Ride Progress")]
    public static void AuthorAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author the ride HUD in Edit Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scenes before authoring the ride HUD.");

        Sprite flag = ImportFlag();
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string path in ScenePaths)
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                MushRideHud hud = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MushRideHud>(true)).Single(h => h.enabled);
                Integrate(hud, flag);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static Sprite ImportFlag()
    {
        AssetDatabase.ImportAsset(FlagPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(FlagPath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing goal flag: " + FlagPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(FlagPath);
    }

    private static void Integrate(MushRideHud hud, Sprite flag)
    {
        SerializedObject bindings = new(hud);
        GameObject track = Get<GameObject>(bindings, "trackRoot");
        RectTransform panel = track.transform.Find("TrackUI_Panel").GetComponent<RectTransform>();
        RectTransform progressRoot = Get<GameObject>(bindings, "progressRoot").GetComponent<RectTransform>();
        UnityEngine.UI.Image progress = Get<UnityEngine.UI.Image>(bindings, "progress");
        RectTransform marker = Get<RectTransform>(bindings, "progressIcon");
        RectTransform background = progressRoot.Find("ProgressImage").GetComponent<RectTransform>();

        // Expand the backing upward and arrange the rows without scaling their contents.
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panel.anchoredPosition = new Vector2(0f, AddedHeight * 0.5f);
        Vector3 angles = track.transform.localEulerAngles;
        angles.x = 33.455f;
        track.transform.localRotation = Quaternion.Euler(angles);

        float timerY = PanelHeight * 0.5f - 8f - 25f;
        float secondStaminaY = -PanelHeight * 0.5f + 8f + 22f;
        float firstStaminaY = secondStaminaY + 44f;
        float markerY = (marker.rect.height - ProgressBackgroundHeight) * 0.5f;
        // Keep the gauge and stamina rows stationary while lowering the timer and top edge.
        float progressY = 34.8f - AddedHeight * 0.5f;
        SetY(panel.Find("DogState").GetComponent<RectTransform>(), timerY);
        SetY(panel.Find("TimerText").GetComponent<RectTransform>(), timerY);
        SetY(Get<UnityEngine.UI.Image>(bindings, "staminaFill").transform.parent.GetComponent<RectTransform>(), firstStaminaY);
        SetY(Get<UnityEngine.UI.Image>(bindings, "secondStaminaFill").transform.parent.GetComponent<RectTransform>(), secondStaminaY);
        SetY(Get<UnityEngine.UI.Image>(bindings, "firstPortrait").rectTransform, firstStaminaY);
        SetY(Get<UnityEngine.UI.Image>(bindings, "secondPortrait").rectTransform, secondStaminaY);

        progressRoot.SetParent(panel, false);
        Center(progressRoot);
        progressRoot.anchoredPosition = new Vector2(0f, progressY);
        progressRoot.sizeDelta = new Vector2(PanelWidth - 26f, ProgressBackgroundHeight);
        Center(background);
        background.sizeDelta = new Vector2(PanelWidth - 26f, ProgressBackgroundHeight);
        Center(progress.rectTransform);
        progress.rectTransform.sizeDelta = new Vector2(background.sizeDelta.x - 48f, ProgressFillHeight);
        progress.fillAmount = 0f;

        // Preserve both marker dimensions while resizing only the gauge rectangles.
        marker.anchoredPosition = new Vector2(-progress.rectTransform.rect.width * 0.5f, markerY - MarkerBottomPadding + IconBottomInset);
        Transform existing = progress.transform.Find("GoalIcon");
        GameObject goalObject = existing != null ? existing.gameObject
            : UnityEngine.Object.Instantiate(marker.gameObject, progress.transform, false);
        goalObject.name = "GoalIcon";
        UnityEngine.UI.Image goal = goalObject.GetComponent<UnityEngine.UI.Image>();
        goal.sprite = flag;
        goal.type = UnityEngine.UI.Image.Type.Simple;
        goal.preserveAspect = true;
        goal.raycastTarget = false;
        RectTransform goalRect = goal.rectTransform;
        goalRect.anchorMin = goalRect.anchorMax = new Vector2(1f, 0.5f);
        goalRect.pivot = new Vector2(0.5f, 0.5f);
        goalRect.sizeDelta = marker.sizeDelta;
        goalRect.anchoredPosition3D = new Vector3(0f, markerY - GoalBottomPadding + IconBottomInset, 0f);
        goalRect.localScale = marker.localScale;
        goalRect.localRotation = marker.localRotation;
        goalObject.SetActive(true);
        marker.SetAsLastSibling();

        // Decorative corner artwork keeps its original pixel size while the backing grows.
        RectTransform artwork = panel.Find("Improved UI Artwork").GetComponent<RectTransform>();
        RectTransform snow = artwork.Find("SnowTrim").GetComponent<RectTransform>();
        snow.anchorMin = new Vector2(0f, 1f);
        snow.anchorMax = Vector2.one;
        snow.pivot = new Vector2(0.5f, 0.5f);
        snow.sizeDelta = new Vector2(0f, 30.55104f);
        snow.anchoredPosition = Vector2.zero;
        RectTransform leaves = artwork.Find("Leaves").GetComponent<RectTransform>();
        leaves.anchorMin = leaves.anchorMax = Vector2.one;
        leaves.pivot = Vector2.one;
        leaves.sizeDelta = new Vector2(22.147125f, 27.4176f);
        leaves.anchoredPosition = new Vector2(-13.458638f, -6.65856f);

        Canvas.ForceUpdateCanvases();
        if (!progressRoot.IsChildOf(track.transform))
            throw new InvalidOperationException("Progress must move with the sled HUD.");
        foreach (var (icon, inset) in new[] { (marker, MarkerBottomPadding), (goalRect, GoalBottomPadding) })
        {
            Vector3 tip = icon.TransformPoint(new Vector3(0f, icon.rect.yMin + inset, 0f));
            if (Mathf.Abs(background.InverseTransformPoint(tip).y - background.rect.yMin - IconBottomInset) > 0.001f)
                throw new InvalidOperationException("Both visible icon tips must sit just inside the gauge border.");
        }
        if (Mathf.Abs(background.rect.height - ProgressBackgroundHeight) > 0.001f ||
            Mathf.Abs(progress.rectTransform.rect.height - ProgressFillHeight) > 0.001f ||
            Vector2.Distance(marker.sizeDelta, new Vector2(42f, 42f)) > 0.001f)
            throw new InvalidOperationException("Progress heights must be 60% with the original marker size.");
    }

    private static T Get<T>(SerializedObject bindings, string field) where T : UnityEngine.Object
    {
        T value = bindings.FindProperty(field).objectReferenceValue as T;
        if (value == null) throw new InvalidOperationException("Missing ride HUD binding: " + field);
        return value;
    }

    private static void SetY(RectTransform rect, float y)
        => rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);

    private static void Center(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition3D = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }
}
#endif
