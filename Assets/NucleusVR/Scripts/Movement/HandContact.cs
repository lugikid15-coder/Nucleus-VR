using UnityEngine;

namespace NucleusVR.Movement
{
    internal sealed class HandContact
    {
        private const float Skin = 0.006f;
        private const float PullAwayDistance = 0.04f;
        private const float MaximumAnchorStretch = 0.58f;
        private const float MaximumPhysicalHandSpeed = 11f;
        private const float MinimumTapSpeed = 0.4f;
        private const float FullTapSpeed = 4.5f;
        private const float TapCooldown = 0.14f;
        private const float SlideProbeDistance = 0.04f;

        private readonly Transform contactTransform;
        private readonly Rigidbody handBody;
        private readonly SphereCollider sphere;
        private readonly float radius;
        private Vector3 colliderAnchor, colliderNormal;
        private float slip, tapCooldownRemaining;
        private bool initialized, blockInitialAnchor;

        internal readonly struct Impact
        {
            internal readonly Collider Collider;
            internal readonly Vector3 Point;
            internal readonly float Strength;

            internal Impact(RaycastHit hit, float strength)
            {
                Collider = hit.collider;
                Point = hit.point;
                Strength = strength;
            }
        }

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
        internal Vector3 Normal { get; private set; }
        internal bool SupportsWeight => Anchor != null && (slip < 1f || Normal.y >= 0.5f);
        internal SphereCollider Sphere => sphere;
        internal Impact Tap { get; private set; }

        internal Vector3 Resolve(bool tracked, Vector3 shoulder, Vector3 target, Quaternion rotation,
            LocomotionCollisionQueries queries, float step)
        {
            Tap = default;
            tapCooldownRemaining = Mathf.Max(0f, tapCooldownRemaining - step);
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

            Collider departedEdge = null;
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
                    if (Slide(target, center, normal, queries, step))
                    {
                        MoveCollider(rotation, firstSample);
                        return DriveTo(target);
                    }

                    departedEdge = Anchor;
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
                if (blockInitialAnchor || hit.collider == departedEdge)
                {
                    MoveCollider(rotation, firstSample);
                    return Vector3.zero;
                }

                Anchor = hit.collider;
                HandSurface surface = Anchor.GetComponentInParent<HandSurface>();
                slip = surface != null ? surface.Slip : 0f;
                Bind(Position, hit.normal);
                RecordTap(hit, (end - start) / step);
                if (!Slide(target, Position, Normal, queries, step))
                    Anchor = null;

                MoveCollider(rotation, firstSample);
                return Anchor != null ? DriveTo(target) : Vector3.zero;
            }

            Position = queries.SeparateHand(sphere, end, Skin);
            blockInitialAnchor = false;
            MoveCollider(rotation, firstSample);
            return Vector3.zero;
        }

        internal void Reset()
        {
            Anchor = null;
            Tap = default;
            initialized = false;
            if (sphere != null)
                sphere.enabled = false;
        }

        private bool Slide(Vector3 target, Vector3 center, Vector3 normal,
            LocomotionCollisionQueries queries, float step)
        {
            Position = center;
            Normal = normal;
            if (slip <= 0f)
                return true;

            Vector3 tangent = Vector3.ProjectOnPlane(target - center, normal) * slip;
            Vector3 travel = Vector3.ClampMagnitude(tangent, MaximumPhysicalHandSpeed * step);
            if (travel.sqrMagnitude < 0.000001f)
                return true;

            Vector3 sideStart = center + normal * Skin;
            if (queries.SphereCast(sideStart, radius, sideStart + travel, out RaycastHit boundary))
                travel = travel.normalized * Mathf.Max(0f, boundary.distance - Skin);

            // If the original surface ends, leave from the swept position so the next free sweep
            // does not keep pulling the player toward the previous anchor at the edge.
            Position = center + travel;
            Vector3 above = Position + normal * SlideProbeDistance;
            Vector3 below = Position - normal * SlideProbeDistance;
            if (!queries.SphereCast(above, radius, below, out RaycastHit support) ||
                support.collider != Anchor || Vector3.Dot(support.normal, normal) < 0.9f)
                return false;

            Vector3 seated = above - normal * Mathf.Max(0f, support.distance - Skin);
            Bind(seated, support.normal);
            return true;
        }

        private void Bind(Vector3 center, Vector3 normal)
        {
            Position = center;
            Normal = normal;
            Transform surface = Anchor.transform;
            colliderAnchor = surface.InverseTransformPoint(center);
            colliderNormal = surface.InverseTransformDirection(normal);
        }

        private Vector3 DriveTo(Vector3 target)
        {
            Vector3 drive = Position - target;
            return slip < 1f ? drive : Normal * Vector3.Dot(drive, Normal);
        }

        private void RecordTap(RaycastHit hit, Vector3 incomingVelocity)
        {
            float speed = Vector3.Dot(-hit.normal, incomingVelocity);
            if (tapCooldownRemaining > 0f || speed <= MinimumTapSpeed)
                return;

            Tap = new Impact(hit, Mathf.InverseLerp(MinimumTapSpeed, FullTapSpeed, speed));
            tapCooldownRemaining = TapCooldown;
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
