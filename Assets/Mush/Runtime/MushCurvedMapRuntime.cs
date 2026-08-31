using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Replaces the broken straight V2 map presentation at runtime with a visible,
/// deterministic curved winter course. Existing imported renderers/colliders
/// are disabled, while their scene/controller roots remain intact.
/// </summary>
[DisallowMultipleComponent]
public sealed class MushCurvedMapRuntime : MonoBehaviour
{
    private const float RoadHalfWidth = 6.5f;
    private const float SharpCurveRoadHalfWidth = 3.25f;
    private const float TerrainHalfWidth = 105f;
    public const string GeneratedWorldRootName = "Mush Rebuilt Curved World";
    public const string CustomSceneContentRootName = "SCENE CONTENT - Add Models Here";
    public const string RideTeamRootName = "Mush Ride Team";

    private readonly List<Vector3> routePoints = new();
    private readonly List<Material> runtimeMaterials = new();
    private bool built;
    private bool isSnowfield;
    private bool isSharpCurve;
    private bool usesEditableTrack;
    private bool overridesTrackWidths;
    private int defaultCurveCount;
    private float activeCourseLength = MushTrackPathUtility.DefaultCourseLength;
    private float activeSampleSpacing = MushTrackPathUtility.DefaultSampleSpacing;
    private float authoredRoadHalfWidth;
    private float authoredTerrainHalfWidth;
    private Transform rebuiltRoot;
    private Mesh pineMesh;
    private Mesh mountainMesh;
    private Renderer roadRenderer;
    private Renderer terrainRenderer;
    private Transform sharpProgressTarget;
    private float sharpProgress;
    private Light sharpSun;
    private Camera sharpCamera;
    private Material sharpSky;
    private Renderer sharpStars;
    private ParticleSystem sharpMeteorShower;
    private Transform sharpAuroraRoot;
    private Renderer sharpAuroraSkyRenderer;
    private MaterialPropertyBlock sharpEffectBlock;

    public Vector3 StartForward { get; private set; } = Vector3.back;
    public Transform AmbientSnowTransform { get; private set; }
    public float LengthMeters => activeCourseLength;
    public float RoadHalfWidthMeters => ActiveRoadHalfWidth;
    public bool IsSharpCurveMap => isSharpCurve;
    public float CurrentProgress01 => sharpProgress;
    public bool SharpDownhillSpeedBoostActive => isSharpCurve && sharpProgress >= 0.35f && sharpProgress <= 0.66f;

    private float ActiveRoadHalfWidth => overridesTrackWidths
        ? authoredRoadHalfWidth
        : isSharpCurve ? SharpCurveRoadHalfWidth : RoadHalfWidth;
    private float ActiveTerrainHalfWidth => overridesTrackWidths
        ? authoredTerrainHalfWidth
        : isSharpCurve ? 42f : TerrainHalfWidth;

    public static MushCurvedMapRuntime EnsureBuilt(Transform mapRoot)
    {
        if (mapRoot == null)
            return null;

        MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
        if (runtime == null)
            runtime = mapRoot.gameObject.AddComponent<MushCurvedMapRuntime>();
        runtime.BuildWorld();
        return runtime;
    }

    public void BuildWorld()
    {
        if (built)
            return;

        ConfigureMapType();
        BuildActiveRoute();
        rebuiltRoot = transform.Find(GeneratedWorldRootName);
        if (rebuiltRoot == null)
        {
            // Compatibility path for scenes that have not been opened once by
            // the editor baker yet. As soon as the scene is baked, this branch
            // is never reached and play mode adopts the saved hierarchy.
            Debug.LogWarning(
                $"[Mush] '{gameObject.scene.path}' has not been baked yet; generating a temporary compatibility world. " +
                "Open the scene once and save it to make the hierarchy authoritative.",
                this);
            DisableImportedPresentation();
            GameObject rootObject = new(GeneratedWorldRootName);
            rebuiltRoot = rootObject.transform;
            rebuiltRoot.SetParent(transform, false);
            BuildCourseMeshes();
            BuildScenery();
            BuildSkyAndLighting();
            BuildAmbientSnow();
            PositionRouteMarkers();
        }
        else
        {
            CacheBakedWorldReferences();
            ConfigureRuntimeEnvironmentControllers();
        }

        built = true;
        Bounds roadBounds = roadRenderer != null ? roadRenderer.bounds : default;
        Bounds terrainBounds = terrainRenderer != null ? terrainRenderer.bounds : default;
        Debug.Log(
            $"[Mush Map Rebuild] Scene={gameObject.scene.path}, Type={(isSharpCurve ? "SHARP CURVE" : isSnowfield ? "SNOW" : "TREE")}, " +
            $"Track={(usesEditableTrack ? "EDITABLE" : "DEFAULT")}, Length={activeCourseLength:0}m, " +
            $"Samples={routePoints.Count}, Curves={defaultCurveCount}, " +
            $"RoadBounds={roadBounds.size}, TerrainBounds={terrainBounds.size}, " +
            $"Renderers={rebuiltRoot.GetComponentsInChildren<Renderer>(true).Length}",
            this);
    }

    /// <summary>
    /// Creates the actual gameplay world as ordinary scene objects. This is
    /// editor authoring work, not a runtime preview: once saved, play mode uses
    /// this exact hierarchy without recreating or replacing it.
    /// </summary>
    public void RebuildSceneWorld()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("A baked Mush world can only be rebuilt outside play mode.");

        built = false;
        ConfigureMapType();
        BuildActiveRoute();
        Transform customContent = transform.Find(CustomSceneContentRootName);
        if (customContent == null)
        {
            GameObject customContentObject = new(CustomSceneContentRootName);
            customContent = customContentObject.transform;
            customContent.SetParent(transform, false);
        }
        DisableImportedPresentation();

        Transform previousRoot = transform.Find(GeneratedWorldRootName);
        if (previousRoot != null)
            DestroyImmediate(previousRoot.gameObject);

        GameObject rootObject = new(GeneratedWorldRootName);
        rebuiltRoot = rootObject.transform;
        rebuiltRoot.SetParent(transform, false);

