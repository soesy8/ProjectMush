using TMPro;
using Mush.Quest;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>Updates the scene-authored track panels from the actual ride state.</summary>
[DefaultExecutionOrder(100)]
public sealed class MushRideHud : MonoBehaviour
{
    [SerializeField] private MushMapRideBootstrap ride;
    [SerializeField] private TMP_Text timer;
    [SerializeField] private Image progress;
    [SerializeField, FormerlySerializedAs("staminaDepletion")] private Image staminaFill;
    [SerializeField] private Image secondStaminaFill;
    [SerializeField] private bool useTeamStamina;
    [SerializeField] private TMP_Text staminaText;
    [SerializeField] private TMP_Text secondStaminaText;
    [SerializeField] private Image firstPortrait;
    [SerializeField] private Sprite firstNormalPortrait;
    [SerializeField] private Sprite firstGoodPortrait;
    [SerializeField] private Sprite firstBadPortrait;
    [SerializeField] private Image secondPortrait;
    [SerializeField] private Sprite secondNormalPortrait;
    [SerializeField] private Sprite secondGoodPortrait;
    [SerializeField] private Sprite secondBadPortrait;
    [SerializeField] private RectTransform progressIcon;
    [SerializeField] private GameObject progressRoot;
    [SerializeField] private GameObject trackRoot;
    private int previousSeconds = -1;
    private int previousFirstStamina = -1;
    private int previousSecondStamina = -1;
    private RectTransform progressRect;
    private Vector2 authoredProgressAnchorMin, authoredProgressAnchorMax;
    private Vector2 authoredProgressPivot, authoredProgressPosition;
    private bool vrProgressPlaced;

    private void OnEnable()
    {
        previousSeconds = previousFirstStamina = previousSecondStamina = -1;
    }

    private void LateUpdate()
    {
        if (ride == null) return;
        UpdateVrProgressPosition();
        bool visible = !ride.HasFinished;
        RefreshProgress(ride.RouteProgress);
        if (trackRoot != null && trackRoot.activeSelf != visible) trackRoot.SetActive(visible);
        if (progressRoot != null && progressRoot.activeSelf != visible) progressRoot.SetActive(visible);
        if (!visible) return;
        if (useTeamStamina)
            RefreshStaminaValue(staminaFill, staminaText, MushGameSave.TeamStamina, ref previousFirstStamina);
        else
        {
            RefreshStamina(staminaFill, staminaText, 0, ref previousFirstStamina);
            RefreshStamina(secondStaminaFill, secondStaminaText, 1, ref previousSecondStamina);
            RefreshPortrait(firstPortrait, MushGameSave.GetDogCondition(0),
                firstNormalPortrait, firstGoodPortrait, firstBadPortrait);
            RefreshPortrait(secondPortrait, MushGameSave.GetDogCondition(1),
                secondNormalPortrait, secondGoodPortrait, secondBadPortrait);
        }
        int seconds = Mathf.CeilToInt(ride.RemainingSeconds);
        if (timer != null)
        {
            if (ride.ShowingOffCourseTimePenalty)
            {
                timer.text = $"-{Mathf.CeilToInt(ride.OffCourseTimePenalty)}초";
                timer.color = new Color(1f, 0.10f, 0.06f);
                // Restore the countdown even when the displayed second has not changed.
                previousSeconds = -1;
            }
            else
            {
                if (seconds != previousSeconds)
                {
                    timer.text = $"{seconds / 60:00}:{seconds % 60:00}";
                    previousSeconds = seconds;
                }
                timer.color = seconds <= 10 ? new Color(1f, 0.3f, 0.2f) : Color.white;
            }
        }
    }

    private void UpdateVrProgressPosition()
    {
        bool vr = MushQuestTrackedInputRig.IsXrActive;
        if (vr == vrProgressPlaced) return;
        if (vr)
        {
            progressRect = progressRoot != null ? progressRoot.GetComponent<RectTransform>() : null;
            if (progressRect == null) return;
            authoredProgressAnchorMin = progressRect.anchorMin;
            authoredProgressAnchorMax = progressRect.anchorMax;
            authoredProgressPivot = progressRect.pivot;
            authoredProgressPosition = progressRect.anchoredPosition;
            progressRect.anchorMin = new Vector2(authoredProgressAnchorMin.x, 0.5f);
            progressRect.anchorMax = new Vector2(authoredProgressAnchorMax.x, 0.5f);
            progressRect.pivot = new Vector2(authoredProgressPivot.x, 0.5f);
            // Keep the complete authored progress panel close to the headset's eye line.
            progressRect.anchoredPosition = new Vector2(authoredProgressPosition.x, 150f);
            vrProgressPlaced = true;
        }
        else RestoreProgressPosition();
    }

    private void RestoreProgressPosition()
    {
        if (!vrProgressPlaced || progressRect == null) return;
        progressRect.anchorMin = authoredProgressAnchorMin;
        progressRect.anchorMax = authoredProgressAnchorMax;
        progressRect.pivot = authoredProgressPivot;
        progressRect.anchoredPosition = authoredProgressPosition;
        vrProgressPlaced = false;
    }

    private void OnDisable() => RestoreProgressPosition();

    private void RefreshProgress(float routeProgress)
    {
        float value = Mathf.Clamp01(routeProgress);
        if (progress == null) return;
        progress.fillAmount = value;
        if (progressIcon == null) return;

        // Convert the fill edge into the icon parent's coordinates, so the marker
        // follows the authored bar even when its anchors or width change.
        Rect rect = progress.rectTransform.rect;
        Vector3 edge = progress.rectTransform.TransformPoint(
            new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, value), rect.center.y, 0f));
        Vector3 local = progressIcon.parent.InverseTransformPoint(edge);
        Vector3 position = progressIcon.localPosition;
        position.x = local.x;
        progressIcon.localPosition = position;
    }

    private static void RefreshPortrait(Image portrait, MushDogCondition condition, Sprite normal, Sprite good, Sprite bad)
    {
        if (portrait == null) return;
        Sprite artwork = condition switch
        {
            MushDogCondition.Good => good,
            MushDogCondition.Bad => bad,
            _ => normal,
        };
        if (artwork != null && portrait.sprite != artwork)
            portrait.sprite = artwork;
    }

    private static void RefreshStamina(Image fill, TMP_Text label, int dogIndex,
        ref int previousValue)
    {
        RefreshStaminaValue(fill, label, MushGameSave.GetDogStamina(dogIndex), ref previousValue);
    }

    private static void RefreshStaminaValue(Image fill, TMP_Text label, float stamina,
        ref int previousValue)
    {
        float value = Mathf.Clamp(stamina, 0f, 100f);
        if (fill != null) fill.fillAmount = value / 100f;
        int displayed = Mathf.FloorToInt(value);
        if (label != null && displayed != previousValue)
            label.text = $"{displayed} / 100";
        previousValue = displayed;
    }
}
