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
    private const float TerrainHalfWidth = 105f;
    private const string CustomRoadVisualPrefix = "CUSTOM SLOT - Road - ";
    private const string CustomTerrainVisualPrefix = "CUSTOM SLOT - Terrain - ";
    public const int CurrentBakedWorldVersion = 8;
    public const string GeneratedWorldRootName = "Mush Rebuilt Curved World";
    public const string DeformedRoadRootName = "VISIBLE Deformed Snow Road Module";
    public const string CustomSceneContentRootName = "SCENE CONTENT - Add Models Here";
    public const string RideTeamRootName = "Mush Ride Team";

    private readonly List<Vector3> routePoints = new();
    private readonly List<Vector3> terrainBoundaryPoints = new();
    private readonly List<int> terrainTriangleIndices = new();
    private readonly List<Material> runtimeMaterials = new();
    [SerializeField, HideInInspector] private int bakedWorldVersion;
    private bool built;
    private bool isSnowfield;
    private bool isSharpCurve;
    private bool usesEditableTrack;
    private bool usesEditableTerrain;
    private bool overridesTrackWidths;
    private float activeCourseLength = MushTrackPathUtility.DefaultCourseLength;
    private float activeSampleSpacing = MushTrackPathUtility.DefaultSampleSpacing;
    private float authoredRoadHalfWidth;
    private float authoredTerrainHalfWidth;
    private MushTrackAuthoring activeAuthoring;
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
    public bool HasCurrentBakedWorldVersion => bakedWorldVersion == CurrentBakedWorldVersion;
    public float CurrentProgress01 => sharpProgress;
    public bool SharpDownhillSpeedBoostActive => isSharpCurve && sharpProgress >= 0.35f && sharpProgress <= 0.66f;

    private float ActiveRoadHalfWidth => overridesTrackWidths
        ? authoredRoadHalfWidth
        : RoadHalfWidth;
    private float ActiveTerrainHalfWidth => overridesTrackWidths
        ? authoredTerrainHalfWidth
        : TerrainHalfWidth;

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

        ConfigureOptionalSceneFeatures();
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
            if (activeAuthoring == null || activeAuthoring.GenerateProceduralEnvironment)
            {
                BuildScenery();
                BuildSkyAndLighting();
                BuildAmbientSnow();
            }
            PositionRouteMarkers();
        }
        else
        {
            CacheBakedWorldReferences();
            ApplyCustomCoursePresentation();
            ConfigureRuntimeEnvironmentControllers();
        }

        built = true;
        Bounds roadBounds = roadRenderer != null ? roadRenderer.bounds : default;
        Bounds terrainBounds = terrainRenderer != null ? terrainRenderer.bounds : default;
        Debug.Log(
            $"[Mush Map Rebuild] Scene={gameObject.scene.path}, " +
            $"Track={(usesEditableTrack ? "EDITABLE" : "DEFAULT")}, Length={activeCourseLength:0}m, " +
            $"Samples={routePoints.Count}, " +
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
        ConfigureOptionalSceneFeatures();
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
        if (activeAuthoring == null || activeAuthoring.GenerateProceduralEnvironment)
        {
            BuildScenery();
            BuildSkyAndLighting();
            BuildAmbientSnow();
        }
        PositionRouteMarkers();
        bakedWorldVersion = CurrentBakedWorldVersion;
        built = true;
    }

    /// <summary>
    /// Rebuilds only the drivable surface while an artist edits the route.
    /// Procedural scenery is intentionally left in place until the artist
    /// explicitly requests a full scenery layout rebuild.
    /// </summary>
    public void RebuildSceneCourseGeometry()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("A baked Mush course can only be rebuilt outside play mode.");

        built = false;
        ConfigureOptionalSceneFeatures();
        BuildActiveRoute();
        rebuiltRoot = transform.Find(GeneratedWorldRootName);
        if (rebuiltRoot == null)
        {
            RebuildSceneWorld();
            return;
        }

        Material existingTrack = FindGeneratedComponent<Renderer>("Left Sled Track")?.sharedMaterial;
        DestroyGeneratedCourseObject("VISIBLE Snow Terrain");
        DestroyGeneratedCourseObject("VISIBLE Curved Packed-Snow Road");
        DestroyGeneratedCourseObject("Left Sled Track");
        DestroyGeneratedCourseObject("Right Sled Track");
        DestroyGeneratedCourseObject(DeformedRoadRootName);
        terrainRenderer = null;
        roadRenderer = null;

        BuildCourseMeshes(existingTrack);
        PositionRouteMarkers();
        bakedWorldVersion = CurrentBakedWorldVersion;
        built = true;
    }

    private void DestroyGeneratedCourseObject(string objectName)
    {
        Transform courseObject = rebuiltRoot.Find(objectName);
        if (courseObject != null)
            DestroyImmediate(courseObject.gameObject);
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

    private void ConfigureOptionalSceneFeatures()
    {
        // These flags preserve optional effects in the existing gameplay
        // scenes. They never participate in creating, identifying, or opening
        // a map; authoring has no map category.
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

        float routeDistance = (nearestSegment + nearestT) * activeSampleSpacing;
        bool onRoad = Mathf.Abs(signedLateralDistance) <= ActiveRoadHalfWidth + 0.25f;
        float surfaceHeight;
        Vector3 authoredTerrainNormal = Vector3.up;
        if (onRoad)
        {
            surfaceHeight = routeCenter.y + 0.10f;
        }
        else if (usesEditableTerrain)
        {
            if (!TryGetEditableTerrainSurface(point, out surfaceHeight, out authoredTerrainNormal))
                return false;
        }
        else
        {
            if (Mathf.Abs(signedLateralDistance) > ActiveTerrainHalfWidth)
                return false;
            surfaceHeight = TerrainHeight(routeDistance, signedLateralDistance, routeCenter.y);
        }

        Vector3 localForward;
        Vector3 localSurfaceRight;
        Vector3 localNormal;
        if (onRoad)
        {
            localForward = (endPoint - startPoint).normalized;
            localSurfaceRight = localRight;
            localNormal = Vector3.Cross(localForward, localSurfaceRight).normalized;
        }
        else if (usesEditableTerrain)
        {
            localNormal = authoredTerrainNormal;
            localForward = Vector3.ProjectOnPlane(flatForward, localNormal).normalized;
            if (localForward.sqrMagnitude < 0.0001f)
                localForward = flatForward;
            localSurfaceRight = Vector3.Cross(localNormal, localForward).normalized;
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
            localNormal = Vector3.Cross(localForward, localSurfaceRight).normalized;
        }

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
            if (childIsRideTeam(renderer.transform) || IsAssignedSceneVisual(renderer.transform))
                continue;
            if (customContent == null || !renderer.transform.IsChildOf(customContent))
                renderer.enabled = false;
        }
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (childIsRideTeam(collider.transform) || IsAssignedSceneVisual(collider.transform))
                continue;
            if (customContent == null || !collider.transform.IsChildOf(customContent))
                collider.enabled = false;
        }
        foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (childIsRideTeam(particles.transform) ||
                IsAssignedSceneVisual(particles.transform) ||
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

    private bool IsAssignedSceneVisual(Transform candidate)
    {
        if (candidate == null || activeAuthoring == null)
            return false;

        return IsSameOrChild(candidate, activeAuthoring.CustomRoadVisual) ||
               IsSameOrChild(candidate, activeAuthoring.CustomTerrainVisual);
    }

    private static bool IsSameOrChild(Transform candidate, GameObject root)
    {
        if (root == null || !root.scene.IsValid())
            return false;
        return candidate == root.transform || candidate.IsChildOf(root.transform);
    }

    private void BuildActiveRoute()
    {
        routePoints.Clear();
        terrainBoundaryPoints.Clear();
        terrainTriangleIndices.Clear();
        usesEditableTrack = false;
        usesEditableTerrain = false;
        overridesTrackWidths = false;

        activeAuthoring = MushTrackAuthoring.FindFor(transform);
        if (activeAuthoring != null)
        {
            overridesTrackWidths = activeAuthoring.OverridesTrackWidths;
            authoredRoadHalfWidth = Mathf.Max(0.5f, activeAuthoring.RoadHalfWidth);
            authoredTerrainHalfWidth = Mathf.Max(activeAuthoring.TerrainHalfWidth, authoredRoadHalfWidth + 4f);
        }

        if (activeAuthoring != null && activeAuthoring.TryBuildSampledRoute(
                routePoints,
                out activeCourseLength,
                out activeSampleSpacing))
        {
            usesEditableTrack = true;
        }
        else
        {
            MushTrackPathUtility.BuildDefaultRoute(routePoints);
            activeCourseLength = MushTrackPathUtility.DefaultCourseLength;
            activeSampleSpacing = MushTrackPathUtility.DefaultSampleSpacing;
        }

        if (routePoints.Count < 2)
            throw new InvalidOperationException("Track generation requires at least two route samples.");

        if (activeAuthoring != null &&
            activeAuthoring.TryCopyTerrainBoundary(terrainBoundaryPoints))
        {
            NormalizeTerrainBoundaryWinding(terrainBoundaryPoints);
            usesEditableTerrain = TryTriangulateTerrainBoundary(
                terrainBoundaryPoints,
                terrainTriangleIndices);
            if (!usesEditableTerrain)
            {
                terrainBoundaryPoints.Clear();
                terrainTriangleIndices.Clear();
                Debug.LogWarning(
                    "[Mush] 편집 지형 경계가 서로 교차하거나 면적이 없습니다. 기본 지형으로 표시합니다.",
                    this);
            }
        }

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

    private void BuildCourseMeshes(Material existingTrack = null)
    {
        Material snow = CreateCourseMaterial(
            activeAuthoring != null ? activeAuthoring.TerrainMaterialOverride : null,
            activeAuthoring != null ? activeAuthoring.TerrainTextureOverride : null,
            "Rebuilt Default Terrain",
            new Color(0.78f, 0.88f, 0.96f),
            0.12f);
        Material road = CreateCourseMaterial(
            activeAuthoring != null ? activeAuthoring.RoadMaterialOverride : null,
            activeAuthoring != null ? activeAuthoring.RoadTextureOverride : null,
            "Rebuilt Default Road",
            new Color(0.27f, 0.39f, 0.53f),
            0.18f);
        Material track = existingTrack != null
            ? existingTrack
            : CreateLitMaterial(
                "Rebuilt Dark Sled Tracks",
                new Color(0.075f, 0.12f, 0.17f),
                0.08f);

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
        if (activeAuthoring != null && activeAuthoring.UsesDeformableRoadModule)
            BuildDeformedRoadModule(activeAuthoring.DeformableRoadModule);
        ApplyCustomCoursePresentation();
    }

    private void ApplyCustomCoursePresentation()
    {
        Transform deformedRoadRoot = rebuiltRoot != null ? rebuiltRoot.Find(DeformedRoadRootName) : null;
        bool hasDeformedRoad = deformedRoadRoot != null &&
                               activeAuthoring != null &&
                               activeAuthoring.UsesDeformableRoadModule;
        GameObject customRoad = ResolveCustomVisual(
            activeAuthoring != null ? activeAuthoring.CustomRoadVisual : null,
            CustomRoadVisualPrefix);
        GameObject customTerrain = ResolveCustomVisual(
            activeAuthoring != null ? activeAuthoring.CustomTerrainVisual : null,
            CustomTerrainVisualPrefix);
        bool hasCustomRoad = customRoad != null;
        bool hasCustomTerrain = customTerrain != null;

        // 고정형 커스텀 도로가 선택되어 있으면 그 모델만 보여주고,
        // None으로 돌아오면 경로 변형 도로 모듈이 있을 때는 그 도로를 다시 보여줍니다.
        bool showDeformedRoad = hasDeformedRoad && !hasCustomRoad;
        bool showGeneratedRoad = activeAuthoring == null || (!hasCustomRoad && !hasDeformedRoad);
        bool showGeneratedTerrain = activeAuthoring == null || !hasCustomTerrain;

        if (roadRenderer != null)
            roadRenderer.enabled = showGeneratedRoad;
        if (terrainRenderer != null)
            terrainRenderer.enabled = showGeneratedTerrain;

        Renderer leftTrackRenderer = FindGeneratedComponent<Renderer>("Left Sled Track");
        Renderer rightTrackRenderer = FindGeneratedComponent<Renderer>("Right Sled Track");
        if (leftTrackRenderer != null)
            leftTrackRenderer.enabled = showGeneratedRoad;
        if (rightTrackRenderer != null)
            rightTrackRenderer.enabled = showGeneratedRoad;

        // 경로 변형 도로 루트도 표시 상태를 직접 관리해야 고정형 도로와 겹쳐 보이지 않습니다.
        SetVisualRenderersEnabled(deformedRoadRoot, showDeformedRoad);

        // 현재 슬롯에 지정된 씬 모델은 재빌드 후에도 반드시 다시 보이게 합니다.
        // 슬롯을 None으로 바꿨을 때 이전 모델을 숨기는 처리는 Editor 쪽에서 이전 참조를 기억해 처리합니다.
        if (activeAuthoring != null)
        {
            SetVisualRenderersEnabled(customRoad, hasCustomRoad);
            SetVisualRenderersEnabled(customTerrain, hasCustomTerrain);
        }
    }

    private GameObject ResolveCustomVisual(GameObject source, string generatedPrefix)
    {
        Transform customContent = transform.Find(CustomSceneContentRootName);
        if (customContent == null)
        {
            GameObject customContentObject = new(CustomSceneContentRootName);
            customContent = customContentObject.transform;
            customContent.SetParent(transform, false);
        }

        if (source != null && source.scene.IsValid())
        {
            RemoveGeneratedVisualInstances(customContent, generatedPrefix, null);
            return source;
        }

        string expectedName = source != null ? generatedPrefix + source.name : null;
        GameObject reusable = RemoveGeneratedVisualInstances(customContent, generatedPrefix, expectedName);
        if (source == null)
            return null;
        if (reusable != null)
            return reusable;

        GameObject instance = Instantiate(source, customContent, false);
        instance.name = expectedName;
        return instance;
    }

    private static GameObject RemoveGeneratedVisualInstances(
        Transform customContent,
        string generatedPrefix,
        string keepName)
    {
        GameObject keep = null;
        for (int index = customContent.childCount - 1; index >= 0; index--)
        {
            Transform child = customContent.GetChild(index);
            if (!child.name.StartsWith(generatedPrefix, StringComparison.Ordinal))
                continue;
            if (keep == null && !string.IsNullOrEmpty(keepName) && child.name == keepName)
            {
                keep = child.gameObject;
                continue;
            }

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
        return keep;
    }

    private Material CreateCourseMaterial(
        Material materialOverride,
        Texture textureOverride,
        string materialName,
        Color fallbackColor,
        float smoothness)
    {
        if (materialOverride != null && textureOverride == null)
            return materialOverride;

        Material material;
        if (materialOverride != null)
        {
            material = new Material(materialOverride) { name = materialName };
            runtimeMaterials.Add(material);
        }
        else
        {
            material = CreateLitMaterial(materialName, fallbackColor, smoothness);
        }

        if (textureOverride != null)
        {
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", textureOverride);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", textureOverride);
        }
        return material;
    }

    private static void SetVisualRenderersEnabled(Transform visualRoot, bool enabled)
    {
        if (visualRoot == null)
            return;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
            renderers[index].enabled = enabled;
    }

    private static void SetVisualRenderersEnabled(GameObject visualRoot, bool enabled)
    {
        if (visualRoot == null)
            return;

        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
            renderers[index].enabled = enabled;

        Terrain[] terrains = visualRoot.GetComponentsInChildren<Terrain>(true);
        for (int index = 0; index < terrains.Length; index++)
            terrains[index].enabled = enabled;
    }

    private readonly struct RoadModuleGeometry
    {
        public readonly float MinZ;
        public readonly float MaxZ;
        public readonly float RoadCenterX;
        public readonly float RoadHalfWidth;
        public readonly float OuterHalfWidth;
        public readonly float SourceBaseY;

        public RoadModuleGeometry(
            float minZ,
            float maxZ,
            float roadCenterX,
            float roadHalfWidth,
            float outerHalfWidth,
            float sourceBaseY)
        {
            MinZ = minZ;
            MaxZ = maxZ;
            RoadCenterX = roadCenterX;
            RoadHalfWidth = roadHalfWidth;
            OuterHalfWidth = outerHalfWidth;
            SourceBaseY = sourceBaseY;
        }

        public float Length => MaxZ - MinZ;
    }

    private readonly struct RoadGridVertex
    {
        public readonly Vector3 Position;
        public readonly Vector2 Uv;

        public RoadGridVertex(Vector3 position, Vector2 uv)
        {
            Position = position;
            Uv = uv;
        }
    }

    private void BuildDeformedRoadModule(GameObject sourceRoot)
    {
        MeshFilter[] sourceFilters = sourceRoot.GetComponentsInChildren<MeshFilter>(true);
        if (sourceFilters.Length == 0 || !TryMeasureRoadModule(sourceRoot, sourceFilters, out RoadModuleGeometry geometry))
        {
            Debug.LogWarning($"[Mush] '{sourceRoot.name}' does not contain a usable deformable road mesh.", activeAuthoring);
            return;
        }

        GameObject visualRootObject = new(DeformedRoadRootName);
        Transform visualRoot = visualRootObject.transform;
        visualRoot.SetParent(rebuiltRoot, false);
        Dictionary<Material, Material> generatedMaterials = new();
        int generatedMeshCount = 0;

        Material sourceSnowField = FindSourceMaterial(sourceFilters, "SnowField");
        if (sourceSnowField != null && activeAuthoring.TerrainMaterialOverride == null)
        {
            Material generatedSnowField = CreateRoadModuleMaterial(sourceSnowField);
            generatedMaterials[sourceSnowField] = generatedSnowField;
            if (terrainRenderer != null)
                terrainRenderer.sharedMaterial = generatedSnowField;
        }

        for (int index = 0; index < sourceFilters.Length; index++)
        {
            MeshFilter sourceFilter = sourceFilters[index];
            if (sourceFilter.name.Contains("Terrain", StringComparison.OrdinalIgnoreCase))
                continue;

            Mesh sourceMesh = sourceFilter.sharedMesh;
            if (sourceMesh == null || !sourceMesh.isReadable || sourceMesh.vertexCount == 0)
            {
                Debug.LogWarning(
                    $"[Mush] Road module mesh '{sourceFilter.name}' is not readable and was skipped. Enable Read/Write on the FBX importer.",
                    activeAuthoring);
                continue;
            }

            Mesh deformedMesh = BuildDeformedModuleMesh(sourceRoot.transform, sourceFilter, geometry);
            if (deformedMesh == null)
                continue;

            Material[] materials = BuildRoadModuleMaterials(
                sourceFilter.GetComponent<Renderer>(),
                deformedMesh.subMeshCount,
                generatedMaterials);
            GameObject meshObject = CreateMeshObject(
                $"Deformed {sourceFilter.name}",
                visualRoot,
                deformedMesh,
                materials[0],
                false);
            MeshRenderer meshRenderer = meshObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = materials;
            meshRenderer.receiveShadows = true;
            generatedMeshCount++;
        }

        if (generatedMeshCount > 0)
            return;

        DestroyImmediate(visualRootObject);
    }

    private static bool TryMeasureRoadModule(
        GameObject sourceRoot,
        IReadOnlyList<MeshFilter> sourceFilters,
        out RoadModuleGeometry geometry)
    {
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        float outerHalfWidth = 0f;
        float roadMinX = float.PositiveInfinity;
        float roadMaxX = float.NegativeInfinity;
        float roadMinY = float.PositiveInfinity;
        float roadMaxY = float.NegativeInfinity;
        bool foundRoad = false;

        for (int filterIndex = 0; filterIndex < sourceFilters.Count; filterIndex++)
        {
            MeshFilter filter = sourceFilters[filterIndex];
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable)
                continue;

            bool isRoad = filter.name.Contains("SnowRoad", StringComparison.OrdinalIgnoreCase) &&
                          !filter.name.Contains("Terrain", StringComparison.OrdinalIgnoreCase);
            Matrix4x4 sourceToModule = sourceRoot.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                Vector3 point = sourceToModule.MultiplyPoint3x4(vertices[vertexIndex]);
                minZ = Mathf.Min(minZ, point.z);
                maxZ = Mathf.Max(maxZ, point.z);
                outerHalfWidth = Mathf.Max(outerHalfWidth, Mathf.Abs(point.x));
                if (!isRoad)
                    continue;

                foundRoad = true;
                roadMinX = Mathf.Min(roadMinX, point.x);
                roadMaxX = Mathf.Max(roadMaxX, point.x);
                roadMinY = Mathf.Min(roadMinY, point.y);
                roadMaxY = Mathf.Max(roadMaxY, point.y);
            }
        }

        float length = maxZ - minZ;
        float roadWidth = roadMaxX - roadMinX;
        if (!foundRoad || length < 0.1f || roadWidth < 0.1f)
        {
            geometry = default;
            return false;
        }

        float roadCenterX = (roadMinX + roadMaxX) * 0.5f;
        geometry = new RoadModuleGeometry(
            minZ,
            maxZ,
            roadCenterX,
            roadWidth * 0.5f,
            Mathf.Max(roadWidth * 0.5f, outerHalfWidth),
            (roadMinY + roadMaxY) * 0.5f);
        return true;
    }

    private Mesh BuildDeformedModuleMesh(
        Transform sourceRoot,
        MeshFilter sourceFilter,
        RoadModuleGeometry geometry)
    {
        Mesh sourceMesh = sourceFilter.sharedMesh;
        Vector3[] sourceVertices = sourceMesh.vertices;
        Vector2[] sourceUv = sourceMesh.uv;
        Matrix4x4 sourceToModule = sourceRoot.worldToLocalMatrix * sourceFilter.transform.localToWorldMatrix;
        Dictionary<int, List<RoadGridVertex>> rowsByZ = new();
        for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
        {
            Vector3 point = sourceToModule.MultiplyPoint3x4(sourceVertices[vertexIndex]);
            int rowKey = Mathf.RoundToInt(point.z * 1000f);
            if (!rowsByZ.TryGetValue(rowKey, out List<RoadGridVertex> row))
            {
                row = new List<RoadGridVertex>();
                rowsByZ.Add(rowKey, row);
            }
            row.Add(new RoadGridVertex(
                point,
                sourceUv.Length == sourceVertices.Length ? sourceUv[vertexIndex] : Vector2.zero));
        }

        List<int> rowKeys = new(rowsByZ.Keys);
        rowKeys.Sort((left, right) => right.CompareTo(left));
        if (rowKeys.Count < 2)
            return null;

        int columnCount = rowsByZ[rowKeys[0]].Count;
        if (columnCount < 2)
            return null;
        for (int rowIndex = 0; rowIndex < rowKeys.Count; rowIndex++)
        {
            List<RoadGridVertex> row = rowsByZ[rowKeys[rowIndex]];
            if (row.Count != columnCount)
            {
                Debug.LogWarning(
                    $"[Mush] Road module '{sourceFilter.name}' must use a regular longitudinal grid.",
                    activeAuthoring);
                return null;
            }
            row.Sort((left, right) => left.Position.x.CompareTo(right.Position.x));
        }

        // 반복 모듈의 첫 행과 마지막 행은 같은 경계 위치에서 만나므로,
        // 두 단면의 높이를 평균낸 공통 경계 단면을 만들어 반복 사이에 틈이나 가로 홈이 생기지 않게 합니다.
        List<RoadGridVertex> moduleStartRow = rowsByZ[rowKeys[0]];
        List<RoadGridVertex> moduleEndRow = rowsByZ[rowKeys[^1]];

        float moduleLength = geometry.Length;
        int repeatCount = Mathf.Max(1, Mathf.CeilToInt(activeCourseLength / moduleLength));
        int sourceVertexCount = rowKeys.Count * columnCount;
        int outputVertexCount = sourceVertexCount * repeatCount;
        Vector3[] outputVertices = new Vector3[outputVertexCount];
        Vector2[] outputUv = new Vector2[outputVertexCount];
        float lateralScale = ActiveRoadHalfWidth / geometry.RoadHalfWidth;

        for (int repeat = 0; repeat < repeatCount; repeat++)
        {
            float repeatStartDistance = repeat * moduleLength;
            int vertexOffset = repeat * sourceVertexCount;
            for (int rowIndex = 0; rowIndex < rowKeys.Count; rowIndex++)
            {
                List<RoadGridVertex> sourceRow = rowsByZ[rowKeys[rowIndex]];
                for (int column = 0; column < columnCount; column++)
                {
                    RoadGridVertex sourceVertex = sourceRow[column];
                    float localDistance = geometry.MaxZ - sourceVertex.Position.z;
                    float distance = Mathf.Min(activeCourseLength, repeatStartDistance + localDistance);
                    EvaluateRouteFrame(distance, out Vector3 routeCenter, out Vector3 routeRight);

                    float lateral = (sourceVertex.Position.x - geometry.RoadCenterX) * lateralScale;
                    Vector3 deformedPoint = routeCenter + routeRight * lateral;

                    // 예전 코드는 모듈 경계 0.75m 안쪽의 높낮이를 0으로 눌러서
                    // 10m 반복 경계마다 도로를 가로지르는 평평한 홈/검은 선이 생길 수 있었습니다.
                    float sourceHeightOffset = sourceVertex.Position.y - geometry.SourceBaseY;
                    float sharedSeamSourceY =
                        (moduleStartRow[column].Position.y + moduleEndRow[column].Position.y) * 0.5f;
                    float sharedSeamHeightOffset = sharedSeamSourceY - geometry.SourceBaseY;
                    float seamDistance = Mathf.Min(localDistance, moduleLength - localDistance);
                    float seamBlend = 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(0f, 0.75f, seamDistance));
                    float continuousHeightOffset = Mathf.Lerp(
                        sourceHeightOffset,
                        sharedSeamHeightOffset,
                        seamBlend);

                    // 경계에서는 시작/끝 단면이 같은 높이를 공유하고, 경계에서 멀어질수록 원본 모델의 높낮이로 자연스럽게 돌아갑니다.
                    deformedPoint.y = routeCenter.y + 0.10f + continuousHeightOffset * 0.42f;

                    int outputIndex = vertexOffset + rowIndex * columnCount + column;
                    outputVertices[outputIndex] = deformedPoint;
                    outputUv[outputIndex] = sourceVertex.Uv;
                }
            }
        }

        Mesh outputMesh = new() { name = $"Spline Deformed {sourceMesh.name}" };
        outputMesh.indexFormat = IndexFormat.UInt32;
        outputMesh.vertices = outputVertices;
        outputMesh.uv = outputUv;
        List<int>[] subMeshIndices = { new(), new(), new() };
        for (int repeat = 0; repeat < repeatCount; repeat++)
        {
            int vertexOffset = repeat * sourceVertexCount;
            float repeatStartDistance = repeat * moduleLength;
            for (int rowIndex = 0; rowIndex < rowKeys.Count - 1; rowIndex++)
            {
                List<RoadGridVertex> row = rowsByZ[rowKeys[rowIndex]];
                float rowDistance = repeatStartDistance + geometry.MaxZ - row[0].Position.z;
                if (rowDistance >= activeCourseLength)
                    continue;

                for (int column = 0; column < columnCount - 1; column++)
                {
                    float sourceCellCenterX =
                        ((row[column].Position.x + row[column + 1].Position.x) * 0.5f) -
                        geometry.RoadCenterX;
                    float normalizedLateral = Mathf.Abs(sourceCellCenterX) / geometry.RoadHalfWidth;
                    int materialIndex = normalizedLateral >= 0.75f
                        ? 1
                        : normalizedLateral >= 0.30f && normalizedLateral < 0.45f ? 2 : 0;

                    int a = vertexOffset + rowIndex * columnCount + column;
                    int b = a + 1;
                    int c = a + columnCount;
                    int d = c + 1;
                    List<int> triangles = subMeshIndices[materialIndex];
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }
        }

        outputMesh.subMeshCount = subMeshIndices.Length;
        for (int subMesh = 0; subMesh < subMeshIndices.Length; subMesh++)
            outputMesh.SetTriangles(subMeshIndices[subMesh], subMesh, false);
        outputMesh.RecalculateNormals();
        Vector3[] outputNormals = outputMesh.normals;
        int lastRowOffset = (rowKeys.Count - 1) * columnCount;
        for (int repeat = 1; repeat < repeatCount; repeat++)
        {
            int previousBoundary = (repeat - 1) * sourceVertexCount + lastRowOffset;
            int nextBoundary = repeat * sourceVertexCount;
            for (int column = 0; column < columnCount; column++)
            {
                Vector3 averagedNormal =
                    (outputNormals[previousBoundary + column] + outputNormals[nextBoundary + column]).normalized;
                outputNormals[previousBoundary + column] = averagedNormal;
                outputNormals[nextBoundary + column] = averagedNormal;
            }
        }
        outputMesh.normals = outputNormals;
        outputMesh.RecalculateTangents();
        outputMesh.RecalculateBounds();
        return outputMesh;
    }

    private static Material FindSourceMaterial(
        IReadOnlyList<MeshFilter> sourceFilters,
        string namePart)
    {
        for (int filterIndex = 0; filterIndex < sourceFilters.Count; filterIndex++)
        {
            Renderer renderer = sourceFilters[filterIndex].GetComponent<Renderer>();
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material != null && material.name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                    return material;
            }
        }
        return null;
    }

    private void EvaluateRouteFrame(float distance, out Vector3 center, out Vector3 right)
    {
        // 트랙은 일정 간격으로 샘플된 점 목록이지만, 그 점 사이를 단순 직선으로 보간하면
        // 각 샘플 경계마다 진행 방향이 갑자기 바뀌어 커브에서 도로를 가로지르는 꺾임선이 보일 수 있습니다.
        float routeIndex = Mathf.Clamp(distance / activeSampleSpacing, 0f, routePoints.Count - 1f);
        int startIndex = Mathf.Min(Mathf.FloorToInt(routeIndex), routePoints.Count - 2);
        float interpolation = routeIndex - startIndex;

        // 현재 구간 앞뒤의 점까지 함께 사용해 중심 위치 자체를 부드러운 Catmull-Rom 곡선으로 계산합니다.
        // 끝점에서는 가장 가까운 점을 반복해서 사용하므로 트랙의 시작과 끝도 안전하게 처리됩니다.
        int point0Index = Mathf.Max(0, startIndex - 1);
        int point1Index = startIndex;
        int point2Index = Mathf.Min(routePoints.Count - 1, startIndex + 1);
        int point3Index = Mathf.Min(routePoints.Count - 1, startIndex + 2);

        Vector3 point0 = routePoints[point0Index];
        Vector3 point1 = routePoints[point1Index];
        Vector3 point2 = routePoints[point2Index];
        Vector3 point3 = routePoints[point3Index];

        center = EvaluateUniformCatmullRom(point0, point1, point2, point3, interpolation);

        // 같은 곡선의 미분값을 진행 방향으로 사용합니다.
        // 이전처럼 4m 샘플 한 구간마다 고정된 tangent를 쓰지 않으므로 커브의 단면 방향도 연속적으로 회전합니다.
        Vector3 tangent = EvaluateUniformCatmullRomTangent(
            point0,
            point1,
            point2,
            point3,
            interpolation);
        Vector3 flatTangent = Vector3.ProjectOnPlane(tangent, Vector3.up);

        // 극단적으로 가까운 포인트 때문에 미분값이 거의 0이 되면 인접 샘플 방향을 안전한 대체값으로 사용합니다.
        if (flatTangent.sqrMagnitude < 0.0001f)
            flatTangent = Vector3.ProjectOnPlane(point2 - point1, Vector3.up);
        if (flatTangent.sqrMagnitude < 0.0001f)
            flatTangent = Vector3.back;

        // 진행 방향의 직각 벡터를 도로의 좌우 방향으로 사용합니다.
        right = Vector3.Cross(Vector3.up, flatTangent.normalized).normalized;
    }

    private static Vector3 EvaluateUniformCatmullRom(
        Vector3 point0,
        Vector3 point1,
        Vector3 point2,
        Vector3 point3,
        float interpolation)
    {
        // 0~1 범위로 제한해 현재 두 샘플 사이만 평가하고, 과도한 외삽이 생기지 않게 합니다.
        float t = Mathf.Clamp01(interpolation);
        float tSquared = t * t;
        float tCubed = tSquared * t;

        // 균일 Catmull-Rom 공식으로 point1에서 point2까지 부드럽게 이어지는 위치를 계산합니다.
        return 0.5f *
               ((2f * point1) +
                (-point0 + point2) * t +
                (2f * point0 - 5f * point1 + 4f * point2 - point3) * tSquared +
                (-point0 + 3f * point1 - 3f * point2 + point3) * tCubed);
    }

    private static Vector3 EvaluateUniformCatmullRomTangent(
        Vector3 point0,
        Vector3 point1,
        Vector3 point2,
        Vector3 point3,
        float interpolation)
    {
        // 위 Catmull-Rom 위치식의 1차 미분값으로, 커브를 따라 연속적으로 변하는 진행 방향을 얻습니다.
        float t = Mathf.Clamp01(interpolation);
        float tSquared = t * t;

        return 0.5f *
               ((-point0 + point2) +
                2f * (2f * point0 - 5f * point1 + 4f * point2 - point3) * t +
                3f * (-point0 + 3f * point1 - 3f * point2 + point3) * tSquared);
    }

    private Material[] BuildRoadModuleMaterials(
        Renderer sourceRenderer,
        int subMeshCount,
        IDictionary<Material, Material> cache)
    {
        Material[] sourceMaterials = sourceRenderer != null ? sourceRenderer.sharedMaterials : Array.Empty<Material>();
        Material[] materials = new Material[Mathf.Max(1, subMeshCount)];
        string[] expectedNames = { "PackedSnow", "SnowField", "SledTrack" };
        for (int index = 0; index < materials.Length; index++)
        {
            Material source = index < expectedNames.Length
                ? FindNamedMaterial(sourceMaterials, expectedNames[index])
                : null;
            if (source == null && sourceMaterials.Length > 0)
                source = sourceMaterials[Mathf.Min(index, sourceMaterials.Length - 1)];
            if (source != null && cache.TryGetValue(source, out Material cached))
            {
                materials[index] = cached;
                continue;
            }

            Material generated = CreateRoadModuleMaterial(source);
            materials[index] = generated;
            if (source != null)
                cache[source] = generated;
        }
        return materials;
    }

    private static Material FindNamedMaterial(IReadOnlyList<Material> materials, string namePart)
    {
        for (int index = 0; index < materials.Count; index++)
        {
            Material material = materials[index];
            if (material != null && material.name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                return material;
        }
        return null;
    }

    private Material CreateRoadModuleMaterial(Material source)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new MissingReferenceException("Universal Render Pipeline/Lit shader is required for the Mush road module.");

        string sourceName = source != null ? source.name : "Fallback";
        Material material = new(shader) { name = $"SnowRoad {sourceName}" };
        Texture albedo = FindMaterialTexture(source, "_BaseMap", "_MainTex");
        Texture normal = FindMaterialTexture(source, "_BumpMap", "_NormalMap");
        if (albedo != null)
        {
            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_MainTex", albedo);
        }
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }

        float smoothness = sourceName.Contains("SledTrack", StringComparison.OrdinalIgnoreCase)
            ? 0.18f
            : sourceName.Contains("SnowField", StringComparison.OrdinalIgnoreCase) ? 0.30f : 0.24f;
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Smoothness", smoothness);
        material.enableInstancing = true;
        runtimeMaterials.Add(material);
        return material;
    }

    private static Texture FindMaterialTexture(Material material, params string[] propertyNames)
    {
        if (material == null)
            return null;

        for (int index = 0; index < propertyNames.Length; index++)
        {
            string propertyName = propertyNames[index];
            if (!material.HasProperty(propertyName))
                continue;
            Texture texture = material.GetTexture(propertyName);
            if (texture != null)
                return texture;
        }
        return null;
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

    private static void NormalizeTerrainBoundaryWinding(List<Vector3> points)
    {
        for (int index = points.Count - 1; index >= 0; index--)
        {
            int previous = (index - 1 + points.Count) % points.Count;
            Vector2 point = new(points[index].x, points[index].z);
            Vector2 previousPoint = new(points[previous].x, points[previous].z);
            if (points.Count > 3 && (point - previousPoint).sqrMagnitude < 0.0001f)
                points.RemoveAt(index);
        }

        float signedAreaTwice = 0f;
        for (int index = 0; index < points.Count; index++)
        {
            Vector3 current = points[index];
            Vector3 next = points[(index + 1) % points.Count];
            signedAreaTwice += current.x * next.z - current.z * next.x;
        }

        // XZ 좌표의 시계 방향 정점은 Unity의 위쪽(+Y) 노멀을 만듭니다.
        if (signedAreaTwice > 0f)
            points.Reverse();
    }

    private static bool TryTriangulateTerrainBoundary(
        IReadOnlyList<Vector3> points,
        List<int> triangles)
    {
        triangles.Clear();
        if (points == null || points.Count < 3)
            return false;

        List<int> remaining = new(points.Count);
        for (int index = 0; index < points.Count; index++)
            remaining.Add(index);

        int safety = points.Count * points.Count;
        while (remaining.Count > 3 && safety-- > 0)
        {
            bool clippedEar = false;
            for (int remainingIndex = 0; remainingIndex < remaining.Count; remainingIndex++)
            {
                int previousIndex = remaining[(remainingIndex - 1 + remaining.Count) % remaining.Count];
                int currentIndex = remaining[remainingIndex];
                int nextIndex = remaining[(remainingIndex + 1) % remaining.Count];
                Vector3 previous = points[previousIndex];
                Vector3 current = points[currentIndex];
                Vector3 next = points[nextIndex];

                if (CrossXZ(previous, current, next) >= -0.0001f)
                    continue;

                bool containsOtherPoint = false;
                for (int testIndex = 0; testIndex < remaining.Count; testIndex++)
                {
                    int pointIndex = remaining[testIndex];
                    if (pointIndex == previousIndex || pointIndex == currentIndex || pointIndex == nextIndex)
                        continue;
                    if (!PointInsideTriangleXZ(points[pointIndex], previous, current, next))
                        continue;
                    containsOtherPoint = true;
                    break;
                }
                if (containsOtherPoint)
                    continue;

                triangles.Add(previousIndex);
                triangles.Add(currentIndex);
                triangles.Add(nextIndex);
                remaining.RemoveAt(remainingIndex);
                clippedEar = true;
                break;
            }

            if (!clippedEar)
            {
                triangles.Clear();
                return false;
            }
        }

        if (remaining.Count != 3 ||
            CrossXZ(points[remaining[0]], points[remaining[1]], points[remaining[2]]) >= -0.0001f)
        {
            triangles.Clear();
            return false;
        }

        triangles.Add(remaining[0]);
        triangles.Add(remaining[1]);
        triangles.Add(remaining[2]);
        return triangles.Count == (points.Count - 2) * 3;
    }

    private static float CrossXZ(Vector3 a, Vector3 b, Vector3 c)
    {
        return (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
    }

    private static bool PointInsideTriangleXZ(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        float ab = CrossXZ(a, b, point);
        float bc = CrossXZ(b, c, point);
        float ca = CrossXZ(c, a, point);
        bool hasNegative = ab < -0.0001f || bc < -0.0001f || ca < -0.0001f;
        bool hasPositive = ab > 0.0001f || bc > 0.0001f || ca > 0.0001f;
        return !(hasNegative && hasPositive);
    }

    private bool TryGetEditableTerrainSurface(
        Vector2 point,
        out float height,
        out Vector3 normal)
    {
        height = 0f;
        normal = Vector3.up;
        for (int triangleIndex = 0;
             triangleIndex + 2 < terrainTriangleIndices.Count;
             triangleIndex += 3)
        {
            Vector3 a = terrainBoundaryPoints[terrainTriangleIndices[triangleIndex]];
            Vector3 b = terrainBoundaryPoints[terrainTriangleIndices[triangleIndex + 1]];
            Vector3 c = terrainBoundaryPoints[terrainTriangleIndices[triangleIndex + 2]];
            float denominator = (b.z - c.z) * (a.x - c.x) +
                                (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < 0.000001f)
                continue;

            float weightA = ((b.z - c.z) * (point.x - c.x) +
                             (c.x - b.x) * (point.y - c.z)) / denominator;
            float weightB = ((c.z - a.z) * (point.x - c.x) +
                             (a.x - c.x) * (point.y - c.z)) / denominator;
            float weightC = 1f - weightA - weightB;
            if (weightA < -0.0001f || weightB < -0.0001f || weightC < -0.0001f)
                continue;

            height = a.y * weightA + b.y * weightB + c.y * weightC;
            normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.y < 0f)
                normal = -normal;
            if (normal.sqrMagnitude < 0.0001f)
                normal = Vector3.up;
            return true;
        }

        return false;
    }

    private float DistanceToEditableTerrainBoundary(Vector2 point)
    {
        float nearestSqrDistance = float.PositiveInfinity;
        for (int index = 0; index < terrainBoundaryPoints.Count; index++)
        {
            Vector3 start3 = terrainBoundaryPoints[index];
            Vector3 end3 = terrainBoundaryPoints[(index + 1) % terrainBoundaryPoints.Count];
            Vector2 start = new(start3.x, start3.z);
            Vector2 end = new(end3.x, end3.z);
            Vector2 segment = end - start;
            float t = segment.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude)
                : 0f;
            nearestSqrDistance = Mathf.Min(
                nearestSqrDistance,
                (point - (start + segment * t)).sqrMagnitude);
        }
        return Mathf.Sqrt(nearestSqrDistance);
    }

    private Mesh BuildTerrainMesh()
    {
        return usesEditableTerrain
            ? BuildEditableTerrainMesh()
            : BuildRouteWidthTerrainMesh();
    }

    private Mesh BuildEditableTerrainMesh()
    {
        Vector3[] vertices = terrainBoundaryPoints.ToArray();
        Vector2[] uv = new Vector2[vertices.Length];
        for (int index = 0; index < vertices.Length; index++)
            uv[index] = new Vector2(vertices[index].x * 0.08f, vertices[index].z * 0.08f);

        Mesh mesh = new() { name = "Editable Terrain Boundary" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.SetTriangles(terrainTriangleIndices, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildRouteWidthTerrainMesh()
    {
        const int maxGridAxisVertices = 384;
        float gridSpacing = Mathf.Clamp(activeSampleSpacing * 1.5f, 4f, 8f);
        float padding = ActiveTerrainHalfWidth + gridSpacing;
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        for (int index = 0; index < routePoints.Count; index++)
        {
            Vector3 point = routePoints[index];
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minZ = Mathf.Min(minZ, point.z);
            maxZ = Mathf.Max(maxZ, point.z);
        }

        minX -= padding;
        maxX += padding;
        minZ -= padding;
        maxZ += padding;
        float sizeX = maxX - minX;
        float sizeZ = maxZ - minZ;
        gridSpacing = Mathf.Max(
            gridSpacing,
            sizeX / (maxGridAxisVertices - 1),
            sizeZ / (maxGridAxisVertices - 1));

        int columns = Mathf.Max(2, Mathf.CeilToInt(sizeX / gridSpacing) + 1);
        int rows = Mathf.Max(2, Mathf.CeilToInt(sizeZ / gridSpacing) + 1);
        Vector3[] vertices = new Vector3[rows * columns];
        Vector2[] uv = new Vector2[vertices.Length];
        bool[] nearRoute = new bool[vertices.Length];

        for (int row = 0; row < rows; row++)
        {
            float z = row == rows - 1 ? maxZ : minZ + row * gridSpacing;
            for (int column = 0; column < columns; column++)
            {
                float x = column == columns - 1 ? maxX : minX + column * gridSpacing;
                TerrainRouteSample routeSample = FindNearestTerrainRouteSample(x, z);
                int vertex = row * columns + column;
                vertices[vertex] = new Vector3(
                    x,
                    TerrainHeight(
                        routeSample.DistanceAlongRoute,
                        routeSample.SignedLateralDistance,
                        routeSample.CenterHeight),
                    z);
                uv[vertex] = new Vector2(x * 0.08f, z * 0.08f);
                nearRoute[vertex] = Mathf.Abs(routeSample.SignedLateralDistance) <=
                                    ActiveTerrainHalfWidth + gridSpacing * 0.75f;
            }
        }

        List<int> triangles = new((rows - 1) * (columns - 1) * 6);
        for (int row = 0; row < rows - 1; row++)
        for (int column = 0; column < columns - 1; column++)
        {
            int a = row * columns + column;
            int b = a + 1;
            int c = a + columns;
            int d = c + 1;
            if (!nearRoute[a] && !nearRoute[b] && !nearRoute[c] && !nearRoute[d])
                continue;

            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(d);
        }

        Mesh mesh = new() { name = "Non-Folding Winter Terrain" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private TerrainRouteSample FindNearestTerrainRouteSample(float x, float z)
    {
        Vector2 query = new(x, z);
        float bestDistanceSqr = float.PositiveInfinity;
        TerrainRouteSample best = new(
            routePoints[0].y,
            0f,
            Vector2.Distance(query, new Vector2(routePoints[0].x, routePoints[0].z)),
            Vector2.Distance(query, new Vector2(routePoints[0].x, routePoints[0].z)));

        for (int segment = 0; segment < routePoints.Count - 1; segment++)
        {
            Vector3 start = routePoints[segment];
            Vector3 end = routePoints[segment + 1];
            Vector2 startXZ = new(start.x, start.z);
            Vector2 segmentXZ = new(end.x - start.x, end.z - start.z);
            float segmentLengthSqr = segmentXZ.sqrMagnitude;
            if (segmentLengthSqr <= 0.0001f)
                continue;

            float t = Mathf.Clamp01(Vector2.Dot(query - startXZ, segmentXZ) / segmentLengthSqr);
            Vector2 centerXZ = startXZ + segmentXZ * t;
            Vector2 centerToQuery = query - centerXZ;
            float distanceSqr = centerToQuery.sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            Vector2 right = new(segmentXZ.y, -segmentXZ.x);
            right.Normalize();
            best = new TerrainRouteSample(
                Mathf.Lerp(start.y, end.y, t),
                (segment + t) * activeSampleSpacing,
                Vector2.Dot(centerToQuery, right),
                Mathf.Sqrt(distanceSqr));
        }

        return best;
    }

    private readonly struct TerrainRouteSample
    {
        public readonly float CenterHeight;
        public readonly float DistanceAlongRoute;
        public readonly float SignedLateralDistance;
        public readonly float DistanceFromRoute;

        public TerrainRouteSample(
            float centerHeight,
            float distanceAlongRoute,
            float signedLateralDistance,
            float distanceFromRoute)
        {
            CenterHeight = centerHeight;
            DistanceAlongRoute = distanceAlongRoute;
            SignedLateralDistance = signedLateralDistance;
            DistanceFromRoute = distanceFromRoute;
        }
    }

    private bool TryGroundScenery(
        Vector3 proposedPosition,
        float minimumRouteClearance,
        float terrainEdgeMargin,
        out Vector3 groundedPosition)
    {
        TerrainRouteSample nearest = FindNearestTerrainRouteSample(
            proposedPosition.x,
            proposedPosition.z);
        if (nearest.DistanceFromRoute < minimumRouteClearance)
        {
            groundedPosition = default;
            return false;
        }

        groundedPosition = proposedPosition;
        if (usesEditableTerrain)
        {
            Vector2 query = new(proposedPosition.x, proposedPosition.z);
            if (DistanceToEditableTerrainBoundary(query) < terrainEdgeMargin ||
                !TryGetEditableTerrainSurface(query, out float height, out _))
            {
                groundedPosition = default;
                return false;
            }
            groundedPosition.y = height;
        }
        else
        {
            float maximumRouteDistance = ActiveTerrainHalfWidth - terrainEdgeMargin;
            if (nearest.DistanceFromRoute > maximumRouteDistance)
            {
                groundedPosition = default;
                return false;
            }
            groundedPosition.y = TerrainHeight(
                nearest.DistanceAlongRoute,
                nearest.SignedLateralDistance,
                nearest.CenterHeight);
        }
        return true;
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
            int perSide = isSnowfield ? 1 : 2;
            for (int side = -1; side <= 1; side += 2)
            for (int layer = 0; layer < perSide; layer++)
            {
                float scale = Mathf.Lerp(isSnowfield ? 2.4f : 3.0f, isSnowfield ? 5.8f : 7.2f, (float)random.NextDouble());
                float lateral = side * Mathf.Lerp(
                    ActiveRoadHalfWidth + 8f + layer * 11f,
                    ActiveTerrainHalfWidth - 8f,
                    (float)random.NextDouble());
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                float clearance = ActiveRoadHalfWidth + Mathf.Max(3f, scale * 0.42f);
                if (!TryGroundScenery(position, clearance, 5f, out position))
                    continue;
                BuildPine(position, scale, trunk, foliage, random.Next(0, 360));
            }

            if (index % (treeStep * 4) == 0)
            {
                int side = random.NextDouble() < 0.5 ? -1 : 1;
                float lateral = side * Mathf.Lerp(13f, 32f, (float)random.NextDouble());
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                if (!TryGroundScenery(position, ActiveRoadHalfWidth + 3f, 3f, out position))
                    continue;
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

        if (!isSharpCurve)
        {
            for (int index = 18; index < routePoints.Count - 8; index += 18)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float size = Mathf.Lerp(13f, 28f, (float)random.NextDouble());
                    float lateralMagnitude = Mathf.Min(88f, ActiveTerrainHalfWidth - size * 0.55f);
                    if (lateralMagnitude <= ActiveRoadHalfWidth + size * 0.70f)
                        continue;
                    float lateral = side * lateralMagnitude;
                    Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                    if (!TryGroundScenery(
                            position,
                            ActiveRoadHalfWidth + size * 0.70f,
                            size * 0.50f,
                            out position))
                        continue;
                    position.y -= Mathf.Min(1f, size * 0.04f);
                    CreateMeshObject("Distant Snow Mountain", rebuiltRoot, mountainMesh, mountain, false, position,
                        Quaternion.Euler(0f, random.Next(0, 360), 0f),
                        new Vector3(size, size * Mathf.Lerp(0.8f, 1.35f, (float)random.NextDouble()), size));
                }
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
                if (!TryGroundScenery(position, ActiveRoadHalfWidth + 4f, 4f, out position))
                    continue;
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
        AlignSavedRideTeamToStart();
    }

    private void AlignSavedRideTeamToStart()
    {
        Transform savedTeam = transform.Find(RideTeamRootName);
        if (savedTeam == null)
            return;

        Vector3 horizontalForward = Vector3.ProjectOnPlane(StartForward, Vector3.up).normalized;
        if (horizontalForward.sqrMagnitude < 0.0001f)
            horizontalForward = Vector3.back;

        // The road surface is lifted 0.10 m over the route and the ride
        // controller keeps the team another 0.06 m above that surface.
        savedTeam.localPosition = routePoints[0] + Vector3.up * 0.16f;
        savedTeam.localRotation = Quaternion.LookRotation(horizontalForward, Vector3.up);
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
