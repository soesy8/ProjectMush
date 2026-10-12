#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Mush.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Creates the acceleration draft assets and binds only Track_v2.</summary>
public static class MushAccelerationVfxAuthoring
{
    public const string Folder = "Assets/Mush/VFX/Acceleration";
    public const string PrefabPath = Folder + "/VFX_AccelerationEntry.prefab";
    public const string MaterialPath = Folder + "/M_AccelerationFlow.mat";
    public const string MeshPath = Folder + "/Mesh_AccelerationFlow.asset";
    private const string TrackPath = "Assets/Scenes/Track_v2.unity";
    private const int Segments = 32;
    private const float MaximumFlowWidth = 0.384f * 1.15f * 0.80f;

    [MenuItem("Mush/VFX/Install Acceleration Draft (Track_v2)")]
    public static void InstallTrackV2()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != TrackPath)
            throw new InvalidOperationException("Open Track_v2 in Edit Mode before installing the draft.");

        MushSledKeyboardController controller = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<MushSledKeyboardController>(true)).Single();
        MushMapRideBootstrap ride = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<MushMapRideBootstrap>(true)).Single();
        EnsureFolder("Assets/Mush/VFX");
        EnsureFolder(Folder);
        Texture2D soft = ImportTexture("T_VFX_WindFlow_Soft.png");
        Texture2D wispy = ImportTexture("T_VFX_WindFlow_Wispy.png");
        Shader shader = Shader.Find("Mush/Acceleration Flow")
            ?? throw new InvalidOperationException("Acceleration shader was not imported.");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "M_AccelerationFlow" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", soft);
        material.SetTexture("_DetailMap", wispy);
        material.SetColor("_BaseColor", new Color(194f / 255f, 238f / 255f, 1f, 0.58f));
        material.SetFloat("_Elapsed", -1f);
        material.SetFloat("_Duration", 0.85f);
        material.SetFloat("_TextureStrength", 0.45f);
        material.SetVector("_ClearZone", new Vector4(0.5f, 0.5f, 0.265f, 0.32f));
        material.SetFloat("_ClearZoneFeather", 0.12f);
        material.SetFloat("_EdgeFeather", 0.30f);
        material.SetFloat("_EdgeTransparencyScale", 1.40f);
        material.SetVector("_EndFeather", new Vector4(0.09f, 0.07f, 0f, 0f));
        material.SetFloat("_InnerTipFeather", 0.22f);
        material.SetFloat("_InnerTipOpacity", 0.12f);
        material.SetFloat("_TransparentRegionScale", 1.10f);
        EditorUtility.SetDirty(material);

        Mesh generated = BuildMesh();
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null)
        {
            mesh = generated;
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }
        else
        {
            // Mesh setters also refresh GPU buffers for already loaded renderers.
            Undo.RegisterCompleteObjectUndo(mesh, "Update acceleration flow mesh");
            mesh.Clear();
            mesh.SetVertices(generated.vertices);
            mesh.SetUVs(0, generated.uv);
            mesh.SetUVs(1, generated.uv2);
            mesh.SetTriangles(generated.triangles, 0);
            mesh.bounds = generated.bounds;
            mesh.name = generated.name;
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(mesh);
        }

        GameObject template = new("AirflowFrame", typeof(MeshFilter), typeof(MeshRenderer),
            typeof(MushAccelerationVfx));
        GameObject prefab;
        try
        {
            SetupRenderer(template, mesh, material);
            template.GetComponent<MushAccelerationVfx>().Configure(null, null, material);
            prefab = PrefabUtility.SaveAsPrefabAsset(template, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(template); }

        Transform existing = controller.transform.Find("AirflowFrame");
        if (existing == null)
            existing = controller.transform.Find("VFX_AccelerationEntry");
        GameObject instance;
        if (existing != null) instance = existing.gameObject;
        else
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, controller.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Install acceleration draft");
        }
        Undo.RecordObject(instance.transform, "Position acceleration draft");
        instance.name = "AirflowFrame";
        Vector3 origin = controller.transform.InverseTransformPoint(ride.RideCamera.transform.position);
        instance.transform.SetLocalPositionAndRotation(origin, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        SetupRenderer(instance, mesh, material);
        instance.GetComponent<MushAccelerationVfx>().Configure(controller, ride, material);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<MushAccelerationVfx>());
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = instance;
        Debug.Log("[Mush Acceleration] Track_v2 draft installed: 16 flows, 0.85 seconds, one reusable renderer.");
    }

    private static void SetupRenderer(GameObject target, Mesh mesh, Material material)
    {
        target.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.enabled = false;
        renderer.sortingOrder = -10;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    private static Texture2D ImportTexture(string filename)
    {
        string path = Folder + "/" + filename;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter
            ?? throw new InvalidOperationException("Missing draft texture: " + path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.isReadable = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static Mesh BuildMesh()
    {
        List<Vector3> vertices = new();
        List<Vector2> uvs = new();
        List<Vector2> flowData = new();
        List<int> indices = new();
        // Remove eight of the previous twenty flows, then add the four marked side flows.
        // Retained source indices preserve their curves, width ratios and start delays.
        int[] sourceLines = { 0, 1, 3, 5, 6, 8, 11, 13, 14, 15, 16, 18, 20, 21, 22, 23 };
        float[] widthRatios = { 0.20f, 0.30f, 0.34f, 0.28f, 0.20f, 0.32f,
            0.36f, 1.00f, 0.30f, 0.45f, 0.20f, 0.24f, 0.24f, 0.30f, 0.22f, 0.32f };
        for (int flow = 0; flow < sourceLines.Length; flow++)
        {
            int line = sourceLines[flow];
            Vector2 offset;
            if (line < 10)
            {
                int side = line < 5 ? -1 : 1;
                int row = line % 5;
                offset = new Vector2(side * (1.90f + 0.11f * (row % 3)), -0.25f + row * 0.43f);
            }
            else if (line < 16)
            {
                int top = line - 10;
                offset = new Vector2(-1.65f + top * 0.66f, 1.95f + (top % 3) * 0.16f);
            }
            else if (line < 20)
            {
                // Lower diagonals surround the sled without crossing its center.
                int lower = line - 16;
                int side = lower < 2 ? -1 : 1;
                int row = lower % 2;
                offset = new Vector2(side * (1.55f + row * 0.30f), -1.25f - row * 0.40f);
            }
            else
            {
                // The four user-marked paths sit below the horizon on both sides.
                int marked = line - 20;
                int side = marked < 2 ? -1 : 1;
                int row = marked % 2;
                offset = new Vector2(side * (1.95f + row * 0.10f), -0.55f - row * 0.40f);
            }
            Vector3 tangent = new Vector3(-offset.y, offset.x, 0f).normalized;
            float width = MaximumFlowWidth * widthRatios[flow];
            float delay = ((line * 7) % 20) / 19f * 0.07f;
            float front = 7.3f - (line % 4) * 0.28f;
            float amplitude = 0.16f + (line % 4) * 0.025f;
            float cycles = 1.10f + (line % 3) * 0.08f;
            float phase = line * 2.399963f;
            // Two fixed crossed surfaces keep the strip readable during head turns.
            // They remain in the sled frame and never billboard to the head camera.
            for (int face = 0; face < 2; face++)
            {
                Vector3 cross = Quaternion.AngleAxis(face == 0 ? -22f : 22f, Vector3.forward) * tangent;
                int first = vertices.Count;
                for (int segment = 0; segment <= Segments; segment++)
                {
                    float u = segment / (float)Segments;
                    float wave = Mathf.Sin(u * Mathf.PI * 2f * cycles + phase) * amplitude;
                    float spread = 1f + 0.08f * u;
                    Vector3 center = new(offset.x * spread, offset.y, Mathf.Lerp(front, -2.8f, u));
                    center += tangent * wave;
                    Vector3 halfWidth = cross * (width * 0.5f);
                    vertices.Add(center - halfWidth);
                    vertices.Add(center + halfWidth);
                    uvs.Add(new Vector2(u, 0f));
                    uvs.Add(new Vector2(u, 1f));
                    Vector2 data = new(delay, line % 3 == 0 ? 1f : 0f);
                    flowData.Add(data);
                    flowData.Add(data);
                    if (segment == Segments) continue;
                    int v = first + segment * 2;
                    indices.Add(v); indices.Add(v + 2); indices.Add(v + 1);
                    indices.Add(v + 1); indices.Add(v + 2); indices.Add(v + 3);
                }
            }
        }
        Mesh mesh = new() { name = "Mesh_AccelerationFlow" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, flowData);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
#endif
