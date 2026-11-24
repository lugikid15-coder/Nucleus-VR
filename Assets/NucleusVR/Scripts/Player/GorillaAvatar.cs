using System;
using UnityEngine;

namespace NucleusVR.Player
{
    /// <summary>Poses the same avatar from local tracked targets or replicated targets.</summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class GorillaAvatar : MonoBehaviour
    {
        private const float UpperArmLength = 0.34f;
        private const float ForearmLength = 0.32f;
        private const float Response = 24f;

        [Serializable]
        internal sealed class Arm
        {
            public Transform Shoulder;
            public Transform UpperSkin;
            public Transform Elbow;
            public Transform ForearmSkin;
            public Transform Wrist;
            public Transform[] Knuckles;
            public Transform[] MiddleJoints;
            public Transform ThumbBase;
            public Transform ThumbJoint;

            public void Pose(Vector3 handPosition, Quaternion handRotation, Quaternion bodyRotation,
                float side, float grip)
            {
                Vector3 shoulderPosition = Shoulder.position;
                Vector3 toHand = handPosition - shoulderPosition;
                float distance = toHand.magnitude;
                Vector3 towardHand = distance > 0.001f ? toHand / distance : bodyRotation * Vector3.forward;
                Vector3 outward = bodyRotation * new Vector3(side, -0.85f, -0.35f);
                Vector3 bend = Vector3.ProjectOnPlane(outward, towardHand);
                if (bend.sqrMagnitude < 0.0001f)
                    bend = Vector3.ProjectOnPlane(bodyRotation * Vector3.up, towardHand);

                // Stretch the skin for long reaches while leaving the wrist on the tracked contact.
                Vector3 elbowPosition = SolveElbow(shoulderPosition, towardHand, bend.normalized, distance,
                    UpperArmLength, ForearmLength);
                Shoulder.rotation = DirectionRotation(elbowPosition - shoulderPosition, bodyRotation);
                Elbow.position = elbowPosition;
                Elbow.rotation = DirectionRotation(handPosition - elbowPosition, bodyRotation);
                Wrist.SetPositionAndRotation(handPosition, handRotation);
                UpperSkin.localScale = new Vector3(1f, 1f,
                    Vector3.Distance(shoulderPosition, elbowPosition) / UpperArmLength);
                ForearmSkin.localScale = new Vector3(1f, 1f,
                    Vector3.Distance(elbowPosition, handPosition) / ForearmLength);

                float center = (Knuckles.Length - 1) * 0.5f;
                float spreadPerFinger = ThumbBase == null ? 11f : side * 14f;
                for (int finger = 0; finger < Knuckles.Length; finger++)
                {
                    float spread = (finger - center) * spreadPerFinger * (1f - grip * 0.75f);
                    Knuckles[finger].localRotation = Quaternion.Euler(8f + 63f * grip, spread, 0f);
                    MiddleJoints[finger].localRotation = Quaternion.Euler(9f + 77f * grip, 0f, 0f);
                }

                if (ThumbBase != null && ThumbJoint != null)
                {
                    // Rotate across the palm as the two fingers curl.
                    ThumbBase.localRotation = Quaternion.Euler(18f + 34f * grip, -side * (55f - 74f * grip), 0f);
                    ThumbJoint.localRotation = Quaternion.Euler(8f + 40f * grip, 0f, 0f);
                }
            }

            private static Quaternion DirectionRotation(Vector3 direction, Quaternion bodyRotation)
            {
                if (direction.sqrMagnitude < 0.000001f)
                    return bodyRotation;

                Vector3 unit = direction.normalized;
                Vector3 up = bodyRotation * Vector3.up;
                if (Mathf.Abs(Vector3.Dot(unit, up)) > 0.95f)
                    up = bodyRotation * Vector3.forward;

                return Quaternion.LookRotation(unit, up);
            }
        }

        [SerializeField] private Transform trackedHead;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Transform jaw;
        [SerializeField] private Arm leftArm;
        [SerializeField] private Arm rightArm;
        [SerializeField] private SkinnedMeshRenderer bodyRenderer;

        private Vector3 facing = Vector3.forward;
        private bool leftTracked = true;
        private bool rightTracked = true;
        private Vector3 desiredExpression;
        private Vector3 expression;
        private UnityEngine.Object[] transientAssets;

        internal bool HasModel => modelRoot != null;

        public void SetTrackingTargets(Transform headTarget, Transform leftTarget, Transform rightTarget)
        {
            trackedHead = headTarget;
            leftHand = leftTarget;
            rightHand = rightTarget;
        }

        public void SetFirstPerson(bool firstPerson)
        {
            if (bodyRenderer != null)
                bodyRenderer.enabled = !firstPerson;
        }

        public void SetTrackingAvailability(bool left, bool right)
        {
            leftTracked = left;
            rightTracked = right;
        }

        public void SetExpression(float leftGrip, float rightGrip, float jawOpen)
        {
            desiredExpression = new Vector3(UnitValue(leftGrip), UnitValue(rightGrip), UnitValue(jawOpen));
        }

        internal void Initialize(Transform root, Transform headBone, Transform jawBone, Arm left,
            Arm right, SkinnedMeshRenderer bodySkin)
        {
            modelRoot = root;
            head = headBone;
            jaw = jawBone;
            leftArm = left;
            rightArm = right;
            bodyRenderer = bodySkin;
            Vector3 planar = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            facing = planar.sqrMagnitude > 0.0001f ? planar.normalized : Vector3.forward;
        }

        internal void OwnAssets(params UnityEngine.Object[] assets)
        {
            transientAssets = assets;
        }

        private void LateUpdate()
        {
            RefreshPose(Time.deltaTime);
        }

        internal void RefreshPose(float deltaTime)
        {
            if (modelRoot == null || trackedHead == null)
                return;

            Vector3 planar = Vector3.ProjectOnPlane(trackedHead.forward, Vector3.up);
            if (planar.sqrMagnitude > 0.04f)
                facing = planar.normalized;

            Quaternion bodyRotation = Quaternion.LookRotation(facing, Vector3.up);
            modelRoot.SetPositionAndRotation(trackedHead.position - Vector3.up * 0.72f, bodyRotation);
            head.SetPositionAndRotation(trackedHead.position, trackedHead.rotation);
            expression = Vector3.Lerp(expression, desiredExpression, 1f - Mathf.Exp(-deltaTime * Response));
            jaw.localRotation = Quaternion.Euler(27f * expression.z, 0f, 0f);
            PoseArm(leftArm, leftHand, leftTracked, bodyRotation, -1f, expression.x);
            PoseArm(rightArm, rightHand, rightTracked, bodyRotation, 1f, expression.y);
        }

        private void PoseArm(Arm arm, Transform target, bool tracked, Quaternion bodyRotation,
            float side, float grip)
        {
            if (tracked && target != null)
            {
                arm.Pose(target.position, target.rotation, bodyRotation, side, grip);
                return;
            }

            // A lost controller settles into a relaxed position below the shoulder.
            Vector3 idle = modelRoot.TransformPoint(new Vector3(side * 0.42f, -0.04f, 0.19f));
            Quaternion rotation = bodyRotation * Quaternion.Euler(55f, side * 14f, 0f);
            arm.Pose(idle, rotation, bodyRotation, side, 0f);
        }

        private void OnDestroy()
        {
            if (transientAssets == null)
                return;

            foreach (UnityEngine.Object asset in transientAssets)
            {
                if (asset == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(asset);
                else
                    DestroyImmediate(asset);
            }
        }

        internal static Vector3 SolveElbow(Vector3 shoulder, Vector3 direction, Vector3 bend,
            float distance, float upper, float forearm)
        {
            float stretch = Mathf.Max(1f, distance / (upper + forearm));
            upper *= stretch;
            forearm *= stretch;
            float reach = Mathf.Max(distance, Mathf.Abs(upper - forearm) + 0.001f);
            float along = (upper * upper - forearm * forearm + reach * reach) / (2f * reach);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            return shoulder + direction * along + bend * height;
        }

        private static float UnitValue(float value)
        {
            return float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
        }
    }
}
