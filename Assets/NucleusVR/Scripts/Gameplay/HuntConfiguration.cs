using System;
using UnityEngine;

namespace NucleusVR.Gameplay
{
    [CreateAssetMenu(menuName = "Nucleus VR/Hunt configuration")]
    public sealed class HuntConfiguration : ScriptableObject
    {
        [Serializable]
        private struct MapSettings
        {
            public MonsterMap map;
            public string instructions;
            [Min(1)] public int eventInterval;
            [Min(1)] public int eventLength;
        }

        [SerializeField, Range(2, 10)] private int minimumPlayers = 2;
        [SerializeField, Min(1)] private float voteSeconds = 15;
        [SerializeField, Min(1)] private float roundSeconds = 180;
        [SerializeField, Min(1)] private float resultsSeconds = 10;
        [SerializeField] private MapSettings[] maps;

        internal MonsterRoundSettings RoundSettings => new(minimumPlayers: Mathf.Clamp(minimumPlayers, 2, 10),
            votingSeconds: Mathf.Max(1f, voteSeconds), roundSeconds: Mathf.Max(1f, roundSeconds),
            resultsSeconds: Mathf.Max(1f, resultsSeconds));

        internal string Instructions(MonsterMap map) => TryGet(map, out MapSettings config) &&
            !string.IsNullOrWhiteSpace(config.instructions) ? config.instructions : "Activate three stations and escape.";

        internal bool EventActive(MonsterMap map, double remaining)
        {
            if (remaining <= 18d)
                return true;
            if (!TryGet(map, out MapSettings config))
                return (int)remaining % 53 < 7;
            int interval = Mathf.Max(1, config.eventInterval);
            return (int)remaining % interval < Mathf.Clamp(config.eventLength, 1, interval);
        }

        private bool TryGet(MonsterMap map, out MapSettings config)
        {
            if (maps != null)
                foreach (MapSettings entry in maps)
                    if (entry.map == map)
                    {
                        config = entry;
                        return true;
                    }
            config = default;
            return false;
        }
    }
}
