using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NucleusVR.VR;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    public enum SessionState
    {
        Offline,
        Connecting,
        Hosting,
        Connected,
        Disconnecting,
        Failed
    }

    /// <summary>Opt-in direct transport setup for players already in the same scene.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class DirectSession : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;

        [SerializeField] private Transform trackingOrigin;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private TrackedRig trackedRig;
        [SerializeField] private GameObject playerPrefab;
        [SerializeField, Range(1, 10)] private int playerLimit = 10;

        private readonly HashSet<ulong> approvedPending = new();
        private NetworkManager manager;
        private UnityTransport transport;
        private DirectPoseChannel poseChannel;
        private ushort currentPort;
        private float localJaw;

        public SessionState State { get; private set; } = SessionState.Offline;
        public string Status { get; private set; } = "Offline";
        public bool IsSessionActive => manager != null && (manager.IsListening || manager.ShutdownInProgress);

        public void Configure(
            Transform origin,
            Transform trackedHead,
            Transform trackedLeftHand,
            Transform trackedRightHand,
            GameObject networkPlayerPrefab,
            TrackedRig tracking = null)
        {
            trackingOrigin = origin;
            head = trackedHead;
            leftHand = trackedLeftHand;
            rightHand = trackedRightHand;
            playerPrefab = networkPlayerPrefab;
            trackedRig = tracking != null || origin == null ? tracking : origin.GetComponentInChildren<TrackedRig>();
        }

        private void Awake()
        {
            manager = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            poseChannel = new DirectPoseChannel(manager);
        }

        private void OnEnable()
        {
            manager.OnClientConnectedCallback += OnConnected;
            manager.OnClientDisconnectCallback += OnDisconnected;
            manager.OnClientStopped += OnStopped;
            manager.OnTransportFailure += OnTransportFailure;
        }

        private void OnDisable()
        {
            if (manager == null)
                return;

            poseChannel.Detach();
            if (manager.IsListening && !manager.ShutdownInProgress)
                manager.Shutdown();

            if (manager.ConnectionApprovalCallback == ApproveConnection)
                manager.ConnectionApprovalCallback = null;

            manager.OnClientConnectedCallback -= OnConnected;
            manager.OnClientDisconnectCallback -= OnDisconnected;
            manager.OnClientStopped -= OnStopped;
            manager.OnTransportFailure -= OnTransportFailure;
            approvedPending.Clear();
            State = SessionState.Offline;
            Status = "Offline";
        }

        public bool StartHost(ushort port = DefaultPort)
        {
            if (!PrepareStart(port))
                return false;

            manager.ConnectionApprovalCallback = ApproveConnection;
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            State = SessionState.Hosting;
            Status = "Starting host…";
            return StartTransport(true);
        }

        public bool StartClient(string address, ushort port = DefaultPort)
        {
            if (IsSessionActive)
                return false;

            if (!TryParseAddress(address, out IPAddress ip))
                return Fail("Enter the host's IPv4 address.");

            if (!PrepareStart(port))
                return false;

            if (manager.ConnectionApprovalCallback == ApproveConnection)
                manager.ConnectionApprovalCallback = null;

            transport.SetConnectionData(ip.ToString(), port);
            State = SessionState.Connecting;
            Status = $"Connecting to {ip}:{port}…";
            return StartTransport(false);
        }

        public void Disconnect()
        {
            if (!IsSessionActive || manager.ShutdownInProgress)
                return;

            State = SessionState.Disconnecting;
            Status = "Disconnecting…";
            manager.Shutdown();
        }

        public void SetLocalJaw(float openAmount)
        {
            localJaw = float.IsFinite(openAmount) ? Mathf.Clamp01(openAmount) : 0f;
        }

        internal void SendPose(NetworkRig ownerRig, RigPose pose)
        {
            poseChannel.Send(ownerRig, pose);
        }

        internal bool TryCapturePose(uint sequence, out RigPose pose)
        {
            pose = default;
            if (!HasTrackingTargets || (trackedRig != null && !trackedRig.HeadTracked))
                return false;

            Vector3 origin = trackingOrigin.position;
            float yaw = trackingOrigin.eulerAngles.y;
            Quaternion toWorld = Quaternion.Euler(0f, yaw, 0f);
            Quaternion toLocal = Quaternion.Inverse(toWorld);
            Vector3 localHead = toLocal * (head.position - origin);
            Vector3 localLeft = toLocal * (leftHand.position - origin);
            Vector3 localRight = toLocal * (rightHand.position - origin);
            bool usingLeftHand = trackedRig == null || trackedRig.LeftHandTracked;
            bool usingRightHand = trackedRig == null || trackedRig.RightHandTracked;

            pose = new RigPose
            {
                Sequence = sequence,
                Origin = origin,
                Yaw = yaw,
                HeadPosition = localHead,
                HeadRotation = toLocal * head.rotation,
                LeftPosition = localHead + (usingLeftHand
                    ? Vector3.ClampMagnitude(localLeft - localHead, 1.25f) : new Vector3(-0.38f, -0.75f, 0.1f)),
                LeftRotation = usingLeftHand ? toLocal * leftHand.rotation : Quaternion.identity,
                RightPosition = localHead + (usingRightHand
                    ? Vector3.ClampMagnitude(localRight - localHead, 1.25f) : new Vector3(0.38f, -0.75f, 0.1f)),
                RightRotation = usingRightHand ? toLocal * rightHand.rotation : Quaternion.identity,
                LeftGrip = trackedRig != null ? EncodeExpression(trackedRig.LeftGrip) : (byte)0,
                RightGrip = trackedRig != null ? EncodeExpression(trackedRig.RightGrip) : (byte)0,
                Jaw = EncodeExpression(localJaw)
            };
            return true;
        }

        private bool HasTrackingTargets =>
            trackingOrigin != null && head != null && leftHand != null && rightHand != null;

        private bool PrepareStart(ushort port)
        {
            if (!isActiveAndEnabled || IsSessionActive)
                return false;

            if (port == 0)
                return Fail("Select a UDP port from 1 to 65535.");

            if (!HasTrackingTargets)
                return Fail("The local tracking rig is missing.");

            if (playerPrefab == null ||
                !playerPrefab.TryGetComponent(out NetworkObject networkObject) || networkObject.PrefabIdHash == 0 ||
                !playerPrefab.TryGetComponent<NetworkRig>(out _))
                return Fail("The scenario needs a saved NetworkRig prefab.");

            approvedPending.Clear();
            currentPort = port;
            manager.NetworkConfig ??= new NetworkConfig();
            manager.NetworkConfig.ProtocolVersion = 1;
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = playerPrefab;
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.EnableSceneManagement = false;
            return true;
        }

        private bool StartTransport(bool host)
        {
            try
            {
                if (host ? manager.StartHost() : manager.StartClient())
                {
                    poseChannel.Attach();
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (manager.IsListening && !manager.ShutdownInProgress)
                    manager.Shutdown();
            }

            return Fail(host ? $"Unable to start the host on UDP {currentPort}." : "Unable to start the client.");
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            bool localHost = request.ClientNetworkId == NetworkManager.ServerClientId;
            int limit = Mathf.Clamp(playerLimit, 1, 10);
            bool roomAvailable = manager.ConnectedClients.Count + approvedPending.Count < limit;
            response.Approved = localHost || roomAvailable;
            response.CreatePlayerObject = response.Approved;
            response.Pending = false;

            if (response.Approved)
                approvedPending.Add(request.ClientNetworkId);
            else
                response.Reason = "The room is full.";
        }

        private void OnConnected(ulong clientId)
        {
            approvedPending.Remove(clientId);
            if (manager.IsHost)
            {
                UpdateHostStatus();
            }
            else if (clientId == manager.LocalClientId)
            {
                State = SessionState.Connected;
                Status = "Connected";
            }
        }

        private void OnDisconnected(ulong clientId)
        {
            approvedPending.Remove(clientId);
            if (State is SessionState.Disconnecting or SessionState.Failed)
                return;

            if (manager.IsHost && clientId != manager.LocalClientId)
            {
                UpdateHostStatus();
                return;
            }

            if (clientId == manager.LocalClientId)
            {
                string reason = manager.DisconnectReason;
                Fail(string.IsNullOrEmpty(reason) ? "The connection closed." : reason);
            }
        }

        private void OnStopped(bool wasHost)
        {
            poseChannel.Detach();
            approvedPending.Clear();
            if (State != SessionState.Failed)
            {
                State = SessionState.Offline;
                Status = "Offline";
            }
        }

        private void OnTransportFailure()
        {
            Fail("The network transport failed.");
        }

        private void UpdateHostStatus()
        {
            State = SessionState.Hosting;
            Status = $"Hosting on UDP {currentPort} · {manager.ConnectedClients.Count}/{Mathf.Clamp(playerLimit, 1, 10)} players";
        }

        private bool Fail(string reason)
        {
            State = SessionState.Failed;
            Status = reason;
            return false;
        }

        private static bool TryParseAddress(string address, out IPAddress ip)
        {
            if (!IPAddress.TryParse(address?.Trim(), out ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            if (IPAddress.Any.Equals(ip) || IPAddress.Broadcast.Equals(ip))
                return false;

            int firstOctet = ip.GetAddressBytes()[0];
            return firstOctet > 0 && firstOctet < 224;
        }

        private static byte EncodeExpression(float value)
        {
            return float.IsFinite(value) ? (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f) : (byte)0;
        }
    }
}
