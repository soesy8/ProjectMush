using Mush.Customization;
using UnityEngine;

namespace Mush.UI
{
    /// <summary>Loads static UI artwork without changing its sprites, colors or decorative layout.</summary>
    public static class MushUiPanelSkin
    {
        private const float SourceWidth = 200f;
        private const float SourceHeight = 100f;
        public static Font ThemeFont => MushCustomizationCatalog.Load()?.koreanFont;

        public static UnityEngine.UI.Image CreateCanvasPanel(Transform parent, string objectName, Vector2 anchoredPosition,
            Vector2 size, bool preservePrefabLayout = false)
        {
            MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
            if (parent == null || catalog == null || catalog.uiPanelPrefab == null) return null;
            GameObject panel = Object.Instantiate(catalog.uiPanelPrefab, parent, false);
            panel.name = objectName;
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.anchoredPosition = anchoredPosition;
            rect.localRotation = Quaternion.identity;
            rect.localScale = preservePrefabLayout
                ? Vector3.one * Mathf.Min(size.x / SourceWidth, size.y / SourceHeight) : Vector3.one;
            if (!preservePrefabLayout) rect.sizeDelta = size;
            rect.SetAsFirstSibling();
            foreach (UnityEngine.UI.Graphic graphic in panel.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                graphic.raycastTarget = false;
            Transform templateText = FindDeepChild(panel.transform, "UI_Text");
            if (templateText != null) templateText.gameObject.SetActive(false);
            return FindDeepChild(panel.transform, "Image_Inside")?.GetComponent<UnityEngine.UI.Image>();
        }

        public static UnityEngine.UI.Image CreateImage(Transform parent, string objectName, Vector2 position, Vector2 size, Color palette)
        {
            string resource = "ArtOptUI/Images/" + ColorUtility.ToHtmlStringRGBA(palette);
            GameObject prefab = Resources.Load<GameObject>(resource);
            if (prefab == null)
            {
                Debug.LogError("[Mush] Missing authored UI image template: " + resource);
                return null;
            }
            GameObject instance = Object.Instantiate(prefab, parent, false);
            instance.name = objectName;
            RectTransform rect = instance.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return instance.GetComponent<UnityEngine.UI.Image>();
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                    return child;
            }
            return null;
        }
    }
}
