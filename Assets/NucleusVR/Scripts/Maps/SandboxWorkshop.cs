using System;
using NucleusVR.Anatomy;
using NucleusVR.Core;
using NucleusVR.Gore;
using NucleusVR.Multiplayer;
using NucleusVR.Props;
using UnityEngine;

namespace NucleusVR.Maps
{
    /// <summary>Room-scale specimen workshop with host-controlled state and local gore presentation.</summary>
    internal sealed class SandboxWorkshop : MonoBehaviour
    {
        private const ushort FirstSubject = 301;
        private const byte GravityEnabled = 1 << 0;
        private const byte IdleEnabled = 1 << 1;
        private const byte PropsEnabled = 1 << 2;
        private const byte SubjectMask = 0b11;

        private struct LooseProp
        {
            public Rigidbody Body;
            public GrabbableEquipment Grip;
            public Vector3 RestPoint;
            public Quaternion RestRotation;
        }

        private readonly AnatomySpecimen[] specimens = new AnatomySpecimen[2];
        private readonly AnatomyIdleDriver[] drivers = new AnatomyIdleDriver[2];
        private readonly bool[] registered = new bool[2];
        private readonly LooseProp[] props = new LooseProp[3];
        private DirectSession session;
        private AnatomyVisuals visuals;
        private GoreEffects effects;
        private Material accent;
        private TextMesh gravityLabel, idleLabel, goreLabel, propsLabel, performanceLabel;
        private byte flags;
        private byte selected;
        private uint resetRevision;
        private bool built;
        private bool authority = true;
        private bool hasPresentation;
        private float frameTime, nextReadout;

        public Vector3 Spawn => transform.TransformPoint(new Vector3(0f, 0f, -4.2f));

        public void Build(Material lit, Material signal, DirectSession network, Action<ushort, Transform> register)
        {
            session = network;
            visuals = new AnatomyVisuals(lit);
            effects = gameObject.AddComponent<GoreEffects>();
            effects.Build(signal, 1 << CalibrationLayers.Course);
            accent = new Material(signal) { name = "Workshop amber", color = new Color32(239, 175, 84, 255) };

            Block("Workshop floor", new Vector3(0f, -0.15f, 0f), new Vector3(12.4f, 0.30f, 12.4f), lit);
            Block("Far wall", new Vector3(0f, 1.9f, 6.04f), new Vector3(12.4f, 3.8f, 0.25f), lit);
            Block("Entry wall", new Vector3(0f, 1.9f, -6.04f), new Vector3(12.4f, 3.8f, 0.25f), lit);
            Block("Left wall", new Vector3(-6.04f, 1.9f, 0f), new Vector3(0.25f, 3.8f, 12f), lit);
            Block("Right wall", new Vector3(6.04f, 1.9f, 0f), new Vector3(0.25f, 3.8f, 12f), lit);
            Block("Ceiling", new Vector3(0f, 3.9f, 0f), new Vector3(12.4f, 0.23f, 12.4f), lit);
            Block("Back wall light strip", new Vector3(0f, 3.12f, 5.86f),
                new Vector3(9.2f, 0.045f, 0.06f), accent, false);
            Label("Physics workshop", new Vector3(0f, 2.78f, 5.86f), 0.34f, accent.color);
            Label("Strike with a hand. Grip an exposed part to detach it.",
                new Vector3(0f, 2.36f, 5.86f), 0.155f, Color.white);
            AddLight(new Vector3(-3.3f, 3.45f, 1.8f), new Color32(250, 210, 162, 255));
            AddLight(new Vector3(3.0f, 3.45f, -0.5f), new Color32(164, 207, 247, 255));

            BuildSpecimen(0, "Gorilla", -2.35f, 1.08f, AnatomyProfile.Default);
            BuildSpecimen(1, "Mutant", 2.35f, 1.18f, AnatomyProfile.CreateScaled(1.25f, 1.1f, 1.1f));
            BuildControls(lit, register);
            BuildTools(lit);
            if (session != null)
                session.AnatomyImpactApplied += OnImpact;

            built = true;
        }

