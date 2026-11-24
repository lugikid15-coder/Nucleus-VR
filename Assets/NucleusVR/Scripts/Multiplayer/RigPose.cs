using Unity.Netcode;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    // Positions and rotations of tracked points use the player's yaw-only locomotion frame.
    public struct RigPose : INetworkSerializable
    {
        private const int VectorBytes = 3 * sizeof(float);
        private const int RotationBytes = 4 * sizeof(float);

        public const int EncodedSize = sizeof(uint) + sizeof(float) + 4 * VectorBytes + 3 * RotationBytes + 3 * sizeof(byte);

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
        }

        public static bool IsNewer(uint incoming, uint previous)
        {
            return unchecked((int)(incoming - previous)) > 0;
        }
    }
}
