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
    [SerializeField] private RectTransform progressIcon;
    [SerializeField] private GameObject progressRoot;
    [SerializeField] private GameObject trackRoot;
    [SerializeField] private GameObject standingDog;
    [SerializeField] private RectTransform runningDog;
    private int previousSeconds = -1;

    private void Awake()
    {
        if (staminaFill == null && trackRoot != null)
        {
            foreach (Image image in trackRoot.GetComponentsInChildren<Image>(true))
            {
                if (image.name != "FilledImage") continue;
                staminaFill = image;
                break;
            }
        }
        if (staminaFill == null) return;
        staminaFill.type = Image.Type.Filled;
        staminaFill.fillMethod = Image.FillMethod.Horizontal;
        staminaFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        staminaFill.fillAmount = ride != null ? Mathf.Clamp01(ride.Stamina01) : 1f;
    }

    private void LateUpdate()
    {
        if (ride == null) return;
        bool visible = !ride.HasFinished;
        if (trackRoot != null && trackRoot.activeSelf != visible) trackRoot.SetActive(visible);
        if (progressRoot != null && progressRoot.activeSelf != visible) progressRoot.SetActive(visible);
        if (!visible) return;
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
        // Current route projection deliberately decreases when the sled travels backwards.
        float value = Mathf.Clamp01(ride.RouteProgress);
        if (progress != null) progress.fillAmount = value;
        if (staminaFill != null) staminaFill.fillAmount = Mathf.Clamp01(ride.Stamina01);
        if (progressIcon != null && progress != null)
        {
            RectTransform rect = progress.rectTransform;
            progressIcon.position = rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, value), rect.rect.center.y, 0f));
        }
        bool boosting = ride.IsBoosting;
        if (standingDog != null && standingDog.activeSelf == boosting) standingDog.SetActive(!boosting);
        if (runningDog != null && runningDog.gameObject.activeSelf != boosting) runningDog.gameObject.SetActive(boosting);
    }

}
