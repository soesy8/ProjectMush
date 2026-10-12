using System.Collections.Generic;
using Mush.Quest;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

[DisallowMultipleComponent]
public sealed class MushLobbyStaminaHud : MonoBehaviour
{
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
    private CanvasGroup vrHudVisibility;
    private float originalHudAlpha;
    private bool originalHudRaycasts;
    private bool vrVisibilityApplied;

    private void OnEnable()
    {
        displayedFirstStamina = -1;
        displayedSecondStamina = -1;
        displayedFirstCondition = null;
        displayedSecondCondition = null;
        BindAuthoredDogUi();
        TryConfigureVrCanvas();
        Refresh();
        RefreshVrHudVisibility();
    }

    private void RefreshVrHudVisibility()
    {
        if (IsXrDisplayRunning())
        {
            if (vrHudVisibility == null)
            {
                vrHudVisibility = GetComponent<CanvasGroup>();
                if (vrHudVisibility == null) vrHudVisibility = gameObject.AddComponent<CanvasGroup>();
            }
            if (!vrVisibilityApplied)
            {
                originalHudAlpha = vrHudVisibility.alpha;
                originalHudRaycasts = vrHudVisibility.blocksRaycasts;
                vrVisibilityApplied = true;
            }
            vrHudVisibility.alpha = Mush.Lobby.MushLobbyMapPanel.IsOpen ||
                MushSceneUI.ModalOpen ||
                (Mush.Lobby.MushLobbyPauseMenu.Active != null && Mush.Lobby.MushLobbyPauseMenu.Active.IsOpen) ? 0f : 1f;
            vrHudVisibility.blocksRaycasts = false;
        }
        else if (vrHudVisibility != null && vrVisibilityApplied)
        {
            vrHudVisibility.alpha = originalHudAlpha;
            vrHudVisibility.blocksRaycasts = originalHudRaycasts;
            vrVisibilityApplied = false;
        }
    }

    private void LateUpdate()
    {
        if (!vrCanvasConfigured)
            TryConfigureVrCanvas();
        Refresh();
        RefreshVrHudVisibility();
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

        MushVrUiLayout.PlaceLobbyScreen(canvas, vrCamera);
        canvas.sortingOrder = 250;
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

        // Use the same saved condition as the sled's buff/default/penalty selection.
        int firstStamina = Mathf.Clamp(Mathf.FloorToInt(MushGameSave.GetDogStamina(0)), 0, 100);
        MushDogCondition firstCondition = MushGameSave.GetDogCondition(0);
        if (firstCondition != displayedFirstCondition)
        {
            displayedFirstCondition = firstCondition;
            conditionIcon?.SetCondition(firstCondition);
            if (conditionText != null)
                conditionText.text = ConditionLabel(firstCondition);
        }
        int secondStamina = Mathf.Clamp(Mathf.FloorToInt(MushGameSave.GetDogStamina(1)), 0, 100);
        MushDogCondition secondCondition = MushGameSave.GetDogCondition(1);
        if (secondCondition != displayedSecondCondition)
        {
            displayedSecondCondition = secondCondition;
            secondConditionIcon?.SetCondition(secondCondition);
            if (secondConditionText != null)
                secondConditionText.text = ConditionLabel(secondCondition);
        }
        if (firstStamina != displayedFirstStamina)
        {
            displayedFirstStamina = firstStamina;
            if (staminaFill != null)
                staminaFill.fillAmount = firstStamina / 100f;
            if (staminaText != null)
                staminaText.SetText("카이  {0} / 100", firstStamina);
        }

        if (secondStamina != displayedSecondStamina)
        {
            displayedSecondStamina = secondStamina;
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
            _ => "보통",
        };
    }
}