        public void Present(byte flags, byte subjects, uint reset, bool authority)
        {
            if (!built)
                return;

            bool changedAuthority = this.authority != authority;
            bool changedFlags = !hasPresentation || this.flags != flags;
            bool resetRequested = hasPresentation ? resetRevision != reset : reset != 0;
            this.flags = flags;
            selected = (byte)(subjects & SubjectMask);
            resetRevision = reset;
            this.authority = authority;
            hasPresentation = true;

            ApplySubjects();
            if (changedAuthority)
            {
                FindFirstObjectByType<EquipmentHands>()?.ReleaseAll();
                ApplyPropOwnership();
            }
            if (changedFlags)
                ApplyPropVisibility();

            if (resetRequested)
                ResetWorkshop();
            ApplyPhysicsOptions();
            if (changedFlags)
                UpdateLabels();
        }

        public void CycleGore()
        {
            if (!built)
                return;

            effects.Quality = (GoreQuality)(((int)effects.Quality + 1) % 4);
            foreach (AnatomySpecimen specimen in specimens)
                specimen.Refresh();
            UpdateLabels();
        }

        private void BuildSpecimen(int index, string label, float x, float scale, AnatomyProfile profile)
        {
            Vector3 location = new(x, 0f, 2.7f);
            Block(label + " floor marker", location + Vector3.up * 0.017f,
                new Vector3(2.36f, 0.028f, 2.44f), accent, false);
            Label(label, new Vector3(x, 1.93f, 5.85f), 0.235f, Color.white);
            var root = new GameObject("Workshop " + label.ToLowerInvariant());
            root.transform.SetParent(transform, false);
            root.transform.SetLocalPositionAndRotation(location, Quaternion.Euler(0f, 180f, 0f));
            var specimen = root.AddComponent<AnatomySpecimen>();
            specimen.Build((ushort)(FirstSubject + index), visuals, effects, profile, scale);
            specimens[index] = specimen;
            drivers[index] = root.GetComponent<AnatomyIdleDriver>();
            root.SetActive(false);
        }

        private void BuildControls(Material lit, Action<ushort, Transform> register)
        {
            Block("Workshop control housing", new Vector3(3.67f, 1.11f, -0.96f),
                new Vector3(3.28f, 1.34f, 0.24f), lit);
            Label("Workshop controls", new Vector3(3.67f, 1.66f, -1.107f), 0.19f, accent.color);
            AddControl(register, 301, "Gorilla", new Vector3(2.64f, 1.39f, -1.13f), out _);
            AddControl(register, 302, "Mutant", new Vector3(3.67f, 1.39f, -1.13f), out _);
            AddControl(register, 303, "Gravity on", new Vector3(4.70f, 1.39f, -1.13f), out gravityLabel);
            AddControl(register, 304, "Idle on", new Vector3(2.64f, 1.07f, -1.13f), out idleLabel);
            AddControl(register, 305, "Reset", new Vector3(3.67f, 1.07f, -1.13f), out _);
            AddControl(register, 306, "Gore medium", new Vector3(4.70f, 1.07f, -1.13f), out goreLabel);
            AddControl(register, 307, "Props on", new Vector3(2.64f, 0.75f, -1.13f), out propsLabel);
            performanceLabel = Label("", new Vector3(4.18f, 0.75f, -1.10f), 0.12f, accent.color);
        }

        private void AddControl(Action<ushort, Transform> register, ushort id, string title, Vector3 point,
            out TextMesh label)
        {
            GameObject pad = Block(title + " pad", point, new Vector3(0.93f, 0.23f, 0.08f), accent, false);
            label = Label(title, point + Vector3.back * 0.049f, 0.13f, Color.black);
            register(id, pad.transform);
        }

