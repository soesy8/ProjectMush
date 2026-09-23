using System.Collections.Generic;
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
    private Image secondStaminaFill;
    private TMP_Text secondStaminaText;
    private MushDogConditionIcon secondConditionIcon;
    private TMP_Text secondConditionText;
    private int displayedFirstStamina = -1;
    private int displayedSecondStamina = -1;
    private MushDogCondition? displayedFirstCondition;
    private MushDogCondition? displayedSecondCondition;
    private bool vrCanvasConfigured;
    private bool individualUiReady;
    private static Sprite roundedBarSprite;

    private void OnEnable()
    {
        displayedFirstStamina = -1;
        displayedSecondStamina = -1;
        displayedFirstCondition = null;
        displayedSecondCondition = null;
        EnsureIndividualDogUi();
        TryConfigureVrCanvas();
        Refresh();
    }

    private void LateUpdate()
    {
        if (!vrCanvasConfigured)
            TryConfigureVrCanvas();
        Refresh();
    }

    private void EnsureIndividualDogUi()
    {
        if (individualUiReady || staminaFill == null || staminaText == null)
            return;

        RectTransform root = transform as RectTransform;
        RectTransform firstBackground = staminaFill.transform.parent as RectTransform;
        RectTransform firstText = staminaText.rectTransform;
        if (root == null || firstBackground == null)
            return;

        root.sizeDelta = new Vector2(Mathf.Max(root.sizeDelta.x, 1000f), 170f);
        root.anchoredPosition = new Vector2(root.anchoredPosition.x, -104f);
        firstBackground.anchoredPosition = new Vector2(-35f, 28f);
        firstBackground.sizeDelta = new Vector2(760f, 54f);
        if (staminaFill.transform is RectTransform firstFillRect)
            firstFillRect.sizeDelta = firstBackground.sizeDelta;
        firstText.anchoredPosition = new Vector2(-35f, 28f);
        firstText.sizeDelta = new Vector2(730f, 40f);
        staminaText.fontSize = 24f;

        Transform existingBackground = transform.Find("Second Dog Stamina Background");
        GameObject secondBackgroundObject;
        if (existingBackground != null)
        {
            secondBackgroundObject = existingBackground.gameObject;
        }
        else
        {
            secondBackgroundObject = Instantiate(firstBackground.gameObject, transform, false);
            secondBackgroundObject.name = "Second Dog Stamina Background";
        }

        RectTransform secondBackground = secondBackgroundObject.GetComponent<RectTransform>();
        secondBackground.anchoredPosition = new Vector2(-35f, -38f);
        secondBackground.sizeDelta = new Vector2(760f, 54f);
        Transform secondFillTransform = secondBackground.Find(staminaFill.name);
        secondStaminaFill = secondFillTransform != null ? secondFillTransform.GetComponent<Image>() : null;
        if (secondFillTransform is RectTransform secondFillRect)
            secondFillRect.sizeDelta = secondBackground.sizeDelta;

        Transform existingText = transform.Find("Second Dog Stamina Text");
        if (existingText != null)
        {
            secondStaminaText = existingText.GetComponent<TMP_Text>();
        }
        else
        {
            GameObject secondTextObject = Instantiate(staminaText.gameObject, transform, false);
            secondTextObject.name = "Second Dog Stamina Text";
            secondStaminaText = secondTextObject.GetComponent<TMP_Text>();
        }
        if (secondStaminaText != null)
        {
            RectTransform secondText = secondStaminaText.rectTransform;
            secondText.anchoredPosition = new Vector2(-35f, -38f);
            secondText.sizeDelta = new Vector2(730f, 40f);
            secondStaminaText.fontSize = 24f;
            secondStaminaText.raycastTarget = false;
        }

        if (conditionIcon != null)
        {
            conditionIcon.rectTransform.anchoredPosition = new Vector2(380f, 28f);
            conditionIcon.rectTransform.sizeDelta = new Vector2(50f, 50f);
            Transform existingSecondIcon = transform.Find("Second Dog Condition Icon");
            if (existingSecondIcon != null)
            {
                secondConditionIcon = existingSecondIcon.GetComponent<MushDogConditionIcon>();
            }
            else
            {
                GameObject secondIconObject = Instantiate(conditionIcon.gameObject, transform, false);
                secondIconObject.name = "Second Dog Condition Icon";
                secondConditionIcon = secondIconObject.GetComponent<MushDogConditionIcon>();
            }
            if (secondConditionIcon != null)
            {
                secondConditionIcon.rectTransform.anchoredPosition = new Vector2(380f, -38f);
                secondConditionIcon.rectTransform.sizeDelta = new Vector2(50f, 50f);
            }
        }
        if (conditionText != null)
        {
            conditionText.rectTransform.anchoredPosition = new Vector2(445f, 28f);
            conditionText.rectTransform.sizeDelta = new Vector2(100f, 40f);
            conditionText.fontSize = 22f;
            Transform existingSecondConditionText = transform.Find("Second Dog Condition Text");
            if (existingSecondConditionText != null)
            {
                secondConditionText = existingSecondConditionText.GetComponent<TMP_Text>();
            }
            else
            {
                GameObject secondConditionTextObject = Instantiate(conditionText.gameObject, transform, false);
                secondConditionTextObject.name = "Second Dog Condition Text";
                secondConditionText = secondConditionTextObject.GetComponent<TMP_Text>();
            }
            if (secondConditionText != null)
            {
                secondConditionText.rectTransform.anchoredPosition = new Vector2(445f, -38f);
                secondConditionText.rectTransform.sizeDelta = new Vector2(100f, 40f);
                secondConditionText.fontSize = 22f;
                secondConditionText.raycastTarget = false;
            }
        }
        ConfigureBarVisuals(firstBackground, staminaFill);
        ConfigureBarVisuals(secondBackground, secondStaminaFill);
        staminaText.color = Color.white;
        if (secondStaminaText != null) secondStaminaText.color = Color.white;
        individualUiReady = secondStaminaFill != null && secondStaminaText != null &&
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
        canvasRect.SetParent(vrCamera.transform, false);
        canvasRect.sizeDelta = new Vector2(1920f, 1080f);
        canvasRect.localScale = Vector3.one * VrCanvasScale;
        canvasRect.SetLocalPositionAndRotation(Vector3.forward * VrCanvasDistance, Quaternion.identity);
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
            EnsureIndividualDogUi();

        MushDogCondition firstCondition = MushGameSave.GetDogCondition(0);
        if (firstCondition != displayedFirstCondition)
        {
            displayedFirstCondition = firstCondition;
            if (conditionIcon != null) conditionIcon.SetCondition(firstCondition);
            if (conditionText != null)
                conditionText.text = ConditionLabel(firstCondition);
        }
        MushDogCondition secondCondition = MushGameSave.GetDogCondition(1);
        if (secondCondition != displayedSecondCondition)
        {
            displayedSecondCondition = secondCondition;
            if (secondConditionIcon != null) secondConditionIcon.SetCondition(secondCondition);
            if (secondConditionText != null)
                secondConditionText.text = ConditionLabel(secondCondition);
        }
        int firstStamina = Mathf.Clamp(Mathf.FloorToInt(MushGameSave.GetDogStamina(0)), 0, 100);
        if (firstStamina != displayedFirstStamina)
        {
            displayedFirstStamina = firstStamina;
            if (staminaFill != null)
                SetBarFill(staminaFill, firstStamina / 100f);
            if (staminaText != null)
                staminaText.SetText("허스키  {0} / 100", firstStamina);
        }

        int secondStamina = Mathf.Clamp(Mathf.FloorToInt(MushGameSave.GetDogStamina(1)), 0, 100);
        if (secondStamina != displayedSecondStamina)
        {
            displayedSecondStamina = secondStamina;
            if (secondStaminaFill != null)
                SetBarFill(secondStaminaFill, secondStamina / 100f);
            if (secondStaminaText != null)
                secondStaminaText.SetText("말라뮤트  {0} / 100", secondStamina);
        }
    }

    private static void ConfigureBarVisuals(RectTransform background, Image fill)
    {
        if (fill == null || !background.TryGetComponent(out Image frame)) return;
        if (roundedBarSprite == null)
        {
            const int size = 32;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - 16f) - 8f, 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - 16f) - 8f, 0f);
                byte alpha = (byte)(255f * Mathf.Clamp01(8.5f - Mathf.Sqrt(dx * dx + dy * dy)));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            { name = "Stamina Rounded Bar", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            roundedBarSprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f,
                100f, 0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
        }
        frame.sprite = roundedBarSprite;
        frame.type = Image.Type.Sliced;
        frame.color = new Color(0.81f, 0.65f, 0.44f, 1f);
        frame.preserveAspect = false;
        frame.raycastTarget = false;
        Transform existing = background.Find("Stamina Track");
        Image track = existing != null ? existing.GetComponent<Image>() :
            new GameObject("Stamina Track", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        track.transform.SetParent(background, false);
        track.transform.SetAsFirstSibling();
        track.rectTransform.anchorMin = Vector2.zero;
        track.rectTransform.anchorMax = Vector2.one;
        track.rectTransform.offsetMin = Vector2.one * 4f;
        track.rectTransform.offsetMax = Vector2.one * -4f;
        track.sprite = roundedBarSprite;
        track.type = Image.Type.Sliced;
        track.color = new Color(0.13f, 0.075f, 0.035f, 1f);
        track.raycastTarget = false;
        fill.sprite = roundedBarSprite;
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.52f, 0.32f, 0.12f, 1f);
        fill.preserveAspect = false;
        fill.raycastTarget = false;
    }

    private static void SetBarFill(Image fill, float value)
    {
        RectTransform rect = fill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.up;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(4f, 0f);
        float width = Mathf.Max(0f, ((RectTransform)rect.parent).rect.width - 8f) * Mathf.Clamp01(value);
        rect.sizeDelta = new Vector2(width, -8f);
        fill.enabled = width > 0f;
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
