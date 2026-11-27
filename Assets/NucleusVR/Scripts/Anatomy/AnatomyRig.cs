using System;
using System.Collections.Generic;
using UnityEngine;

namespace NucleusVR.Anatomy
{
    /// <summary>Coordinates host-owned ragdoll bodies and the physical attachments of a specimen.</summary>
    [DisallowMultipleComponent]
    public sealed class AnatomyRig : MonoBehaviour
    {
        private const float MaximumImpulse = 36f;
        private const float MaximumImpactSpeedChange = 7f;

        [SerializeField] private bool authority;
        private readonly Part[] regions = new Part[(int)AnatomyRegion.Count];
        private readonly Dictionary<OrganKind, Part> organs = new();
        private readonly Dictionary<Rigidbody, Part> bodies = new();
        private Behaviour trackedDriver;
        private bool driverEnabledAtBind;

        public bool HasAuthority => authority;
        public bool RagdollActive { get; private set; }

        public void Configure(bool localAuthority) => SetAuthority(localAuthority);

        public void ConfigureTrackedDriver(Behaviour driver)
        {
            if (driver == this || trackedDriver == driver)
                return;
            if (trackedDriver != null)
                trackedDriver.enabled = driverEnabledAtBind;
            trackedDriver = driver;
            driverEnabledAtBind = driver != null && driver.enabled;
            UpdateDriver();
        }

        public void SetAuthority(bool localAuthority)
        {
            if (authority == localAuthority)
                return;
            if (!localAuthority)
                ReleaseGrips();

            authority = localAuthority;
            foreach (Part part in bodies.Values)
            {
                if (authority)
                    part.ReplicaPoseReceived = false;
                ApplyBodyMode(part);
            }
            UpdateDriver();
        }

        public bool BindRegion(AnatomyRegion region, Rigidbody body, Rigidbody parent = null,
            Vector3? worldJointPoint = null)
        {
            int index = (int)region;
            if (index >= regions.Length || regions[index] != null ||
                worldJointPoint.HasValue && !Finite(worldJointPoint.Value))
                return false;

            if (!TryMakePart(body, parent, false, out Part part))
                return false;

            if (parent != null)
            {
                Vector3 pivot = worldJointPoint ?? Vector3.Lerp(parent.position, body.position, 0.5f);
                part.Joint = body.gameObject.AddComponent<ConfigurableJoint>();
                part.Joint.autoConfigureConnectedAnchor = false;
                part.Joint.connectedBody = parent;
                part.Joint.anchor = body.transform.InverseTransformPoint(pivot);
                part.Joint.connectedAnchor = parent.transform.InverseTransformPoint(pivot);
                part.Joint.enablePreprocessing = false;
                part.Joint.projectionDistance = 0.04f;
                part.Joint.projectionAngle = 18f;
            }

            part.Region = region;
            regions[index] = part;
            UpdateJoint(part);
            ApplyBodyMode(part);
            return true;
        }

        public bool BindOrgan(OrganKind organ, Rigidbody body, Rigidbody parent)
        {
            if (parent == null || (int)organ >= (int)OrganKind.Count || organs.ContainsKey(organ) ||
                !TryMakePart(body, parent, true, out Part part))
                return false;

            organs.Add(organ, part);
            ApplyBodyMode(part);
            return true;
        }

        public bool TryGetBody(AnatomyRegion region, out Rigidbody body) => TryGetRegionBody(region, out body);

        public bool TryGetRegionBody(AnatomyRegion region, out Rigidbody body)
        {
            body = GetRegion(region)?.Body;
            return body != null;
        }

        public bool TryGetOrganBody(OrganKind organ, out Rigidbody body)
        {
            body = organs.TryGetValue(organ, out Part part) ? part.Body : null;
            return body != null;
        }

        public bool ShouldDrive(AnatomyRegion region)
        {
            Part part = GetRegion(region);
            return authority && isActiveAndEnabled && !RagdollActive && part?.Body != null && !part.Dynamic;
        }

        public bool SetImpairment(AnatomyRegion region, float amount) =>
            authority && ApplyImpairment(GetRegion(region), amount);

        public bool SetReplicaImpairment(AnatomyRegion region, float amount) =>
            !authority && ApplyImpairment(GetRegion(region), amount);

        public float GetImpairment(AnatomyRegion region) => GetRegion(region)?.Impairment ?? 0f;

