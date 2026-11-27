using System;
using NucleusVR.VR;
using UnityEngine;

namespace NucleusVR.Movement
{
    /// <summary>Moves the player by resolving tracked hands against the world at the physics tick.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    [DefaultExecutionOrder(100)]
    public sealed class HandLocomotion : MonoBehaviour
    {
        private const float HeadClearance = 0.13f;
        private const float QuerySkin = 0.005f;
        private const float MaximumFallSpeed = 14f;
        private const float SnapEngage = 0.72f;
        private const float SnapRelease = 0.35f;

        [SerializeField] private TrackedRig rig;
        [SerializeField] private CapsuleCollider bodyCollider;
        [SerializeField] private Transform leftContact;
        [SerializeField] private Transform rightContact;
        [SerializeField] private LayerMask environment = ~0;
        [SerializeField, Min(0.1f)] private float maximumDriveSpeed = 5f;
        [SerializeField, Min(0.1f)] private float maximumReleaseSpeed = 5f;
        [SerializeField, Range(0.25f, 1.3f)] private float armReach = 1.05f;
        [SerializeField, Range(15f, 90f)] private float snapDegrees = 30f;
        [SerializeField] private bool smoothTurning;
        [SerializeField, Range(25f, 180f)] private float smoothDegreesPerSecond = 85f;

        private Rigidbody body;
        private HandContact left, right;
        private LocomotionCollisionQueries queries;
        private bool turnHeld, leftSupported, rightSupported, supportedWeight, recordedHead;
        private Vector3 previousPhysicsPosition, recentVelocity, previousHeadLocal;
        private Vector3 trackingCorrection;
        private Vector3? carryTarget;
        private float roleDrive = 1f;

        public Transform LeftContact => leftContact;
        public Transform RightContact => rightContact;
        public Collider LeftHandSurface => left?.Anchor;
        public Collider RightHandSurface => right?.Anchor;
        public float HeadObstructionMetres => trackingCorrection.magnitude;
        public event Action<Collider, Vector3, float> SurfaceTapped;

        public void Configure(TrackedRig trackedRig, CapsuleCollider capsule, Transform physicalLeft,
            Transform physicalRight, LayerMask environmentLayers)
        {
            rig = trackedRig;
            bodyCollider = capsule;
            leftContact = physicalLeft;
            rightContact = physicalRight;
            environment = environmentLayers;
            Initialize();
        }

        public void SetSmoothTurning(bool enabled)
        {
            smoothTurning = enabled;
            turnHeld = true;
        }

        internal void SetRoleDrive(float scale) => roleDrive = Mathf.Clamp(scale, 0.65f, 1.35f);

        internal void SetCarryTarget(Vector3? target) => carryTarget = target;

        internal void ApplyGameplayImpulse(Vector3 velocity)
        {
            if (body != null)
                body.AddForce(Vector3.ClampMagnitude(velocity, 4f), ForceMode.VelocityChange);
        }

