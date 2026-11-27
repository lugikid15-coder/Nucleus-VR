using System;
using NucleusVR.Gameplay;
using NUnit.Framework;

namespace NucleusVR.Tests
{
    public sealed class MonsterRoundTests
    {
        [Test]
        public void EnforceRosterLimitAndPermitHostIdZero()
        {
            var round = new MonsterRound(new MonsterRoundSettings(maxPlayers: 2));

            Assert.That(round.TryJoin(0), Is.True);
            Assert.That(round.TryJoin(0), Is.False);
            Assert.That(round.TryBeginVoting(), Is.False);
            Assert.That(round.TryJoin(21), Is.True);
            Assert.That(round.TryJoin(22), Is.False);
            Assert.That(round.PlayerCount, Is.EqualTo(2));
            Assert.That(round.TryBeginVoting(), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonsterRoundSettings(maxPlayers: 11));
        }

        [Test]
        public void KeepOneEditableVotePerPlayerAndChooseTheMajority()
        {
            MonsterRound round = NewRound(4, 5, 6);
            Assert.That(round.TryVote(4, MonsterMap.Backrooms), Is.True);
            Assert.That(round.TryVote(4, MonsterMap.Backrooms), Is.False);
            Assert.That(round.TryVote(4, MonsterMap.Mines), Is.True);
            Assert.That(round.TryVote(5, MonsterMap.Mines), Is.True);
            Assert.That(round.TryVote(6, MonsterMap.Factory), Is.True);
            Assert.That(round.TryVote(25, MonsterMap.Factory), Is.False);
            Assert.That(round.TryVote(6, (MonsterMap)12), Is.False);
            Assert.That(round.GetVoteCount(MonsterMap.Backrooms), Is.Zero);
            Assert.That(round.GetVoteCount(MonsterMap.Mines), Is.EqualTo(2));

            Assert.That(round.Tick(1d), Is.False);
            Assert.That(round.Phase, Is.EqualTo(MonsterRoundPhase.Voting));
            Assert.That(round.Tick(1d), Is.True);
            Assert.That(round.Phase, Is.EqualTo(MonsterRoundPhase.Active));
            Assert.That(round.Map, Is.EqualTo(MonsterMap.Mines));
            Assert.That(round.TryVote(6, MonsterMap.Backrooms), Is.False);
        }

        [Test]
        public void RotateMonsterAndTiedMapsAcrossRounds()
        {
            MonsterRound round = NewRound(9, 5, 3);
            ulong[] expectedMonsters = { 9, 5, 3, 9 };
            MonsterMap[] expectedMaps =
                { MonsterMap.Backrooms, MonsterMap.Factory, MonsterMap.Mines, MonsterMap.Backrooms };

            for (int i = 0; i < expectedMonsters.Length; i++)
            {
                Assert.That(round.Tick(2d), Is.True);
                Assert.That(round.Map, Is.EqualTo(expectedMaps[i]));
                Assert.That(round.MonsterId, Is.EqualTo(expectedMonsters[i]));
                Assert.That(round.GetRole(expectedMonsters[i]), Is.EqualTo(MonsterRole.Monster));
                Assert.That(round.RoundNumber, Is.EqualTo((uint)i + 1));
                round.Reset();
                Assert.That(round.PlayerCount, Is.EqualTo(3));
                Assert.That(round.TryBeginVoting(), Is.True);
            }
        }

