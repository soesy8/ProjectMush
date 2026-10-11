using System.Reflection;
using UnityEngine;

namespace Mush.Testing
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("Mush Tests/Title VR Desktop Preview")]
    public sealed class TitleVrDesktopPreview : DesktopVrUiPreview
    {
        // Read the same serialized settings that the real Title XR path uses.
        private static readonly FieldInfo ScaleField = typeof(MushCanvasQuestInput).GetField(
            "vrTitleScale", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HeightField = typeof(MushCanvasQuestInput).GetField(
            "vrTitleHeightOffset", BindingFlags.Instance | BindingFlags.NonPublic);

        protected override bool SupportsScene => gameObject.scene.name == "Title";
        protected override float CanvasScale => (float)ScaleField.GetValue(questInput);
        protected override float CanvasHeightOffset => (float)HeightField.GetValue(questInput);
    }
}