        public void ResetTo(Vector3 position, Quaternion yaw)
        {
            if (body == null)
                Initialize();
            body.position = position;
            body.rotation = Quaternion.Euler(0f, yaw.eulerAngles.y, 0f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = true;
            trackingCorrection = Vector3.zero;
            carryTarget = null;
            rig?.SetCollisionOffset(Vector3.zero);
            ResetSupport();
            turnHeld = false;
            recordedHead = false;
        }

        private void Awake() => Initialize();

        private void OnDisable()
        {
            if (body != null)
                body.useGravity = true;
            ResetSupport();
            trackingCorrection = Vector3.zero;
            rig?.SetCollisionOffset(Vector3.zero);
        }

        private void Initialize()
        {
            body = GetComponent<Rigidbody>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<CapsuleCollider>();
            bodyCollider.direction = 1;
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints |= RigidbodyConstraints.FreezeRotation;
            body.maxDepenetrationVelocity = 1.8f;

            left?.Reset();
            right?.Reset();
            left = new HandContact(leftContact);
            right = new HandContact(rightContact);
            queries = new LocomotionCollisionQueries(body, left.Sphere, right.Sphere, environment);
            ResetSupport();
        }

        private static void IgnoreHandPair(Collider first, Collider second)
        {
            // Tracking loss can disable a hand collider, which can also invalidate ignored pairs.
            if (first != null && first.enabled && second != null && second.enabled &&
                !Physics.GetIgnoreCollision(first, second))
                Physics.IgnoreCollision(first, second);
        }

        private void FixedUpdate()
        {
            if (rig == null || !rig.HeadTracked || !rig.FloorTrackingActive)
            {
                body.useGravity = true;
                recordedHead = false;
                ResetSupport();
                LimitFallSpeed();
                return;
            }

            float step = Time.fixedDeltaTime;
            Vector3 headLocal = transform.InverseTransformPoint(rig.RawHeadWorldPosition);
            bool recentered = recordedHead && (headLocal - previousHeadLocal).sqrMagnitude > 0.45f * 0.45f;
            previousHeadLocal = headLocal;
            recordedHead = true;
            if (recentered)
                ResetSupport();

            if (supportedWeight)
            {
                Vector3 measured = (body.position - previousPhysicsPosition) / step;
                measured = Vector3.ClampMagnitude(measured, maximumReleaseSpeed);
                recentVelocity = Vector3.Lerp(recentVelocity, measured, 1f - Mathf.Exp(-step / 0.055f));
            }
            previousPhysicsPosition = body.position;

            UpdateCapsule(headLocal);
            bool turned = TryTurn(step);
            if (turned)
                ResetSupport();

            Vector3 trackingHead = headLocal + trackingCorrection;
            Quaternion yaw = body.rotation;
            Vector3 leftDrive = ResolveHand(left, rig.LeftHandTracked, rig.RawLeftWorldPosition, rig.LeftHand,
                trackingHead, -0.18f, yaw, step);
            Vector3 rightDrive = ResolveHand(right, rig.RightHandTracked, rig.RawRightWorldPosition, rig.RightHand,
                trackingHead, 0.18f, yaw, step);
            IgnoreHandPair(bodyCollider, left.Sphere);
            IgnoreHandPair(bodyCollider, right.Sphere);
            IgnoreHandPair(left.Sphere, right.Sphere);
            bool hasLeftAnchor = left.Anchor != null;
            bool hasRightAnchor = right.Anchor != null;
            int anchors = (hasLeftAnchor ? 1 : 0) + (hasRightAnchor ? 1 : 0);

            if (carryTarget.HasValue)
            {
                Vector3 pull = carryTarget.Value - body.position;
                pull.y = 0f;
                if (pull.sqrMagnitude < 6.25f)
                {
                    body.useGravity = true;
                    Vector3 velocity = Vector3.ClampMagnitude(pull * 3.2f, 2.3f);
                    velocity.y = body.linearVelocity.y;
                    body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, velocity, step * 9f);
                    ResetSupport();
                    LimitFallSpeed();
                    return;
                }
            }

            if (anchors > 0 && !turned && !recentered)
            {
                supportedWeight = left.SupportsWeight || right.SupportsWeight;
                body.useGravity = !supportedWeight;
                Vector3 drive = Vector3.ClampMagnitude((leftDrive + rightDrive) / (step * anchors),
                    maximumDriveSpeed * roleDrive);
                // A fully slippery wall still resists penetration but cannot hold the player's weight.
                Vector3 carry = Vector3.zero;
                if (!supportedWeight)
                {
                    recentVelocity = Vector3.zero;
                    carry = Vector3.up * body.linearVelocity.y;
                    if (hasLeftAnchor)
                        carry = Vector3.ProjectOnPlane(carry, left.Normal);
                    if (hasRightAnchor)
                        carry = Vector3.ProjectOnPlane(carry, right.Normal);
                }
                body.linearVelocity = drive + carry;
                LimitFallSpeed();
                leftSupported = hasLeftAnchor;
                rightSupported = hasRightAnchor;
            }
            else
            {
                body.useGravity = true;
                bool intentionalRelease = supportedWeight &&
                    (!leftSupported || rig.LeftHandTracked) && (!rightSupported || rig.RightHandTracked);
                if (intentionalRelease)
                    body.linearVelocity = Vector3.ClampMagnitude(recentVelocity, maximumReleaseSpeed);
                else
                    LimitFallSpeed();
                leftSupported = rightSupported = supportedWeight = false;
                recentVelocity = Vector3.zero;
            }

            NotifyTap(left.Tap);
            NotifyTap(right.Tap);
        }

        private void NotifyTap(HandContact.Impact impact)
        {
            if (impact.Collider != null)
                SurfaceTapped?.Invoke(impact.Collider, impact.Point, impact.Strength);
        }

        private Vector3 ResolveHand(HandContact hand, bool tracked, Vector3 worldTrackingPosition, Transform pose,
            Vector3 localHead, float shoulderSide, Quaternion yaw, float step)
        {
            Vector3 shoulder = localHead + new Vector3(shoulderSide, -0.2f, 0f);
            Vector3 controller = transform.InverseTransformPoint(worldTrackingPosition) + trackingCorrection;
            Vector3 reachable = shoulder + Vector3.ClampMagnitude(controller - shoulder, armReach);
            Vector3 center = body.position + yaw * shoulder;
            Vector3 desired = body.position + yaw * reachable;
            Quaternion rotation = pose == null ? yaw : yaw * (Quaternion.Inverse(transform.rotation) * pose.rotation);
            return hand.Resolve(tracked, center, desired, rotation, queries, step);
        }

