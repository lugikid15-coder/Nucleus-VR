using NucleusVR.Anatomy;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    public enum AnatomyAction : byte
    {
        Damage,
        Fracture,
        Detach,
        Grab,
        Release,
        Reset
    }

    public readonly struct AnatomyTarget
    {
        public bool IsOrgan { get; }
        public byte Index { get; }
        public AnatomyRegion Region => (AnatomyRegion)Index;
        public OrganKind Organ => (OrganKind)Index;

        internal AnatomyTarget(bool isOrgan, byte index)
        {
            IsOrgan = isOrgan;
            Index = index;
        }

        public static AnatomyTarget ForRegion(AnatomyRegion region) => new(false, (byte)region);
        public static AnatomyTarget ForOrgan(OrganKind organ) => new(true, (byte)organ);

        internal bool IsValid => IsOrgan
            ? Index < (byte)OrganKind.Count
            : Index < (byte)AnatomyRegion.Count;
    }

    public readonly struct AnatomyImpact
    {
        public ushort SubjectId { get; }
        public AnatomyAction Action { get; }
        public AnatomyTarget Target { get; }
        public Vector3 Point { get; }
        public Vector3 Direction { get; }

        internal AnatomyImpact(ushort subjectId, AnatomyAction action, AnatomyTarget target, Vector3 point, Vector3 direction)
        {
            SubjectId = subjectId;
            Action = action;
            Target = target;
            Point = point;
            Direction = direction;
        }
    }
}
