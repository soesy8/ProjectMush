using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum MushTrackPreset
{
    Snowfield,
    Forest,
    SharpCurve
}

/// <summary>
/// Scene-owned prototype track data. Control points are stored in the target
/// map root's local space, so art and VFX can be replaced without changing the
/// gameplay route.
/// </summary>
[DisallowMultipleComponent]
public sealed class MushTrackAuthoring : MonoBehaviour
{
    [SerializeField] private MushTrackPreset preset;
    [SerializeField] private string targetMapRootName = "Mush_Map_Snowfield_V2";
    [SerializeField] private bool useEditablePath;
    [SerializeField, Min(1f)] private float sampleSpacing = MushTrackPathUtility.DefaultSampleSpacing;
    [SerializeField] private bool overrideTrackWidths;
    [SerializeField, Min(0.5f)] private float roadHalfWidth = 6.5f;
    [SerializeField, Min(4f)] private float terrainHalfWidth = 105f;
    [SerializeField] private List<Vector3> controlPoints = new();

    public MushTrackPreset Preset => preset;
    public string TargetMapRootName => targetMapRootName;
    public bool UsesEditablePath => useEditablePath && controlPoints.Count >= 2;
    public int ControlPointCount => controlPoints.Count;
    public float RoadHalfWidth => roadHalfWidth;
    public float TerrainHalfWidth => terrainHalfWidth;
    public bool OverridesTrackWidths => overrideTrackWidths;

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
        return mapRoot != null &&
               gameObject.scene == mapRoot.gameObject.scene &&
               targetMapRootName.Equals(mapRoot.name, StringComparison.OrdinalIgnoreCase);
    }

    public Transform ResolveMapRoot()
    {
        Scene scene = gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int index = 0; index < roots.Length; index++)
        {
            Transform match = FindNamedTransform(roots[index].transform, targetMapRootName);
            if (match != null)
                return match;
        }
        return null;
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

        MushTrackPathUtility.BuildDefaultRoute(preset, output, out _);
    }

    public void CopyEditableControlPointPreview(List<Vector3> output)
    {
        output.Clear();
        if (UsesEditablePath)
        {
            output.AddRange(controlPoints);
            return;
        }

        MushTrackPathUtility.BuildEditableDefaultRoute(preset, output, 0.75f);
    }

    public Vector3 GetControlPoint(int index) => controlPoints[index];

    public void SetControlPoint(int index, Vector3 point)
    {
        if (index >= 0 && index < controlPoints.Count)
            controlPoints[index] = point;
    }

    public void BakeDefaultPath(float simplificationTolerance = 0.75f)
    {
        MushTrackPathUtility.BuildEditableDefaultRoute(preset, controlPoints, simplificationTolerance);
        useEditablePath = controlPoints.Count >= 2;
    }

    public void SetEditablePathEnabled(bool enabled)
    {
        useEditablePath = enabled && controlPoints.Count >= 2;
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

    private static Transform FindNamedTransform(Transform current, string objectName)
    {
        if ((current.gameObject.hideFlags & HideFlags.DontSaveInEditor) != 0)
            return null;
        if (current.name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
            return current;

        for (int index = 0; index < current.childCount; index++)
        {
            Transform match = FindNamedTransform(current.GetChild(index), objectName);
            if (match != null)
                return match;
        }
        return null;
    }
}

public static class MushTrackPathUtility
{
    public const float DefaultCourseLength = 960f;
    public const float DefaultSampleSpacing = 4f;

    private readonly struct CurveEvent
    {
        public readonly float Start;
        public readonly float Length;
        public readonly float Strength;
        public readonly bool IsSCurve;

        public CurveEvent(float start, float length, float strength, bool isSCurve)
        {
            Start = start;
            Length = length;
            Strength = strength;
            IsSCurve = isSCurve;
        }
    }

    public static void BuildDefaultRoute(MushTrackPreset preset, List<Vector3> output, out int curveCount)
    {
        output.Clear();
        List<CurveEvent> curves = new();
        BuildCurveSchedule(preset, curves);
        curveCount = curves.Count;

        int sampleCount = Mathf.RoundToInt(DefaultCourseLength / DefaultSampleSpacing) + 1;
        Vector3 position = Vector3.zero;
        float headingRadians = 0f;
        output.Add(new Vector3(0f, RouteHeight(preset, 0f), 0f));

        for (int index = 1; index < sampleCount; index++)
        {
            float distance = index * DefaultSampleSpacing;
            headingRadians += EvaluateCurvature(curves, distance) * DefaultSampleSpacing * Mathf.Deg2Rad;
            float headingLimit = preset == MushTrackPreset.SharpCurve ? 170f : 52f;
            headingRadians = Mathf.Clamp(
                headingRadians,
                -headingLimit * Mathf.Deg2Rad,
                headingLimit * Mathf.Deg2Rad);

            Vector3 forward = new(Mathf.Sin(headingRadians), 0f, -Mathf.Cos(headingRadians));
            position += forward * DefaultSampleSpacing;
            position.y = RouteHeight(preset, distance);
            output.Add(position);
        }
    }

    public static void BuildEditableDefaultRoute(
        MushTrackPreset preset,
        List<Vector3> output,
        float simplificationTolerance)
    {
        List<Vector3> source = new();
        BuildDefaultRoute(preset, source, out _);
        SimplifyPolyline(source, Mathf.Max(0.05f, simplificationTolerance), output);
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

        float[] cumulativeDistances = new float[controlPoints.Count];
        for (int index = 1; index < controlPoints.Count; index++)
        {
            routeLength += Vector3.Distance(controlPoints[index - 1], controlPoints[index]);
            cumulativeDistances[index] = routeLength;
        }
        if (routeLength < 1f)
            return false;

        int segmentCount = Mathf.Max(1, Mathf.CeilToInt(routeLength / actualSpacing));
        actualSpacing = routeLength / segmentCount;
        int sourceSegment = 0;
        for (int sample = 0; sample <= segmentCount; sample++)
        {
            float distance = sample == segmentCount ? routeLength : sample * actualSpacing;
            while (sourceSegment < controlPoints.Count - 2 &&
                   cumulativeDistances[sourceSegment + 1] < distance)
                sourceSegment++;

            float startDistance = cumulativeDistances[sourceSegment];
            float endDistance = cumulativeDistances[sourceSegment + 1];
            float t = Mathf.InverseLerp(startDistance, endDistance, distance);
            output.Add(Vector3.Lerp(controlPoints[sourceSegment], controlPoints[sourceSegment + 1], t));
        }
        return output.Count >= 2;
    }

    private static void BuildCurveSchedule(MushTrackPreset preset, List<CurveEvent> curves)
    {
        if (preset == MushTrackPreset.SharpCurve)
        {
            curves.Add(new CurveEvent(88f, 32f, -4.50f, false));
            curves.Add(new CurveEvent(140f, 24f, 4.25f, false));
            curves.Add(new CurveEvent(172f, 24f, -4.45f, false));
            curves.Add(new CurveEvent(720f, 32f, 4.65f, false));
            return;
        }

        System.Random random = new(preset == MushTrackPreset.Snowfield ? 27183 : 91457);
        float cursor = 48f;
        int direction = random.NextDouble() < 0.5 ? -1 : 1;
        while (cursor < DefaultCourseLength - 80f)
        {
            cursor += random.Next(28, 66);
            bool isSCurve = random.NextDouble() < 0.34;
            float length = isSCurve ? random.Next(80, 132) : random.Next(58, 146);
            length = Mathf.Min(length, DefaultCourseLength - 45f - cursor);
            if (length < 35f)
                break;

            float strength = Mathf.Lerp(0.18f, isSCurve ? 0.48f : 0.36f, (float)random.NextDouble());
            curves.Add(new CurveEvent(cursor, length, strength * direction, isSCurve));
            direction *= -1;
            cursor += length;
        }
    }

    private static float EvaluateCurvature(List<CurveEvent> curves, float distance)
    {
        float curvature = 0f;
        for (int index = 0; index < curves.Count; index++)
        {
            CurveEvent curve = curves[index];
            if (distance < curve.Start || distance > curve.Start + curve.Length)
                continue;

            float t = Mathf.InverseLerp(curve.Start, curve.Start + curve.Length, distance);
            float wave = curve.IsSCurve ? Mathf.Sin(t * Mathf.PI * 2f) : Mathf.Sin(t * Mathf.PI);
            curvature += curve.Strength * wave;
        }
        return curvature;
    }

    private static float RouteHeight(MushTrackPreset preset, float distance)
    {
        if (preset == MushTrackPreset.SharpCurve)
        {
            const float plateauHeight = 78f;
            const float crestHeight = 84f;
            const float valleyHeight = -42f;
            if (distance < 320f)
                return plateauHeight + Mathf.Sin(distance * 0.018f) * 1.25f;
            if (distance < 350f)
            {
                float crest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(320f, 350f, distance));
                return Mathf.Lerp(plateauHeight, crestHeight, crest);
            }
            if (distance < 620f)
            {
                float descent = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(350f, 620f, distance));
                return Mathf.Lerp(crestHeight, valleyHeight, descent);
            }

            float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(620f, 690f, distance));
            return valleyHeight + Mathf.Sin(distance * 0.014f) * 0.22f * settle;
        }

        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 70f, distance));
        return fadeIn * (Mathf.Sin(distance * 0.0105f) * 1.7f + Mathf.Sin(distance * 0.027f) * 0.48f);
    }

    private static void SimplifyPolyline(IReadOnlyList<Vector3> source, float tolerance, List<Vector3> output)
    {
        output.Clear();
        if (source == null || source.Count < 2)
            return;

        bool[] keep = new bool[source.Count];
        keep[0] = true;
        keep[^1] = true;
        MarkPolylinePoints(source, 0, source.Count - 1, tolerance * tolerance, keep);
        for (int index = 0; index < source.Count; index++)
        {
            if (keep[index])
                output.Add(source[index]);
        }
    }

    private static void MarkPolylinePoints(
        IReadOnlyList<Vector3> source,
        int startIndex,
        int endIndex,
        float toleranceSqr,
        bool[] keep)
    {
        if (endIndex <= startIndex + 1)
            return;

        Vector3 start = source[startIndex];
        Vector3 segment = source[endIndex] - start;
        float segmentLengthSqr = segment.sqrMagnitude;
        float farthestDistanceSqr = 0f;
        int farthestIndex = -1;
        for (int index = startIndex + 1; index < endIndex; index++)
        {
            float t = segmentLengthSqr > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(source[index] - start, segment) / segmentLengthSqr)
                : 0f;
            float distanceSqr = (source[index] - (start + segment * t)).sqrMagnitude;
            if (distanceSqr <= farthestDistanceSqr)
                continue;
            farthestDistanceSqr = distanceSqr;
            farthestIndex = index;
        }

        if (farthestIndex < 0 || farthestDistanceSqr <= toleranceSqr)
            return;

        keep[farthestIndex] = true;
        MarkPolylinePoints(source, startIndex, farthestIndex, toleranceSqr, keep);
        MarkPolylinePoints(source, farthestIndex, endIndex, toleranceSqr, keep);
    }
}
