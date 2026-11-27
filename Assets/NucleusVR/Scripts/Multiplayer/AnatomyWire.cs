using System;
using NucleusVR.Anatomy;
using Unity.Netcode;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    internal static class AnatomyWire
    {
        internal const int Regions = (int)AnatomyRegion.Count;
        internal const int Organs = (int)OrganKind.Count;
        internal const int BodyCount = Regions + Organs;
        internal const int RequestBytes = sizeof(ushort) + 4 * sizeof(byte);
        internal const int HeaderBytes = 2 * sizeof(uint) + 2 * sizeof(ushort);
        internal const int SnapshotBytes = 2 * sizeof(byte) +
            Regions * (4 * sizeof(float) + sizeof(byte) + sizeof(ushort)) +
            Organs * (sizeof(float) + 3 * sizeof(byte));
        internal const int ImpactBytes = 3 * sizeof(byte) + 6 * sizeof(float);
        internal const int PoseBytes = 7 * sizeof(float);
        internal const int MaxFrameBytes = HeaderBytes + SnapshotBytes + ImpactBytes + BodyCount * PoseBytes;

        internal readonly struct Header
        {
            public readonly ushort Id, BodyMask;
            public readonly uint Revision, Sequence;

            public Header(ushort id, uint revision, uint sequence, ushort bodyMask)
            {
                Id = id;
                Revision = revision;
                Sequence = sequence;
                BodyMask = bodyMask;
            }
        }

        internal static void WriteRequest(FastBufferWriter writer, ushort id, AnatomyAction action, AnatomyTarget target, bool left)
        {
            writer.WriteValueSafe(id);
            writer.WriteValueSafe((byte)action);
            writer.WriteValueSafe((byte)(target.IsOrgan ? 1 : 0));
            writer.WriteValueSafe(target.Index);
            writer.WriteValueSafe((byte)(left ? 1 : 0));
        }

        internal static bool TryReadRequest(FastBufferReader reader, out ushort id, out AnatomyAction action,
            out AnatomyTarget target, out bool left)
        {
            id = 0;
            action = default;
            target = default;
            left = false;
            if (reader.Length - reader.Position != RequestBytes)
                return false;

            reader.ReadValueSafe(out id);
            reader.ReadValueSafe(out byte code);
            reader.ReadValueSafe(out byte scope);
            reader.ReadValueSafe(out byte index);
            reader.ReadValueSafe(out byte hand);
            action = (AnatomyAction)code;
            target = new AnatomyTarget(scope == 1, index);
            left = hand == 1;
            return id != 0 && code <= (byte)AnatomyAction.Reset && scope <= 1 && hand <= 1 && target.IsValid;
        }

        internal static void WriteHeader(FastBufferWriter writer, Header header)
        {
            writer.WriteValueSafe(header.Id);
            writer.WriteValueSafe(header.Revision);
            writer.WriteValueSafe(header.Sequence);
            writer.WriteValueSafe(header.BodyMask);
        }

        internal static bool TryReadHeader(FastBufferReader reader, out Header header)
        {
            header = default;
            if (reader.Length - reader.Position < HeaderBytes)
                return false;

            reader.ReadValueSafe(out ushort id);
            reader.ReadValueSafe(out uint revision);
            reader.ReadValueSafe(out uint sequence);
            reader.ReadValueSafe(out ushort mask);
            header = new Header(id, revision, sequence, mask);
            return id != 0 && (mask & ~((1 << BodyCount) - 1)) == 0;
        }

        internal static void WriteSnapshot(FastBufferWriter writer, AnatomySnapshot snapshot, bool ragdoll, AnatomyImpact? impact)
        {
            writer.WriteValueSafe((byte)(ragdoll ? 1 : 0));
            writer.WriteValueSafe((byte)(impact.HasValue ? 1 : 0));
            for (int i = 0; i < Regions; i++)
            {
                AnatomyRegionState value = snapshot.GetRegion((AnatomyRegion)i);
                writer.WriteValueSafe(value.Damage);
                writer.WriteValueSafe(value.Fracture);
                writer.WriteValueSafe(value.JointDamage);
                writer.WriteValueSafe(value.Impairment);
                writer.WriteValueSafe((byte)value.Stage);
                writer.WriteValueSafe((ushort)value.VisibleLayers);
            }

            for (int i = 0; i < Organs; i++)
            {
                AnatomyOrganState value = snapshot.GetOrgan((OrganKind)i);
                writer.WriteValueSafe((byte)value.Region);
                writer.WriteValueSafe(value.Damage);
                writer.WriteValueSafe((byte)value.Stage);
                writer.WriteValueSafe((byte)(value.IsExposed ? 1 : 0));
            }

            if (impact is AnatomyImpact valueImpact)
            {
                writer.WriteValueSafe((byte)valueImpact.Action);
                writer.WriteValueSafe((byte)(valueImpact.Target.IsOrgan ? 1 : 0));
                writer.WriteValueSafe(valueImpact.Target.Index);
                writer.WriteValueSafe(valueImpact.Point);
                writer.WriteValueSafe(valueImpact.Direction);
            }
        }

        internal static bool TryReadSnapshot(FastBufferReader reader, Header header, out AnatomySnapshot snapshot,
            out bool ragdoll, out AnatomyImpact? impact)
        {
            snapshot = default;
            ragdoll = false;
            impact = null;
            int motionBytes = MotionBytes(header.BodyMask);
            if (reader.Length - reader.Position < SnapshotBytes + motionBytes)
                return false;

            reader.ReadValueSafe(out byte ragdollFlag);
            reader.ReadValueSafe(out byte impactFlag);

            if (ragdollFlag > 1 || impactFlag > 1 ||
                reader.Length - reader.Position != SnapshotBytes - 2 + motionBytes + ImpactBytes * impactFlag)
                return false;

            var regions = new AnatomyRegionState[Regions];
            for (int i = 0; i < Regions; i++)
            {
                reader.ReadValueSafe(out float damage);
                reader.ReadValueSafe(out float fracture);
                reader.ReadValueSafe(out float joint);
                reader.ReadValueSafe(out float impairment);
                reader.ReadValueSafe(out byte stage);
                reader.ReadValueSafe(out ushort layers);
                if (!UnitValue(damage) || !UnitValue(fracture) || !UnitValue(joint) ||
                    !UnitValue(impairment) || stage > (byte)AnatomyStage.Severed ||
                    (layers & ~(ushort)AnatomyLayers.All) != 0)
                    return false;

                regions[i] = new AnatomyRegionState(damage, fracture, joint, impairment, (AnatomyStage)stage, (AnatomyLayers)layers);
            }

            var organs = new AnatomyOrganState[Organs];
            for (int i = 0; i < Organs; i++)
            {
                reader.ReadValueSafe(out byte region);
                reader.ReadValueSafe(out float damage);
                reader.ReadValueSafe(out byte stage);
                reader.ReadValueSafe(out byte exposed);
                if (region >= Regions || !UnitValue(damage) || stage > (byte)AnatomyOrganStage.Detached || exposed > 1)
                    return false;

                organs[i] = new AnatomyOrganState((AnatomyRegion)region, damage, (AnatomyOrganStage)stage, exposed == 1);
            }

            if (impactFlag == 1)
            {
                reader.ReadValueSafe(out byte action);
                reader.ReadValueSafe(out byte scope);
                reader.ReadValueSafe(out byte index);
                reader.ReadValueSafe(out Vector3 point);
                reader.ReadValueSafe(out Vector3 direction);
                var target = new AnatomyTarget(scope == 1, index);
                if (action > (byte)AnatomyAction.Detach || scope > 1 || !target.IsValid ||
                    !IsWorldPosition(point) || !IsFinite(direction) || direction.sqrMagnitude > 1.01f)
                    return false;

                impact = new AnatomyImpact(header.Id, (AnatomyAction)action, target, point, direction);
            }

            snapshot = new AnatomySnapshot(header.Revision, regions, organs);
            ragdoll = ragdollFlag == 1;
            return true;
        }

        internal static int MotionBytes(ushort mask)
        {
            int bodies = 0;
            while (mask != 0)
            {
                mask = (ushort)(mask & (mask - 1));
                bodies++;
            }
            return bodies * PoseBytes;
        }

        internal static void WriteMotion(FastBufferWriter writer, ushort mask, Pose[] poses)
        {
            for (int i = 0; i < BodyCount; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                writer.WriteValueSafe(poses[i].position);
                writer.WriteValueSafe(poses[i].rotation);
            }
        }

        internal static bool TryReadMotion(FastBufferReader reader, ushort mask, Pose[] poses)
        {
            if (reader.Length - reader.Position != MotionBytes(mask))
                return false;

            for (int i = 0; i < BodyCount; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                reader.ReadValueSafe(out Vector3 position);
                reader.ReadValueSafe(out Quaternion rotation);
                if (!IsWorldPosition(position) || !IsRotation(rotation))
                    return false;

                poses[i] = new Pose(position, Quaternion.Normalize(rotation));
            }
            return true;
        }

        internal static bool IsWorldPosition(Vector3 value)
        {
            const float Bounds = 1000f;
            return Mathf.Abs(value.x) <= Bounds && Mathf.Abs(value.y) <= Bounds && Mathf.Abs(value.z) <= Bounds;
        }

        internal static bool IsRotation(Quaternion value)
        {
            float length = Quaternion.Dot(value, value);
            return length >= 0.98f && length <= 1.02f;
        }

        private static bool IsFinite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        private static bool UnitValue(float value) => value >= 0f && value <= 1f;
    }
}
