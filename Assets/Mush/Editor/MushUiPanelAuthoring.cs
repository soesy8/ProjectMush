using Mush.Customization;
using UnityEngine;
using UnityEngine.UI;

namespace Mush.UI
{
    /// <summary>Builds decorative panels in Edit Mode; runtime code instantiates authored artwork.</summary>
    public static class MushUiPanelAuthoring
    {
        private const string SkinObjectName = "Mush UI Panel Skin";
        private const float SourceWidth = 200f;
        private const float SourceHeight = 100f;
        private static Font ThemeFont => MushUiPanelSkin.ThemeFont;

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
            {
                return existing.gameObject;
            }

            // Mesh panel artwork is authored before entering Play Mode.
            if (Application.isPlaying) return null;

            MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
            if (catalog == null || catalog.uiPanelPrefab == null)
                return null;

            Vector2 panelSize = FindAndHideLegacyBacking(panelRoot, fallbackSize);
            GameObject skin = Object.Instantiate(catalog.uiPanelPrefab, panelRoot, false);
            skin.name = SkinObjectName;

            RectTransform rect = skin.GetComponent<RectTransform>();
            if (rect == null)
            {
                DestroyForCurrentMode(skin);
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

        private static void DestroyForCurrentMode(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
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
