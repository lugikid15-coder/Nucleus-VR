using NucleusVR.Multiplayer;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NucleusVR.Tests
{
    public sealed class RigPoseTests
    {
        [Test]
        public void PoseRoundTripsInOneSmallTransportPayload()
        {
            RigPose original = ValidPose(83);
            original.LeftGrip = 216;
            original.RightGrip = 74;
            original.Jaw = 92;
            original.TrackedHands = 3;

            using var writer = new FastBufferWriter(128, Allocator.Temp);
            writer.WriteNetworkSerializable(original);
            using var reader = new FastBufferReader(writer, Allocator.None);
            reader.ReadNetworkSerializable(out RigPose copy);

            Assert.That(writer.Length, Is.EqualTo(RigPose.EncodedSize));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
            Assert.That(copy.Sequence, Is.EqualTo(original.Sequence));
            Assert.That(copy.Origin, Is.EqualTo(original.Origin));
            Assert.That(copy.Yaw, Is.EqualTo(original.Yaw));
            Assert.That(copy.HeadPosition, Is.EqualTo(original.HeadPosition));
            Assert.That(copy.HeadRotation, Is.EqualTo(original.HeadRotation));
            Assert.That(copy.LeftPosition, Is.EqualTo(original.LeftPosition));
            Assert.That(copy.LeftRotation, Is.EqualTo(original.LeftRotation));
            Assert.That(copy.RightPosition, Is.EqualTo(original.RightPosition));
            Assert.That(copy.RightRotation, Is.EqualTo(original.RightRotation));
            Assert.That(copy.LeftGrip, Is.EqualTo(original.LeftGrip));
            Assert.That(copy.RightGrip, Is.EqualTo(original.RightGrip));
            Assert.That(copy.Jaw, Is.EqualTo(original.Jaw));
            Assert.That(copy.TrackedHands, Is.EqualTo(original.TrackedHands));
        }

        [Test]
        public void RejectsDuplicateAndOutOfOrderPosesAcrossSequenceWrap()
        {
            var gate = new RigPoseGate();
            Assert.That(gate.TryAccept(ValidPose(uint.MaxValue), 1.00), Is.True);
            Assert.That(gate.TryAccept(ValidPose(0), 1.05), Is.True);
            Assert.That(gate.TryAccept(ValidPose(uint.MaxValue), 1.10), Is.False);
            Assert.That(gate.TryAccept(ValidPose(0), 1.15), Is.False);
            Assert.That(gate.TryAccept(ValidPose(1), 1.20), Is.True);
        }

        [Test]
        public void RejectsInvalidGeometryWithoutReplacingTheAcceptedPose()
        {
            var gate = new RigPoseGate();
            Assert.That(gate.TryAccept(ValidPose(12), 1.00), Is.True);
            RigPose invalid = ValidPose(13);

            invalid.Origin.x = float.NaN;
            Assert.That(gate.TryAccept(invalid, 1.05), Is.False);
            invalid = ValidPose(13);
            invalid.RightPosition += Vector3.right * 3f;
            Assert.That(gate.TryAccept(invalid, 1.10), Is.False);
            invalid = ValidPose(13);
            invalid.HeadRotation = default;
            Assert.That(gate.TryAccept(invalid, 1.15), Is.False);

            Assert.That(gate.Latest.Sequence, Is.EqualTo(12u));
            Assert.That(gate.TryAccept(ValidPose(13), 1.20), Is.True);
        }

        [Test]
        public void AllowsCrouchedTrackingAndNormalizesNearUnitRotations()
        {
            var gate = new RigPoseGate();
            RigPose crouched = ValidPose(1);
            crouched.HeadPosition = new Vector3(1f, 0.44f, 0.8f);
            crouched.LeftPosition = new Vector3(0.66f, 0.21f, 0.85f);
            crouched.RightPosition = new Vector3(1.34f, 0.21f, 0.85f);
            crouched.HeadRotation = new Quaternion(0f, 0f, 0f, 0.995f);

            Assert.That(gate.TryAccept(crouched, 0), Is.True);
            Assert.That(gate.Latest.HeadRotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void RecoversAfterLossButRejectsSuddenLargeOriginChanges()
        {
            var gate = new RigPoseGate();
            RigPose initial = ValidPose(1);
            RigPose next = ValidPose(2);
            next.Origin = initial.Origin + Vector3.forward * 8f;

            Assert.That(gate.TryAccept(initial, 0), Is.True);
            Assert.That(gate.TryAccept(next, 0.05), Is.False);
            Assert.That(gate.TryAccept(next, 0.5), Is.True);

            next.Sequence++;
            next.Origin += Vector3.forward * 8f;
            Assert.That(gate.TryAccept(next, 0.55), Is.False);
            next.Origin = gate.Latest.Origin + Vector3.forward * 0.6f;
            Assert.That(gate.TryAccept(next, 0.55), Is.True);
        }

        [Test]
        public void RateLimitsBurstAndRecoversAtTheNextInterval()
        {
            var gate = new RigPoseGate();
            for (uint sequence = 1; sequence <= 4; sequence++)
                Assert.That(gate.TryAccept(ValidPose(sequence), 1.0), Is.True);

            Assert.That(gate.TryAccept(ValidPose(5), 1.0), Is.False);
            Assert.That(gate.TryAccept(ValidPose(5), 1.05), Is.True);
        }

        [Test]
        public void ClockFailuresDoNotCorruptTheStream()
        {
            var gate = new RigPoseGate();
            Assert.That(gate.TryAccept(ValidPose(1), 1.0), Is.True);
            Assert.That(gate.TryAccept(ValidPose(2), 0.9), Is.False);
            Assert.That(gate.TryAccept(ValidPose(2), double.PositiveInfinity), Is.False);
            Assert.That(gate.TryAccept(ValidPose(2), 1.1), Is.True);
        }

        [Test]
        public void AuthoritativeWarpWaitsForANewPoseNearItsDestination()
        {
            var gate = new RigPoseGate();
            Assert.That(gate.TryAccept(ValidPose(1), 1.0), Is.True);
            var destination = new Vector3(220f, 0f, -90f);
            Assert.That(gate.AuthorizeWarp(destination, 1.05), Is.True);
            Assert.That(gate.IsAwaitingWarp(1.05), Is.True);
            Assert.That(gate.TryAccept(ValidPose(2), 1.10), Is.False);

            RigPose arriving = ValidPose(3);
            arriving.Origin = destination + Vector3.right * 0.6f;
            Assert.That(gate.TryAccept(arriving, 1.15), Is.True);
            Assert.That(gate.IsAwaitingWarp(1.16), Is.False);
            Assert.That(gate.Latest.Origin, Is.EqualTo(arriving.Origin));
            Assert.That(gate.TryAccept(ValidPose(4), 1.20), Is.False, "A completed warp also accepted the old origin");
        }

        [Test]
        public void ExpiredWarpReturnsToOrdinaryMovementAdmission()
        {
            var gate = new RigPoseGate();
            Assert.That(gate.TryAccept(ValidPose(1), 1.0), Is.True);
            var destination = new Vector3(240f, 0f, 90f);
            Assert.That(gate.AuthorizeWarp(new Vector3(float.NaN, 0f, 0f), 1.04), Is.False);
            Assert.That(gate.AuthorizeWarp(destination, 1.05), Is.True);
            Assert.That(gate.IsAwaitingWarp(11.06), Is.False);

            RigPose arriving = ValidPose(2);
            arriving.Origin = destination;
            Assert.That(gate.TryAccept(arriving, 11.10), Is.False);
            Assert.That(gate.TryAccept(ValidPose(3), 11.15), Is.True);
        }

        private static RigPose ValidPose(uint sequence)
        {
            return new RigPose
            {
                Sequence = sequence,
                Origin = new Vector3(2f, 0f, -8f),
                Yaw = 245f,
                HeadPosition = new Vector3(0f, 1.6f, 0f),
                HeadRotation = Quaternion.Euler(5f, 20f, 0f),
                LeftPosition = new Vector3(-0.4f, 1.1f, 0.25f),
                LeftRotation = Quaternion.Euler(0f, 10f, -75f),
                RightPosition = new Vector3(0.38f, 1.2f, 0.4f),
                RightRotation = Quaternion.Euler(0f, 15f, 55f)
            };
        }
    }
}
