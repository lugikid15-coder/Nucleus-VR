using NucleusVR.Core;
using NucleusVR.VR;
using UnityEngine;
using ScenePhysics = UnityEngine.Physics;

namespace NucleusVR.Props
{
    internal sealed class EquipmentHands : MonoBehaviour
    {
        private sealed class Hand
        {
            public SphereCollider Shape;
            public GrabbableEquipment Held;
            public Vector3 ObjectOffset;
            public Quaternion ObjectRotation;
            public bool Closed;
        }

        private const float AcquireDistance = 0.19f;
        private const float BreakDistance = 0.83f;
        private readonly Collider[] candidates = new Collider[8];
        private readonly Hand left = new();
        private readonly Hand right = new();
        private TrackedRig rig;

        public void Configure(TrackedRig tracking, Transform leftContact, Transform rightContact)
        {
            rig = tracking;
            left.Shape = leftContact.GetComponent<SphereCollider>();
            right.Shape = rightContact.GetComponent<SphereCollider>();
        }

        public void ReleaseAll()
        {
            Drop(left);
            Drop(right);
        }

        private void OnDisable() => ReleaseAll();

        private void FixedUpdate()
        {
            if (rig == null)
                return;

            Drive(left, rig.HeadTracked && rig.LeftHandTracked ? rig.LeftGrip : 0f);
            Drive(right, rig.HeadTracked && rig.RightHandTracked ? rig.RightGrip : 0f);
        }

        private void Drive(Hand hand, float grip)
        {
            if (grip <= 0.35f)
            {
                hand.Closed = false;
                Drop(hand);
                return;
            }

            Transform target = hand.Shape.transform;
            if (!hand.Closed && grip >= 0.65f)
            {
                hand.Closed = true;
                AttachClosest(hand, target);
            }

            GrabbableEquipment item = hand.Held;
            if (item == null)
                return;

            Rigidbody body = item.Body;
            Quaternion desiredRotation = target.rotation * hand.ObjectRotation;
            Vector3 desiredPosition = target.TransformPoint(hand.ObjectOffset);
            Vector3 error = desiredPosition - body.position;
            if (error.sqrMagnitude > BreakDistance * BreakDistance || !item.isActiveAndEnabled)
            {
                Drop(hand);
                return;
            }

            // A capped spring keeps the object collidable with the world and preserves its
            // measured velocity on release. No tracking-space impulse is injected.
            Vector3 acceleration = error * 100f - body.linearVelocity * 13f;
            body.AddForce(Vector3.ClampMagnitude(acceleration, 60f), ForceMode.Acceleration);

            Quaternion delta = desiredRotation * Quaternion.Inverse(body.rotation);
            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            float angle = Mathf.DeltaAngle(0f, degrees) * Mathf.Deg2Rad;
            Vector3 angularAcceleration = axis * (angle * 82f) - body.angularVelocity * 12f;
            body.AddTorque(Vector3.ClampMagnitude(angularAcceleration, 110f), ForceMode.Acceleration);
        }

        private void AttachClosest(Hand hand, Transform target)
        {
            int count = ScenePhysics.OverlapSphereNonAlloc(target.position, AcquireDistance, candidates,
                1 << CalibrationLayers.Props, QueryTriggerInteraction.Ignore);
            GrabbableEquipment nearest = null;
            float best = AcquireDistance * AcquireDistance;
            for (int i = 0; i < count; i++)
            {
                Collider shape = candidates[i];
                if (!shape.TryGetComponent(out GrabbableEquipment item) || item.IsHeld)
                    continue;
                float distance = (shape.ClosestPoint(target.position) - target.position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = item;
                }
            }

            if (nearest == null || !nearest.TryClaim())
                return;

            hand.Held = nearest;
            hand.ObjectOffset = target.InverseTransformPoint(nearest.Body.position);
            hand.ObjectRotation = Quaternion.Inverse(target.rotation) * nearest.Body.rotation;
            ScenePhysics.IgnoreCollision(hand.Shape, nearest.Shape);
        }

        private static void Drop(Hand hand)
        {
            if (hand.Held == null)
                return;

            if (hand.Shape != null && hand.Held.Shape != null)
                ScenePhysics.IgnoreCollision(hand.Shape, hand.Held.Shape, false);
            hand.Held.Release();
            hand.Held = null;
        }
    }
}
