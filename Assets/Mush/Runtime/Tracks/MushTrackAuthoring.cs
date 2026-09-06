using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene-owned prototype track data. Control points are stored in the target
/// map root's local space, so art and VFX can be replaced without changing the
/// gameplay route.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Mush/Mush Map Editor")]
public sealed class MushTrackAuthoring : MonoBehaviour
{
    [SerializeField, HideInInspector] private Transform mapRoot;
    [SerializeField] private bool useEditablePath;
    [SerializeField, Min(1f)] private float sampleSpacing = MushTrackPathUtility.DefaultSampleSpacing;
    [SerializeField] private bool overrideTrackWidths;
    [SerializeField, Min(0.5f)] private float roadHalfWidth = 6.5f;
    [SerializeField, Min(4f)] private float terrainHalfWidth = 105f;
    [SerializeField] private GameObject deformableRoadModule;
    [SerializeField] private bool useDeformableRoadModule; // false면 원래의 매끈한 스크립트 생성 도로를 기본으로 사용합니다.
    [SerializeField] private GameObject customTerrainVisual;
    [SerializeField] private Material roadMaterialOverride;
    [SerializeField] private Material terrainMaterialOverride;
    [SerializeField] private Texture roadTextureOverride;
    [SerializeField] private Texture terrainTextureOverride;
    [SerializeField] private List<Vector3> controlPoints = new();
    [SerializeField] private bool generateProceduralEnvironment = true;

    public bool UsesEditablePath => useEditablePath && controlPoints.Count >= 2;
    public int ControlPointCount => controlPoints.Count;
    public bool GenerateProceduralEnvironment => generateProceduralEnvironment;
    public float RoadHalfWidth => roadHalfWidth;
    public float TerrainHalfWidth => terrainHalfWidth;
    public bool OverridesTrackWidths => overrideTrackWidths;
    public GameObject DeformableRoadModule => deformableRoadModule;
    public GameObject RoadModel => deformableRoadModule; // 도로 외형 교체에 사용할 구간 모델입니다.
    public bool UsesDeformableRoadModule => useDeformableRoadModule && deformableRoadModule != null; // 사용 토글이 켜진 모델만 가벼운 구간 인스턴스로 사용합니다.
    public bool HasRoadModel => useDeformableRoadModule && deformableRoadModule != null;
    public GameObject CustomTerrainVisual => customTerrainVisual;
    public Material RoadMaterialOverride => roadMaterialOverride;
    public Material TerrainMaterialOverride => terrainMaterialOverride;
    public Texture RoadTextureOverride => roadTextureOverride;
    public Texture TerrainTextureOverride => terrainTextureOverride;
    public bool HasCustomTerrainVisual => customTerrainVisual != null;
    public bool HasDeformableRoadModule => deformableRoadModule != null;
    public float PreviewRoadHalfWidth => overrideTrackWidths
        ? Mathf.Max(0.5f, roadHalfWidth)
        : 6.5f;

    private void Reset()
    {
        ConfigureNewMapDefaults();
    }

    public void ConfigureNewMapDefaults()
    {
        mapRoot = transform;
        generateProceduralEnvironment = false;
        BakeDefaultPath();
    }

