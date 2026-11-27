using Unity.Netcode;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    // Positions and rotations of tracked points use the player's yaw-only locomotion frame.
    public struct RigPose : INetworkSerializable
    {
        private const int VectorBytes = 3 * sizeof(float);
        private const int RotationBytes = 4 * sizeof(float);

        public const int EncodedSize = sizeof(uint) + sizeof(float) + 4 * VectorBytes + 3 * RotationBytes + 4 * sizeof(byte);

        public uint Sequence;
        public Vector3 Origin;
        public float Yaw;
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;
        public Vector3 LeftPosition;
        public Quaternion LeftRotation;
        public Vector3 RightPosition;
        public Quaternion RightRotation;
        public byte LeftGrip;
        public byte RightGrip;
        public byte Jaw;
        public byte TrackedHands;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref HeadPosition);
            serializer.SerializeValue(ref HeadRotation);
            serializer.SerializeValue(ref LeftPosition);
            serializer.SerializeValue(ref LeftRotation);
            serializer.SerializeValue(ref RightPosition);
            serializer.SerializeValue(ref RightRotation);
            serializer.SerializeValue(ref LeftGrip);
            serializer.SerializeValue(ref RightGrip);
            serializer.SerializeValue(ref Jaw);
            serializer.SerializeValue(ref TrackedHands);
        }

        public bool IsHandTracked(bool left) => (TrackedHands & (left ? 1 : 2)) != 0;

        public Vector3 WorldHand(bool left)
        {
            return Origin + Quaternion.Euler(0f, Yaw, 0f) * (left ? LeftPosition : RightPosition);
        }

        public static bool IsNewer(uint incoming, uint previous)
        {
            return unchecked((int)(incoming - previous)) > 0;
        }
    }
}
