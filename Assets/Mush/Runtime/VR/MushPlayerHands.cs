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
