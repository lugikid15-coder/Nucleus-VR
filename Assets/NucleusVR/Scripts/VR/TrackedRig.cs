using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace NucleusVR.VR
{
    /// <summary>Reads floor-relative OpenXR poses and publishes them beneath a common tracking origin.</summary>
    [DisallowMultipleComponent]
    public sealed class TrackedRig : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;

        private readonly List<XRInputSubsystem> inputSubsystems = new();
        private Pose headPose, leftPose, rightPose;
        private Vector3 headOffset, leftOffset, rightOffset;
        private XRInputSubsystem activeInput;
        private bool synthetic;
        private float nextOriginCheck;

        public Transform Head => head;
        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;
        public bool HeadTracked { get; private set; }
        public bool LeftHandTracked { get; private set; }
        public bool RightHandTracked { get; private set; }
        public bool FloorTrackingActive => synthetic || activeInput != null &&
            (activeInput.GetTrackingOriginMode() & TrackingOriginModeFlags.Floor) != 0;
        public float LeftGrip { get; private set; }
        public float RightGrip { get; private set; }
        public Vector2 TurnInput { get; private set; }
        public Vector3 RawHeadWorldPosition => RawWorldPosition(head, headPose.position);
        public Vector3 RawLeftWorldPosition => RawWorldPosition(leftHand, leftPose.position);
        public Vector3 RawRightWorldPosition => RawWorldPosition(rightHand, rightPose.position);

        public void Configure(Transform trackedHead, Transform trackedLeft, Transform trackedRight)
        {
            head = trackedHead;
            leftHand = trackedLeft;
            rightHand = trackedRight;
        }

        private void OnEnable()
        {
            nextOriginCheck = 0f;
            Application.onBeforeRender += ApplyBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyBeforeRender;
            HeadTracked = LeftHandTracked = RightHandTracked = false;
            TurnInput = Vector2.zero;
            LeftGrip = RightGrip = 0f;
        }

        private void Update()
        {
            if (!synthetic)
            {
                EnsureFloorOrigin();
                SampleDevices();
            }
            ApplyPoses();
        }

        // Physics consumes the Update samples. Render-time samples reduce display latency without changing
        // the fixed-step input snapshots or the collision solver's reference frame.
        private void ApplyBeforeRender()
        {
            if (synthetic)
                return;

            if (HeadTracked)
                ApplyLatePose(head, XRNode.Head, headPose, headOffset);
            if (LeftHandTracked)
                ApplyLatePose(leftHand, XRNode.LeftHand, leftPose, leftOffset);
            if (RightHandTracked)
                ApplyLatePose(rightHand, XRNode.RightHand, rightPose, rightOffset);
        }

        private void EnsureFloorOrigin()
        {
            if (Time.unscaledTime < nextOriginCheck)
                return;

            nextOriginCheck = Time.unscaledTime + 1f;
            if (activeInput != null && activeInput.running && FloorTrackingActive)
                return;

            activeInput = null;
            inputSubsystems.Clear();
            SubsystemManager.GetSubsystems(inputSubsystems);
            foreach (XRInputSubsystem subsystem in inputSubsystems)
            {
                if (!subsystem.running)
                    continue;

                if ((subsystem.GetTrackingOriginMode() & TrackingOriginModeFlags.Floor) != 0 ||
                    (subsystem.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0 &&
                    subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                {
                    activeInput = subsystem;
                    return;
                }
            }
        }

        private void SampleDevices()
        {
            InputDevice headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            HeadTracked = ReadPose(headDevice, ref headPose);
            LeftHandTracked = ReadPose(leftDevice, ref leftPose);
            RightHandTracked = ReadPose(rightDevice, ref rightPose);
            LeftGrip = LeftHandTracked && leftDevice.TryGetFeatureValue(CommonUsages.grip, out float left)
                ? Mathf.Clamp01(left) : 0f;
            RightGrip = RightHandTracked && rightDevice.TryGetFeatureValue(CommonUsages.grip, out float right)
                ? Mathf.Clamp01(right) : 0f;
            TurnInput = RightHandTracked && rightDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis)
                ? axis : Vector2.zero;
        }

        private static bool ReadPose(InputDevice device, ref Pose pose)
        {
            if (!device.isValid ||
                device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked ||
                device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state) &&
                (state & (InputTrackingState.Position | InputTrackingState.Rotation)) !=
                (InputTrackingState.Position | InputTrackingState.Rotation))
                return false;

            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                return false;

            float rotationLength = Quaternion.Dot(rotation, rotation);
            if (!(position.sqrMagnitude < 10000f) || !(rotationLength > 0.5f && rotationLength < 1.5f))
                return false;

            pose = new Pose(position, Quaternion.Normalize(rotation));
            return true;
        }

        public void SetSyntheticTracking(Pose head, Pose leftHand, Pose rightHand, Vector2 turnInput = default,
            float leftGrip = 0f, float rightGrip = 0f)
        {
            synthetic = true;
            HeadTracked = LeftHandTracked = RightHandTracked = true;
            headPose = head;
            leftPose = leftHand;
            rightPose = rightHand;
            TurnInput = turnInput;
            LeftGrip = Mathf.Clamp01(leftGrip);
            RightGrip = Mathf.Clamp01(rightGrip);
            ApplyPoses();
        }

        public void ClearSyntheticTracking()
        {
            synthetic = false;
            HeadTracked = LeftHandTracked = RightHandTracked = false;
            TurnInput = Vector2.zero;
            LeftGrip = RightGrip = 0f;
        }

        internal void SetCollisionOffset(Vector3 worldOffset)
        {
            headOffset = PoseSpaceOffset(head, worldOffset);
            leftOffset = PoseSpaceOffset(leftHand, worldOffset);
            rightOffset = PoseSpaceOffset(rightHand, worldOffset);
            ApplyPoses();
        }

        private void ApplyPoses()
        {
            if (HeadTracked)
                Apply(head, headPose, headOffset);
            if (LeftHandTracked)
                Apply(leftHand, leftPose, leftOffset);
            if (RightHandTracked)
                Apply(rightHand, rightPose, rightOffset);
        }

        private static void ApplyLatePose(Transform target, XRNode node, Pose previous, Vector3 offset)
        {
            Pose pose = default;
            if (!ReadPose(InputDevices.GetDeviceAtXRNode(node), ref pose))
                return;

            Vector3 lateMotion = Vector3.ClampMagnitude(pose.position - previous.position, 0.08f);
            if (offset.sqrMagnitude > 0.000064f)
            {
                Vector3 obstruction = offset.normalized;
                float intoObstacle = Vector3.Dot(lateMotion, obstruction);
                if (intoObstacle < 0f)
                    lateMotion -= obstruction * intoObstacle;
            }
            pose.position = previous.position + lateMotion;
            Apply(target, pose, offset);
        }

        private static void Apply(Transform target, Pose pose, Vector3 offset)
        {
            if (target != null)
                target.SetLocalPositionAndRotation(pose.position + offset, pose.rotation);
        }

        private static Vector3 RawWorldPosition(Transform target, Vector3 position) =>
            target != null && target.parent != null ? target.parent.TransformPoint(position) : position;

        private static Vector3 PoseSpaceOffset(Transform target, Vector3 worldOffset) =>
            target != null && target.parent != null ? target.parent.InverseTransformVector(worldOffset) : worldOffset;
    }
}
