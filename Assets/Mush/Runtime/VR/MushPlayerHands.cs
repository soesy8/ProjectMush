using UnityEngine;

namespace Mush.Quest
{
    public static class MushPlayerHands
    {
        public static Transform Create(Transform parent, bool left, string objectName)
        {
            GameObject prefab = Resources.Load<GameObject>(left ? "MushLeftHand" : "MushRightHand");
            if (prefab == null || parent == null)
                return null;
            GameObject hand = Object.Instantiate(prefab, parent, false);
            hand.name = objectName;
            hand.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            return hand.transform;
        }

        public static Transform FitRideHand(Transform hand)
        {
            if (hand == null)
                return null;
            hand.localScale = Vector3.one;
            hand.localPosition = Vector3.zero;
            Bounds bounds = new();
            bool found = false;
            foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is LineRenderer) continue;
                Bounds world = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = hand.InverseTransformPoint(new Vector3(
                        (corner & 1) == 0 ? world.min.x : world.max.x,
                        (corner & 2) == 0 ? world.min.y : world.max.y,
                        (corner & 4) == 0 ? world.min.z : world.max.z));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!found) return hand;
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = 0.23f / Mathf.Max(0.001f, size);
            hand.localScale = Vector3.one * scale;
            hand.localPosition = -bounds.center * scale;
            Transform grip = hand.Find("Rein Grip Point");
            if (grip == null)
            {
                grip = new GameObject("Rein Grip Point").transform;
                grip.SetParent(hand, false);
            }
            grip.localPosition = bounds.center + Vector3.forward * bounds.size.z * 0.15f;
            return grip;
        }

        public static void InstallLobby(Transform rig)
        {
            foreach (Transform anchor in rig.GetComponentsInChildren<Transform>(true))
            {
                bool left = anchor.name == "Left Controller";
                if (!left && anchor.name != "Right Controller")
                    continue;
                string handName = left ? "Lobby Left Hand" : "Lobby Right Hand";
                Transform oldHand = anchor.Find(handName);
                if (oldHand != null)
                {
                    oldHand.name += " Replaced";
                    oldHand.gameObject.SetActive(false);
                }
                foreach (Renderer visual in anchor.GetComponentsInChildren<Renderer>(true))
                    if (visual is not LineRenderer)
                        visual.enabled = false;
                Create(anchor, left, handName);
            }
        }
    }
}
