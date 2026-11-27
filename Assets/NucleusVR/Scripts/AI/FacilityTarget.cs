using UnityEngine;

namespace NucleusVR.AI
{
    internal readonly struct FacilityTarget
    {
        public readonly ulong Id;
        public readonly Vector3 Position;

        public FacilityTarget(ulong id, Vector3 position)
        {
            Id = id;
            Position = position;
        }
    }
}
