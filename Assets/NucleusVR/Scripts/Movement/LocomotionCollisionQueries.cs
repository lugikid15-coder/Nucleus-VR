using UnityEngine;

namespace NucleusVR.Movement
{
    internal sealed class LocomotionCollisionQueries
    {
        private readonly RaycastHit[] hits = new RaycastHit[24];
        private readonly Collider[] overlaps = new Collider[24];
        private readonly Rigidbody body;
        private readonly Collider leftHand;
        private readonly Collider rightHand;
        private readonly LayerMask layers;

        internal LocomotionCollisionQueries(Rigidbody body, Collider leftHand, Collider rightHand, LayerMask layers)
        {
            this.body = body;
            this.leftHand = leftHand;
            this.rightHand = rightHand;
            this.layers = layers;
        }

        internal bool SphereCast(Vector3 from, float radius, Vector3 to, out RaycastHit hit)
        {
            Vector3 motion = to - from;
            float distance = motion.magnitude;
            hit = default;
            if (distance < 0.0001f)
                return false;

            Vector3 direction = motion / distance;
            int count = Physics.SphereCastNonAlloc(from, radius, direction, hits, distance, layers,
                QueryTriggerInteraction.Ignore);
            RaycastHit[] results = hits;
            if (count == hits.Length)
            {
                results = Physics.SphereCastAll(from, radius, direction, distance, layers,
                    QueryTriggerInteraction.Ignore);
                count = results.Length;
            }
            return Nearest(results, count, direction, true, out hit);
        }

        internal bool CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 motion, out RaycastHit hit)
        {
            float distance = motion.magnitude;
            hit = default;
            if (distance < 0.0001f)
                return false;

            Vector3 direction = motion / distance;
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, hits, distance, layers,
                QueryTriggerInteraction.Ignore);
            RaycastHit[] results = hits;
            if (count == hits.Length)
            {
                results = Physics.CapsuleCastAll(bottom, top, radius, direction, distance, layers,
                    QueryTriggerInteraction.Ignore);
                count = results.Length;
            }
            return Nearest(results, count, direction, false, out hit);
        }

        internal bool OverlapsBody(Vector3 bottom, Vector3 top, float radius)
        {
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps, layers,
                QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length)
                return true;

            for (int index = 0; index < count; index++)
            {
                if (IsExternal(overlaps[index]))
                    return true;
            }
            return false;
        }

        internal Vector3 SeparateHand(SphereCollider probe, Vector3 center, float skin)
        {
            if (probe == null)
                return center;

            Quaternion orientation = probe.transform.rotation;
            // Two passes handle common floor-wall corners without running a solver over the whole scene.
            for (int pass = 0; pass < 2; pass++)
            {
                int count = Physics.OverlapSphereNonAlloc(center, probe.radius + skin, overlaps, layers,
                    QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int index = 0; index < count; index++)
                {
                    Collider other = overlaps[index];
                    if (!IsExternal(other) || !CanAnchorTo(other))
                        continue;

                    if (Physics.ComputePenetration(probe, center, orientation, other, other.transform.position,
                            other.transform.rotation, out Vector3 normal, out float distance))
                    {
                        center += normal * (distance + skin);
                        moved = true;
                    }
                }
                if (!moved)
                    break;
            }
            return center;
        }

        internal bool CanAnchorTo(Collider collider) => collider != null && collider.enabled && !collider.isTrigger &&
            collider.gameObject.activeInHierarchy &&
            ((1 << collider.gameObject.layer) & layers.value) != 0 &&
            (collider.attachedRigidbody == null || collider.attachedRigidbody.isKinematic);

        private bool IsExternal(Collider collider) => collider != null &&
            collider != leftHand && collider != rightHand && collider.attachedRigidbody != body;

        private bool Nearest(RaycastHit[] results, int count, Vector3 direction, bool anchorsOnly,
            out RaycastHit nearest)
        {
            float closest = float.PositiveInfinity;
            nearest = default;

            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = results[index];
                if (hit.distance >= closest || !IsExternal(hit.collider) ||
                    anchorsOnly && !CanAnchorTo(hit.collider) || Vector3.Dot(direction, hit.normal) > -0.001f)
                    continue;

                closest = hit.distance;
                nearest = hit;
            }
            return nearest.collider != null;
        }
    }
}