    public static MushTrackAuthoring FindFor(Transform mapRoot)
    {
        if (mapRoot == null)
            return null;

        MushTrackAuthoring[] candidates = FindObjectsByType<MushTrackAuthoring>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < candidates.Length; index++)
        {
            MushTrackAuthoring candidate = candidates[index];
            if (candidate != null && candidate.AppliesTo(mapRoot))
                return candidate;
        }
        return null;
    }

    public bool AppliesTo(Transform mapRoot)
    {
        return mapRoot != null && ResolveMapRoot() == mapRoot;
    }

    public Transform ResolveMapRoot()
    {
        if (mapRoot != null && mapRoot.gameObject.scene == gameObject.scene)
            return mapRoot;

        MushCurvedMapRuntime localRuntime = GetComponent<MushCurvedMapRuntime>();
        if (localRuntime != null)
            return transform;

        MushCurvedMapRuntime parentRuntime = GetComponentInParent<MushCurvedMapRuntime>();
        if (parentRuntime != null)
            return parentRuntime.transform;

        Scene scene = gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        Transform onlyRuntimeRoot = null;
        int runtimeRootCount = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int index = 0; index < roots.Length; index++)
        {
            MushCurvedMapRuntime[] runtimes = roots[index].GetComponentsInChildren<MushCurvedMapRuntime>(true);
            for (int runtimeIndex = 0; runtimeIndex < runtimes.Length; runtimeIndex++)
            {
                runtimeRootCount++;
                onlyRuntimeRoot = runtimes[runtimeIndex].transform;
            }
        }

        if (runtimeRootCount == 1)
            return onlyRuntimeRoot;

        // A newly added component owns the object it is attached to. This is
        // the normal authoring path and requires no map ID or map category.
        return transform;
    }

    public void SetMapRoot(Transform root)
    {
        mapRoot = root;
    }

    public bool TryBuildSampledRoute(
        List<Vector3> output,
        out float routeLength,
        out float actualSampleSpacing)
    {
        output.Clear();
        routeLength = 0f;
        actualSampleSpacing = sampleSpacing;
        if (!UsesEditablePath)
            return false;

        return MushTrackPathUtility.BuildUniformRoute(
            controlPoints,
            sampleSpacing,
            output,
            out routeLength,
            out actualSampleSpacing);
    }

    public void CopyPreviewRoute(List<Vector3> output)
    {
        if (UsesEditablePath)
        {
            if (MushTrackPathUtility.BuildUniformRoute(
                    controlPoints,
                    sampleSpacing,
                    output,
                    out _,
                    out _))
                return;
        }

        MushTrackPathUtility.BuildDefaultRoute(output);
    }

    public void CopyEditableControlPointPreview(List<Vector3> output)
    {
        output.Clear();
        if (UsesEditablePath)
        {
            output.AddRange(controlPoints);
            return;
        }

        MushTrackPathUtility.BuildEditableDefaultRoute(output);
    }

    public Vector3 GetControlPoint(int index) => controlPoints[index];

    public void SetControlPoint(int index, Vector3 point)
    {
        if (index >= 0 && index < controlPoints.Count)
            controlPoints[index] = point;
    }

    public void BakeDefaultPath()
    {
        MushTrackPathUtility.BuildEditableDefaultRoute(controlPoints);
        useEditablePath = controlPoints.Count >= 2;
    }


    public void SetEditablePathEnabled(bool enabled)
    {
        useEditablePath = enabled && controlPoints.Count >= 2;
    }


    public void SetDeformableRoadModule(GameObject module)
    {
        deformableRoadModule = module;
        useDeformableRoadModule = module != null; // 기존 직렬화 토글도 함께 맞춰 외부 코드와의 호환을 유지합니다.
    }

    public void SetTerrainModel(GameObject model)
    {
        customTerrainVisual = model; // 지형 모델이 있으면 런타임이 도로를 그 표면에 투영합니다.
    }

    public void SetProceduralEnvironmentEnabled(bool enabled)
    {
        generateProceduralEnvironment = enabled;
    }

    public int InsertControlPointAfter(int index)
    {
        if (controlPoints.Count < 2)
        {
            BakeDefaultPath();
            return Mathf.Clamp(index, 0, controlPoints.Count - 1);
        }

        index = Mathf.Clamp(index, 0, controlPoints.Count - 1);
        Vector3 point;
        if (index < controlPoints.Count - 1)
            point = Vector3.Lerp(controlPoints[index], controlPoints[index + 1], 0.5f);
        else
            point = controlPoints[index] + (controlPoints[index] - controlPoints[index - 1]);
        controlPoints.Insert(index + 1, point);
        useEditablePath = true;
        return index + 1;
    }

    public int RemoveControlPoint(int index)
    {
        if (controlPoints.Count <= 2 || index < 0 || index >= controlPoints.Count)
            return Mathf.Clamp(index, 0, controlPoints.Count - 1);

        controlPoints.RemoveAt(index);
        return Mathf.Clamp(index, 0, controlPoints.Count - 1);
    }

    public int ReverseControlPoints(int selectedIndex)
    {
        controlPoints.Reverse();
        return controlPoints.Count > 0 ? controlPoints.Count - 1 - selectedIndex : -1;
    }


}

public static class MushTrackPathUtility
{
    public const float DefaultCourseLength = 960f;
    public const float DefaultSampleSpacing = 4f;

    public static void BuildDefaultRoute(List<Vector3> output)
    {
        output.Clear();
        int sampleCount = Mathf.RoundToInt(DefaultCourseLength / DefaultSampleSpacing) + 1;
        for (int index = 0; index < sampleCount; index++)
            output.Add(new Vector3(0f, 0f, -index * DefaultSampleSpacing));
    }

    public static void BuildEditableDefaultRoute(List<Vector3> output)
    {
        output.Clear();
        output.Add(Vector3.zero);
        output.Add(new Vector3(0f, 0f, -DefaultCourseLength));
    }

