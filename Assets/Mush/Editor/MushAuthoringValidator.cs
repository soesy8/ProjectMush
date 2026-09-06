using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mush.Customization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Batch validation of scene persistence, team extension and curve baking.</summary>
[InitializeOnLoad]
public static class MushAuthoringValidator
{
    private const string Key = "Mush.AuthoringValidation.";
    private static int frames;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Maps = { "snow", "Tree", "SharpCurve" };

    static MushAuthoringValidator()
    {
        if (SessionState.GetBool(Key + "active", false)) Subscribe();
    }

    public static void Run()
    {
        ValidateEquipment();
        ValidateBender();
        SessionState.SetBool(Key + "active", true);
        SessionState.SetInt(Key + "map", 0);
        Subscribe();
        PrepareScene();
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error) && !message.StartsWith("[Mush Validation]"))
            SessionState.SetString(Key + "error", message);
    }

    private static T Find<T>() where T : Component
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            T item = root.GetComponentInChildren<T>(true);
            if (item != null) return item;
        }
        throw new Exception("Missing " + typeof(T).Name);
    }

    private static void PrepareScene()
    {
        int map = SessionState.GetInt(Key + "map", 0);
        SessionState.SetString(Key + "error", "");
        EditorSceneManager.OpenScene($"Assets/Mush/Scenes/{Maps[map]}.unity", OpenSceneMode.Single);
        MushCurvedMapRuntime runtime = Find<MushCurvedMapRuntime>();
        Require(new SerializedObject(runtime).FindProperty("bakedRoute").arraySize >= 2, "Baked route is missing");
        MushRideDog dog = Find<MushRideDog>();
        GameObject extra = Object.Instantiate(dog.gameObject, dog.transform.parent);
        extra.name = "Validation Extra Dog";
        extra.transform.localPosition += new Vector3(0.35f, 0f, 2.5f);
        extra.transform.localScale = Vector3.one * 0.73f;
        MushRideDog member = extra.GetComponent<MushRideDog>();
        member.Configure(member.Visual, member.Harness, -1, false, 1.25f);
        GameObject custom = new("Validation Authored Object");
        custom.transform.SetParent(runtime.transform, false);
        custom.transform.localPosition = new Vector3(13f, 7f, -21f);
        custom.SetActive(false);
        SessionState.SetString(Key + "geometry", GeometrySignature(runtime));
        MushMapRideBootstrap bootstrap = Find<MushMapRideBootstrap>();
        Camera camera = (Camera)typeof(MushMapRideBootstrap).GetField("rideCamera", Private).GetValue(bootstrap);
        camera.transform.localPosition += new Vector3(0.09f, 0.12f, -0.07f);
        SessionState.SetString(Key + "camera", JsonUtility.ToJson(camera.transform.localPosition));
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            frames = 0;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            int next = SessionState.GetInt(Key + "map", 0) + 1;
            SessionState.SetInt(Key + "map", next);
            if (next < Maps.Length) EditorApplication.delayCall += PrepareScene;
            else
            {
                SessionState.EraseBool(Key + "active");
                Debug.Log("[Mush Validation] PASS: all three scenes, extra dog, authored camera/object, persistent meshes, dog equipment and curved mesh baking.");
                EditorApplication.Exit(0);
            }
        }
    }

    private static void Tick()
    {
        if (++frames < 20) return;
        EditorApplication.update -= Tick;
        try
        {
            Require(string.IsNullOrEmpty(SessionState.GetString(Key + "error", "")), SessionState.GetString(Key + "error", ""));
            MushCurvedMapRuntime runtime = Find<MushCurvedMapRuntime>();
            Require(GeometrySignature(runtime) == SessionState.GetString(Key + "geometry", ""), "Play mode replaced or moved a saved mesh");
            MushMapRideBootstrap bootstrap = Find<MushMapRideBootstrap>();
            ICollection dogs = (ICollection)typeof(MushMapRideBootstrap).GetField("dogs", Private).GetValue(bootstrap);
            Require(dogs.Count == 3, "Additional dog was not registered");
            Transform extra = runtime.transform.Find(MushCurvedMapRuntime.RideTeamRootName).Find("Validation Extra Dog");
            Require(extra != null && Vector3.Distance(extra.localScale, Vector3.one * 0.73f) < 0.0001f, "Authored dog scale changed");
            Transform custom = runtime.transform.Find("Validation Authored Object");
            Require(custom != null && !custom.gameObject.activeSelf && custom.localPosition == new Vector3(13f, 7f, -21f), "Authored object changed");
            Camera camera = (Camera)typeof(MushMapRideBootstrap).GetField("rideCamera", Private).GetValue(bootstrap);
            Vector3 expected = JsonUtility.FromJson<Vector3>(SessionState.GetString(Key + "camera", ""));
            Require(Vector3.Distance(camera.transform.localPosition, expected) < 0.005f, "Authored camera position changed");
            Debug.Log($"[Mush Validation] {Maps[SessionState.GetInt(Key + "map", 0)]}: 3 dogs; saved meshes, camera and user content retained.");
            EditorApplication.ExitPlaymode();
        }
        catch (Exception exception)
        {
            SessionState.EraseBool(Key + "active");
            Debug.LogError("[Mush Validation] FAIL: " + exception);
            EditorApplication.Exit(1);
        }
    }

    private static string GeometrySignature(MushCurvedMapRuntime runtime)
    {
        Transform world = runtime.transform.Find(MushCurvedMapRuntime.GeneratedWorldRootName);
        var lines = new List<string>();
        foreach (MeshFilter filter in world.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            Require(mesh != null && AssetDatabase.Contains(mesh), "Unsaved mesh: " + filter.name);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long id);
            // The animated sky follows the camera; authored scene geometry stays fixed.
            Vector3 position = filter.name == "FX_StarDome_Rebuilt" ? Vector3.zero : filter.transform.localPosition;
            lines.Add($"{filter.name}|{guid}|{id}|{mesh.vertexCount}|{position}|{filter.transform.localScale}");
        }
        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }

    private static void ValidateEquipment()
    {
        var state = new MushCustomizationState();
        state.SetDogHat(0, "one"); state.SetDogHat(1, "two"); state.SetDogHat(2, "three"); state.SetDogNeck(9, "nine");
        state = JsonUtility.FromJson<MushCustomizationState>(JsonUtility.ToJson(state));
        Require(state.GetDogHat(0) == "one" && state.GetDogHat(1) == "two" && state.GetDogHat(2) == "three"
            && state.GetDogHat(3) == "" && state.GetDogNeck(9) == "nine", "Dog equipment aliases another member");
    }

    private static void ValidateBender()
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh bent = null;
        Mesh unreadable = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
        unreadable.UploadMeshData(true);
        cube.GetComponent<MeshFilter>().sharedMesh = unreadable;
        try
        {
            int budget = MushRoadMeshBender.VertexBudget;
            bent = MushRoadMeshBender.Bend(cube.GetComponent<MeshFilter>(), cube.transform,
                -0.5f, 0.5f, 0f, -0.5f, 2f, 0f, 20f, 2f, TestFrame, ref budget);
            Require(bent.vertexCount > cube.GetComponent<MeshFilter>().sharedMesh.vertexCount && budget > 0, "Road did not subdivide within its budget");
            foreach (Vector3 vertex in bent.vertices) Require(float.IsFinite(vertex.x) && float.IsFinite(vertex.y) && float.IsFinite(vertex.z), "Non-finite road vertex");
            Require(bent.bounds.size.x > 8f && bent.bounds.size.z > 8f, "Road is not curved");
            budget = 1;
            bool rejected = false;
            try { MushRoadMeshBender.Bend(cube.GetComponent<MeshFilter>(), cube.transform, -0.5f, 0.5f, 0f, -0.5f, 2f, 0f, 20f, 2f, TestFrame, ref budget); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Road vertex budget was not enforced");
        }
        finally { if (bent != null) Object.DestroyImmediate(bent); Object.DestroyImmediate(unreadable); Object.DestroyImmediate(cube); }
    }

    private static void TestFrame(float distance, out Vector3 center, out Vector3 right)
    {
        float angle = Mathf.Clamp(distance, 0f, 20f) / 20f * Mathf.PI * 0.5f;
        center = new Vector3(12f * (1f - Mathf.Cos(angle)), 0f, -12f * Mathf.Sin(angle));
        right = Vector3.Cross(Vector3.up, new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
