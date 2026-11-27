using System.Collections.Generic;

namespace NucleusVR.Gameplay
{
    /// <summary>Host-owned round rules. Reach, contact and movement are checked by the game before these actions.</summary>
    public sealed class MonsterRound
    {
        private sealed class Player
        {
            public Player(ulong id)
            {
                Id = id;
            }

            public ulong Id { get; }
            public MonsterRole Role;
            public MonsterMap? Vote;
            public uint LastMonsterRound;
        }

        public const int ObjectiveCount = 3;
        private const int MapCount = 3;

        private readonly List<Player> players = new(MonsterRoundSettings.PlayerLimit);
        private ulong? practicePlayer;
        private int firstTieChoice;

        public MonsterRound(MonsterRoundSettings settings = null)
        {
            Settings = settings ?? new MonsterRoundSettings();
        }

        public MonsterRoundSettings Settings { get; }
        public MonsterRoundPhase Phase { get; private set; }
        public MonsterMap Map { get; private set; }
        public MonsterRoundOutcome Outcome { get; private set; }
        public MonsterRoundEndReason EndReason { get; private set; }
        public double RemainingSeconds { get; private set; }
        public int CompletedObjectives { get; private set; }
        public bool ExitUnlocked => CompletedObjectives == ObjectiveCount;
        public ulong? MonsterId { get; private set; }
        public uint RoundNumber { get; private set; }
        public uint Revision { get; private set; }
        public bool IsPractice => practicePlayer.HasValue;
        public int EscapedCount { get; private set; }
        public int CapturedCount { get; private set; }
        public int PlayerCount => players.Count;

        public bool TryJoin(ulong id)
        {
            if (players.Count >= Settings.MaxPlayers || FindPlayer(id) != null)
                return false;

            players.Add(new Player(id));
            AdvanceRevision();
            return true;
        }

        public bool Leave(ulong id)
        {
            Player player = FindPlayer(id);
            if (player == null)
                return false;

            players.Remove(player);
            if (Phase == MonsterRoundPhase.Voting && !HasEnoughPlayers())
                Finish(MonsterRoundOutcome.Aborted, MonsterRoundEndReason.InsufficientPlayers);
            else if (Phase == MonsterRoundPhase.Active)
            {
                if (id == MonsterId)
                    Finish(MonsterRoundOutcome.SurvivorsWon, MonsterRoundEndReason.MonsterDisconnected);
                else if (id == practicePlayer)
                    Finish(MonsterRoundOutcome.Aborted, MonsterRoundEndReason.InsufficientPlayers);
                else if (player.Role == MonsterRole.Survivor)
                    ResolveIfNoSurvivorsRemain();
            }

            if (id == MonsterId)
                MonsterId = null;

            AdvanceRevision();
            return true;
        }

        public bool TryBeginVoting(bool practice = false)
        {
            if (Phase != MonsterRoundPhase.Waiting ||
                (practice ? players.Count != 1 : players.Count < Settings.MinimumPlayers))
                return false;

            practicePlayer = practice ? players[0].Id : null;
            Phase = MonsterRoundPhase.Voting;
            RemainingSeconds = Settings.VotingSeconds;
            AdvanceRevision();
            return true;
        }

        public bool TryVote(ulong id, MonsterMap map)
        {
            if (Phase != MonsterRoundPhase.Voting || (byte)map >= MapCount || !IsEligible(id))
                return false;

            Player player = FindPlayer(id);
            if (player == null || player.Vote == map)
                return false;

            player.Vote = map;
            AdvanceRevision();
            return true;
        }

        public bool TryActivateObjective(ulong id, int objectiveIndex)
        {
            if (Phase != MonsterRoundPhase.Active || GetRole(id) != MonsterRole.Survivor ||
                ExitUnlocked || objectiveIndex != CompletedObjectives)
                return false;

            CompletedObjectives++;
            AdvanceRevision();
            return true;
        }

        public bool TryEscape(ulong id)
        {
            if (Phase != MonsterRoundPhase.Active || !ExitUnlocked)
                return false;

            return ResolveSurvivor(id, MonsterRole.Escaped);
        }

        public bool TryCapture(ulong monsterId, ulong survivorId)
        {
            if (Phase != MonsterRoundPhase.Active || MonsterId != monsterId)
                return false;

            return ResolveSurvivor(survivorId, MonsterRole.Captured);
        }

        /// <summary>Advances countdowns using an elapsed, unscaled time delta. Returns true for a phase change.</summary>
        public bool Tick(double deltaSeconds)
        {
            if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0 || Phase == MonsterRoundPhase.Waiting)
                return false;

            bool transitioned = false;
            while (Phase != MonsterRoundPhase.Waiting && deltaSeconds >= RemainingSeconds)
            {
                deltaSeconds -= RemainingSeconds;
                if (Phase == MonsterRoundPhase.Voting)
                    StartRound();
                else if (Phase == MonsterRoundPhase.Active)
                    FinishOnTimeout();
                else
                    ClearRound();

                AdvanceRevision();
                transitioned = true;
            }

            if (Phase != MonsterRoundPhase.Waiting)
                RemainingSeconds -= deltaSeconds;
            return transitioned;
        }