        [Test]
        public void RequireActiveSurvivorsAndOrderedObjectivesBeforeEscape()
        {
            MonsterRound round = NewRound(0, 8, 12);
            round.Tick(2d);

            Assert.That(round.TryActivateObjective(0, 0), Is.False);
            Assert.That(round.TryActivateObjective(8, 1), Is.False);
            Assert.That(round.TryEscape(8), Is.False);
            Assert.That(round.TryCapture(8, 12), Is.False);
            Assert.That(round.TryCapture(0, 0), Is.False);
            Assert.That(round.TryActivateObjective(8, 0), Is.True);
            Assert.That(round.TryActivateObjective(8, 0), Is.False);
            Assert.That(round.TryActivateObjective(12, 1), Is.True);
            Assert.That(round.TryActivateObjective(8, 2), Is.True);
            Assert.That(round.ExitUnlocked, Is.True);
            Assert.That(round.TryActivateObjective(12, 3), Is.False);
            Assert.That(round.TryEscape(8), Is.True);
            Assert.That(round.GetRole(8), Is.EqualTo(MonsterRole.Escaped));
            Assert.That(round.Phase, Is.EqualTo(MonsterRoundPhase.Active));
            Assert.That(round.TryCapture(0, 12), Is.True);
            Assert.That(round.GetRole(12), Is.EqualTo(MonsterRole.Captured));
            Assert.That(round.Phase, Is.EqualTo(MonsterRoundPhase.Finished));
            Assert.That(round.Outcome, Is.EqualTo(MonsterRoundOutcome.SurvivorsWon));
        }

        [Test]
        public void KeepLateJoinersOutUntilTheFollowingRound()
        {
            MonsterRound round = NewRound(1, 2);
            round.Tick(2d);

            Assert.That(round.TryJoin(3), Is.True);
            Assert.That(round.GetRole(3), Is.EqualTo(MonsterRole.Spectator));
            Assert.That(round.TryActivateObjective(3, 0), Is.False);
            Assert.That(round.TryCapture(1, 3), Is.False);
            Assert.That(round.TryCapture(1, 2), Is.True);
            Assert.That(round.Outcome, Is.EqualTo(MonsterRoundOutcome.MonsterWon));

            Assert.That(round.Tick(4d), Is.True);
            Assert.That(round.TryBeginVoting(), Is.True);
            round.Tick(2d);
            Assert.That(round.GetRole(3), Is.EqualTo(MonsterRole.Survivor));
        }

        [Test]
        public void CountdownUsesElapsedTimeAndReturnsToWaiting()
        {
            MonsterRound round = NewRound(1, 2);
            Assert.That(round.Tick(double.NaN), Is.False);
            Assert.That(round.Tick(-5d), Is.False);
            Assert.That(round.RemainingSeconds, Is.EqualTo(2d));

            Assert.That(round.Tick(34d), Is.True);
            Assert.That(round.Phase, Is.EqualTo(MonsterRoundPhase.Finished));
            Assert.That(round.RemainingSeconds, Is.EqualTo(2d));
            Assert.That(round.Outcome, Is.EqualTo(MonsterRoundOutcome.MonsterWon));
            Assert.That(round.EndReason, Is.EqualTo(MonsterRoundEndReason.TimerExpired));
            Assert.That(round.GetRole(2), Is.EqualTo(MonsterRole.Captured));

            round.Tick(2d);
            Assert.That(round.Phase, Is.EqualTo(MonsterRoundPhase.Waiting));
            Assert.That(round.RemainingSeconds, Is.Zero);
            Assert.That(round.GetRole(2), Is.EqualTo(MonsterRole.Spectator));
        }

        [Test]
        public void DisconnectCancelsUnderpopulatedVoteAndResolvesLostMonster()
        {
            MonsterRound round = NewRound(1, 2);

            Assert.That(round.Leave(2), Is.True);
            Assert.That(round.Phase, Is.EqualTo(MonsterRoundPhase.Finished));
            Assert.That(round.Outcome, Is.EqualTo(MonsterRoundOutcome.Aborted));
            Assert.That(round.EndReason, Is.EqualTo(MonsterRoundEndReason.InsufficientPlayers));

            round.Reset();
            Assert.That(round.TryJoin(3), Is.True);
            Assert.That(round.TryBeginVoting(), Is.True);
            round.Tick(2d);
            Assert.That(round.Leave(1), Is.True);
            Assert.That(round.Leave(1), Is.False);
            Assert.That(round.MonsterId, Is.Null);
            Assert.That(round.Outcome, Is.EqualTo(MonsterRoundOutcome.SurvivorsWon));
            Assert.That(round.EndReason, Is.EqualTo(MonsterRoundEndReason.MonsterDisconnected));
        }

