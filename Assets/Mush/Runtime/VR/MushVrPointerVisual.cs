using UnityEngine;

namespace Mush.Quest
{
    public static class MushVrPointerVisual
    {
        private static Material material;

        public static void Update(LineRenderer ray, Vector3 start, Vector3 end, bool visible, bool hit)
        {
            if (ray == null) return;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("MushVrPointer");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                material = new Material(shader) { name = "VR Pointer Overlay" };
            }
            ray.gameObject.layer = 5;
            ray.sharedMaterial = material;
            ray.enabled = visible;
            ray.startColor = ray.endColor = hit ? Color.cyan : Color.white;
            ray.SetPosition(0, start);
            ray.SetPosition(1, end);
            Transform existing = ray.transform.Find("VR Pointer Tip");
            LineRenderer tip;
            if (existing == null)
            {
                GameObject tipObject = new("VR Pointer Tip");
                tipObject.layer = 5;
                tipObject.transform.SetParent(ray.transform, false);
                tip = tipObject.AddComponent<LineRenderer>();
                tip.useWorldSpace = true;
                tip.loop = true;
                tip.positionCount = 24;
                tip.startWidth = tip.endWidth = 0.004f;
                tip.sharedMaterial = material;
            }
            else tip = existing.GetComponent<LineRenderer>();
            tip.enabled = visible;
            if (!visible) return;
            Camera camera = Camera.main;
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            Vector3 up = camera != null ? camera.transform.up : Vector3.up;
            float radius = hit ? 0.012f : 0.008f;
            tip.startColor = tip.endColor = hit ? Color.cyan : Color.white;
            for (int index = 0; index < tip.positionCount; index++)
            {
                float angle = index * Mathf.PI * 2f / tip.positionCount;
                tip.SetPosition(index, end + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
            }
        }

        public static void Hide(LineRenderer ray)
        {
            if (ray == null) return;
            ray.enabled = false;
            LineRenderer tip = ray.transform.Find("VR Pointer Tip")?.GetComponent<LineRenderer>();
            if (tip != null) tip.enabled = false;
        }
    }
}
