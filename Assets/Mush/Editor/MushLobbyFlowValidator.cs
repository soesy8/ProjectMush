#if UNITY_EDITOR

using System;
using System.IO;
using System.Reflection;
using Mush.Lobby;
using Mush.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MushLobbyFlowValidator
{
    private const string StageKey = "Mush.LobbyFlowValidation.Stage";
    private static int waitFrames;
    private static bool mapPanelOpened;

    static MushLobbyFlowValidator()
    {
        if (SessionState.GetInt(StageKey, 0) != 0)
            Subscribe();
    }

    public static void Run()
    {
        SessionState.SetInt(StageKey, 1);
        waitFrames = 0;
        mapPanelOpened = false;
        Subscribe();
        EditorSceneManager.OpenScene("Assets/Scenes/MushLobby.unity", OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetInt(StageKey, 0) == 6)
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SessionState.EraseInt(StageKey);
            Debug.Log("[Mush Lobby Flow Validation] PASS: Korean lobby board -> snow -> finish return -> lobby -> Tree -> lobby.");
            EditorApplication.Exit(0);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;

        waitFrames++;
        if (waitFrames > 900)
        {
            Fail("Timed out while validating stage " + SessionState.GetInt(StageKey, 0));
            return;
        }

        int stage = SessionState.GetInt(StageKey, 0);
        try
        {
            switch (stage)
            {
                case 1:
                    ValidateLobbyAndOpenSnow();
                    break;
                case 2:
                    ValidateSnowAndFinish();
                    break;
                case 3:
                    OpenTreeFromReturnedLobby();
                    break;
                case 4:
                    ValidateTreeAndReturn();
                    break;
                case 5:
                    FinishInLobby();
                    break;
            }
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private static void ValidateLobbyAndOpenSnow()
    {
        if (SceneManager.GetActiveScene().name != "MushLobby")
            return;
        MushLobbyController controller = UnityEngine.Object.FindFirstObjectByType<MushLobbyController>();
        Camera camera = Camera.main;
        if (controller == null || camera == null || waitFrames < 24)
            return;

        if (!mapPanelOpened)
        {
            controller.HandleAction(MushLobbyAction.OpenMapBoard);
            mapPanelOpened = true;
            return;
        }

        ValidateKoreanLobby(controller);
        Capture(camera, "lobby_korean_map_board");
        SetStage(2);
        controller.HandleAction(MushLobbyAction.SelectSnowfield);
    }

    private static void ValidateSnowAndFinish()
    {
        if (SceneManager.GetActiveScene().name != "snow")
            return;
        MushMapRideBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<MushMapRideBootstrap>();
        if (bootstrap == null || GameObject.Find("Mush Ride Team") == null || waitFrames < 20)
            return;

        FieldInfo controllerField = typeof(MushMapRideBootstrap).GetField("rideController", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo travelledField = typeof(MushMapRideBootstrap).GetField("travelledCourseDistance", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo lengthField = typeof(MushMapRideBootstrap).GetField("courseLengthMeters", BindingFlags.Instance | BindingFlags.NonPublic);
        MushSledKeyboardController rideController = controllerField?.GetValue(bootstrap) as MushSledKeyboardController;
        if (rideController == null || travelledField == null || lengthField == null)
            throw new InvalidOperationException("Snow ride completion fields/controller are unavailable.");

        travelledField.SetValue(bootstrap, (float)lengthField.GetValue(bootstrap));
        rideController.StartRide();
        SetStage(3);
    }

    private static void OpenTreeFromReturnedLobby()
    {
        if (SceneManager.GetActiveScene().name != "MushLobby")
            return;
        MushLobbyController controller = UnityEngine.Object.FindFirstObjectByType<MushLobbyController>();
        if (controller == null || waitFrames < 20)
            return;

        SetStage(4);
        controller.HandleAction(MushLobbyAction.SelectForest);
    }

    private static void ValidateTreeAndReturn()
    {
        if (SceneManager.GetActiveScene().name != "Tree")
            return;
        if (GameObject.Find("Mush Ride Team") == null ||
            UnityEngine.Object.FindFirstObjectByType<MushCurvedMapRuntime>() == null || waitFrames < 20)
            return;

        SetStage(5);
        SceneManager.LoadScene("MushLobby");
    }

    private static void FinishInLobby()
    {
        if (SceneManager.GetActiveScene().name != "MushLobby" ||
            UnityEngine.Object.FindFirstObjectByType<MushLobbyController>() == null || waitFrames < 20)
            return;

        SessionState.SetInt(StageKey, 6);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }

    private static void ValidateKoreanLobby(MushLobbyController controller)
    {
        if (MushLobbyController.ActiveKoreanFont == null)
            throw new InvalidOperationException("Korean lobby font was not loaded.");

        bool snowButton = false;
        bool treeButton = false;
        FieldInfo actionField = typeof(MushLobbyInteractable).GetField("action", BindingFlags.Instance | BindingFlags.NonPublic);
        if (actionField == null)
            throw new InvalidOperationException("Could not inspect lobby map actions.");
        foreach (MushLobbyInteractable interactable in Resources.FindObjectsOfTypeAll<MushLobbyInteractable>())
        {
            if (interactable == null || interactable.gameObject.scene != controller.gameObject.scene)
                continue;
            MushLobbyAction action = (MushLobbyAction)actionField.GetValue(interactable);
            snowButton |= action == MushLobbyAction.SelectSnowfield;
            treeButton |= action == MushLobbyAction.SelectForest;
        }
        if (!snowButton || !treeButton)
            throw new InvalidOperationException("The Korean map board is missing a snow or Tree scene button.");

        foreach (TextMesh text in Resources.FindObjectsOfTypeAll<TextMesh>())
        {
            if (text == null || text.gameObject.scene != controller.gameObject.scene || string.IsNullOrWhiteSpace(text.text))
                continue;
            foreach (char character in text.text)
            {
                if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z'))
                    throw new InvalidOperationException($"English lobby text remains: '{text.text}' on {text.name}");
            }
        }
    }

    private static void Capture(Camera camera, string name)
    {
        RenderTexture target = new(1280, 720, 24, RenderTextureFormat.ARGB32);
        Texture2D texture = new(1280, 720, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        camera.targetTexture = target;
        RenderTexture.active = target;
        camera.Render();
        camera.Render();
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        texture.Apply();
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        File.WriteAllBytes(Path.Combine(projectRoot, "Logs", name + ".png"), texture.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        UnityEngine.Object.Destroy(texture);
        UnityEngine.Object.Destroy(target);
    }

    private static void SetStage(int stage)
    {
        SessionState.SetInt(StageKey, stage);
        waitFrames = 0;
        mapPanelOpened = false;
    }

    private static void Fail(string message)
    {
        Debug.LogError("[Mush Lobby Flow Validation] FAIL: " + message);
        SessionState.EraseInt(StageKey);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(1);
    }
}

#endif
