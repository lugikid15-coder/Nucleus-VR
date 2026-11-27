using System;

namespace NucleusVR.Anatomy
{
    /// <summary>A complete, detached copy of the presentation state at one authority revision.</summary>
    public sealed class AnatomySnapshot
    {
        private readonly AnatomyRegionState[] regions;
        private readonly AnatomyOrganState[] organs;

        public uint Revision { get; }

        public AnatomySnapshot(uint revision, AnatomyRegionState[] regions, AnatomyOrganState[] organs)
        {
            if (regions == null || regions.Length != (int)AnatomyRegion.Count)
                throw new ArgumentException("Provide one state for each anatomy region.", nameof(regions));
            if (organs == null || organs.Length != (int)OrganKind.Count)
                throw new ArgumentException("Provide one state for each organ.", nameof(organs));

            Revision = revision;
            this.regions = (AnatomyRegionState[])regions.Clone();
            this.organs = (AnatomyOrganState[])organs.Clone();
        }

        internal AnatomySnapshot(AnatomyDamageState state)
        {
            Revision = state.Revision;
            regions = new AnatomyRegionState[(int)AnatomyRegion.Count];
            organs = new AnatomyOrganState[(int)OrganKind.Count];
            for (int index = 0; index < regions.Length; index++)
                regions[index] = state.GetRegion((AnatomyRegion)index);
            for (int index = 0; index < organs.Length; index++)
                organs[index] = state.GetOrgan((OrganKind)index);
        }

        public AnatomyRegionState GetRegion(AnatomyRegion region) => regions[AnatomyProfile.RegionIndex(region)];
        public AnatomyOrganState GetOrgan(OrganKind organ) => organs[AnatomyProfile.OrganIndex(organ)];
    }
}
