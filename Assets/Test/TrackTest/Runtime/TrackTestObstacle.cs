using Mush.Prototype;
using UnityEngine;

namespace Mush.Testing.TrackTest
{
    /// <summary>Preserves existing test prefab references to the shared obstacle shape.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class TrackTestObstacle : MushTrackObstacle
    {
    }
}
