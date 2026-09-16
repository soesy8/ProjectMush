#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MushMapRuntimeValidator
{
    public static void Run()
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Logs"));
            ValidateScene("Assets/Mush/Scenes/snow.unity", true);
            ValidateScene("Assets/Mush/Scenes/Tree.unity", false);
            Debug.Log("[Mush Map Validation] PASS: both rebuilt maps rendered successfully.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void ValidateScene(string scenePath, bool snow)
    {
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        Transform mapRoot = FindMapRoot(snow);
        if (mapRoot == null)
            throw new InvalidOperationException("Map root missing in " + scenePath);

        MushCurvedMapRuntime world = MushCurvedMapRuntime.EnsureBuilt(mapRoot);
        Transform spawn = FindDeepChild(mapRoot, "SPAWN_Sled");
        Renderer road = FindRenderer(mapRoot, "VISIBLE Curved Packed-Snow Road");
        Renderer terrain = FindRenderer(mapRoot, "VISIBLE Snow Terrain");
        if (spawn == null || road == null || terrain == null || !road.enabled || !terrain.enabled)
            throw new InvalidOperationException("Visible road/terrain/spawn was not generated in " + scenePath);

        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new("Validation Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
        }
        camera.enabled = true;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.cullingMask = ~0;
        camera.useOcclusionCulling = false;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        camera.targetDisplay = 0;
        camera.fieldOfView = 82f;
        camera.nearClipPlane = 0.04f;
        camera.farClipPlane = 900f;
        camera.transform.position = spawn.position + Vector3.up * 1.52f - world.StartForward * 2.1f;
        Vector3 lookTarget = spawn.position + world.StartForward * 42f + Vector3.up * 0.65f;
        camera.transform.rotation = Quaternion.LookRotation(lookTarget - camera.transform.position, Vector3.up);

        Physics.SyncTransforms();
        if (!Physics.Raycast(spawn.position + Vector3.up * 12f, Vector3.down, out RaycastHit hit, 30f) ||
            !hit.collider.name.Contains("Road", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Road collider is not on top at spawn in {scenePath}. Hit={hit.collider?.name}");

        int errorShaders = 0;
        foreach (Renderer renderer in mapRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null || material.shader.name.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    errorShaders++;
                    Debug.LogError($"[Mush Map Validation] Error shader: Renderer={renderer.name}, Material={material?.name}, Shader={material?.shader?.name}");
                }
            }
        }
        if (errorShaders > 0)
            throw new InvalidOperationException($"{errorShaders} missing/error shaders in rebuilt {scenePath}.");

        if (snow)
        {
            MushSnowfieldBlizzardController controller = mapRoot.GetComponent<MushSnowfieldBlizzardController>();
            RenderPhase(camera, mapRoot, controller, 0.00f, "snow_clear_start");
            RenderPhase(camera, mapRoot, controller, 0.50f, "snow_blizzard_peak");
            RenderPhase(camera, mapRoot, controller, 0.85f, "snow_clear_finish");
        }
        else
        {
            MushForestTimeCycleController controller = mapRoot.GetComponent<MushForestTimeCycleController>();
            RenderPhase(camera, mapRoot, controller, 0.00f, "tree_day_start");
            RenderPhase(camera, mapRoot, controller, 0.32f, "tree_twilight");
            RenderPhase(camera, mapRoot, controller, 0.48f, "tree_night_stars");
            RenderPhase(camera, mapRoot, controller, 0.66f, "tree_dawn");
            RenderPhase(camera, mapRoot, controller, 0.78f, "tree_sunrise");
            RenderPhase(camera, mapRoot, controller, 0.95f, "tree_day_finish");
        }

        Debug.Log(
            $"[Mush Map Validation] {scenePath}: road={road.bounds.size}, terrain={terrain.bounds.size}, " +
            $"roadCenter={road.bounds.center}, terrainCenter={terrain.bounds.center}, " +
            $"mapPosition={mapRoot.position}, mapScale={mapRoot.lossyScale}, camera={camera.transform.position}, " +
            $"roadInFrustum={GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), road.bounds)}, " +
            $"spawnViewport={camera.WorldToViewportPoint(spawn.position)}, spawnHit={hit.collider.name}, " +
            $"renderers={mapRoot.GetComponentsInChildren<Renderer>(true).Length}");
    }

    private static void RenderPhase(Camera camera, Transform mapRoot, MushSnowfieldBlizzardController controller, float progress, string fileName)
    {
        if (controller == null)
            throw new InvalidOperationException("Snow controller missing.");
        controller.PreviewProgress(progress);
        SimulateParticles(mapRoot);
        RenderCamera(camera, fileName);
    }

    private static void RenderPhase(Camera camera, Transform mapRoot, MushForestTimeCycleController controller, float progress, string fileName)
    {
        if (controller == null)
            throw new InvalidOperationException("Tree controller missing.");
        controller.PreviewProgress(progress);
        SimulateParticles(mapRoot);
        RenderCamera(camera, fileName);
    }

    private static void SimulateParticles(Transform root)
    {
        foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (!particles.gameObject.activeInHierarchy)
                continue;
            particles.Simulate(1.2f, true, true, true);
        }
    }

    private static void RenderCamera(Camera camera, string fileName)
    {
        RenderTexture renderTexture = new(960, 540, 24, RenderTextureFormat.ARGB32);
        Texture2D texture = new(960, 540, TextureFormat.RGBA32, false);
        RenderTexture previous = RenderTexture.active;
        camera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;
        // The first off-screen URP render can be a shader warm-up frame. Render
        // once before the frame we read so the captured result matches gameplay.
        camera.Render();
        camera.Render();
        texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        texture.Apply();
        ValidateRenderedDetail(texture, fileName);
        string root = Directory.GetParent(Application.dataPath)!.FullName;
        File.WriteAllBytes(Path.Combine(root, "Logs", fileName + ".png"), texture.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(renderTexture);
    }

    private static void ValidateRenderedDetail(Texture2D texture, string fileName)
    {
        Color32[] pixels = texture.GetPixels32();
        const int width = 960;
        const int height = 540;
        double sum = 0d;
        double sumSquared = 0d;
        int sampled = 0;

        // The lower part of the view must contain the road, terrain and nearby props.
        // Sampling every fourth pixel keeps validation cheap while still detecting a
        // skybox-only/solid-colour frame.
        for (int y = 0; y < height * 9 / 20; y += 4)
        {
            int row = y * width;
            for (int x = 0; x < width; x += 4)
            {
                Color32 pixel = pixels[row + x];
                double luminance = (pixel.r * 0.2126d + pixel.g * 0.7152d + pixel.b * 0.0722d) / 255d;
                sum += luminance;
                sumSquared += luminance * luminance;
                sampled++;
            }
        }

        double mean = sum / sampled;
        double variance = sumSquared / sampled - mean * mean;
        Debug.Log($"[Mush Map Validation] Frame={fileName}, lower-frame luminance variance={variance:F6}");
        if (variance < 0.0015d)
            throw new InvalidOperationException(
                $"Rendered frame '{fileName}' is visually empty/flat (lower-frame variance={variance:F6}).");
    }

    private static Transform FindMapRoot(bool snow)
    {
        if (snow)
            return UnityEngine.Object.FindFirstObjectByType<MushSnowfieldBlizzardController>()?.transform;
        return UnityEngine.Object.FindFirstObjectByType<MushForestTimeCycleController>()?.transform;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private static Renderer FindRenderer(Transform root, string rendererName)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name.Equals(rendererName, StringComparison.OrdinalIgnoreCase))
                return renderer;
        }
        return null;
    }
}

