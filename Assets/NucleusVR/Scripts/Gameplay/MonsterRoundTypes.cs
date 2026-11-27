using System;
using System.Collections.Generic;

namespace NucleusVR.Gameplay
{
    public enum MonsterMap : byte
    {
        Backrooms = 0,
        Factory = 1,
        Mines = 2
    }

    public enum MonsterRoundPhase : byte
    {
        Waiting,
        Voting,
        Active,
        Finished
    }

    public enum MonsterRole : byte
    {
        Spectator,
        Survivor,
        Monster,
        Captured,
        Escaped
    }

    public enum MonsterRoundOutcome : byte
    {
        None,
        SurvivorsWon,
        MonsterWon,
        Aborted
    }

    public enum MonsterRoundEndReason : byte
    {
        None,
        AllSurvivorsResolved,
        TimerExpired,
        MonsterDisconnected,
        InsufficientPlayers
    }

    public sealed class MonsterRoundSettings
    {
        public const int PlayerLimit = 10;

        public MonsterRoundSettings(int maxPlayers = PlayerLimit, int minimumPlayers = 2,
            double votingSeconds = 15, double roundSeconds = 180, double resultsSeconds = 10)
        {
            if (maxPlayers < 2 || maxPlayers > PlayerLimit)
                throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            if (minimumPlayers < 2 || minimumPlayers > maxPlayers)
                throw new ArgumentOutOfRangeException(nameof(minimumPlayers));

            MaxPlayers = maxPlayers;
            MinimumPlayers = minimumPlayers;
            VotingSeconds = PositiveTime(votingSeconds, nameof(votingSeconds));
            RoundSeconds = PositiveTime(roundSeconds, nameof(roundSeconds));
            ResultsSeconds = PositiveTime(resultsSeconds, nameof(resultsSeconds));
        }

        public int MaxPlayers { get; }
        public int MinimumPlayers { get; }
        public double VotingSeconds { get; }
        public double RoundSeconds { get; }
        public double ResultsSeconds { get; }

        private static double PositiveTime(double seconds, string name)
        {
            if (!double.IsFinite(seconds) || seconds <= 0)
                throw new ArgumentOutOfRangeException(name);
            return seconds;
        }
    }

    public readonly struct MonsterRoundPlayer
    {
        public MonsterRoundPlayer(ulong id, MonsterRole role, MonsterMap? vote)
        {
            Id = id;
            Role = role;
            Vote = vote;
        }

        public ulong Id { get; }
        public MonsterRole Role { get; }
        public MonsterMap? Vote { get; }
    }

    /// <summary>A complete view of one authoritative round for local or remote presentation.</summary>
    public sealed class MonsterRoundSnapshot
    {
        public MonsterRoundSnapshot(MonsterRoundPhase phase, MonsterMap map, MonsterRoundOutcome outcome,
            MonsterRoundEndReason endReason, double remainingSeconds, int completedObjectives, ulong? monsterId,
            uint roundNumber, uint revision, bool isPractice, int escapedCount, int capturedCount,
            MonsterRoundPlayer[] players)
        {
            if (players == null)
                throw new ArgumentNullException(nameof(players));

            Phase = phase;
            Map = map;
            Outcome = outcome;
            EndReason = endReason;
            RemainingSeconds = remainingSeconds;
            CompletedObjectives = completedObjectives;
            MonsterId = monsterId;
            RoundNumber = roundNumber;
            Revision = revision;
            IsPractice = isPractice;
            EscapedCount = escapedCount;
            CapturedCount = capturedCount;
            Players = Array.AsReadOnly((MonsterRoundPlayer[])players.Clone());
        }

        public MonsterRoundPhase Phase { get; }
        public MonsterMap Map { get; }
        public MonsterRoundOutcome Outcome { get; }
        public MonsterRoundEndReason EndReason { get; }
        public double RemainingSeconds { get; }
        public int CompletedObjectives { get; }
        public bool ExitUnlocked => CompletedObjectives == MonsterRound.ObjectiveCount;
        public ulong? MonsterId { get; }
        public uint RoundNumber { get; }
        public uint Revision { get; }
        public bool IsPractice { get; }
        public int EscapedCount { get; }
        public int CapturedCount { get; }
        public IReadOnlyList<MonsterRoundPlayer> Players { get; }

        public MonsterRole GetRole(ulong id)
        {
            foreach (MonsterRoundPlayer player in Players)
                if (player.Id == id)
                    return player.Role;
            return MonsterRole.Spectator;
        }
    }
}