        [Test]
        public void SinglePlayerPracticeUsesObjectivesWithoutAssigningAMonster()
        {
            var round = new MonsterRound(new MonsterRoundSettings(votingSeconds: 2d));
            Assert.That(round.TryJoin(0), Is.True);
            Assert.That(round.TryBeginVoting(practice: true), Is.True);
            Assert.That(round.TryVote(0, MonsterMap.Factory), Is.True);
            Assert.That(round.TryJoin(22), Is.True);
            Assert.That(round.TryVote(22, MonsterMap.Mines), Is.False);
            round.Tick(2d);

            Assert.That(round.IsPractice, Is.True);
            Assert.That(round.MonsterId, Is.Null);
            Assert.That(round.Map, Is.EqualTo(MonsterMap.Factory));
            Assert.That(round.GetRole(22), Is.EqualTo(MonsterRole.Spectator));
            for (int objective = 0; objective < MonsterRound.ObjectiveCount; objective++)
                Assert.That(round.TryActivateObjective(0, objective), Is.True);
            Assert.That(round.TryEscape(0), Is.True);
            Assert.That(round.Outcome, Is.EqualTo(MonsterRoundOutcome.SurvivorsWon));
        }

        [Test]
        public void SnapshotsPreserveTheRosterAndVotesOfTheirOwnRevision()
        {
            MonsterRound round = NewRound(0, 9);
            Assert.That(round.TryVote(9, MonsterMap.Factory), Is.True);
            MonsterRoundSnapshot voting = round.CaptureSnapshot();

            round.Tick(2d);
            Assert.That(round.TryCapture(0, 9), Is.True);
            MonsterRoundSnapshot finished = round.CaptureSnapshot();

            Assert.That(voting.Phase, Is.EqualTo(MonsterRoundPhase.Voting));
            Assert.That(voting.GetRole(9), Is.EqualTo(MonsterRole.Spectator));
            Assert.That(voting.Players[1].Vote, Is.EqualTo(MonsterMap.Factory));
            Assert.That(finished.Revision, Is.GreaterThan(voting.Revision));
            Assert.That(finished.GetRole(9), Is.EqualTo(MonsterRole.Captured));
            Assert.That(finished.CapturedCount, Is.EqualTo(1));
        }

        [Test]
        public void ResettingSessionDropsDepartedPlayersBeforeAnotherRoomStarts()
        {
            MonsterRound round = NewRound(0, 1, 2);
            round.Tick(2d);
            round.Reset(clearPlayers: true);

            Assert.That(round.PlayerCount, Is.Zero);
            Assert.That(round.MonsterId, Is.Null);
            Assert.That(round.RoundNumber, Is.Zero);
            Assert.That(round.TryJoin(0), Is.True);
            Assert.That(round.TryBeginVoting(), Is.False);
            Assert.That(round.TryJoin(90), Is.True);
            Assert.That(round.TryBeginVoting(), Is.True);
            round.Tick(2d);
            Assert.That(round.MonsterId, Is.EqualTo(0UL));
            Assert.That(round.GetRole(90), Is.EqualTo(MonsterRole.Survivor));
        }

        private static MonsterRound NewRound(params ulong[] ids)
        {
            var round = new MonsterRound(new MonsterRoundSettings(votingSeconds: 2d, roundSeconds: 30d, resultsSeconds: 4d));
            foreach (ulong id in ids)
                Assert.That(round.TryJoin(id), Is.True);
            Assert.That(round.TryBeginVoting(), Is.True);
            return round;
        }
    }
}
