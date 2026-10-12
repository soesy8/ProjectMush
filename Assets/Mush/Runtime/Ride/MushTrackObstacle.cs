using UnityEngine;

namespace Mush.Prototype
{
    /// <summary>Query-only primitive used by track obstacle avoidance. No Rigidbody or per-frame callback.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public class MushTrackObstacle : MonoBehaviour
    {
        [SerializeField] private CapsuleCollider body;

        public CapsuleCollider Body => body != null ? body : body = GetComponent<CapsuleCollider>();
        public Vector3 Center => transform.TransformPoint(Body.center);
        public float Radius => Body.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));

        public static void InstallForScene(MushMapRideBootstrap ride)
        {
            foreach (GameObject root in ride.gameObject.scene.GetRootGameObjects())
                foreach (Transform prop in root.GetComponentsInChildren<Transform>(true))
                {
                    string name = prop.name;
                    bool tree = name.StartsWith("Tree", System.StringComparison.OrdinalIgnoreCase) ||
                                name == "Visible Snow Pine";
                    bool rock = name.StartsWith("Rock", System.StringComparison.OrdinalIgnoreCase) ||
                                name == "Snow Rock";
                    if ((!tree && !rock) || prop.GetComponentInParent<MushTrackObstacle>() != null ||
                        prop.GetComponentInChildren<MushTrackObstacle>(true) != null)
                        continue;

                    Bounds bounds = default;
                    bool foundMesh = false;
                    foreach (MeshFilter filter in prop.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null) continue;
                        Bounds meshBounds = filter.sharedMesh.bounds;
                        Matrix4x4 toProp = prop.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                        for (int corner = 0; corner < 8; corner++)
                        {
                            Vector3 point = toProp.MultiplyPoint3x4(meshBounds.center +
                                Vector3.Scale(meshBounds.extents, new Vector3(
                                    (corner & 1) == 0 ? -1f : 1f,
                                    (corner & 2) == 0 ? -1f : 1f,
                                    (corner & 4) == 0 ? -1f : 1f)));
                            if (!foundMesh) { bounds = new Bounds(point, Vector3.zero); foundMesh = true; }
                            else bounds.Encapsulate(point);
                        }
                    }
                    if (!foundMesh) continue;
                    float radius = Mathf.Max(0.15f,
                        Mathf.Max(bounds.extents.x, bounds.extents.z) * (tree ? 0.18f : 0.9f));
                    float height = tree ? Mathf.Clamp(bounds.size.y, 1.8f, 3.5f) :
                        Mathf.Max(bounds.size.y, radius * 2f);
                    // Keep the prop's original layers and colliders; only the query child uses layer 2.
                    GameObject query = new("Mush Track Obstacle Query");
                    query.transform.SetParent(prop, false);
                    query.AddComponent<MushTrackObstacle>().Configure(
                        new Vector3(bounds.center.x, bounds.min.y + height * 0.5f, bounds.center.z),
                        radius, height);
                }
        }

        public void Configure(Vector3 localCenter, float localRadius, float localHeight)
        {
            body = GetComponent<CapsuleCollider>();
            body.direction = 1;
            body.center = localCenter;
            body.radius = Mathf.Max(0.05f, localRadius);
            body.height = Mathf.Max(body.radius * 2f, localHeight);
            body.isTrigger = true;
            // Explicit query mask only; no project layer or collision-matrix edits.
            gameObject.layer = 2;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (Body == null) return;
            Gizmos.color = new Color(1f, 0.65f, 0.15f, 0.8f);
            Gizmos.DrawWireSphere(Center, Radius);
        }
    }
}
