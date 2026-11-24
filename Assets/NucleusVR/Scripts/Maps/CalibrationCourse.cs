using System.Collections.Generic;
using UnityEngine;
using NucleusVR.Core;
using NucleusVR.Movement;
using NucleusVR.Props;

namespace NucleusVR.Maps
{
    public sealed class CalibrationCourse : MonoBehaviour
    {
        public static readonly Vector3 Spawn = new(-6.25f, 0.04f, -5.25f);
        public const int CheckpointCount = 3;

        private readonly List<Material> materials = new();
        private readonly List<MeshRenderer> stations = new();
        private readonly List<Transform> props = new();
        private readonly List<Pose> propSpawns = new();
        private Material steel;
        private Material plating;
        private Material rubber;
        private Material copper;
        private Material gold;
        private Material mint;
        private Material dormant;
        private Material active;
        private Material passed;

        public Transform ResetPad { get; private set; }
        public Transform SmoothTurnPad { get; private set; }
        public IReadOnlyList<MeshRenderer> Stations => stations;

        public void Build(Material litTemplate, Material signalTemplate)
        {
            steel = Finish(litTemplate, "Wall enamel", new Color32(48, 62, 77, 255));
            plating = Finish(litTemplate, "Anodized steel", new Color32(118, 139, 150, 255));
            rubber = Finish(litTemplate, "Floor composite", new Color32(30, 38, 48, 255));
            copper = Finish(litTemplate, "Safety cladding", new Color32(170, 83, 46, 255));
            gold = Finish(signalTemplate, "Route amber", new Color32(241, 175, 83, 255));
            mint = Finish(signalTemplate, "Facility mint", new Color32(119, 219, 192, 255));
            dormant = Finish(signalTemplate, "Station idle", new Color32(70, 85, 99, 255));
            active = Finish(signalTemplate, "Station armed", new Color32(255, 211, 114, 255));
            passed = Finish(signalTemplate, "Station verified", new Color32(114, 238, 182, 255));

            var light = new GameObject("Overhead light");
            light.transform.SetParent(transform, false);
            light.transform.rotation = Quaternion.Euler(58f, -29f, 0f);
            var overhead = light.AddComponent<Light>();
            overhead.type = LightType.Directional;
            overhead.color = new Color(0.81f, 0.9f, 1f);
            overhead.intensity = 1.2f;
            overhead.shadows = LightShadows.None;

            Block("Foundation", new Vector3(0f, -0.25f, 0f), new Vector3(18f, 0.5f, 16f), rubber);
            Block("North wall", new Vector3(0f, 3.4f, 8f), new Vector3(18f, 6.8f, 0.35f), steel);
            Block("South wall", new Vector3(0f, 3.4f, -8f), new Vector3(18f, 6.8f, 0.35f), steel);
            Block("West wall", new Vector3(-9f, 3.4f, 0f), new Vector3(0.35f, 6.8f, 16f), steel);
            Block("East wall", new Vector3(9f, 3.4f, 0f), new Vector3(0.35f, 6.8f, 16f), steel);
            Block("Ceiling", new Vector3(0f, 6.85f, 0f), new Vector3(18f, 0.35f, 16f), rubber);

            for (int i = -2; i <= 2; i++)
            {
                float x = i * 3.5f;
                Block("Overhead truss", new Vector3(x, 6.35f, 0f), new Vector3(0.24f, 0.48f, 16f), plating);
                Block("Luminaire", new Vector3(x, 6.08f, 0f), new Vector3(0.14f, 0.055f, 5f), mint, false);
                Block("Wall support", new Vector3(x, 3.3f, 7.7f), new Vector3(0.22f, 6.6f, 0.42f), plating);
                Block("Wall support", new Vector3(x, 3.3f, -7.7f), new Vector3(0.22f, 6.6f, 0.42f), plating);
            }

            Block("North power conduit", new Vector3(0f, 3.12f, 7.62f), new Vector3(17.5f, 0.26f, 0.44f), copper);
            Block("South power conduit", new Vector3(0f, 3.12f, -7.62f), new Vector3(17.5f, 0.26f, 0.44f), copper);
            Block("Bulkhead", new Vector3(-5.7f, 1.6f, 7.68f), new Vector3(2.7f, 3.2f, 0.33f), plating);
            Block("Bulkhead inset", new Vector3(-5.7f, 1.6f, 7.49f), new Vector3(2.25f, 2.8f, 0.075f), rubber, false);
            Block("Bulkhead seam", new Vector3(-5.7f, 1.6f, 7.44f), new Vector3(0.045f, 2.75f, 0.035f), mint, false);

            Label("NUCLEUS / RESEARCH FACILITY", new Vector3(-5.7f, 4.45f, 7.77f), 0.29f, mint.color);
            Label("CALIBRATION BAY", new Vector3(-5.7f, 3.86f, 7.77f), 0.45f, Color.white);
            Label("Hands only. Touch the amber pads in sequence.", new Vector3(-6.2f, 2.1f, -3.96f), 0.15f, Color.white);

            Block("Start console", new Vector3(-6.25f, 0.56f, -4.05f), new Vector3(2.55f, 1.12f, 0.76f), steel);
            AddStation(0, "START", new Vector3(-6.25f, 1.12f, -4.45f));
            ResetPad = AddPad("RESET", new Vector3(-7.18f, 1.12f, -4.45f), mint).transform;
            SmoothTurnPad = AddPad("SNAP / SMOOTH", new Vector3(-5.32f, 1.12f, -4.45f), mint).transform;
            MarkRoute(new Vector3(-4.6f, 0.012f, -5.5f), new Vector3(-1.7f, 0.012f, -1.8f), gold);
            AddTractionBenches(litTemplate);

            // Offset barriers leave several ways to reach the first checkpoint.
            Block("Low barrier", new Vector3(-3.15f, 0.4f, -2.6f), new Vector3(2.5f, 0.8f, 0.4f), copper);
            Block("Low barrier cap", new Vector3(-3.15f, 0.82f, -2.6f), new Vector3(2.52f, 0.04f, 0.43f), plating, false);
            Block("Checkpoint plinth", new Vector3(-1.9f, 0.63f, -0.72f), new Vector3(1.15f, 1.26f, 0.7f), steel);
            AddStation(1, "01 / TRANSFER", new Vector3(-1.9f, 1.28f, -1.1f));

            MarkRoute(new Vector3(-0.85f, 0.012f, -1.3f), new Vector3(3.6f, 0.012f, 0.55f), gold);
            for (int i = 0; i < 4; i++)
            {
                Block("Training step", new Vector3(2.8f + 1.1f * i, 0.375f * (i + 1), 1.3f),
                    new Vector3(1.1f, 0.75f * (i + 1), 2.5f), i % 2 == 0 ? steel : plating);
                Block("Step edge", new Vector3(2.26f + 1.1f * i, 0.75f * (i + 1) + 0.012f, 1.3f),
                    new Vector3(0.07f, 0.025f, 2.48f), gold, false);
            }

            Block("Upper platform", new Vector3(6.92f, 1.5f, 1.3f), new Vector3(1.68f, 3f, 3.3f), steel);
            Block("Upper station mount", new Vector3(6.73f, 3.51f, 2.35f), new Vector3(0.86f, 1.02f, 0.42f), steel);
            AddStation(2, "02 / ASCENT", new Vector3(6.73f, 4.01f, 2.09f));
            for (int i = 0; i < 4; i++)
            {
                Block("Service grip", new Vector3(7.1f + (i % 2) * 0.45f, 0.58f + i * 0.65f, -0.46f),
                    new Vector3(0.38f, 0.16f, 0.28f), copper);
            }

            Block("Finish plinth", new Vector3(-1.1f, 0.62f, 5f), new Vector3(1.3f, 1.24f, 0.73f), steel);
            AddStation(3, "03 / VERIFY", new Vector3(-1.1f, 1.25f, 4.6f));
            MarkRoute(new Vector3(5.3f, 0.012f, 4.5f), new Vector3(-0.3f, 0.012f, 4.5f), gold);
            AddPhysicsCrate(new Vector3(1.25f, 0.34f, 4.25f), plating);
            AddPhysicsCrate(new Vector3(1.95f, 0.34f, 5.1f), copper);

            UpdateSignals(0);
        }