        public void Reset(bool clearPlayers = false)
        {
            if (clearPlayers)
            {
                players.Clear();
                Map = MonsterMap.Backrooms;
                RoundNumber = 0;
                firstTieChoice = 0;
            }
            ClearRound();
            AdvanceRevision();
        }

        public MonsterRole GetRole(ulong id) => FindPlayer(id)?.Role ?? MonsterRole.Spectator;

        public int GetVoteCount(MonsterMap map)
        {
            if ((byte)map >= MapCount)
                return 0;

            int count = 0;
            foreach (Player player in players)
                if (IsEligible(player.Id) && player.Vote == map)
                    count++;
            return count;
        }

        public MonsterRoundSnapshot CaptureSnapshot()
        {
            var roster = new MonsterRoundPlayer[players.Count];
            for (int index = 0; index < roster.Length; index++)
            {
                Player player = players[index];
                roster[index] = new MonsterRoundPlayer(player.Id, player.Role, player.Vote);
            }

            return new MonsterRoundSnapshot(Phase, Map, Outcome, EndReason, RemainingSeconds, CompletedObjectives,
                MonsterId, RoundNumber, Revision, IsPractice, EscapedCount, CapturedCount, roster);
        }

        private bool ResolveSurvivor(ulong id, MonsterRole result)
        {
            Player player = FindPlayer(id);
            if (player == null || player.Role != MonsterRole.Survivor)
                return false;

            player.Role = result;
            if (result == MonsterRole.Escaped)
                EscapedCount++;
            else
                CapturedCount++;

            ResolveIfNoSurvivorsRemain();
            AdvanceRevision();
            return true;
        }

        private void StartRound()
        {
            if (!HasEnoughPlayers())
            {
                Finish(MonsterRoundOutcome.Aborted, MonsterRoundEndReason.InsufficientPlayers);
                return;
            }

            Map = SelectMap();
            firstTieChoice = ((int)Map + 1) % MapCount;
            RoundNumber = unchecked(RoundNumber + 1);
            Phase = MonsterRoundPhase.Active;
            RemainingSeconds = Settings.RoundSeconds;

            foreach (Player player in players)
                player.Role = IsEligible(player.Id) ? MonsterRole.Survivor : MonsterRole.Spectator;

            if (!IsPractice)
            {
                Player monster = PickMonster();
                monster.Role = MonsterRole.Monster;
                monster.LastMonsterRound = RoundNumber;
                MonsterId = monster.Id;
            }
        }

        private MonsterMap SelectMap()
        {
            MonsterMap selected = (MonsterMap)firstTieChoice;
            int bestVotes = -1;
            for (int offset = 0; offset < MapCount; offset++)
            {
                var option = (MonsterMap)((firstTieChoice + offset) % MapCount);
                int votes = GetVoteCount(option);
                if (votes <= bestVotes)
                    continue;

                selected = option;
                bestVotes = votes;
            }
            return selected;
        }

        private Player PickMonster()
        {
            Player leastRecent = players[0];
            foreach (Player player in players)
                if (player.LastMonsterRound < leastRecent.LastMonsterRound)
                    leastRecent = player;
            return leastRecent;
        }

        private void ResolveIfNoSurvivorsRemain()
        {
            foreach (Player player in players)
                if (player.Role == MonsterRole.Survivor)
                    return;

            Finish(OutcomeForEscapes(), MonsterRoundEndReason.AllSurvivorsResolved);
        }

        private void FinishOnTimeout()
        {
            foreach (Player player in players)
            {
                if (player.Role != MonsterRole.Survivor)
                    continue;

                player.Role = MonsterRole.Captured;
                CapturedCount++;
            }

            Finish(OutcomeForEscapes(), MonsterRoundEndReason.TimerExpired);
        }

        private MonsterRoundOutcome OutcomeForEscapes()
        {
            if (EscapedCount > 0)
                return MonsterRoundOutcome.SurvivorsWon;
            return IsPractice ? MonsterRoundOutcome.Aborted : MonsterRoundOutcome.MonsterWon;
        }

        private void Finish(MonsterRoundOutcome outcome, MonsterRoundEndReason reason)
        {
            Outcome = outcome;
            EndReason = reason;
            Phase = MonsterRoundPhase.Finished;
            RemainingSeconds = Settings.ResultsSeconds;
        }

        private void ClearRound()
        {
            Phase = MonsterRoundPhase.Waiting;
            Outcome = MonsterRoundOutcome.None;
            EndReason = MonsterRoundEndReason.None;
            CompletedObjectives = 0;
            EscapedCount = 0;
            CapturedCount = 0;
            RemainingSeconds = 0;
            MonsterId = null;
            practicePlayer = null;
            foreach (Player player in players)
            {
                player.Role = MonsterRole.Spectator;
                player.Vote = null;
            }
        }

        private bool HasEnoughPlayers() => IsPractice
            ? FindPlayer(practicePlayer.Value) != null
            : players.Count >= Settings.MinimumPlayers;

        private bool IsEligible(ulong id) => !IsPractice || id == practicePlayer;

        private Player FindPlayer(ulong id)
        {
            foreach (Player player in players)
                if (player.Id == id)
                    return player;
            return null;
        }

        private void AdvanceRevision() => Revision = unchecked(Revision + 1);
    }
}
