using NucleusVR.Secrets;
using NUnit.Framework;

namespace NucleusVR.Tests
{
    public sealed class FacilitySecretsTests
    {
        [Test]
        public void CoreNeedsEveryFragmentAndOrderedCircuitBeforePickup()
        {
            var secrets = new FacilitySecrets();
            Use(secrets, 105, 107, 101, 101, 103);
            Assert.That(secrets.PuzzleBits, Is.EqualTo(0b101u));
            Assert.That(secrets.Progress, Is.Zero);

            Use(secrets, 102, 105, 106);
            Assert.That((secrets.PuzzleBits >> 3) & 0b11u, Is.Zero);
            Use(secrets, 105, 104, 106, 105);
            Assert.That(secrets.PuzzleBits, Is.EqualTo(0b11111u));
            Assert.That(secrets.Progress, Is.Zero);

            Use(secrets, 107);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.GoldenCore));
            Assert.That(secrets.Progress, Is.EqualTo(FacilitySecrets.GoldenCore));
        }

        [Test]
        public void RadioAndTerminalOnlyUnlockAtTheirFinalSteps()
        {
            var secrets = new FacilitySecrets();
            Use(secrets, 110, 114, 117, 118);
            Assert.That(secrets.Progress, Is.Zero);
            Assert.That(secrets.LastReward, Is.Zero);

            Use(secrets, 113, 113);
            Assert.That((secrets.PuzzleBits >> 6) & 0b11u, Is.EqualTo(2u));
            Assert.That(secrets.Progress, Is.Zero);
            Use(secrets, 113);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.SecretRadio));
            Use(secrets, 110);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.Room404));

            Use(secrets, 116, 118, 116, 117);
            Assert.That((secrets.PuzzleBits >> 9) & 0b11u, Is.EqualTo(2u));
            Assert.That(secrets.Progress & FacilitySecrets.StrangeComputer, Is.Zero);
            Use(secrets, 118);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.StrangeComputer));
            Use(secrets, 114);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.DeveloperRoom));
        }

        [Test]
        public void BossAccessNeedsTranscriptAndStaffPassAndPickupNeedsDefeat()
        {
            var secrets = new FacilitySecrets();
            Assert.That(secrets.CompleteBoss(), Is.False);

            Use(secrets, 113, 113, 113, 110, 115);
            Use(secrets, 116, 117, 118, 114, 115);
            Assert.That(secrets.Progress & FacilitySecrets.HiddenBoss, Is.Zero);
            Use(secrets, 111, 115);
            Assert.That(secrets.PuzzleBits & (1u << 11), Is.Not.Zero);
            Assert.That(secrets.Progress & FacilitySecrets.HiddenBoss, Is.Not.Zero);
            Assert.That(secrets.LastReward, Is.Zero);
            Use(secrets, 115);
            Assert.That(secrets.LastReward, Is.Zero);

            Assert.That(secrets.CompleteBoss(), Is.True);
            Assert.That(secrets.CompleteBoss(), Is.False);
            Assert.That(secrets.PuzzleBits & (1u << 8), Is.Not.Zero);
            Assert.That(secrets.LastReward, Is.Zero);
            Use(secrets, 115);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.HiddenBoss));
        }

        [Test]
        public void DiscoveryStationsAllowPerPlayerClaimsWithoutLeakingPreviousReward()
        {
            var secrets = new FacilitySecrets();
            Use(secrets, 112);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.TinyCreature));
            Use(secrets, 119);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.ForgottenExperiment));
            Use(secrets, 112);
            Assert.That(secrets.LastReward, Is.EqualTo(FacilitySecrets.TinyCreature));

            Assert.That(secrets.Use(120, out string feedback), Is.False);
            Assert.That(feedback, Is.Empty);
            Assert.That(secrets.LastReward, Is.Zero);
            Use(secrets, 110);
            Assert.That(secrets.LastReward, Is.Zero);
            Assert.That(secrets.Progress, Is.EqualTo(FacilitySecrets.TinyCreature | FacilitySecrets.ForgottenExperiment));
        }

        private static void Use(FacilitySecrets secrets, params ushort[] stations)
        {
            foreach (ushort station in stations)
            {
                Assert.That(secrets.Use(station, out string feedback), Is.True, $"Station {station}");
                Assert.That(feedback, Is.Not.Empty, $"Station {station}");
            }
        }
    }
}
