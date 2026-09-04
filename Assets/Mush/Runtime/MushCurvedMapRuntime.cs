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
    private const string CustomTerrainVisualPrefix = "CUSTOM SLOT - Terrain - ";
    public const int CurrentBakedWorldVersion = 9;
    public const string GeneratedWorldRootName = "Mush Rebuilt Curved World";
    public const string DeformedRoadRootName = "VISIBLE Deformed Snow Road Module";
    public const string CustomSceneContentRootName = "SCENE CONTENT - Add Models Here";
    public const string RideTeamRootName = "Mush Ride Team";
    private const string TerrainCollisionProxyRootName = "Mush Terrain Surface Collision Proxy";
    private const string CustomModelPreviewRootName = "Mush Custom Model Preview";

    private readonly List<Vector3> routePoints = new();
    private readonly List<Vector3> terrainBoundaryPoints = new();
    private readonly List<Vector3> terrainSurfacePoints = new();
    private readonly List<Vector3> terrainHeightPoints = new();
    private readonly List<int> terrainTriangleIndices = new();
    private readonly List<Collider> activeTerrainSurfaceColliders = new(); // 지형 모델이 있을 때 도로 중심과 양쪽 가장자리를 같은 표면에 밀착시키는 데 재사용합니다.
    private readonly List<Material> runtimeMaterials = new();
    private float activeTerrainRayTop; // 현재 지형 모델 표면 샘플링용 Ray 시작 높이입니다.
    private float activeTerrainRayDistance; // 현재 지형 모델 전체를 통과할 Ray 길이입니다.
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

    public void CopyActiveRoutePreview(List<Vector3> output) // Scene View가 실제 지형 투영까지 끝난 현재 도로 중심선을 표시할 때 사용합니다.
    {
        output.Clear(); // 호출자가 가진 이전 샘플을 비웁니다.
        output.AddRange(routePoints); // 현재 런타임이 실제로 사용하는 지형 맞춤 경로를 그대로 복사합니다.
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

        // A baked scene already owns its visible road and terrain meshes. Play
        // mode only needs the sampled route and terrain heights for movement;
        // clipping every terrain triangle against every road sample again can
        // grow explosively on long, tightly curved authored tracks.
        rebuiltRoot = transform.Find(GeneratedWorldRootName);
        bool hasBakedWorld = rebuiltRoot != null;
        ConfigureOptionalSceneFeatures();
        BuildActiveRoute();
        if (!hasBakedWorld)
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
            // 기존 씬의 지형/배경은 그대로 사용하고, 플레이 시작 시 도로 리본만 현재 포인트로 맞춥니다.
            RefreshTrackMeshesOnly();
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
    /// Rebuilds only the route-dependent surfaces while an artist edits the track.
    /// With a terrain model the road is projected onto it; without one the lightweight
    /// generated terrain follows the road. Scenery and ordinary scene models stay untouched.
    /// </summary>
    public void RebuildSceneCourseGeometry()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("A baked Mush course can only be rebuilt outside play mode.");

        // 도로 포인트 편집에서는 폐기된 수동 지형 포인트 데이터를 전혀 사용하지 않습니다.
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
        PositionRouteMarkers();
        built = true;
    }

    /// <summary>
    /// 씬을 열거나 스크립트가 다시 로드됐을 때 저장된 도로/지형 Mesh는 건드리지 않고,
    /// 현재 경로와 모델 슬롯을 읽어 저장되지 않는 도로 모델 프리뷰와 표시 상태만 복구합니다.
    /// </summary>
    public void RefreshEditorPresentationOnly()
    {
        if (Application.isPlaying)
            return;

        built = false;
        ConfigureOptionalSceneFeatures();
        BuildActiveRoute(); // 지형 모델이 있으면 실제 표면에 투영된 현재 경로까지 계산합니다.
        rebuiltRoot = transform.Find(GeneratedWorldRootName);
        if (rebuiltRoot == null)
            return;

        CacheBakedWorldReferences(); // 저장된 작은 도로/지형 Mesh와 기존 배경 참조만 다시 잡습니다.
        RefreshRoadModelInstances(); // 도로 모델은 HideAndDontSave 구간 인스턴스로만 복구합니다.
        ApplyCustomCoursePresentation(); // 모델 유무에 따른 표시 상태만 맞춥니다.
        built = true;
    }

    /// <summary>
    /// Rewrites only the route-dependent road/track meshes and, when necessary,
    /// the lightweight automatic terrain. Scenery, ordinary scene models and VFX are untouched.
    /// </summary>
    private void RefreshTrackMeshesOnly()
    {
        if (rebuiltRoot == null) // 기존 생성 월드가 없으면 갱신할 대상도 없습니다.
            return;

        bool hasTerrainModel = activeTerrainVisual != null; // 지형 모델이 지정되어 실제 표면을 사용하는지 확인합니다.

        if (!hasTerrainModel) // 지형 모델이 없을 때만 자동 생성 지형을 현재 도로 높이에 맞춰 다시 만듭니다.
            ReplaceGeneratedTerrainMesh();

        terrainRenderer = FindGeneratedComponent<Renderer>("VISIBLE Snow Terrain"); // 현재 생성 지형 Renderer 참조를 다시 잡습니다.

        ReplaceRibbonMesh("VISIBLE Curved Packed-Snow Road", ActiveRoadHalfWidth, 0f, 0.10f, true); // 실제 주행 충돌용 가벼운 도로 리본을 갱신합니다.
        ReplaceRibbonMesh("Left Sled Track", 0.10f, -1.75f, 0.145f, false); // 왼쪽 썰매 자국을 현재 경로에 맞춥니다.
        ReplaceRibbonMesh("Right Sled Track", 0.10f, 1.75f, 0.145f, false); // 오른쪽 썰매 자국을 현재 경로에 맞춥니다.

        roadRenderer = FindGeneratedComponent<Renderer>("VISIBLE Curved Packed-Snow Road"); // 생성 도로 Renderer를 다시 찾습니다.

        RefreshRoadModelInstances(); // 도로 모델이 지정되어 있으면 Mesh를 복제하지 않고 기존 구간 인스턴스 위치만 갱신합니다.
        ApplyCustomCoursePresentation(); // 도로 모델/지형 모델 지정 여부에 따라 무엇을 보여줄지 최종 정리합니다.
    }

    /// <summary>
    /// 지형 모델이 없을 때만 사용하는 자동 지형을 현재 도로 높이에 맞춰 갱신합니다.
    /// 기존 GeneratedMaps 에셋을 수정하지 않고 DontSave 임시 Mesh만 사용합니다.
    /// </summary>
    private void ReplaceGeneratedTerrainMesh()
    {
        Transform target = rebuiltRoot.Find("VISIBLE Snow Terrain"); // 기존 자동 지형 오브젝트를 찾습니다.
        if (target == null) // 정상 씬에서는 항상 존재하며, 없으면 전체 배경을 건드리지 않고 그대로 둡니다.
            return;

        MeshFilter filter = target.GetComponent<MeshFilter>(); // 지형 MeshFilter를 가져옵니다.
        if (filter == null)
            return;

        Mesh generated = BuildRouteWidthTerrainMesh(); // 도로 높이와 곡선을 따라 자연스럽게 이어지는 원래 자동 지형을 계산합니다.
        Mesh destination = filter.sharedMesh; // 씬이 원래 참조하던 작은 GeneratedMaps Mesh를 가져옵니다.

        if (Application.isPlaying) // Play Mode에서는 프로젝트 에셋을 절대로 수정하지 않습니다.
        {
            generated.hideFlags = HideFlags.DontSave; // 플레이 중에만 쓰는 임시 Mesh로 둡니다.
            filter.sharedMesh = generated; // 플레이가 끝나면 Unity가 씬 참조를 원래대로 되돌립니다.
            destination = generated;
        }
        else if (destination != null) // Edit Mode에서는 MeshFilter의 참조를 바꾸지 않고 기존 작은 Mesh 내용만 갱신합니다.
        {
            CopyMeshGeometry(generated, destination); // DontSave Mesh를 씬에 연결하지 않으므로 저장 후 Mesh가 사라지지 않습니다.
            DestroyImmediate(generated); // 계산용 Mesh는 즉시 정리합니다.
        }
        else // 과거 잘못된 패치로 Mesh 참조가 이미 비어 있는 경우에만 임시 복구 표시를 사용합니다.
        {
            generated.hideFlags = HideFlags.DontSave; // 이 경우에는 씬 저장 전에 정상 기준 씬 복원이 필요합니다.
            filter.sharedMesh = generated;
            destination = generated;
        }

        MeshCollider collider = target.GetComponent<MeshCollider>(); // 지형 충돌체도 현재 표시 Mesh와 맞춥니다.
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
        Transform target = rebuiltRoot.Find(objectName); // 기존 도로/썰매 자국 오브젝트를 찾습니다.
        if (target == null)
            return;

        MeshFilter filter = target.GetComponent<MeshFilter>(); // 씬이 원래 가진 MeshFilter를 사용합니다.
        if (filter == null)
            return;

        Mesh generated = BuildRibbonMesh(halfWidth, lateralOffset, verticalOffset); // 현재 트랙 포인트로 가벼운 리본 Mesh만 계산합니다.
        Mesh destination = filter.sharedMesh; // 기존 GeneratedMaps의 작은 Mesh 참조를 유지합니다.

        if (Application.isPlaying) // Play Mode에서는 프로젝트 Mesh 에셋을 수정하지 않습니다.
        {
            generated.hideFlags = HideFlags.DontSave; // 실행 중에만 존재하는 Mesh로 사용합니다.
            filter.sharedMesh = generated;
            destination = generated;
        }
        else if (destination != null) // Edit Mode에서는 참조를 갈아끼우지 않고 기존 Mesh 데이터만 갱신합니다.
        {
            CopyMeshGeometry(generated, destination); // 씬 저장 후 MeshFilter가 null이 되는 문제를 막습니다.
            DestroyImmediate(generated); // 계산용 임시 Mesh는 바로 제거합니다.
        }
        else // 이전 패치로 이미 참조가 손상된 씬을 열었을 때만 화면 확인용 임시 Mesh를 연결합니다.
        {
            generated.hideFlags = HideFlags.DontSave;
            filter.sharedMesh = generated;
            destination = generated;
        }

        if (updateCollider) // 실제 주행 도로만 Collider를 함께 갱신합니다.
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
        // 일반 씬 오브젝트는 맵 편집기의 관리 대상이 아닙니다.
        // 사용자가 Hierarchy 어디에 배치했든 Renderer/Collider/VFX 상태를 건드리지 않습니다.
        // 맵 편집기는 자신이 생성한 "Mush Rebuilt Curved World"만 다시 만듭니다.
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

        return IsSameOrChild(candidate, activeAuthoring.CustomTerrainVisual);
    }

    private static bool IsSameOrChild(Transform candidate, GameObject root)
    {
        if (root == null || !root.scene.IsValid())
            return false;
        return candidate == root.transform || candidate.IsChildOf(root.transform);
    }

    private void BuildActiveRoute()
    {
        routePoints.Clear(); // 이전 도로 샘플을 비웁니다.
        terrainBoundaryPoints.Clear(); // 폐기된 수동 지형 편집 데이터는 런타임에서도 사용하지 않습니다.
        terrainSurfacePoints.Clear(); // 폐기된 수동 지형 삼각분할 데이터를 비웁니다.
        terrainHeightPoints.Clear(); // 폐기된 수동 지형 높이점을 비웁니다.
        terrainTriangleIndices.Clear(); // 폐기된 수동 지형 인덱스를 비웁니다.
        usesEditableTrack = false; // 새 경로를 읽기 전 사용자 경로 사용 여부를 초기화합니다.
        usesEditableTerrain = false; // 지형 직접 편집 기능은 완전히 사용하지 않습니다.
        overridesTrackWidths = false; // 맵별 폭 지정 여부를 초기화합니다.
        activeTerrainVisual = null; // 이번 경로에서 사용할 지형 모델 참조를 초기화합니다.
        DestroyTerrainCollisionProxy(); // 이전 지형 모델용 임시 충돌 프록시가 있으면 먼저 제거합니다.
        activeTerrainSurfaceColliders.Clear(); // 이전 지형 모델 Collider 참조를 새 투영 전에 비웁니다.

        activeAuthoring = MushTrackAuthoring.FindFor(transform); // 현재 맵에 연결된 트랙 편집 데이터를 찾습니다.
        if (activeAuthoring != null) // 트랙 데이터가 있으면 폭 설정과 모델 설정을 읽습니다.
        {
            overridesTrackWidths = activeAuthoring.OverridesTrackWidths; // 사용자 지정 폭 사용 여부를 가져옵니다.
            authoredRoadHalfWidth = Mathf.Max(0.5f, activeAuthoring.RoadHalfWidth); // 도로 반폭을 최소값 이상으로 제한합니다.
            authoredTerrainHalfWidth = Mathf.Max(activeAuthoring.TerrainHalfWidth, authoredRoadHalfWidth + 4f); // 자동 지형은 항상 도로보다 넓게 유지합니다.
        }

        if (activeAuthoring != null && activeAuthoring.TryBuildSampledRoute(
                routePoints,
                out activeCourseLength,
                out activeSampleSpacing)) // 사용자 제어점을 실제 곡선 샘플로 변환합니다.
        {
            usesEditableTrack = true; // 정상 사용자 경로를 사용 중임을 기록합니다.
        }
        else // 사용자 경로가 없으면 기존 기본 직선을 사용합니다.
        {
            MushTrackPathUtility.BuildDefaultRoute(routePoints); // 기본 960m 직선 경로를 만듭니다.
            activeCourseLength = MushTrackPathUtility.DefaultCourseLength; // 기본 길이를 저장합니다.
            activeSampleSpacing = MushTrackPathUtility.DefaultSampleSpacing; // 기본 샘플 간격을 저장합니다.
        }

        if (routePoints.Count < 2) // 도로는 최소 두 샘플이 필요합니다.
            throw new InvalidOperationException("Track generation requires at least two route samples."); // 잘못된 상태를 즉시 알립니다.

        if (activeAuthoring != null && activeAuthoring.HasCustomTerrainVisual) // 지형 모델이 지정되어 있으면 도로가 지형 쪽에 맞춰집니다.
        {
            activeTerrainVisual = ResolveCustomVisual(
                activeAuthoring.CustomTerrainVisual,
                CustomTerrainVisualPrefix); // 씬 오브젝트면 그대로 쓰고 Prefab이면 가벼운 인스턴스를 한 번 준비합니다.

            if (activeTerrainVisual != null) // 실제 지형 모델을 얻었을 때만 표면 투영을 수행합니다.
                ProjectRouteOntoTerrainModel(activeTerrainVisual); // 모든 도로 샘플의 Y를 지형 표면 높이에 맞춥니다.
        }
        else // 지형 모델 슬롯을 None으로 바꾼 경우에는 이전에 자동 생성했던 Prefab 인스턴스만 정리합니다.
        {
            ResolveCustomVisual(null, CustomTerrainVisualPrefix); // 사용자가 직접 씬에 둔 일반 오브젝트는 건드리지 않고 시스템 생성 인스턴스만 제거합니다.
        }

        StartForward = Vector3.ProjectOnPlane(routePoints[1] - routePoints[0], Vector3.up).normalized; // 시작 진행 방향을 수평면에서 계산합니다.
        if (StartForward.sqrMagnitude < 0.0001f) // 두 시작점이 수평상 거의 같은 위치면 안전한 기본 방향을 사용합니다.
            StartForward = Vector3.back; // 기본 진행 방향은 기존 맵과 같은 뒤쪽 방향입니다.
    }

    /// <summary>
    /// 지정된 지형 모델에 Collider가 있으면 그대로 사용하고,
    /// Collider가 없으면 원본 Mesh를 공유하는 DontSave MeshCollider 프록시만 임시 생성합니다.
    /// Mesh 데이터 자체는 복제하지 않습니다.
    /// </summary>
    private void PrepareTerrainSurfaceColliders(GameObject terrainVisual, List<Collider> output)
    {
        output.Clear(); // 이전 호출에서 사용한 Collider 목록을 비웁니다.
        if (terrainVisual == null) // 지형 모델이 없으면 준비할 표면도 없습니다.
            return;

        Collider[] existingColliders = terrainVisual.GetComponentsInChildren<Collider>(true); // 모델이 원래 가진 Collider를 모두 찾습니다.
        for (int index = 0; index < existingColliders.Length; index++) // 실제 표면으로 쓸 수 있는 Collider만 골라냅니다.
        {
            Collider collider = existingColliders[index]; // 현재 Collider를 가져옵니다.
            if (collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy) // 활성 일반 Collider만 도로 투영에 사용합니다.
                output.Add(collider); // 별도 복제 없이 기존 Collider를 그대로 사용합니다.
        }

        if (output.Count > 0) // 모델 자체에 Collider가 있으면 임시 프록시를 만들 필요가 없습니다.
            return;

        MeshFilter[] meshFilters = terrainVisual.GetComponentsInChildren<MeshFilter>(true); // Collider가 없을 때 실제 표시 Mesh들을 찾습니다.
        if (meshFilters.Length == 0) // MeshFilter조차 없다면 자동 표면 투영을 만들 수 없습니다.
            return;

        GameObject proxyRootObject = new(TerrainCollisionProxyRootName); // 표면 투영과 Play Mode 충돌에 사용할 숨은 프록시 루트를 만듭니다.
        proxyRootObject.hideFlags = HideFlags.HideAndDontSave; // Hierarchy/씬/에셋에 저장되지 않는 완전 임시 오브젝트로 둡니다.
        terrainCollisionProxyRoot = proxyRootObject.transform; // 다음 갱신 때 정확히 제거할 수 있도록 참조를 보관합니다.
        if (gameObject.scene.IsValid()) // 현재 맵 씬이 정상적이면 프록시도 같은 씬에 두어 PhysicsScene을 정확히 공유합니다.
            SceneManager.MoveGameObjectToScene(proxyRootObject, gameObject.scene); // 부모 없이 월드 좌표 그대로 사용할 수 있게 같은 씬 루트로 옮깁니다.

        for (int index = 0; index < meshFilters.Length; index++) // 지형 모델의 각 Mesh를 공유 Collider로 만듭니다.
        {
            MeshFilter filter = meshFilters[index]; // 현재 지형 MeshFilter를 가져옵니다.
            if (filter == null || filter.sharedMesh == null || !filter.gameObject.activeInHierarchy) // 표시되지 않는 Mesh는 건너뜁니다.
                continue;

            GameObject proxyObject = new($"Terrain Surface Proxy {index:000}"); // 한 Mesh당 아주 가벼운 Collider 전용 오브젝트를 만듭니다.
            proxyObject.hideFlags = HideFlags.HideAndDontSave; // 프록시는 씬 파일에 저장하지 않습니다.
            Transform proxyTransform = proxyObject.transform; // 원본 Mesh와 같은 월드 변환을 적용할 Transform을 가져옵니다.
            proxyTransform.SetParent(terrainCollisionProxyRoot, false); // 숨은 프록시 루트 아래에 배치합니다.
            proxyTransform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation); // 원본 Mesh의 월드 위치와 회전을 그대로 맞춥니다.
            proxyTransform.localScale = filter.transform.lossyScale; // 원본 Mesh의 최종 월드 스케일까지 동일하게 맞춥니다.

            MeshCollider proxyCollider = proxyObject.AddComponent<MeshCollider>(); // 실제 표면 질의에 사용할 MeshCollider를 추가합니다.
            proxyCollider.sharedMesh = filter.sharedMesh; // 새 Mesh를 만들지 않고 프로젝트의 원본 Mesh를 그대로 공유합니다.
            proxyCollider.convex = false; // 지형 표면은 오목한 정적 Mesh로 취급합니다.
            output.Add(proxyCollider); // 이후 도로 높이 투영과 Play Mode 충돌에 이 Collider를 사용합니다.
        }
    }

    /// <summary>
    /// 지형 모델 표면을 수직으로 샘플링해 현재 도로 중심선 높이를 맞춥니다.
    /// 제어점 자체를 바꾸지 않으므로 지형 모델을 제거하면 원래 제어점 높이가 다시 사용됩니다.
    /// </summary>
    private void ProjectRouteOntoTerrainModel(GameObject terrainVisual)
    {
        PrepareTerrainSurfaceColliders(terrainVisual, activeTerrainSurfaceColliders); // 기존 Collider 또는 공유 Mesh 프록시를 한 번 준비해 도로 전체에서 재사용합니다.

        if (activeTerrainSurfaceColliders.Count == 0)
        {
            Debug.LogWarning(
                $"[Mush] Terrain model '{terrainVisual.name}' has no usable Collider or MeshFilter. The road keeps its authored height.",
                activeAuthoring);
            return;
        }

        Bounds surfaceBounds = activeTerrainSurfaceColliders[0].bounds; // 모든 지형 조각을 포함하는 높이 범위를 계산합니다.
        for (int index = 1; index < activeTerrainSurfaceColliders.Count; index++)
            surfaceBounds.Encapsulate(activeTerrainSurfaceColliders[index].bounds);

        activeTerrainRayTop = surfaceBounds.max.y + Mathf.Max(50f, surfaceBounds.size.y + 10f); // 가장 높은 지형보다 충분히 위에서 시작합니다.
        activeTerrainRayDistance = Mathf.Max(100f, surfaceBounds.size.y + 120f); // 가장 낮은 지형까지 충분히 내려갑니다.

        int projectedCount = 0;
        for (int routeIndex = 0; routeIndex < routePoints.Count; routeIndex++)
        {
            Vector3 localPoint = routePoints[routeIndex];
            if (!TrySampleActiveTerrain(localPoint, out Vector3 sampledPoint, out _)) // 같은 XZ에 지형이 없으면 원래 높이를 유지합니다.
                continue;

            localPoint.y = sampledPoint.y; // 제어점 XZ는 보존하고 지형 표면의 Y만 사용합니다.
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
        localPoint = localQuery; // 실패하면 호출자가 원래 좌표를 그대로 사용할 수 있게 초기화합니다.
        localNormal = Vector3.up; // 실패 시 안전한 위쪽 노멀을 반환합니다.
        if (activeTerrainSurfaceColliders.Count == 0 || activeTerrainRayDistance <= 0f)
            return false;

        Vector3 worldQuery = transform.TransformPoint(localQuery); // 지형 Collider가 사용하는 월드 XZ로 변환합니다.
        Ray ray = new(new Vector3(worldQuery.x, activeTerrainRayTop, worldQuery.z), Vector3.down); // 같은 XZ에서 위에서 아래로 표면을 찾습니다.
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

        localPoint = transform.InverseTransformPoint(nearestHit.point); // 지형 실제 표면점을 맵 로컬 좌표로 되돌립니다.
        localNormal = transform.InverseTransformDirection(nearestHit.normal).normalized; // 도로 모델/리본이 필요하면 지형 노멀도 함께 사용할 수 있습니다.
        return true;
    }

    private void DestroyTerrainCollisionProxy()
    {
        if (terrainCollisionProxyRoot == null) // 이전 프록시가 없으면 아무것도 하지 않습니다.
            return;

        GameObject proxyObject = terrainCollisionProxyRoot.gameObject; // 제거할 임시 프록시 루트 오브젝트를 가져옵니다.
        terrainCollisionProxyRoot = null; // Destroy 중 재진입해도 같은 오브젝트를 다시 잡지 않도록 참조부터 비웁니다.

        if (Application.isPlaying) // Play Mode에서는 일반 Destroy를 사용합니다.
            Destroy(proxyObject); // 프레임 종료 시 임시 Collider 프록시를 제거합니다.
        else // Edit Mode에서는 즉시 제거할 수 있습니다.
            DestroyImmediate(proxyObject); // 씬에 임시 프록시가 남지 않도록 바로 제거합니다.
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
        Transform roadModelRoot = rebuiltRoot != null ? rebuiltRoot.Find(DeformedRoadRootName) : null; // 반복 배치된 도로 모델 루트를 찾습니다.
        bool hasRoadModel = roadModelRoot != null &&
                            activeAuthoring != null &&
                            activeAuthoring.HasRoadModel; // 실제 도로 모델이 지정되어 있고 인스턴스 루트도 존재하는지 확인합니다.
        bool hasTerrainModel = activeTerrainVisual != null; // 실제 사용할 지형 모델 인스턴스/씬 오브젝트가 있는지 확인합니다.
        bool showGeneratedRoad = !hasRoadModel; // 도로 모델이 없을 때만 가벼운 기본 리본 도로를 눈에 보이게 합니다.
        bool showGeneratedTerrain = !hasTerrainModel; // 지형 모델이 없을 때만 자동 생성 지형을 눈에 보이게 합니다.

        if (roadRenderer != null) // 기본 생성 도로 Renderer가 있으면 표시 상태를 적용합니다.
            roadRenderer.enabled = showGeneratedRoad; // 도로 모델이 있으면 기본 리본은 충돌용으로만 남기고 숨깁니다.

        if (terrainRenderer != null) // 기본 자동 지형 Renderer가 있으면 표시 상태를 적용합니다.
            terrainRenderer.enabled = showGeneratedTerrain; // 지형 모델이 있으면 자동 지형은 숨깁니다.

        Renderer leftTrackRenderer = FindGeneratedComponent<Renderer>("Left Sled Track"); // 왼쪽 기본 썰매 자국 Renderer를 찾습니다.
        Renderer rightTrackRenderer = FindGeneratedComponent<Renderer>("Right Sled Track"); // 오른쪽 기본 썰매 자국 Renderer를 찾습니다.
        if (leftTrackRenderer != null) // 왼쪽 자국이 있으면 도로 모델 표시와 겹치지 않게 맞춥니다.
            leftTrackRenderer.enabled = showGeneratedRoad; // 기본 리본 도로를 쓸 때만 표시합니다.
        if (rightTrackRenderer != null) // 오른쪽 자국이 있으면 동일하게 처리합니다.
            rightTrackRenderer.enabled = showGeneratedRoad; // 도로 모델이 있으면 모델 자체 외형을 그대로 보여줍니다.

        SetVisualRenderersEnabled(roadModelRoot, hasRoadModel); // 반복 배치된 도로 모델은 지정된 경우에만 보이게 합니다.
        SetVisualRenderersEnabled(activeTerrainVisual, hasTerrainModel); // 지정한 지형 모델은 별도 저장 위치 규칙 없이 그대로 보이게 합니다.

        Transform generatedTerrainTransform = rebuiltRoot != null ? rebuiltRoot.Find("VISIBLE Snow Terrain") : null; // 자동 지형 오브젝트를 찾습니다.
        MeshCollider generatedTerrainCollider = generatedTerrainTransform != null
            ? generatedTerrainTransform.GetComponent<MeshCollider>()
            : null; // 자동 지형의 충돌체를 가져옵니다.
        if (generatedTerrainCollider != null) // 지형 모델과 자동 지형 충돌이 겹치지 않게 합니다.
            generatedTerrainCollider.enabled = !hasTerrainModel; // 지형 모델이 있을 때는 자동 지형 Collider를 끕니다.
    }

    private GameObject ResolveCustomVisual(GameObject source, string generatedPrefix)
    {
        if (source != null && source.scene.IsValid()) // 사용자가 Hierarchy 어디에 놓은 씬 모델이면 그대로 사용합니다.
        {
            Transform oldPreviewRoot = transform.Find(CustomModelPreviewRootName); // 전에 Prefab 지형을 미리보기로 쓰고 있었다면 숨은 프리뷰만 정리합니다.
            if (oldPreviewRoot != null && !Application.isPlaying)
                DestroyImmediate(oldPreviewRoot.gameObject);
            return source; // 전용 폴더로 옮기거나 복제하지 않습니다.
        }

        Transform previewRoot = transform.Find(CustomModelPreviewRootName); // Prefab/FBX 슬롯용 저장되지 않는 미리보기 루트를 찾습니다.
        if (source == null)
        {
            if (previewRoot != null && !Application.isPlaying) // 모델 슬롯을 비웠다면 기존 임시 프리뷰만 정리합니다.
                DestroyImmediate(previewRoot.gameObject);
            return null;
        }

        string expectedName = generatedPrefix + source.name; // 현재 슬롯 모델과 재사용 프리뷰를 구분할 이름입니다.
        if (previewRoot == null)
        {
            GameObject previewRootObject = new(CustomModelPreviewRootName); // 전용 저장 위치가 아니라 단순 미리보기 컨테이너입니다.
            if (!Application.isPlaying)
                previewRootObject.hideFlags = HideFlags.HideAndDontSave; // 씬 저장 대상에서 완전히 제외합니다.
            previewRoot = previewRootObject.transform;
            previewRoot.SetParent(transform, false);
        }

        for (int index = previewRoot.childCount - 1; index >= 0; index--) // 이전 슬롯 모델 프리뷰를 정리하거나 같은 모델을 재사용합니다.
        {
            Transform child = previewRoot.GetChild(index);
            if (child.name == expectedName)
                return child.gameObject;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        GameObject instance = Instantiate(source, previewRoot, false); // 원본 Mesh/Material을 공유하는 가벼운 인스턴스만 만듭니다.
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
        if (sourceRoot == null || rebuiltRoot == null) // 도로 모델이나 생성 월드가 없으면 배치할 수 없습니다.
            return;

        MeshFilter[] sourceFilters = sourceRoot.GetComponentsInChildren<MeshFilter>(true); // 도로 모델이 공유할 원본 Mesh들을 찾습니다.
        if (sourceFilters.Length == 0 ||
            !TryMeasureRoadModule(sourceRoot, sourceFilters, out RoadModuleGeometry geometry) ||
            geometry.Length <= 0.1f) // 길이/폭을 측정할 수 없는 모델은 잘못된 도로 모델입니다.
        {
            Debug.LogWarning(
                $"[Mush] Road model '{sourceRoot.name}' needs at least one usable MeshFilter. Its local Z axis is treated as the road length.",
                activeAuthoring); // 모델 방향 규칙과 실패 이유를 Console에 알려줍니다.
            return;
        }

        Transform visualRoot = rebuiltRoot.Find(DeformedRoadRootName); // 기존 도로 모델 인스턴스 루트가 있으면 재사용합니다.
        if (visualRoot == null) // 처음 도로 모델을 지정했을 때만 루트를 새로 만듭니다.
        {
            GameObject visualRootObject = new(DeformedRoadRootName); // 반복 도로 모델을 정리할 전용 루트를 만듭니다.
            if (!Application.isPlaying)
                visualRootObject.hideFlags = HideFlags.HideAndDontSave; // 편집 미리보기 구간은 씬에 수백 개씩 저장하지 않습니다.
            visualRoot = visualRootObject.transform; // Transform을 가져옵니다.
            visualRoot.SetParent(rebuiltRoot, false); // 생성 월드 아래에 배치합니다.
        }

        string sourceMarker = $" [{sourceRoot.name}]"; // 현재 인스턴스들이 어느 도로 모델에서 만들어졌는지 이름으로 가볍게 식별합니다.
        if (visualRoot.childCount > 0 &&
            !visualRoot.GetChild(0).name.EndsWith(sourceMarker, StringComparison.Ordinal)) // 모델을 교체했는데 기존 구간이 남아 있으면 전부 새 모델로 바꿉니다.
        {
            for (int childIndex = visualRoot.childCount - 1; childIndex >= 0; childIndex--) // 기존 모델 인스턴스를 뒤에서부터 제거합니다.
            {
                Transform oldChild = visualRoot.GetChild(childIndex); // 제거할 이전 모델 구간을 가져옵니다.
                if (Application.isPlaying) // Play Mode에서는 일반 Destroy를 사용합니다.
                    Destroy(oldChild.gameObject); // 프레임 종료 시 이전 도로 모델을 제거합니다.
                else // Edit Mode에서는 즉시 제거합니다.
                    DestroyImmediate(oldChild.gameObject); // 모델 슬롯을 바꾼 즉시 이전 외형이 사라지게 합니다.
            }
        }

        float preferredSegmentLength = Mathf.Min(
            geometry.Length,
            Mathf.Max(2f, activeSampleSpacing * 2f)); // 급커브에서도 너무 긴 직선 조각이 되지 않도록 샘플 간격을 기준으로 구간 길이를 제한합니다.
        int segmentCount = Mathf.Clamp(
            Mathf.CeilToInt(activeCourseLength / Mathf.Max(0.1f, preferredSegmentLength)),
            1,
            256); // 씬 오브젝트 수가 폭증하지 않도록 최대 256구간으로 제한합니다.
        float segmentDistance = activeCourseLength / segmentCount; // 전체 도로 길이를 정확히 같은 수의 구간으로 나눕니다.
        float lateralScale = ActiveRoadHalfWidth / Mathf.Max(0.01f, geometry.RoadHalfWidth); // 선택한 모델 폭을 현재 도로 폭에 자동으로 맞춥니다.
        Vector3 sourceScale = sourceRoot.transform.localScale; // 모델이 원래 가진 기본 스케일을 보존합니다.
        Vector3 moduleCenter = new(
            geometry.RoadCenterX,
            geometry.SourceBaseY,
            (geometry.MinZ + geometry.MaxZ) * 0.5f); // 모델의 길이/폭 중심과 바닥 높이를 계산합니다.

        while (visualRoot.childCount < segmentCount) // 필요한 구간 수보다 기존 인스턴스가 적으면 부족한 만큼만 추가합니다.
        {
            int segmentIndex = visualRoot.childCount; // 새 인스턴스 번호를 현재 자식 수로 정합니다.
            GameObject instance = Instantiate(sourceRoot, visualRoot, false); // Mesh/Material 원본을 공유하는 일반 인스턴스만 만듭니다.
            instance.name = $"Road Module {segmentIndex + 1:000}{sourceMarker}"; // 구간 순서와 현재 원본 모델을 함께 기록해 교체를 자동 감지합니다.

            Transform[] instanceTransforms = instance.GetComponentsInChildren<Transform>(true); // 모델 안에 지형 조각이 같이 들어 있는 경우를 찾습니다.
            for (int childIndex = 0; childIndex < instanceTransforms.Length; childIndex++) // 모든 자식을 확인합니다.
            {
                Transform child = instanceTransforms[childIndex]; // 현재 자식을 가져옵니다.
                if (child != instance.transform &&
                    child.name.Contains("Terrain", StringComparison.OrdinalIgnoreCase)) // 도로 모델 안의 Terrain 이름 조각은 반복 배치하지 않습니다.
                {
                    child.gameObject.SetActive(false); // 실제 지형은 별도 지형 모델/자동 지형이 담당하므로 숨깁니다.
                }
            }
        }

        while (visualRoot.childCount > segmentCount) // 경로가 짧아져 필요 구간 수가 줄었으면 남는 인스턴스만 제거합니다.
        {
            Transform extra = visualRoot.GetChild(visualRoot.childCount - 1); // 맨 뒤의 초과 인스턴스를 가져옵니다.
            if (Application.isPlaying) // Play Mode에서는 일반 Destroy를 사용합니다.
                Destroy(extra.gameObject); // 프레임 종료 시 초과 구간을 제거합니다.
            else // Edit Mode에서는 바로 제거합니다.
                DestroyImmediate(extra.gameObject); // 즉시 Hierarchy에서 초과 구간을 없앱니다.
        }

        for (int segment = 0; segment < segmentCount; segment++) // 기존 인스턴스를 현재 곡선 위치에 맞춰 재배치합니다.
        {
            float startDistance = segment * segmentDistance; // 현재 구간의 시작 거리입니다.
            float endDistance = segment == segmentCount - 1
                ? activeCourseLength
                : (segment + 1) * segmentDistance; // 마지막 구간은 정확히 도로 끝에 맞춥니다.
            float segmentLength = Mathf.Max(0.01f, endDistance - startDistance); // 현재 구간의 실제 길이를 구합니다.
            float middleDistance = (startDistance + endDistance) * 0.5f; // 위치/회전을 잡을 구간 중앙 거리입니다.

            EvaluateRouteFrame(middleDistance, out Vector3 center, out _); // 지형 모델에 투영된 도로 중심 위치를 얻습니다.
            float directionSample = Mathf.Min(1f, segmentLength * 0.25f); // 진행 방향을 계산할 앞뒤 샘플 거리를 정합니다.
            EvaluateRouteFrame(Mathf.Max(0f, middleDistance - directionSample), out Vector3 before, out _); // 중앙보다 조금 전 위치를 구합니다.
            EvaluateRouteFrame(Mathf.Min(activeCourseLength, middleDistance + directionSample), out Vector3 after, out _); // 중앙보다 조금 뒤 위치를 구합니다.
            Vector3 forward = after - before; // 오르막/내리막까지 포함한 실제 3D 진행 방향을 계산합니다.
            if (forward.sqrMagnitude < 0.0001f) // 거의 같은 위치라 방향을 만들 수 없으면 기본 방향을 사용합니다.
                forward = Vector3.back; // 기존 도로 모델 방향과 맞는 안전한 진행 방향입니다.
            forward.Normalize(); // 회전에 사용할 단위 방향으로 만듭니다.

            Vector3 roadUp = Vector3.up; // 기본 자동 지형에서는 월드 위쪽을 도로의 위 방향으로 사용합니다.
            if (activeTerrainVisual != null && TrySampleActiveTerrain(center, out _, out Vector3 terrainNormal)) // 지형 모델이 있으면 실제 표면 노멀을 읽습니다.
                roadUp = terrainNormal.sqrMagnitude > 0.0001f ? terrainNormal.normalized : Vector3.up; // 경사면에서도 도로 모델이 표면 기울기를 따라 눕게 합니다.

            Quaternion rotation = Quaternion.LookRotation(-forward, roadUp); // 진행 방향뿐 아니라 지형 표면 기울기까지 반영해 도로 모델을 배치합니다.
            float longitudinalScale = segmentLength / geometry.Length; // 원본 도로 모델 길이를 현재 구간 길이에만 맞춰 늘이거나 줄입니다.
            Vector3 instanceScale = new(
                sourceScale.x * lateralScale,
                sourceScale.y,
                sourceScale.z * longitudinalScale); // 폭/길이만 자동 조절하고 모델 높이 스케일은 보존합니다.

            Transform instanceTransform = visualRoot.GetChild(segment); // 새로 만들지 않고 기존 구간 인스턴스를 가져옵니다.
            instanceTransform.localRotation = rotation; // 현재 도로의 3D 진행 방향에 맞춰 회전시킵니다.
            instanceTransform.localScale = instanceScale; // 현재 구간 길이와 도로 폭에 맞춰 스케일을 적용합니다.
            instanceTransform.localPosition =
                center + roadUp * 0.10f - rotation * Vector3.Scale(moduleCenter, instanceScale); // 지형 노멀 방향으로 조금 띄워 모델 바닥이 실제 표면에 밀착되게 합니다.
            instanceTransform.gameObject.SetActive(true); // 재사용 인스턴스가 혹시 비활성화돼 있었다면 다시 켭니다.
        }
    }

    private void RefreshRoadModelInstances()
    {
        if (rebuiltRoot == null) // 생성 월드가 없으면 도로 모델 인스턴스를 관리할 수 없습니다.
            return;

        Transform existingRoot = rebuiltRoot.Find(DeformedRoadRootName); // 이전 도로 모델 인스턴스 루트를 찾습니다.

        if (activeAuthoring == null || !activeAuthoring.HasRoadModel) // 도로 모델이 None이면 기존 반복 모델을 제거합니다.
        {
            if (existingRoot != null) // 이전에 모델을 사용했던 경우에만 제거합니다.
            {
                if (Application.isPlaying) // Play Mode에서는 일반 Destroy를 사용합니다.
                    Destroy(existingRoot.gameObject); // 프레임 종료 시 기존 도로 모델 인스턴스를 제거합니다.
                else // Edit Mode에서는 즉시 제거합니다.
                    DestroyImmediate(existingRoot.gameObject); // Scene View에서 바로 기본 리본 도로로 돌아오게 합니다.
            }

            return; // 모델이 없으므로 추가 배치는 하지 않습니다.
        }

        BuildDeformedRoadModule(activeAuthoring.RoadModel); // 지정된 도로 모델 인스턴스를 현재 경로/지형 높이에 맞춰 갱신합니다.
    }

    public void InvalidateRoadModelInstances()
    {
        if (rebuiltRoot == null) // 생성 월드가 아직 연결되지 않았다면 지울 것도 없습니다.
            rebuiltRoot = transform.Find(GeneratedWorldRootName); // 현재 씬의 생성 월드를 다시 찾아봅니다.

        if (rebuiltRoot == null) // 생성 월드 자체가 없으면 안전하게 끝냅니다.
            return;

        Transform existingRoot = rebuiltRoot.Find(DeformedRoadRootName); // 이전 모델로 만든 반복 인스턴스 루트를 찾습니다.
        if (existingRoot == null) // 도로 모델을 한 번도 사용하지 않았다면 아무것도 하지 않습니다.
            return;

        if (Application.isPlaying) // Play Mode에서는 일반 Destroy를 사용합니다.
            Destroy(existingRoot.gameObject); // 프레임 종료 시 이전 모델 인스턴스를 제거합니다.
        else // Edit Mode에서는 즉시 제거합니다.
            DestroyImmediate(existingRoot.gameObject); // 모델 교체 즉시 새 모델로 다시 만들 수 있게 지웁니다.
    }

    private static bool TryMeasureRoadModule(
        GameObject sourceRoot,
        IReadOnlyList<MeshFilter> sourceFilters,
        out RoadModuleGeometry geometry)
    {
        float minZ = float.PositiveInfinity; // 도로 모델 전체의 로컬 Z 최소값을 찾습니다.
        float maxZ = float.NegativeInfinity; // 도로 모델 전체의 로컬 Z 최대값을 찾습니다.
        float minX = float.PositiveInfinity; // 도로 모델 전체의 로컬 X 최소값을 찾습니다.
        float maxX = float.NegativeInfinity; // 도로 모델 전체의 로컬 X 최대값을 찾습니다.
        float minY = float.PositiveInfinity; // 도로 모델 바닥 높이를 찾습니다.
        bool foundRoad = false; // 실제 사용할 Mesh를 하나라도 측정했는지 기록합니다.

        for (int filterIndex = 0; filterIndex < sourceFilters.Count; filterIndex++) // 도로 모델 안의 모든 MeshFilter를 확인합니다.
        {
            MeshFilter filter = sourceFilters[filterIndex]; // 현재 MeshFilter를 가져옵니다.
            Mesh mesh = filter != null ? filter.sharedMesh : null; // 원본 공유 Mesh를 가져옵니다.
            if (mesh == null) // Mesh가 없는 필터는 측정할 수 없습니다.
                continue;

            if (filter.name.Contains("Terrain", StringComparison.OrdinalIgnoreCase)) // FBX에 도로와 지형이 같이 들어 있다면 Terrain 이름 조각은 폭/길이 측정에서 제외합니다.
                continue;

            Bounds bounds = mesh.bounds; // Read/Write 옵션과 무관하게 사용할 수 있는 원본 Mesh 로컬 Bounds를 가져옵니다.
            Matrix4x4 sourceToModule =
                sourceRoot.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix; // 각 Mesh의 로컬 Bounds를 도로 모델 루트 로컬 좌표로 바꿀 행렬입니다.

            for (int corner = 0; corner < 8; corner++) // Bounds의 8개 꼭짓점만 변환해 전체 크기를 매우 가볍게 측정합니다.
            {
                Vector3 localCorner = new(
                    (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (corner & 4) == 0 ? bounds.min.z : bounds.max.z); // 현재 Bounds 모서리 좌표를 만듭니다.
                Vector3 point = sourceToModule.MultiplyPoint3x4(localCorner); // 도로 모델 루트 기준 좌표로 변환합니다.

                minX = Mathf.Min(minX, point.x); // 전체 왼쪽 끝을 갱신합니다.
                maxX = Mathf.Max(maxX, point.x); // 전체 오른쪽 끝을 갱신합니다.
                minY = Mathf.Min(minY, point.y); // 모델 바닥 높이를 갱신합니다.
                minZ = Mathf.Min(minZ, point.z); // 모델 길이 시작점을 갱신합니다.
                maxZ = Mathf.Max(maxZ, point.z); // 모델 길이 끝점을 갱신합니다.
            }

            foundRoad = true; // 사용할 수 있는 Mesh 하나를 정상 측정했습니다.
        }

        float length = maxZ - minZ; // 모델 로컬 Z 방향의 실제 길이를 계산합니다.
        float width = maxX - minX; // 모델 로컬 X 방향의 실제 폭을 계산합니다.
        if (!foundRoad || length < 0.1f || width < 0.1f) // 길이나 폭이 사실상 0이면 도로 모듈로 사용할 수 없습니다.
        {
            geometry = default; // 실패한 경우 기본 구조체를 반환합니다.
            return false; // 호출자에게 배치 불가를 알립니다.
        }

        float roadCenterX = (minX + maxX) * 0.5f; // 모델 폭의 중심 X를 계산합니다.
        geometry = new RoadModuleGeometry(
            minZ,
            maxZ,
            roadCenterX,
            width * 0.5f,
            width * 0.5f,
            minY); // 바닥 Y를 도로 표면에 맞추도록 최소 Y를 기준으로 저장합니다.
        return true; // 도로 모델의 길이/폭 측정이 성공했습니다.
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
        bool conformToTerrainModel = activeTerrainVisual != null && activeTerrainSurfaceColliders.Count > 0; // 지형 모델이 있으면 도로 양쪽 끝까지 실제 표면에 맞춥니다.

        for (int index = 0; index < count; index++)
        {
            Vector3 right = RouteRight(index);
            Vector3 baseCenter = routePoints[index] + right * lateralOffset;
            Vector3 left = baseCenter - right * halfWidth;
            Vector3 rightPoint = baseCenter + right * halfWidth;

            if (conformToTerrainModel) // 중심 Y만 맞추는 것이 아니라 도로 폭 양쪽을 각각 지형 표면에 투영합니다.
            {
                if (TrySampleActiveTerrain(left, out Vector3 sampledLeft, out _))
                    left.y = sampledLeft.y;
                if (TrySampleActiveTerrain(rightPoint, out Vector3 sampledRight, out _))
                    rightPoint.y = sampledRight.y;
            }

            left.y += yLift; // 지형과 정확히 겹쳐 Z-fighting이 생기지 않도록 아주 조금만 띄웁니다.
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

    private void InsertTerrainSurfacePoint(Vector3 point)
    {
        for (int index = 0; index < terrainSurfacePoints.Count; index++)
        {
            Vector3 existing = terrainSurfacePoints[index];
            if (new Vector2(existing.x - point.x, existing.z - point.z).sqrMagnitude < 0.0001f)
                return;
        }

        int pointIndex = terrainSurfacePoints.Count;
        terrainSurfacePoints.Add(point);
        bool inserted = false;
        // Split every incident face when the point lies on a shared edge.
        // Only the surface is changed; the perimeter list is never reordered.
        for (int triangle = terrainTriangleIndices.Count - 3; triangle >= 0; triangle -= 3)
        {
            int a = terrainTriangleIndices[triangle];
            int b = terrainTriangleIndices[triangle + 1];
            int c = terrainTriangleIndices[triangle + 2];
            if (!PointInsideTriangleXZ(point, terrainSurfacePoints[a], terrainSurfacePoints[b], terrainSurfacePoints[c]))
                continue;
            terrainTriangleIndices.RemoveRange(triangle, 3);
            AddTerrainFace(terrainSurfacePoints, terrainTriangleIndices, a, b, pointIndex);
            AddTerrainFace(terrainSurfacePoints, terrainTriangleIndices, b, c, pointIndex);
            AddTerrainFace(terrainSurfacePoints, terrainTriangleIndices, c, a, pointIndex);
            inserted = true;
        }
        if (!inserted)
            terrainSurfacePoints.RemoveAt(pointIndex);
    }

    private static void AddTerrainFace(
        IReadOnlyList<Vector3> points, List<int> indices, int a, int b, int c)
    {
        float area = CrossXZ(points[a], points[b], points[c]);
        if (Mathf.Abs(area) < 0.000001f)
            return;
        indices.Add(a);
        indices.Add(area < 0f ? b : c);
        indices.Add(area < 0f ? c : b);
    }

    private void FitTerrainBelowRoad()
    {
        // These footprints use exactly the same cross-sections and diagonal
        // as BuildRibbonMesh. Merely lowering nearby vertices would still let
        // a large terrain triangle bridge over the road between those vertices.
        List<TerrainRoadCut> cuts = new((routePoints.Count - 1) * 2);
        for (int index = 0; index < routePoints.Count - 1; index++)
        {
            Vector3 right = RouteRight(index) * ActiveRoadHalfWidth;
            Vector3 nextRight = RouteRight(index + 1) * ActiveRoadHalfWidth;
            Vector3 center = routePoints[index] - Vector3.up * 0.18f;
            Vector3 nextCenter = routePoints[index + 1] - Vector3.up * 0.18f;
            cuts.Add(new TerrainRoadCut(center - right, nextCenter - nextRight, center + right));
            cuts.Add(new TerrainRoadCut(center + right, nextCenter - nextRight, nextCenter + nextRight));
        }

        List<Vector3> finalPoints = new();
        List<int> finalIndices = new();
        Dictionary<Vector2Int, int> vertexLookup = new();
        for (int triangle = 0; triangle < terrainTriangleIndices.Count; triangle += 3)
        {
            List<List<Vector3>> pieces = new()
            {
                new List<Vector3>
                {
                    terrainSurfacePoints[terrainTriangleIndices[triangle]],
                    terrainSurfacePoints[terrainTriangleIndices[triangle + 1]],
                    terrainSurfacePoints[terrainTriangleIndices[triangle + 2]],
                },
            };
            for (int cutIndex = 0; cutIndex < cuts.Count; cutIndex++)
            {
                TerrainRoadCut cut = cuts[cutIndex];
                for (int pieceIndex = pieces.Count - 1; pieceIndex >= 0; pieceIndex--)
                {
                    List<Vector3> polygon = pieces[pieceIndex];
                    if (!cut.NeedsTerrainCut(polygon) || !TryPartitionTerrainPolygon(polygon, cut, out List<List<Vector3>> split))
                        continue;
                    pieces.RemoveAt(pieceIndex);
                    pieces.AddRange(split);
                }
            }

            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                List<Vector3> polygon = pieces[pieceIndex];
                if (polygon.Count < 3)
                    continue;
                int first = GetTerrainSurfaceVertex(polygon[0], cuts, finalPoints, vertexLookup);
                int previous = GetTerrainSurfaceVertex(polygon[1], cuts, finalPoints, vertexLookup);
                for (int vertex = 2; vertex < polygon.Count; vertex++)
                {
                    int current = GetTerrainSurfaceVertex(polygon[vertex], cuts, finalPoints, vertexLookup);
                    AddTerrainFace(finalPoints, finalIndices, first, previous, current);
                    previous = current;
                }
            }
        }
        StitchTerrainSurfaceEdges(finalPoints, finalIndices);
        terrainSurfacePoints.Clear();
        terrainSurfacePoints.AddRange(finalPoints);
        terrainTriangleIndices.Clear();
        terrainTriangleIndices.AddRange(finalIndices);
    }

    private void TessellateEditableTerrain(float maximumEdgeLength)
    {
        if (terrainTriangleIndices.Count < 3 || maximumEdgeLength <= 0f)
            return;

        float longestEdge = 0f;
        for (int triangle = 0; triangle < terrainTriangleIndices.Count; triangle += 3)
        {
            Vector3 a = terrainSurfacePoints[terrainTriangleIndices[triangle]];
            Vector3 b = terrainSurfacePoints[terrainTriangleIndices[triangle + 1]];
            Vector3 c = terrainSurfacePoints[terrainTriangleIndices[triangle + 2]];
            longestEdge = Mathf.Max(
                longestEdge,
                Vector3.Distance(a, b),
                Vector3.Distance(b, c),
                Vector3.Distance(c, a));
        }

        int subdivisions = Mathf.CeilToInt(longestEdge / maximumEdgeLength);
        if (subdivisions <= 1)
            return;
        subdivisions = Mathf.Min(subdivisions, 64);

        List<Vector3> tessellatedPoints = new();
        List<int> tessellatedTriangles = new();
        Dictionary<Vector3Int, int> pointLookup = new();
        float inverseSubdivisions = 1f / subdivisions;

        for (int triangle = 0; triangle < terrainTriangleIndices.Count; triangle += 3)
        {
            Vector3 a = terrainSurfacePoints[terrainTriangleIndices[triangle]];
            Vector3 b = terrainSurfacePoints[terrainTriangleIndices[triangle + 1]];
            Vector3 c = terrainSurfacePoints[terrainTriangleIndices[triangle + 2]];
            int[,] grid = new int[subdivisions + 1, subdivisions + 1];

            for (int row = 0; row <= subdivisions; row++)
            for (int column = 0; column <= subdivisions - row; column++)
            {
                Vector3 point = a +
                                (b - a) * (row * inverseSubdivisions) +
                                (c - a) * (column * inverseSubdivisions);
                grid[row, column] = GetTessellatedTerrainPoint(
                    point,
                    tessellatedPoints,
                    pointLookup);
            }

            for (int row = 0; row < subdivisions; row++)
            for (int column = 0; column < subdivisions - row; column++)
            {
                int lowerLeft = grid[row, column];
                int lowerRight = grid[row + 1, column];
                int upperLeft = grid[row, column + 1];
                AddTerrainFace(
                    tessellatedPoints,
                    tessellatedTriangles,
                    lowerLeft,
                    lowerRight,
                    upperLeft);

                if (column >= subdivisions - row - 1)
                    continue;
                int upperRight = grid[row + 1, column + 1];
                AddTerrainFace(
                    tessellatedPoints,
                    tessellatedTriangles,
                    lowerRight,
                    upperRight,
                    upperLeft);
            }
        }

        terrainSurfacePoints.Clear();
        terrainSurfacePoints.AddRange(tessellatedPoints);
        terrainTriangleIndices.Clear();
        terrainTriangleIndices.AddRange(tessellatedTriangles);
    }

    private static int GetTessellatedTerrainPoint(
        Vector3 point,
        List<Vector3> points,
        Dictionary<Vector3Int, int> lookup)
    {
        Vector3Int key = new(
            Mathf.RoundToInt(point.x * 1000f),
            Mathf.RoundToInt(point.y * 1000f),
            Mathf.RoundToInt(point.z * 1000f));
        if (lookup.TryGetValue(key, out int existing))
            return existing;

        int index = points.Count;
        points.Add(point);
        lookup.Add(key, index);
        return index;
    }

    private static void StitchTerrainSurfaceEdges(List<Vector3> points, List<int> triangles)
    {
        // A road end can touch the edge of a neighbouring terrain face without
        // intersecting its area. Split that neighbour too, so a lowered road
        // edge cannot leave a T-junction and an open seam in the heightfield.
        const float cellSize = 32f;
        Dictionary<Vector2Int, List<int>> cells = new();
        for (int index = 0; index < points.Count; index++)
        {
            Vector3 point = points[index];
            Vector2Int cell = new(Mathf.FloorToInt(point.x / cellSize), Mathf.FloorToInt(point.z / cellSize));
            if (!cells.TryGetValue(cell, out List<int> entries))
            {
                entries = new List<int>();
                cells.Add(cell, entries);
            }
            entries.Add(index);
        }
        Stack<(int A, int B, int C)> pending = new();
        for (int index = 0; index < triangles.Count; index += 3)
            pending.Push((triangles[index], triangles[index + 1], triangles[index + 2]));
        triangles.Clear();
        while (pending.Count > 0)
        {
            (int a, int b, int c) = pending.Pop();
            if (Mathf.Abs(CrossXZ(points[a], points[b], points[c])) < 0.000001f)
                continue;
            int split = FindTerrainEdgeVertex(points, cells, cellSize, a, b, c);
            if (split >= 0)
            {
                pending.Push((a, split, c));
                pending.Push((split, b, c));
                continue;
            }
            split = FindTerrainEdgeVertex(points, cells, cellSize, b, c, a);
            if (split >= 0)
            {
                pending.Push((b, split, a));
                pending.Push((split, c, a));
                continue;
            }
            split = FindTerrainEdgeVertex(points, cells, cellSize, c, a, b);
            if (split >= 0)
            {
                pending.Push((c, split, b));
                pending.Push((split, a, b));
                continue;
            }
            AddTerrainFace(points, triangles, a, b, c);
        }
    }

    private static int FindTerrainEdgeVertex(
        IReadOnlyList<Vector3> points,
        Dictionary<Vector2Int, List<int>> cells,
        float cellSize, int a, int b, int opposite)
    {
        Vector2 start = new(points[a].x, points[a].z);
        Vector2 end = new(points[b].x, points[b].z);
        Vector2 segment = end - start;
        if (segment.sqrMagnitude < 0.000001f)
            return -1;
        int minX = Mathf.FloorToInt((Mathf.Min(start.x, end.x) - 0.001f) / cellSize);
        int maxX = Mathf.FloorToInt((Mathf.Max(start.x, end.x) + 0.001f) / cellSize);
        int minZ = Mathf.FloorToInt((Mathf.Min(start.y, end.y) - 0.001f) / cellSize);
        int maxZ = Mathf.FloorToInt((Mathf.Max(start.y, end.y) + 0.001f) / cellSize);
        float nearestT = 1f;
        int selected = -1;
        for (int x = minX; x <= maxX; x++)
        for (int z = minZ; z <= maxZ; z++)
        {
            if (!cells.TryGetValue(new Vector2Int(x, z), out List<int> entries))
                continue;
            for (int index = 0; index < entries.Count; index++)
            {
                int candidate = entries[index];
                if (candidate == a || candidate == b || candidate == opposite)
                    continue;
                Vector2 point = new(points[candidate].x, points[candidate].z);
                if ((point - start).sqrMagnitude < 0.000001f || (point - end).sqrMagnitude < 0.000001f)
                    continue;
                float t = Vector2.Dot(point - start, segment) / segment.sqrMagnitude;
                if (t <= 0f || t >= nearestT || (point - (start + segment * t)).sqrMagnitude > 0.000001f)
                    continue;
                nearestT = t;
                selected = candidate;
            }
        }
        return selected;
    }

    private static int GetTerrainSurfaceVertex(
        Vector3 point,
        IReadOnlyList<TerrainRoadCut> cuts,
        List<Vector3> vertices,
        Dictionary<Vector2Int, int> lookup)
    {
        Vector2Int key = new(Mathf.RoundToInt(point.x * 1000f), Mathf.RoundToInt(point.z * 1000f));
        bool exists = lookup.TryGetValue(key, out int index);
        if (exists)
        {
            Vector3 existing = vertices[index];
            point = new Vector3(existing.x, Mathf.Min(existing.y, point.y), existing.z);
        }
        for (int cutIndex = 0; cutIndex < cuts.Count; cutIndex++)
        {
            if (cuts[cutIndex].TryGetCeiling(point, out float ceiling))
                point.y = Mathf.Min(point.y, ceiling);
        }
        if (exists)
        {
            vertices[index] = point;
            return index;
        }
        index = vertices.Count;
        vertices.Add(point);
        lookup.Add(key, index);
        return index;
    }

    private static bool TryPartitionTerrainPolygon(
        List<Vector3> polygon, TerrainRoadCut cut, out List<List<Vector3>> pieces)
    {
        pieces = new List<List<Vector3>>();
        List<Vector3> remainder = polygon;
        Vector3[] boundary = { cut.A, cut.B, cut.C };
        for (int edge = 0; edge < 3 && remainder.Count >= 3; edge++)
        {
            Vector3 start = boundary[edge];
            Vector3 end = boundary[(edge + 1) % 3];
            List<Vector3> outside = ClipTerrainPolygon(remainder, start, end, false);
            if (TerrainPolygonHasArea(outside))
                pieces.Add(outside);
            remainder = ClipTerrainPolygon(remainder, start, end, true);
        }
        // Do not slice the terrain along infinite extensions of road edges
        // when the road triangle does not actually intersect this polygon.
        if (!TerrainPolygonHasArea(remainder))
            return false;
        pieces.Add(remainder);
        return true;
    }

    private static List<Vector3> ClipTerrainPolygon(
        List<Vector3> polygon, Vector3 start, Vector3 end, bool keepInside)
    {
        List<Vector3> result = new(polygon.Count + 1);
        if (polygon.Count == 0)
            return result;
        Vector3 previous = polygon[^1];
        float previousDistance = CrossXZ(start, end, previous);
        bool previousInside = keepInside ? previousDistance <= 0f : previousDistance >= 0f;
        for (int index = 0; index < polygon.Count; index++)
        {
            Vector3 current = polygon[index];
            float distance = CrossXZ(start, end, current);
            bool inside = keepInside ? distance <= 0f : distance >= 0f;
            if (inside != previousInside)
            {
                float t = previousDistance / (previousDistance - distance);
                AppendTerrainPolygonVertex(result, Vector3.Lerp(previous, current, Mathf.Clamp01(t)));
            }
            if (inside)
                AppendTerrainPolygonVertex(result, current);
            previous = current;
            previousDistance = distance;
            previousInside = inside;
        }
        if (result.Count > 1 && (result[0] - result[^1]).sqrMagnitude < 0.00000001f)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static void AppendTerrainPolygonVertex(List<Vector3> points, Vector3 point)
    {
        if (points.Count == 0 || (points[^1] - point).sqrMagnitude >= 0.00000001f)
            points.Add(point);
    }

    private static bool TerrainPolygonHasArea(List<Vector3> points)
    {
        if (points.Count < 3)
            return false;
        float area = 0f;
        for (int index = 1; index < points.Count - 1; index++)
            area += CrossXZ(points[0], points[index], points[index + 1]);
        return Mathf.Abs(area) > 0.000001f;
    }

    private readonly struct TerrainRoadCut
    {
        public readonly Vector3 A;
        public readonly Vector3 B;
        public readonly Vector3 C;
        private readonly Vector2 minimum;
        private readonly Vector2 maximum;

        public TerrainRoadCut(Vector3 a, Vector3 b, Vector3 c)
        {
            A = a;
            B = CrossXZ(a, b, c) <= 0f ? b : c;
            C = CrossXZ(a, b, c) <= 0f ? c : b;
            minimum = new Vector2(Mathf.Min(a.x, b.x, c.x), Mathf.Min(a.z, b.z, c.z));
            maximum = new Vector2(Mathf.Max(a.x, b.x, c.x), Mathf.Max(a.z, b.z, c.z));
        }

        public bool NeedsTerrainCut(List<Vector3> polygon)
        {
            float minX = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;
            float maximumHeight = float.NegativeInfinity;
            for (int index = 0; index < polygon.Count; index++)
            {
                minX = Mathf.Min(minX, polygon[index].x);
                minZ = Mathf.Min(minZ, polygon[index].z);
                maxX = Mathf.Max(maxX, polygon[index].x);
                maxZ = Mathf.Max(maxZ, polygon[index].z);
                maximumHeight = Mathf.Max(maximumHeight, polygon[index].y);
            }
            return maximumHeight > Mathf.Min(A.y, B.y, C.y) &&
                   maxX >= minimum.x && minX <= maximum.x && maxZ >= minimum.y && minZ <= maximum.y;
        }

        public bool TryGetCeiling(Vector3 point, out float height)
        {
            height = 0f;
            if (point.x < minimum.x - 0.001f || point.x > maximum.x + 0.001f ||
                point.z < minimum.y - 0.001f || point.z > maximum.y + 0.001f)
                return false;
            float area = CrossXZ(A, B, C);
            if (Mathf.Abs(area) < 0.000001f)
                return false;
            float a = CrossXZ(B, C, point) / area;
            float b = CrossXZ(C, A, point) / area;
            float c = 1f - a - b;
            if (a < -0.0001f || b < -0.0001f || c < -0.0001f)
                return false;
            height = A.y * a + B.y * b + C.y * c;
            return true;
        }
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
            Vector3 a = terrainSurfacePoints[terrainTriangleIndices[triangleIndex]];
            Vector3 b = terrainSurfacePoints[terrainTriangleIndices[triangleIndex + 1]];
            Vector3 c = terrainSurfacePoints[terrainTriangleIndices[triangleIndex + 2]];
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
        // The baked mesh needs short collider edges, but the route data does
        // not need this extra topology during every Play Mode startup.
        TessellateEditableTerrain(200f);
        Vector3[] vertices = terrainSurfacePoints.ToArray();
        Vector2[] uv = new Vector2[vertices.Length];
        for (int index = 0; index < vertices.Length; index++)
            uv[index] = new Vector2(vertices[index].x * 0.08f, vertices[index].z * 0.08f);

        Mesh mesh = new() { name = "Editable Terrain Surface" };
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
