using System;
using System.IO;
using System.Linq;
using Mush.Customization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Authors a visible-sled stencil mask and applies it only to the supplied snow-fog prefab.</summary>
public static class MushSledFogMaskInstaller
{
    public const string FeatureName = "Mush Sled Snow Fog Mask";
    public const string MaskMaterialPath = "Assets/Mush/Rendering/MushSledStencilMask.mat";
    public const string FogMaterialPath = "Assets/Mush/Rendering/MushSledProtectedSnowFog.mat";
    public const string FogPrefabPath = "Assets/Art/Prefabs/VFX/VFX_SnowFog_Plus.prefab";
    private const string SourceFogMaterialPath = "Assets/Art/Materials/Track_Module/M_FogVFXPlus.mat";
    public static readonly string[] RendererPaths =
    {
        "Assets/VR/Settings/Project Configuration/Standalone Preset.asset",
        "Assets/VR/Settings/Project Configuration/Android Preset.asset"
    };
    public static readonly string[] TrackPaths =
    {
        "Assets/Scenes/Track_v2.unity", "Assets/Scenes/Tree.unity", "Assets/Scenes/SharpCurve.unity"
    };

    [MenuItem("Mush/Rendering/Install Sled Fog Mask")]
    public static void Install()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Install the mask in Edit Mode.");
        Require(!Enumerable.Range(0, SceneManager.sceneCount).Any(index => SceneManager.GetSceneAt(index).isDirty),
            "Save modified scenes before installing the mask.");
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            int layer = EnsureLayer();
            Material mask = EnsureMaterial(MaskMaterialPath, "Mush/Sled Stencil Mask");
            Material fog = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
            if (fog == null)
            {
                Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceFogMaterialPath);
                Require(source != null, "The source snow-fog material is missing.");
                fog = new Material(source) { name = "MushSledProtectedSnowFog" };
                AssetDatabase.CreateAsset(fog, FogMaterialPath);
            }
            fog.shader = Shader.Find("Mush/Sled Protected Snow Fog");
            Require(fog.shader != null, "The protected fog shader did not import.");
            EditorUtility.SetDirty(fog);
            foreach (string path in RendererPaths) ConfigureRenderer(path, layer, mask);
            ConfigureFogPrefab(fog);
            foreach (string path in TrackPaths) ConfigureTrack(path);
            AssetDatabase.SaveAssets();
            ValidateBindings();
            Debug.Log("[Mush Fog Mask] PASS: installed on PC/Android renderers and all three tracks; only snow fog is filtered.");
        }
        finally
        {
            if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static int EnsureLayer()
    {
        int layer = LayerMask.NameToLayer(MushSledFogMask.LayerName);
        if (layer >= 0) return layer;
        SerializedObject tags = new(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tags.FindProperty("layers");
        for (int index = 8; index < layers.arraySize; index++)
        {
            if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(index).stringValue)) continue;
            layers.GetArrayElementAtIndex(index).stringValue = MushSledFogMask.LayerName;
            tags.ApplyModifiedPropertiesWithoutUndo();
            return index;
        }
        throw new InvalidOperationException("No free user layer is available for the sled mask.");
    }

    private static Material EnsureMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        Require(shader != null, "Missing shader: " + shaderName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureRenderer(string path, int layer, Material mask)
    {
        UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
        Require(renderer != null, "Missing URP renderer: " + path);
        RenderObjects feature = renderer.rendererFeatures.OfType<RenderObjects>().SingleOrDefault(item => item.name == FeatureName);
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = FeatureName;
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
        }
        feature.settings.passTag = FeatureName;
        feature.settings.Event = RenderPassEvent.BeforeRenderingTransparents;
        feature.settings.filterSettings.RenderQueueType = RenderQueueType.Opaque;
        feature.settings.filterSettings.LayerMask = 1 << layer;
        feature.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material;
        feature.settings.overrideMaterial = mask;
        feature.settings.overrideMaterialPassIndex = 0;
        // The mask shader writes only bit 64 and keeps the original depth/color intact.
        feature.settings.overrideDepthState = false;
        feature.settings.stencilSettings.overrideStencilState = false;
        feature.settings.cameraSettings.overrideCamera = false;
        feature.SetActive(true);
        feature.Create();
        EditorUtility.SetDirty(feature);
        SerializedObject data = new(renderer);
        SerializedProperty map = data.FindProperty("m_RendererFeatureMap");
        map.arraySize = renderer.rendererFeatures.Count;
        for (int index = 0; index < map.arraySize; index++)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[index], out string _, out long localId);
            map.GetArrayElementAtIndex(index).longValue = localId;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(renderer);
    }

    private static void ConfigureFogPrefab(Material fog)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(FogPrefabPath);
        try
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceFogMaterialPath);
            int changed = 0;
            foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    if (materials[index] != source && materials[index] != fog) continue;
                    materials[index] = fog;
                    changed++;
                }
                renderer.sharedMaterials = materials;
            }
            Require(changed > 0, "The selected snow-fog prefab has no matching material.");
            PrefabUtility.SaveAsPrefabAsset(root, FogPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ConfigureTrack(string path)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        MushMapRideBootstrap bootstrap = Components<MushMapRideBootstrap>(scene).Single();
        SerializedObject data = new(bootstrap);
        Transform holder = (Transform)data.FindProperty("sledHolder").objectReferenceValue;
        Transform seat = (Transform)data.FindProperty("rideSeatAnchor").objectReferenceValue;
        Require(holder != null && seat != null, "Missing authored sled in " + path);
        Require(MushSledFogMask.ApplyToSled(holder) > 0, "The sled has no meshes in " + path);
        foreach (Transform child in seat.GetComponentsInChildren<Transform>(true))
            if (child.name == MushCustomizationVisuals.SuppliedSledName) MushSledFogMask.ApplyToSled(child);
        EditorSceneManager.MarkSceneDirty(scene);
        Require(EditorSceneManager.SaveScene(scene), "Could not save " + path);
    }

    public static void ValidateBindings(bool validateTracks = true)
    {
        int layer = LayerMask.NameToLayer(MushSledFogMask.LayerName);
        Require(layer >= 8, "The sled mask layer is missing.");
        Material mask = AssetDatabase.LoadAssetAtPath<Material>(MaskMaterialPath);
        Material fog = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
        Require(mask != null && fog != null && mask.shader.name == "Mush/Sled Stencil Mask" &&
                fog.shader.name == "Mush/Sled Protected Snow Fog", "The mask material bindings are incorrect.");
        Require(!ShaderUtil.ShaderHasError(mask.shader) && !ShaderUtil.ShaderHasError(fog.shader), "A mask shader has compile errors.");
        foreach (string path in RendererPaths)
        {
            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            RenderObjects feature = renderer.rendererFeatures.OfType<RenderObjects>().Single(item => item.name == FeatureName);
            Require(feature.isActive && feature.settings.Event == RenderPassEvent.BeforeRenderingTransparents &&
                    feature.settings.filterSettings.LayerMask.value == (1 << layer) &&
                    feature.settings.overrideMaterial == mask && !feature.settings.cameraSettings.overrideCamera &&
                    !feature.settings.stencilSettings.overrideStencilState, "Incorrect mask pass in " + path);
        }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FogPrefabPath);
        Require(prefab.GetComponentsInChildren<ParticleSystemRenderer>(true).All(renderer => renderer.sharedMaterial == fog),
            "The chosen fog prefab does not use the protected shader.");
        foreach (string path in new[] { "Assets/Art/Prefabs/VFX/TrackVFX_Snow.prefab", "Assets/Art/Prefabs/VFX/VFX_FireFog.prefab" })
        {
            GameObject other = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(other.GetComponentsInChildren<ParticleSystemRenderer>(true).All(renderer => !renderer.sharedMaterials.Contains(fog)),
                "The protected material leaked into another VFX: " + path);
        }
        if (!validateTracks) return;
        foreach (string path in TrackPaths)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            MushMapRideBootstrap bootstrap = Components<MushMapRideBootstrap>(scene).Single();
            SerializedObject data = new(bootstrap);
            Transform holder = (Transform)data.FindProperty("sledHolder").objectReferenceValue;
            Require(holder.GetComponentsInChildren<MeshRenderer>(true).All(renderer => renderer.gameObject.layer == layer),
                "An authored sled mesh is missing the mask layer: " + path);
            Require(Components<ParticleSystemRenderer>(scene).All(renderer => renderer.gameObject.layer != layer),
                "The mask layer must not include VFX: " + path);
        }
    }

    private static T[] Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
