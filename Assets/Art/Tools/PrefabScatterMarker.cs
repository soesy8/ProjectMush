using UnityEngine;

namespace Mush.EditorTools.PrefabScatter
{
    /// <summary>
    /// Identifies GameObjects created by PrefabScatterTool.
    /// Keep this file outside an Editor folder so the marker can be attached to scene objects.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class PrefabScatterMarker : MonoBehaviour
    {
        [HideInInspector]
        public GameObject sourcePrefab;
    }
}