        BuildCourseMeshes();
        BuildScenery();
        BuildSkyAndLighting();
        BuildAmbientSnow();
        PositionRouteMarkers();
        built = true;
    }

    /// <summary>
    /// The editor transfers generated meshes and materials into an AssetDatabase
    /// container. They are no longer temporary resources owned by this component.
    /// </summary>
    public void ReleaseBakedResourceOwnership()
    {
        runtimeMaterials.Clear();
        pineMesh = null;
        mountainMesh = null;
    }

    private void ConfigureMapType()
    {
        isSharpCurve = name.Contains("SharpCurve", StringComparison.OrdinalIgnoreCase) ||
                       gameObject.scene.name.Equals("SharpCurve", StringComparison.OrdinalIgnoreCase);
        isSnowfield = !isSharpCurve &&
                      (GetComponent<MushSnowfieldBlizzardController>() != null ||
                       name.Contains("Snow", StringComparison.OrdinalIgnoreCase));
    }

    private void CacheBakedWorldReferences()
    {
        roadRenderer = FindGeneratedComponent<Renderer>("VISIBLE Curved Packed-Snow Road");
        terrainRenderer = FindGeneratedComponent<Renderer>("VISIBLE Snow Terrain");
        AmbientSnowTransform = FindGeneratedTransform("FX_AmbientSnow_Rebuilt");

        if (!isSharpCurve)
            return;

        sharpSun = FindGeneratedComponent<Light>("Mush Rebuilt Sun") ?? FindSceneComponent<Light>();
        sharpCamera = FindSceneComponent<Camera>();
        sharpSky = RenderSettings.skybox;
        sharpStars = FindGeneratedComponent<Renderer>("FX_StarDome_Rebuilt");
        sharpMeteorShower = FindGeneratedComponent<ParticleSystem>("FX_SharpCurve_MeteorShower");
        sharpAuroraRoot = FindGeneratedTransform("FX_SharpCurve_Aurora");
        sharpAuroraSkyRenderer = FindGeneratedComponent<Renderer>("Aurora Sky Dome Renderer");
    }

    private void ConfigureRuntimeEnvironmentControllers()
    {
        Camera sceneCamera = FindSceneComponent<Camera>();
        Light sun = FindGeneratedComponent<Light>("Mush Rebuilt Sun") ?? FindSceneComponent<Light>();
        ParticleSystem snow = AmbientSnowTransform != null
            ? AmbientSnowTransform.GetComponent<ParticleSystem>()
            : null;

        if (isSharpCurve)
        {
            ApplySharpCurveEnvironment(0f);
            return;
        }

        if (isSnowfield)
        {
            MushSnowfieldBlizzardController controller = GetComponent<MushSnowfieldBlizzardController>();
            controller?.ConfigureRuntimeWorld(sun, sceneCamera, RenderSettings.skybox, null, activeCourseLength);
            controller?.SetSnowParticles(snow);
        }
        else
        {
            MushForestTimeCycleController controller = GetComponent<MushForestTimeCycleController>();
            controller?.ConfigureRuntimeWorld(
                sun,
                sceneCamera,
                RenderSettings.skybox,
                FindGeneratedComponent<Renderer>("FX_StarDome_Rebuilt"),
                activeCourseLength);
        }
    }

    private Transform FindGeneratedTransform(string objectName)
    {
        if (rebuiltRoot == null)
            return null;

        foreach (Transform child in rebuiltRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private T FindGeneratedComponent<T>(string objectName) where T : Component
    {
        Transform found = FindGeneratedTransform(objectName);
        return found != null ? found.GetComponent<T>() : null;
    }

    private T FindSceneComponent<T>() where T : Component
    {
        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
            return null;

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }
        return null;
    }

    /// <summary>
    /// Returns the horizontal distance from a world position to the visible
    /// road centre line. The same sampled route builds the road mesh, so this
    /// remains accurate through every generated curve without using colliders.
    /// </summary>
    public bool TryGetRoadLateralDistance(Vector3 worldPosition, out float lateralDistance)
    {
        if (!built)
            BuildWorld();

        lateralDistance = 0f;
        if (routePoints.Count < 2)
            return false;

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector2 point = new(localPosition.x, localPosition.z);
        float nearestSqrDistance = float.PositiveInfinity;

        for (int index = 0; index < routePoints.Count - 1; index++)
        {
            Vector2 start = new(routePoints[index].x, routePoints[index].z);
            Vector2 end = new(routePoints[index + 1].x, routePoints[index + 1].z);
            Vector2 segment = end - start;
            float segmentLengthSqr = segment.sqrMagnitude;
            float t = segmentLengthSqr > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSqr)
                : 0f;
            float sqrDistance = (point - (start + segment * t)).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
                nearestSqrDistance = sqrDistance;
        }

        lateralDistance = Mathf.Sqrt(nearestSqrDistance);
        return true;
    }

    /// <summary>
    /// Samples the same mathematical surface used to build the visible road
    /// and surrounding terrain.  Ride code uses this instead of trying to
    /// rediscover a steep procedural course with a single physics ray.
    /// </summary>
    public bool TryGetCourseSurface(
        Vector3 worldPosition,
        out Vector3 surfacePoint,
        out Vector3 surfaceNormal,
        out Vector3 surfaceForward,
        out float signedLateralDistance)
    {
        if (!built)
            BuildWorld();

        surfacePoint = worldPosition;
        surfaceNormal = Vector3.up;
        surfaceForward = transform.forward;
        signedLateralDistance = 0f;
        if (routePoints.Count < 2)
            return false;

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector2 point = new(localPosition.x, localPosition.z);
        float nearestSqrDistance = float.PositiveInfinity;
        int nearestSegment = 0;
        float nearestT = 0f;

        for (int index = 0; index < routePoints.Count - 1; index++)
        {
            Vector2 start = new(routePoints[index].x, routePoints[index].z);
            Vector2 end = new(routePoints[index + 1].x, routePoints[index + 1].z);
            Vector2 segment = end - start;
            float segmentLengthSqr = segment.sqrMagnitude;
            float t = segmentLengthSqr > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSqr)
                : 0f;
            float sqrDistance = (point - (start + segment * t)).sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance)
                continue;

            nearestSqrDistance = sqrDistance;
            nearestSegment = index;
            nearestT = t;
        }

        Vector3 startPoint = routePoints[nearestSegment];
        Vector3 endPoint = routePoints[nearestSegment + 1];
        Vector3 routeCenter = Vector3.Lerp(startPoint, endPoint, nearestT);
        Vector3 flatForward = Vector3.ProjectOnPlane(endPoint - startPoint, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.back;
        Vector3 localRight = Vector3.Cross(Vector3.up, flatForward).normalized;
        signedLateralDistance = Vector3.Dot(localPosition - routeCenter, localRight);
        if (Mathf.Abs(signedLateralDistance) > ActiveTerrainHalfWidth)
            return false;

        float routeDistance = (nearestSegment + nearestT) * activeSampleSpacing;
        bool onRoad = Mathf.Abs(signedLateralDistance) <= ActiveRoadHalfWidth + 0.25f;
        float surfaceHeight = onRoad
            ? routeCenter.y + 0.10f
            : TerrainHeight(routeDistance, signedLateralDistance, routeCenter.y);

        Vector3 localForward;
        Vector3 localSurfaceRight;
        if (onRoad)
        {
            localForward = (endPoint - startPoint).normalized;
            localSurfaceRight = localRight;
        }
        else
        {
            const float derivativeStep = 1f;
            float beforeDistance = Mathf.Max(0f, routeDistance - derivativeStep);
            float afterDistance = Mathf.Min(activeCourseLength, routeDistance + derivativeStep);
            float beforeHeight = TerrainHeight(
                beforeDistance,
                signedLateralDistance,
                RouteCenterHeightAtDistance(beforeDistance));
            float afterHeight = TerrainHeight(
                afterDistance,
                signedLateralDistance,
                RouteCenterHeightAtDistance(afterDistance));
            localForward = (flatForward * (afterDistance - beforeDistance) +
                            Vector3.up * (afterHeight - beforeHeight)).normalized;

            float leftHeight = TerrainHeight(
                routeDistance,
                signedLateralDistance - derivativeStep,
                routeCenter.y);
            float rightHeight = TerrainHeight(
                routeDistance,
                signedLateralDistance + derivativeStep,
                routeCenter.y);
            localSurfaceRight = (localRight * (derivativeStep * 2f) +
                                 Vector3.up * (rightHeight - leftHeight)).normalized;
        }

        Vector3 localNormal = Vector3.Cross(localForward, localSurfaceRight).normalized;
        if (localNormal.y < 0f)
            localNormal = -localNormal;

        surfacePoint = transform.TransformPoint(new Vector3(localPosition.x, surfaceHeight, localPosition.z));
        surfaceNormal = transform.TransformDirection(localNormal).normalized;
        surfaceForward = transform.TransformDirection(localForward).normalized;
        return true;
    }

    private void DisableImportedPresentation()
    {
        Transform customContent = transform.Find(CustomSceneContentRootName);
        foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
        {
            if (child == transform || child.name == GeneratedWorldRootName ||
                child.name == RideTeamRootName ||
                (customContent != null && (child == customContent || child.IsChildOf(customContent))))
                continue;

            if (child.name.Equals("FX_Blizzard_V2", StringComparison.OrdinalIgnoreCase) ||
                child.name.Contains("StarDome", StringComparison.OrdinalIgnoreCase))
                child.name = "OLD_DISABLED_" + child.name;
        }

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (childIsRideTeam(renderer.transform))
                continue;
            if (customContent == null || !renderer.transform.IsChildOf(customContent))
                renderer.enabled = false;
        }
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (childIsRideTeam(collider.transform))
                continue;
            if (customContent == null || !collider.transform.IsChildOf(customContent))
                collider.enabled = false;
        }
        foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (childIsRideTeam(particles.transform) ||
                (customContent != null && particles.transform.IsChildOf(customContent)))
                continue;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.gameObject.SetActive(false);
        }
    }

    private bool childIsRideTeam(Transform candidate)
    {
        if (candidate == null)
            return false;
        Transform team = transform.Find(RideTeamRootName);
        return team != null && (candidate == team || candidate.IsChildOf(team));
    }

    private void BuildActiveRoute()
    {
        routePoints.Clear();
        usesEditableTrack = false;
        overridesTrackWidths = false;
        defaultCurveCount = 0;

        MushTrackAuthoring authoring = MushTrackAuthoring.FindFor(transform);
        if (authoring != null && authoring.TryBuildSampledRoute(
                routePoints,
                out activeCourseLength,
                out activeSampleSpacing))
        {
            usesEditableTrack = true;
            overridesTrackWidths = authoring.OverridesTrackWidths;
            authoredRoadHalfWidth = Mathf.Max(0.5f, authoring.RoadHalfWidth);
            authoredTerrainHalfWidth = Mathf.Max(authoring.TerrainHalfWidth, authoredRoadHalfWidth + 4f);
        }
        else
        {
            MushTrackPreset preset = isSharpCurve
                ? MushTrackPreset.SharpCurve
                : isSnowfield ? MushTrackPreset.Snowfield : MushTrackPreset.Forest;
            MushTrackPathUtility.BuildDefaultRoute(preset, routePoints, out defaultCurveCount);
            activeCourseLength = MushTrackPathUtility.DefaultCourseLength;
            activeSampleSpacing = MushTrackPathUtility.DefaultSampleSpacing;
        }

        if (routePoints.Count < 2)
            throw new InvalidOperationException("Track generation requires at least two route samples.");

        StartForward = Vector3.ProjectOnPlane(routePoints[1] - routePoints[0], Vector3.up).normalized;
        if (StartForward.sqrMagnitude < 0.0001f)
            StartForward = Vector3.back;
    }

    private float RouteCenterHeightAtDistance(float distance)
    {
        if (routePoints.Count == 0)
            return 0f;
        if (routePoints.Count == 1 || activeSampleSpacing <= 0.0001f)
            return routePoints[0].y;

        float routeIndex = Mathf.Clamp(distance / activeSampleSpacing, 0f, routePoints.Count - 1f);
        int startIndex = Mathf.Min(Mathf.FloorToInt(routeIndex), routePoints.Count - 2);
        return Mathf.Lerp(routePoints[startIndex].y, routePoints[startIndex + 1].y, routeIndex - startIndex);
    }

    private void BuildCourseMeshes()
    {
        Material snow = CreateLitMaterial(
            "Rebuilt Snow Terrain",
            isSnowfield ? new Color(0.84f, 0.91f, 0.98f) : new Color(0.77f, 0.87f, 0.94f),
            0.12f);
        Material road = CreateLitMaterial(
            "Rebuilt Packed Snow Road",
            isSnowfield ? new Color(0.27f, 0.39f, 0.53f) : new Color(0.23f, 0.34f, 0.43f),
            0.18f);
        Material track = CreateLitMaterial("Rebuilt Dark Sled Tracks", new Color(0.075f, 0.12f, 0.17f), 0.08f);

        GameObject terrainObject = CreateMeshObject("VISIBLE Snow Terrain", rebuiltRoot, BuildTerrainMesh(), snow, true);
        terrainRenderer = terrainObject.GetComponent<Renderer>();

        GameObject roadObject = CreateMeshObject(
            "VISIBLE Curved Packed-Snow Road",
            rebuiltRoot,
            BuildRibbonMesh(ActiveRoadHalfWidth, 0f, 0.10f),
            road,
            true);
        roadRenderer = roadObject.GetComponent<Renderer>();

        CreateMeshObject("Left Sled Track", rebuiltRoot, BuildRibbonMesh(0.10f, -1.75f, 0.145f), track, false);
        CreateMeshObject("Right Sled Track", rebuiltRoot, BuildRibbonMesh(0.10f, 1.75f, 0.145f), track, false);
    }

    private Mesh BuildRibbonMesh(float halfWidth, float lateralOffset, float yLift)
    {
        int count = routePoints.Count;
        Vector3[] vertices = new Vector3[count * 2];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[(count - 1) * 6];

        for (int index = 0; index < count; index++)
        {
            Vector3 right = RouteRight(index);
            Vector3 center = routePoints[index] + right * lateralOffset + Vector3.up * yLift;
            vertices[index * 2] = center - right * halfWidth;
            vertices[index * 2 + 1] = center + right * halfWidth;
            float v = index * activeSampleSpacing * 0.1f;
            uv[index * 2] = new Vector2(0f, v);
            uv[index * 2 + 1] = new Vector2(1f, v);

            if (index >= count - 1)
                continue;
            int triangle = index * 6;
            int vertex = index * 2;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 2;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }

        Mesh mesh = new() { name = $"Curved Ribbon {halfWidth:0.00}" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildTerrainMesh()
    {
        const int columns = 17;
        int rows = routePoints.Count;
        Vector3[] vertices = new Vector3[rows * columns];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[(rows - 1) * (columns - 1) * 6];

        for (int row = 0; row < rows; row++)
        {
            float distance = row * activeSampleSpacing;
            Vector3 right = RouteRight(row);
            for (int column = 0; column < columns; column++)
            {
                float lateral = Mathf.Lerp(-ActiveTerrainHalfWidth, ActiveTerrainHalfWidth, column / (float)(columns - 1));
                float height = TerrainHeight(distance, lateral, routePoints[row].y);
                int vertex = row * columns + column;
                vertices[vertex] = routePoints[row] + right * lateral;
                vertices[vertex].y = height;
                uv[vertex] = new Vector2(column * 0.32f, row * 0.28f);
            }
        }

        int write = 0;
        for (int row = 0; row < rows - 1; row++)
        for (int column = 0; column < columns - 1; column++)
        {
            int a = row * columns + column;
            int b = a + 1;
            int c = a + columns;
            int d = c + 1;
            triangles[write++] = a;
            triangles[write++] = c;
            triangles[write++] = b;
            triangles[write++] = b;
            triangles[write++] = c;
            triangles[write++] = d;
        }

        Mesh mesh = new() { name = "Curved Winter Terrain" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private float TerrainHeight(float distance, float lateral, float routeHeight)
    {
        float outsideRoad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ActiveRoadHalfWidth + 1.5f, 42f, Mathf.Abs(lateral)));
        float rolling = Mathf.Sin(distance * 0.019f + lateral * 0.043f) * 1.4f +
                        Mathf.Sin(distance * 0.007f - lateral * 0.085f) * 0.75f;
        float distantRise = Mathf.Pow(
            Mathf.InverseLerp(24f, ActiveTerrainHalfWidth, Mathf.Abs(lateral)),
            1.35f) * (isSharpCurve ? 15f : 8.5f);
        return routeHeight - 0.18f + outsideRoad * (0.55f + rolling + distantRise);
    }

    private Vector3 RouteTangent(int index)
    {
        int previous = Mathf.Max(0, index - 1);
        int next = Mathf.Min(routePoints.Count - 1, index + 1);
        Vector3 tangent = Vector3.ProjectOnPlane(routePoints[next] - routePoints[previous], Vector3.up);
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.back;
    }

    private Vector3 RouteRight(int index)
    {
        Vector3 right = Vector3.Cross(Vector3.up, RouteTangent(index));
        return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.left;
    }

    private void BuildScenery()
    {
        Material trunk = CreateLitMaterial("Rebuilt Tree Trunks", new Color(0.25f, 0.12f, 0.055f), 0.10f);
        Material foliage = CreateLitMaterial(
            "Rebuilt Pine Needles",
            isSnowfield ? new Color(0.055f, 0.14f, 0.16f) : new Color(0.035f, 0.17f, 0.105f),
            0.08f);
        Material rock = CreateLitMaterial("Rebuilt Snowy Rock", new Color(0.38f, 0.45f, 0.51f), 0.15f);
        Material post = CreateLitMaterial("Rebuilt Route Post", new Color(0.88f, 0.24f, 0.035f), 0.20f, new Color(1.8f, 0.25f, 0.02f));
        Material mountain = CreateLitMaterial("Rebuilt Distant Mountains", new Color(0.36f, 0.48f, 0.61f), 0.05f);

        pineMesh = BuildStackedPineMesh();
        mountainMesh = BuildMountainMesh();
        System.Random random = new(isSnowfield ? 6673 : 44191);
        int treeStep = isSnowfield ? 6 : 3;

        for (int index = 8; index < routePoints.Count - 8; index += treeStep)
        {
            float distance = index * activeSampleSpacing;
            int perSide = isSnowfield ? 1 : 2;
            for (int side = -1; side <= 1; side += 2)
            for (int layer = 0; layer < perSide; layer++)
            {
                float lateral = side * Mathf.Lerp(
                    ActiveRoadHalfWidth + 8f + layer * 11f,
                    ActiveTerrainHalfWidth - 8f,
                    (float)random.NextDouble());
                float y = TerrainHeight(distance, lateral, routePoints[index].y);
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                position.y = y;
                float scale = Mathf.Lerp(isSnowfield ? 2.4f : 3.0f, isSnowfield ? 5.8f : 7.2f, (float)random.NextDouble());
                BuildPine(position, scale, trunk, foliage, random.Next(0, 360));
            }

            if (index % (treeStep * 4) == 0)
            {
                int side = random.NextDouble() < 0.5 ? -1 : 1;
                float lateral = side * Mathf.Lerp(13f, 32f, (float)random.NextDouble());
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                position.y = TerrainHeight(distance, lateral, routePoints[index].y) + 0.4f;
                CreateMeshObject("Snow Rock", rebuiltRoot, mountainMesh, rock, false, position,
                    Quaternion.Euler(0f, random.Next(0, 360), 0f),
                    new Vector3(1.2f, 0.75f, 1.0f) * Mathf.Lerp(0.8f, 1.8f, (float)random.NextDouble()));
            }
        }

        for (int index = 10; index < routePoints.Count - 4; index += 10)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = routePoints[index] + RouteRight(index) * (side * (ActiveRoadHalfWidth + 1.15f));
                position.y += 0.62f;
                CreateCube("Visible Route Beacon", rebuiltRoot, position, new Vector3(0.12f, 1.25f, 0.12f), post,
                    Quaternion.LookRotation(RouteTangent(index), Vector3.up));
            }
        }

        for (int index = 18; index < routePoints.Count - 8; index += 18)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float lateral = side * 88f;
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                position.y = TerrainHeight(index * activeSampleSpacing, lateral, routePoints[index].y) - 1f;
                float size = Mathf.Lerp(13f, 28f, (float)random.NextDouble());
                CreateMeshObject("Distant Snow Mountain", rebuiltRoot, mountainMesh, mountain, false, position,
                    Quaternion.Euler(0f, random.Next(0, 360), 0f),
                    new Vector3(size, size * Mathf.Lerp(0.8f, 1.35f, (float)random.NextDouble()), size));
            }
        }

        if (isSnowfield)
        {
            Material cabinWall = CreateLitMaterial("Rebuilt Cabin Wall", new Color(0.25f, 0.095f, 0.04f), 0.12f);
            Material cabinRoof = CreateLitMaterial("Rebuilt Cabin Roof Snow", new Color(0.76f, 0.85f, 0.92f), 0.14f);
            for (int index = 35; index < routePoints.Count - 20; index += 55)
            {
                int side = (index / 55) % 2 == 0 ? -1 : 1;
                float lateral = side * 25f;
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                position.y = TerrainHeight(index * activeSampleSpacing, lateral, routePoints[index].y);
                BuildCabin(position, RouteTangent(index), cabinWall, cabinRoof);
            }
        }
    }

    private void BuildPine(Vector3 position, float height, Material trunk, Material foliage, int yaw)
    {
        GameObject rootObject = new("Visible Snow Pine");
        Transform root = rootObject.transform;
        root.SetParent(rebuiltRoot, false);
        root.position = position;
        root.rotation = Quaternion.Euler(0f, yaw, 0f);
        CreateCube("Trunk", root, new Vector3(0f, height * 0.22f, 0f), new Vector3(height * 0.10f, height * 0.44f, height * 0.10f), trunk, Quaternion.identity, true);
        GameObject crown = CreateMeshObject("Pine Crown", root, pineMesh, foliage, false, scale: Vector3.one * height);
        crown.transform.localPosition = new Vector3(0f, height * 0.20f, 0f);
        crown.transform.localRotation = Quaternion.identity;
    }

    private void BuildCabin(Vector3 position, Vector3 forward, Material wall, Material roof)
    {
        GameObject cabinObject = new("Visible Snow Cabin");
        Transform cabin = cabinObject.transform;
        cabin.SetParent(rebuiltRoot, false);
        cabin.position = position;
        cabin.rotation = Quaternion.LookRotation(forward, Vector3.up);
        CreateCube("Cabin Walls", cabin, new Vector3(0f, 1.35f, 0f), new Vector3(4.5f, 2.7f, 3.4f), wall, Quaternion.identity, true);
        CreateCube("Cabin Roof Left", cabin, new Vector3(-1.18f, 3.0f, 0f), new Vector3(2.9f, 0.20f, 4.0f), roof, Quaternion.Euler(0f, 0f, -28f), true);
        CreateCube("Cabin Roof Right", cabin, new Vector3(1.18f, 3.0f, 0f), new Vector3(2.9f, 0.20f, 4.0f), roof, Quaternion.Euler(0f, 0f, 28f), true);
    }

    private void BuildSkyAndLighting()
    {
        Light sun = null;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional)
                {
                    sun = light;
                    break;
                }
            }
            if (sun != null)
                break;
        }
        if (sun == null)
        {
            GameObject sunObject = new("Mush Rebuilt Sun");
            if (!Application.isPlaying && rebuiltRoot != null)
                sunObject.transform.SetParent(rebuiltRoot, false);
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.enabled = true;
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(35f, -28f, 0f);

        Shader skyShader = Shader.Find("Skybox/Procedural");
        Material sky = skyShader != null ? new Material(skyShader) { name = "Runtime Rebuilt Winter Sky" } : null;
        if (sky != null)
        {
            SetColor(sky, "_SkyTint", isSharpCurve
                ? new Color(0.50f, 0.68f, 0.90f)
                : isSnowfield ? new Color(0.52f, 0.72f, 0.93f) : new Color(0.56f, 0.74f, 0.92f));
            SetColor(sky, "_GroundColor", new Color(0.46f, 0.54f, 0.62f));
            SetFloat(sky, "_AtmosphereThickness", 0.75f);
            SetFloat(sky, "_Exposure", 1.2f);
            SetFloat(sky, "_SunSize", 0.045f);
            RenderSettings.skybox = sky;
            runtimeMaterials.Add(sky);
        }

        Renderer stars = null;
        if (!isSnowfield)
        {
            Material starMaterial = CreateUnlitMaterial("Rebuilt Stars", new Color(0.82f, 0.91f, 1f));
            GameObject starObject = CreateMeshObject("FX_StarDome_Rebuilt", rebuiltRoot, BuildStarDomeMesh(), starMaterial, false);
            stars = starObject.GetComponent<Renderer>();
            stars.enabled = false;
        }

        Camera sceneCamera = FindSceneComponent<Camera>();
        if (sceneCamera != null)
        {
            sceneCamera.clearFlags = CameraClearFlags.Skybox;
            sceneCamera.backgroundColor = isSharpCurve
                ? new Color(0.50f, 0.68f, 0.90f)
                : isSnowfield ? new Color(0.52f, 0.72f, 0.93f) : new Color(0.56f, 0.74f, 0.92f);
        }

        if (isSharpCurve)
        {
            sharpSun = sun;
            sharpCamera = sceneCamera;
            sharpSky = sky;
            sharpStars = stars;
            BuildSharpCurveSkyEffects();
            ApplySharpCurveEnvironment(0f);
        }
        else if (isSnowfield)
        {
            MushSnowfieldBlizzardController controller = GetComponent<MushSnowfieldBlizzardController>();
            if (Application.isPlaying)
                controller?.ConfigureRuntimeWorld(sun, sceneCamera, sky, null, activeCourseLength);
        }
        else
        {
            MushForestTimeCycleController controller = GetComponent<MushForestTimeCycleController>();
            if (Application.isPlaying)
                controller?.ConfigureRuntimeWorld(sun, sceneCamera, sky, stars, activeCourseLength);
        }
    }

    private void BuildAmbientSnow()
    {
        GameObject snowObject = new("FX_AmbientSnow_Rebuilt");
        snowObject.transform.SetParent(rebuiltRoot, false);
        snowObject.transform.localPosition = new Vector3(0f, 4f, -7f);
        AmbientSnowTransform = snowObject.transform;

        ParticleSystem particles = snowObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = isSnowfield ? 620 : 380;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.0f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.82f, 0.92f, 1f, 0.55f),
            new Color(1f, 1f, 1f, 0.92f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = isSnowfield ? 22f : 14f;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(28f, 12f, 30f);
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = isSnowfield ? -2.4f : -1.1f;
        velocity.y = -2.2f;
        velocity.z = -1.0f;

        ParticleSystemRenderer renderer = snowObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 1.6f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = CreateParticleMaterial();
        particles.Play();

        if (isSnowfield)
        {
            MushSnowfieldBlizzardController controller = GetComponent<MushSnowfieldBlizzardController>();
            if (Application.isPlaying)
                controller?.SetSnowParticles(particles);
        }
    }

    public void SetProgressTarget(Transform target)
    {
        sharpProgressTarget = target;
        sharpProgress = 0f;
    }

    public bool TryGetRouteProgress(Vector3 worldPosition, out float progress)
    {
        progress = 0f;
        if (routePoints.Count < 2)
            return false;

        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector2 point = new(local.x, local.z);
        float nearestSqrDistance = float.PositiveInfinity;
        float nearestProgress = 0f;
        for (int index = 0; index < routePoints.Count - 1; index++)
        {
            Vector2 start = new(routePoints[index].x, routePoints[index].z);
            Vector2 end = new(routePoints[index + 1].x, routePoints[index + 1].z);
            Vector2 segment = end - start;
            float segmentLengthSqr = segment.sqrMagnitude;
            float t = segmentLengthSqr > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSqr)
                : 0f;
            float sqrDistance = (point - (start + segment * t)).sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance)
                continue;

            nearestSqrDistance = sqrDistance;
            nearestProgress = (index + t) / (routePoints.Count - 1f);
        }

        progress = Mathf.Clamp01(nearestProgress);
        return true;
    }

    /// <summary>
    /// Returns an exact pose on the generated road centreline. Course recovery
    /// uses this instead of restoring a raw world position near an edge.
    /// </summary>
    public bool TryGetRoutePose(
        float progress,
        out Vector3 surfacePoint,
        out Vector3 surfaceNormal,
        out Vector3 surfaceForward)
    {
        if (!built)
            BuildWorld();

        surfacePoint = transform.position;
        surfaceNormal = transform.up;
        surfaceForward = transform.forward;
        if (routePoints.Count < 2)
            return false;

        float routeIndex = Mathf.Clamp01(progress) * (routePoints.Count - 1f);
        int segmentIndex = Mathf.Min(Mathf.FloorToInt(routeIndex), routePoints.Count - 2);
        float segmentT = Mathf.Clamp01(routeIndex - segmentIndex);
        Vector3 startPoint = routePoints[segmentIndex];
        Vector3 endPoint = routePoints[segmentIndex + 1];
        Vector3 routeCenter = Vector3.Lerp(startPoint, endPoint, segmentT);

        Vector3 localForward = (endPoint - startPoint).normalized;
        Vector3 flatForward = Vector3.ProjectOnPlane(localForward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.back;
        Vector3 localRight = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 localNormal = Vector3.Cross(localForward, localRight).normalized;
        if (localNormal.y < 0f)
            localNormal = -localNormal;

        surfacePoint = transform.TransformPoint(routeCenter + Vector3.up * 0.10f);
        surfaceNormal = transform.TransformDirection(localNormal).normalized;
        surfaceForward = transform.TransformDirection(localForward).normalized;
        return true;
    }

    private void Update()
    {
        if (!built || !isSharpCurve)
            return;

        if (sharpProgressTarget != null && TryGetRouteProgress(sharpProgressTarget.position, out float routeProgress))
            sharpProgress = Mathf.Max(sharpProgress, routeProgress);
        ApplySharpCurveEnvironment(sharpProgress);
    }

    private void BuildSharpCurveSkyEffects()
    {
        GameObject meteorObject = new("FX_SharpCurve_MeteorShower");
        meteorObject.transform.SetParent(rebuiltRoot, false);
        sharpMeteorShower = meteorObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule meteorMain = sharpMeteorShower.main;
        meteorMain.loop = true;
        meteorMain.playOnAwake = false;
        meteorMain.maxParticles = 96;
        // Keep every streak in the sky.  The old 1.15 second maximum combined
        // with -24 m/s vertical velocity let low box spawns travel below the
        // camera and visually spear the flat ground after the descent.
        meteorMain.startLifetime = new ParticleSystem.MinMaxCurve(0.50f, 0.78f);
        meteorMain.startSpeed = 0f;
        meteorMain.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.095f);
        meteorMain.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.70f, 0.88f, 1f, 0.92f),
            new Color(1f, 0.76f, 0.42f, 1f));
        meteorMain.simulationSpace = ParticleSystemSimulationSpace.Local;

        ParticleSystem.EmissionModule meteorEmission = sharpMeteorShower.emission;
        meteorEmission.rateOverTime = 0f;
        ParticleSystem.ShapeModule meteorShape = sharpMeteorShower.shape;
        meteorShape.shapeType = ParticleSystemShapeType.Box;
        meteorShape.scale = new Vector3(78f, 18f, 14f);
        ParticleSystem.VelocityOverLifetimeModule meteorVelocity = sharpMeteorShower.velocityOverLifetime;
        meteorVelocity.enabled = true;
        meteorVelocity.space = ParticleSystemSimulationSpace.Local;
        meteorVelocity.x = -29f;
        meteorVelocity.y = -22f;
        meteorVelocity.z = -8f;

        ParticleSystemRenderer meteorRenderer = meteorObject.GetComponent<ParticleSystemRenderer>();
        meteorRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        meteorRenderer.velocityScale = 0.12f;
        meteorRenderer.lengthScale = 4.0f;
        meteorRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meteorRenderer.receiveShadows = false;
        meteorRenderer.sharedMaterial = CreateTransparentEffectMaterial(
            "Sharp Curve Meteors",
            new Color(0.72f, 0.90f, 1f, 0.92f),
            true);
        sharpMeteorShower.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        sharpAuroraRoot = new GameObject("FX_SharpCurve_Aurora").transform;
        sharpAuroraRoot.SetParent(rebuiltRoot, false);
        GameObject auroraSky = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        auroraSky.name = "Aurora Sky Dome Renderer";
        auroraSky.transform.SetParent(sharpAuroraRoot, false);
        auroraSky.transform.localPosition = Vector3.zero;
        auroraSky.transform.localRotation = Quaternion.identity;
        auroraSky.transform.localScale = Vector3.one * 840f;
        Collider auroraCollider = auroraSky.GetComponent<Collider>();
        if (auroraCollider != null)
        {
            if (Application.isPlaying) Destroy(auroraCollider);
            else DestroyImmediate(auroraCollider);
        }

        Shader auroraShader = Resources.Load<Shader>("MushSkyAurora") ?? Shader.Find("Mush/Sky Aurora");
        if (auroraShader != null)
        {
            Material auroraMaterial = new(auroraShader) { name = "Runtime Sky-Wide Aurora" };
            auroraMaterial.SetFloat("_Visibility", 0f);
            auroraMaterial.SetFloat("_Intensity", 0.72f);
            auroraMaterial.SetFloat("_Speed", 0.18f);
            runtimeMaterials.Add(auroraMaterial);
            sharpAuroraSkyRenderer = auroraSky.GetComponent<Renderer>();
            sharpAuroraSkyRenderer.sharedMaterial = auroraMaterial;
            sharpAuroraSkyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sharpAuroraSkyRenderer.receiveShadows = false;
            sharpAuroraSkyRenderer.lightProbeUsage = LightProbeUsage.Off;
            sharpAuroraSkyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        else
        {
            Debug.LogError("[Mush] Sky aurora shader could not be loaded.", this);
            auroraSky.SetActive(false);
        }
        sharpAuroraRoot.gameObject.SetActive(false);
    }

    private void ApplySharpCurveEnvironment(float progress)
    {
        sharpCamera ??= FindSceneComponent<Camera>();
        Color daySky = new(0.50f, 0.68f, 0.90f);
        Color sunsetSky = new(0.52f, 0.22f, 0.30f);
        Color nightSky = new(0.012f, 0.025f, 0.092f);
        Color skyColor;
        if (progress < 0.30f)
        {
            float evening = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.30f, progress));
            skyColor = Color.Lerp(daySky, sunsetSky, evening);
        }
        else
        {
            float night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.50f, progress));
            skyColor = Color.Lerp(sunsetSky, nightSky, night);
        }

        float nightStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.52f, progress));
        if (sharpSun != null)
        {
            sharpSun.color = Color.Lerp(new Color(1f, 0.95f, 0.82f), new Color(0.28f, 0.38f, 0.62f), nightStrength);
            sharpSun.intensity = Mathf.Lerp(1.08f, 0.18f, nightStrength);
            sharpSun.transform.rotation = Quaternion.Euler(
                Mathf.Lerp(34f, -16f, nightStrength),
                Mathf.Lerp(-28f, -104f, nightStrength),
                0f);
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 38f;
        RenderSettings.fogEndDistance = Mathf.Lerp(280f, 190f, nightStrength);
        RenderSettings.fogColor = Color.Lerp(new Color(0.54f, 0.62f, 0.70f), new Color(0.035f, 0.055f, 0.11f), nightStrength);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.Lerp(new Color(0.58f, 0.65f, 0.72f), new Color(0.065f, 0.09f, 0.16f), nightStrength);
        if (sharpSky != null)
        {
            SetColor(sharpSky, "_SkyTint", skyColor);
            SetColor(sharpSky, "_GroundColor", skyColor * 0.42f);
            SetFloat(sharpSky, "_Exposure", Mathf.Lerp(1.2f, 0.42f, nightStrength));
            SetFloat(sharpSky, "_AtmosphereThickness", Mathf.Lerp(0.75f, 1.38f, nightStrength));
        }
        if (sharpCamera != null)
        {
            sharpCamera.clearFlags = CameraClearFlags.Skybox;
            sharpCamera.backgroundColor = skyColor;
        }

        UpdateSharpStars(nightStrength);
        UpdateMeteorShower(progress);
        UpdateAurora(progress);
    }

    private void UpdateSharpStars(float visibility)
    {
        if (sharpStars == null)
            return;

        if (sharpCamera != null)
            sharpStars.transform.position = sharpCamera.transform.position;
        sharpStars.enabled = visibility > 0.02f;
        if (!sharpStars.enabled)
            return;

        sharpEffectBlock ??= new MaterialPropertyBlock();
        Color starColor = new Color(0.70f, 0.86f, 1f) * Mathf.Lerp(0.35f, 3.2f, visibility);
        sharpStars.GetPropertyBlock(sharpEffectBlock);
        sharpEffectBlock.SetColor("_BaseColor", starColor);
        sharpEffectBlock.SetColor("_Color", starColor);
        sharpEffectBlock.SetColor("_EmissionColor", starColor);
        sharpStars.SetPropertyBlock(sharpEffectBlock);
    }

    private void UpdateMeteorShower(float progress)
    {
        if (sharpMeteorShower == null)
            return;

        // The descent ends at 620 m, but stopping there made the shower appear
        // for only a moment once the sled reached the following flat. Keep it
        // overhead through that section and cross-fade it into the aurora.
        const float meteorFadeStartProgress = 0.76f;
        const float meteorEndProgress = 0.86f;
        if (progress >= meteorEndProgress)
        {
            if (sharpMeteorShower.isPlaying || sharpMeteorShower.particleCount > 0)
                sharpMeteorShower.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.38f, progress));
        float fadeOut = 1f - Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(meteorFadeStartProgress, meteorEndProgress, progress));
        float strength = fadeIn * fadeOut;
        if (sharpCamera != null)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(sharpCamera.transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;
            sharpMeteorShower.transform.position = sharpCamera.transform.position + flatForward * 46f + Vector3.up * 38f;
            sharpMeteorShower.transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
        }

        ParticleSystem.EmissionModule emission = sharpMeteorShower.emission;
        emission.rateOverTime = 10f * strength;
        if (strength > 0.015f)
        {
            if (!sharpMeteorShower.isPlaying)
                sharpMeteorShower.Play();
        }
        else if (sharpMeteorShower.isPlaying || sharpMeteorShower.particleCount > 0)
        {
            sharpMeteorShower.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void UpdateAurora(float progress)
    {
        if (sharpAuroraRoot == null || sharpAuroraSkyRenderer == null)
            return;

        float visibility = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.83f, 0.89f, progress));
        bool visible = visibility > 0.01f;
        sharpAuroraRoot.gameObject.SetActive(visible);
        if (!visible)
            return;

        if (sharpCamera != null)
        {
            // Keep the dome centred on the viewer but fixed to world axes.
            // Looking left, right or overhead now reveals a continuous sky
            // effect instead of a billboard that follows the camera forward.
            sharpAuroraRoot.position = sharpCamera.transform.position;
            sharpAuroraRoot.rotation = Quaternion.identity;
        }

        sharpEffectBlock ??= new MaterialPropertyBlock();
        sharpAuroraSkyRenderer.GetPropertyBlock(sharpEffectBlock);
        sharpEffectBlock.SetFloat("_Visibility", visibility);
        sharpAuroraSkyRenderer.SetPropertyBlock(sharpEffectBlock);
    }

    private void PositionRouteMarkers()
    {
        Vector3 start = routePoints[0] + Vector3.up * 0.02f;
        Vector3 finish = routePoints[^1] + Vector3.up * 0.02f;
        SetOrCreateMarker("SPAWN_Sled", start, StartForward);
        SetOrCreateMarker("SPAWN_Dog_Left", start + RouteRight(0) * -0.8f + StartForward * 4f, StartForward);
        SetOrCreateMarker("SPAWN_Dog_Right", start + RouteRight(0) * 0.8f + StartForward * 4f, StartForward);
        SetOrCreateMarker("FINISH_Delivery", finish, RouteTangent(routePoints.Count - 1));
    }

    private void SetOrCreateMarker(string markerName, Vector3 position, Vector3 forward)
    {
        Transform found = null;
        Transform searchRoot = Application.isPlaying ? transform : rebuiltRoot;
        foreach (Transform child in searchRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.Equals(markerName, StringComparison.OrdinalIgnoreCase))
                continue;
            found = child;
            break;
        }
        if (found == null)
        {
            GameObject markerObject = new(markerName);
            found = markerObject.transform;
            found.SetParent(rebuiltRoot, false);
        }
        found.position = position;
        found.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up).normalized, Vector3.up);
    }

    private GameObject CreateMeshObject(
        string objectName,
        Transform parent,
        Mesh mesh,
        Material material,
        bool addCollider,
        Vector3? position = null,
        Quaternion? rotation = null,
        Vector3? scale = null)
    {
        GameObject gameObject = new(objectName);
        gameObject.transform.SetParent(parent, false);
        gameObject.transform.position = position ?? parent.position;
        gameObject.transform.rotation = rotation ?? parent.rotation;
        gameObject.transform.localScale = scale ?? Vector3.one;
        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (addCollider)
        {
            MeshCollider collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }
        return gameObject;
    }

    private static GameObject CreateCube(
        string objectName,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        Quaternion rotation,
        bool localSpace = false)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = objectName;
        cube.transform.SetParent(parent, false);
        if (localSpace)
        {
            cube.transform.localPosition = position;
            cube.transform.localRotation = rotation;
        }
        else
        {
            cube.transform.position = position;
            cube.transform.rotation = rotation;
        }
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = cube.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
        return cube;
    }

    private Material CreateLitMaterial(string materialName, Color color, float smoothness, Color? emission = null)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new(shader) { name = materialName };
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetFloat(material, "_Smoothness", smoothness);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            SetColor(material, "_EmissionColor", emission.Value);
        }
        material.enableInstancing = true;
        runtimeMaterials.Add(material);
        return material;
    }

    private Material CreateUnlitMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        Material material = new(shader) { name = materialName };
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        runtimeMaterials.Add(material);
        return material;
    }

    private Material CreateParticleMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                        Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
        Material material = new(shader) { name = "Runtime White Snow Particles" };
        SetColor(material, "_BaseColor", new Color(0.92f, 0.97f, 1f, 0.82f));
        SetColor(material, "_Color", new Color(0.92f, 0.97f, 1f, 0.82f));
        runtimeMaterials.Add(material);
        return material;
    }

    private Material CreateTransparentEffectMaterial(string materialName, Color color, bool additive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                        Shader.Find("Particles/Standard Unlit") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");
        Material material = new(shader) { name = materialName };
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetFloat(material, "_Surface", 1f);
        SetFloat(material, "_ZWrite", 0f);
        SetFloat(material, "_Cull", 0f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetFloat(material, "_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        runtimeMaterials.Add(material);
        return material;
    }

    private static void SetColor(Material material, string property, Color value)
    {
        if (material != null && material.HasProperty(property))
            material.SetColor(property, value);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material != null && material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static Mesh BuildStackedPineMesh()
    {
        List<Vector3> vertices = new();
        List<int> triangles = new();
        const int sides = 8;
        for (int tier = 0; tier < 3; tier++)
        {
            float bottom = 0.18f + tier * 0.19f;
            float top = bottom + 0.42f;
            float radius = 0.28f - tier * 0.045f;
            int tip = vertices.Count;
            vertices.Add(new Vector3(0f, top, 0f));
            int ring = vertices.Count;
            for (int side = 0; side < sides; side++)
            {
                float angle = side / (float)sides * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, bottom, Mathf.Sin(angle) * radius));
            }
            for (int side = 0; side < sides; side++)
            {
                triangles.Add(tip);
                triangles.Add(ring + (side + 1) % sides);
                triangles.Add(ring + side);
            }
        }
        Mesh mesh = new() { name = "Shared Stacked Pine" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildMountainMesh()
    {
        Vector3[] vertices =
        {
            new(-0.5f, 0f, -0.5f), new(0.5f, 0f, -0.5f), new(0.5f, 0f, 0.5f), new(-0.5f, 0f, 0.5f),
            new(0f, 1f, 0f),
        };
        int[] triangles = { 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0, 0, 1, 2, 0, 2, 3 };
        Mesh mesh = new() { name = "Shared Snow Mountain" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildStarDomeMesh()
    {
        const int starCount = 180;
        const float radius = 185f;
        System.Random random = new(73019);
        Vector3[] vertices = new Vector3[starCount * 4];
        int[] triangles = new int[starCount * 6];
        for (int index = 0; index < starCount; index++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float y = Mathf.Lerp(0.12f, 0.96f, (float)random.NextDouble());
            float horizontal = Mathf.Sqrt(1f - y * y);
            Vector3 direction = new(Mathf.Cos(angle) * horizontal, y, Mathf.Sin(angle) * horizontal);
            Vector3 center = direction * radius;
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            Vector3 up = Vector3.Cross(direction, right).normalized;
            float size = Mathf.Lerp(0.18f, 0.62f, (float)random.NextDouble());
            int vertex = index * 4;
            vertices[vertex] = center - right * size - up * size;
            vertices[vertex + 1] = center - right * size + up * size;
            vertices[vertex + 2] = center + right * size + up * size;
            vertices[vertex + 3] = center + right * size - up * size;
            int triangle = index * 6;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 1;
            triangles[triangle + 2] = vertex + 2;
            triangles[triangle + 3] = vertex;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }
        Mesh mesh = new() { name = "Rebuilt Star Dome" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
        if (pineMesh != null)
        {
            if (Application.isPlaying) Destroy(pineMesh);
            else DestroyImmediate(pineMesh);
        }
        if (mountainMesh != null)
        {
            if (Application.isPlaying) Destroy(mountainMesh);
            else DestroyImmediate(mountainMesh);
        }
    }
}

/// <summary>
/// The Quest prototype deliberately spends its GPU budget on stereo rendering,
/// weather and interaction feedback instead of realtime shadows.
/// </summary>
public static class MushShadowPerformance
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        DisableGlobalShadowQuality();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyAfterInitialSceneLoad()
    {
        DisableForLoadedScenes();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DisableForScene(scene);
    }

    public static void DisableForLoadedScenes()
    {
        DisableGlobalShadowQuality();
        for (int index = 0; index < SceneManager.sceneCount; index++)
            DisableForScene(SceneManager.GetSceneAt(index));
    }

    private static void DisableGlobalShadowQuality()
    {
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.shadowDistance = 0f;
        QualitySettings.shadowCascades = 0;
    }

    private static void DisableForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                light.shadows = LightShadows.None;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }
}

/// <summary>
/// Applies the Unity 6 SRP foveation level and opts VR cameras into automatic
/// viewport dynamic resolution. OpenXR owns the actual resolution changes.
/// </summary>
public static class MushVrRenderPerformance
{
    private const float MediumFoveationLevel = 0.5f;
    private static readonly List<XRDisplaySubsystem> Displays = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        ApplyToLoadedContent();
    }

    public static void ConfigureCamera(Camera camera)
    {
        if (camera != null)
            camera.allowDynamicResolution = true;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToLoadedContent();
    }

    private static void ApplyToLoadedContent()
    {
        Displays.Clear();
        SubsystemManager.GetSubsystems(Displays);
        foreach (XRDisplaySubsystem display in Displays)
        {
            if (display != null)
                display.foveatedRenderingLevel = MediumFoveationLevel;
        }

        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
            ConfigureCamera(camera);
    }
}
