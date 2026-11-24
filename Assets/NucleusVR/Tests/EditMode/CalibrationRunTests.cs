using System;
using NucleusVR.Maps;
using NUnit.Framework;

namespace NucleusVR.Tests
{
    public sealed class CalibrationRunTests
    {
        [Test]
        public void RequireAtLeastOneCheckpoint()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CalibrationRun(0));
        }

        [Test]
        public void OnlyAcceptEachStationInOrder()
        {
            var run = new CalibrationRun(2);

            Assert.That(run.TryActivate(1, 1d), Is.False);
            Assert.That(run.TryActivate(0, 2d), Is.True);
            Assert.That(run.TryActivate(0, 3d), Is.False);
            Assert.That(run.TryActivate(2, 3d), Is.False);
            Assert.That(run.NextStation, Is.EqualTo(1));

            Assert.That(run.TryActivate(1, 4d), Is.True);
            Assert.That(run.TryActivate(2, 6.5d), Is.True);
            Assert.That(run.Completed, Is.True);
            Assert.That(run.InProgress, Is.False);
            Assert.That(run.ElapsedSeconds, Is.EqualTo(4.5d));
            Assert.That(run.TryActivate(3, 8d), Is.False);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void RejectNonfiniteTimeWithoutAdvancing(double seconds)
        {
            var run = new CalibrationRun(1);

            Assert.That(run.TryActivate(0, seconds), Is.False);
            Assert.That(run.TryActivate(0, 1d), Is.True);
            Assert.That(run.TryActivate(1, seconds), Is.False);
            Assert.That(run.NextStation, Is.EqualTo(1));
            Assert.That(run.TryActivate(1, 3d), Is.True);
        }

        [Test]
        public void RejectTimeEarlierThanTheMostRecentCheckpoint()
        {
            var run = new CalibrationRun(2);
            Assert.That(run.TryActivate(0, 1d), Is.True);
            Assert.That(run.TryActivate(1, 3d), Is.True);

            Assert.That(run.TryActivate(2, 2d), Is.False);
            Assert.That(run.Completed, Is.False);
            Assert.That(run.TryActivate(2, 4d), Is.True);
            Assert.That(run.ElapsedSeconds, Is.EqualTo(3d));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResetReturnsAStartedOrFinishedRunToItsInitialState(bool finishFirst)
        {
            var run = new CalibrationRun(1);
            Assert.That(run.TryActivate(0, 6d), Is.True);
            if (finishFirst)
                Assert.That(run.TryActivate(1, 8d), Is.True);

            run.Reset();

            Assert.That(run.NextStation, Is.Zero);
            Assert.That(run.InProgress, Is.False);
            Assert.That(run.Completed, Is.False);
            Assert.That(run.ElapsedSeconds, Is.Zero);
            Assert.That(run.TryActivate(0, 1d), Is.True);
            Assert.That(run.TryActivate(1, 2d), Is.True);
            Assert.That(run.ElapsedSeconds, Is.EqualTo(1d));
        }
    }
}
