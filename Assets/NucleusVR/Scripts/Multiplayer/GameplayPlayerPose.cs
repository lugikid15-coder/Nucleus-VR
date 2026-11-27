using UnityEngine;

namespace NucleusVR.Multiplayer
{
    public readonly struct GameplayPlayerPose
    {
        public Vector3 Origin { get; }
        public float Yaw { get; }
        public Vector3 Head { get; }
        public Quaternion HeadRotation { get; }
        public Vector3 LeftHand { get; }
        public Quaternion LeftHandRotation { get; }
        public Vector3 RightHand { get; }
        public Quaternion RightHandRotation { get; }
        public bool LeftTracked { get; }
        public bool RightTracked { get; }
        public float LeftGrip { get; }
        public float RightGrip { get; }

        internal GameplayPlayerPose(RigPose pose)
        {
            Origin = pose.Origin;
            Yaw = pose.Yaw;
            Quaternion worldYaw = Quaternion.Euler(0f, Yaw, 0f);
            Head = Origin + worldYaw * pose.HeadPosition;
            HeadRotation = worldYaw * pose.HeadRotation;
            LeftHand = Origin + worldYaw * pose.LeftPosition;
            LeftHandRotation = worldYaw * pose.LeftRotation;
            RightHand = Origin + worldYaw * pose.RightPosition;
            RightHandRotation = worldYaw * pose.RightRotation;
            LeftTracked = pose.IsHandTracked(true);
            RightTracked = pose.IsHandTracked(false);
            LeftGrip = LeftTracked ? pose.LeftGrip / 255f : 0f;
            RightGrip = RightTracked ? pose.RightGrip / 255f : 0f;
        }
    }
}
