using System;
using System.Collections.Generic;
using System.IO;
using Mush.Quest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class MushVrUiPreviewWindow : EditorWindow
{
    private const string LobbyPath = "Assets/Scenes/PM_Lobby.unity";
    private const int Width = 1600;
    private const int Height = 900;
    private Scene previewScene;
    private Camera previewCamera;
    private Transform hand;
    private MushVrWristHud wrist;
    private CanvasGroup visibility;
    private RenderTexture frame;
    private Vector3 eyePosition;
    private Quaternion eyeRotation;
    private Vector3 handPosition = new(-0.18f, -0.24f, 0.55f);
    private Vector3 handRotation;
    private Vector3 wristOffset;
    private float wristWidth;
    private bool tracked = true;
    private bool menuOpen;
    private Vector2 look;
    private Vector2 lastPointer;
    private bool dragging;
    private double lastTick;
    private UnityEngine.UIElements.Image viewport;
    private Label status;
    private Vector3Field positionField;
    private Vector3Field rotationField;
    private Toggle menuToggle;
    private Toggle trackedToggle;

    [MenuItem("Mush/VR UI/PC Wrist Preview")]
    public static void Open()
    {
        MushVrUiPreviewWindow window = GetWindow<MushVrUiPreviewWindow>();
        window.titleContent = new GUIContent("VR UI PC Preview");
        window.minSize = new Vector2(800f, 620f);
        window.Show();
    }

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.style.paddingLeft = root.style.paddingRight = 8;
        root.style.paddingTop = root.style.paddingBottom = 8;
        root.Add(new HelpBox("실제 로비 UI와 손 모델을 사용하는 PC 미리보기입니다. 화면을 드래그하면 시점이 회전합니다. 방향키: 손 좌우·앞뒤 / R·F: 높이 / Q·E: 손목 회전 / Shift: 빠르게", HelpBoxMessageType.Info));
        VisualElement buttons = new();
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.flexWrap = Wrap.Wrap;
        AddButton(buttons, "손 올리기", () => SetPose("raised"));
        AddButton(buttons, "손 내리기", () => SetPose("lowered"));
        AddButton(buttons, "손목 돌리기", () => SetPose("rotated"));
        AddButton(buttons, "고개 돌리기", () => SetPose("look-away"));
        AddButton(buttons, "초기화", () => SetPose("raised"));
        AddButton(buttons, "캡처 저장", SaveFromWindow);
        root.Add(buttons);
        positionField = new Vector3Field("왼손 위치 (머리 기준 m)");
        positionField.RegisterValueChangedCallback(evt => handPosition = evt.newValue);
        root.Add(positionField);
        rotationField = new Vector3Field("왼손 회전 (도)");
        rotationField.RegisterValueChangedCallback(evt => handRotation = evt.newValue);
        root.Add(rotationField);
        menuToggle = new Toggle("다른 메뉴가 열림");
        menuToggle.RegisterValueChangedCallback(evt => menuOpen = evt.newValue);
        root.Add(menuToggle);
        trackedToggle = new Toggle("왼손 추적 중");
        trackedToggle.RegisterValueChangedCallback(evt => tracked = evt.newValue);
        root.Add(trackedToggle);
        status = new Label();
        status.style.marginTop = status.style.marginBottom = 6;
        root.Add(status);
        viewport = new UnityEngine.UIElements.Image { scaleMode = ScaleMode.ScaleToFit, focusable = true };
        viewport.style.flexGrow = 1;
        viewport.style.minHeight = 300;
        viewport.style.backgroundColor = new Color(0.035f, 0.04f, 0.05f);
        viewport.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            dragging = true;
            lastPointer = evt.position;
            viewport.Focus();
            viewport.CapturePointer(evt.pointerId);
        });
        viewport.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!dragging) return;
            Vector2 current = evt.position;
            look += (current - lastPointer) * 0.15f;
            look.y = Mathf.Clamp(look.y, -80f, 80f);
            lastPointer = current;
        });
        viewport.RegisterCallback<PointerUpEvent>(evt =>
        {
            dragging = false;
            viewport.ReleasePointer(evt.pointerId);
        });
        viewport.RegisterCallback<KeyDownEvent>(OnKey);
        root.Add(viewport);
        try
        {
            BuildPreview();
            SetPose("raised");
            viewport.image = frame;
        }
        catch (Exception exception)
        {
            status.text = exception.Message;
            Debug.LogException(exception);
        }
    }

    private static void AddButton(VisualElement row, string text, Action action)
    {
        Button button = new(action) { text = text };
        button.style.marginRight = 4;
        row.Add(button);
    }

    private void OnEnable()
    {
        lastTick = EditorApplication.timeSinceStartup;
        EditorApplication.update += UpdatePreview;
    }

    private void OnDisable()
    {
        EditorApplication.update -= UpdatePreview;
        DisposePreview();
    }

    private void BuildPreview()
    {
        DisposePreview();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play 모드를 종료한 뒤 미리보기를 열어주세요.");
        Scene source = SceneManager.GetSceneByPath(LobbyPath);
        bool closeSource = !source.IsValid() || !source.isLoaded;
        if (closeSource) source = EditorSceneManager.OpenScene(LobbyPath, OpenSceneMode.Additive);
        try
        {
            MushLobbyStaminaHud sourceHud = null;
            Camera sourceCamera = null;
            foreach (GameObject root in source.GetRootGameObjects())
            {
                if (sourceHud == null) sourceHud = root.GetComponentInChildren<MushLobbyStaminaHud>(true);
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    if (sourceCamera == null || camera.CompareTag("MainCamera")) sourceCamera = camera;
            }
            if (sourceHud == null || sourceCamera == null)
                throw new InvalidOperationException("PM_Lobby의 상태 UI 또는 카메라를 찾지 못했습니다.");
            previewScene = EditorSceneManager.NewPreviewScene();
            GameObject cameraObject = NewPreviewObject("PC preview eye");
            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.CopyFrom(sourceCamera);
            previewCamera.enabled = false;
            previewCamera.scene = previewScene;
            previewCamera.cameraType = CameraType.Game;
            previewCamera.nearClipPlane = 0.02f;
            previewCamera.farClipPlane = 80f;
            previewCamera.fieldOfView = 60f;
            previewCamera.aspect = (float)Width / Height;
            previewCamera.cullingMask = ~0;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.14f, 0.18f, 0.23f);
            UniversalAdditionalCameraData cameraData = previewCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.allowXRRendering = false;
            eyePosition = sourceCamera.transform.position;
            eyeRotation = Quaternion.Euler(0f, sourceCamera.transform.eulerAngles.y, 0f);

            // Copy only rendered geometry; gameplay scripts and scene saves are never run.
            foreach (GameObject root in source.GetRootGameObjects())
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || filter == null ||
                        filter.sharedMesh == null || renderer.GetComponentInParent<Canvas>() != null ||
                        renderer.GetComponentInParent<Camera>() != null) continue;
                    GameObject copy = NewPreviewObject(renderer.name);
                    copy.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    copy.transform.localScale = renderer.transform.lossyScale;
                    copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    MeshRenderer target = copy.AddComponent<MeshRenderer>();
                    target.sharedMaterials = renderer.sharedMaterials;
                    target.shadowCastingMode = renderer.shadowCastingMode;
                    target.receiveShadows = renderer.receiveShadows;
                }
            AddLight("Preview key light", new Vector3(35f, -35f, 0f), new Color(1f, 0.86f, 0.69f), 1.2f);
            AddLight("Preview fill light", new Vector3(35f, 140f, 0f), new Color(0.65f, 0.8f, 1f), 0.65f);

            GameObject handPrefab = Resources.Load<GameObject>("MushLeftHand");
            GameObject handObject = handPrefab != null ? Instantiate(handPrefab) : new GameObject("Virtual left wrist");
            SceneManager.MoveGameObjectToScene(handObject, previewScene);
            hand = handObject.transform;
            GameObject hudObject = Instantiate(sourceHud.gameObject);
            hudObject.name = "Lobby Stamina HUD (PC preview copy)";
            SceneManager.MoveGameObjectToScene(hudObject, previewScene);
            MushLobbyStaminaHud hud = hudObject.GetComponent<MushLobbyStaminaHud>();
            hud.enabled = false;
            hud.RefreshEditorPreview();
            SerializedObject settings = new(sourceHud);
            wristOffset = settings.FindProperty("vrWristOffset").vector3Value;
            wristWidth = settings.FindProperty("vrWristWidth").floatValue;
            wrist = hudObject.GetComponent<MushVrWristHud>() ?? hudObject.AddComponent<MushVrWristHud>();
            wrist.ConfigureEditorPreview(previewCamera, wristOffset, wristWidth);
            SceneManager.MoveGameObjectToScene(hudObject.transform.parent.gameObject, previewScene);
            visibility = hudObject.GetComponent<CanvasGroup>();
            frame = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Mush PC VR Preview" };
            frame.Create();
            previewCamera.targetTexture = frame;
        }
        finally
        {
            if (closeSource) EditorSceneManager.CloseScene(source, true);
        }
    }

    private GameObject NewPreviewObject(string objectName)
    {
        GameObject root = new(objectName);
        SceneManager.MoveGameObjectToScene(root, previewScene);
        return root;
    }

    private void AddLight(string lightName, Vector3 rotation, Color color, float intensity)
    {
        GameObject root = NewPreviewObject(lightName);
        root.transform.rotation = Quaternion.Euler(rotation);
        Light light = root.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.Soft;
    }

    private void UpdatePreview()
    {
        if (previewCamera == null || wrist == null || frame == null) return;
        double now = EditorApplication.timeSinceStartup;
        float delta = Mathf.Clamp((float)(now - lastTick), 0f, 0.1f);
        if (delta < 1f / 30f) return;
        lastTick = now;
        Tick(delta);
        RenderFrame();
        if (status != null)
            status.text = $"{(visibility.alpha > 0.01f ? "표시 중" : "숨김")}  ·  실제 UI 폭 {wristWidth * 100f:0}cm  ·  눈에서 UI까지 {Vector3.Distance(previewCamera.transform.position, wrist.transform.parent.position):0.00}m  ·  헤드셋 연결 불필요";
        viewport?.MarkDirtyRepaint();
    }

    private void Tick(float delta)
    {
        previewCamera.transform.SetPositionAndRotation(eyePosition, eyeRotation * Quaternion.Euler(look.y, look.x, 0f));
        hand.SetPositionAndRotation(eyePosition + eyeRotation * handPosition, eyeRotation * Quaternion.Euler(handRotation));
        wrist.MenuOpen = menuOpen;
        wrist.RefreshEditorPreview(hand, tracked, delta);
        Canvas.ForceUpdateCanvases();
    }

    private void RenderFrame()
    {
        UniversalRenderPipeline.SingleCameraRequest request = new() { destination = frame };
        if (RenderPipeline.SupportsRenderRequest(previewCamera, request))
            RenderPipeline.SubmitRenderRequest(previewCamera, request);
        else previewCamera.Render();
    }

    private void SetPose(string pose)
    {
        handPosition = new Vector3(-0.18f, -0.24f, 0.55f);
        handRotation = Vector3.zero;
        look = Vector2.zero;
        tracked = true;
        menuOpen = false;
        switch (pose)
        {
            case "lowered": handPosition.y = -0.8f; break;
            case "rotated": handPosition = new Vector3(-0.12f, -0.22f, 0.55f); handRotation = new Vector3(10f, 35f, 25f); break;
            case "look-away": look.x = 100f; break;
            case "menu": menuOpen = true; break;
            case "tracking-lost": tracked = false; break;
        }
        positionField?.SetValueWithoutNotify(handPosition);
        rotationField?.SetValueWithoutNotify(handRotation);
        menuToggle?.SetValueWithoutNotify(menuOpen);
        trackedToggle?.SetValueWithoutNotify(tracked);
    }

    private void OnKey(KeyDownEvent evt)
    {
        float step = evt.shiftKey ? 0.05f : 0.01f;
        switch (evt.keyCode)
        {
            case KeyCode.LeftArrow: handPosition.x -= step; break;
            case KeyCode.RightArrow: handPosition.x += step; break;
            case KeyCode.UpArrow: handPosition.z += step; break;
            case KeyCode.DownArrow: handPosition.z -= step; break;
            case KeyCode.R: handPosition.y += step; break;
            case KeyCode.F: handPosition.y -= step; break;
            case KeyCode.Q: handRotation.z -= 5f; break;
            case KeyCode.E: handRotation.z += 5f; break;
            default: return;
        }
        positionField.SetValueWithoutNotify(handPosition);
        rotationField.SetValueWithoutNotify(handRotation);
        evt.StopPropagation();
    }

    private void SaveFromWindow()
    {
        string path = EditorUtility.SaveFilePanel("PC VR 미리보기 캡처", "", "MushWristPreview.png", "png");
        if (!string.IsNullOrEmpty(path)) Capture(path);
    }

    private void Capture(string path)
    {
        RenderFrame();
        RenderTexture previous = RenderTexture.active;
        Texture2D pixels = new(Width, Height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = frame;
            pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            DestroyImmediate(pixels);
        }
    }

    private void DisposePreview()
    {
        if (wrist != null) wrist.Release();
        wrist = null;
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (frame != null)
        {
            frame.Release();
            DestroyImmediate(frame);
        }
        frame = null;
        if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
        previewCamera = null;
        hand = null;
    }

    public static string RunCaptureValidation(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        MushVrUiPreviewWindow preview = CreateInstance<MushVrUiPreviewWindow>();
        List<string> results = new();
        try
        {
            preview.CreateGUI();
            if (preview.wrist == null || preview.viewport.image != preview.frame)
                throw new InvalidOperationException("PC 미리보기 창 초기화에 실패했습니다.");
            foreach (string pose in new[] { "raised", "rotated", "lowered", "look-away", "menu", "tracking-lost" })
            {
                preview.SetPose(pose);
                for (int i = 0; i < 15; i++) preview.Tick(1f / 30f);
                bool shouldShow = pose is "raised" or "rotated";
                if ((preview.visibility.alpha > 0.99f) != shouldShow || (!shouldShow && preview.visibility.alpha > 0.01f))
                    throw new InvalidOperationException($"{pose}: unexpected HUD alpha {preview.visibility.alpha}");
                if (shouldShow && Vector3.Distance(preview.wrist.transform.parent.position, preview.hand.TransformPoint(preview.wristOffset)) > 0.001f)
                    throw new InvalidOperationException($"{pose}: HUD did not follow the left wrist");
                string path = Path.Combine(outputDirectory, $"wrist-{pose}.png");
                preview.Capture(path);
                results.Add($"PASS {pose}: alpha={preview.visibility.alpha:0.00}; screenshot={path}");
            }
            string report = string.Join("\n", results);
            File.WriteAllText(Path.Combine(outputDirectory, "pc-preview-results.txt"), report);
            return report;
        }
        finally
        {
            preview.DisposePreview();
            DestroyImmediate(preview);
        }
    }

    public static void CaptureBatch()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/MushVrPreview"));
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (arguments[i] == "-mushVrPreviewOutput") output = arguments[i + 1];
        try { Debug.Log(RunCaptureValidation(output)); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
