using NucleusVR.Player;
using Unity.Netcode;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRig : NetworkBehaviour
    {
        [SerializeField] private Transform remoteOrigin;
        [SerializeField] private Transform remoteHead;
        [SerializeField] private Transform remoteLeftHand;
        [SerializeField] private Transform remoteRightHand;
        [SerializeField] private GameObject remoteVisualRoot;
        [SerializeField] private GorillaAvatar avatar;
        [SerializeField] private Shader avatarShader;
        [SerializeField, Range(10, 25)] private int samplesPerSecond = 20;
        [SerializeField, Min(0.001f)] private float interpolationHalfLife = 0.04f;

        private DirectSession session;
        private RigPoseGate serverGate;
        private RigPose target;
        private RigPose displayed;
        private bool hasTarget;
        private bool hasDisplay;
        private double nextSend;
        private uint sequence;
        private Vector3 leftVelocity;
        private Vector3 rightVelocity;
        internal bool HuntMonster { private get; set; }

        public void Configure(
            Transform origin,
            Transform head,
            Transform leftHand,
            Transform rightHand,
            GameObject visuals,
            GorillaAvatar remoteAvatar = null)
        {
            remoteOrigin = origin;
            remoteHead = head;
            remoteLeftHand = leftHand;
            remoteRightHand = rightHand;
            remoteVisualRoot = visuals;
            avatar = remoteAvatar;
        }

        private void Awake()
        {
            if (remoteVisualRoot != null)
            {
                if (avatar == null)
                    avatar = remoteVisualRoot.GetComponentInChildren<GorillaAvatar>(true);

                remoteVisualRoot.SetActive(false);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                serverGate = new RigPoseGate();

            if (IsOwner)
                session = NetworkManager.GetComponent<DirectSession>();
        }

        public override void OnNetworkDespawn()
        {
            hasTarget = false;
            hasDisplay = false;
            nextSend = 0;
            sequence = 0;
            leftVelocity = rightVelocity = Vector3.zero;
            serverGate = null;
            session = null;
            HuntMonster = false;
            if (remoteVisualRoot != null)
                remoteVisualRoot.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
            {
                PublishLocalPose();
                return;
            }

            if (!hasTarget || remoteOrigin == null || remoteHead == null ||
                remoteLeftHand == null || remoteRightHand == null)
                return;

            if (!hasDisplay)
            {
                displayed = target;
                hasDisplay = true;
            }
            else
            {
                float alpha = 1f - Mathf.Pow(0.5f, Time.unscaledDeltaTime / Mathf.Max(0.001f, interpolationHalfLife));
                displayed.Origin = Vector3.Lerp(displayed.Origin, target.Origin, alpha);
                displayed.Yaw = Mathf.LerpAngle(displayed.Yaw, target.Yaw, alpha);
                displayed.HeadPosition = Vector3.Lerp(displayed.HeadPosition, target.HeadPosition, alpha);
                displayed.HeadRotation = Quaternion.Slerp(displayed.HeadRotation, target.HeadRotation, alpha);
                displayed.LeftPosition = Vector3.Lerp(displayed.LeftPosition, target.LeftPosition, alpha);
                displayed.LeftRotation = Quaternion.Slerp(displayed.LeftRotation, target.LeftRotation, alpha);
                displayed.RightPosition = Vector3.Lerp(displayed.RightPosition, target.RightPosition, alpha);
                displayed.RightRotation = Quaternion.Slerp(displayed.RightRotation, target.RightRotation, alpha);
            }

            ApplyDisplayPose();
        }

        private void PublishLocalPose()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (session == null || now < nextSend)
                return;

            double period = 1.0 / Mathf.Clamp(samplesPerSecond, 10, 25);
            nextSend = nextSend + period > now ? nextSend + period : now + period;
            if (session.TryCapturePose(++sequence, out RigPose pose))
                session.SendPose(this, pose);
        }

        private void ApplyDisplayPose()
        {
            Quaternion yawRotation = Quaternion.Euler(0f, displayed.Yaw, 0f);
            remoteOrigin.SetPositionAndRotation(displayed.Origin, yawRotation);
            remoteHead.SetPositionAndRotation(displayed.Origin + yawRotation * displayed.HeadPosition,
                yawRotation * displayed.HeadRotation);
            remoteLeftHand.SetPositionAndRotation(displayed.Origin + yawRotation * displayed.LeftPosition,
                yawRotation * displayed.LeftRotation);
            remoteRightHand.SetPositionAndRotation(displayed.Origin + yawRotation * displayed.RightPosition,
                yawRotation * displayed.RightRotation);

            if (remoteVisualRoot != null && !remoteVisualRoot.activeSelf)
            {
                // An authored rig can use a baked model. The bootstrap prefab may provide
                // only the pose targets and shader, which builds the same model on demand.
                if (avatarShader != null)
                {
                    if (avatar == null)
                        avatar = remoteVisualRoot.AddComponent<GorillaAvatar>();

                    if (!avatar.HasModel)
                        GorillaAvatarFactory.Build(avatar, remoteHead, remoteLeftHand, remoteRightHand, false, avatarShader);
                }

                remoteVisualRoot.SetActive(true);
            }

            if (avatar != null)
            {
                avatar.SetTrackingAvailability(target.IsHandTracked(true), target.IsHandTracked(false));
                avatar.SetExpression(target.LeftGrip / 255f, target.RightGrip / 255f, target.Jaw / 255f);
                avatar.SetHuntAppearance(HuntMonster);
            }
        }

        internal bool TryAcceptServerPose(RigPose pose, out RigPose accepted)
        {
            accepted = default;
            if (serverGate == null)
                return false;

            double now = Time.realtimeSinceStartupAsDouble;
            bool hadPose = serverGate.HasPose && !serverGate.IsAwaitingWarp(now);
            RigPose prior = serverGate.Latest;
            double elapsed = now - serverGate.LastAcceptedTime;
            if (!serverGate.TryAccept(pose, now))
                return false;

            accepted = serverGate.Latest;
            leftVelocity = TrackVelocity(hadPose, prior, accepted, true, elapsed, leftVelocity);
            rightVelocity = TrackVelocity(hadPose, prior, accepted, false, elapsed, rightVelocity);
            QueuePose(accepted);
            return true;
        }

        internal bool TryGetAcceptedHand(bool left, double now, out Vector3 point, out Vector3 velocity)
        {
            point = velocity = Vector3.zero;
            if (!TryGetAcceptedPose(now, out RigPose pose) || !pose.IsHandTracked(left))
                return false;

            point = pose.WorldHand(left);
            velocity = left ? leftVelocity : rightVelocity;
            return true;
        }

        internal bool TryGetAcceptedPose(double now, out RigPose pose)
        {
            pose = default;
            if (serverGate == null || !serverGate.HasPose || serverGate.IsAwaitingWarp(now) ||
                now - serverGate.LastAcceptedTime > 0.35)
                return false;

            pose = serverGate.Latest;
            return true;
        }

        internal bool AuthorizeWarp(Vector3 destination, double now)
        {
            if (serverGate == null || !serverGate.AuthorizeWarp(destination, now))
                return false;

            leftVelocity = rightVelocity = Vector3.zero;
            return true;
        }

        private static Vector3 TrackVelocity(bool hadPose, RigPose prior, RigPose current,
            bool left, double elapsed, Vector3 previous)
        {
            if (!hadPose || !prior.IsHandTracked(left) || !current.IsHandTracked(left) || elapsed > 0.25)
                return Vector3.zero;

            float interval = Mathf.Max(0.02f, (float)elapsed);
            Vector3 measured = (current.WorldHand(left) - prior.WorldHand(left)) / interval;
            return Vector3.Lerp(previous, Vector3.ClampMagnitude(measured, 8f), 0.55f);
        }

        // NGO includes this snapshot with the initial object for late joiners.
        protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            bool synchronizedPose = serializer.IsWriter && serverGate != null && serverGate.HasPose;
            serializer.SerializeValue(ref synchronizedPose);
            if (!synchronizedPose)
                return;

            RigPose pose = serializer.IsWriter ? serverGate.Latest : default;
            serializer.SerializeValue(ref pose);
            if (serializer.IsReader)
            {
                target = pose;
                hasTarget = true;
            }
        }

        internal void QueuePose(RigPose pose)
        {
            if (IsOwner || (hasTarget && !RigPose.IsNewer(pose.Sequence, target.Sequence)))
                return;

            if (hasTarget && (target.Origin - pose.Origin).sqrMagnitude > 25f)
                hasDisplay = false;
            target = pose;
            hasTarget = true;
        }
    }
}
