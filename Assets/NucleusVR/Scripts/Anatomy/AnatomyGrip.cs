using System.Collections.Generic;
using UnityEngine;

namespace NucleusVR.Anatomy
{
    /// <summary>Creates independent physical holds from two authority-sampled kinematic hands.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(125)]
    public sealed class AnatomyGrip : MonoBehaviour
    {
        private const float ContactTolerance = 0.045f;
        private const float WristDiscontinuity = 0.35f;
        private const float VelocitySmoothingTime = 0.045f;

        [SerializeField] private bool authority;
        [SerializeField, Range(0.1f, 0.4f)] private float reach = 0.28f;
        [SerializeField, Range(1f, 12f)] private float maximumThrowSpeed = 7.5f;

        private readonly Hold left = new();
        private readonly Hold right = new();
        private readonly List<Collider> shapes = new();

        public void Configure(Rigidbody leftHand, Rigidbody rightHand, bool localAuthority)
        {
            ReleaseAll();
            left.Hand = leftHand;
            right.Hand = rightHand != leftHand ? rightHand : null;
            ResetWrist(left);
            ResetWrist(right);
            authority = localAuthority;
        }

        public void SetAuthority(bool localAuthority)
        {
            if (authority == localAuthority)
                return;
            ReleaseAll();
            ResetWrist(left);
            ResetWrist(right);
            authority = localAuthority;
        }

        public Rigidbody GetHeldBody(bool useLeftHand) => Slot(useLeftHand).Body;

        public void ResetHandMotion(bool useLeftHand) => ResetWrist(Slot(useLeftHand));

        public bool TryGrab(bool useLeftHand, Rigidbody body, Vector3 worldPoint)
        {
            Hold slot = Slot(useLeftHand);
            if (!authority || !isActiveAndEnabled || body == null || body.isKinematic ||
                !body.gameObject.activeInHierarchy ||
                !UsableHand(slot.Hand) || slot.Hand == body || !AnatomyRig.Finite(worldPoint) ||
                (slot.Hand.position - worldPoint).sqrMagnitude > reach * reach)
                return false;
            if (slot.Body == null && !ReferenceEquals(slot.Body, null))
                Release(slot, false);
            if (slot.Body != null)
                return slot.Body == body;
            if (!OnSurface(body, worldPoint))
                return false;

            AnatomyRig rig = body.GetComponentInParent<AnatomyRig>();
            if (rig == null || !rig.ClaimGrip(body, this))
                return false;

            slot.Rig = rig;
            slot.Body = body;
            ConfigurableJoint joint = body.gameObject.AddComponent<ConfigurableJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = slot.Hand;
            joint.anchor = body.transform.InverseTransformPoint(worldPoint);
            joint.connectedAnchor = slot.Hand.transform.InverseTransformPoint(worldPoint);
            joint.enablePreprocessing = false;
            joint.enableCollision = false;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            var drive = new JointDrive
            {
                positionSpring = 780f,
                positionDamper = 60f,
                maximumForce = 700f
            };
            joint.xDrive = joint.yDrive = joint.zDrive = drive;
            slot.Joint = joint;
            body.WakeUp();
            return true;
        }

        public bool Release(bool useLeftHand, bool throwOnRelease = true) =>
            Release(Slot(useLeftHand), throwOnRelease);

        public void ReleaseAll()
        {
            Release(left, false);
            Release(right, false);
        }

        internal void ReleaseBody(Rigidbody body)
        {
            if (ReferenceEquals(left.Body, body))
                Release(left, false);
            if (ReferenceEquals(right.Body, body))
                Release(right, false);
        }

        private void FixedUpdate()
        {
            if (!authority)
                return;

            SampleWrist(left, Time.fixedDeltaTime);
            SampleWrist(right, Time.fixedDeltaTime);
        }

        private void OnDisable()
        {
            ReleaseAll();
            ResetWrist(left);
            ResetWrist(right);
        }

        private void SampleWrist(Hold slot, float step)
        {
            if (!UsableHand(slot.Hand))
            {
                Release(slot, false);
                ResetWrist(slot);
                return;
            }
            if (!ReferenceEquals(slot.Body, null) &&
                (slot.Body == null || slot.Body.isKinematic || slot.Joint == null ||
                 slot.Rig == null || !slot.Rig.HasAuthority))
                Release(slot, false);

            Vector3 position = slot.Hand.position;
            if (!slot.HasSample)
            {
                slot.HasSample = true;
                slot.LastWrist = position;
                return;
            }

            Vector3 travel = position - slot.LastWrist;
            slot.LastWrist = position;
            if (!AnatomyRig.Finite(travel) || travel.sqrMagnitude > WristDiscontinuity * WristDiscontinuity)
            {
                Release(slot, false);
                slot.Velocity = Vector3.zero;
                return;
            }
            Vector3 measured = Vector3.ClampMagnitude(travel / step, maximumThrowSpeed);
            slot.Velocity = Vector3.Lerp(slot.Velocity, measured, 1f - Mathf.Exp(-step / VelocitySmoothingTime));
        }

        private bool Release(Hold slot, bool throwOnRelease)
        {
            Rigidbody body = slot.Body;
            if (ReferenceEquals(body, null))
                return false;
            AnatomyRig rig = slot.Rig;
            if (slot.Joint != null)
            {
                // Destroy is deferred until the frame boundary. Remove the spring first so a
                // released piece cannot be pulled back during another fixed step this frame.
                slot.Joint.xDrive = slot.Joint.yDrive = slot.Joint.zDrive = default;
                slot.Joint.enableCollision = true;
                Destroy(slot.Joint);
            }
            slot.Joint = null;
            slot.Body = null;
            slot.Rig = null;
            bool lastHand = rig != null && rig.ReleaseGrip(body, this);
            if (throwOnRelease && authority && lastHand && rig.HasAuthority && body != null &&
                !body.isKinematic && UsableHand(slot.Hand))
                body.linearVelocity = Vector3.ClampMagnitude(
                    Vector3.Lerp(body.linearVelocity, slot.Velocity, 0.65f), maximumThrowSpeed);
            return true;
        }

        private bool OnSurface(Rigidbody body, Vector3 point)
        {
            body.GetComponentsInChildren(false, shapes);
            foreach (Collider shape in shapes)
            {
                if (!shape.enabled || shape.isTrigger || shape.attachedRigidbody != body)
                    continue;
                Vector3 delta = shape.ClosestPoint(point) - point;
                if (delta.sqrMagnitude <= ContactTolerance * ContactTolerance)
                    return true;
            }
            return false;
        }

        private Hold Slot(bool useLeftHand) => useLeftHand ? left : right;

        private static bool UsableHand(Rigidbody hand) => hand != null &&
            hand.isKinematic && hand.gameObject.activeInHierarchy;

        private static void ResetWrist(Hold slot)
        {
            slot.HasSample = false;
            slot.LastWrist = slot.Velocity = Vector3.zero;
        }

        private sealed class Hold
        {
            internal Rigidbody Hand, Body;
            internal AnatomyRig Rig;
            internal ConfigurableJoint Joint;
            internal bool HasSample;
            internal Vector3 LastWrist, Velocity;
        }
    }
}
