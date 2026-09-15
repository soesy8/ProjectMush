using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Rebuilds Track_MadeTest as the broad, gently winding snowfield course.
/// All generated mesh/material sub-assets are kept beside the scene so this
/// art-test scene does not write into the shared Mush/GeneratedMaps folder.
/// </summary>
public static class BuildSnowfieldTrack980
{
    private const string ScenePath = "Assets/Art_Track_Test/Track_MadeTest.unity";
    private const string LocalBakePath = "Assets/Art_Track_Test/Model/Track_MadeTest_LocalBaked.asset";
    private const string ReportPath = "Assets/Art_Track_Test/Track_980m_Setup_Report.txt";
    private const float TerrainHalfWidth = 99f;

    // Centripetal Catmull-Rom length at 4 m sampling: approximately 979.1 m.
    // The restrained lateral offsets and elevation changes create readable,
    // sled-friendly S curves without sharp switchbacks.
    private static readonly Vector3[] RouteControlPoints =
    {
        new(0f, 0f, 0f),
        new(0f, 1f, -70f),
        new(-22f, 5f, -145f),
        new(-48f, 10f, -225f),
        new(-30f, 15f, -305f),
        new(18f, 18f, -390f),
        new(52f, 12f, -475f),
        new(38f, 4f, -560f),
        new(-12f, -2f, -645f),
        new(-55f, 2f, -730f),
        new(-38f, 9f, -815f),
        new(8f, 14f, -907f),
    };

    // This project intentionally has Pipeline auto-start disabled in its shared
    // VR settings. Start only the already-installed local Pipeline bridge when
    // this art-test tool loads, without changing those shared settings.
    [InitializeOnLoadMethod]
    private static void EnsureLocalPipelineBridge()
    {
        EditorApplication.delayCall += () =>
        {
            Type startup = Type.GetType(
                "Unity.Pipeline.Editor.PipelineServerStartup, Unity.Pipeline.Editor");
            startup?.GetMethod("EnsureServerStarted")?.Invoke(null, null);
        };
    }

    [MenuItem("Mush/Tracks/Build 980m Snowfield Track", false, 20)]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before rebuilding Track_MadeTest.");

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        MushTrackAuthoring authoring = FindTrack(scene);
        ConfigureAuthoring(authoring);

        List<Vector3> sampledRoute = new();
        if (!authoring.TryBuildSampledRoute(sampledRoute, out float routeLength, out float spacing))
            throw new InvalidOperationException("The authored snowfield route could not be sampled.");
        if (routeLength < 970f || routeLength > 990f)
            throw new InvalidOperationException($"Expected an approximately 980 m route, got {routeLength:0.0} m.");

        Transform mapRoot = authoring.ResolveMapRoot();
        MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
        if (runtime == null)
            runtime = mapRoot.gameObject.AddComponent<MushCurvedMapRuntime>();

