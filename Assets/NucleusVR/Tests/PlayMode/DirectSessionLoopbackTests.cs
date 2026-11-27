#if UNITY_EDITOR
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using NucleusVR.Anatomy;
using NucleusVR.Multiplayer;
using NucleusVR.VR;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using AssetDatabase = UnityEditor.AssetDatabase;
using Object = UnityEngine.Object;

namespace NucleusVR.Tests
{
    public sealed class DirectSessionLoopbackTests
    {
        private const string PrefabPath = "Assets/NucleusVR/Prefabs/Players/Direct Player.prefab";
        private const string VisualChildPath = "Pose origin/Gorilla";

        [UnityTest]
        public IEnumerator LoopbackSynchronizesLateJoinAndRemovesDisconnectedRig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            ushort port = AvailableLoopbackPort();
            var host = new Endpoint("Loopback host", prefab, new Vector3(4f, 0f, 7f));
            var client = new Endpoint("Loopback client", prefab, new Vector3(-2f, 0f, 3f));

            try
            {
                Assert.That(host.Session.StartHost(port), Is.True, host.Session.Status);
                yield return WaitFor(() => host.Manager.LocalClient.PlayerObject != null, "host player to spawn");
                NetworkObject hostLocalRig = host.Manager.LocalClient.PlayerObject;
                Vector3 hostHead = host.Head.position;
                yield return new WaitForSecondsRealtime(0.25f);
                host.Tracking.enabled = false;

                Assert.That(client.Session.StartClient("127.0.0.1", port), Is.True, client.Session.Status);
                yield return WaitFor(() => client.Session.State == SessionState.Connected,
                    "client to connect: " + client.Session.Status);
                NetworkObject clientLocalRig = client.Manager.LocalClient.PlayerObject;
                Assert.That(clientLocalRig, Is.Not.Null);

                ulong hostRigId = hostLocalRig.NetworkObjectId;
                ulong clientRigId = clientLocalRig.NetworkObjectId;
                yield return WaitFor(() => HasVisiblePose(client.Manager, hostRigId, hostHead),
                    "late joiner to show host's retained pose");
                yield return WaitFor(() => HasVisiblePose(host.Manager, clientRigId, client.Head.position),
                    "host to show client's live pose");

                NetworkObject remoteHostRig = client.Manager.SpawnManager.SpawnedObjects[hostRigId];
                Assert.That(remoteHostRig.GetComponentsInChildren<SkinnedMeshRenderer>(), Has.Length.EqualTo(2));
                Assert.That(hostLocalRig.transform.Find(VisualChildPath).gameObject.activeSelf, Is.False);
                Assert.That(clientLocalRig.transform.Find(VisualChildPath).gameObject.activeSelf, Is.False);

                client.Tracking.transform.position += Vector3.right * 0.65f;
                yield return WaitFor(() => HasVisiblePose(host.Manager, clientRigId, client.Head.position),
                    "host to show client's moved pose");

                client.Session.Disconnect();
                yield return WaitFor(() => !client.Session.IsSessionActive &&
                    !host.Manager.SpawnManager.SpawnedObjects.ContainsKey(clientRigId),
                    "client shutdown and host-side despawn");
                host.Session.Disconnect();
                yield return WaitFor(() => !host.Session.IsSessionActive, "host shutdown");
            }
            finally
            {
                Object.Destroy(client.Root);
                Object.Destroy(host.Root);
            }
        }

        [UnityTest]
        public IEnumerator GameplayLoopbackRetainsStateAndAuthenticatesAHandAtItsSite()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            ushort port = AvailableLoopbackPort();
            var host = new Endpoint("Gameplay host", prefab, Vector3.zero);
            var client = new Endpoint("Gameplay client", prefab, new Vector3(3f, 0f, 1f));
            var destination = new Vector3(220f, 0f, -60f);
            const ushort siteId = 204;
            var site = new GameObject("Gameplay site");
            site.transform.SetParent(host.Root.transform, false);
            site.transform.position = destination + new Vector3(0.35f, 1.2f, 0.35f);
            Assert.That(host.Session.RegisterGameplaySite(siteId, site.AddComponent<BoxCollider>()), Is.True);

