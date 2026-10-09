using UnityEngine;

namespace Mush.Testing.TrackTest
{
    /// <summary>Query-only primitive on a copied prop. No Rigidbody or per-frame callback.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class TrackTestObstacle : MonoBehaviour
    {
        [SerializeField] private CapsuleCollider body;

        public CapsuleCollider Body => body != null ? body : body = GetComponent<CapsuleCollider>();
        public Vector3 Center => transform.TransformPoint(Body.center);
        public float Radius => Body.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));

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

        private void OnDrawGizmosSelected()
        {
            if (Body == null) return;
            Gizmos.color = new Color(1f, 0.65f, 0.15f, 0.8f);
            Gizmos.DrawWireSphere(Center, Radius);
        }
    }
}
