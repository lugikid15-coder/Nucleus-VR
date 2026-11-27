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
        private const int CodeLength = OnlineRoomCode.Length;
        private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const float GripPress = 0.65f;
        private const float GripRelease = 0.35f;
        private const float KeyTop = 0.085f;
        private static readonly Color Ink = new Color32(19, 38, 45, 255);
        private static readonly Color Foreground = new Color32(234, 245, 245, 255);
        private static readonly Color Muted = new Color32(163, 187, 196, 255);
        private static readonly Color Mint = new Color32(155, 238, 205, 255);

        private enum Command
        {
            Character,
            Backspace,
            Connect,
            Join,
            Leave
        }

        private enum Appearance
        {
            Unset,
            Disabled,
            Idle,
            Focused
        }

        private sealed class Pad
        {
            public MeshRenderer Face;
            public TextMesh Caption;
            public Vector2 Center;
            public Vector2 HalfSize;
            public Command Kind;
            public Action Activate;
            public bool Primary;
            public Appearance State;
        }

        private readonly List<Pad> pads = new(41);
        private readonly List<Material> finishes = new(6);
        private Material housing;
        private Material keypad;
        private Material action;
        private Material disabled;
        private Material focused;
        private OnlineSession session;
        private DesktopRigDriver desktop;
        private TrackedRig tracking;
        private Transform left;
        private Transform right;
        private TextMesh codeLabel;
        private TextMesh statusLabel;
        private string typed = "";
        private string lastTyped;
        private string lastRoomCode;
        private string lastStatus;
        private bool lastInRoom;
        private bool leftHeld = true;
        private bool rightHeld = true;

        public void Build(OnlineSession network, DesktopRigDriver desktopDriver, TrackedRig rig,
            Transform leftContact, Transform rightContact, Material surface, Material signal)
        {
            session = network;
            desktop = desktopDriver;
            tracking = rig;
            left = leftContact;
            right = rightContact;

            housing = Finish(surface, "Session terminal steel", new Color32(34, 51, 62, 255));
            keypad = Finish(signal, "Session key", new Color32(44, 72, 84, 255));
            action = Finish(signal, "Session action", new Color32(51, 113, 105, 255));
            disabled = Finish(signal, "Unavailable session key", new Color32(36, 49, 58, 255));
            focused = Finish(signal, "Targeted session key", new Color32(146, 218, 193, 255));
            Material display = Finish(signal, "Session display", new Color32(18, 31, 42, 255));

            Block("Terminal pedestal", new Vector3(0f, -0.988f, 0.13f), new Vector3(0.64f, 0.864f, 0.32f), housing);
            Block("Terminal case", new Vector3(0f, 0.047f, 0.123f), new Vector3(1.55f, 1.21f, 0.17f), housing);
            Block("Status display", new Vector3(0f, 0.349f, 0.012f), new Vector3(1.43f, 0.389f, 0.028f), display, false);
            Label("Online multiplayer", 0f, 0.485f, 0.089f, Mint);
            statusLabel = Label("", 0f, 0.376f, 0.048f, Foreground);
            codeLabel = Label("", 0f, 0.253f, 0.076f, Mint);
            Label("Touch a key and squeeze grip", 0f, 0.153f, 0.046f, Muted);

            for (int index = 0; index < Characters.Length; index++)
            {
                char character = Characters[index];
                float x = -0.630f + (index % 6) * 0.153f;
                float y = KeyTop - (index / 6) * 0.110f;
                AddPad(character.ToString(), x, y, 0.133f, 0.090f, Command.Character, () => typed += character);
            }

            AddAction("Quick join", 0, Command.Connect, () => session.QuickJoin(), true);
            AddAction("Create private", 1, Command.Connect, () => session.CreatePrivate(), true);
            AddAction("Join with code", 2, Command.Join, () => session.JoinCode(typed), true);
            AddAction("Backspace", 3, Command.Backspace, () => typed = typed[..^1]);
            AddAction("Leave / cancel", 4, Command.Leave, () => session.Leave());

            RefreshReadout();
            RefreshPads(null, null);
        }

        private void LateUpdate()
        {
            if (session == null)
                return;

            bool usingXR = desktop != null && !desktop.IsDesktop && tracking != null;
            bool useLeft = usingXR && tracking.LeftHandTracked && left != null;
            bool useRight = usingXR && tracking.RightHandTracked && right != null;
            Pad leftKey = useLeft ? FindPad(left.position) : null;
            Pad rightKey = useRight ? FindPad(right.position) : null;
            bool leftPulse = GripPulse(useLeft, useLeft ? tracking.LeftGrip : 0f, ref leftHeld);
            bool rightPulse = GripPulse(useRight, useRight ? tracking.RightGrip : 0f, ref rightHeld);

            if (leftPulse && leftKey != null && Allowed(leftKey.Kind))
                leftKey.Activate();
            if (rightPulse && rightKey != null && (!leftPulse || rightKey != leftKey) && Allowed(rightKey.Kind))
                rightKey.Activate();

            RefreshReadout();
            RefreshPads(leftKey, rightKey);
        }

        private static bool GripPulse(bool tracked, float grip, ref bool held)
        {
            if (!tracked)
            {
                held = true;
                return false;
            }

            if (grip <= GripRelease)
                held = false;
            if (held || !(grip >= GripPress))
                return false;

            held = true;
            return true;
        }

        private bool Allowed(Command command)
        {
            if (session.Busy)
                return command == Command.Leave;
            if (session.InRoom)
                return command == Command.Leave;

            return command switch
            {
                Command.Character => typed.Length < CodeLength,
                Command.Backspace => typed.Length > 0,
                Command.Connect => true,
                Command.Join => typed.Length == CodeLength,
                _ => false
            };
        }

        private Pad FindPad(Vector3 worldPosition)
        {
            Vector3 position = transform.InverseTransformPoint(worldPosition);
            if (position.z < -0.157f || position.z > 0.024f)
                return null;

            foreach (Pad pad in pads)
            {
                if (Mathf.Abs(position.x - pad.Center.x) <= pad.HalfSize.x &&
                    Mathf.Abs(position.y - pad.Center.y) <= pad.HalfSize.y)
                    return pad;
            }

            return null;
        }

        private void RefreshPads(Pad leftKey, Pad rightKey)
        {
            foreach (Pad pad in pads)
            {
                Appearance state = !Allowed(pad.Kind) ? Appearance.Disabled :
                    pad == leftKey || pad == rightKey ? Appearance.Focused : Appearance.Idle;
                if (state == pad.State)
                    continue;

                pad.State = state;
                pad.Face.sharedMaterial = state switch
                {
                    Appearance.Disabled => disabled,
                    Appearance.Focused => focused,
                    _ => pad.Primary ? action : keypad
                };
                pad.Caption.color = state == Appearance.Focused ? Ink : state == Appearance.Disabled ? Muted : Foreground;
            }
        }

        private void RefreshReadout()
        {
            string status = session.Status;
            if (status != lastStatus)
            {
                lastStatus = status;
                statusLabel.text = WrapStatus(status);
            }

            string roomCode = session.RoomCode;
            bool inRoom = session.InRoom;
            if (typed == lastTyped && roomCode == lastRoomCode && inRoom == lastInRoom)
                return;

            lastTyped = typed;
            lastRoomCode = roomCode;
            lastInRoom = inRoom;
            if (inRoom)
            {
                codeLabel.text = string.IsNullOrWhiteSpace(roomCode) ? "Invite code unavailable" : $"Invite code   {roomCode}";
                return;
            }

            codeLabel.text = "Invite code   " + string.Join(" ", typed.PadRight(CodeLength, '_').ToCharArray());
        }

        private static string WrapStatus(string value)
        {
            const int columns = 49;
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string line = value.Trim().Replace('\n', ' ').Replace('\r', ' ');
            if (line.Length <= columns)
                return line;

            int split = line.LastIndexOf(' ', columns);
            if (split <= 0)
                split = columns;
            string tail = line[split..].TrimStart();
            if (tail.Length > columns)
                tail = tail[..(columns - 1)] + "…";

            return line[..split] + "\n" + tail;
        }

        private void AddAction(string text, int row, Command command, Action activate, bool primary = false)
        {
            AddPad(text, 0.462f, KeyTop - row * 0.127f, 0.43f, 0.103f, command, activate, primary);
        }

        private void AddPad(string text, float x, float y, float width, float height, Command command, Action activate,
            bool primary = false)
        {
            GameObject block = Block(text + " key", new Vector3(x, y, -0.010f), new Vector3(width, height, 0.06f),
                primary ? action : keypad, false);
            pads.Add(new Pad
            {
                Face = block.GetComponent<MeshRenderer>(),
                Caption = Label(text, x, y, command == Command.Character ? 0.070f : 0.050f, Foreground),
                Center = new Vector2(x, y),
                HalfSize = new Vector2(width * 0.5f + 0.002f, height * 0.5f + 0.002f),
                Kind = command,
                Activate = activate,
                Primary = primary
            });
        }

        private TextMesh Label(string text, float x, float y, float size, Color color) =>
            FacilityGeometry.Label(transform, text, new Vector3(x, y, -0.052f), size, color);

        private GameObject Block(string text, Vector3 position, Vector3 size, Material finish, bool collidable = true) =>
            FacilityGeometry.Block(transform, text, position, size, finish, CalibrationLayers.Course, collidable);

        private Material Finish(Material source, string name, Color color)
        {
            var finish = new Material(source) { name = name, color = color };
            finishes.Add(finish);
            return finish;
        }

        private void OnDestroy()
        {
            foreach (Material finish in finishes)
                Destroy(finish);
        }
    }
}
