using System;
using NucleusVR.Anatomy;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    internal sealed class AnatomySubject
    {
        internal readonly ushort Id;
        internal readonly AnatomyDamageState Damage;
        internal readonly AnatomyRig Rig;
        internal readonly Pose[] WirePoses = new Pose[AnatomyWire.BodyCount];
        internal bool IsAvailable => Rig != null && Rig.isActiveAndEnabled;

        private readonly Action<AnatomySnapshot> refresh;
        private readonly Action<bool> authorityChanged;
        private readonly Pose[] targets = new Pose[AnatomyWire.BodyCount];
        private readonly Pose[] offlinePoses = new Pose[AnatomyWire.BodyCount];
        private readonly Pose[] lastSent = new Pose[AnatomyWire.BodyCount];

        private AnatomySnapshot replica;
        private bool hasReplica;
        private bool replicaRagdoll;
        private bool offlineRagdoll;
        private bool hasOfflineState;
        private ushort offlineMask;
        private ushort targetMask;
        private ushort sentMask;
        private uint latestSnapshotSequence;
        private uint latestMotionSequence;
        private uint nextSequence;
        private double lastSentAt;
        private bool observedActive;

        internal AnatomySubject(ushort id, AnatomyDamageState damage, AnatomyRig rig,
            Action<AnatomySnapshot> refresh, Action<bool> onAuthorityChanged)
        {
            Id = id;
            Damage = damage;
            Rig = rig;
            this.refresh = refresh;
            authorityChanged = onAuthorityChanged;
        }

        internal bool TryGetBody(AnatomyTarget target, out Rigidbody body) => target.IsOrgan
            ? Rig.TryGetOrganBody(target.Organ, out body)
            : Rig.TryGetRegionBody(target.Region, out body);

        internal bool ObserveActivation()
        {
            bool active = IsAvailable;
            bool activated = active && !observedActive;
            observedActive = active;
            return activated;
        }

        internal void SetSessionAuthority(bool local)
        {
            if (Rig == null)
                return;

            if (!local && !hasOfflineState)
            {
                offlineMask = CaptureAllBodies(offlinePoses, out _);
                offlineRagdoll = Rig.RagdollActive;
                hasOfflineState = true;
                Rig.SetAuthority(false);
                Rig.ResetRig();
            }
            else if (local && hasOfflineState)
            {
                Rig.SetAuthority(true);
                Rig.ResetRig();
                AnatomySnapshot localState = Damage.CaptureSnapshot();
                ApplyAuthoritativeStages(localState);
                if (offlineRagdoll)
                    Rig.ActivateRagdoll();

                for (int i = 0; i < AnatomyWire.BodyCount; i++)
                {
                    if ((offlineMask & (1 << i)) == 0 || !TryGetSlotBody(i, out Rigidbody body))
                        continue;

                    body.position = offlinePoses[i].position;
                    body.rotation = offlinePoses[i].rotation;
                }
                refresh?.Invoke(localState);
                hasOfflineState = false;
            }
            else
            {
                Rig.SetAuthority(local);
            }

            hasReplica = false;
            targetMask = 0;
            observedActive = false;
            authorityChanged?.Invoke(local);
        }

        internal void ApplyAuthoritativeStages(AnatomySnapshot snapshot)
        {
            for (int i = 0; i < AnatomyWire.Regions; i++)
            {
                var region = (AnatomyRegion)i;
                AnatomyRegionState state = snapshot.GetRegion(region);
                Rig.SetStage(region, state.Stage);
                Rig.SetImpairment(region, state.Impairment);
            }
            if (!Rig.RagdollActive &&
                (snapshot.GetRegion(AnatomyRegion.Head).Stage >= AnatomyStage.Fractured ||
                 snapshot.GetRegion(AnatomyRegion.Torso).Stage >= AnatomyStage.Fractured))
                Rig.ActivateRagdoll();

            for (int i = 0; i < AnatomyWire.Organs; i++)
            {
                OrganKind organ = (OrganKind)i;
                if (snapshot.GetOrgan(organ).IsDetached && Rig.TryGetOrganBody(organ, out Rigidbody body))
                    Rig.DetachOrgan(organ, Vector3.zero, body.position);
            }
        }

        internal void NotifySnapshot(AnatomySnapshot snapshot) => refresh?.Invoke(snapshot);

        internal AnatomyWire.Header CaptureMotion(uint revision, bool onlyWhenMoving)
        {
            ushort mask = CaptureAllBodies(WirePoses, out bool dynamic);
            double now = Time.realtimeSinceStartupAsDouble;
            if (onlyWhenMoving && !dynamic && now - lastSentAt < 0.8 && !PoseChanged(mask))
                return new AnatomyWire.Header(Id, revision, nextSequence, 0);

            Array.Copy(WirePoses, lastSent, AnatomyWire.BodyCount);
            sentMask = mask;
            lastSentAt = now;
            return new AnatomyWire.Header(Id, revision, ++nextSequence, mask);
        }

        internal bool ReceiveSnapshot(AnatomyWire.Header header, AnatomySnapshot snapshot, bool ragdoll)
        {
            bool revisionChanged = !hasReplica || replica.Revision != header.Revision;
            if (hasReplica && (revisionChanged
                ? !RigPose.IsNewer(header.Revision, replica.Revision)
                : !RigPose.IsNewer(header.Sequence, latestSnapshotSequence)))
                return false;

            if (hasReplica && DidReset(snapshot, ragdoll))
                Rig.ResetRig();

            for (int i = 0; i < AnatomyWire.Regions; i++)
            {
                var region = (AnatomyRegion)i;
                AnatomyRegionState state = snapshot.GetRegion(region);
                Rig.SetReplicaStage(region, state.Stage);
                Rig.SetReplicaImpairment(region, state.Impairment);
            }

            if (revisionChanged || RigPose.IsNewer(header.Sequence, latestMotionSequence))
            {
                CacheMotion(header);
                UpdateReplica(1f);
            }

            replica = snapshot;
            latestSnapshotSequence = header.Sequence;
            replicaRagdoll = ragdoll;
            hasReplica = true;
            refresh?.Invoke(snapshot);
            return true;
        }

        internal void ReceiveMotion(AnatomyWire.Header header)
        {
            if (!hasReplica || header.Revision != replica.Revision ||
                !RigPose.IsNewer(header.Sequence, latestMotionSequence))
                return;

            CacheMotion(header);
        }

        internal void UpdateReplica(float interpolation)
        {
            if (Rig == null)
                return;

            for (int i = 0; i < AnatomyWire.BodyCount; i++)
            {
                int bit = 1 << i;
                if ((targetMask & bit) == 0)
                    continue;

                if (!TryGetSlotBody(i, out Rigidbody body))
                {
                    targetMask &= (ushort)~bit;
                    continue;
                }

                Pose target = targets[i];
                bool settled = interpolation >= 1f || (body.position - target.position).sqrMagnitude <= 0.000001f &&
                    Mathf.Abs(Quaternion.Dot(body.rotation, target.rotation)) >= 0.999998f;
                Vector3 position = settled ? target.position : Vector3.Lerp(body.position, target.position, interpolation);
                Quaternion rotation = settled ? target.rotation : Quaternion.Slerp(body.rotation, target.rotation, interpolation);
                if (i < AnatomyWire.Regions)
                    Rig.SetReplicaPose((AnatomyRegion)i, position, rotation);
                else
                    Rig.SetReplicaOrganPose((OrganKind)(i - AnatomyWire.Regions), position, rotation);
                if (settled)
                    targetMask &= (ushort)~bit;
            }
        }

        private bool DidReset(AnatomySnapshot incoming, bool ragdoll)
        {
            if (replicaRagdoll && !ragdoll)
                return true;

            for (int i = 0; i < AnatomyWire.Regions; i++)
                if (incoming.GetRegion((AnatomyRegion)i).Stage < replica.GetRegion((AnatomyRegion)i).Stage)
                    return true;

            for (int i = 0; i < AnatomyWire.Organs; i++)
                if (incoming.GetOrgan((OrganKind)i).Stage < replica.GetOrgan((OrganKind)i).Stage)
                    return true;

            return false;
        }

        private void CacheMotion(AnatomyWire.Header header)
        {
            latestMotionSequence = header.Sequence;
            targetMask = header.BodyMask;
            Array.Copy(WirePoses, targets, AnatomyWire.BodyCount);
        }

        private ushort CaptureAllBodies(Pose[] poses, out bool dynamic)
        {
            ushort mask = 0;
            dynamic = false;
            for (int i = 0; i < AnatomyWire.BodyCount; i++)
            {
                if (!TryGetSlotBody(i, out Rigidbody body) ||
                    !AnatomyWire.IsWorldPosition(body.position) || !AnatomyWire.IsRotation(body.rotation))
                    continue;

                dynamic |= !body.isKinematic;
                poses[i] = new Pose(body.position, body.rotation);
                mask |= (ushort)(1 << i);
            }
            return mask;
        }

        private bool PoseChanged(ushort mask)
        {
            if (mask != sentMask)
                return true;

            for (int i = 0; i < AnatomyWire.BodyCount; i++)
            {
                if ((mask & (1 << i)) != 0 &&
                    ((WirePoses[i].position - lastSent[i].position).sqrMagnitude > 0.000001f ||
                     Mathf.Abs(Quaternion.Dot(WirePoses[i].rotation, lastSent[i].rotation)) < 0.999998f))
                    return true;
            }
            return false;
        }

        private bool TryGetSlotBody(int slot, out Rigidbody body) => TryGetBody(slot < AnatomyWire.Regions
            ? AnatomyTarget.ForRegion((AnatomyRegion)slot)
            : AnatomyTarget.ForOrgan((OrganKind)(slot - AnatomyWire.Regions)), out body);
    }
}
