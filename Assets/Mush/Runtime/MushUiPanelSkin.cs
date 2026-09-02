using Mush.Customization;
using UnityEngine;
using UnityEngine.UI;

namespace Mush.UI
{
    /// <summary>
    /// Keeps the existing VR-friendly 3D buttons and colliders while replacing
    /// their flat backing meshes with the shared wood-and-leaf panel prefab.
    /// </summary>
    public static class MushUiPanelSkin
    {
        private const string SkinObjectName = "Mush UI Panel Skin";
        private const float SourceWidth = 200f;
        private const float SourceHeight = 100f;

        public static Font ThemeFont
        {
            get
            {
                MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
                return catalog != null ? catalog.koreanFont : null;
            }
        }

        public static void ApplyFont(TextMesh textMesh)
        {
            if (textMesh == null)
                return;

            Font font = ThemeFont;
            if (font == null)
                return;

            textMesh.font = font;
            if (textMesh.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = font.material;
        }

        public static GameObject ApplyPanel(Transform panelRoot, Vector2 fallbackSize, float depth = -0.04f)
        {
            if (panelRoot == null)
                return null;

            Transform existing = panelRoot.Find(SkinObjectName);
            if (existing != null)
                return existing.gameObject;

            MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
            if (catalog == null || catalog.uiPanelPrefab == null)
                return null;

            Vector2 panelSize = FindAndHideLegacyBacking(panelRoot, fallbackSize);
            GameObject skin = Object.Instantiate(catalog.uiPanelPrefab, panelRoot, false);
            skin.name = SkinObjectName;

            RectTransform rect = skin.GetComponent<RectTransform>();
            if (rect == null)
            {
                Object.Destroy(skin);
                return null;
            }

            rect.localPosition = new Vector3(0f, 0f, depth);
            rect.localRotation = Quaternion.identity;
            rect.localScale = new Vector3(panelSize.x / SourceWidth, panelSize.y / SourceHeight, 1f);
            rect.SetAsFirstSibling();

            Canvas canvas = skin.GetComponent<Canvas>();
            if (canvas == null)
                canvas = skin.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = -50;

            foreach (Graphic graphic in skin.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;

            Transform sampleText = FindDeepChild(skin.transform, "UI_Text");
            if (sampleText != null)
                sampleText.gameObject.SetActive(false);

            return skin;
        }

        public static Image CreateCanvasPanel(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
            if (parent == null || catalog == null || catalog.uiPanelPrefab == null)
                return null;

            GameObject panel = Object.Instantiate(catalog.uiPanelPrefab, parent, false);
            panel.name = objectName;
            RectTransform rect = panel.GetComponent<RectTransform>();
            if (rect == null)
            {
                Object.Destroy(panel);
                return null;
            }

            rect.anchorMin = Vector2.one * 0.5f;
            rect.anchorMax = Vector2.one * 0.5f;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.SetAsFirstSibling();

            RectTransform outside = FindDeepChild(panel.transform, "Image_Outside") as RectTransform;
            if (outside != null)
            {
                outside.anchorMin = Vector2.zero;
                outside.anchorMax = Vector2.one;
                outside.anchoredPosition = Vector2.zero;
                outside.sizeDelta = Vector2.zero;
            }

            RectTransform inside = FindDeepChild(panel.transform, "Image_Inside") as RectTransform;
            if (inside != null)
            {
                inside.anchorMin = Vector2.zero;
                inside.anchorMax = Vector2.one;
                inside.offsetMin = new Vector2(12f, 8f);
                inside.offsetMax = new Vector2(-12f, -8f);
            }

            ArrangeCornerLeaf(panel.transform, "Image_Leaf", new Vector2(-26f, -6f), size.y);
            ArrangeCornerLeaf(panel.transform, "Image_Leaf_2", new Vector2(-8f, -28f), size.y);

            foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;

            Transform sampleText = FindDeepChild(panel.transform, "UI_Text");
            if (sampleText != null)
                sampleText.gameObject.SetActive(false);

            return inside != null ? inside.GetComponent<Image>() : panel.GetComponentInChildren<Image>(true);
        }

        private static void ArrangeCornerLeaf(
            Transform panel,
            string leafName,
            Vector2 anchoredPosition,
            float panelHeight)
        {
            RectTransform leaf = FindDeepChild(panel, leafName) as RectTransform;
            if (leaf == null)
                return;

            leaf.anchorMin = Vector2.one;
            leaf.anchorMax = Vector2.one;
            leaf.anchoredPosition = anchoredPosition;
            float leafSize = Mathf.Min(70f, Mathf.Max(36f, panelHeight * 0.54f));
            leaf.sizeDelta = Vector2.one * leafSize;
        }

        private static Vector2 FindAndHideLegacyBacking(Transform panelRoot, Vector2 fallbackSize)
        {
            Vector2 size = fallbackSize;
            float largestArea = 0f;

            for (int index = 0; index < panelRoot.childCount; index++)
            {
                Transform child = panelRoot.GetChild(index);
                if (!child.TryGetComponent(out Renderer renderer))
                    continue;

                string childName = child.name;
                bool isBacking = childName.EndsWith("Back", System.StringComparison.OrdinalIgnoreCase);
                bool isOldDecoration = childName.Contains("Header", System.StringComparison.OrdinalIgnoreCase) ||
                                       childName.Contains("Trim", System.StringComparison.OrdinalIgnoreCase);
                if (!isBacking && !isOldDecoration)
                    continue;

                renderer.enabled = false;
                if (!isBacking)
                    continue;

                Vector3 scale = child.localScale;
                float area = Mathf.Abs(scale.x * scale.y);
                if (area <= largestArea)
                    continue;

                largestArea = area;
                size = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }

            size.x = Mathf.Max(0.01f, size.x * 1.04f);
            size.y = Mathf.Max(0.01f, size.y * 1.08f);
            return size;
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