        /// <summary>Returns how far a locally driven joint can follow its tracked pose.</summary>
        public float GetTrackingWeight(AnatomyRegion region)
        {
            if (!ShouldDrive(region))
                return 0f;
            float impairment = 0f;
            for (Part part = GetRegion(region); part != null; part = part.Parent)
                impairment = Mathf.Max(impairment, part.Impairment);
            return 1f - impairment;
        }

        public float GetGripStrength(bool useLeftHand)
        {
            AnatomyRegion forearm = useLeftHand ? AnatomyRegion.LeftForearm : AnatomyRegion.RightForearm;
            return GetTrackingWeight(forearm);
        }

        public bool TryDriveRegionPose(AnatomyRegion region, Vector3 position, Quaternion rotation)
        {
            if (!ShouldDrive(region) || !Finite(position) || !ValidRotation(rotation))
                return false;
            Part part = GetRegion(region);
            float weight = GetTrackingWeight(region);
            Transform reference = part.RestParent != null ? part.RestParent : transform;
            Vector3 restPosition = reference.TransformPoint(part.RestPosition);
            Quaternion restRotation = reference.rotation * part.RestRotation;
            if (part.Parent?.Body != null)
            {
                Rigidbody parent = part.Parent.Body;
                restPosition = parent.position + parent.rotation * part.AttachmentPosition;
                restRotation = parent.rotation * part.AttachmentRotation;
            }
            return SetKinematicPose(part, Vector3.Lerp(restPosition, position, weight),
                Quaternion.Slerp(restRotation, rotation, weight));
        }

        public bool SetStage(AnatomyRegion region, AnatomyStage stage) => authority && AdvanceStage(region, stage);
        public bool SetReplicaStage(AnatomyRegion region, AnatomyStage stage) => !authority && AdvanceStage(region, stage);

        public bool ActivateRagdoll()
        {
            if (!authority)
                return false;
            RagdollActive = true;
            UpdateDriver();
            foreach (Part part in regions)
                if (part != null)
                    Activate(part);
            return true;
        }

        public bool ApplyImpact(AnatomyRegion region, Vector3 point, Vector3 impulse)
        {
            Part part = GetRegion(region);
            return authority && part?.Body != null && ApplyImpact(part.Body, point, impulse);
        }

        public bool DetachOrgan(OrganKind organ, Vector3 impulse, Vector3 point)
        {
            if (!authority || !Finite(impulse) || !Finite(point) || !organs.TryGetValue(organ, out Part part) ||
                part.Body == null || part.Detached)
                return false;

            FollowAttachment(part, true);
            part.Detached = true;
            Activate(part);
            ApplyImpact(part.Body, point, impulse);
            return true;
        }

        public bool SetReplicaPose(AnatomyRegion region, Vector3 position, Quaternion rotation) =>
            !authority && SetKinematicPose(GetRegion(region), position, rotation);

        public bool SetReplicaOrganPose(OrganKind organ, Vector3 position, Quaternion rotation)
        {
            if (authority || !organs.TryGetValue(organ, out Part part) || !SetKinematicPose(part, position, rotation))
                return false;
            // An explicitly replicated organ pose owns its transform until the next reset.
            part.ReplicaPoseReceived = true;
            return true;
        }

        public void ResetRig()
        {
            ReleaseGrips();
            RagdollActive = false;
            // Freeze the entire assembly before restoring any local poses.
            foreach (Part part in bodies.Values)
            {
                part.Stage = AnatomyStage.Intact;
                part.Impairment = 0f;
                part.Dynamic = part.Detached = part.ReplicaPoseReceived = false;
                ApplyBodyMode(part);
            }
            foreach (Part part in bodies.Values)
            {
                if (part.Body == null)
                    continue;
                Transform bone = part.Body.transform;
                bone.SetParent(part.RestParent, false);
                bone.SetLocalPositionAndRotation(part.RestPosition, part.RestRotation);
                UpdateJoint(part);
            }
            foreach (Part part in bodies.Values)
            {
                if (part.Body == null)
                    continue;
                part.Body.position = part.Body.transform.position;
                part.Body.rotation = part.Body.transform.rotation;
            }
            UpdateDriver();
        }

        private void FixedUpdate()
        {
            foreach (Part organ in organs.Values)
                if (!organ.Detached && !organ.ReplicaPoseReceived)
                    FollowAttachment(organ, false);
        }

        private void OnDisable()
        {
            ReleaseGrips();
            UpdateDriver();
        }

        private void OnEnable() => UpdateDriver();

