using UnityEngine;
using UnityEngine.UI;

namespace Mush.Lobby
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MushMapStarGraphic : Image
    {
        private static Sprite starSprite;

        public void SetEarned(bool value)
        {
            EnsureSprite();
            color = value ? new Color(1f, 0.76f, 0.20f, 1f) : new Color(0.035f, 0.025f, 0.02f, 1f);
            SetAllDirty();
        }

        protected override void OnEnable()
        {
            EnsureSprite();
            base.OnEnable();
        }

        private void EnsureSprite()
        {
            if (starSprite == null)
                starSprite = CreateStarSprite();
            sprite = starSprite;
            type = Type.Simple;
            useSpriteMesh = false;
            preserveAspect = true;
            raycastTarget = false;
        }

        private static Sprite CreateStarSprite()
        {
            const int size = 128;
            Vector2[] outline = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float angle = (90f - i * 36f) * Mathf.Deg2Rad;
                outline[i] = Vector2.one * 0.5f +
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 2 == 0 ? 0.48f : 0.22f);
            }

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int coverage = 0;
                for (int sampleY = 0; sampleY < 2; sampleY++)
                for (int sampleX = 0; sampleX < 2; sampleX++)
                {
                    Vector2 point = new((x + 0.25f + sampleX * 0.5f) / size,
                        (y + 0.25f + sampleY * 0.5f) / size);
                    if (Contains(outline, point)) coverage++;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * 255 / 4));
            }

            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "Map Star Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite result = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                Vector2.one * 0.5f, size, 0, SpriteMeshType.FullRect);
            result.name = "Map Star";
            return result;
        }

        private static bool Contains(Vector2[] outline, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[j];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
