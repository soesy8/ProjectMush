using UnityEngine;
using UnityEngine.UI;

/// <summary>A scene-authored face graphic that stays visible in edit mode.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MushDogConditionIcon : MaskableGraphic
{
    [SerializeField] private MushDogCondition condition;

    public void SetCondition(MushDogCondition value)
    {
        if (condition == value) return;
        condition = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.48f;
        Color ink = new Color(0.22f, 0.10f, 0.04f) * color;
        Color face = (condition switch
        {
            MushDogCondition.Bad => new Color(0.95f, 0.40f, 0.32f),
            MushDogCondition.Good => new Color(0.55f, 0.84f, 0.42f),
            _ => new Color(0.94f, 0.76f, 0.45f),
        }) * color;
        Disc(mesh, center, radius, ink);
        Disc(mesh, center, radius * 0.90f, face);
        Disc(mesh, center + new Vector2(-0.32f, 0.23f) * radius, radius * 0.09f, ink);
        Disc(mesh, center + new Vector2(0.32f, 0.23f) * radius, radius * 0.09f, ink);

        Vector2 previous = MouthPoint(-1f);
        for (int i = 1; i <= 12; i++)
        {
            Vector2 next = MouthPoint(i / 6f - 1f);
            Stroke(mesh, center + previous * radius, center + next * radius, radius * 0.085f, ink);
            previous = next;
        }
    }

    private Vector2 MouthPoint(float t)
    {
        float y = condition switch
        {
            MushDogCondition.Good => -0.39f + 0.25f * t * t,
            MushDogCondition.Bad => -0.17f - 0.25f * t * t,
            _ => -0.27f,
        };
        return new Vector2(t * 0.38f, y);
    }

    private static void Disc(VertexHelper mesh, Vector2 center, float radius, Color tint)
    {
        const int segments = 40;
        int start = mesh.currentVertCount;
        mesh.AddVert(center, tint, Vector2.zero);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * (Mathf.PI * 2f / segments);
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
        }
        for (int i = 0; i < segments; i++)
            mesh.AddTriangle(start, start + 1 + (i + 1) % segments, start + 1 + i);
    }

    private static void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color tint)
    {
        Vector2 direction = (to - from).normalized;
        Vector2 side = new Vector2(-direction.y, direction.x) * (width * 0.5f);
        int start = mesh.currentVertCount;
        mesh.AddVert(from + side, tint, Vector2.zero);
        mesh.AddVert(to + side, tint, Vector2.zero);
        mesh.AddVert(to - side, tint, Vector2.zero);
        mesh.AddVert(from - side, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