        private void UpdateDriver()
        {
            if (trackedDriver != null)
                trackedDriver.enabled = isActiveAndEnabled && authority && !RagdollActive && driverEnabledAtBind;
        }

        private bool AdvanceStage(AnatomyRegion region, AnatomyStage stage)
        {
            Part part = GetRegion(region);
            if (part?.Body == null || stage > AnatomyStage.Severed || stage < part.Stage)
                return false;
            if (stage == part.Stage)
                return true;

            part.Stage = stage;
            if (stage < AnatomyStage.Fractured)
                return true;

            if (stage == AnatomyStage.Severed && part.Parent != null)
            {
                bool inherited = part.Parent.Stage == AnatomyStage.Severed &&
                    (region == AnatomyRegion.LeftForearm && part.Parent.Region == AnatomyRegion.LeftUpperArm ||
                     region == AnatomyRegion.RightForearm && part.Parent.Region == AnatomyRegion.RightUpperArm);
                part.Detached = !inherited;
            }

            UpdateJoint(part);
            if (region == AnatomyRegion.Torso)
            {
                RagdollActive = true;
                UpdateDriver();
            }

            // Every body downstream of the changed joint simulates with that branch. A child marked
            // severed by a parent injury still articulates with the already detached parent.
            foreach (Part candidate in regions)
                if (candidate != null && (RagdollActive || WithinBranch(candidate, part)))
                    Activate(candidate);
            return true;
        }

        private bool TryMakePart(Rigidbody body, Rigidbody parent, bool organ, out Part part)
        {
            part = null;
            if (body == null || body.transform == transform || !body.transform.IsChildOf(transform) ||
                bodies.ContainsKey(body))
                return false;

            Part relative = null;
            if (parent != null && (!bodies.TryGetValue(parent, out relative) || relative.IsOrgan))
                return false;

            Collider[] shapes = Array.FindAll(body.GetComponentsInChildren<Collider>(true),
                item => item.attachedRigidbody == body);
            if (shapes.Length == 0 ||
                Array.Exists(shapes, item => item is MeshCollider mesh && !mesh.convex))
                return false;

            part = new Part(body, relative, shapes, organ);
            bodies.Add(body, part);
            return true;
        }

        private Part GetRegion(AnatomyRegion region)
        {
            int index = (int)region;
            return index < regions.Length ? regions[index] : null;
        }

        private static bool WithinBranch(Part candidate, Part root)
        {
            for (Part current = candidate; current != null; current = current.Parent)
                if (current == root)
                    return true;
            return false;
        }

        private void Activate(Part part)
        {
            part.Dynamic = true;
            ApplyBodyMode(part);
        }

        private void ApplyBodyMode(Part part)
        {
            Rigidbody body = part.Body;
            if (body == null)
                return;
            for (int index = 0; index < part.Shapes.Length; index++)
            {
                Collider shape = part.Shapes[index];
                if (shape != null)
                    shape.isTrigger = !authority || (part.IsOrgan ? !part.Detached : part.OriginalTriggers[index]);
            }

            bool simulate = authority && part.Dynamic;
            if (!simulate && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            if (simulate && body.transform.parent != transform)
                body.transform.SetParent(transform, true);

            body.isKinematic = !simulate;
            body.useGravity = simulate;
            body.interpolation = authority ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.maxDepenetrationVelocity = 2.5f;
            if (simulate)
                body.WakeUp();
        }

        private static void UpdateJoint(Part part)
        {
            ConfigurableJoint joint = part.Joint;
            if (joint == null)
                return;

            ConfigurableJointMotion linear = part.Detached ? ConfigurableJointMotion.Free :
                ConfigurableJointMotion.Locked;
            ConfigurableJointMotion angular = part.Detached ? ConfigurableJointMotion.Free :
                ConfigurableJointMotion.Limited;
            joint.xMotion = joint.yMotion = joint.zMotion = linear;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = angular;
            joint.enableCollision = part.Detached;
            joint.projectionMode = part.Detached ? JointProjectionMode.None : JointProjectionMode.PositionAndRotation;
            bool broken = part.Stage >= AnatomyStage.Fractured;
            bool elbow = part.Region == AnatomyRegion.LeftForearm || part.Region == AnatomyRegion.RightForearm;
            float twist = broken ? 100f : elbow ? 70f : 42f;
            float swing = broken ? 58f : elbow ? 14f : 48f;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -twist };
            joint.highAngularXLimit = new SoftJointLimit { limit = twist };
            joint.angularYLimit = joint.angularZLimit = new SoftJointLimit { limit = swing };
        }

