using System;
using NucleusVR.Core;
using NucleusVR.Gore;
using UnityEngine;

namespace NucleusVR.Anatomy
{
    /// <summary>Maps damage state to the visible anatomy and physical regions of one subject.</summary>
    internal sealed class AnatomySpecimen : MonoBehaviour
    {
        private readonly AnatomyVisuals.RegionView[] regions = new AnatomyVisuals.RegionView[(int)AnatomyRegion.Count];
        private readonly AnatomyVisuals.OrganView[] organs = new AnatomyVisuals.OrganView[(int)OrganKind.Count];
        private readonly Collider[] organShapes = new Collider[(int)OrganKind.Count];
        private readonly bool[] announcedFracture = new bool[(int)AnatomyRegion.Count];
        private AnatomySnapshot replica;
        private GoreEffects gore;
        private AnatomyIdleDriver poseDriver;

        public ushort Id { get; private set; }
        public AnatomyRig Rig { get; private set; }
        public AnatomyDamageState State { get; private set; }
        public AnatomyRegion LastRegion { get; private set; } = AnatomyRegion.Torso;
        public OrganKind? LastOrgan { get; private set; }
        public bool OrganInteractionVisible => gore.Quality != GoreQuality.Off;
        public event Action<AnatomySpecimen> Presented;

        public void Build(ushort id, AnatomyVisuals visuals, GoreEffects effects, AnatomyProfile profile, float scale)
        {
            Id = id;
            State = new AnatomyDamageState(profile);
            gore = effects;
            Rig = gameObject.AddComponent<AnatomyRig>();
            Rig.Configure(true);

            Rigidbody torso = Part(AnatomyRegion.Torso, new Vector3(0f, 1.28f, 0f),
                new Vector3(0.63f, 0.63f, 0.44f), 18f, 0f, scale, null, visuals);
            Part(AnatomyRegion.Head, new Vector3(0f, 1.82f, 0.035f),
                new Vector3(0.38f, 0.38f, 0.34f), 4f, 0f, scale, torso, visuals);
            BuildArm(false, scale, torso, visuals);
            BuildArm(true, scale, torso, visuals);

            for (int i = 0; i < organs.Length; i++)
                BuildOrgan((OrganKind)i, scale, visuals);
            poseDriver = gameObject.AddComponent<AnatomyIdleDriver>();
            poseDriver.Configure(Rig, scale > 1.05f ? 1f : 0f);
            Refresh();
        }

        public AnatomyRegionState ReadRegion(AnatomyRegion region) =>
            replica == null ? State.GetRegion(region) : replica.GetRegion(region);
        public AnatomyOrganState ReadOrgan(OrganKind organ) =>
            replica == null ? State.GetOrgan(organ) : replica.GetOrgan(organ);

        public void PresentSnapshot(AnatomySnapshot snapshot)
        {
            replica = Rig.HasAuthority ? null : snapshot;
            Refresh();
        }

        public void AuthorityChanged(bool authority)
        {
            if (authority)
                replica = null;
            Refresh();
        }

        public bool Strike(AnatomyHitbox hit, Vector3 point, Vector3 velocity)
        {
            if (!Rig.HasAuthority || hit.Subject != this)
                return false;

            Vector3 direction = velocity.normalized;
            float speed = Mathf.Clamp(velocity.magnitude, 0f, 10f);
            float amount = Mathf.Clamp(speed * 5.8f, 6f, 43f);
            bool changed = State.ApplyDamage(hit.Region, amount, hit.Organ);
            if (speed >= 6f && !hit.Organ.HasValue)
            {
                changed |= State.ApplyFracture(hit.Region, speed * 2.1f);
                changed |= State.ApplyJointDamage(hit.Region, speed * 1.1f);
            }

            ApplyRigStages();
            Vector3 impulse = direction * Mathf.Min(hit.Body.mass * 2f, speed * 0.9f);
            if (hit.Organ.HasValue && State.GetOrgan(hit.Organ.Value).IsDetached)
                hit.Body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
            else
                Rig.ApplyImpact(hit.Region, point, impulse);

            SeenImpact(hit.Region, hit.Organ, point, direction, Mathf.Clamp01(speed / 7f));
            return changed;
        }

