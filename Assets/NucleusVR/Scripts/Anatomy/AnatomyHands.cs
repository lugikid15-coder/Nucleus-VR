using NucleusVR.Core;
using NucleusVR.Movement;
using NucleusVR.Multiplayer;
using NucleusVR.VR;
using UnityEngine;

namespace NucleusVR.Anatomy
{
    /// <summary>Converts strokes and grip edges from the physical VR hands into anatomy actions.</summary>
    [DefaultExecutionOrder(150)]
    internal sealed class AnatomyHands : MonoBehaviour
    {
        private sealed class Hand
        {
            public Rigidbody Body;
            public Vector3 Previous;
            public AnatomyHitbox Contact;
            public AnatomyHitbox RequestedGrab;
            public bool Sampled;
            public bool Closed;
            public float NextStrike;
        }

        private const float ImpactRadius = 0.105f;
        private const float GrabRadius = 0.17f;
        private const float MinimumImpactSpeed = 1.65f;
        private const float MaximumTickTravel = 0.39f;

        private readonly Collider[] overlaps = new Collider[20];
        private readonly RaycastHit[] sweeps = new RaycastHit[14];
        private readonly Hand left = new();
        private readonly Hand right = new();
        private TrackedRig tracking;
        private AnatomyGrip grip;
        private DirectSession session;
        private bool wasOnline;

        public void Configure(TrackedRig source, HandLocomotion movement, DirectSession directSession)
        {
            tracking = source;
            left.Body = movement.LeftContact.GetComponent<Rigidbody>();
            right.Body = movement.RightContact.GetComponent<Rigidbody>();
            grip = gameObject.AddComponent<AnatomyGrip>();
            grip.Configure(left.Body, right.Body, true);
            session = directSession;
        }

        private void FixedUpdate()
        {
            if (tracking == null)
                return;

            bool online = session != null && session.IsSessionActive;
            if (online != wasOnline)
            {
                grip.ReleaseAll();
                left.RequestedGrab = right.RequestedGrab = null;
                left.Sampled = right.Sampled = false;
                wasOnline = online;
            }

            Tick(left, true, tracking.HeadTracked && tracking.LeftHandTracked, tracking.LeftGrip, online);
            Tick(right, false, tracking.HeadTracked && tracking.RightHandTracked, tracking.RightGrip, online);
        }

        private void Tick(Hand hand, bool isLeft, bool tracked, float gripValue, bool online)
        {
            if (!tracked)
            {
                Release(hand, isLeft, online, false);
                hand.Closed = false;
                hand.Sampled = false;
                hand.Contact = null;
                return;
            }

            bool canRelease = hand.Closed && gripValue <= 0.35f;
            bool canGrab = !hand.Closed && gripValue >= 0.65f;
            if (canRelease)
            {
                Release(hand, isLeft, online, true);
                hand.Closed = false;
            }
            else if (canGrab)
                hand.Closed = true;

            Vector3 position = hand.Body.position;
            Vector3 before = hand.Previous;
            bool sampled = hand.Sampled;
            hand.Previous = position;
            hand.Sampled = true;
            AnatomyHitbox prior = hand.Contact;
            hand.Contact = SampleNearby(position, ImpactRadius);
            if (canGrab && TryGrab(hand, isLeft, position, online))
                return;
            if (grip.GetHeldBody(isLeft) != null || hand.RequestedGrab != null || !sampled ||
                Time.fixedTime < hand.NextStrike)
                return;

            Vector3 travel = position - before;
            float distance = travel.magnitude;
            if (distance > MaximumTickTravel || distance < MinimumImpactSpeed * Time.fixedDeltaTime)
                return;

            Vector3 direction = travel / distance;
            Vector3 point = position;
            AnatomyHitbox target = hand.Contact != prior ? hand.Contact : null;
            if (target != null)
                point = SurfacePoint(target.Body, before, position);
            else
                target = SweepForImpact(before, direction, distance, prior, out point);
            if (target == null)
                return;

            hand.NextStrike = Time.fixedTime + 0.20f;
            if (online)
                session.RequestAnatomy(target.Subject.Id, AnatomyAction.Damage, Encode(target), isLeft);
            else
                target.Subject.Strike(target, point, travel / Time.fixedDeltaTime);
        }

