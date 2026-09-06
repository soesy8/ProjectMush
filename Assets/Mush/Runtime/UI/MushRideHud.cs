using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Connects the supplied TrackUI and ProgressUI to the existing ride state.</summary>
public sealed class MushRideHud : MonoBehaviour
{
    [SerializeField] private MushMapRideBootstrap ride;
    [SerializeField] private TextMesh timerSource;
    [SerializeField] private TMP_Text timer;
    [SerializeField] private Image progress;
    [SerializeField] private RectTransform progressIcon;
    [SerializeField] private GameObject progressRoot;
    [SerializeField] private GameObject trackRoot;
    [SerializeField] private GameObject standingDog;
    [SerializeField] private RectTransform runningDog;
    private Vector2 runningRest;
    private TMP_FontAsset runtimeFont;

    public void Configure(MushMapRideBootstrap source, TextMesh oldTimer, GameObject track, GameObject bar)
    {
        ride = source;
        timerSource = oldTimer;
        trackRoot = track;
        progressRoot = bar;
        timer = Find<TMP_Text>(track.transform, "TimerText");
        progress = Find<Image>(bar.transform, "ProgressBar");
        progressIcon = Find<RectTransform>(bar.transform, "ProgressIcon");
        standingDog = Find<Transform>(track.transform, "DogStateImage_Normal")?.gameObject;
        runningDog = Find<RectTransform>(track.transform, "DogStateImage_Fast");
        if (progress != null)
        {
            progress.type = Image.Type.Filled;
            progress.fillMethod = Image.FillMethod.Horizontal;
            progress.fillOrigin = (int)Image.OriginHorizontal.Left;
            progress.fillAmount = 0f;
        }
        PrepareRuntimeFont();
        PositionProgressAtScreenTop();
        if (timer != null) timer.text = oldTimer != null ? oldTimer.text : "02:00";
        if (standingDog != null) standingDog.SetActive(true);
        if (runningDog != null) runningDog.gameObject.SetActive(false);
    }

    private void Awake()
    {
        PrepareRuntimeFont();
        if (runningDog != null) runningRest = runningDog.anchoredPosition;
    }

    private void Start() => PositionProgressAtScreenTop();

    private void PositionProgressAtScreenTop()
    {
        if (!Application.isPlaying || progressRoot == null || progress == null) return;
        Canvas canvas = progressRoot.GetComponentInParent<Canvas>();
        Camera camera = progressRoot.GetComponentInParent<Camera>();
        if (camera == null) camera = Camera.main;
        if (canvas == null || camera == null) return;

        // Camera-space UI follows the viewport in both desktop and XR rendering.
        canvas.transform.localScale = Vector3.one;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = Mathf.Max(1f, camera.nearClipPlane + 0.5f);
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        if (canvas.transform != progressRoot.transform && progressRoot.transform is RectTransform root)
        {
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.localScale = Vector3.one;
        }
        RectTransform background = Find<RectTransform>(progressRoot.transform, "ProgressImage");
        if (background == null) return;
        background.anchorMin = new Vector2(0.1f, 1f);
        background.anchorMax = new Vector2(0.9f, 1f);
        background.pivot = Vector2.one * 0.5f;
        background.anchoredPosition = new Vector2(0f, -54f);
        background.sizeDelta = new Vector2(0f, 48f);
        background.localScale = Vector3.one;
        RectTransform fill = progress.rectTransform;
        fill.anchorMin = new Vector2(0f, 0.5f);
        fill.anchorMax = new Vector2(1f, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = new Vector2(-32f, 26f);
        if (progressIcon != null) progressIcon.sizeDelta = new Vector2(42f, 42f);
    }

    private void PrepareRuntimeFont()
    {
        if (!Application.isPlaying || timer == null || timer.font == null) return;
        if (runtimeFont == null)
        {
            Font source = timer.font.sourceFontFile;
            if (source == null) return;
            // A separate atlas avoids modifying/reimporting the supplied project asset during play.
            runtimeFont = TMP_FontAsset.CreateFontAsset(source);
            if (runtimeFont == null) return;
            runtimeFont.name = "Mush Ride UI Runtime Font";
            runtimeFont.hideFlags = HideFlags.DontSave;
            runtimeFont.material.hideFlags = HideFlags.DontSave;
            runtimeFont.TryAddCharacters("배달 완료!기록최고로비다시하기초0123456789:- ", out _);
            foreach (Texture2D atlas in runtimeFont.atlasTextures)
                if (atlas != null) atlas.hideFlags = HideFlags.DontSave;
        }
        timer.font = runtimeFont;
        timer.fontSharedMaterial = runtimeFont.material;
    }

    private void OnDestroy()
    {
        if (runtimeFont == null) return;
        foreach (Texture2D atlas in runtimeFont.atlasTextures)
            if (atlas != null) Destroy(atlas);
        Destroy(runtimeFont);
    }

    private void LateUpdate()
    {
        if (ride == null) return;
        bool visible = !ride.HasFinished;
        if (trackRoot != null) trackRoot.SetActive(visible);
        if (progressRoot != null) progressRoot.SetActive(visible);
        if (!visible) return;
        if (timer != null && timerSource != null)
        {
            if (timer.text != timerSource.text) timer.text = timerSource.text;
            timer.color = timerSource.color;
        }
        float value = ride.RouteProgress;
        if (progress != null) progress.fillAmount = value;
        if (progressIcon != null && progress != null)
        {
            RectTransform rect = progress.rectTransform;
            progressIcon.position = rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, value), rect.rect.center.y, 0f));
        }
        bool running = ride.IsMoving;
        if (standingDog != null) standingDog.SetActive(!running);
        if (runningDog != null)
        {
            runningDog.gameObject.SetActive(running);
            float phase = Time.time * 13f;
            runningDog.anchoredPosition = runningRest + (running ? Vector2.up * Mathf.Abs(Mathf.Sin(phase)) * 4f : Vector2.zero);
            runningDog.localRotation = Quaternion.Euler(0f, 0f, running ? Mathf.Sin(phase) * 4f : 0f);
        }
    }

    private static T Find<T>(Transform root, string objectName) where T : Component
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child.GetComponent<T>();
        return null;
    }
}
