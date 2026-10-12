using System.Collections;
using UnityEngine;
using Mush.Quest;
using TMPro;
using UnityEngine.UI;

/// <summary>Shows the authored control cards before departure and alongside the pause menu.</summary>
[DefaultExecutionOrder(200)]
public sealed class MushRideControlGuide : MonoBehaviour
{
    [SerializeField] private MushMapRideBootstrap ride;
    [SerializeField] private RectTransform guideRoot;
    [SerializeField] private CanvasGroup guideGroup;
    [SerializeField, Min(0.01f)] private float departureDuration = 0.45f;
    [SerializeField, Min(0f)] private float departureRise = 320f;

    private Vector2 restingPosition;
    private bool initialized;
    private bool departureHandled;
    private bool departing;
    private bool paused;
    private float departureElapsed;
    private bool vrArtworkConfigured;
    private bool optionsVisible;

    private void Awake()
    {
        if (guideRoot == null || guideGroup == null) return;
        restingPosition = guideRoot.anchoredPosition;
        guideGroup.interactable = false;
        guideGroup.blocksRaycasts = false;
        Hide();
    }

    private IEnumerator Start()
    {
        // MushSceneUI restores a continued ride after the bootstrap has bound its controller.
        // Wait for that restore so an already-started save never flashes the departure cards.
        yield return null;
        if (ride == null || guideRoot == null || guideGroup == null) yield break;
        initialized = true;
        departureHandled = ride.HasRideStarted;
        paused = ride.IsPaused;
        if (!ride.HasFinished && (paused || !departureHandled)) Show();
    }

    private void LateUpdate()
    {
        if (!initialized) return;
        if (optionsVisible) { Hide(); return; }
        if (ride.HasFinished)
        {
            departing = false;
            Hide();
            return;
        }

        if (paused != ride.IsPaused) SetPaused(ride.IsPaused);
        if (paused) return;

        if (!departureHandled && ride.HasRideStarted)
        {
            departureHandled = true;
            departing = true;
            departureElapsed = 0f;
        }
        if (!departing) return;

        departureElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(departureElapsed / Mathf.Max(0.01f, departureDuration));
        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        guideRoot.anchoredPosition = restingPosition + Vector2.up * (departureRise * eased);
        guideGroup.alpha = 1f - progress;
        if (progress >= 1f)
        {
            departing = false;
            Hide();
        }
    }

    public void SetPaused(bool value)
    {
        paused = value;
        departing = false;
        if (ride != null && ride.HasRideStarted) departureHandled = true;
        if (guideRoot == null || guideGroup == null) return;
        if (!optionsVisible && ride != null && !ride.HasFinished && (paused || !departureHandled)) Show();
        else Hide();
    }

    public void SetOptionsVisible(bool value)
    {
        optionsVisible = value;
        if (guideRoot == null || guideGroup == null) return;
        if (optionsVisible) Hide();
        else SetPaused(ride != null && ride.IsPaused);
    }

    private void Show()
    {
        ConfigureVrGuide();
        guideRoot.anchoredPosition = restingPosition;
        guideGroup.alpha = 1f;
        guideRoot.gameObject.SetActive(true);
    }

    private void Hide()
    {
        guideGroup.alpha = 0f;
        guideRoot.gameObject.SetActive(false);
        guideRoot.anchoredPosition = restingPosition;
    }

    private void ConfigureVrGuide()
    {
        if (!MushQuestTrackedInputRig.IsXrActive) return;
        // Keep the ZIP's keyboard artwork and layout for PC; VR has its own controller guide.
        guideRoot.anchorMin = new Vector2(0.06f, paused ? 0f : 0.5f);
        guideRoot.anchorMax = new Vector2(0.94f, paused ? 0f : 0.5f);
        guideRoot.pivot = new Vector2(0.5f, paused ? 0f : 0.5f);
        restingPosition = new Vector2(0f, paused ? 85f : 0f);
        if (vrArtworkConfigured) return;
        vrArtworkConfigured = true;
        TMP_Text template = GetComponentInChildren<TMP_Text>(true);
        string[] names = { "Control Start", "Control Accelerate", "Control Steer" };
        string[] instructions = { "출발\n양손 그립 잡기", "가속\n검지 트리거 유지", "조향\n고삐를 당겨 좌우 회전" };
        for (int index = 0; index < names.Length; index++)
        {
            Transform card = guideRoot.Find(names[index]);
            if (card == null) continue;
            Image background = card.GetComponent<Image>();
            if (background != null)
            {
                background.sprite = null;
                background.color = new Color(0.025f, 0.045f, 0.075f, 0.92f);
            }
            CreateVrLabel(card, "VR Control Text", instructions[index], template, 36f, true);
        }
        TMP_Text caption = CreateVrLabel(guideRoot, "VR Menu Controls",
            "X 버튼: 메뉴 열기/닫기   ·   A 버튼: 코스 복귀", template, 28f, false);
        LayoutElement element = caption.gameObject.AddComponent<LayoutElement>();
        element.ignoreLayout = true;
        RectTransform rect = caption.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(1200f, 50f);
        rect.anchoredPosition = new Vector2(0f, -12f);
    }

    private static TMP_Text CreateVrLabel(Transform parent, string name, string text,
        TMP_Text template, float size, bool stretch)
    {
        GameObject root = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        root.layer = 5;
        root.transform.SetParent(parent, false);
        TMP_Text label = root.GetComponent<TMP_Text>();
        if (template != null) label.font = template.font;
        label.text = text;
        label.color = Color.white;
        label.fontSize = size;
        label.fontSizeMin = 26f;
        label.fontSizeMax = size;
        label.enableAutoSizing = true;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        if (stretch)
        {
            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 12f);
            rect.offsetMax = new Vector2(-16f, -12f);
        }
        return label;
    }
}
