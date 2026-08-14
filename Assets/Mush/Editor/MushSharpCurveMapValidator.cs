#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MushSharpCurveMapValidator
{
    private const string ScenePath = "Assets/Scenes/SharpCurve.unity";

    [MenuItem("Mush/Maps/Validate Sharp Curve Map")]
    public static void Validate()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject rootObject = GameObject.Find("Mush_Map_SharpCurve");
            if (rootObject == null)
                throw new InvalidOperationException("SharpCurve map root is missing.");

            MushCurvedMapRuntime world = MushCurvedMapRuntime.EnsureBuilt(rootObject.transform);
            if (world == null || !world.IsSharpCurveMap)
                throw new InvalidOperationException("SharpCurve procedural mode did not activate.");

            FieldInfo routeField = typeof(MushCurvedMapRuntime).GetField(
                "routePoints",
                BindingFlags.Instance | BindingFlags.NonPublic);
            List<Vector3> route = routeField?.GetValue(world) as List<Vector3>;
            if (route == null || route.Count < 220)
                throw new InvalidOperationException("SharpCurve route samples were not generated.");

            ValidateTurnOrder(route);
            ValidateSteepDescent(route);
            ValidateSurfaceCollider(rootObject.transform, "VISIBLE Snow Terrain");
            ValidateSurfaceCollider(rootObject.transform, "VISIBLE Curved Packed-Snow Road");
            ValidateGroundCoverage(route);

            if (FindChild(rootObject.transform, "FINISH_Delivery") == null)
                throw new InvalidOperationException("SharpCurve finish marker is missing.");
            if (FindChild(rootObject.transform, "FX_SharpCurve_MeteorShower") == null)
                throw new InvalidOperationException("SharpCurve meteor shower is missing.");
            if (FindChild(rootObject.transform, "FX_SharpCurve_Aurora") == null)
                throw new InvalidOperationException("SharpCurve aurora is missing.");

            bool sceneEnabled = false;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && scene.path == ScenePath)
                {
                    sceneEnabled = true;
                    break;
                }
            }
            if (!sceneEnabled)
                throw new InvalidOperationException("SharpCurve is not enabled in Build Settings.");

            Debug.Log(
                "[Mush SharpCurve Validation] PASS: left/right/left/right turns, steep descent, " +
                "road and terrain mesh colliders, finish marker, meteor shower, aurora, and Build Settings.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    private static void ValidateTurnOrder(List<Vector3> route)
    {
        Vector3 start = HorizontalTangent(route, 5);
        Vector3 afterLeft = HorizontalTangent(route, 40);
        Vector3 afterRight = HorizontalTangent(route, 61);
        Vector3 afterSecondLeft = HorizontalTangent(route, 80);
        Vector3 afterFinalRight = HorizontalTangent(route, 205);

        float first = SignedTurn(start, afterLeft);
        float second = SignedTurn(afterLeft, afterRight);
        float third = SignedTurn(afterRight, afterSecondLeft);
        float fourth = SignedTurn(afterSecondLeft, afterFinalRight);
        if (first < 75f || second > -42f || third < 45f || fourth > -75f)
        {
            throw new InvalidOperationException(
                $"Unexpected turn order/strength: {first:0.0}, {second:0.0}, {third:0.0}, {fourth:0.0} degrees.");
        }
    }

    private static void ValidateSteepDescent(List<Vector3> route)
    {
        float totalDrop = route[87].y - route[155].y;
        float maximumGrade = 0f;
        for (int index = 1; index < route.Count; index++)
        {
            Vector3 segment = route[index] - route[index - 1];
            float horizontal = new Vector2(segment.x, segment.z).magnitude;
            if (horizontal > 0.001f)
                maximumGrade = Mathf.Max(maximumGrade, Mathf.Abs(segment.y) / horizontal);
        }

        float maximumSlopeDegrees = Mathf.Atan(maximumGrade) * Mathf.Rad2Deg;
        if (totalDrop < 115f || maximumSlopeDegrees < 30f || maximumSlopeDegrees > 42f)
        {
            throw new InvalidOperationException(
                $"Steep descent is outside the safe target: drop={totalDrop:0.0}m, slope={maximumSlopeDegrees:0.0} degrees.");
        }
    }

    private static void ValidateSurfaceCollider(Transform root, string objectName)
    {
        Transform surface = FindChild(root, objectName);
        if (surface == null || !surface.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null ||
            !surface.TryGetComponent(out MeshCollider collider) || collider.sharedMesh != filter.sharedMesh ||
            !collider.enabled)
        {
            throw new InvalidOperationException(objectName + " does not have a matching enabled MeshCollider.");
        }
    }

    private static void ValidateGroundCoverage(List<Vector3> route)
    {
        Physics.SyncTransforms();
        for (int index = 0; index < route.Count; index += 12)
        {
            Vector3 worldPoint = route[index];
            RaycastHit[] hits = Physics.RaycastAll(
                worldPoint + Vector3.up * 18f,
                Vector3.down,
                38f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            bool foundRoadSurface = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.name == "VISIBLE Curved Packed-Snow Road" ||
                    hit.collider.name == "VISIBLE Snow Terrain")
                {
                    foundRoadSurface = true;
                    break;
                }
            }
            if (!foundRoadSurface)
                throw new InvalidOperationException($"No ground collider under route sample {index}.");
        }
    }

    private static Vector3 HorizontalTangent(List<Vector3> route, int index)
    {
        int previous = Mathf.Max(0, index - 1);
        int next = Mathf.Min(route.Count - 1, index + 1);
        return Vector3.ProjectOnPlane(route[next] - route[previous], Vector3.up).normalized;
    }

    private static float SignedTurn(Vector3 from, Vector3 to)
    {
        // For a sled initially travelling toward world -Z, a positive cross-Y
        // value is a left turn and a negative value is a right turn.
        float unsigned = Vector3.Angle(from, to);
        return Mathf.Sign(Vector3.Cross(from, to).y) * unsigned;
    }

    private static Transform FindChild(Transform root, string targetName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == targetName)
                return child;
        }
        return null;
    }
}

#endif