        private void UpdateCapsule(Vector3 localHead)
        {
            Vector3 center = bodyCollider.center;
            float radius = bodyCollider.radius;
            float desiredHeight = Mathf.Clamp(localHead.y + HeadClearance,
                2f * Mathf.Max(radius, HeadClearance), 2.4f);
            float height = Mathf.Min(bodyCollider.height, desiredHeight);
            Vector3 from = new(center.x, 0f, center.z);
            Vector3 wanted = new(localHead.x, 0f, localHead.z);
            Vector3 feet = body.position + body.rotation * from;
            Vector3 offset = body.rotation * (wanted - from);

            // Sweep laterally before resizing, so a recenter or a fast room-scale step cannot cross thin walls.
            Vector3 moved = SweepCapsule(feet, height, offset, out RaycastHit hit);
            if (hit.collider != null)
            {
                Vector3 slide = Vector3.ProjectOnPlane(offset - moved, hit.normal);
                moved += SweepCapsule(feet + moved, height, Vector3.ProjectOnPlane(slide, Vector3.up), out _);
            }
            if (moved.sqrMagnitude > 0.00000025f && !IsBlocked(feet + moved, height))
                feet += moved;

            if (desiredHeight - height >= 0.001f)
                height = UnobstructedHeight(feet, height, desiredHeight);

            Vector3 corrected = Quaternion.Inverse(body.rotation) * (feet - body.position);
            if (Mathf.Abs(bodyCollider.height - height) >= 0.001f)
                bodyCollider.height = height;
            Vector3 updatedCenter = new(corrected.x, bodyCollider.height * 0.5f, corrected.z);
            if ((center - updatedCenter).sqrMagnitude >= 0.00000025f)
                bodyCollider.center = updatedCenter;

            float safeHead = Mathf.Clamp(localHead.y, HeadClearance, bodyCollider.height - HeadClearance);
            trackingCorrection = new Vector3(bodyCollider.center.x - localHead.x, safeHead - localHead.y,
                bodyCollider.center.z - localHead.z);
            if (trackingCorrection.sqrMagnitude < 0.000004f)
                trackingCorrection = Vector3.zero;
            rig.SetCollisionOffset(transform.TransformVector(trackingCorrection));
        }

        private float UnobstructedHeight(Vector3 feet, float lower, float upper)
        {
            if (!IsBlocked(feet, upper))
                return upper;

            for (int pass = 0; pass < 6; pass++)
            {
                float middle = (lower + upper) * 0.5f;
                if (IsBlocked(feet, middle))
                    upper = middle;
                else
                    lower = middle;
            }
            return lower;
        }

        private Vector3 SweepCapsule(Vector3 feet, float height, Vector3 motion, out RaycastHit hit)
        {
            GetEndpoints(feet, height, out Vector3 bottom, out Vector3 top);
            return queries.CapsuleCast(bottom, top, Mathf.Max(0.001f, bodyCollider.radius - QuerySkin), motion,
                out hit)
                ? motion.normalized * Mathf.Max(0f, hit.distance - QuerySkin)
                : motion;
        }

        private bool IsBlocked(Vector3 feet, float height)
        {
            GetEndpoints(feet, height, out Vector3 bottom, out Vector3 top);
            return queries.OverlapsBody(bottom, top, Mathf.Max(0.001f, bodyCollider.radius - QuerySkin));
        }

        private void GetEndpoints(Vector3 feet, float height, out Vector3 bottom, out Vector3 top)
        {
            float radius = bodyCollider.radius;
            bottom = feet + Vector3.up * radius;
            top = feet + Vector3.up * (height - radius);
        }

        private bool TryTurn(float step)
        {
            float axis = rig.TurnInput.x;
            float absolute = Mathf.Abs(axis);
            if (absolute <= SnapRelease)
                turnHeld = false;

            float angle;
            if (smoothTurning)
            {
                float magnitude = Mathf.InverseLerp(SnapRelease, 1f, absolute);
                angle = Mathf.Sign(axis) * magnitude * smoothDegreesPerSecond * step;
            }
            else
            {
                if (absolute < SnapEngage || turnHeld)
                    return false;

                angle = Mathf.Sign(axis) * snapDegrees;
                turnHeld = true;
            }

            if (Mathf.Abs(angle) < 0.001f)
                return false;

            Vector3 center = bodyCollider.center;
            Vector3 localPivot = new(center.x, 0f, center.z);
            Vector3 worldPivot = body.position + body.rotation * localPivot;
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f) * body.rotation;
            // The capsule stays at the same world position under the headset across a turn.
            body.position = worldPivot - rotation * localPivot;
            body.rotation = rotation;
            return true;
        }

        private void LimitFallSpeed()
        {
            Vector3 velocity = body.linearVelocity;
            if (velocity.y < -MaximumFallSpeed)
            {
                velocity.y = -MaximumFallSpeed;
                body.linearVelocity = velocity;
            }
        }

        private void ResetSupport()
        {
            leftSupported = rightSupported = supportedWeight = false;
            recentVelocity = Vector3.zero;
            left?.Reset();
            right?.Reset();
            if (body != null)
                previousPhysicsPosition = body.position;
        }
    }
}
