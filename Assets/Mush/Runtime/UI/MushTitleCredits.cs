using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Rolls the authored title credits through the right-hand viewport.</summary>
[DisallowMultipleComponent]
public sealed class MushTitleCredits : MonoBehaviour
{
    [SerializeField] private Button creditButton;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private TMP_Text content;
    [SerializeField, Min(1f)] private float scrollSpeed = 52f;
    private bool scrolling;

    private void Awake()
    {
        if (creditButton != null) creditButton.onClick.AddListener(PlayCredits);
        if (viewport != null) viewport.gameObject.SetActive(false);
    }

    public void PlayCredits()
    {
        if (viewport == null || content == null) return;
        viewport.gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        RectTransform textRect = content.rectTransform;
        float preferredHeight = content.GetPreferredValues(content.text, textRect.rect.width, Mathf.Infinity).y;
        textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight + 32f);
        textRect.anchoredPosition = Vector2.zero;
        scrolling = true;
    }

    private void Update()
    {
        if (!scrolling || viewport == null || content == null) return;
        RectTransform textRect = content.rectTransform;
        textRect.anchoredPosition += Vector2.up * (scrollSpeed * Time.unscaledDeltaTime);
        if (textRect.anchoredPosition.y < viewport.rect.height + textRect.rect.height) return;
        scrolling = false;
        viewport.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        scrolling = false;
        if (viewport != null) viewport.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (creditButton != null) creditButton.onClick.RemoveListener(PlayCredits);
    }
}