[InitializeOnLoad]
public static class MushPlayModeRuntimeValidator
{
    private const string StageKey = "Mush.PlayValidation.Stage";
    private const string ErrorKey = "Mush.PlayValidation.Error";
    private static int waitFrames;

    static MushPlayModeRuntimeValidator()
    {
        if (SessionState.GetInt(StageKey, 0) != 0)
            Subscribe();
    }

    public static void Run()
    {
        SessionState.SetString(ErrorKey, string.Empty);
        SessionState.SetInt(StageKey, 1);
        Subscribe();
        EditorSceneManager.OpenScene("Assets/Mush/Scenes/snow.unity", OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        int stage = SessionState.GetInt(StageKey, 0);
        if (state == PlayModeStateChange.EnteredPlayMode && (stage == 1 || stage == 3))
        {
            waitFrames = 0;
            EditorApplication.update -= WaitForRuntimePresentation;
            EditorApplication.update += WaitForRuntimePresentation;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        if (stage == 2)
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/Mush/Scenes/Tree.unity", OpenSceneMode.Single);
                SessionState.SetInt(StageKey, 3);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }
        else if (stage == 4)
        {
            SessionState.EraseInt(StageKey);
            SessionState.EraseString(ErrorKey);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Debug.Log("[Mush Play Validation] PASS: snow and Tree both created the visible sled, two dogs, reins, curved road and ride camera in Play Mode.");
            EditorApplication.Exit(0);
        }
        else if (stage == 5)
        {
            string error = SessionState.GetString(ErrorKey, "Unknown play-mode validation failure.");
            SessionState.EraseInt(StageKey);
            SessionState.EraseString(ErrorKey);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Debug.LogError("[Mush Play Validation] FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static void WaitForRuntimePresentation()
    {
        waitFrames++;
        GameObject team = GameObject.Find("Mush Ride Team");
        MushCurvedMapRuntime world = UnityEngine.Object.FindFirstObjectByType<MushCurvedMapRuntime>();
        Camera camera = Camera.main;
        if (team == null || world == null || camera == null || !camera.transform.IsChildOf(team.transform))
        {
            if (waitFrames > 360)
                Fail(new TimeoutException("Ride team/world/camera did not appear within 360 editor frames."));
            return;
        }

        try
        {
            ValidateRuntimePresentation(team.transform, world, camera);
            bool snow = SessionState.GetInt(StageKey, 0) == 1;
            Capture(camera, snow ? "play_snow_actual" : "play_tree_actual");
            Debug.Log($"[Mush Play Validation] {(snow ? "snow" : "Tree")}: " +
                      $"team={team.transform.position}, camera={camera.transform.position}, renderers={team.GetComponentsInChildren<Renderer>(true).Length}");
            EditorApplication.update -= WaitForRuntimePresentation;
            SessionState.SetInt(StageKey, snow ? 2 : 4);
            EditorApplication.isPlaying = false;
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void ValidateRuntimePresentation(Transform team, MushCurvedMapRuntime world, Camera camera)
    {
        Transform sled = FindDeepChild(team, "Sled");
        Transform leftDog = FindDeepChild(team, "Left Husky");
        Transform rightDog = FindDeepChild(team, "Right Malamute");
        Transform leftRein = FindDeepChild(team, "Left Rein");
        Transform rightRein = FindDeepChild(team, "Right Rein");
        Renderer road = FindRenderer(world.transform, "VISIBLE Curved Packed-Snow Road");
        Renderer terrain = FindRenderer(world.transform, "VISIBLE Snow Terrain");
        if (sled == null || leftDog == null || rightDog == null || leftRein == null || rightRein == null)
            throw new InvalidOperationException("Sled, two dogs or reins are missing from the runtime ride team.");
        if (road == null || terrain == null || !road.enabled || !terrain.enabled)
            throw new InvalidOperationException("Rebuilt road or terrain is missing in Play Mode.");
        if (leftDog.GetComponentsInChildren<Renderer>(true).Length == 0 ||
            rightDog.GetComponentsInChildren<Renderer>(true).Length == 0 ||
            sled.GetComponentsInChildren<Renderer>(true).Length == 0)
            throw new InvalidOperationException("A runtime sled/dog exists by name but has no renderer.");

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        if (!AnyRendererInFrustum(leftDog, planes) || !AnyRendererInFrustum(rightDog, planes))
            throw new InvalidOperationException("One or both runtime dog models are outside the ride camera view.");
    }

    private static bool AnyRendererInFrustum(Transform root, Plane[] planes)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.enabled && renderer.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                return true;
        }
        return false;
    }

    private static void Capture(Camera camera, string fileName)
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
        File.WriteAllBytes(Path.Combine(projectRoot, "Logs", fileName + ".png"), texture.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        UnityEngine.Object.Destroy(texture);
        UnityEngine.Object.Destroy(target);
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private static Renderer FindRenderer(Transform root, string rendererName)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.name.Equals(rendererName, StringComparison.OrdinalIgnoreCase))
                return renderer;
        }
        return null;
    }

    private static void Fail(Exception exception)
    {
        EditorApplication.update -= WaitForRuntimePresentation;
        SessionState.SetString(ErrorKey, exception.ToString());
        SessionState.SetInt(StageKey, 5);
        Debug.LogException(exception);
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;
        else
            EditorApplication.delayCall += () => OnPlayModeChanged(PlayModeStateChange.EnteredEditMode);
    }
}

#endif
