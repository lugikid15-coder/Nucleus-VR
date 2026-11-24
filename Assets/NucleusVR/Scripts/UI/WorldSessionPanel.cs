using System;
using System.Collections.Generic;
using NucleusVR.Core;
using NucleusVR.Maps;
using NucleusVR.Multiplayer;
using NucleusVR.VR;
using UnityEngine;

namespace NucleusVR.UI
{
    internal sealed class WorldSessionPanel : MonoBehaviour
    {
        private sealed class Pad
        {
            public MeshRenderer Face;
            public Action Activate;
        }

        private readonly List<Pad> pads = new();
        private Material housing;
        private Material idle;
        private Material highlighted;
        private TrackedRig tracking;
        private Transform left;
        private Transform right;
        private DesktopRigDriver desktop;
        private DirectSession session;
        private TextMesh addressLabel;
        private TextMesh statusLabel;
        private string address = "";
        private bool leftPressed;
        private bool rightPressed;

        public void Build(DirectSession network, DesktopRigDriver desktopDriver, TrackedRig rig,
            Transform leftContact, Transform rightContact, Material surface, Material signal)
        {
            session = network;
            desktop = desktopDriver;
            tracking = rig;
            left = leftContact;
            right = rightContact;

            housing = new Material(surface) { color = new Color32(38, 55, 67, 255) };
            idle = new Material(signal) { color = new Color32(36, 60, 72, 255) };
            highlighted = new Material(signal) { color = new Color32(44, 121, 106, 255) };

            Block("Terminal plinth", new Vector3(0f, -1.22f, 0.16f), new Vector3(0.7f, 0.85f, 0.39f), housing);
            Block("Terminal case", new Vector3(0f, -0.04f, 0.12f), new Vector3(1.34f, 1.72f, 0.15f), housing);
            Label("DIRECT MULTIPLAYER", 0f, 0.68f, 0.10f, new Color32(146, 237, 197, 255));
            Label("Touch a key and squeeze grip.", 0f, 0.52f, 0.045f, Color.white);
            addressLabel = Label("Host IPv4", 0f, 0.39f, 0.07f, Color.white);
            statusLabel = Label(session.Status, 0f, 0.25f, 0.045f, Color.white);

            const string digits = "123456789.0";
            for (int index = 0; index < digits.Length; index++)
            {
                char key = digits[index];
                AddPad(key.ToString(), index, () =>
                {
                    if (address.Length < 15)
                        address += key;
                });
            }

            AddPad("Back", 11, () =>
            {
                if (address.Length > 0)
                    address = address[..^1];
            });
            AddPad("Host", 12, () => session.StartHost());
            AddPad("Join", 13, () => session.StartClient(address));
            AddPad("Leave", 14, session.Disconnect);
        }

        private void LateUpdate()
        {
            bool desktopMode = desktop.IsDesktop;
            Pad leftKey = desktopMode || !tracking.LeftHandTracked ? null : AtHand(left);
            Pad rightKey = desktopMode || !tracking.RightHandTracked ? null : AtHand(right);

            bool activateLeft = Pressed(!desktopMode && tracking.LeftHandTracked ? tracking.LeftGrip : 0f, ref leftPressed);
            bool activateRight = Pressed(!desktopMode && tracking.RightHandTracked ? tracking.RightGrip : 0f, ref rightPressed);
            if (activateLeft)
                leftKey?.Activate();
            if (activateRight && (!activateLeft || rightKey != leftKey))
                rightKey?.Activate();

            foreach (Pad pad in pads)
                pad.Face.sharedMaterial = pad == leftKey || pad == rightKey ? highlighted : idle;

            addressLabel.text = address.Length == 0 ? "Host IPv4" : address;
            statusLabel.text = session.Status;
        }

        private static bool Pressed(float grip, ref bool pressed)
        {
            if (grip <= 0.35f)
                pressed = false;
            if (grip < 0.65f || pressed)
                return false;

            pressed = true;
            return true;
        }

        private Pad AtHand(Transform hand)
        {
            Vector3 position = transform.InverseTransformPoint(hand.position);
            foreach (Pad pad in pads)
            {
                Vector3 delta = position - pad.Face.transform.localPosition;
                if (Mathf.Abs(delta.x) <= 0.145f && Mathf.Abs(delta.y) <= 0.082f &&
                    delta.z >= -0.15f && delta.z <= 0.02f)
                    return pad;
            }
            return null;
        }

        private void AddPad(string text, int index, Action activate)
        {
            float x = ((index % 4) - 1.5f) * 0.32f;
            float y = 0.075f - (index / 4) * 0.212f;
            var frame = Block(text + " key", new Vector3(x, y, 0f), new Vector3(0.285f, 0.16f, 0.065f), idle, false);
            Label(text, x, y, 0.079f, Color.white);
            pads.Add(new Pad { Face = frame.GetComponent<MeshRenderer>(), Activate = activate });
        }

        private TextMesh Label(string text, float x, float y, float size, Color color)
            => FacilityGeometry.Label(transform, text, new Vector3(x, y, -0.045f), size, color);

        private GameObject Block(string text, Vector3 position, Vector3 size, Material finish, bool collidable = true)
            => FacilityGeometry.Block(transform, text, position, size, finish, CalibrationLayers.Course, collidable);

        private void OnDestroy()
        {
            Destroy(housing);
            Destroy(idle);
            Destroy(highlighted);
        }
    }
}