        private static void FollowAttachment(Part part, bool teleport)
        {
            if (part.Body == null || part.Parent?.Body == null)
                return;
            Rigidbody parent = part.Parent.Body;
            Vector3 position = parent.position + parent.rotation * part.AttachmentPosition;
            Quaternion rotation = parent.rotation * part.AttachmentRotation;
            if (teleport)
            {
                part.Body.position = position;
                part.Body.rotation = rotation;
            }
            else
            {
                part.Body.MovePosition(position);
                part.Body.MoveRotation(rotation);
            }
        }

        private static bool ApplyImpact(Rigidbody body, Vector3 point, Vector3 impulse)
        {
            if (body.isKinematic || !Finite(point) || !Finite(impulse))
                return false;

            Vector3 center = body.worldCenterOfMass;
            Vector3 safePoint = center + Vector3.ClampMagnitude(point - center, 0.55f);
            Vector3 safeImpulse = Vector3.ClampMagnitude(impulse,
                Mathf.Min(MaximumImpulse, body.mass * MaximumImpactSpeedChange));
            body.AddForceAtPosition(safeImpulse, safePoint, ForceMode.Impulse);
            return true;
        }

        private static bool SetKinematicPose(Part part, Vector3 position, Quaternion rotation)
        {
            if (part?.Body == null || !Finite(position) || !ValidRotation(rotation))
                return false;
            part.Body.position = position;
            part.Body.rotation = Quaternion.Normalize(rotation);
            return true;
        }

        private static bool ValidRotation(Quaternion rotation)
        {
            float length = Quaternion.Dot(rotation, rotation);
            return length > 0.5f && length < 1.5f;
        }

        private static bool ApplyImpairment(Part part, float amount)
        {
            if (part?.Body == null || !float.IsFinite(amount))
                return false;
            part.Impairment = Mathf.Clamp01(amount);
            return true;
        }

        internal static bool Finite(Vector3 vector) =>
            float.IsFinite(vector.x) && float.IsFinite(vector.y) && float.IsFinite(vector.z);

        internal bool ClaimGrip(Rigidbody body, AnatomyGrip grip)
        {
            if (!authority || !isActiveAndEnabled || body == null || body.isKinematic ||
                !bodies.TryGetValue(body, out Part part) || part.GripOwner != null && part.GripOwner != grip)
                return false;
            part.GripOwner = grip;
            part.Grips++;
            return true;
        }

        internal bool ReleaseGrip(Rigidbody body, AnatomyGrip grip)
        {
            if (ReferenceEquals(body, null) || !bodies.TryGetValue(body, out Part part) ||
                part.GripOwner != grip || part.Grips == 0)
                return false;
            part.Grips--;
            if (part.Grips > 0)
                return false;
            part.GripOwner = null;
            return true;
        }

        private void ReleaseGrips()
        {
            foreach (Part part in bodies.Values)
            {
                if (part.GripOwner != null)
                    part.GripOwner.ReleaseBody(part.Body);
                part.GripOwner = null;
                part.Grips = 0;
            }
        }

        private sealed class Part
        {
            internal readonly Rigidbody Body;
            internal readonly Part Parent;
            internal readonly bool IsOrgan;
            internal readonly Transform RestParent;
            internal readonly Vector3 RestPosition, AttachmentPosition;
            internal readonly Quaternion RestRotation, AttachmentRotation;
            internal readonly Collider[] Shapes;
            internal readonly bool[] OriginalTriggers;
            internal ConfigurableJoint Joint;
            internal AnatomyRegion Region;
            internal AnatomyStage Stage;
            internal float Impairment;
            internal bool Dynamic, Detached, ReplicaPoseReceived;
            internal AnatomyGrip GripOwner;
            internal int Grips;

            internal Part(Rigidbody body, Part parent, Collider[] shapes, bool organ)
            {
                Body = body;
                Parent = parent;
                IsOrgan = organ;
                Shapes = shapes;
                OriginalTriggers = Array.ConvertAll(shapes, shape => shape.isTrigger);
                Transform bone = body.transform;
                RestParent = bone.parent;
                RestPosition = bone.localPosition;
                RestRotation = bone.localRotation;
                if (parent != null)
                {
                    Quaternion inverse = Quaternion.Inverse(parent.Body.rotation);
                    AttachmentPosition = inverse * (body.position - parent.Body.position);
                    AttachmentRotation = inverse * body.rotation;
                }
            }
        }
    }
}
