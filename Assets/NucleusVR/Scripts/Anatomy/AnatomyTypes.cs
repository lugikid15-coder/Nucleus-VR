using System;

namespace NucleusVR.Anatomy
{
    public enum AnatomyRegion : byte
    {
        Head = 0,
        Torso = 1,
        LeftUpperArm = 2,
        LeftForearm = 3,
        RightUpperArm = 4,
        RightForearm = 5,
        Count = 6
    }

    public enum OrganKind : byte
    {
        Brain = 0,
        Heart = 1,
        Lungs = 2,
        Liver = 3,
        Stomach = 4,
        Intestines = 5,
        Kidneys = 6,
        Count = 7
    }

    public enum AnatomyStage : byte
    {
        Intact = 0,
        Wounded = 1,
        Exposed = 2,
        Fractured = 3,
        Severed = 4
    }

    public enum AnatomyOrganStage : byte
    {
        Intact = 0,
        Damaged = 1,
        Critical = 2,
        Detached = 3
    }

    [Flags]
    public enum AnatomyLayers : byte
    {
        None = 0,
        Skin = 1 << 0,
        Muscles = 1 << 1,
        Bones = 1 << 2,
        Veins = 1 << 3,
        Nerves = 1 << 4,
        Organs = 1 << 5,
        InternalTissue = 1 << 6,
        All = Skin | Muscles | Bones | Veins | Nerves | Organs | InternalTissue
    }

    public readonly struct AnatomyRegionState
    {
        public float Damage { get; }
        public float Fracture { get; }
        public float JointDamage { get; }
        public float Impairment { get; }
        public AnatomyStage Stage { get; }
        public AnatomyLayers VisibleLayers { get; }

        public AnatomyRegionState(float damage, float fracture, float jointDamage, float impairment,
            AnatomyStage stage, AnatomyLayers visibleLayers)
        {
            Damage = damage;
            Fracture = fracture;
            JointDamage = jointDamage;
            Impairment = impairment;
            Stage = stage;
            VisibleLayers = visibleLayers;
        }
    }

    public readonly struct AnatomyOrganState
    {
        public AnatomyRegion Region { get; }
        public float Damage { get; }
        public AnatomyOrganStage Stage { get; }
        public bool IsExposed { get; }
        public bool IsDetached => Stage == AnatomyOrganStage.Detached;

        public AnatomyOrganState(AnatomyRegion region, float damage, AnatomyOrganStage stage, bool isExposed)
        {
            Region = region;
            Damage = damage;
            Stage = stage;
            IsExposed = isExposed;
        }
    }
}