        private void BuildTools(Material lit)
        {
            Block("Tool bench", new Vector3(-4.27f, 0.83f, -1.7f), new Vector3(2.45f, 0.15f, 1.2f), lit);
            Block("Bench base", new Vector3(-4.27f, 0.36f, -1.7f), new Vector3(1.8f, 0.72f, 0.92f), lit);
            Label("Practice props", new Vector3(-4.27f, 1.39f, -2.23f), 0.16f, accent.color);
            BuildTool(0, "Impact block", PrimitiveType.Cube, new Vector3(-4.99f, 1.08f, -1.7f),
                new Vector3(0.32f, 0.32f, 0.32f), 2.2f, accent);
            BuildTool(1, "Carry ball", PrimitiveType.Sphere, new Vector3(-4.25f, 1.08f, -1.7f),
                new Vector3(0.33f, 0.33f, 0.33f), 1.1f, accent);
            BuildTool(2, "Counterweight", PrimitiveType.Cube, new Vector3(-3.54f, 1.08f, -1.7f),
                new Vector3(0.24f, 0.32f, 0.46f), 3.8f, lit);
        }

        private void BuildTool(int index, string title, PrimitiveType primitive, Vector3 position, Vector3 scale,
            float mass, Material material)
        {
            GameObject tool = GameObject.CreatePrimitive(primitive);
            tool.name = title;
            tool.layer = CalibrationLayers.Props;
            tool.transform.SetParent(transform, false);
            tool.transform.localPosition = position;
            tool.transform.localScale = scale;
            tool.GetComponent<MeshRenderer>().sharedMaterial = material;
            var body = tool.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            props[index] = new LooseProp
            {
                Body = body,
                Grip = tool.AddComponent<GrabbableEquipment>(),
                RestPoint = position,
                RestRotation = Quaternion.identity
            };
        }

        private void ApplySubjects()
        {
            for (int i = 0; i < specimens.Length; i++)
            {
                AnatomySpecimen specimen = specimens[i];
                bool visible = (selected & (1 << i)) != 0;
                if (!visible && specimen.gameObject.activeSelf)
                {
                    Unregister(i);
                    specimen.ResetSubject();
                }

                specimen.gameObject.SetActive(visible);
                if (!visible || !isActiveAndEnabled)
                    continue;

                if (session != null && !registered[i])
                    registered[i] = session.RegisterAnatomySubject(specimen.Id, specimen.State, specimen.Rig,
                        specimen.PresentSnapshot, specimen.AuthorityChanged);
            }
        }

        private void ApplyPropOwnership()
        {
            foreach (LooseProp prop in props)
            {
                prop.Grip.enabled = authority;
                prop.Body.isKinematic = !authority;
            }
        }

        private void ApplyPropVisibility()
        {
            bool visible = (flags & PropsEnabled) != 0;
            foreach (LooseProp prop in props)
            {
                GameObject tool = prop.Body.gameObject;
                if (tool.activeSelf == visible)
                    continue;

                if (!visible && prop.Grip.IsHeld)
                    FindFirstObjectByType<EquipmentHands>()?.ReleaseAll();
                tool.SetActive(visible);
                if (visible)
                    ResetTool(prop);
            }
        }

        private void ResetWorkshop()
        {
            FindFirstObjectByType<EquipmentHands>()?.ReleaseAll();
            effects.Clear();
            foreach (LooseProp prop in props)
                if (prop.Body.gameObject.activeInHierarchy)
                    ResetTool(prop);

            if (!authority)
                return;

            foreach (AnatomySpecimen specimen in specimens)
            {
                if (specimen.gameObject.activeSelf &&
                    !(session != null && session.IsSessionActive &&
                      session.RequestAnatomy(specimen.Id, AnatomyAction.Reset,
                          AnatomyTarget.ForRegion(AnatomyRegion.Torso), true)))
                    specimen.ResetSubject();
            }
        }

        private void ResetTool(LooseProp prop)
        {
            Rigidbody body = prop.Body;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.position = transform.TransformPoint(prop.RestPoint);
            body.rotation = transform.rotation * prop.RestRotation;
        }

        private void FixedUpdate()
        {
            if (built && hasPresentation)
                ApplyPhysicsOptions();
        }

