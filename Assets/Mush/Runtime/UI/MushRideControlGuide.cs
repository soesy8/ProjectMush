using System.Collections;
using UnityEngine;
using Mush.Quest;

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
    private Vector2 authoredRestingPosition, authoredAnchorMin, authoredAnchorMax, authoredPivot;
    private bool vrLayoutApplied;
    private bool vrPausedLayout;
    private bool initialized;
    private bool departureHandled;
    private bool departing;
    private bool paused;
    private float departureElapsed;
    private bool optionsVisible;

    private void Awake()
    {
        if (guideRoot == null || guideGroup == null) return;
        restingPosition = guideRoot.anchoredPosition;
        authoredRestingPosition = restingPosition;
        authoredAnchorMin = guideRoot.anchorMin;
        authoredAnchorMax = guideRoot.anchorMax;
        authoredPivot = guideRoot.pivot;
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
        UpdateViewLayout();
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
        float rise = MushQuestTrackedInputRig.IsXrActive ? Mathf.Min(departureRise, 80f) : departureRise;
        guideRoot.anchoredPosition = restingPosition + Vector2.up * (rise * eased);
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
        UpdateViewLayout();
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

    private void UpdateViewLayout()
    {
        if (guideRoot == null) return;
        if (MushQuestTrackedInputRig.IsXrActive)
        {
            if (vrLayoutApplied && vrPausedLayout == paused) return;
            guideRoot.anchorMin = new Vector2(authoredAnchorMin.x, 0.5f);
            guideRoot.anchorMax = new Vector2(authoredAnchorMax.x, 0.5f);
            guideRoot.pivot = new Vector2(authoredPivot.x, 0.5f);
            restingPosition = new Vector2(authoredRestingPosition.x, paused ? -340f : -160f);
            vrLayoutApplied = true;
            vrPausedLayout = paused;
        }
        else
        {
            if (!vrLayoutApplied) return;
            guideRoot.anchorMin = authoredAnchorMin;
            guideRoot.anchorMax = authoredAnchorMax;
            guideRoot.pivot = authoredPivot;
            restingPosition = authoredRestingPosition;
            vrLayoutApplied = false;
        }
        guideRoot.anchoredPosition = restingPosition;
    }

}