    public static bool BuildUniformRoute(
        IReadOnlyList<Vector3> controlPoints,
        float requestedSpacing,
        List<Vector3> output,
        out float routeLength,
        out float actualSpacing)
    {
        output.Clear();
        routeLength = 0f;
        actualSpacing = Mathf.Max(1f, requestedSpacing);
        if (controlPoints == null || controlPoints.Count < 2)
            return false;

        if (controlPoints.Count == 2)
            return BuildUniformPolylineRoute(
                controlPoints,
                actualSpacing,
                output,
                out routeLength,
                out actualSpacing);

        List<Vector3> smoothPath = new(controlPoints.Count * 6);
        for (int segment = 0; segment < controlPoints.Count - 1; segment++)
        {
            Vector3 p1 = controlPoints[segment];
            Vector3 p2 = controlPoints[segment + 1];
            Vector3 p0 = segment > 0
                ? controlPoints[segment - 1]
                : p1 + (p1 - p2);
            Vector3 p3 = segment + 2 < controlPoints.Count
                ? controlPoints[segment + 2]
                : p2 + (p2 - p1);

            int subdivisions = Mathf.Max(
                4,
                Mathf.CeilToInt(Vector3.Distance(p1, p2) / Mathf.Max(0.5f, actualSpacing * 0.5f)));
            int firstSample = segment == 0 ? 0 : 1;
            for (int sample = firstSample; sample <= subdivisions; sample++)
            {
                float t = sample / (float)subdivisions;
                smoothPath.Add(EvaluateCentripetalCatmullRom(p0, p1, p2, p3, t));
            }
        }

        return BuildUniformPolylineRoute(
            smoothPath,
            actualSpacing,
            output,
            out routeLength,
            out actualSpacing);
    }

    private static bool BuildUniformPolylineRoute(
        IReadOnlyList<Vector3> source,
        float requestedSpacing,
        List<Vector3> output,
        out float routeLength,
        out float actualSpacing)
    {
        output.Clear();
        routeLength = 0f;
        actualSpacing = Mathf.Max(1f, requestedSpacing);
        if (source == null || source.Count < 2)
            return false;

        float[] cumulativeDistances = new float[source.Count];
        for (int index = 1; index < source.Count; index++)
        {
            routeLength += Vector3.Distance(source[index - 1], source[index]);
            cumulativeDistances[index] = routeLength;
        }
        if (routeLength < 1f)
            return false;

        int segmentCount = Mathf.Clamp(Mathf.CeilToInt(routeLength / actualSpacing), 1, 4096);
        actualSpacing = routeLength / segmentCount;
        int sourceSegment = 0;
        for (int sample = 0; sample <= segmentCount; sample++)
        {
            float distance = sample == segmentCount ? routeLength : sample * actualSpacing;
            while (sourceSegment < source.Count - 2 &&
                   cumulativeDistances[sourceSegment + 1] < distance)
                sourceSegment++;

            float startDistance = cumulativeDistances[sourceSegment];
            float endDistance = cumulativeDistances[sourceSegment + 1];
            float t = Mathf.InverseLerp(startDistance, endDistance, distance);
            output.Add(Vector3.Lerp(source[sourceSegment], source[sourceSegment + 1], t));
        }
        return output.Count >= 2;
    }

    private static Vector3 EvaluateCentripetalCatmullRom(
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        float normalizedTime)
    {
        float t0 = 0f;
        float t1 = t0 + CatmullRomKnotDistance(p0, p1);
        float t2 = t1 + CatmullRomKnotDistance(p1, p2);
        float t3 = t2 + CatmullRomKnotDistance(p2, p3);
        float t = Mathf.Lerp(t1, t2, Mathf.Clamp01(normalizedTime));

        Vector3 a1 = InterpolateKnot(p0, p1, t0, t1, t);
        Vector3 a2 = InterpolateKnot(p1, p2, t1, t2, t);
        Vector3 a3 = InterpolateKnot(p2, p3, t2, t3, t);
        Vector3 b1 = InterpolateKnot(a1, a2, t0, t2, t);
        Vector3 b2 = InterpolateKnot(a2, a3, t1, t3, t);
        return InterpolateKnot(b1, b2, t1, t2, t);
    }

    private static float CatmullRomKnotDistance(Vector3 from, Vector3 to)
    {
        return Mathf.Max(0.0001f, Mathf.Sqrt(Vector3.Distance(from, to)));
    }

    private static Vector3 InterpolateKnot(
        Vector3 from,
        Vector3 to,
        float fromTime,
        float toTime,
        float time)
    {
        float range = toTime - fromTime;
        if (range <= 0.0001f)
            return from;
        return Vector3.LerpUnclamped(from, to, (time - fromTime) / range);
    }

}
