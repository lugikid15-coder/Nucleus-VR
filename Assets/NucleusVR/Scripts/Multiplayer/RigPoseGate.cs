using System;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    /// <summary>
    /// Server-side stream admission. The accepted pose is presentation state. Authoritative
    /// movement, map collision and hit validation will need their own simulation state.
    /// </summary>
    public sealed class RigPoseGate
    {
        private const float MaxWorldAxis = 1000f;
        private const float MaxRoomOffset = 12f;
        private const float MaxHandReach = 1.35f;
        private const float MaxOriginSpeed = 20f;
        private const float OriginBurst = 2.5f;
        private const float SamplesPerSecond = 30f;
        private const float SampleBurst = 4f;

        private double lastTime;
        private double lastAcceptedTime;
        private double sampleBudget = SampleBurst;
        private double originBudget = OriginBurst;

        public bool HasPose { get; private set; }
        public RigPose Latest { get; private set; }

        public bool TryAccept(RigPose pose, double serverTime)
        {
            if (!double.IsFinite(serverTime) || serverTime < lastTime)
                return false;

            double elapsed = serverTime - lastTime;
            lastTime = serverTime;
            sampleBudget = Math.Min(SampleBurst, sampleBudget + elapsed * SamplesPerSecond);
            originBudget = Math.Min(OriginBurst, originBudget + elapsed * MaxOriginSpeed);
            if (sampleBudget < 1)
                return false;

            // Malformed/duplicate attempts count against the sender's processing budget.
            sampleBudget -= 1;
            if ((HasPose && !RigPose.IsNewer(pose.Sequence, Latest.Sequence)) || !HasValidGeometry(pose))
                return false;

            if (HasPose)
            {
                float distance = Vector3.Distance(pose.Origin, Latest.Origin);
                double gap = serverTime - lastAcceptedTime;
                // A dropped packet should not strand the avatar at its previous position.
                // Use the accumulated movement budget during ordinary updates and allow
                // bounded catch-up after a longer silence in the accepted stream.
                double available = gap > 0.25
                    ? Math.Max(originBudget, MaxOriginSpeed * Math.Min(gap, 3))
                    : originBudget;
                if (distance > available)
                    return false;

                originBudget = Math.Max(0, originBudget - distance);
            }

            pose.HeadRotation = Quaternion.Normalize(pose.HeadRotation);
            pose.LeftRotation = Quaternion.Normalize(pose.LeftRotation);
            pose.RightRotation = Quaternion.Normalize(pose.RightRotation);
            Latest = pose;
            lastAcceptedTime = serverTime;
            HasPose = true;
            return true;
        }

        private static bool HasValidGeometry(RigPose pose)
        {
            return IsWithin(pose.Origin, MaxWorldAxis) &&
                   float.IsFinite(pose.Yaw) && pose.Yaw >= 0f && pose.Yaw <= 360f &&
                   IsWithin(pose.HeadPosition, MaxRoomOffset) &&
                   pose.HeadPosition.y >= -0.6f && pose.HeadPosition.y <= 3f &&
                   IsWithin(pose.LeftPosition, MaxRoomOffset + MaxHandReach) &&
                   IsWithin(pose.RightPosition, MaxRoomOffset + MaxHandReach) &&
                   (pose.LeftPosition - pose.HeadPosition).sqrMagnitude <= MaxHandReach * MaxHandReach &&
                   (pose.RightPosition - pose.HeadPosition).sqrMagnitude <= MaxHandReach * MaxHandReach &&
                   IsUnitRotation(pose.HeadRotation) &&
                   IsUnitRotation(pose.LeftRotation) &&
                   IsUnitRotation(pose.RightRotation);
        }

        private static bool IsWithin(Vector3 position, float limit)
        {
            // Infinities and NaNs fail the same comparison as other out-of-range values.
            return Mathf.Abs(position.x) <= limit &&
                   Mathf.Abs(position.y) <= limit &&
                   Mathf.Abs(position.z) <= limit;
        }

        private static bool IsUnitRotation(Quaternion rotation)
        {
            float squaredMagnitude = Quaternion.Dot(rotation, rotation);
            return squaredMagnitude >= 0.98f && squaredMagnitude <= 1.02f;
        }
    }
}
