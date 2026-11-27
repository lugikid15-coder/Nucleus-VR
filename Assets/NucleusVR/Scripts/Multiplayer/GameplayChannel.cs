using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    /// <summary>Connection-associated commands and one retained gameplay snapshot for a direct or Relay session.</summary>
    internal sealed class GameplayChannel
    {
        private const string UseMessage = "NucleusVR.Game.Use.v1";
        private const string StateMessage = "NucleusVR.Game.State.v1";
        private const string MotionMessage = "NucleusVR.Game.Motion.v1";
        private const int UseBytes = sizeof(ushort) + sizeof(byte);
        private const int StateHeader = sizeof(uint);
        private const int MotionHeader = 2 * sizeof(uint);
        private const double UseInterval = 0.12;
        private const float MaximumReach = 0.75f;

        private readonly NetworkManager manager;
        private readonly Action<ulong, ushort, bool, Vector3> onUse;
        private readonly Action<ulong, bool> onRoster;
        private readonly Action<byte[]> onState;
        private readonly Action<byte[]> onMotion;
        private readonly Dictionary<ushort, Site> sites = new();
        private readonly Dictionary<ulong, UseBudget> useBudgets = new();
        private readonly HashSet<ulong> connected = new();
        private readonly List<ulong> recipients = new(10);

        private CustomMessagingManager messaging;
        private byte[] retainedState;
        private uint stateSequence, motionSequence;
        private bool hasReceivedState, hasReceivedMotion;

        private bool IsAuthority => manager.IsListening && manager.IsServer && !manager.ShutdownInProgress;

        internal GameplayChannel(NetworkManager manager, Action<ulong, ushort, bool, Vector3> onUse,
            Action<ulong, bool> onRoster, Action<byte[]> onState, Action<byte[]> onMotion)
        {
            this.manager = manager;
            this.onUse = onUse;
            this.onRoster = onRoster;
            this.onState = onState;
            this.onMotion = onMotion;
        }

        internal void Attach()
        {
            messaging = manager.CustomMessagingManager;
            if (manager.IsServer)
            {
                messaging.RegisterNamedMessageHandler(UseMessage, ReceiveUse);
                foreach (ulong playerId in manager.ConnectedClientsIds)
                    OnConnected(playerId);
            }
            else
            {
                messaging.RegisterNamedMessageHandler(StateMessage, ReceiveState);
                messaging.RegisterNamedMessageHandler(MotionMessage, ReceiveMotion);
            }
        }

        internal void Detach()
        {
            if (messaging != null)
            {
                messaging.UnregisterNamedMessageHandler(UseMessage);
                messaging.UnregisterNamedMessageHandler(StateMessage);
                messaging.UnregisterNamedMessageHandler(MotionMessage);
                messaging = null;
            }

            retainedState = null;
            hasReceivedState = hasReceivedMotion = false;
            stateSequence = motionSequence = 0;
            recipients.Clear();
            useBudgets.Clear();
            foreach (ulong playerId in connected)
                onRoster(playerId, false);
            connected.Clear();
        }

        internal bool RegisterSite(ushort id, Collider target, float reach)
        {
            if (id == 0 || target == null || !float.IsFinite(reach) || reach <= 0 ||
                !(target is BoxCollider or SphereCollider or CapsuleCollider || target is MeshCollider { convex: true }))
                return false;

            return sites.TryAdd(id, new Site(target, Mathf.Min(reach, MaximumReach)));
        }

        internal bool UnregisterSite(ushort id) => sites.Remove(id);

        internal bool TryGetPlayer(ulong id, out GameplayPlayerPose pose)
        {
            pose = default;
            if (!IsAuthority || !TryGetRig(id, out NetworkRig rig) ||
                !rig.TryGetAcceptedPose(Time.realtimeSinceStartupAsDouble, out RigPose accepted))
                return false;

            pose = new GameplayPlayerPose(accepted);
            return true;
        }

        internal bool AuthorizeWarp(ulong id, Vector3 destination)
        {
            return IsAuthority && TryGetRig(id, out NetworkRig rig) &&
                rig.AuthorizeWarp(destination, Time.realtimeSinceStartupAsDouble);
        }

        internal bool RequestUse(ushort id, bool left)
        {
            if (id == 0 || manager.ShutdownInProgress || messaging == null)
                return false;

            if (IsAuthority)
                return TryUse(NetworkManager.ServerClientId, id, left);

            if (!manager.IsConnectedClient)
                return false;

            using var writer = new FastBufferWriter(UseBytes, Allocator.Temp);
            writer.WriteValueSafe(id);
            writer.WriteValueSafe(left ? (byte)1 : (byte)0);
            messaging.SendNamedMessage(UseMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            return true;
        }

        internal bool PublishState(byte[] payload)
        {
            if (!IsAuthority || !ValidPayload(payload))
                return false;

            // The controller may reuse its serialization buffer after publishing.
            retainedState = (byte[])payload.Clone();
            stateSequence = unchecked(stateSequence + 1);
            if (CollectRecipients())
            {
                using var writer = CreateStateWriter();
                messaging.SendNamedMessage(StateMessage, recipients, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            return true;
        }

        internal bool PublishMotion(byte[] payload)
        {
            if (!IsAuthority || retainedState == null || !ValidPayload(payload))
                return false;

            if (CollectRecipients())
            {
                motionSequence = unchecked(motionSequence + 1);
                using var writer = new FastBufferWriter(MotionHeader + payload.Length, Allocator.Temp);
                writer.WriteValueSafe(stateSequence);
                writer.WriteValueSafe(motionSequence);
                writer.WriteBytesSafe(payload);
                messaging.SendNamedMessage(MotionMessage, recipients, writer, NetworkDelivery.Unreliable);
            }
            return true;
        }

        internal void OnConnected(ulong playerId)
        {
            if (!IsAuthority || !connected.Add(playerId))
                return;

            byte[] priorState = retainedState;
            onRoster(playerId, true);

            // An authoritative roster callback can publish a replacement to every peer.
            // Otherwise, give the new peer the most recent snapshot directly.
            if (playerId != NetworkManager.ServerClientId && messaging != null &&
                retainedState != null && ReferenceEquals(retainedState, priorState))
            {
                using var writer = CreateStateWriter();
                messaging.SendNamedMessage(StateMessage, playerId, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        internal void OnDisconnected(ulong playerId)
        {
            useBudgets.Remove(playerId);
            if (connected.Remove(playerId))
                onRoster(playerId, false);
        }

        private void ReceiveUse(ulong sender, FastBufferReader reader)
        {
            if (!IsAuthority || reader.Length - reader.Position != UseBytes)
                return;

            reader.ReadValueSafe(out ushort id);
            reader.ReadValueSafe(out byte hand);
            if (hand <= 1)
                TryUse(sender, id, hand == 1);
        }

        private bool TryUse(ulong sender, ushort id, bool left)
        {
            if (!sites.TryGetValue(id, out Site site) || site.Shape == null || !site.Shape.enabled ||
                !site.Shape.gameObject.activeInHierarchy || !TryGetRig(sender, out NetworkRig rig))
                return false;

            double now = Time.realtimeSinceStartupAsDouble;
            if (!ReserveUse(sender, left, now) || !rig.TryGetAcceptedHand(left, now, out Vector3 hand, out _))
                return false;

            Vector3 point = site.Shape.ClosestPoint(hand);
            float distance = (point - hand).sqrMagnitude;
            if (!float.IsFinite(distance) || distance > site.Reach * site.Reach)
                return false;

            onUse(sender, id, left, point);
            return true;
        }

        private bool ReserveUse(ulong playerId, bool left, double now)
        {
            useBudgets.TryGetValue(playerId, out UseBudget budget);
            if (now < (left ? budget.Left : budget.Right))
                return false;

            if (left)
                budget.Left = now + UseInterval;
            else
                budget.Right = now + UseInterval;

            useBudgets[playerId] = budget;
            return true;
        }

        private bool TryGetRig(ulong id, out NetworkRig rig)
        {
            rig = null;
            return manager.ConnectedClients.TryGetValue(id, out NetworkClient player) &&
                player.PlayerObject != null && player.PlayerObject.TryGetComponent(out rig);
        }

        private bool CollectRecipients()
        {
            recipients.Clear();
            if (messaging == null)
                return false;

            foreach (ulong id in manager.ConnectedClientsIds)
                if (id != NetworkManager.ServerClientId)
                    recipients.Add(id);

            return recipients.Count > 0;
        }

        private FastBufferWriter CreateStateWriter()
        {
            var writer = new FastBufferWriter(StateHeader + retainedState.Length, Allocator.Temp);
            writer.WriteValueSafe(stateSequence);
            writer.WriteBytesSafe(retainedState);
            return writer;
        }

        private void ReceiveState(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || !ValidFrame(reader, StateHeader))
                return;

            reader.ReadValueSafe(out uint sequence);
            if (hasReceivedState && !RigPose.IsNewer(sequence, stateSequence))
                return;

            stateSequence = sequence;
            hasReceivedState = true;
            hasReceivedMotion = false;
            onState(ReadPayload(reader));
        }

        private void ReceiveMotion(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || !hasReceivedState || !ValidFrame(reader, MotionHeader))
                return;

            reader.ReadValueSafe(out uint snapshotSequence);
            reader.ReadValueSafe(out uint sequence);
            if (snapshotSequence != stateSequence || (hasReceivedMotion && !RigPose.IsNewer(sequence, motionSequence)))
                return;

            motionSequence = sequence;
            hasReceivedMotion = true;
            onMotion(ReadPayload(reader));
        }

        private static bool ValidPayload(byte[] payload) =>
            payload != null && payload.Length > 0 && payload.Length <= DirectSession.MaxGameplayPayload;

        private static bool ValidFrame(FastBufferReader reader, int headerBytes)
        {
            int length = reader.Length - reader.Position - headerBytes;
            return length > 0 && length <= DirectSession.MaxGameplayPayload;
        }

        private static byte[] ReadPayload(FastBufferReader reader)
        {
            var payload = new byte[reader.Length - reader.Position];
            reader.ReadBytesSafe(ref payload, payload.Length);
            return payload;
        }

        private readonly struct Site
        {
            public readonly Collider Shape;
            public readonly float Reach;
            public Site(Collider shape, float reach) => (Shape, Reach) = (shape, reach);
        }

        private struct UseBudget
        {
            public double Left, Right;
        }
    }
}
