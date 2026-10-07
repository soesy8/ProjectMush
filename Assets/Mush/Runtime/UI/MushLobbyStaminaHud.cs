using System.Collections.Generic;
using Mush.Quest;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

[DisallowMultipleComponent]
public sealed class MushLobbyStaminaHud : MonoBehaviour
{
    private const float VrCanvasDistance = 1.65f;
    private const float VrCanvasScale = 0.00105f;
    private static readonly List<XRDisplaySubsystem> XrDisplays = new();
    [SerializeField] private Image staminaFill;
    [SerializeField] private TMP_Text staminaText;
    [SerializeField] private MushDogConditionIcon conditionIcon;
    [SerializeField] private TMP_Text conditionText;
    [SerializeField] private Image secondStaminaFill;
    [SerializeField] private TMP_Text secondStaminaText;
    [SerializeField] private MushDogConditionIcon secondConditionIcon;
    [SerializeField] private TMP_Text secondConditionText;
    private int displayedFirstStamina = -1;
    private int displayedSecondStamina = -1;
    private MushDogCondition? displayedFirstCondition;
    private MushDogCondition? displayedSecondCondition;
    private bool vrCanvasConfigured;
    private bool individualUiReady;

    private void OnEnable()
    {
        displayedFirstStamina = -1;
        displayedSecondStamina = -1;
        displayedFirstCondition = null;
        displayedSecondCondition = null;
        BindAuthoredDogUi();
        TryConfigureVrCanvas();
        Refresh();
    }

    private void LateUpdate()
    {
        if (!vrCanvasConfigured)
            TryConfigureVrCanvas();
        Refresh();
    }

    // Layout, graphics and both dogs' rows are authored in the scene.
    private void BindAuthoredDogUi()
    {
        individualUiReady = staminaFill != null && staminaText != null &&
                            secondStaminaFill != null && secondStaminaText != null &&
                            secondConditionIcon != null && secondConditionText != null;
    }

    private void TryConfigureVrCanvas()
    {
        if (vrCanvasConfigured || !IsXrDisplayRunning())
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
        Camera vrCamera = canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        if (canvas == null || canvasRect == null || vrCamera == null)
            return;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = vrCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 250;
        MushVrUiLayout.PlaceFixed(canvas, vrCamera, VrCanvasDistance, VrCanvasScale, 0.12f);
        Canvas.ForceUpdateCanvases();
        vrCanvasConfigured = true;
    }

    private static bool IsXrDisplayRunning()
    {
        XrDisplays.Clear();
        SubsystemManager.GetSubsystems(XrDisplays);
        foreach (XRDisplaySubsystem display in XrDisplays)
        {
            if (display != null && display.running)
                return true;
        }
        return false;
    }

    private void Refresh()
    {
        if (!individualUiReady)
            BindAuthoredDogUi();

        MushDogCondition firstCondition = MushGameSave.GetDogCondition(0);
        if (firstCondition != displayedFirstCondition)
        {
            displayedFirstCondition = firstCondition;
            if (conditionText != null)
                conditionText.text = ConditionLabel(firstCondition);
        }
        MushDogCondition secondCondition = MushGameSave.GetDogCondition(1);
        if (secondCondition != displayedSecondCondition)
        {
            displayedSecondCondition = secondCondition;
            if (secondConditionText != null)
                secondConditionText.text = ConditionLabel(secondCondition);
        }
        int firstStamina = Mathf.Clamp(Mathf.FloorToInt(MushGameSave.GetDogStamina(0)), 0, 100);
        if (firstStamina != displayedFirstStamina)
        {
            displayedFirstStamina = firstStamina;
            conditionIcon?.SetStamina(firstStamina);
            if (staminaFill != null)
                staminaFill.fillAmount = firstStamina / 100f;
            if (staminaText != null)
                staminaText.SetText("카이  {0} / 100", firstStamina);
        }

        int secondStamina = Mathf.Clamp(Mathf.FloorToInt(MushGameSave.GetDogStamina(1)), 0, 100);
        if (secondStamina != displayedSecondStamina)
        {
            displayedSecondStamina = secondStamina;
            secondConditionIcon?.SetStamina(secondStamina);
            if (secondStaminaFill != null)
                secondStaminaFill.fillAmount = secondStamina / 100f;
            if (secondStaminaText != null)
                secondStaminaText.SetText("루미  {0} / 100", secondStamina);
        }
    }



    private static string ConditionLabel(MushDogCondition condition)
    {
        return condition switch
        {
            MushDogCondition.Bad => "나쁨",
            MushDogCondition.Good => "좋음",
            _ => "평범",
        };
    }
}
