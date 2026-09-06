using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Stores the scene-baked route and queries its surfaces during gameplay.
/// Geometry is generated only by explicit edit-mode authoring operations.
/// </summary>
[DisallowMultipleComponent]
public sealed class MushCurvedMapRuntime : MonoBehaviour
{
    private const float RoadHalfWidth = 6.5f;
    private const float TerrainHalfWidth = 105f;
    private const string CustomTerrainVisualPrefix = "CUSTOM SLOT - Terrain - ";
    public const int CurrentBakedWorldVersion = 9;
    public const string GeneratedWorldRootName = "Mush Rebuilt Curved World";
    public const string DeformedRoadRootName = "VISIBLE Deformed Snow Road Module";
    public const string CustomSceneContentRootName = "SCENE CONTENT - Add Models Here";
    public const string RideTeamRootName = "Mush Ride Team";
    private const string TerrainCollisionProxyRootName = "Mush Terrain Surface Collision Proxy";
    private const string CustomModelPreviewRootName = "Mush Custom Model Preview";

    [SerializeField, HideInInspector] private List<Vector3> bakedRoute = new();
    [SerializeField, HideInInspector] private float bakedLength;
    [SerializeField, HideInInspector] private float bakedSpacing;
    [SerializeField, HideInInspector] private float bakedRoadWidth = RoadHalfWidth;
    [SerializeField, HideInInspector] private float bakedTerrainWidth = TerrainHalfWidth;
    [SerializeField, HideInInspector] private List<Collider> bakedSurfaceColliders = new();
    private readonly List<Vector3> routePoints = new();
    private readonly List<Collider> activeTerrainSurfaceColliders = new();
    private readonly List<Material> runtimeMaterials = new();
    private float activeTerrainRayTop;
    private float activeTerrainRayDistance;
    [SerializeField, HideInInspector] private int bakedWorldVersion;
    private bool built;
    private bool isSnowfield;
    private bool isSharpCurve;
    private bool overridesTrackWidths;
    private float activeCourseLength = MushTrackPathUtility.DefaultCourseLength;
    private float activeSampleSpacing = MushTrackPathUtility.DefaultSampleSpacing;
    private float authoredRoadHalfWidth;
    private float authoredTerrainHalfWidth;
    private MushTrackAuthoring activeAuthoring;
    private GameObject activeTerrainVisual;
    private Transform terrainCollisionProxyRoot;
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

    public void CopyActiveRoutePreview(List<Vector3> output)
    {
        output.Clear();
        output.AddRange(routePoints);
    }

