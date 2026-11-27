using System;

namespace NucleusVR.Anatomy
{
    /// <summary>Authoritative damage accumulation for one fictional character.</summary>
    public sealed class AnatomyDamageState
    {
        private struct RegionDamage
        {
            public float Surface;
            public float Bone;
            public float Joint;
            public bool Severed;
        }

        private struct OrganDamage
        {
            public float Amount;
            public bool Detached;
        }

        private readonly RegionDamage[] regions = new RegionDamage[(int)AnatomyRegion.Count];
        private readonly OrganDamage[] organs = new OrganDamage[(int)OrganKind.Count];
        private readonly bool[] regionHasOrgans = new bool[(int)AnatomyRegion.Count];

        public AnatomyProfile Profile { get; }
        public uint Revision { get; private set; }

        public AnatomyDamageState(AnatomyProfile profile = null)
        {
            Profile = profile ?? AnatomyProfile.Default;
            for (int index = 0; index < organs.Length; index++)
                regionHasOrgans[(int)Profile.GetOrgan((OrganKind)index).Region] = true;
        }

        /// <summary>Applies absolute damage units against the capacity of the region and optional organ.</summary>
        public bool ApplyDamage(AnatomyRegion region, float amount, OrganKind? organ = null)
        {
            if (region >= AnatomyRegion.Count || !IsDamageAmount(amount))
                return false;
            if (organ.HasValue && !CanDamageOrgan(organ.Value, region))
                return false;

            ref RegionDamage target = ref regions[(int)region];
            AnatomyRegionDefinition definition = Profile.GetRegion(region);
            bool changed = !target.Severed && Increase(ref target.Surface, amount, definition.MaxDamage);

            if (organ.HasValue)
            {
                int index = (int)organ.Value;
                changed |= Increase(ref organs[index].Amount, amount, Profile.GetOrgan(organ.Value).MaxDamage);
            }

            if (!changed)
                return false;

            UpdateSeveredRegion(region, definition);
            AdvanceRevision();
            return true;
        }

        public bool ApplyFracture(AnatomyRegion region, float amount) => ApplyStructuralDamage(region, amount, false);
        public bool ApplyJointDamage(AnatomyRegion region, float amount) => ApplyStructuralDamage(region, amount, true);

        public bool TryDetachOrgan(OrganKind organ)
        {
            if (organ >= OrganKind.Count)
                return false;

            AnatomyOrganState state = GetOrgan(organ);
            if (state.IsDetached || !state.IsExposed)
                return false;

            organs[(int)organ].Detached = true;
            AdvanceRevision();
            return true;
        }

        public AnatomyRegionState GetRegion(AnatomyRegion region)
        {
            AnatomyRegionDefinition definition = Profile.GetRegion(region);
            RegionDamage target = regions[(int)region];

            float damage = target.Surface / definition.MaxDamage;
            float joint = target.Severed ? 1f : target.Joint / definition.StructuralCapacity;
            float bone = target.Bone / definition.StructuralCapacity;
            float structuralWear = Math.Max(damage, joint);
            if (structuralWear >= definition.FractureAt)
                bone = Math.Max(bone, structuralWear);

            float severity = Math.Max(structuralWear, bone);
            AnatomyStage stage = GetStage(target.Severed, severity, definition);
            float impairment = Math.Max(0f, severity - definition.WoundAt) / (1f - definition.WoundAt);

            return new AnatomyRegionState(damage, bone, joint, impairment, stage,
                GetVisibleLayers(stage, regionHasOrgans[(int)region]));
        }

        public AnatomyOrganState GetOrgan(OrganKind organ)
        {
            AnatomyOrganDefinition definition = Profile.GetOrgan(organ);
            OrganDamage target = organs[(int)organ];
            float damage = target.Amount / definition.MaxDamage;

            AnatomyOrganStage stage = AnatomyOrganStage.Intact;
            if (target.Detached)
                stage = AnatomyOrganStage.Detached;
            else if (damage >= definition.CriticalAt)
                stage = AnatomyOrganStage.Critical;
            else if (damage >= definition.DamageAt)
                stage = AnatomyOrganStage.Damaged;

            bool exposed = GetRegion(definition.Region).Stage >= AnatomyStage.Exposed;
            return new AnatomyOrganState(definition.Region, damage, stage, exposed);
        }

        public AnatomySnapshot CaptureSnapshot() => new AnatomySnapshot(this);

        public void Reset()
        {
            Array.Clear(regions, 0, regions.Length);
            Array.Clear(organs, 0, organs.Length);
            AdvanceRevision();
        }

        private bool ApplyStructuralDamage(AnatomyRegion region, float amount, bool toJoint)
        {
            if (region >= AnatomyRegion.Count || !IsDamageAmount(amount))
                return false;

            ref RegionDamage target = ref regions[(int)region];
            if (target.Severed)
                return false;

            AnatomyRegionDefinition definition = Profile.GetRegion(region);
            bool changed = toJoint
                ? Increase(ref target.Joint, amount, definition.StructuralCapacity)
                : Increase(ref target.Bone, amount, definition.StructuralCapacity);
            if (!changed)
                return false;

            UpdateSeveredRegion(region, definition);
            AdvanceRevision();
            return true;
        }

        private bool CanDamageOrgan(OrganKind organ, AnatomyRegion region) =>
            organ < OrganKind.Count && !organs[(int)organ].Detached && Profile.GetOrgan(organ).Region == region;

        private void UpdateSeveredRegion(AnatomyRegion region, AnatomyRegionDefinition definition)
        {
            ref RegionDamage target = ref regions[(int)region];
            if (target.Severed || !definition.CanSever)
                return;

            if (target.Surface / definition.MaxDamage < definition.SeverAt &&
                target.Joint / definition.StructuralCapacity < definition.SeverAt)
                return;

            target.Severed = true;
            // Removing a proximal arm disconnects the attached forearm in the same revision.
            if (region == AnatomyRegion.LeftUpperArm)
                regions[(int)AnatomyRegion.LeftForearm].Severed = true;
            else if (region == AnatomyRegion.RightUpperArm)
                regions[(int)AnatomyRegion.RightForearm].Severed = true;
        }

        private static AnatomyStage GetStage(bool severed, float severity, AnatomyRegionDefinition definition)
        {
            if (severed)
                return AnatomyStage.Severed;
            if (severity >= definition.FractureAt)
                return AnatomyStage.Fractured;
            if (severity >= definition.ExposureAt)
                return AnatomyStage.Exposed;
            if (severity >= definition.WoundAt)
                return AnatomyStage.Wounded;
            return AnatomyStage.Intact;
        }

        private static AnatomyLayers GetVisibleLayers(AnatomyStage stage, bool hasOrgans)
        {
            AnatomyLayers layers = AnatomyLayers.Skin;
            if (stage >= AnatomyStage.Wounded)
                layers |= AnatomyLayers.Muscles;
            if (stage >= AnatomyStage.Exposed)
            {
                layers |= AnatomyLayers.Veins | AnatomyLayers.Nerves | AnatomyLayers.InternalTissue;
                if (hasOrgans)
                    layers |= AnatomyLayers.Organs;
            }
            if (stage >= AnatomyStage.Fractured)
                layers |= AnatomyLayers.Bones;
            return layers;
        }

        private static bool IsDamageAmount(float amount) => float.IsFinite(amount) && amount > 0f;

        private static bool Increase(ref float value, float amount, float limit)
        {
            float next = Math.Min(limit, value + amount);
            if (next == value)
                return false;

            value = next;
            return true;
        }

        private void AdvanceRevision() => Revision = unchecked(Revision + 1);
    }
}