        private void ApplyPhysicsOptions()
        {
            bool gravity = (flags & GravityEnabled) != 0;
            bool idle = (flags & IdleEnabled) != 0;
            foreach (LooseProp prop in props)
                if (prop.Body.gameObject.activeInHierarchy)
                    SetGravity(prop.Body, gravity);

            for (int i = 0; i < specimens.Length; i++)
            {
                AnatomySpecimen specimen = specimens[i];
                if (!specimen.gameObject.activeInHierarchy)
                    continue;

                drivers[i].enabled = idle && specimen.Rig.ShouldDrive(AnatomyRegion.Torso);
                for (int region = 0; region < (int)AnatomyRegion.Count; region++)
                    if (specimen.Rig.TryGetBody((AnatomyRegion)region, out Rigidbody body))
                        SetGravity(body, gravity);
                for (int organ = 0; organ < (int)OrganKind.Count; organ++)
                    if (specimen.Rig.TryGetOrganBody((OrganKind)organ, out Rigidbody body))
                        SetGravity(body, gravity);
            }
        }

        private static void SetGravity(Rigidbody body, bool gravity)
        {
            bool enabled = gravity && !body.isKinematic;
            if (body.useGravity != enabled)
                body.useGravity = enabled;
        }

        private void Update()
        {
            if (!built || Time.unscaledDeltaTime <= 0f)
                return;

            frameTime = frameTime <= 0f ? Time.unscaledDeltaTime :
                Mathf.Lerp(frameTime, Time.unscaledDeltaTime, 0.12f);
            if (Time.unscaledTime < nextReadout)
                return;

            nextReadout = Time.unscaledTime + 0.5f;
            performanceLabel.text = $"{Mathf.RoundToInt(1f / frameTime)} FPS";
        }

        private void UpdateLabels()
        {
            gravityLabel.text = (flags & GravityEnabled) != 0 ? "Gravity on" : "Gravity off";
            idleLabel.text = (flags & IdleEnabled) != 0 ? "Idle on" : "Idle off";
            propsLabel.text = (flags & PropsEnabled) != 0 ? "Props on" : "Props off";
            goreLabel.text = "Gore " + effects.Quality;
        }

        private void AddLight(Vector3 position, Color color)
        {
            var source = new GameObject("Workshop light");
            source.transform.SetParent(transform, false);
            source.transform.localPosition = position;
            var lamp = source.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = color;
            lamp.range = 8f;
            lamp.intensity = 2.3f;
            lamp.shadows = LightShadows.None;
        }

        private void OnImpact(AnatomyImpact impact)
        {
            for (int i = 0; i < specimens.Length; i++)
            {
                AnatomySpecimen specimen = specimens[i];
                if (!registered[i] || specimen.Id != impact.SubjectId || !specimen.isActiveAndEnabled)
                    continue;

                OrganKind? organ = impact.Target.IsOrgan ? impact.Target.Organ : null;
                AnatomyRegion region = organ.HasValue ? specimen.ReadOrgan(organ.Value).Region : impact.Target.Region;
                specimen.SeenImpact(region, organ, impact.Point, impact.Direction, 0.72f);
                return;
            }
        }

        private void Unregister(int index)
        {
            if (!registered[index])
                return;
            session?.UnregisterAnatomySubject(specimens[index].Id);
            registered[index] = false;
        }

        private void OnEnable()
        {
            if (built && hasPresentation)
                ApplySubjects();
        }

        private void OnDisable()
        {
            if (!built)
                return;

            for (int i = 0; i < registered.Length; i++)
                Unregister(i);
            foreach (LooseProp prop in props)
            {
                if (prop.Grip == null || !prop.Grip.IsHeld)
                    continue;

                FindFirstObjectByType<EquipmentHands>()?.ReleaseAll();
                break;
            }
            effects.Clear();
        }

        private void OnDestroy()
        {
            if (session != null)
                session.AnatomyImpactApplied -= OnImpact;
            visuals?.Dispose();
            Destroy(accent);
        }

        private GameObject Block(string label, Vector3 point, Vector3 size, Material material, bool collidable = true) =>
            FacilityGeometry.Block(transform, label, point, size, material, CalibrationLayers.Course, collidable);

        private TextMesh Label(string text, Vector3 point, float size, Color color) =>
            FacilityGeometry.Label(transform, text, point, size, color);
    }
}
