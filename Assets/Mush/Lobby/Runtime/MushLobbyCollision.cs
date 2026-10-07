using UnityEngine;

namespace Mush.Lobby
{
    /// <summary>Adds solid collisions to the visible static lobby meshes.</summary>
    public static class MushLobbyCollision
    {
        public static void Install(Transform lobbyRoot)
        {
            if (lobbyRoot == null || lobbyRoot.gameObject.scene.name != "PM_Lobby") return;
            foreach (MeshFilter filter in lobbyRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null ||
                    filter.GetComponentInParent<Canvas>() != null ||
                    filter.GetComponentInParent<Rigidbody>() != null ||
                    filter.GetComponentInParent<MushLobbyDogRoamer>() != null ||
                    filter.GetComponentInParent<MushLobbyFeedDispenser>() != null)
                    continue;
                bool solid = false;
                foreach (Collider collider in filter.GetComponents<Collider>())
                    if (collider.enabled && !collider.isTrigger) { solid = true; break; }
                if (solid) continue;
                MeshCollider blocker = filter.gameObject.AddComponent<MeshCollider>();
                blocker.sharedMesh = filter.sharedMesh;
                blocker.convex = false;
                blocker.isTrigger = false;
            }
        }
    }
}
