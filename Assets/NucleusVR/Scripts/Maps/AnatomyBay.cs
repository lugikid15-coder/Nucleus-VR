using NucleusVR.Anatomy;
using NucleusVR.Core;
using NucleusVR.Gore;
using NucleusVR.Movement;
using NucleusVR.Multiplayer;
using NucleusVR.VR;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NucleusVR.Maps
{
    internal sealed class AnatomyBay : MonoBehaviour
    {
        private readonly AnatomySpecimen[] subjects = new AnatomySpecimen[2];
        private readonly TextMesh[] readouts = new TextMesh[2];
        private AnatomyVisuals visuals;
        private GoreEffects effects;
        private DirectSession session;
        private TrackedRig tracking;
        private HandLocomotion movement;
        private DesktopRigDriver desktop;
        private Transform qualityPad;
        private Transform resetPad;
        private TextMesh qualityLabel;
        private bool leftPressed, rightPressed;
        private bool previousPlayerIgnore, previousHandIgnore, configuredLayers;
        private Material cyan;

        public void Build(Material lit, Material signal, TrackedRig rig, HandLocomotion motor,
            DesktopRigDriver desktopDriver, DirectSession network)
        {
            session = network;
            tracking = rig;
            movement = motor;
            desktop = desktopDriver;

            previousPlayerIgnore = Physics.GetIgnoreLayerCollision(CalibrationLayers.Player, CalibrationLayers.Anatomy);
            previousHandIgnore = Physics.GetIgnoreLayerCollision(CalibrationLayers.Hands, CalibrationLayers.Anatomy);
            Physics.IgnoreLayerCollision(CalibrationLayers.Player, CalibrationLayers.Anatomy);
            Physics.IgnoreLayerCollision(CalibrationLayers.Hands, CalibrationLayers.Anatomy);
            configuredLayers = true;

            visuals = new AnatomyVisuals(lit);
            effects = gameObject.AddComponent<GoreEffects>();
            effects.Build(signal, 1 << CalibrationLayers.Course);
            rig.gameObject.AddComponent<AnatomyHands>().Configure(rig, motor, network);

            cyan = new Material(signal) { name = "Lab signal", color = new Color32(93, 203, 219, 255) };
            Block("Lab wall", new Vector3(-4.95f, 1.52f, 6.18f), new Vector3(4.9f, 3.04f, 0.18f), lit);
            Block("Lab header light", new Vector3(-4.95f, 2.75f, 6.04f), new Vector3(4.55f, 0.035f, 0.05f), cyan, false);
            Label("Anatomy lab", new Vector3(-4.95f, 2.45f, 6.04f), 0.31f, cyan.color);
            Label("Strike a specimen. Squeeze grip to carry or extract exposed anatomy.",
                new Vector3(-4.95f, 2.14f, 6.04f), 0.115f, Color.white);

            CreateSubject(0, "Gorilla specimen", -5.95f, 1f, AnatomyProfile.Default);
            CreateSubject(1, "Containment mutant", -3.95f, 1.15f, AnatomyProfile.CreateScaled(1.28f, 1.1f, 1.02f));

            Block("Lab controls", new Vector3(-7.35f, 0.64f, 4.35f), new Vector3(0.83f, 1.28f, 0.55f), lit);
            qualityPad = Block("Anatomy graphics control", new Vector3(-7.35f, 1.25f, 4.03f),
                new Vector3(0.54f, 0.20f, 0.08f), cyan, false).transform;
            resetPad = Block("Reset specimens control", new Vector3(-7.35f, 0.94f, 4.03f),
                new Vector3(0.54f, 0.20f, 0.08f), cyan, false).transform;
            qualityLabel = Label("", new Vector3(-7.35f, 1.45f, 4.16f), 0.11f, Color.white);
            Label("Reset specimens", new Vector3(-7.35f, 0.94f, 3.98f), 0.075f, Color.black);
            Label("Squeeze on a pad", new Vector3(-7.35f, 1.58f, 4.18f), 0.084f, cyan.color);
            ShowQuality();

            if (session != null)
                session.AnatomyImpactApplied += OnRemoteImpact;
        }

        private void CreateSubject(int index, string title, float x, float scale, AnatomyProfile profile)
        {
            const float z = 4.95f;
            Block(title + " floor marker", new Vector3(x, 0.014f, z),
                new Vector3(1.45f, 0.022f, 1.45f), cyan, false);
            Label(title, new Vector3(x, 1.82f, 6.03f), 0.15f, Color.white);
            readouts[index] = Label("", new Vector3(x, 1.56f, 6.03f), 0.093f, cyan.color);
            var specimen = new GameObject(title);
            specimen.transform.SetParent(transform, false);
            specimen.transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, 180f, 0f));
            var subject = specimen.AddComponent<AnatomySpecimen>();
            subjects[index] = subject;
            subject.Build((ushort)(100 + index), visuals, effects, profile, scale);
            subject.Presented += UpdateReadout;
            UpdateReadout(subject);
            session?.RegisterAnatomySubject(subject.Id, subject.State, subject.Rig, subject.PresentSnapshot,
                subject.AuthorityChanged);
        }

        private void LateUpdate()
        {
            if (qualityPad == null)
                return;

            if (desktop.IsDesktop && !desktop.MenuOpen && Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
                CycleQuality();

            if (!tracking.HeadTracked)
            {
                leftPressed = rightPressed = false;
                return;
            }
            UsePanel(movement.LeftContact, tracking.LeftHandTracked ? tracking.LeftGrip : 0f, ref leftPressed);
            UsePanel(movement.RightContact, tracking.RightHandTracked ? tracking.RightGrip : 0f, ref rightPressed);
        }

        private void UsePanel(Transform hand, float amount, ref bool pressed)
        {
            if (amount <= 0.35f)
                pressed = false;
            if (pressed || amount < 0.65f)
                return;

            pressed = true;
            Vector3 point = hand.position;
            if (Touches(point, qualityPad.position))
                CycleQuality();
            else if (Touches(point, resetPad.position))
                ResetSubjects();
        }

        private static bool Touches(Vector3 hand, Vector3 pad)
        {
            Vector3 delta = hand - pad;
            return Mathf.Abs(delta.x) <= 0.28f && Mathf.Abs(delta.y) <= 0.10f && Mathf.Abs(delta.z) <= 0.13f;
        }

        private void CycleQuality()
        {
            effects.Quality = (GoreQuality)(((int)effects.Quality + 1) % 4);
            ShowQuality();
            for (int i = 0; i < subjects.Length; i++)
                subjects[i].Refresh();
        }

        private void ShowQuality() => qualityLabel.text = "Gore " + effects.Quality;

        private void ResetSubjects()
        {
            bool online = session != null && session.IsSessionActive;
            bool reset = !online;
            foreach (AnatomySpecimen subject in subjects)
            {
                if (online)
                    reset |= session.RequestAnatomy(subject.Id, AnatomyAction.Reset, AnatomyTarget.ForRegion(AnatomyRegion.Torso), true);
                else
                    subject.ResetSubject();
            }
            if (reset)
                effects.Clear();
        }

        private void OnRemoteImpact(AnatomyImpact impact)
        {
            foreach (AnatomySpecimen subject in subjects)
            {
                if (subject.Id != impact.SubjectId)
                    continue;
                OrganKind? organ = impact.Target.IsOrgan ? impact.Target.Organ : null;
                AnatomyRegion region = organ.HasValue ? subject.ReadOrgan(organ.Value).Region : impact.Target.Region;
                subject.SeenImpact(region, organ, impact.Point, impact.Direction, 0.72f);
                return;
            }
        }

        private void UpdateReadout(AnatomySpecimen subject)
        {
            var region = subject.ReadRegion(subject.LastRegion);
            TextMesh panel = readouts[subject.Id - 100];
            if (subject.LastOrgan.HasValue)
            {
                OrganKind organ = subject.LastOrgan.Value;
                var state = subject.ReadOrgan(organ);
                panel.text = $"{organ} · {(state.IsDetached ? "Detached" : $"{state.Damage:P0} damaged")}";
            }
            else
            {
                string name = subject.LastRegion switch
                {
                    AnatomyRegion.LeftUpperArm => "Left upper arm",
                    AnatomyRegion.LeftForearm => "Left forearm",
                    AnatomyRegion.RightUpperArm => "Right upper arm",
                    AnatomyRegion.RightForearm => "Right forearm",
                    _ => subject.LastRegion.ToString(),
                };
                panel.text = region.Stage == AnatomyStage.Intact ? "Intact" :
                    $"{name} · {region.Stage} · {1f - region.Impairment:P0} function";
            }
        }

        private GameObject Block(string label, Vector3 point, Vector3 size, Material material, bool collidable = true)
            => FacilityGeometry.Block(transform, label, point, size, material, CalibrationLayers.Course, collidable);

        private TextMesh Label(string text, Vector3 point, float size, Color color)
            => FacilityGeometry.Label(transform, text, point, size, color);

        private void OnDestroy()
        {
            if (session != null)
            {
                session.AnatomyImpactApplied -= OnRemoteImpact;
                foreach (AnatomySpecimen subject in subjects)
                    if (subject != null)
                        session.UnregisterAnatomySubject(subject.Id);
            }
            visuals?.Dispose();
            Destroy(cyan);
            if (configuredLayers)
            {
                Physics.IgnoreLayerCollision(CalibrationLayers.Player, CalibrationLayers.Anatomy, previousPlayerIgnore);
                Physics.IgnoreLayerCollision(CalibrationLayers.Hands, CalibrationLayers.Anatomy, previousHandIgnore);
            }
        }
    }
}