    private float ActiveRoadHalfWidth => overridesTrackWidths
        ? authoredRoadHalfWidth
        : RoadHalfWidth;
    private float ActiveTerrainHalfWidth => activeAuthoring != null
        ? Mathf.Max(authoredTerrainHalfWidth, ActiveRoadHalfWidth + 4f)
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
        rebuiltRoot = transform.Find(GeneratedWorldRootName);
        ConfigureOptionalSceneFeatures();
        if (Application.isPlaying)
        {
            if (rebuiltRoot == null || bakedRoute == null || bakedRoute.Count < 2)
            {
                Debug.LogError("[Mush] 저장된 주행 경로가 없습니다. 편집 모드에서 도로 갱신 후 씬을 저장해 주세요.", this);
                enabled = false;
                built = true;
                return;
            }
            routePoints.Clear();
            routePoints.AddRange(bakedRoute);
            activeCourseLength = bakedLength;
            activeSampleSpacing = bakedSpacing;
            authoredRoadHalfWidth = bakedRoadWidth;
            authoredTerrainHalfWidth = bakedTerrainWidth;
            overridesTrackWidths = true;
            activeAuthoring = MushTrackAuthoring.FindFor(transform);
            StartForward = Vector3.ProjectOnPlane(routePoints[1] - routePoints[0], Vector3.up).normalized;
            CacheBakedWorldReferences();
            built = true;
            ConfigureRuntimeEnvironmentControllers();
            return;
        }
        if (rebuiltRoot == null)
            RebuildSceneWorld();
        else
        {
            BuildActiveRoute();
            CacheBakedWorldReferences();
            built = true;
        }
    }

    private void StoreBakedRoute()
    {
        bakedRoute.Clear();
        bakedRoute.AddRange(routePoints);
        bakedLength = activeCourseLength;
        bakedSpacing = activeSampleSpacing;
        bakedRoadWidth = ActiveRoadHalfWidth;
        bakedTerrainWidth = ActiveTerrainHalfWidth;
        bakedSurfaceColliders.Clear();
        if (rebuiltRoot != null)
        {
            foreach (MeshCollider collider in rebuiltRoot.GetComponentsInChildren<MeshCollider>(true))
                if (collider.enabled) bakedSurfaceColliders.Add(collider);
        }
        foreach (Collider collider in activeTerrainSurfaceColliders)
            if (collider != null && collider.enabled && !bakedSurfaceColliders.Contains(collider))
                bakedSurfaceColliders.Add(collider);
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
        if (transform.Find(GeneratedWorldRootName) != null)
        {
            RebuildSceneCourseGeometry();
            return;
        }

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
        StoreBakedRoute();
        bakedWorldVersion = CurrentBakedWorldVersion;
        built = true;
    }

    /// <summary>
    /// Rebuilds only the route-dependent surfaces while an artist edits the track.
    /// With a terrain model the road is projected onto it; without one the lightweight
    /// generated terrain follows the road. Scenery and ordinary scene models stay untouched.
    /// </summary>
    public void RebuildSceneCourseGeometry()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("A baked Mush course can only be rebuilt outside play mode.");
        // 지형 모델이 있으면 도로만 그 표면에 붙이고, 없으면 자동 지형만 도로 기준으로 다시 계산합니다.
        built = false;
        ConfigureOptionalSceneFeatures();
        BuildActiveRoute();
        rebuiltRoot = transform.Find(GeneratedWorldRootName);
        if (rebuiltRoot == null)
        {
            RebuildSceneWorld();
            return;
        }

        RefreshTrackMeshesOnly();
        StoreBakedRoute();
        PositionRouteMarkers();
        built = true;
    }

    /// <summary>
    /// 현재 경로와 모델 슬롯을 읽어 저장되지 않는 도로 모델 프리뷰와 표시 상태만 복구합니다.
    /// </summary>
    public void RefreshEditorPresentationOnly()
    {
        if (Application.isPlaying) return;
        rebuiltRoot = transform.Find(GeneratedWorldRootName);
        CacheBakedWorldReferences();
    }

    /// <summary>
    /// Rewrites only the route-dependent road/track meshes and, when necessary,
    /// the lightweight automatic terrain. Scenery, ordinary scene models and VFX are untouched.
    /// </summary>
    private void RefreshTrackMeshesOnly()
    {
        if (rebuiltRoot == null)
            return;

        bool hasTerrainModel = activeTerrainVisual != null;

        if (!hasTerrainModel)
            ReplaceGeneratedTerrainMesh();

        terrainRenderer = FindGeneratedComponent<Renderer>("VISIBLE Snow Terrain");

        ReplaceRibbonMesh("VISIBLE Curved Packed-Snow Road", ActiveRoadHalfWidth, 0f, 0.10f, true);
        ReplaceRibbonMesh("Left Sled Track", 0.10f, -1.75f, 0.145f, false);
        ReplaceRibbonMesh("Right Sled Track", 0.10f, 1.75f, 0.145f, false);

        roadRenderer = FindGeneratedComponent<Renderer>("VISIBLE Curved Packed-Snow Road");

        RefreshRoadModelInstances();
        ApplyCustomCoursePresentation();
    }

    /// <summary>
    /// 기존 GeneratedMaps 에셋을 수정하지 않고 DontSave 임시 Mesh만 사용합니다.
    /// </summary>
    private void ReplaceGeneratedTerrainMesh()
    {
        Transform target = rebuiltRoot.Find("VISIBLE Snow Terrain");
        if (target == null)
            return;

        MeshFilter filter = target.GetComponent<MeshFilter>();
        if (filter == null)
            return;

        Mesh generated = BuildRouteWidthTerrainMesh();
        Mesh destination = filter.sharedMesh;

        if (Application.isPlaying)
        {
            filter.sharedMesh = generated;
            destination = generated;
        }
        else if (destination != null)
        {
            CopyMeshGeometry(generated, destination);
            DestroyImmediate(generated);
        }
        else
        {
            filter.sharedMesh = generated;
            destination = generated;
        }

        MeshCollider collider = target.GetComponent<MeshCollider>();
        if (collider != null)
        {
            collider.sharedMesh = null;
            collider.sharedMesh = destination;
        }
    }

    /// <summary>
    /// Copies a freshly generated lightweight ribbon into the mesh already referenced by the scene.
    /// Reusing the existing mesh prevents per-edit GameObject churn and avoids creating large baked assets.
    /// </summary>
    private void ReplaceRibbonMesh(
        string objectName,
        float halfWidth,
        float lateralOffset,
        float verticalOffset,
        bool updateCollider)
    {
        Transform target = rebuiltRoot.Find(objectName);
        if (target == null)
            return;

        MeshFilter filter = target.GetComponent<MeshFilter>();
        if (filter == null)
            return;

        Mesh generated = BuildRibbonMesh(halfWidth, lateralOffset, verticalOffset);
        Mesh destination = filter.sharedMesh;

        if (Application.isPlaying)
        {
            filter.sharedMesh = generated;
            destination = generated;
        }
        else if (destination != null)
        {
            CopyMeshGeometry(generated, destination);
            DestroyImmediate(generated);
        }
        else
        {
            filter.sharedMesh = generated;
            destination = generated;
        }

        if (updateCollider)
        {
            MeshCollider collider = target.GetComponent<MeshCollider>();
            if (collider != null)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = destination;
            }
        }
    }

    /// <summary>
    /// Copies only geometry data needed by the generated ribbon.
    /// The target mesh asset stays tiny and keeps the scene's existing reference.
    /// </summary>
    private static void CopyMeshGeometry(Mesh source, Mesh destination)
    {
        destination.Clear(false);
        destination.indexFormat = source.indexFormat;
        destination.vertices = source.vertices;
        destination.normals = source.normals;
        destination.tangents = source.tangents;
        destination.colors = source.colors;
        destination.uv = source.uv;
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
        if (onRoad)
        {
            surfaceHeight = routeCenter.y + 0.10f;
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
        if (Application.isPlaying && TrySampleBakedSurface(worldPosition, out RaycastHit savedHit))
        {
            surfacePoint = savedHit.point;
            surfaceNormal = savedHit.normal;
            surfaceForward = Vector3.ProjectOnPlane(surfaceForward, surfaceNormal).normalized;
        }
        return true;
    }

    private bool TrySampleBakedSurface(Vector3 worldPosition, out RaycastHit result)
    {
        result = default;
        Ray ray = new(worldPosition + transform.up * 100f, -transform.up);
        float closest = float.PositiveInfinity;
        bool found = false;
        foreach (Collider surface in bakedSurfaceColliders)
        {
            if (surface == null || !surface.enabled || !surface.gameObject.activeInHierarchy || surface.isTrigger) continue;
            if (!surface.Raycast(ray, out RaycastHit hit, 250f)) continue;
            float distance = (hit.point - worldPosition).sqrMagnitude;
            if (distance >= closest) continue;
            closest = distance;
            result = hit;
            found = true;
        }
        return found;
    }





    private void BuildActiveRoute()
    {
        routePoints.Clear();
        overridesTrackWidths = false;
        activeTerrainVisual = null;
        DestroyTerrainCollisionProxy();
        activeTerrainSurfaceColliders.Clear();

        activeAuthoring = MushTrackAuthoring.FindFor(transform);
        if (activeAuthoring != null)
        {
            overridesTrackWidths = activeAuthoring.OverridesTrackWidths;
            authoredRoadHalfWidth = Mathf.Max(0.5f, activeAuthoring.RoadHalfWidth);
            authoredTerrainHalfWidth = Mathf.Max(activeAuthoring.TerrainHalfWidth, authoredRoadHalfWidth + 4f);
        }

        if (activeAuthoring == null || !activeAuthoring.TryBuildSampledRoute(
                routePoints,
                out activeCourseLength,
                out activeSampleSpacing))
        {
            MushTrackPathUtility.BuildDefaultRoute(routePoints);
            activeCourseLength = MushTrackPathUtility.DefaultCourseLength;
            activeSampleSpacing = MushTrackPathUtility.DefaultSampleSpacing;
        }

        if (routePoints.Count < 2)
            throw new InvalidOperationException("Track generation requires at least two route samples.");

        if (activeAuthoring != null && activeAuthoring.HasCustomTerrainVisual)
        {
            activeTerrainVisual = ResolveCustomVisual(
                activeAuthoring.CustomTerrainVisual,
                CustomTerrainVisualPrefix);

            if (activeTerrainVisual != null)
                ProjectRouteOntoTerrainModel(activeTerrainVisual);
        }
        else
        {
            ResolveCustomVisual(null, CustomTerrainVisualPrefix);
        }

        StartForward = Vector3.ProjectOnPlane(routePoints[1] - routePoints[0], Vector3.up).normalized;
        if (StartForward.sqrMagnitude < 0.0001f)
            StartForward = Vector3.back;
    }

    /// <summary>
    /// Collider가 없으면 원본 Mesh를 공유하는 DontSave MeshCollider 프록시만 임시 생성합니다.
    /// </summary>
    private void PrepareTerrainSurfaceColliders(GameObject terrainVisual, List<Collider> output)
    {
        output.Clear();
        if (terrainVisual == null)
            return;

        Collider[] existingColliders = terrainVisual.GetComponentsInChildren<Collider>(true);
        for (int index = 0; index < existingColliders.Length; index++)
        {
            Collider collider = existingColliders[index];
            if (collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy)
                output.Add(collider);
        }

        if (output.Count > 0)
            return;

        MeshFilter[] meshFilters = terrainVisual.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length == 0)
            return;

        GameObject proxyRootObject = new(TerrainCollisionProxyRootName);
        proxyRootObject.hideFlags = HideFlags.None;
        terrainCollisionProxyRoot = proxyRootObject.transform;
        if (gameObject.scene.IsValid())
            SceneManager.MoveGameObjectToScene(proxyRootObject, gameObject.scene);

        for (int index = 0; index < meshFilters.Length; index++)
        {
            MeshFilter filter = meshFilters[index];
            if (filter == null || filter.sharedMesh == null || !filter.gameObject.activeInHierarchy)
                continue;

            GameObject proxyObject = new($"Terrain Surface Proxy {index:000}");
            proxyObject.hideFlags = HideFlags.None;
            Transform proxyTransform = proxyObject.transform;
            proxyTransform.SetParent(terrainCollisionProxyRoot, false);
            proxyTransform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
            proxyTransform.localScale = filter.transform.lossyScale;

            MeshCollider proxyCollider = proxyObject.AddComponent<MeshCollider>();
            proxyCollider.sharedMesh = filter.sharedMesh;
            proxyCollider.convex = false;
            proxyTransform.SetParent(filter.transform, true);
            output.Add(proxyCollider);
        }
    }

    /// <summary>
    /// 제어점 자체를 바꾸지 않으므로 지형 모델을 제거하면 원래 제어점 높이가 다시 사용됩니다.
    /// </summary>
    private void ProjectRouteOntoTerrainModel(GameObject terrainVisual)
    {
        PrepareTerrainSurfaceColliders(terrainVisual, activeTerrainSurfaceColliders);

        if (activeTerrainSurfaceColliders.Count == 0)
        {
            Debug.LogWarning(
                $"[Mush] Terrain model '{terrainVisual.name}' has no usable Collider or MeshFilter. The road keeps its authored height.",
                activeAuthoring);
            return;
        }

        Bounds surfaceBounds = activeTerrainSurfaceColliders[0].bounds;
        for (int index = 1; index < activeTerrainSurfaceColliders.Count; index++)
            surfaceBounds.Encapsulate(activeTerrainSurfaceColliders[index].bounds);

        activeTerrainRayTop = surfaceBounds.max.y + Mathf.Max(50f, surfaceBounds.size.y + 10f);
        activeTerrainRayDistance = Mathf.Max(100f, surfaceBounds.size.y + 120f);

        int projectedCount = 0;
        for (int routeIndex = 0; routeIndex < routePoints.Count; routeIndex++)
        {
            Vector3 localPoint = routePoints[routeIndex];
            if (!TrySampleActiveTerrain(localPoint, out Vector3 sampledPoint, out _))
                continue;

            localPoint.y = sampledPoint.y;
            routePoints[routeIndex] = localPoint;
            projectedCount++;
        }

        if (projectedCount >= 2)
        {
            activeCourseLength = 0f;
            for (int index = 1; index < routePoints.Count; index++)
                activeCourseLength += Vector3.Distance(routePoints[index - 1], routePoints[index]);

            activeSampleSpacing = routePoints.Count > 1
                ? activeCourseLength / (routePoints.Count - 1)
                : MushTrackPathUtility.DefaultSampleSpacing;
        }
    }

    private bool TrySampleActiveTerrain(Vector3 localQuery, out Vector3 localPoint, out Vector3 localNormal)
    {
        localPoint = localQuery;
        localNormal = Vector3.up;
        if (activeTerrainSurfaceColliders.Count == 0 || activeTerrainRayDistance <= 0f)
            return false;

        Vector3 worldQuery = transform.TransformPoint(localQuery);
        Ray ray = new(new Vector3(worldQuery.x, activeTerrainRayTop, worldQuery.z), Vector3.down);
        bool found = false;
        float nearestDistance = float.PositiveInfinity;
        RaycastHit nearestHit = default;

        for (int colliderIndex = 0; colliderIndex < activeTerrainSurfaceColliders.Count; colliderIndex++)
        {
            Collider collider = activeTerrainSurfaceColliders[colliderIndex];
            if (collider == null || !collider.enabled)
                continue;
            if (!collider.Raycast(ray, out RaycastHit hit, activeTerrainRayDistance) || hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            nearestHit = hit;
            found = true;
        }

        if (!found)
            return false;

        localPoint = transform.InverseTransformPoint(nearestHit.point);
        localNormal = transform.InverseTransformDirection(nearestHit.normal).normalized;
        return true;
    }

    private void DestroyTerrainCollisionProxy()
    {
        if (terrainCollisionProxyRoot == null)
            return;

        GameObject proxyObject = terrainCollisionProxyRoot.gameObject;
        terrainCollisionProxyRoot = null;

        if (Application.isPlaying)
            Destroy(proxyObject);
        else
            DestroyImmediate(proxyObject);
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

        GameObject terrainObject = CreateMeshObject("VISIBLE Snow Terrain", rebuiltRoot, BuildRouteWidthTerrainMesh(), snow, true);
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
        Transform roadModelRoot = rebuiltRoot != null ? rebuiltRoot.Find(DeformedRoadRootName) : null;
        bool hasRoadModel = roadModelRoot != null &&
                            activeAuthoring != null &&
                            activeAuthoring.HasRoadModel;
        bool hasTerrainModel = activeTerrainVisual != null;
        bool showGeneratedRoad = !hasRoadModel;
        bool showGeneratedTerrain = !hasTerrainModel;

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

        SetVisualRenderersEnabled(roadModelRoot, hasRoadModel);


        Transform generatedTerrainTransform = rebuiltRoot != null ? rebuiltRoot.Find("VISIBLE Snow Terrain") : null;
        MeshCollider generatedTerrainCollider = generatedTerrainTransform != null
            ? generatedTerrainTransform.GetComponent<MeshCollider>()
            : null;
        if (generatedTerrainCollider != null)
            generatedTerrainCollider.enabled = !hasTerrainModel;
    }

    private GameObject ResolveCustomVisual(GameObject source, string generatedPrefix)
    {
        if (source != null && source.scene.IsValid())
        {
            Transform oldPreviewRoot = transform.Find(CustomModelPreviewRootName);
            if (oldPreviewRoot != null && !Application.isPlaying)
                DestroyImmediate(oldPreviewRoot.gameObject);
            return source;
        }

        Transform previewRoot = transform.Find(CustomModelPreviewRootName);
        if (source == null)
        {
            if (previewRoot != null && !Application.isPlaying)
                DestroyImmediate(previewRoot.gameObject);
            return null;
        }

        string expectedName = generatedPrefix + source.name;
        if (previewRoot == null)
        {
            GameObject previewRootObject = new(CustomModelPreviewRootName);
            previewRoot = previewRootObject.transform;
            previewRoot.SetParent(transform, false);
        }

        for (int index = previewRoot.childCount - 1; index >= 0; index--)
        {
            Transform child = previewRoot.GetChild(index);
            if (child.name == expectedName)
                return child.gameObject;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        GameObject instance = Instantiate(source, previewRoot, false);
        instance.name = expectedName;
        return instance;
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

    private void BuildDeformedRoadModule(GameObject sourceRoot)
    {
        if (Application.isPlaying || sourceRoot == null || rebuiltRoot == null) return;
        MeshFilter[] filters = sourceRoot.GetComponentsInChildren<MeshFilter>(false);
        if (!TryMeasureRoadModule(sourceRoot, filters, out RoadModuleGeometry geometry)) return;
        int count = Mathf.Clamp(Mathf.CeilToInt(activeCourseLength / Mathf.Clamp(geometry.Length, 8f, 32f)), 1, 128);
        float length = activeCourseLength / count;
        int budget = MushRoadMeshBender.VertexBudget;
        var meshes = new List<Mesh>();
        var materials = new List<Material[]>();
        try
        {
            for (int segment = 0; segment < count; segment++)
                foreach (MeshFilter filter in filters)
                {
                    if (filter.sharedMesh == null) continue;
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || !renderer.enabled) continue;
                    Mesh mesh = MushRoadMeshBender.Bend(filter, sourceRoot.transform,
                        geometry.MinZ, geometry.MaxZ, geometry.RoadCenterX, geometry.SourceBaseY,
                        ActiveRoadHalfWidth / geometry.RoadHalfWidth, segment * length, length,
                        activeSampleSpacing, EvaluateRouteFrame, ref budget);
                    meshes.Add(mesh);
                    materials.Add(renderer.sharedMaterials);
                }
        }
        catch (Exception exception)
        {
            foreach (Mesh mesh in meshes) DestroyImmediate(mesh);
            Debug.LogError("[Mush] 도로 모델 베이크를 중단했습니다. 기존 도로는 유지됩니다. " + exception.Message, this);
            throw;
        }
        Transform root = rebuiltRoot.Find(DeformedRoadRootName);
        if (root == null)
        {
            root = new GameObject(DeformedRoadRootName).transform;
            root.SetParent(rebuiltRoot, false);
        }
        root.hideFlags = HideFlags.None;
        root.gameObject.hideFlags = HideFlags.None;
        var existing = new List<MushGeneratedRoadPart>(root.GetComponentsInChildren<MushGeneratedRoadPart>(true));
        for (int i = 0; i < meshes.Count; i++)
        {
            GameObject part = i < existing.Count ? existing[i].gameObject : new GameObject($"Road Chunk {i + 1:000}");
            if (i >= existing.Count)
            {
                part.transform.SetParent(root, false);
                part.AddComponent<MushGeneratedRoadPart>();
                part.AddComponent<MeshFilter>();
                part.AddComponent<MeshRenderer>();
            }
            MeshFilter filter = part.GetComponent<MeshFilter>();
            if (filter.sharedMesh != null)
            {
                CopyMeshGeometry(meshes[i], filter.sharedMesh);
                DestroyImmediate(meshes[i]);
            }
            else filter.sharedMesh = meshes[i];
            part.GetComponent<MeshRenderer>().sharedMaterials = materials[i];
        }
        for (int i = meshes.Count; i < existing.Count; i++)
        {
            Transform part = existing[i].transform;
            while (part.childCount > 0) part.GetChild(0).SetParent(root, true);
            DestroyImmediate(part.gameObject);
        }
    }

    private void RefreshRoadModelInstances()
    {
        if (rebuiltRoot == null)
            return;

        Transform existingRoot = rebuiltRoot.Find(DeformedRoadRootName);

        if (activeAuthoring == null || !activeAuthoring.HasRoadModel)
        {
            if (existingRoot != null && !Application.isPlaying)
            {
                foreach (MushGeneratedRoadPart part in existingRoot.GetComponentsInChildren<MushGeneratedRoadPart>(true))
                {
                    while (part.transform.childCount > 0)
                        part.transform.GetChild(0).SetParent(existingRoot, true);
                    DestroyImmediate(part.gameObject);
                }
            }

            return;
        }

        BuildDeformedRoadModule(activeAuthoring.RoadModel);
    }

    public void InvalidateRoadModelInstances()
    {
        // The next editor rebuild updates owned meshes in place and preserves added scene children.
    }

    private static bool TryMeasureRoadModule(
        GameObject sourceRoot,
        IReadOnlyList<MeshFilter> sourceFilters,
        out RoadModuleGeometry geometry)
    {
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        bool foundRoad = false;

        for (int filterIndex = 0; filterIndex < sourceFilters.Count; filterIndex++)
        {
            MeshFilter filter = sourceFilters[filterIndex];
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                continue;


            Bounds bounds = mesh.bounds;
            Matrix4x4 sourceToModule =
                sourceRoot.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = new(
                    (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 point = sourceToModule.MultiplyPoint3x4(localCorner);

                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                minZ = Mathf.Min(minZ, point.z);
                maxZ = Mathf.Max(maxZ, point.z);
            }

            foundRoad = true;
        }

        float length = maxZ - minZ;
        float width = maxX - minX;
        if (!foundRoad || length < 0.1f || width < 0.1f)
        {
            geometry = default;
            return false;
        }

        float roadCenterX = (minX + maxX) * 0.5f;
        geometry = new RoadModuleGeometry(
            minZ,
            maxZ,
            roadCenterX,
            width * 0.5f,
            width * 0.5f,
            minY);
        return true;
    }

    private void EvaluateRouteFrame(float distance, out Vector3 center, out Vector3 right)
    {
        // 각 샘플 경계마다 진행 방향이 갑자기 바뀌어 커브에서 도로를 가로지르는 꺾임선이 보일 수 있습니다.
        float routeIndex = Mathf.Clamp(distance / activeSampleSpacing, 0f, routePoints.Count - 1f);
        int startIndex = Mathf.Min(Mathf.FloorToInt(routeIndex), routePoints.Count - 2);
        float interpolation = routeIndex - startIndex;
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
        // 이전처럼 4m 샘플 한 구간마다 고정된 tangent를 쓰지 않으므로 커브의 단면 방향도 연속적으로 회전합니다.
        Vector3 tangent = EvaluateUniformCatmullRomTangent(
            point0,
            point1,
            point2,
            point3,
            interpolation);
        Vector3 flatTangent = Vector3.ProjectOnPlane(tangent, Vector3.up);
        if (flatTangent.sqrMagnitude < 0.0001f)
            flatTangent = Vector3.ProjectOnPlane(point2 - point1, Vector3.up);
        if (flatTangent.sqrMagnitude < 0.0001f)
            flatTangent = Vector3.back;
        right = Vector3.Cross(Vector3.up, flatTangent.normalized).normalized;
    }

    private static Vector3 EvaluateUniformCatmullRom(
        Vector3 point0,
        Vector3 point1,
        Vector3 point2,
        Vector3 point3,
        float interpolation)
    {
        float t = Mathf.Clamp01(interpolation);
        float tSquared = t * t;
        float tCubed = tSquared * t;
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
        float t = Mathf.Clamp01(interpolation);
        float tSquared = t * t;

        return 0.5f *
               ((-point0 + point2) +
                2f * (2f * point0 - 5f * point1 + 4f * point2 - point3) * t +
                3f * (-point0 + 3f * point1 - 3f * point2 + point3) * tSquared);
    }

    private Mesh BuildRibbonMesh(float halfWidth, float lateralOffset, float yLift)
    {
        int count = routePoints.Count;
        Vector3[] vertices = new Vector3[count * 2];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[(count - 1) * 6];
        bool conformToTerrainModel = activeTerrainVisual != null && activeTerrainSurfaceColliders.Count > 0;

        for (int index = 0; index < count; index++)
        {
            Vector3 right = RouteRight(index);
            Vector3 baseCenter = routePoints[index] + right * lateralOffset;
            Vector3 left = baseCenter - right * halfWidth;
            Vector3 rightPoint = baseCenter + right * halfWidth;

            if (conformToTerrainModel)
            {
                if (TrySampleActiveTerrain(left, out Vector3 sampledLeft, out _))
                    left.y = sampledLeft.y;
                if (TrySampleActiveTerrain(rightPoint, out Vector3 sampledRight, out _))
                    rightPoint.y = sampledRight.y;
            }

            left.y += yLift;
            rightPoint.y += yLift;
            vertices[index * 2] = left;
            vertices[index * 2 + 1] = rightPoint;

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