        public void UpdateSignals(int nextStation)
        {
            for (int i = 0; i < stations.Count; i++)
                stations[i].sharedMaterial = i < nextStation ? passed : i == nextStation ? active : dormant;
        }

        public void UpdateTurnSignal(bool smooth)
        {
            SmoothTurnPad.GetComponent<MeshRenderer>().sharedMaterial = smooth ? active : mint;
        }

        public void ResetProps()
        {
            for (int i = 0; i < props.Count; i++)
            {
                Transform item = props[i];
                if (item == null)
                    continue;

                var body = item.GetComponent<Rigidbody>();
                body.position = propSpawns[i].position;
                body.rotation = propSpawns[i].rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private void AddTractionBenches(Material litTemplate)
        {
            Material polished = Finish(litTemplate, "Polished traction plate", new Color32(107, 181, 202, 255));
            polished.SetFloat("_Smoothness", 0.7f);
            AddTractionBench("GRIP", -3.3f, plating);
            GameObject slip = AddTractionBench("SLIP", -1.75f, polished);
            slip.AddComponent<HandSurface>().Configure(1f);
        }

        private GameObject AddTractionBench(string label, float x, Material finish)
        {
            const float z = -5.65f;
            Block(label + " plinth", new Vector3(x, 0.35f, z), new Vector3(0.92f, 0.7f, 0.78f), steel);
            GameObject plate = Block(label + " traction plate", new Vector3(x, 0.76f, z),
                new Vector3(1.25f, 0.12f, 1.1f), finish);
            Block(label + " sign", new Vector3(x, 0.98f, z + 0.48f), new Vector3(1.08f, 0.32f, 0.055f), steel);
            Label(label, new Vector3(x, 0.98f, z + 0.445f), 0.11f, Color.white);
            return plate;
        }

        private void AddStation(int index, string text, Vector3 position)
        {
            if (stations.Count != index)
                throw new System.InvalidOperationException("Stations must be authored in route order.");

            stations.Add(AddPad(text, position, dormant));
        }

        private MeshRenderer AddPad(string text, Vector3 position, Material signal)
        {
            Block(text + " frame", position + new Vector3(0f, 0f, 0.075f), new Vector3(0.5f, 0.38f, 0.12f), copper, false);
            var face = Block(text + " touch surface", position, new Vector3(0.42f, 0.29f, 0.085f), signal, false);
            Label(text, position + new Vector3(0f, 0.27f, -0.04f), 0.13f, Color.white);
            return face.GetComponent<MeshRenderer>();
        }

        private void AddPhysicsCrate(Vector3 position, Material finish)
        {
            var crate = Block("Movable equipment case", position, Vector3.one * 0.6f, finish);
            crate.layer = CalibrationLayers.Props;
            var body = crate.AddComponent<Rigidbody>();
            body.mass = 5f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxLinearVelocity = 7f;
            crate.AddComponent<GrabbableEquipment>();
            props.Add(crate.transform);
            propSpawns.Add(new Pose(position, Quaternion.identity));
        }

        private void MarkRoute(Vector3 from, Vector3 to, Material finish)
        {
            float distance = Vector3.Distance(from, to);
            int markers = Mathf.Max(1, Mathf.FloorToInt(distance / 0.63f));
            var direction = Quaternion.LookRotation(to - from);
            for (int i = 0; i < markers; i++)
            {
                var stripe = Block("Route marker", Vector3.Lerp(from, to, (i + 0.5f) / markers),
                    new Vector3(0.16f, 0.019f, 0.37f), finish, false);
                stripe.transform.rotation = direction;
            }
        }

        private GameObject Block(string label, Vector3 position, Vector3 size, Material finish, bool collidable = true)
            => FacilityGeometry.Block(transform, label, position, size, finish, CalibrationLayers.Course, collidable);

        private void Label(string value, Vector3 position, float size, Color color)
            => FacilityGeometry.Label(transform, value, position, size, color);

        private Material Finish(Material template, string label, Color color)
        {
            var material = new Material(template) { name = label };
            material.color = color;
            materials.Add(material);
            return material;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials)
                Destroy(material);
        }
    }
}
