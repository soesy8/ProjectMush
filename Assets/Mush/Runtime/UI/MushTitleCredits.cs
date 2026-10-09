using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Rolls the authored title credits through the right-hand viewport.</summary>
[DisallowMultipleComponent]
public sealed class MushTitleCredits : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Button creditButton;
    [SerializeField] private UnityEngine.UI.Button skipButton;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private TMP_Text content;
    [SerializeField, Min(1f)] private float scrollSpeed = 65f;
    private bool scrolling;

    private void Awake()
    {
        if (content != null && !content.text.Contains("Inventory Sound Effects"))
            content.text += "\n\n<size=34>SFX / SOUND EFFECTS</size>\n\n" +
                "<size=28>Leather / Inventory Sound Effects\nartisticdude\n(submitted by Ogrebane)</size>\n" +
                "<size=20>Source: https://opengameart.org/content/inventory-sound-effects\n" +
                "License: Creative Commons Attribution 3.0 Unported (CC BY 3.0)\n" +
                "https://creativecommons.org/licenses/by/3.0/\n" +
                "Audio modifications: None. Original WAV used without audio editing.</size>";
        if (creditButton != null) creditButton.onClick.AddListener(PlayCredits);
        if (skipButton != null) skipButton.onClick.AddListener(SkipCredits);
        StopCredits();
    }

    public void PlayCredits()
    {
        if (scrolling || viewport == null || content == null) return;
        scrolling = true;
        if (creditButton != null) creditButton.interactable = false;
        viewport.gameObject.SetActive(true);
        if (skipButton != null)
        {
            skipButton.gameObject.SetActive(true);
            if (EventSystem.current != null && creditButton != null &&
                EventSystem.current.currentSelectedGameObject == creditButton.gameObject)
                skipButton.Select();
        }
        Canvas.ForceUpdateCanvases();
        RectTransform textRect = content.rectTransform;
        float preferredHeight = content.GetPreferredValues(content.text, textRect.rect.width, Mathf.Infinity).y;
        textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight + 32f);
        textRect.anchoredPosition = Vector2.zero;
    }

    public void SkipCredits() => StopCredits();

    private void Update()
    {
        if (!scrolling || viewport == null || content == null) return;
        RectTransform textRect = content.rectTransform;
        textRect.anchoredPosition += Vector2.up * (scrollSpeed * Time.unscaledDeltaTime);
        if (textRect.anchoredPosition.y < viewport.rect.height + textRect.rect.height) return;
        StopCredits();
    }

    private void StopCredits()
    {
        scrolling = false;
        if (creditButton != null) creditButton.interactable = true;
        if (viewport != null) viewport.gameObject.SetActive(false);
        if (skipButton != null)
        {
            bool skipSelected = EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == skipButton.gameObject;
            skipButton.gameObject.SetActive(false);
            if (skipSelected && creditButton != null && creditButton.gameObject.activeInHierarchy)
                creditButton.Select();
        }
    }

    private void OnDisable() => StopCredits();

    private void OnDestroy()
    {
        if (creditButton != null) creditButton.onClick.RemoveListener(PlayCredits);
        if (skipButton != null) skipButton.onClick.RemoveListener(SkipCredits);
    }
}