            try
            {
                byte[] received = null;
                int uses = 0;
                ulong senderId = 0;
                bool departed = false;
                client.Session.GameplayStateReceived += state => received = state;
                host.Session.GameplayRosterChanged += (sender, joined) =>
                {
                    if (sender != host.Session.LocalPlayerId && !joined)
                        departed = true;
                };
                host.Session.GameplayUseReceived += (sender, usedSite, left, point) =>
                {
                    Assert.That(usedSite, Is.EqualTo(siteId));
                    Assert.That(left, Is.False);
                    Assert.That(Vector3.Distance(point, site.transform.position), Is.LessThan(0.12f));
                    senderId = sender;
                    uses++;
                };

                Assert.That(host.Session.StartHost(port), Is.True, host.Session.Status);
                byte[] state = { 8, 21, 34 };
                Assert.That(host.Session.PublishGameplayState(state), Is.True);
                state[0] = 98;
                Assert.That(host.Session.PublishGameplayState(new byte[DirectSession.MaxGameplayPayload + 1]), Is.False);
                Assert.That(client.Session.StartClient("127.0.0.1", port), Is.True, client.Session.Status);
                yield return WaitFor(() => client.Session.State == SessionState.Connected && received != null, "retained gameplay snapshot");
                Assert.That(received, Is.EqualTo(new byte[] { 8, 21, 34 }));
                ulong playerId = client.Session.LocalPlayerId;
                yield return WaitFor(() => host.Session.TryGetGameplayPlayer(playerId, out _), "peer's initial accepted pose");
                Assert.That(client.Session.AuthorizeGameplayWarp(playerId, destination), Is.False);

                Assert.That(client.Session.RequestGameplayUse(siteId, false), Is.True);
                yield return new WaitForSecondsRealtime(0.22f);
                Assert.That(uses, Is.Zero);
                Assert.That(host.Session.AuthorizeGameplayWarp(playerId, destination), Is.True);
                Assert.That(host.Session.TryGetGameplayPlayer(playerId, out _), Is.False, "Old interaction pose remained active during warp");
                client.Tracking.transform.position = destination;
                yield return WaitFor(() => host.Session.TryGetGameplayPlayer(playerId, out GameplayPlayerPose pose) &&
                    Vector3.Distance(pose.Origin, destination) < 0.05f, "accepted authorized warp");

                Assert.That(client.Session.RequestGameplayUse(siteId, false), Is.True);
                yield return WaitFor(() => uses == 1, "authoritative gameplay interaction");
                Assert.That(senderId, Is.EqualTo(playerId));
                client.Session.Disconnect();
                yield return WaitFor(() => departed && !client.Session.IsSessionActive, "gameplay roster cleanup");
                host.Session.Disconnect();
                yield return WaitFor(() => !host.Session.IsSessionActive, "gameplay host shutdown");
            }
            finally
            {
                Object.Destroy(client.Root);
                Object.Destroy(host.Root);
            }
        }

        [UnityTest]
        public IEnumerator AnatomyLoopbackVerifiesReachAndReplicatesDetachAndHostReset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            ushort port = AvailableLoopbackPort();
            var host = new Endpoint("Anatomy host", prefab, new Vector3(4f, 0f, 7f));
            var client = new Endpoint("Anatomy client", prefab, new Vector3(2f, 0f, 7f));
            Vector3 handTarget = new Vector3(4.35f, 1.2f, 7.35f);
            var authority = new TestAnatomySubject(host, handTarget);
            AnatomyTarget arm = AnatomyTarget.ForRegion(AnatomyRegion.LeftUpperArm);

            try
            {
                Assert.That(host.Session.StartHost(port), Is.True, host.Session.Status);
                yield return WaitFor(() => host.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Damage, arm, false),
                    "host's own accepted tracked hand to strike");
                uint firstRevision = authority.State.Revision;

                int clientImpacts = 0;
                client.Session.AnatomyImpactApplied += impact =>
                {
                    if (impact.SubjectId == TestAnatomySubject.Id)
                        clientImpacts++;
                };

                Assert.That(client.Session.StartClient("127.0.0.1", port), Is.True, client.Session.Status);
                yield return WaitFor(() => client.Session.State == SessionState.Connected, "anatomy client connection");
                // Simulate a procedurally created room where the host's targeted baseline
                // can arrive before the client finishes registering the local specimen.
                yield return new WaitForSecondsRealtime(0.2f);
                var replica = new TestAnatomySubject(client, handTarget);
                yield return WaitFor(() => replica.Snapshot?.Revision == firstRevision, "late specimen anatomy resynchronization");
                Assert.That(replica.Rig.HasAuthority, Is.False);
                Assert.That(clientImpacts, Is.Zero);

                yield return AnimatePeerStrikes(client, arm, () => authority.State.Revision > firstRevision, 0.7f);
                Assert.That(authority.State.Revision, Is.EqualTo(firstRevision), "Far request changed host state");

                client.Tracking.transform.position += Vector3.right * 2f;
                ulong clientId = client.Manager.LocalClient.PlayerObject.NetworkObjectId;
                yield return WaitFor(() => HasVisiblePose(host.Manager, clientId, client.Head.position),
                    "client pose to arrive within reach");
                yield return AnimatePeerStrikes(client, arm, () => authority.State.Revision > firstRevision, 2.5f);
                yield return WaitFor(() => replica.Snapshot?.Revision > firstRevision, "validated client strike snapshot");
                Assert.That(authority.State.Revision, Is.GreaterThan(firstRevision));
                Assert.That(replica.State.Revision, Is.Zero, "The client changed its own offline data");
                Assert.That(clientImpacts, Is.GreaterThanOrEqualTo(1));
                Assert.That(client.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Fracture, arm, false), Is.False);
                Assert.That(client.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Detach, arm, false), Is.False);

                yield return WaitFor(() => host.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Fracture, arm, false),
                    "first host fracture action");
                yield return WaitFor(() => host.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Fracture, arm, false),
                    "second host fracture action");
                yield return WaitFor(() => host.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Detach, arm, false),
                    "host detachment");
                yield return WaitFor(() =>
                    replica.Snapshot?.GetRegion(AnatomyRegion.LeftUpperArm).Stage == AnatomyStage.Severed,
                    "replica to present severed anatomy");
                Assert.That(authority.Arm.isKinematic, Is.False);
                Assert.That(replica.Arm.isKinematic, Is.True);

                authority.Arm.useGravity = false;
                authority.Arm.linearVelocity = Vector3.zero;
                authority.Arm.angularVelocity = Vector3.zero;
                Vector3 turnedPiecePosition = handTarget + Vector3.up * 0.31f;
                authority.Arm.position = turnedPiecePosition;
                yield return WaitFor(() => Vector3.Distance(replica.Arm.position, turnedPiecePosition) < 0.07f,
                    "authoritative detached piece movement");
                Assert.That(client.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Grab, arm, false), Is.True);
                yield return WaitFor(() => HasGripJoint(authority.Arm), "host-owned grip for client hand");
                Assert.That(HasGripJoint(replica.Arm), Is.False);
                Assert.That(client.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Release, arm, false), Is.True);
                yield return WaitFor(() => !HasGripJoint(authority.Arm), "host-owned grip release");
                Assert.That(client.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Reset, arm, false), Is.False);
                Assert.That(authority.State.GetRegion(AnatomyRegion.LeftUpperArm).Stage, Is.EqualTo(AnatomyStage.Severed));

                Assert.That(host.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Reset, arm, false), Is.True);
                yield return WaitFor(() =>
                    replica.Snapshot?.GetRegion(AnatomyRegion.LeftUpperArm).Stage == AnatomyStage.Intact,
                    "host reset snapshot");
                Assert.That(authority.Arm.isKinematic, Is.True);
                Assert.That(Vector3.Distance(replica.Arm.position, handTarget), Is.LessThan(0.01f));
                client.Session.Disconnect();
                yield return WaitFor(() => !client.Session.IsSessionActive && replica.Rig.HasAuthority,
                    "offline authority restoration");
                host.Session.Disconnect();
                yield return WaitFor(() => !host.Session.IsSessionActive, "anatomy host shutdown");
            }
            finally
            {
                Object.Destroy(client.Root);
                Object.Destroy(host.Root);
            }
        }

        private static bool HasVisiblePose(NetworkManager manager, ulong id, Vector3 expectedHead)
        {
            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject player))
                return false;

            Transform head = player.transform.Find("Pose origin/Head target");
            Transform visual = player.transform.Find(VisualChildPath);
            return visual != null && visual.gameObject.activeSelf &&
                head != null && Vector3.Distance(head.position, expectedHead) < 0.07f;
        }

        private static bool HasGripJoint(Rigidbody body)
        {
            foreach (var joint in body.GetComponents<ConfigurableJoint>())
                if (joint.connectedBody != null && joint.connectedBody.GetComponentInParent<AnatomyGrip>() != null)
                    return true;
            return false;
        }

        private static IEnumerator AnimatePeerStrikes(Endpoint peer, AnatomyTarget target, Func<bool> accepted, float duration)
        {
            double start = Time.realtimeSinceStartupAsDouble;
            double nextRequest = start + 0.16;
            double now;
            do
            {
                now = Time.realtimeSinceStartupAsDouble;
                peer.SetRightHandOffset(Mathf.Sin((float)(now - start) * 23f) * 0.12f);
                if (now >= nextRequest)
                {
                    Assert.That(peer.Session.RequestAnatomy(TestAnatomySubject.Id, AnatomyAction.Damage, target, false), Is.True);
                    nextRequest = now + 0.19;
                }
                yield return null;
            } while (!accepted() && now < start + duration);
            peer.SetRightHandOffset(0f);
        }

        private static IEnumerator WaitFor(Func<bool> ready, string description)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 7;
            while (!ready())
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    Assert.Fail("Timed out waiting for " + description);
                yield return null;
            }
        }

        private static ushort AvailableLoopbackPort()
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
        }

        private sealed class TestAnatomySubject
        {
            public const ushort Id = 42;
            public readonly AnatomyRig Rig;
            public readonly AnatomyDamageState State = new();
            public readonly Rigidbody Arm;
            public AnatomySnapshot Snapshot { get; private set; }

            public TestAnatomySubject(Endpoint endpoint, Vector3 armPosition)
            {
                var root = new GameObject("Anatomy fixture");
                root.transform.SetParent(endpoint.Root.transform, false);
                Rig = root.AddComponent<AnatomyRig>();
                Rig.Configure(true);
                Rigidbody torso = Body("Torso", armPosition + Vector3.back * 0.68f);
                Arm = Body("Arm", armPosition);
                Assert.That(Rig.BindRegion(AnatomyRegion.Torso, torso), Is.True);
                Assert.That(Rig.BindRegion(AnatomyRegion.LeftUpperArm, Arm, torso), Is.True);
                Assert.That(endpoint.Session.RegisterAnatomySubject(Id, State, Rig, snapshot => Snapshot = snapshot), Is.True);
            }

            private Rigidbody Body(string name, Vector3 position)
            {
                var part = new GameObject(name);
                part.transform.SetParent(Rig.transform, false);
                part.transform.position = position;
                part.AddComponent<SphereCollider>().radius = 0.17f;
                var body = part.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.mass = 14f;
                return body;
            }
        }

        private sealed class Endpoint
        {
            public readonly GameObject Root;
            public readonly NetworkManager Manager;
            public readonly DirectSession Session;
            public readonly TrackedRig Tracking;
            public readonly Transform Head;

            public Endpoint(string name, GameObject playerPrefab, Vector3 origin)
            {
                Root = new GameObject(name);
                Root.SetActive(false);
                Manager = Root.AddComponent<NetworkManager>();
                var transport = Root.AddComponent<UnityTransport>();
                Manager.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    TickRate = 30,
                    EnableSceneManagement = false
                };
                Session = Root.AddComponent<DirectSession>();

                Transform trackingRoot = Child("Tracking origin", Root.transform);
                trackingRoot.position = origin;
                Head = Child("Local head", trackingRoot);
                Transform left = Child("Local left", trackingRoot);
                Transform right = Child("Local right", trackingRoot);
                Tracking = trackingRoot.gameObject.AddComponent<TrackedRig>();
                Tracking.Configure(Head, left, right);
                SetRightHandOffset(0f);
                Session.Configure(trackingRoot, Head, left, right, playerPrefab, Tracking);
                Root.SetActive(true);
            }

            public void SetRightHandOffset(float z)
            {
                Tracking.SetSyntheticTracking(
                    new Pose(new Vector3(0f, 1.64f, 0f), Quaternion.identity),
                    new Pose(new Vector3(-0.35f, 1.2f, 0.35f), Quaternion.identity),
                    new Pose(new Vector3(0.35f, 1.2f, 0.35f + z), Quaternion.identity));
            }

            private static Transform Child(string name, Transform parent)
            {
                var child = new GameObject(name).transform;
                child.SetParent(parent, false);
                return child;
            }
        }
    }
}
#endif
