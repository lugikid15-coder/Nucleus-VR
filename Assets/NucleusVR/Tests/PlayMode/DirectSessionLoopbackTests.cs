#if UNITY_EDITOR
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using NucleusVR.Multiplayer;
using NucleusVR.VR;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
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

        private static bool HasVisiblePose(NetworkManager manager, ulong id, Vector3 expectedHead)
        {
            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject player))
                return false;

            Transform head = player.transform.Find("Pose origin/Head target");
            Transform visual = player.transform.Find(VisualChildPath);
            return visual != null && visual.gameObject.activeSelf &&
                head != null && Vector3.Distance(head.position, expectedHead) < 0.07f;
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
                Tracking.SetSyntheticTracking(
                    new Pose(new Vector3(0f, 1.64f, 0f), Quaternion.identity),
                    new Pose(new Vector3(-0.35f, 1.2f, 0.35f), Quaternion.identity),
                    new Pose(new Vector3(0.35f, 1.2f, 0.35f), Quaternion.identity));
                Session.Configure(trackingRoot, Head, left, right, playerPrefab, Tracking);
                Root.SetActive(true);
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