        runtime.RebuildSceneWorld();
        Transform generatedRoot = mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName);
        if (generatedRoot == null)
            throw new MissingReferenceException("The rebuilt course hierarchy was not created.");

        PersistGeneratedResourcesLocally(runtime, generatedRoot);
        ValidateCourse(generatedRoot, routeLength);

        EditorUtility.SetDirty(authoring);
        EditorUtility.SetDirty(runtime);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(LocalBakePath, ImportAssetOptions.ForceUpdate);

        WriteReport(generatedRoot, routeLength, spacing, sampledRoute.Count);
        AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
        Selection.activeGameObject = mapRoot.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
        SceneView.RepaintAll();
        Debug.Log($"[Track_MadeTest] Snowfield course rebuilt: {routeLength:0.0} m, {sampledRoute.Count} samples.");
    }

    private static void ConfigureAuthoring(MushTrackAuthoring authoring)
    {
        SerializedObject serialized = new(authoring);
        serialized.FindProperty("useEditablePath").boolValue = true;
        serialized.FindProperty("sampleSpacing").floatValue = 4f;
        serialized.FindProperty("overrideTrackWidths").boolValue = true;
        serialized.FindProperty("roadHalfWidth").floatValue = 6.5f;
        serialized.FindProperty("terrainHalfWidth").floatValue = TerrainHalfWidth;
        serialized.FindProperty("useEditableTerrain").boolValue = false;
        serialized.FindProperty("generateProceduralEnvironment").boolValue = false;

        SerializedProperty points = serialized.FindProperty("controlPoints");
        points.arraySize = RouteControlPoints.Length;
        for (int index = 0; index < RouteControlPoints.Length; index++)
            points.GetArrayElementAtIndex(index).vector3Value = RouteControlPoints[index];

        // Keep a useful editable envelope serialized for later art iteration,
        // while the production terrain uses the denser non-folding grid.
        Vector3[] terrainEnvelope = BuildTerrainEnvelope(RouteControlPoints, TerrainHalfWidth);
        SerializedProperty terrainPoints = serialized.FindProperty("terrainControlPoints");
        terrainPoints.arraySize = terrainEnvelope.Length;
        for (int index = 0; index < terrainEnvelope.Length; index++)
            terrainPoints.GetArrayElementAtIndex(index).vector3Value = terrainEnvelope[index];

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(authoring);
    }

    private static Vector3[] BuildTerrainEnvelope(IReadOnlyList<Vector3> route, float halfWidth)
    {
        const float endPadding = 16f;
        List<Vector3> left = new(route.Count);
        List<Vector3> right = new(route.Count);
        for (int index = 0; index < route.Count; index++)
        {
            int previous = Mathf.Max(0, index - 1);
            int next = Mathf.Min(route.Count - 1, index + 1);
            Vector3 tangent = Vector3.ProjectOnPlane(route[next] - route[previous], Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.back;

            Vector3 center = route[index];
            if (index == 0)
                center -= tangent * endPadding;
            else if (index == route.Count - 1)
                center += tangent * endPadding;
            center.y = route[index].y - 0.18f;

            Vector3 lateral = Vector3.Cross(Vector3.up, tangent).normalized;
            left.Add(center - lateral * halfWidth);
            right.Add(center + lateral * halfWidth);
        }

        List<Vector3> envelope = new(left.Count + right.Count);
        envelope.AddRange(left);
        for (int index = right.Count - 1; index >= 0; index--)
            envelope.Add(right[index]);
        return envelope.ToArray();
    }

    private static MushTrackAuthoring FindTrack(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            MushTrackAuthoring authoring = root.GetComponentInChildren<MushTrackAuthoring>(true);
            if (authoring != null)
                return authoring;
        }
        throw new MissingReferenceException("MushTrackAuthoring was not found in Track_MadeTest.");
    }

    private static void PersistGeneratedResourcesLocally(
        MushCurvedMapRuntime runtime,
        Transform generatedRoot)
    {
        MushBakedMapAssetContainer container =
            AssetDatabase.LoadAssetAtPath<MushBakedMapAssetContainer>(LocalBakePath);
        if (container == null)
        {
            container = ScriptableObject.CreateInstance<MushBakedMapAssetContainer>();
            container.name = "Track_MadeTest Local Baked Assets";
            AssetDatabase.CreateAsset(container, LocalBakePath);
        }

        Object[] existingResources = AssetDatabase.LoadAllAssetsAtPath(LocalBakePath);
        List<Object> generatedResources = new();
        HashSet<Object> unique = new();
        foreach (MeshFilter filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
            AddTransientResource(filter.sharedMesh, generatedResources, unique);
        foreach (MeshCollider collider in generatedRoot.GetComponentsInChildren<MeshCollider>(true))
            AddTransientResource(collider.sharedMesh, generatedResources, unique);
        foreach (Renderer renderer in generatedRoot.GetComponentsInChildren<Renderer>(true))
        foreach (Material material in renderer.sharedMaterials)
            AddTransientResource(material, generatedResources, unique);

        for (int index = 0; index < generatedResources.Count; index++)
        {
            Object generated = generatedResources[index];
            string prefix = $"{index:D3}_";
            string stableName = prefix + generated.name;
            Object reusable = FindReusable(existingResources, prefix, generated.GetType());
            if (reusable != null)
            {
                if (generated is Mesh sourceMesh && reusable is Mesh destinationMesh)
                    CopyMesh(sourceMesh, destinationMesh);
                else
                    EditorUtility.CopySerialized(generated, reusable);

                reusable.name = stableName;
                ReplaceReferences(generatedRoot, generated, reusable);
                EditorUtility.SetDirty(reusable);
                Object.DestroyImmediate(generated);
            }
            else
            {
                generated.name = stableName;
                AssetDatabase.AddObjectToAsset(generated, container);
                EditorUtility.SetDirty(generated);
            }
        }

        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssets();
        runtime.ReleaseBakedResourceOwnership();
    }

    private static void AddTransientResource(
        Object resource,
        ICollection<Object> output,
        ISet<Object> unique)
    {
        if (resource != null && !EditorUtility.IsPersistent(resource) && unique.Add(resource))
            output.Add(resource);
    }

    private static Object FindReusable(Object[] resources, string prefix, Type type)
    {
        foreach (Object candidate in resources)
        {
            if (candidate != null && candidate.GetType() == type && candidate.name.StartsWith(prefix, StringComparison.Ordinal))
                return candidate;
        }
        return null;
    }

    private static void CopyMesh(Mesh source, Mesh destination)
    {
        destination.Clear(false);
        destination.indexFormat = source.indexFormat;
        destination.vertices = source.vertices;
        destination.normals = source.normals;
        destination.tangents = source.tangents;
        destination.colors = source.colors;

        List<Vector4> uv = new();
        for (int channel = 0; channel < 8; channel++)
        {
            uv.Clear();
            source.GetUVs(channel, uv);
            destination.SetUVs(channel, uv);
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
    }

    private static void ReplaceReferences(Transform root, Object source, Object replacement)
    {
        if (source is Mesh sourceMesh && replacement is Mesh replacementMesh)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == sourceMesh)
                    filter.sharedMesh = replacementMesh;
            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                if (collider.sharedMesh == sourceMesh)
                    collider.sharedMesh = replacementMesh;
            return;
        }

        if (source is not Material sourceMaterial || replacement is not Material replacementMaterial)
            return;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                if (materials[index] != sourceMaterial)
                    continue;
                materials[index] = replacementMaterial;
                changed = true;
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static void ValidateCourse(Transform generatedRoot, float routeLength)
    {
        string requiredPrefix = "Assets/Art_Track_Test/";
        int meshColliderCount = 0;
        foreach (MeshFilter filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                throw new MissingReferenceException($"{filter.name} has no baked mesh.");
            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (!path.StartsWith(requiredPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Generated mesh escaped the allowed folder: {path}");
        }

        foreach (MeshCollider collider in generatedRoot.GetComponentsInChildren<MeshCollider>(true))
        {
            if (collider.sharedMesh == null)
                throw new MissingReferenceException($"{collider.name} has no collision mesh.");
            meshColliderCount++;
        }

        Transform deformedRoad = generatedRoot.Find(MushCurvedMapRuntime.DeformedRoadRootName);
        if (deformedRoad == null || deformedRoad.GetComponentInChildren<MeshRenderer>(true) == null)
            throw new MissingReferenceException("The existing deformable snow-road module was not generated.");
        if (meshColliderCount < 2)
            throw new InvalidOperationException("Expected terrain and driving-surface MeshColliders.");
        if (Mathf.Abs(routeLength - 980f) > 10f)
            throw new InvalidOperationException($"Track length validation failed: {routeLength:0.0} m.");
    }

    private static void WriteReport(
        Transform generatedRoot,
        float routeLength,
        float sampleSpacing,
        int sampleCount)
    {
        Transform terrain = generatedRoot.Find("VISIBLE Snow Terrain");
        Transform deformedRoad = generatedRoot.Find(MushCurvedMapRuntime.DeformedRoadRootName);
        Renderer terrainRenderer = terrain != null ? terrain.GetComponent<Renderer>() : null;
        Renderer roadRenderer = deformedRoad != null ? deformedRoad.GetComponentInChildren<Renderer>(true) : null;
        int meshCount = generatedRoot.GetComponentsInChildren<MeshFilter>(true).Length;
        int colliderCount = generatedRoot.GetComponentsInChildren<MeshCollider>(true).Length;

        string report =
            "Track_MadeTest 980 m snowfield setup\n" +
            "===================================\n\n" +
            $"Measured route length: {routeLength:0.0} m\n" +
            $"Control points: {RouteControlPoints.Length}\n" +
            $"Uniform route samples: {sampleCount} at {sampleSpacing:0.000} m\n" +
            "Road width: 13.0 m\n" +
            $"Terrain corridor width: {TerrainHalfWidth * 2f:0.0} m\n" +
            "Route style: broad snowfield S-curves with gentle rolling elevation\n" +
            "Track module: Track_SnowRoad_CleanGrid (existing scene module)\n" +
            "Props/scenery generation: disabled\n" +
            $"Generated meshes: {meshCount}\n" +
            $"Generated mesh colliders: {colliderCount}\n" +
            $"Road bounds: {(roadRenderer != null ? roadRenderer.bounds.size.ToString("F1") : "missing")}\n" +
            $"Terrain bounds: {(terrainRenderer != null ? terrainRenderer.bounds.size.ToString("F1") : "missing")}\n" +
            $"Baked asset: {LocalBakePath}\n" +
            "All generated mesh assets are stored under Assets/Art_Track_Test.\n";

        File.WriteAllText(Path.GetFullPath(ReportPath), report);
    }
}