        private bool TryGrab(Hand hand, bool isLeft, Vector3 position, bool online)
        {
            AnatomyHitbox target = SampleNearby(position, GrabRadius);
            if (target == null)
                return false;

            if (online)
            {
                if (!session.RequestAnatomy(target.Subject.Id, AnatomyAction.Grab, Encode(target), isLeft))
                    return false;
                hand.RequestedGrab = target;
                return true;
            }

            Vector3 point = SurfacePoint(target.Body, position, position);
            if (target.Organ.HasValue)
            {
                if (!target.Subject.Extract(target, point, position - target.Body.worldCenterOfMass))
                    return false;
            }
            else if (target.Region == AnatomyRegion.Torso && target.Body.isKinematic)
                target.Subject.Rig.ActivateRagdoll();
            return grip.TryGrab(isLeft, target.Body, point);
        }

        private void Release(Hand hand, bool isLeft, bool online, bool throwing)
        {
            if (online)
            {
                AnatomyHitbox target = hand.RequestedGrab;
                if (target != null)
                    session.RequestAnatomy(target.Subject.Id, AnatomyAction.Release, Encode(target), isLeft);
                hand.RequestedGrab = null;
            }
            else if (grip.GetHeldBody(isLeft) != null)
                grip.Release(isLeft, throwing);
        }

        private AnatomyHitbox SampleNearby(Vector3 position, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(position, radius, overlaps,
                CalibrationLayers.AnatomyMask, QueryTriggerInteraction.Collide);
            float best = float.PositiveInfinity;
            AnatomyHitbox selected = null;
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                if (!collider.TryGetComponent(out AnatomyHitbox target) || !VisibleTarget(target))
                    continue;
                float distance = (collider.ClosestPoint(position) - position).sqrMagnitude;
                // Small exposed organs can be selected even when overlapping the torso hull.
                distance += target.Organ.HasValue ? -0.009f : 0f;
                if (distance >= best)
                    continue;
                best = distance;
                selected = target;
            }
            return selected;
        }

        private AnatomyHitbox SweepForImpact(Vector3 position, Vector3 direction, float distance, AnatomyHitbox prior,
            out Vector3 point)
        {
            int count = Physics.SphereCastNonAlloc(position, ImpactRadius, direction, sweeps,
                distance, CalibrationLayers.AnatomyMask, QueryTriggerInteraction.Collide);
            point = position + direction * distance;
            float earliest = float.PositiveInfinity;
            AnatomyHitbox selected = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = sweeps[i];
                if (hit.distance >= earliest || !hit.collider.TryGetComponent(out AnatomyHitbox target) ||
                    target == prior || !VisibleTarget(target))
                    continue;
                earliest = hit.distance;
                selected = target;
                point = hit.point;
            }
            return selected;
        }

        private static Vector3 SurfacePoint(Rigidbody target, Vector3 approach, Vector3 fallback) =>
            target.TryGetComponent(out Collider shape) ? shape.ClosestPoint(approach) : fallback;

        private static bool VisibleTarget(AnatomyHitbox target) =>
            target.Subject != null && (!target.Organ.HasValue || target.Subject.OrganInteractionVisible);

        private static AnatomyTarget Encode(AnatomyHitbox target) => target.Organ.HasValue
            ? AnatomyTarget.ForOrgan(target.Organ.Value) : AnatomyTarget.ForRegion(target.Region);

        internal void ReleaseAll()
        {
            if (grip == null)
                return;
            grip.ReleaseAll();
            Release(left, true, wasOnline, false);
            Release(right, false, wasOnline, false);
            left.Sampled = right.Sampled = false;
            left.Contact = right.Contact = null;
        }

        private void OnDisable() => ReleaseAll();
    }
}
