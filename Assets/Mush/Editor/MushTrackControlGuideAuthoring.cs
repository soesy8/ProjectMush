#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors shared control-card artwork and binds it to each playable track Canvas.</summary>
public static class MushTrackControlGuideAuthoring
{
    public static readonly string[] TrackScenes =
    {
        "Assets/Scenes/Track_v2.unity", "Assets/Scenes/Tree.unity", "Assets/Scenes/SharpCurve.unity"
    };
    private const string CardsPath = "Assets/Mush/Runtime/UI/MushRideControlGuideCards.prefab";
    private const string TextureFolder = "Assets/Art/Textures/UI/Tutorial/";

    [MenuItem("Mush/UI/Author Track Control Guides")]
    public static void AuthorAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Control guides must be authored in Edit Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scenes before authoring control guides.");

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Sprite[] sprites = new[] { "Start", "Accelerate", "Steer" }.Select(ImportCard).ToArray();
            GameObject cards = CreateCards(sprites);
            GameObject prefab;
            try { prefab = PrefabUtility.SaveAsPrefabAsset(cards, CardsPath); }
            finally { UnityEngine.Object.DestroyImmediate(cards); }

            foreach (string scenePath in TrackScenes)
            {
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                MushSceneUI ui = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MushSceneUI>(true)).Single();
                SerializedObject uiBindings = new(ui);
                MushMapRideBootstrap ride = (MushMapRideBootstrap)uiBindings.FindProperty("ride").objectReferenceValue;
                if (ride == null) throw new InvalidOperationException("Missing ride on " + scenePath);

                // The new cards replace the old control text above the pause menu.
                GameObject pause = (GameObject)uiBindings.FindProperty("pausePanel").objectReferenceValue;
                if (pause != null)
                    foreach (Transform child in pause.GetComponentsInChildren<Transform>(true))
                        if (child.name == "Pause Controls") child.gameObject.SetActive(false);

                Transform existing = ui.transform.Find("Ride Control Guides");
                GameObject instance = existing != null ? existing.gameObject
                    : (GameObject)PrefabUtility.InstantiatePrefab(prefab, ui.transform);
                instance.name = "Ride Control Guides";
                // Above PauseUI's backing; options retain their existing modal priority.
                GameObject options = (GameObject)uiBindings.FindProperty("optionPanel").objectReferenceValue;
                if (options != null) instance.transform.SetSiblingIndex(options.transform.GetSiblingIndex());

                MushRideControlGuide guide = ui.GetComponent<MushRideControlGuide>();
                if (guide == null) guide = ui.gameObject.AddComponent<MushRideControlGuide>();
                SerializedObject guideBindings = new(guide);
                guideBindings.FindProperty("ride").objectReferenceValue = ride;
                guideBindings.FindProperty("guideRoot").objectReferenceValue = instance.GetComponent<RectTransform>();
                guideBindings.FindProperty("guideGroup").objectReferenceValue = instance.GetComponent<CanvasGroup>();
                guideBindings.FindProperty("departureDuration").floatValue = 0.45f;
                guideBindings.FindProperty("departureRise").floatValue = 320f;
                guideBindings.ApplyModifiedPropertiesWithoutUndo();
                uiBindings.FindProperty("controlGuide").objectReferenceValue = guide;
                uiBindings.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Mush Control Guides] Authored " + scenePath);
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static Sprite ImportCard(string name)
    {
        string assetPath = TextureFolder + "T_UI_Control_" + name + ".png";
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        TextureImporterSettings settings = new();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath)
            ?? throw new InvalidOperationException("Could not import control card " + assetPath);
    }

    private static GameObject CreateCards(Sprite[] sprites)
    {
        GameObject root = new("Ride Control Guides", typeof(RectTransform), typeof(CanvasGroup),
            typeof(UnityEngine.UI.HorizontalLayoutGroup));
        root.layer = 5;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.06f, 1f);
        rect.anchorMax = new Vector2(0.94f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -100f);
        rect.sizeDelta = new Vector2(0f, 196f);
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        UnityEngine.UI.HorizontalLayoutGroup layout = root.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = true;

        string[] names = { "Control Start", "Control Accelerate", "Control Steer" };
        for (int i = 0; i < sprites.Length; i++)
        {
            GameObject card = new(names[i], typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.LayoutElement));
            card.layer = root.layer;
            card.transform.SetParent(root.transform, false);
            UnityEngine.UI.Image image = card.GetComponent<UnityEngine.UI.Image>();
            image.sprite = sprites[i];
            image.preserveAspect = true;
            image.raycastTarget = false;
            UnityEngine.UI.LayoutElement element = card.GetComponent<UnityEngine.UI.LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = 540f;
            element.flexibleWidth = 1f;
        }
        return root;
    }
}
#endif
