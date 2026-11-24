using System.Collections.Generic;
using NucleusVR.VR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace NucleusVR.Core
{
    [DefaultExecutionOrder(-500)]
    internal sealed class DesktopRigDriver : MonoBehaviour
    {
        private readonly List<XRDisplaySubsystem> displays = new();
        private TrackedRig rig;
        private Vector3 left = new(-0.34f, -0.52f, 0.48f);
        private Vector3 right = new(0.34f, -0.52f, 0.48f);
        private float yaw;
        private float pitch;
        private float nextXrCheck;
        private bool desktop = true;
        private bool menuOpen = true;

        public bool IsDesktop => desktop;
        public bool MenuOpen => menuOpen;
        public bool ResetPressed { get; private set; }

        public void Configure(TrackedRig tracking)
        {
            rig = tracking;
            RefreshMode();
        }

        public void SetMenu(bool open)
        {
            menuOpen = open;
            if (desktop)
                SetCursorCaptured(!open && Application.isFocused);
        }

        private void Update()
        {
            if (rig == null)
                return;

            if (Time.unscaledTime >= nextXrCheck)
            {
                RefreshMode();
                nextXrCheck = Time.unscaledTime + 0.5f;
            }

            ResetPressed = false;
            if (!desktop)
                return;

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null && (keyboard.f1Key.wasPressedThisFrame ||
                                    (keyboard.escapeKey.wasPressedThisFrame && !menuOpen)))
                SetMenu(!menuOpen);

            bool engaged = !menuOpen && Application.isFocused;
            ResetPressed = engaged && keyboard != null && keyboard.rKey.wasPressedThisFrame;
            if (ResetPressed)
            {
                yaw = 0f;
                pitch = 0f;
            }

            bool leftHeld = engaged && mouse != null && mouse.leftButton.isPressed;
            bool rightHeld = engaged && mouse != null && mouse.rightButton.isPressed;
            Vector2 delta = engaged && mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            float reach = engaged && mouse != null ? mouse.scroll.ReadValue().y * 0.0022f : 0f;
            if (!leftHeld && !rightHeld)
            {
                yaw = Mathf.Repeat(yaw + delta.x * 0.13f, 360f);
                pitch = Mathf.Clamp(pitch - delta.y * 0.13f, -83f, 83f);
            }

            left = UpdateHand(left, -0.34f, leftHeld, delta, reach);
            right = UpdateHand(right, 0.34f, rightHeld, delta, reach);

            float height = engaged && keyboard != null && keyboard.leftCtrlKey.isPressed ? 1.12f : 1.63f;
            float turn = engaged && keyboard != null
                ? (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f)
                : 0f;
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
            Vector3 head = new(0f, height, 0f);
            // Resting hands follow the gaze. Dragging uses the same basis, so looking down
            // reaches the floor and looking up reaches ledges.
            Quaternion aim = facing * Quaternion.Euler(pitch, 0f, 0f);
            rig.SetSyntheticTracking(new Pose(head, aim), new Pose(head + aim * left, aim),
                new Pose(head + aim * right, aim), new Vector2(turn, 0f), leftHeld ? 1f : 0f, rightHeld ? 1f : 0f);
        }

        private static Vector3 UpdateHand(Vector3 prior, float side, bool held, Vector2 delta, float depth)
        {
            if (!held)
                return Vector3.MoveTowards(prior, new Vector3(side, -0.48f, 0.51f), Time.unscaledDeltaTime * 1.7f);

            Vector3 next = prior + new Vector3(delta.x * 0.006f, delta.y * 0.006f, depth);
            next.x = Mathf.Clamp(next.x, side - 0.65f, side + 0.65f);
            next.y = Mathf.Clamp(next.y, -1.4f, 0.45f);
            next.z = Mathf.Clamp(next.z, 0.05f, 1.3f);
            return next;
        }

        private void RefreshMode()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bool nextDesktop = false;
#else
            SubsystemManager.GetSubsystems(displays);
            bool nextDesktop = true;
            foreach (XRDisplaySubsystem display in displays)
                if (display.running)
                    nextDesktop = false;
#endif
            if (desktop == nextDesktop)
                return;

            desktop = nextDesktop;
            if (!desktop)
            {
                rig.ClearSyntheticTracking();
                SetCursorCaptured(false);
            }
            else
            {
                SetMenu(true);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (desktop && !focused)
                SetMenu(true);
        }

        private void OnDisable()
        {
            if (rig != null)
                rig.ClearSyntheticTracking();
            if (desktop)
                SetCursorCaptured(false);
        }

        private static void SetCursorCaptured(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
