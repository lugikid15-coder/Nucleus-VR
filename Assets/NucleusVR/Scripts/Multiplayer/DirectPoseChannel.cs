using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace NucleusVR.Multiplayer
{
    /// <summary>Direct pose messages use NGO's connection-associated sender identity.</summary>
    internal sealed class DirectPoseChannel
    {
        private const string SubmitMessage = "NucleusVR.Pose.Submit.v1";
        private const string RelayMessage = "NucleusVR.Pose.Relay.v1";
        private const int RelaySize = sizeof(ulong) + RigPose.EncodedSize;

        private readonly NetworkManager manager;
        private readonly List<ulong> recipients = new(10);
        private CustomMessagingManager messaging;

        public DirectPoseChannel(NetworkManager manager)
        {
            this.manager = manager;
        }

        public void Attach()
        {
            Detach();
            messaging = manager.CustomMessagingManager;
            if (manager.IsServer)
                messaging.RegisterNamedMessageHandler(SubmitMessage, ReceiveSubmission);
            else
                messaging.RegisterNamedMessageHandler(RelayMessage, ReceiveRelay);
        }

        public void Detach()
        {
            if (messaging == null)
                return;

            messaging.UnregisterNamedMessageHandler(SubmitMessage);
            messaging.UnregisterNamedMessageHandler(RelayMessage);
            messaging = null;
            recipients.Clear();
        }

        public void Send(NetworkRig ownerRig, RigPose pose)
        {
            if (messaging == null || manager.ShutdownInProgress)
                return;

            if (manager.IsServer)
            {
                AcceptAndRelay(ownerRig, pose);
                return;
            }

            using var writer = new FastBufferWriter(RigPose.EncodedSize, Allocator.Temp);
            writer.WriteNetworkSerializable(pose);
            messaging.SendNamedMessage(SubmitMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
        }

        private void ReceiveSubmission(ulong sender, FastBufferReader reader)
        {
            if (reader.Length - reader.Position != RigPose.EncodedSize ||
                !manager.ConnectedClients.TryGetValue(sender, out NetworkClient client) ||
                client.PlayerObject == null ||
                !client.PlayerObject.TryGetComponent(out NetworkRig rig))
                return;

            reader.ReadNetworkSerializable(out RigPose pose);
            AcceptAndRelay(rig, pose);
        }

        private void AcceptAndRelay(NetworkRig rig, RigPose pose)
        {
            if (!rig.TryAcceptServerPose(pose, out RigPose accepted))
                return;

            recipients.Clear();
            IReadOnlyList<ulong> peers = manager.ConnectedClientsIds;
            for (int i = 0; i < peers.Count; i++)
            {
                ulong peer = peers[i];
                if (peer != NetworkManager.ServerClientId && peer != rig.OwnerClientId)
                    recipients.Add(peer);
            }

            if (recipients.Count == 0)
                return;

            using var writer = new FastBufferWriter(RelaySize, Allocator.Temp);
            writer.WriteValueSafe(rig.NetworkObjectId);
            writer.WriteNetworkSerializable(accepted);
            messaging.SendNamedMessage(RelayMessage, recipients, writer, NetworkDelivery.Unreliable);
        }

        private void ReceiveRelay(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || reader.Length - reader.Position != RelaySize)
                return;

            reader.ReadValueSafe(out ulong networkObjectId);
            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject player) ||
                !player.TryGetComponent(out NetworkRig rig))
                return;

            reader.ReadNetworkSerializable(out RigPose pose);
            rig.QueuePose(pose);
        }
    }
}
