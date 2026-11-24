using UnityEngine;

namespace NucleusVR.Movement
{
    internal sealed class HandContact
    {
        private const float Skin = 0.006f;
        private const float PullAwayDistance = 0.04f;
        private const float MaximumAnchorStretch = 0.58f;
        private const float MaximumPhysicalHandSpeed = 11f;

        private readonly Transform contactTransform;
        private readonly Rigidbody handBody;
        private readonly SphereCollider sphere;
        private readonly float radius;
        private Vector3 colliderAnchor, colliderNormal;
        private bool initialized, blockInitialAnchor;

        internal HandContact(Transform contactTransform)
        {
            this.contactTransform = contactTransform;
            if (contactTransform != null)
            {
                handBody = contactTransform.GetComponent<Rigidbody>();
                sphere = contactTransform.GetComponent<SphereCollider>();
            }
            radius = sphere != null ? sphere.radius : 0.085f;
        }

        internal Collider Anchor { get; private set; }
        internal Vector3 Position { get; private set; }
        internal SphereCollider Sphere => sphere;

        internal Vector3 Resolve(bool tracked, Vector3 shoulder, Vector3 target, Quaternion rotation,
            LocomotionCollisionQueries queries, float step)
        {
            if (!tracked)
            {
                Reset();
                return Vector3.zero;
            }

            bool firstSample = !initialized;
            if (firstSample)
            {
                if (sphere != null)
                    sphere.enabled = true;
                Position = queries.SeparateHand(sphere, shoulder, Skin);
                initialized = true;
                blockInitialAnchor = true;
            }

            if (Anchor != null && queries.CanAnchorTo(Anchor))
            {
                Transform surface = Anchor.transform;
                Vector3 center = surface.TransformPoint(colliderAnchor);
                Vector3 normal = surface.TransformDirection(colliderNormal);
                Vector3 error = target - center;
                if (Vector3.Dot(error, normal) <= PullAwayDistance &&
                    error.sqrMagnitude <= MaximumAnchorStretch * MaximumAnchorStretch &&
                    (center - Position).sqrMagnitude <= 0.25f)
                {
                    Position = center;
                    MoveCollider(rotation, firstSample);
                    return -error;
                }
            }

            Anchor = null;
            Vector3 start = queries.SeparateHand(sphere, Position, Skin);
            Vector3 end = Vector3.MoveTowards(start, target, MaximumPhysicalHandSpeed * step);
            if (firstSample)
                end = target;

            if (queries.SphereCast(start, radius, end, out RaycastHit hit))
            {
                Vector3 direction = (end - start).normalized;
                Position = start + direction * Mathf.Max(0f, hit.distance - Skin);
                if (blockInitialAnchor)
                {
                    MoveCollider(rotation, firstSample);
                    return Vector3.zero;
                }
                Anchor = hit.collider;
                colliderAnchor = Anchor.transform.InverseTransformPoint(Position);
                colliderNormal = Anchor.transform.InverseTransformDirection(hit.normal);
                MoveCollider(rotation, firstSample);
                return Position - target;
            }

            Position = queries.SeparateHand(sphere, end, Skin);
            blockInitialAnchor = false;
            MoveCollider(rotation, firstSample);
            return Vector3.zero;
        }

        internal void Reset()
        {
            Anchor = null;
            initialized = false;
            if (sphere != null)
                sphere.enabled = false;
        }

        private void MoveCollider(Quaternion rotation, bool teleport)
        {
            if (contactTransform == null)
                return;

            if (handBody == null)
            {
                contactTransform.SetPositionAndRotation(Position, rotation);
            }
            else if (teleport)
            {
                handBody.position = Position;
                handBody.rotation = rotation;
            }
            else
            {
                handBody.MovePosition(Position);
                handBody.MoveRotation(rotation);
            }
            if (sphere != null)
                sphere.enabled = true;
        }
    }
}