        public bool Extract(AnatomyHitbox hit, Vector3 point, Vector3 direction)
        {
            if (!Rig.HasAuthority || hit.Subject != this || !hit.Organ.HasValue)
                return false;

            OrganKind organ = hit.Organ.Value;
            if (State.GetOrgan(organ).IsDetached)
                return true;
            if (!State.TryDetachOrgan(organ))
                return false;

            Rig.DetachOrgan(organ, direction.normalized * 0.23f, point);
            SeenImpact(hit.Region, organ, point, direction, 0.7f);
            return true;
        }

        public void SeenImpact(AnatomyRegion region, OrganKind? organ, Vector3 point, Vector3 direction, float strength)
        {
            LastRegion = region;
            LastOrgan = organ;
            Rig.TryGetBody(region, out Rigidbody attachment);
            AnatomyStage stage = ReadRegion(region).Stage;
            if (organ.HasValue)
            {
                Rig.TryGetOrganBody(organ.Value, out attachment);
                AnatomyOrganState part = ReadOrgan(organ.Value);
                if (part.IsDetached)
                    stage = AnatomyStage.Severed;
                else if (stage < AnatomyStage.Wounded)
                    stage = AnatomyStage.Wounded;
            }
            Vector3 outward = -direction;
            if (attachment != null && (point - attachment.worldCenterOfMass).sqrMagnitude > 0.001f)
                outward = (point - attachment.worldCenterOfMass).normalized;
            bool fracture = !organ.HasValue && stage >= AnatomyStage.Fractured && !announcedFracture[(int)region];
            if (fracture)
                announcedFracture[(int)region] = true;
            gore.Report(attachment != null ? attachment.transform : null, point, outward, stage, strength, fracture);
            poseDriver.React(region, direction, strength);
            Refresh();
        }

        public void MarkSurface(AnatomyRegion region, OrganKind? organ, Vector3 point, Vector3 normal)
        {
            if (organ.HasValue ? ReadOrgan(organ.Value).IsDetached : ReadRegion(region).Stage >= AnatomyStage.Exposed)
                gore.SurfaceContact(point, normal);
        }

        public void ResetSubject()
        {
            if (!Rig.HasAuthority)
                return;
            State.Reset();
            Rig.ResetRig();
            LastRegion = AnatomyRegion.Torso;
            LastOrgan = null;
            Refresh();
        }

        public void Refresh()
        {
            if (State == null)
                return;

            GoreQuality quality = gore.Quality;
            for (int i = 0; i < regions.Length; i++)
            {
                var region = (AnatomyRegion)i;
                AnatomyRegionState part = ReadRegion(region);
                if (part.Stage == AnatomyStage.Intact)
                    announcedFracture[i] = false;
                if (Rig.HasAuthority)
                    Rig.SetImpairment(region, part.Impairment);
                else
                    Rig.SetReplicaImpairment(region, part.Impairment);
                regions[i].ShowState(part.Stage, quality, part.Stage >= AnatomyStage.Fractured,
                    part.Stage == AnatomyStage.Severed);
            }

            for (int i = 0; i < organs.Length; i++)
            {
                AnatomyOrganState state = ReadOrgan((OrganKind)i);
                bool visible = state.IsExposed || state.IsDetached;
                organs[i].ShowState(state.Damage >= 0.42f, quality, visible);
                organShapes[i].enabled = visible;
            }
            Presented?.Invoke(this);
        }

