using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mush.Prototype;
using UnityEngine;

namespace Mush.Testing
{
    /// <summary>
    /// Temporary adapter to the ride's private pose caches. Moving the existing
    /// controller root also moves its course/boundary/finish checks to the dogs.
    /// No production script or additional movement controller is installed.
    /// </summary>
    internal sealed class TrackDogControlPivot
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo BuiltField = typeof(MushMapRideBootstrap).GetField("built", PrivateInstance);
        private static readonly FieldInfo ControllerField = typeof(MushMapRideBootstrap).GetField("rideController", PrivateInstance);
        private static readonly FieldInfo DogsField = typeof(MushMapRideBootstrap).GetField("dogs", PrivateInstance);
        private static readonly FieldInfo LastPositionField = typeof(MushMapRideBootstrap).GetField("lastRidePosition", PrivateInstance);
        private static readonly MethodInfo RefreshCheckpoint = typeof(MushMapRideBootstrap).GetMethod("InitializeCourseRecoveryCheckpoint", PrivateInstance);
        private static readonly MethodInfo RefreshDogLines = typeof(MushRideDog).GetMethod("LateUpdate", PrivateInstance);

        private readonly MushMapRideBootstrap ride;
        private readonly Transform root;
        private readonly Transform seat;
        private readonly Vector3 originalSeatPosition;
        private readonly Quaternion originalSeatRotation;
        private readonly List<ChildPose> children = new List<ChildPose>();
        private readonly List<DogCache> dogs = new List<DogCache>();
        private readonly List<Action> refreshLines = new List<Action>();
        private bool applied;

        private sealed class ChildPose
        {
            public Transform transform;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public int sibling;
        }

        private sealed class DogCache
        {
            public object runtime;
            public FieldInfo restPosition;
            public Vector3 originalPosition;
        }

        private TrackDogControlPivot(MushMapRideBootstrap state, Transform motionRoot, Transform rider)
        {
            ride = state;
            root = motionRoot;
            seat = rider;
            originalSeatPosition = seat.localPosition;
            originalSeatRotation = seat.localRotation;
            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
                children.Add(new ChildPose { transform = child, position = child.localPosition,
                    rotation = child.localRotation, scale = child.localScale, sibling = index });
            }
            foreach (object dog in (IEnumerable)DogsField.GetValue(ride))
            {
                FieldInfo rest = dog.GetType().GetField("restHolderLocalPosition");
                FieldInfo holder = dog.GetType().GetField("holder");
                if (rest == null || holder == null || ((Transform)holder.GetValue(dog)).parent != root)
                    throw new InvalidOperationException("The temporary test expects dogs directly under the ride root.");
                dogs.Add(new DogCache { runtime = dog, restPosition = rest, originalPosition = (Vector3)rest.GetValue(dog) });
            }
            foreach (MushRideDog dog in root.GetComponentsInChildren<MushRideDog>(false))
                refreshLines.Add((Action)Delegate.CreateDelegate(typeof(Action), dog, RefreshDogLines));
        }

        public static bool TryCreate(MushMapRideBootstrap ride, MushSledKeyboardController controller,
            out TrackDogControlPivot pivot)
        {
            pivot = null;
            if (BuiltField == null || ControllerField == null || DogsField == null ||
                LastPositionField == null || RefreshCheckpoint == null || RefreshDogLines == null)
                throw new InvalidOperationException("The ride pose caches changed; update the temporary test adapter.");
            if (!(bool)BuiltField.GetValue(ride)) return false;
            if ((MushSledKeyboardController)ControllerField.GetValue(ride) != controller ||
                ride.RideViewAnchor == null || ride.RideViewAnchor.parent != controller.transform)
                throw new InvalidOperationException("Assign the Track_v2 ride and its existing controller.");
            pivot = new TrackDogControlPivot(ride, controller.transform, ride.RideViewAnchor);
            return true;
        }

        public Vector3 Apply(Vector3 dogOffset)
        {
            // Preserve every visible world position while relocating the authority.
            root.position = root.TransformPoint(dogOffset);
            applied = true;
            foreach (ChildPose child in children)
                child.transform.localPosition = child.position - dogOffset;
            foreach (DogCache dog in dogs)
                dog.restPosition.SetValue(dog.runtime, dog.originalPosition - dogOffset);

            // The screen-space acceleration frame must travel with the rider.
            foreach (MushAccelerationVfx effect in root.GetComponentsInChildren<MushAccelerationVfx>(true))
                if (effect.transform.parent == root) effect.transform.SetParent(seat, true);

            RefreshBookkeeping();
            return originalSeatPosition - dogOffset;
        }

        public void UpdateTowLines()
        {
            foreach (Action refresh in refreshLines) refresh();
        }

        public void Restore()
        {
            if (!applied) return;
            applied = false;
            if (root == null || seat == null || ride == null) return;

            // Keep the player/camera at their present pose when switching off.
            Vector3 seatPosition = seat.position;
            Quaternion seatRotation = seat.rotation;
            Quaternion rootRotation = Upright(seatRotation * Quaternion.Inverse(originalSeatRotation));
            Vector3 rootPosition = seatPosition - rootRotation * Vector3.Scale(originalSeatPosition, root.lossyScale);
            root.SetPositionAndRotation(rootPosition, rootRotation);

            foreach (ChildPose child in children)
            {
                if (child.transform == null) continue;
                bool reparented = child.transform.parent != root;
                child.transform.SetParent(root, false);
                child.transform.localPosition = child.position;
                if (reparented)
                {
                    child.transform.localRotation = child.rotation;
                    child.transform.localScale = child.scale;
                }
                child.transform.SetSiblingIndex(child.sibling);
            }
            foreach (DogCache dog in dogs)
                dog.restPosition.SetValue(dog.runtime, dog.originalPosition);
            seat.SetPositionAndRotation(seatPosition, seatRotation);
            RefreshBookkeeping();
            UpdateTowLines();
        }

        private void RefreshBookkeeping()
        {
            // A pivot switch is not travelled distance or a collision impact.
            LastPositionField.SetValue(ride, root.position);
            RefreshCheckpoint.Invoke(ride, null);
        }

        public static Quaternion Upright(Quaternion rotation)
        {
            Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            return forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
        }
    }
}
