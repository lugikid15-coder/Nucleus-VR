using UnityEngine;

namespace NucleusVR.Secrets
{
    public static class FacilityDiscoveryStore
    {
        private const string DiscoveriesKey = "NucleusVR.FacilityDiscoveries";

        public static uint Load() => (uint)PlayerPrefs.GetInt(DiscoveriesKey) & FacilitySecrets.KnownMask;

        public static void Record(uint validatedReward)
        {
            uint previous = Load();
            uint updated = previous | (validatedReward & FacilitySecrets.KnownMask);
            if (updated == previous)
                return;

            PlayerPrefs.SetInt(DiscoveriesKey, (int)updated);
            PlayerPrefs.Save();
        }
    }
}