        private void ApplyRigStages()
        {
            for (int i = 0; i < regions.Length; i++)
            {
                var region = (AnatomyRegion)i;
                Rig.SetStage(region, State.GetRegion(region).Stage);
            }
            if (!Rig.RagdollActive && (State.GetRegion(AnatomyRegion.Head).Stage >= AnatomyStage.Fractured ||
                                      State.GetRegion(AnatomyRegion.Torso).Stage >= AnatomyStage.Fractured))
                Rig.ActivateRagdoll();
        }

        private void BuildArm(bool right, float scale, Rigidbody torso, AnatomyVisuals visuals)
        {
            float side = right ? 1f : -1f;
            AnatomyRegion upper = right ? AnatomyRegion.RightUpperArm : AnatomyRegion.LeftUpperArm;
            AnatomyRegion fore = right ? AnatomyRegion.RightForearm : AnatomyRegion.LeftForearm;
            Rigidbody shoulder = Part(upper, new Vector3(side * 0.39f, 1.35f, 0f),
                new Vector3(0.235f, 0.45f, 0.26f), 4.3f, side * 29f, scale, torso, visuals);
            Part(fore, new Vector3(side * 0.535f, 0.985f, 0.035f),
                new Vector3(0.215f, 0.43f, 0.235f), 3.1f, side * 13f, scale, shoulder, visuals);
        }

        private Rigidbody Part(AnatomyRegion region, Vector3 center, Vector3 dimensions, float mass,
            float angle, float scale, Rigidbody parent, AnatomyVisuals visuals)
        {
            var part = new GameObject(region.ToString()) { layer = CalibrationLayers.Anatomy };
            part.transform.SetParent(transform, false);
            part.transform.localPosition = center * scale;
            part.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            Vector3 size = dimensions * scale;
            if (region == AnatomyRegion.Torso)
                part.AddComponent<BoxCollider>().size = size * 0.92f;
            else if (region == AnatomyRegion.Head)
                part.AddComponent<SphereCollider>().radius = size.x * 0.47f;
            else
            {
                var capsule = part.AddComponent<CapsuleCollider>();
                capsule.height = size.y * 0.94f;
                capsule.radius = Mathf.Min(size.x, size.z) * 0.46f;
            }
            var body = part.AddComponent<Rigidbody>();
            body.mass = mass * scale;
            part.AddComponent<AnatomyHitbox>().Bind(this, region, body);
            Rig.BindRegion(region, body, parent);
            regions[(int)region] = visuals.BuildRegion(part.transform, size, region);
            return body;
        }

        private void BuildOrgan(OrganKind kind, float scale, AnatomyVisuals visuals)
        {
            AnatomyVisuals.OrganMount mount = AnatomyVisuals.GetOrganMount(kind);
            AnatomyRegion region = State.GetOrgan(kind).Region;
            Rig.TryGetBody(region, out Rigidbody parent);
            var part = new GameObject(kind.ToString()) { layer = CalibrationLayers.Anatomy };
            part.transform.SetParent(transform, false);
            Vector3 offset = mount.Region == region ? mount.LocalPosition : new Vector3(0f, 0f, 0.09f);
            part.transform.SetPositionAndRotation(parent.transform.TransformPoint(offset * scale),
                parent.rotation * Quaternion.Euler(mount.LocalEuler));

            int index = (int)kind;
            organs[index] = visuals.BuildOrgan(part.transform, kind, scale);
            Bounds bounds = organs[index].LocalBounds;
            var sensor = part.AddComponent<BoxCollider>();
            sensor.center = bounds.center;
            sensor.size = bounds.size * 0.92f;
            sensor.isTrigger = true;
            var body = part.AddComponent<Rigidbody>();
            body.mass = (kind == OrganKind.Brain ? 1.1f : 0.46f) * scale;
            part.AddComponent<AnatomyHitbox>().Bind(this, region, body, kind);
            Rig.BindOrgan(kind, body, parent);
            organShapes[index] = sensor;
        }
    }
}
