using System;

namespace NucleusVR.Anatomy
{
    public readonly struct AnatomyRegionDefinition
    {
        public float MaxDamage { get; }
        public float StructuralCapacity { get; }
        public float WoundAt { get; }
        public float ExposureAt { get; }
        public float FractureAt { get; }
        public float SeverAt { get; }
        public bool CanSever { get; }

        public AnatomyRegionDefinition(float maxDamage, float woundAt = 0.2f, float exposureAt = 0.45f,
            float fractureAt = 0.7f, float severAt = 1f, bool canSever = true, float fractureResistance = 1f)
        {
            if (!float.IsFinite(maxDamage) || maxDamage <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxDamage));

            float structuralCapacity = maxDamage * fractureResistance;
            if (!float.IsFinite(structuralCapacity) || structuralCapacity <= 0f)
                throw new ArgumentOutOfRangeException(nameof(fractureResistance));

            if (!(0f < woundAt && woundAt < exposureAt && exposureAt < fractureAt &&
                  fractureAt < severAt && severAt <= 1f))
                throw new ArgumentException("Region thresholds must be strictly ordered within (0, 1].");

            MaxDamage = maxDamage;
            StructuralCapacity = structuralCapacity;
            WoundAt = woundAt;
            ExposureAt = exposureAt;
            FractureAt = fractureAt;
            SeverAt = severAt;
            CanSever = canSever;
        }
    }

    public readonly struct AnatomyOrganDefinition
    {
        public AnatomyRegion Region { get; }
        public float MaxDamage { get; }
        public float DamageAt { get; }
        public float CriticalAt { get; }

        public AnatomyOrganDefinition(AnatomyRegion region, float maxDamage,
            float damageAt = 0.2f, float criticalAt = 0.7f)
        {
            if (region >= AnatomyRegion.Count)
                throw new ArgumentOutOfRangeException(nameof(region));
            if (!float.IsFinite(maxDamage) || maxDamage <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxDamage));
            if (!(0f < damageAt && damageAt < criticalAt && criticalAt <= 1f))
                throw new ArgumentException("Organ damage thresholds must be strictly ordered within (0, 1].");

            Region = region;
            MaxDamage = maxDamage;
            DamageAt = damageAt;
            CriticalAt = criticalAt;
        }
    }

    /// <summary>Immutable, enum-indexed anatomy tuning. A profile can be shared by many subjects.</summary>
    public sealed class AnatomyProfile
    {
        private readonly AnatomyRegionDefinition[] regions;
        private readonly AnatomyOrganDefinition[] organs;

        public static AnatomyProfile Default { get; } = CreateScaled(1f);

        public AnatomyProfile(AnatomyRegionDefinition[] regionDefinitions, AnatomyOrganDefinition[] organDefinitions)
        {
            if (regionDefinitions == null || regionDefinitions.Length != (int)AnatomyRegion.Count)
                throw new ArgumentException("Provide one definition for each anatomy region.", nameof(regionDefinitions));
            if (organDefinitions == null || organDefinitions.Length != (int)OrganKind.Count)
                throw new ArgumentException("Provide one definition for each organ.", nameof(organDefinitions));

            regions = (AnatomyRegionDefinition[])regionDefinitions.Clone();
            organs = (AnatomyOrganDefinition[])organDefinitions.Clone();
            foreach (AnatomyRegionDefinition definition in regions)
                ValidateDefinition(definition);
            foreach (AnatomyOrganDefinition definition in organs)
                ValidateDefinition(definition);
        }

        public static AnatomyProfile CreateScaled(float durability, float regionFractureResistance = 1f,
            float organResistance = 1f)
        {
            ValidateScale(durability, nameof(durability));
            ValidateScale(regionFractureResistance, nameof(regionFractureResistance));
            ValidateScale(organResistance, nameof(organResistance));

            var regionDefinitions = new AnatomyRegionDefinition[(int)AnatomyRegion.Count];
            for (int index = 0; index < regionDefinitions.Length; index++)
                regionDefinitions[index] = new AnatomyRegionDefinition(100f * durability,
                    canSever: index != (int)AnatomyRegion.Torso, fractureResistance: regionFractureResistance);

            var organDefinitions = new AnatomyOrganDefinition[(int)OrganKind.Count];
            for (int index = 0; index < organDefinitions.Length; index++)
            {
                AnatomyRegion region = index == (int)OrganKind.Brain ? AnatomyRegion.Head : AnatomyRegion.Torso;
                organDefinitions[index] = new AnatomyOrganDefinition(region, 60f * durability * organResistance);
            }

            return new AnatomyProfile(regionDefinitions, organDefinitions);
        }

        public AnatomyRegionDefinition GetRegion(AnatomyRegion region) => regions[RegionIndex(region)];
        public AnatomyOrganDefinition GetOrgan(OrganKind organ) => organs[OrganIndex(organ)];

        public AnatomyProfile WithRegion(AnatomyRegion region, AnatomyRegionDefinition definition)
        {
            var overrides = (AnatomyRegionDefinition[])regions.Clone();
            overrides[RegionIndex(region)] = definition;
            return new AnatomyProfile(overrides, organs);
        }

        public AnatomyProfile WithOrgan(OrganKind organ, AnatomyOrganDefinition definition)
        {
            var overrides = (AnatomyOrganDefinition[])organs.Clone();
            overrides[OrganIndex(organ)] = definition;
            return new AnatomyProfile(regions, overrides);
        }

        internal static int RegionIndex(AnatomyRegion region) => region < AnatomyRegion.Count
            ? (int)region : throw new ArgumentOutOfRangeException(nameof(region));

        internal static int OrganIndex(OrganKind organ) => organ < OrganKind.Count
            ? (int)organ : throw new ArgumentOutOfRangeException(nameof(organ));

        private static void ValidateScale(float scale, string argument)
        {
            if (!float.IsFinite(scale) || scale <= 0f)
                throw new ArgumentOutOfRangeException(argument);
        }

        private static void ValidateDefinition(AnatomyRegionDefinition definition)
        {
            if (definition.MaxDamage <= 0f)
                throw new ArgumentException("Anatomy profiles require initialized region definitions.");
        }

        private static void ValidateDefinition(AnatomyOrganDefinition definition)
        {
            if (definition.MaxDamage <= 0f)
                throw new ArgumentException("Anatomy profiles require initialized organ definitions.");
        }
    }
}
