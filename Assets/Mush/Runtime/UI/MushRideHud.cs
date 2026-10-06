using TMPro;
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
    [SerializeField] private TMP_Text staminaText;
    [SerializeField] private TMP_Text secondStaminaText;
    [SerializeField] private RectTransform progressIcon;
    [SerializeField] private GameObject progressRoot;
    [SerializeField] private GameObject trackRoot;
    private int previousSeconds = -1;
    private int previousFirstStamina = -1;
    private int previousSecondStamina = -1;

    private void OnEnable()
    {
        previousSeconds = previousFirstStamina = previousSecondStamina = -1;
    }

    private void LateUpdate()
    {
        if (ride == null) return;
        bool visible = !ride.HasFinished;
        RefreshProgress(ride.RouteProgress);
        if (trackRoot != null && trackRoot.activeSelf != visible) trackRoot.SetActive(visible);
        if (progressRoot != null && progressRoot.activeSelf != visible) progressRoot.SetActive(visible);
        if (!visible) return;
        RefreshStamina(staminaFill, staminaText, 0, ref previousFirstStamina);
        RefreshStamina(secondStaminaFill, secondStaminaText, 1, ref previousSecondStamina);
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

    private static void RefreshStamina(Image fill, TMP_Text label, int dogIndex,
        ref int previousValue)
    {
        float value = Mathf.Clamp(MushGameSave.GetDogStamina(dogIndex), 0f, 100f);
        if (fill != null) fill.fillAmount = value / 100f;
        int displayed = Mathf.FloorToInt(value);
        if (label != null && displayed != previousValue)
            label.text = $"{displayed} / 100";
        previousValue = displayed;
    }
}
