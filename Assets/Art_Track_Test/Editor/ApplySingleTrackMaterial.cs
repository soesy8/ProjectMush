using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ApplySingleTrackMaterial
{
    private const string ScenePath = "Assets/Art_Track_Test/Track_MadeTest.unity";
    private const string PrefabPath = "Assets/Art_Track_Test/Model/Track_SnowRoad_CleanGrid.prefab";
    private const string MaterialPath = "Assets/Art_Track_Test/Material/M_Track_SnowRoad_Single.mat";
    private const string LocalBakePath = "Assets/Art_Track_Test/Model/Track_MadeTest_LocalBaked.asset";
    private const string DeformedMeshName = "004_Spline Deformed SnowRoad_CleanGrid";
    private const string CloseupPreviewPath = "Assets/Art_Track_Test/Track_SnowRoad_CleanGrid_Closeup.png";
    private const string ReportPath = "Assets/Art_Track_Test/Track_Module_Diagnostic.txt";

    [MenuItem("Mush/Diagnostics/Apply Single Track Material")]
    public static void Run()
    {
        try
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            AssetDatabase.ImportAsset(MaterialPath, ImportAssetOptions.ForceSynchronousImport);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null || material.shader == null)
                throw new MissingReferenceException("Single track material or its shader could not be loaded.");

            UpdateSourcePrefab(material);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            MushTrackAuthoring authoring = FindTrack(scene);
            Transform mapRoot = authoring.ResolveMapRoot();
            Transform generatedRoot = mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName);
            Transform deformedRoot = generatedRoot != null
                ? generatedRoot.Find(MushCurvedMapRuntime.DeformedRoadRootName)
                : null;
            if (deformedRoot == null)
                throw new MissingReferenceException("Deformed road root was not found.");

            MeshFilter filter = deformedRoot.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (filter == null || filter.sharedMesh == null || renderer == null)
                throw new MissingReferenceException("Deformed road mesh or renderer was not found.");

            Mesh sourceMesh = filter.sharedMesh;
            Mesh mesh = FindMeshAsset(LocalBakePath, DeformedMeshName);
            if (mesh == null)
                throw new MissingReferenceException("The local baked deformed-road mesh was not found.");
            if (sourceMesh != mesh)
                CopyMesh(sourceMesh, mesh);
            filter.sharedMesh = mesh;
            EditorUtility.SetDirty(filter);

            int oldSubMeshCount = mesh.subMeshCount;
            if (oldSubMeshCount > 1)
            {
                List<int> combinedTriangles = new();
                for (int subMesh = 0; subMesh < oldSubMeshCount; subMesh++)
                    combinedTriangles.AddRange(mesh.GetTriangles(subMesh));
                mesh.subMeshCount = 1;
                mesh.SetTriangles(combinedTriangles, 0, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                EditorUtility.SetDirty(mesh);
            }

            renderer.sharedMaterials = new[] { material };
            EditorUtility.SetDirty(renderer);

            SerializedObject serialized = new(authoring);
            serialized.FindProperty("roadMaterialOverride").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(authoring);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            RenderPreview(mapRoot, CloseupPreviewPath);
            string reportFile = Path.GetFullPath(ReportPath);
            string report = File.Exists(reportFile) ? File.ReadAllText(reportFile) : string.Empty;
            int previousUpdate = report.IndexOf("\nSingle material update", StringComparison.Ordinal);
            if (previousUpdate >= 0)
                report = report.Substring(0, previousUpdate).TrimEnd();
            File.WriteAllText(
                reportFile,
                report +
                $"\n\nSingle material update\n----------------------\n" +
                $"Material: {MaterialPath}\n" +
                $"Mesh source: {LocalBakePath}\n" +
                $"Textures: T_Track_SnowRoad_BaseColor, T_Track_SnowRoad_Normal_DX, T_Track_SnowRoad_Curvature\n" +
                $"Generated mesh submeshes: {oldSubMeshCount} -> {mesh.subMeshCount}\n" +
                $"Generated renderer material slots: {renderer.sharedMaterials.Length}\n" +
                $"Source prefab material slots: 1\n\n" +
                $"Texture mapping correction\n" +
                $"--------------------------\n" +
                $"Cause: the source image contains sawtooth-shaped grooves, and 1x1 mapping enlarged them across the full road module.\n" +
                $"UV correction: U 1x preserves the authored cross-road groove layout; V 4x shortens repetition along the driving direction.\n" +
                $"Surface correction: Base strength 0.72, Normal strength 0.75, Curvature strength 0.10, Smoothness 0.20.\n");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Applied the single snowy-track material to the source prefab and generated road.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
    }

    private static Mesh FindMeshAsset(string assetPath, string meshName)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            Mesh candidate = asset as Mesh;
            if (candidate != null && candidate.name == meshName)
                return candidate;
        }
        return null;
    }

    private static void CopyMesh(Mesh source, Mesh destination)
    {
        string destinationName = destination.name;
        destination.Clear(false);
        destination.name = destinationName;
        destination.indexFormat = source.indexFormat;
        destination.vertices = source.vertices;
        destination.normals = source.normals;
        destination.tangents = source.tangents;
        destination.colors = source.colors;

        List<Vector4> uvChannel = new();
        for (int channel = 0; channel < 8; channel++)
        {
            uvChannel.Clear();
            source.GetUVs(channel, uvChannel);
            destination.SetUVs(channel, uvChannel);
        }

        destination.subMeshCount = source.subMeshCount;
        for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            destination.SetIndices(
                source.GetIndices(subMesh, true),
                source.GetTopology(subMesh),
                subMesh,
                false,
                0);
        }
        destination.bounds = source.bounds;
        destination.UploadMeshData(false);
        EditorUtility.SetDirty(destination);
    }

    private static MushTrackAuthoring FindTrack(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            MushTrackAuthoring authoring = root.GetComponentInChildren<MushTrackAuthoring>(true);
            if (authoring != null)
                return authoring;
        }
        throw new MissingReferenceException("MushTrackAuthoring was not found.");
    }

    private static void UpdateSourcePrefab(Material material)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            MeshRenderer renderer = contents.GetComponentInChildren<MeshRenderer>(true);
            if (renderer == null)
                throw new MissingComponentException("The track prefab has no MeshRenderer.");
            renderer.sharedMaterials = new[] { material };
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void RenderPreview(Transform mapRoot, string outputPath)
    {
        GameObject cameraObject = new("Temporary Track Preview Camera");
        GameObject lightObject = new("Temporary Track Preview Light");
        RenderTexture target = null;
        Texture2D image = null;
        Camera camera = null;
        try
        {
            camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = mapRoot.TransformPoint(new Vector3(0f, 18f, -42f));
            Vector3 lookAt = mapRoot.TransformPoint(new Vector3(0f, 0.10f, -42f));
            camera.transform.rotation = Quaternion.LookRotation(lookAt - camera.transform.position, Vector3.forward);
            camera.orthographic = true;
            camera.orthographicSize = 4.2f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 350f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.04f, 0.065f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.45f;
            light.color = new Color(0.88f, 0.94f, 1f);
            light.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            target = new RenderTexture(
                1280,
                720,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;
            // The first manual render after opening this scene warms the render pipeline and may be blank.
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.GetFullPath(outputPath), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = null;
            if (camera != null)
                camera.targetTexture = null;
            if (image != null)
                Object.DestroyImmediate(image);
            if (target != null)
                Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
        }
    }
}
