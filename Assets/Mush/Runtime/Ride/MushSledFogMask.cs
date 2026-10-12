using UnityEngine;

/// <summary>Marks only sled meshes for the snow-fog stencil pass, including equipped replacements.</summary>
public static class MushSledFogMask
{
    public const string LayerName = "MushSledFogMask";
    // Reserve this single stencil bit for the two Mush shaders; other bits are preserved.
    public const int StencilBit = 64;

    public static int ApplyToSled(Transform sled)
    {
        if (sled == null) return 0;
        int layer = LayerMask.NameToLayer(LayerName);
        if (layer < 0)
        {
            Debug.LogError($"[Mush] Missing '{LayerName}' layer. Run Mush/Rendering/Install Sled Fog Mask.");
            return 0;
        }

        int count = 0;
        foreach (Renderer renderer in sled.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
            renderer.gameObject.layer = layer;
            count++;
        }
        return count;
    }
}
