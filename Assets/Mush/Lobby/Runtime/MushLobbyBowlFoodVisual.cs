using UnityEngine;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MushLobbyBowlFoodVisual : MonoBehaviour
    {
        private const float BowlFloorHeight = 0.04112f;
        private static readonly int BowlWorldToLocal = Shader.PropertyToID("_BowlWorldToLocal");

        private Mesh foodMesh;
        private MeshRenderer foodRenderer;
        private Vector3 fullPosition;
        private Vector3 emptyPosition;
        private float displayedFill = float.NaN;

        public void Configure(Mesh source, Material material, Vector3 fullLocalPosition)
        {
            if (source == null || material == null)
                return;

            foodMesh = source;
            GetComponent<MeshFilter>().sharedMesh = source;
            foodRenderer = GetComponent<MeshRenderer>();
            foodRenderer.sharedMaterial = material;
            fullPosition = fullLocalPosition;
            emptyPosition = fullPosition + Vector3.up * (BowlFloorHeight - source.bounds.max.y - 0.001f);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            // The food moves within this fixed bowl space. Clip against the
            // inner bowl surface so submerged food and its shadows stay hidden.
            var properties = new MaterialPropertyBlock();
            properties.SetMatrix(BowlWorldToLocal,
                Matrix4x4.Translate(-fullPosition) * transform.parent.worldToLocalMatrix);
            foodRenderer.SetPropertyBlock(properties);
            displayedFill = float.NaN;
            SetFill(0f);
        }

        public void SetFill(float fill)
        {
            if (foodMesh == null || !float.IsFinite(fill))
                return;
            fill = Mathf.Clamp01(fill);
            if (fill == displayedFill)
                return;
            displayedFill = fill;
            foodRenderer.enabled = fill > 0f;
            transform.localPosition = Vector3.Lerp(emptyPosition, fullPosition, fill);
        }
    }
}
