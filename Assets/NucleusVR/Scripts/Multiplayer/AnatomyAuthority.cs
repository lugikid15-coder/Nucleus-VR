using System.Collections.Generic;
using NucleusVR.Anatomy;
using Unity.Netcode;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    internal sealed class AnatomyAuthority
    {
        private const float ContactReach = 0.24f;
        private const float CommandInterval = 0.14f;
        private const float MaxAnchorSpeed = 12f;
        private const float MinimumStrikeMotion = 0.15f;
        private const float MinimumPeerStrikeSpeed = 0.65f;

        private readonly NetworkManager manager;
        private readonly Dictionary<ulong, Actor> actors = new();
        private readonly List<Collider> targetColliders = new();

        internal AnatomyAuthority(NetworkManager manager)
        {
            this.manager = manager;
        }

        internal static bool IsPeerAction(AnatomyAction action) =>
            action is AnatomyAction.Damage or AnatomyAction.Grab or AnatomyAction.Release;

        internal bool TryAction(ulong sender, AnatomySubject subject, AnatomyAction action, AnatomyTarget target,
            bool left, out bool update, out AnatomyImpact? impact)
        {
            update = false;
            impact = null;
            bool fromPeer = sender != NetworkManager.ServerClientId;
            if ((fromPeer && !IsPeerAction(action)) || !TryGetPlayer(sender, out NetworkRig player))
                return false;

            if (!actors.TryGetValue(sender, out Actor actor))
            {
                actor = new Actor();
                actors.Add(sender, actor);
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (action == AnatomyAction.Release)
            {
                bool tracked = player.TryGetAcceptedHand(left, now, out _, out _);
                return actor.Release(left, subject.Id, tracked);
            }

            if (action == AnatomyAction.Reset)
            {
                ReleaseSubject(subject.Id);
                subject.Rig.ResetRig();
                subject.Damage.Reset();
                update = true;
                return true;
            }

            if (!actor.Reserve(left, now))
                return false;

            if (!player.TryGetAcceptedHand(left, now, out Vector3 hand, out Vector3 motion) ||
                (fromPeer && action == AnatomyAction.Damage && motion.sqrMagnitude < MinimumPeerStrikeSpeed * MinimumPeerStrikeSpeed) ||
                !subject.TryGetBody(target, out Rigidbody body) || !FindContact(body, hand, out Vector3 point))
                return false;

            AnatomyOrganState organ = target.IsOrgan ? subject.Damage.GetOrgan(target.Organ) : default;
            if (target.IsOrgan && !organ.IsExposed && !organ.IsDetached)
                return false;

            Vector3 direction = ImpactDirection(motion, hand, body.worldCenterOfMass);
            float speed = motion.magnitude;
            Vector3 impulse = direction * (1.8f + speed * 0.55f);

            if (action == AnatomyAction.Grab)
                return TryGrab(actor, subject, target, left, body, hand, point, direction, out update, out impact);

            bool looseOrganImpact = target.IsOrgan && organ.IsDetached && !body.isKinematic && action == AnatomyAction.Damage;
            bool accepted = looseOrganImpact || (action switch
            {
                AnatomyAction.Damage => Damage(subject, target, speed),
                AnatomyAction.Fracture => !target.IsOrgan &&
                    subject.Damage.ApplyFracture(target.Region, 46f + speed * 2.5f),
                AnatomyAction.Detach => Detach(subject, target, impulse, point),
                _ => false
            });
            if (!accepted)
                return false;

            subject.ApplyAuthoritativeStages(subject.Damage.CaptureSnapshot());
            if (!target.IsOrgan)
                subject.Rig.ApplyImpact(target.Region, point, impulse);
            else if (looseOrganImpact)
                body.AddForceAtPosition(Vector3.ClampMagnitude(impulse, Mathf.Min(2f, body.mass * 2f)), point, ForceMode.Impulse);

            update = true;
            impact = new AnatomyImpact(subject.Id, action, target, point, direction);
            return true;
        }

        internal void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            foreach (var entry in actors)
            {
                Actor actor = entry.Value;
                if (!actor.HasGrip)
                    continue;

                if (!TryGetPlayer(entry.Key, out NetworkRig player))
                {
                    actor.Release(false, actor.SubjectFor(false), false);
                    actor.Release(true, actor.SubjectFor(true), false);
                    continue;
                }

                FollowHand(actor, player, false, now);
                FollowHand(actor, player, true, now);
            }
        }

        internal void Disconnect(ulong clientId)
        {
            if (actors.Remove(clientId, out Actor actor))
                actor.Dispose();
        }

        internal void ReleaseSubject(ushort subjectId)
        {
            foreach (Actor actor in actors.Values)
            {
                actor.Release(true, subjectId, false);
                actor.Release(false, subjectId, false);
            }
        }

        internal void Stop()
        {
            foreach (Actor actor in actors.Values)
                actor.Dispose();
            actors.Clear();
        }

        private bool TryGrab(Actor actor, AnatomySubject subject, AnatomyTarget target, bool left,
            Rigidbody body, Vector3 handPoint, Vector3 hitPoint, Vector3 direction,
            out bool update, out AnatomyImpact? impact)
        {
            impact = null;
            update = false;
            if (actor.Held(left) != null)
                return false;

            foreach (Actor other in actors.Values)
                if (other != actor && (other.Held(true) == body || other.Held(false) == body))
                    return false;

            if (body.isKinematic)
            {
                if (target.IsOrgan)
                {
                    if (!Detach(subject, target, Vector3.zero, hitPoint))
                        return false;

                    update = true;
                    impact = new AnatomyImpact(subject.Id, AnatomyAction.Detach, target, hitPoint, direction);
                }
                else if (target.Region == AnatomyRegion.Torso)
                {
                    update = subject.Rig.ActivateRagdoll();
                }
            }

            if (body.isKinematic)
                return update;

            actor.EnsureAnchors(manager.transform);
            actor.Hand(left).position = handPoint;
            actor.Grip.ResetHandMotion(left);
            if (actor.Grip.TryGrab(left, body, hitPoint))
            {
                actor.Track(left, subject.Id);
                return true;
            }

            return update;
        }

        private static bool Damage(AnatomySubject subject, AnatomyTarget target, float speed)
        {
            float amount = Mathf.Clamp(speed * 5.8f, 6f, 43f);
            if (target.IsOrgan)
            {
                AnatomyOrganState organ = subject.Damage.GetOrgan(target.Organ);
                return subject.Damage.ApplyDamage(organ.Region, amount, target.Organ);
            }

            bool changed = subject.Damage.ApplyDamage(target.Region, amount);
            if (speed >= 6f)
            {
                changed |= subject.Damage.ApplyFracture(target.Region, speed * 2.1f);
                changed |= subject.Damage.ApplyJointDamage(target.Region, speed * 1.1f);
            }
            return changed;
        }

        private static bool Detach(AnatomySubject subject, AnatomyTarget target, Vector3 impulse, Vector3 point)
        {
            if (target.IsOrgan)
            {
                if (!subject.Damage.TryDetachOrgan(target.Organ))
                    return false;

                subject.Rig.DetachOrgan(target.Organ, impulse, point);
                return true;
            }

            AnatomyRegionDefinition definition = subject.Damage.Profile.GetRegion(target.Region);
            return definition.CanSever && subject.Damage.GetRegion(target.Region).Stage == AnatomyStage.Fractured &&
                subject.Damage.ApplyJointDamage(target.Region, definition.StructuralCapacity);
        }

        private static void FollowHand(Actor actor, NetworkRig player, bool left, double now)
        {
            ushort subjectId = actor.SubjectFor(left);
            if (subjectId == 0)
                return;

            if (actor.Held(left) == null || !player.TryGetAcceptedHand(left, now, out Vector3 point, out _))
            {
                actor.Release(left, subjectId, false);
                return;
            }

            Rigidbody hand = actor.Hand(left);
            hand.MovePosition(Vector3.MoveTowards(hand.position, point, MaxAnchorSpeed * Time.fixedDeltaTime));
        }

        private bool FindContact(Rigidbody body, Vector3 hand, out Vector3 point)
        {
            point = default;
            float squaredReach = ContactReach * ContactReach;
            bool found = false;
            body.GetComponentsInChildren(false, targetColliders);
            foreach (Collider shape in targetColliders)
            {
                if (!shape.enabled || shape.attachedRigidbody != body)
                    continue;

                Vector3 nearest = shape.ClosestPoint(hand);
                float squaredDistance = (nearest - hand).sqrMagnitude;
                if (!AnatomyWire.IsWorldPosition(nearest) || squaredDistance > squaredReach)
                    continue;

                squaredReach = squaredDistance;
                point = nearest;
                found = true;
            }
            targetColliders.Clear();
            return found;
        }

        private bool TryGetPlayer(ulong sender, out NetworkRig player)
        {
            player = null;
            return manager.ConnectedClients.TryGetValue(sender, out NetworkClient client) &&
                client.PlayerObject != null && client.PlayerObject.TryGetComponent(out player);
        }

        private static Vector3 ImpactDirection(Vector3 motion, Vector3 hand, Vector3 targetCenter)
        {
            if (motion.sqrMagnitude >= MinimumStrikeMotion * MinimumStrikeMotion)
                return motion.normalized;

            Vector3 toCenter = targetCenter - hand;
            return toCenter.sqrMagnitude > 0.0001f ? toCenter.normalized : Vector3.up;
        }

        private sealed class Actor
        {
            private double nextLeftCommand, nextRightCommand;
            private GameObject root;
            private Rigidbody leftHand;
            private Rigidbody rightHand;
            private ushort leftSubject, rightSubject;
            internal AnatomyGrip Grip { get; private set; }
            internal bool HasGrip => leftSubject != 0 || rightSubject != 0;

            internal Rigidbody Hand(bool left) => left ? leftHand : rightHand;
            internal Rigidbody Held(bool left) => Grip == null ? null : Grip.GetHeldBody(left);
            internal ushort SubjectFor(bool left) => left ? leftSubject : rightSubject;

            internal bool Reserve(bool left, double now)
            {
                if (now < (left ? nextLeftCommand : nextRightCommand))
                    return false;

                if (left)
                    nextLeftCommand = now + CommandInterval;
                else
                    nextRightCommand = now + CommandInterval;
                return true;
            }

            internal void Track(bool left, ushort subjectId)
            {
                if (left)
                    leftSubject = subjectId;
                else
                    rightSubject = subjectId;
            }

            internal bool Release(bool left, ushort subjectId, bool throwOnRelease)
            {
                if (subjectId == 0 || SubjectFor(left) != subjectId)
                    return false;

                Grip.Release(left, throwOnRelease);
                Track(left, 0);
                return true;
            }

            internal void EnsureAnchors(Transform parent)
            {
                if (root != null)
                    return;

                root = new GameObject("Authoritative anatomy grip");
                root.transform.SetParent(parent, false);
                leftHand = CreateHand("Left anchor");
                rightHand = CreateHand("Right anchor");
                Grip = root.AddComponent<AnatomyGrip>();
                Grip.Configure(leftHand, rightHand, true);
            }

            internal void Dispose()
            {
                if (root == null)
                    return;

                Grip.ReleaseAll();
                Object.Destroy(root);
                root = null;
            }

            private Rigidbody CreateHand(string name)
            {
                var anchor = new GameObject(name);
                anchor.transform.SetParent(root.transform, false);
                var rigidbody = anchor.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
                rigidbody.detectCollisions = false;
                return rigidbody;
            }
        }
    }
}
