using NucleusVR.Maps;
using NucleusVR.Movement;
using NucleusVR.Multiplayer;
using NucleusVR.Player;
using NucleusVR.Props;
using NucleusVR.UI;
using NucleusVR.VR;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace NucleusVR.Core
{
    [DisallowMultipleComponent]
    public sealed class CalibrationBoot : MonoBehaviour
    {
        [SerializeField] private Material courseMaterial;
        [SerializeField] private Material signalMaterial;
        [SerializeField] private GameObject networkPlayerPrefab;

        private GameObject networkRoot;
        private bool previousHandBodyCollision;
        private bool changedHandBodyCollision;

        private void Awake()
        {
            if (courseMaterial == null || signalMaterial == null)
            {
                Debug.LogError("Assign the calibration palette to the Bootstrap in the Boot scene.", this);
                return;
            }

            var room = NewChild("Calibration bay", transform).gameObject.AddComponent<CalibrationCourse>();
            room.Build(courseMaterial, signalMaterial);

            var player = NewChild("Local player", transform);
            player.position = CalibrationCourse.Spawn;
            player.gameObject.layer = CalibrationLayers.Player;
            var body = player.gameObject.AddComponent<Rigidbody>();
            body.mass = 65f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            var capsule = player.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.8f, 0f);
            capsule.height = 1.6f;
            capsule.radius = 0.23f;

            var head = NewChild("Head", player);
            head.localPosition = Vector3.up * 1.63f;
            head.gameObject.tag = "MainCamera";
            var camera = head.gameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.055f;
            camera.farClipPlane = 38f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(15, 23, 33, 255);
            head.gameObject.AddComponent<AudioListener>();

            Transform leftHand = NewChild("Tracked left hand", player);
            Transform rightHand = NewChild("Tracked right hand", player);
            leftHand.localPosition = new Vector3(-0.32f, 1.1f, 0.4f);
            rightHand.localPosition = new Vector3(0.32f, 1.1f, 0.4f);
            Transform leftContact = PhysicalHand("Left physical hand", leftHand.position);
            Transform rightContact = PhysicalHand("Right physical hand", rightHand.position);

            previousHandBodyCollision = Physics.GetIgnoreLayerCollision(CalibrationLayers.Player, CalibrationLayers.Hands);
            Physics.IgnoreLayerCollision(CalibrationLayers.Player, CalibrationLayers.Hands);
            changedHandBodyCollision = true;

            var tracking = player.gameObject.AddComponent<TrackedRig>();
            tracking.Configure(head, leftHand, rightHand);
            var motor = player.gameObject.AddComponent<HandLocomotion>();
            motor.Configure(tracking, capsule, leftContact, rightContact, CalibrationLayers.EnvironmentMask);

            var avatar = NewChild("Local gorilla", transform).gameObject.AddComponent<GorillaAvatar>();
            GorillaAvatarFactory.Build(avatar, head, leftContact, rightContact, true, courseMaterial.shader);
            avatar.gameObject.AddComponent<LocalGorillaAvatar>().Configure(avatar, tracking);

            var desktop = gameObject.AddComponent<DesktopRigDriver>();
            desktop.Configure(tracking);
            var hands = player.gameObject.AddComponent<EquipmentHands>();
            hands.Configure(tracking, leftContact, rightContact);
            var interaction = gameObject.AddComponent<CalibrationInteraction>();
            interaction.Configure(room, motor, tracking, leftContact, rightContact, desktop, hands);
            DirectSession session = ConfigureNetwork(player, head, leftContact, rightContact);

            var hud = NewChild("Desktop interface", transform).gameObject.AddComponent<CalibrationHud>();
            hud.Build(desktop, interaction, session);
            if (session != null)
            {
                var terminal = NewChild("Direct multiplayer terminal", transform);
                terminal.position = new Vector3(-8.13f, 1.42f, -4.28f);
                terminal.gameObject.AddComponent<WorldSessionPanel>().Build(session, desktop, tracking,
                    leftContact, rightContact, courseMaterial, signalMaterial);
            }
        }

        private DirectSession ConfigureNetwork(Transform player, Transform head, Transform left, Transform right)
        {
            if (networkPlayerPrefab == null)
                return null;

            networkRoot = new GameObject("Direct session");
            networkRoot.SetActive(false);
            var manager = networkRoot.AddComponent<NetworkManager>();
            var transport = networkRoot.AddComponent<UnityTransport>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                TickRate = 30,
                EnableSceneManagement = false
            };
            var network = networkRoot.AddComponent<DirectSession>();
            network.Configure(player, head, left, right, networkPlayerPrefab);
            networkRoot.SetActive(true);
            return network;
        }

        private Transform PhysicalHand(string label, Vector3 position)
        {
            Transform hand = NewChild(label, transform);
            hand.position = position;
            hand.gameObject.layer = CalibrationLayers.Hands;
            var collider = hand.gameObject.AddComponent<SphereCollider>();
            collider.radius = 0.085f;
            var body = hand.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return hand;
        }

        private static Transform NewChild(string label, Transform parent)
        {
            var child = new GameObject(label).transform;
            child.SetParent(parent, false);
            return child;
        }

        private void OnDestroy()
        {
            if (networkRoot != null)
                Destroy(networkRoot);
            if (changedHandBodyCollision)
                Physics.IgnoreLayerCollision(CalibrationLayers.Player, CalibrationLayers.Hands, previousHandBodyCollision);
        }
    }
}
