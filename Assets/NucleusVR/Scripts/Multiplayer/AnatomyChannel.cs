using System;
using System.Collections.Generic;
using NucleusVR.Anatomy;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NucleusVR.Multiplayer
{
    internal sealed class AnatomyChannel
    {
        private const string RequestMessage = "NucleusVR.Anatomy.Request.v1";
        private const string SnapshotQueryMessage = "NucleusVR.Anatomy.Query.v1";
        private const string SnapshotMessage = "NucleusVR.Anatomy.Snapshot.v1";
        private const string MotionMessage = "NucleusVR.Anatomy.Motion.v1";
        private const double MotionInterval = 1d / 15d;
        private const int MaximumSubjects = 8;

        private readonly NetworkManager manager;
        private readonly Action<AnatomyImpact> notifyImpact;
        private readonly Dictionary<ushort, AnatomySubject> subjects = new();
        private readonly Dictionary<ulong, (double Until, int Count)> snapshotRequests = new();
        private readonly List<ulong> recipients = new(10);
        private readonly AnatomyAuthority authority;
        private CustomMessagingManager messaging;
        private double nextMotion;

        internal AnatomyChannel(NetworkManager manager, Action<AnatomyImpact> notifyImpact)
        {
            this.manager = manager;
            this.notifyImpact = notifyImpact;
            authority = new AnatomyAuthority(manager);
        }

        internal bool Register(ushort id, AnatomyDamageState state, AnatomyRig rig,
            Action<AnatomySnapshot> refresh, Action<bool> authorityChanged)
        {
            if (id == 0 || state == null || rig == null || subjects.Count >= MaximumSubjects || subjects.ContainsKey(id))
                return false;

            var subject = new AnatomySubject(id, state, rig, refresh, authorityChanged);
            subjects.Add(id, subject);
            subject.SetSessionAuthority(messaging == null || manager.IsServer);
            if (messaging != null && (manager.IsServer || manager.IsConnectedClient) && subject.ObserveActivation())
            {
                if (manager.IsServer)
                    PublishSnapshot(subject, null);
                else
                    RequestSnapshot(id);
            }
            return true;
        }

        internal bool Unregister(ushort id)
        {
            if (!subjects.Remove(id, out AnatomySubject subject))
                return false;

            authority.ReleaseSubject(id);
            subject.SetSessionAuthority(true);
            return true;
        }

        internal void Attach()
        {
            messaging = manager.CustomMessagingManager;
            if (manager.IsServer)
            {
                messaging.RegisterNamedMessageHandler(RequestMessage, ReceiveRequest);
                messaging.RegisterNamedMessageHandler(SnapshotQueryMessage, ReceiveSnapshotQuery);
            }
            else
            {
                messaging.RegisterNamedMessageHandler(SnapshotMessage, ReceiveSnapshot);
                messaging.RegisterNamedMessageHandler(MotionMessage, ReceiveMotion);
            }

            foreach (AnatomySubject subject in subjects.Values)
                subject.SetSessionAuthority(manager.IsServer);
        }

        internal void Detach()
        {
            if (messaging == null)
                return;

            messaging.UnregisterNamedMessageHandler(RequestMessage);
            messaging.UnregisterNamedMessageHandler(SnapshotQueryMessage);
            messaging.UnregisterNamedMessageHandler(SnapshotMessage);
            messaging.UnregisterNamedMessageHandler(MotionMessage);
            messaging = null;
            recipients.Clear();
            snapshotRequests.Clear();
            authority.Stop();

            foreach (AnatomySubject subject in subjects.Values)
                subject.SetSessionAuthority(true);
        }

        internal void OnConnected(ulong clientId)
        {
            if (messaging == null || !manager.IsServer || clientId == NetworkManager.ServerClientId)
                return;

            foreach (AnatomySubject subject in subjects.Values)
            {
                if (subject.IsAvailable)
                    SendSnapshot(subject, clientId);
            }
        }

        internal void OnDisconnected(ulong clientId)
        {
            snapshotRequests.Remove(clientId);
            authority.Disconnect(clientId);
        }

        internal bool Request(ushort id, AnatomyAction action, AnatomyTarget target, bool left)
        {
            if (messaging == null || manager.ShutdownInProgress ||
                !subjects.TryGetValue(id, out AnatomySubject subject) || !subject.IsAvailable ||
                action > AnatomyAction.Reset || !target.IsValid)
                return false;

            if (manager.IsServer)
                return ApplyRequest(NetworkManager.ServerClientId, subject, action, target, left);

            if (!manager.IsConnectedClient || !AnatomyAuthority.IsPeerAction(action))
                return false;

            using var writer = new FastBufferWriter(AnatomyWire.RequestBytes, Allocator.Temp);
            AnatomyWire.WriteRequest(writer, id, action, target, left);
            messaging.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            return true;
        }

        internal void FixedUpdate()
        {
            if (messaging != null && manager.IsServer && !manager.ShutdownInProgress)
                authority.Tick();
        }

        internal void LateUpdate()
        {
            if (messaging == null || manager.ShutdownInProgress)
                return;

            if (manager.IsServer)
            {
                SendMotionIfDue();
            }
            else if (manager.IsConnectedClient)
            {
                float interpolation = 1f - Mathf.Pow(0.5f, Time.unscaledDeltaTime / 0.045f);
                foreach (AnatomySubject subject in subjects.Values)
                {
                    if (subject.ObserveActivation())
                        RequestSnapshot(subject.Id);
                    if (subject.IsAvailable)
                        subject.UpdateReplica(interpolation);
                }
            }
        }

        private void RequestSnapshot(ushort id)
        {
            using var writer = new FastBufferWriter(sizeof(ushort), Allocator.Temp);
            writer.WriteValueSafe(id);
            messaging.SendNamedMessage(SnapshotQueryMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private void ReceiveSnapshotQuery(ulong sender, FastBufferReader reader)
        {
            if (reader.Length - reader.Position != sizeof(ushort) || sender == NetworkManager.ServerClientId ||
                !manager.ConnectedClients.ContainsKey(sender))
                return;

            reader.ReadValueSafe(out ushort id);
            if (!subjects.TryGetValue(id, out AnatomySubject subject) || !subject.IsAvailable)
                return;

            double now = Time.realtimeSinceStartupAsDouble;
            if (!snapshotRequests.TryGetValue(sender, out var window) || now >= window.Until)
                window = (now + 1, 0);
            if (window.Count >= MaximumSubjects)
                return;
            snapshotRequests[sender] = (window.Until, window.Count + 1);
            SendSnapshot(subject, sender);
        }

        private void SendSnapshot(AnatomySubject subject, ulong clientId)
        {
            using var writer = new FastBufferWriter(AnatomyWire.MaxFrameBytes, Allocator.Temp);
            WriteSnapshot(writer, subject, null);
            messaging.SendNamedMessage(SnapshotMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private void ReceiveRequest(ulong sender, FastBufferReader reader)
        {
            if (!AnatomyWire.TryReadRequest(reader, out ushort id, out AnatomyAction action, out AnatomyTarget target, out bool left) ||
                !subjects.TryGetValue(id, out AnatomySubject subject) || !subject.IsAvailable)
                return;

            ApplyRequest(sender, subject, action, target, left);
        }

        private bool ApplyRequest(ulong clientId, AnatomySubject subject, AnatomyAction action, AnatomyTarget target, bool left)
        {
            if (!authority.TryAction(clientId, subject, action, target, left, out bool update, out AnatomyImpact? impact))
                return false;

            if (update)
            {
                PublishSnapshot(subject, impact);
                subject.NotifySnapshot(subject.Damage.CaptureSnapshot());
            }
            if (impact.HasValue)
                notifyImpact?.Invoke(impact.Value);

            return true;
        }

        private void PublishSnapshot(AnatomySubject subject, AnatomyImpact? impact)
        {
            if (!subject.IsAvailable)
                return;

            CollectRecipients();
            if (recipients.Count == 0)
                return;

            using var writer = new FastBufferWriter(AnatomyWire.MaxFrameBytes, Allocator.Temp);
            WriteSnapshot(writer, subject, impact);
            messaging.SendNamedMessage(SnapshotMessage, recipients, writer, NetworkDelivery.ReliableSequenced);
        }

        private static void WriteSnapshot(FastBufferWriter writer, AnatomySubject subject, AnatomyImpact? impact)
        {
            AnatomySnapshot snapshot = subject.Damage.CaptureSnapshot();
            AnatomyWire.Header header = subject.CaptureMotion(snapshot.Revision, false);
            AnatomyWire.WriteHeader(writer, header);
            AnatomyWire.WriteSnapshot(writer, snapshot, subject.Rig.RagdollActive, impact);
            AnatomyWire.WriteMotion(writer, header.BodyMask, subject.WirePoses);
        }

        private void SendMotionIfDue()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextMotion)
                return;
            nextMotion = now + MotionInterval;
            CollectRecipients();
            if (recipients.Count == 0)
                return;

            foreach (AnatomySubject subject in subjects.Values)
            {
                if (subject.ObserveActivation())
                {
                    PublishSnapshot(subject, null);
                    continue;
                }
                if (!subject.IsAvailable)
                    continue;

                AnatomyWire.Header header = subject.CaptureMotion(subject.Damage.Revision, true);
                if (header.BodyMask == 0)
                    continue;

                int size = AnatomyWire.HeaderBytes + AnatomyWire.MotionBytes(header.BodyMask);
                using var writer = new FastBufferWriter(size, Allocator.Temp);
                AnatomyWire.WriteHeader(writer, header);
                AnatomyWire.WriteMotion(writer, header.BodyMask, subject.WirePoses);
                messaging.SendNamedMessage(MotionMessage, recipients, writer, NetworkDelivery.Unreliable);
            }
        }

        private void CollectRecipients()
        {
            recipients.Clear();
            foreach (ulong peer in manager.ConnectedClientsIds)
                if (peer != NetworkManager.ServerClientId)
                    recipients.Add(peer);
        }

        private void ReceiveSnapshot(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || !AnatomyWire.TryReadHeader(reader, out AnatomyWire.Header header) ||
                !subjects.TryGetValue(header.Id, out AnatomySubject subject) || !subject.IsAvailable ||
                !AnatomyWire.TryReadSnapshot(reader, header, out AnatomySnapshot snapshot, out bool ragdoll, out AnatomyImpact? impact) ||
                !AnatomyWire.TryReadMotion(reader, header.BodyMask, subject.WirePoses))
                return;

            if (subject.ReceiveSnapshot(header, snapshot, ragdoll) && impact.HasValue)
                notifyImpact?.Invoke(impact.Value);
        }

        private void ReceiveMotion(ulong sender, FastBufferReader reader)
        {
            if (sender != NetworkManager.ServerClientId || !AnatomyWire.TryReadHeader(reader, out AnatomyWire.Header header) ||
                !subjects.TryGetValue(header.Id, out AnatomySubject subject) || !subject.IsAvailable ||
                !AnatomyWire.TryReadMotion(reader, header.BodyMask, subject.WirePoses))
                return;

            subject.ReceiveMotion(header);
        }
    }
}
