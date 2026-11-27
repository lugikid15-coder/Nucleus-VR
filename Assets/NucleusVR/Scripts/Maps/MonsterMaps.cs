using System;
using System.Collections.Generic;
using NucleusVR.Core;
using NucleusVR.Gameplay;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NucleusVR.Maps
{
    /// <summary>Builds the active hunt arena and presents its replicated station state.</summary>
    internal sealed partial class MonsterMaps : MonoBehaviour
    {
        private const ushort FirstObjective = 201;
        private const ushort PrimaryExit = 204;
        private const ushort SecondaryExit = 205;

        private readonly List<Material> finishes = new();
        private readonly List<Mesh> batches = new();
        private readonly Dictionary<Material, MeshBatch> pending = new();
        private readonly Dictionary<ushort, Renderer> stations = new();
        private readonly List<GameObject> closedGates = new();
        private readonly List<Renderer> eventLamps = new();
        private Transform layout;
        private Mesh cube;
        private Material steel, closed, ready, complete, eventSignal;
        private Action<ushort, Transform> registerStation;
        private bool pendingEvent;

        public Vector3 SurvivorSpawn { get; private set; }
        public Vector3 MonsterSpawn { get; private set; }

        public void Build(MonsterMap map, Material lit, Material signal, Action<ushort, Transform> register)
        {
            if (lit == null || signal == null)
                throw new ArgumentNullException(lit == null ? nameof(lit) : nameof(signal));

            Clear();
            registerStation = register ?? throw new ArgumentNullException(nameof(register));
            layout = new GameObject(map.ToString()).transform;
            layout.SetParent(transform, false);

            cube = CreateUnitCube();
            steel = Finish(lit, new Color32(45, 54, 61, 255), "Station steel");
            closed = Finish(signal, new Color32(155, 46, 32, 255), "Locked");
            ready = Finish(signal, new Color32(255, 173, 64, 255), "In sequence");
            complete = Finish(signal, new Color32(67, 231, 164, 255), "Resolved");
            eventSignal = Finish(signal, new Color32(232, 99, 39, 255), "Alarm");

            switch (map)
            {
                case MonsterMap.Backrooms:
                    BuildBackrooms(lit, signal);
                    break;
                case MonsterMap.Factory:
                    BuildFactory(lit, signal);
                    break;
                case MonsterMap.Mines:
                    BuildMines(lit, signal);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(map));
            }
            BakeGeometry();
            Present(0, false, 0);
        }

        public void Present(uint completedObjectives, bool exitOpen, byte eventPhase)
        {
            bool prerequisitesComplete = true;
            for (ushort step = 0; step < 3; step++)
            {
                bool done = (completedObjectives & (1u << step)) != 0;
                Renderer panel = stations[(ushort)(FirstObjective + step)];
                SetFinish(panel, done ? complete : prerequisitesComplete ? ready : closed);
                prerequisitesComplete &= done;
            }

            for (ushort id = PrimaryExit; id <= SecondaryExit; id++)
                if (stations.TryGetValue(id, out Renderer panel))
                    SetFinish(panel, exitOpen ? complete : closed);

            foreach (GameObject gate in closedGates)
                if (gate.activeSelf == exitOpen)
                    gate.SetActive(!exitOpen);
            pendingEvent = eventPhase != 0;
            if (!pendingEvent)
                SetAlarm(ready);
        }

        private void Update()
        {
            if (!pendingEvent)
                return;

            // A slow warning pulse leaves the objective indicators and traversable geometry untouched.
            SetAlarm(Time.unscaledTime % 1.6f < 0.7f ? eventSignal : closed);
        }

        private void SetAlarm(Material finish)
        {
            foreach (Renderer lamp in eventLamps)
                SetFinish(lamp, finish);
        }

        private static void SetFinish(Renderer renderer, Material finish)
        {
            if (renderer.sharedMaterial != finish)
                renderer.sharedMaterial = finish;
        }

        private void Spawn(Vector3 survivor, Vector3 monster)
        {
            SurvivorSpawn = layout.TransformPoint(survivor);
            MonsterSpawn = layout.TransformPoint(monster);
        }

        private void Objective(ushort id, string caption, Vector3 center)
        {
            Vector3 mount = center + Vector3.back * 0.015f + Vector3.down * 0.12f;
            Solid(caption + " pedestal", mount + Vector3.down * 0.48f + Vector3.forward * 0.2f,
                new Vector3(0.39f, 1.25f, 0.35f), steel);
            Solid(caption + " console", center + Vector3.forward * 0.1f,
                new Vector3(0.66f, 0.41f, 0.22f), steel);
            GameObject pad = FacilityGeometry.Block(layout, caption + " contact", center + Vector3.back * 0.035f,
                new Vector3(0.4f, 0.25f, 0.11f), closed, CalibrationLayers.Course);
            stations.Add(id, pad.GetComponent<MeshRenderer>());
            Caption(caption, center + new Vector3(0f, 0.38f, -0.105f), 0.16f);
            registerStation(id, pad.transform);
        }

        private void EscapeBay(ushort id, string caption, float x, float z, Material siding, float depth = 2.6f)
        {
            const float width = 2.55f;
            const float doorHeight = 2.58f;
            float centerDepth = z + depth * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                Solid(caption + " side wall", new Vector3(x + side * (width * 0.5f + 0.12f), 1.6f, centerDepth),
                    new Vector3(0.28f, 3.2f, depth + 0.28f), siding);
                Solid(caption + " guide rail", new Vector3(x + side * 1.2f, 1f, centerDepth),
                    new Vector3(0.13f, 0.12f, depth - 0.15f), ready, false);
            }
            Solid(caption + " end wall", new Vector3(x, 1.6f, z + depth),
                new Vector3(width + 0.55f, 3.2f, 0.23f), siding);
            Solid(caption + " roof", new Vector3(x, 3.18f, centerDepth),
                new Vector3(width + 0.55f, 0.24f, depth + 0.25f), steel);
            Solid(caption + " header", new Vector3(x, 2.86f, z),
                new Vector3(width + 0.5f, 0.64f, 0.29f), steel);
            closedGates.Add(FacilityGeometry.Block(layout, caption + " interlock", new Vector3(x, doorHeight / 2, z),
                new Vector3(width, doorHeight, 0.2f), siding, CalibrationLayers.Course));
            Caption(caption, new Vector3(x, 2.9f, z - 0.16f), 0.22f);
            Alarm(new Vector3(x, 2.49f, z - 0.13f), new Vector3(0.36f, 0.09f, 0.05f));
            Objective(id, "EXIT", new Vector3(x, 1.27f, z + depth - 0.51f));
        }

        private void Caption(string text, Vector3 position, float size) =>
            FacilityGeometry.Label(layout, text, position, size, new Color32(242, 227, 192, 255));

        private void Alarm(Vector3 position, Vector3 size)
        {
            GameObject lamp = FacilityGeometry.Block(layout, "Event lamp", position, size, ready,
                CalibrationLayers.Course, false);
            eventLamps.Add(lamp.GetComponent<MeshRenderer>());
        }

        private Material Finish(Material source, Color color, string label)
        {
            var finish = new Material(source) { name = label, color = color };
            finishes.Add(finish);
            return finish;
        }

        private void Solid(string label, Vector3 position, Vector3 size, Material finish,
            bool collidable = true, Quaternion? orientation = null, bool castsShadow = true)
        {
            Quaternion rotation = orientation ?? Quaternion.identity;
            if (collidable)
            {
                GameObject solid = FacilityGeometry.Block(layout, label, position, size, finish,
                    CalibrationLayers.Course);
                solid.transform.localRotation = rotation;
                solid.GetComponent<MeshRenderer>().enabled = false;
            }
            if (!pending.TryGetValue(finish, out MeshBatch batch))
            {
                batch = new MeshBatch();
                pending.Add(finish, batch);
            }
            var instance = new CombineInstance
            {
                mesh = cube,
                transform = Matrix4x4.TRS(position, rotation, size)
            };
            (castsShadow ? batch.Solids : batch.Trim).Add(instance);
        }

        private void BakeGeometry()
        {
            foreach (KeyValuePair<Material, MeshBatch> item in pending)
            {
                Bake(item.Key, item.Value.Solids, true);
                Bake(item.Key, item.Value.Trim, false);
            }
            pending.Clear();
        }

        private static Mesh CreateUnitCube()
        {
            Vector3[] faces =
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
            };
            var vertices = new Vector3[24];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[36];
            for (int face = 0; face < faces.Length; face++)
            {
                Vector3 normal = faces[face];
                Vector3 across = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 upward = Vector3.Cross(normal, across);
                int offset = face * 4;
                vertices[offset] = (normal - across - upward) * 0.5f;
                vertices[offset + 1] = (normal + across - upward) * 0.5f;
                vertices[offset + 2] = (normal + across + upward) * 0.5f;
                vertices[offset + 3] = (normal - across + upward) * 0.5f;
                uv[offset] = Vector2.zero;
                uv[offset + 1] = Vector2.right;
                uv[offset + 2] = Vector2.one;
                uv[offset + 3] = Vector2.up;
                for (int corner = 0; corner < 4; corner++)
                    normals[offset + corner] = normal;
                int triangle = face * 6;
                triangles[triangle] = triangles[triangle + 3] = offset;
                triangles[triangle + 1] = offset + 1;
                triangles[triangle + 2] = triangles[triangle + 4] = offset + 2;
                triangles[triangle + 5] = offset + 3;
            }
            var mesh = new Mesh { name = "Arena unit block", vertices = vertices, normals = normals, uv = uv };
            mesh.triangles = triangles;
            mesh.RecalculateTangents();
            return mesh;
        }

        private void Bake(Material finish, List<CombineInstance> instances, bool shadows)
        {
            if (instances.Count == 0)
                return;
            var mesh = new Mesh { name = finish.name + " arena geometry" };
            mesh.CombineMeshes(instances.ToArray(), true, true);
            mesh.UploadMeshData(true);
            batches.Add(mesh);
            var visual = new GameObject(mesh.name) { layer = CalibrationLayers.Course };
            visual.transform.SetParent(layout, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = finish;
            if (!shadows)
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void OnDestroy() => Clear();

        private void Clear()
        {
            if (layout != null)
            {
                layout.gameObject.SetActive(false);
                Object.Destroy(layout.gameObject);
            }
            foreach (Material finish in finishes)
                Object.Destroy(finish);
            foreach (Mesh mesh in batches)
                Object.Destroy(mesh);
            if (cube != null)
                Object.Destroy(cube);
            cube = null;
            finishes.Clear();
            batches.Clear();
            pending.Clear();
            eventLamps.Clear();
            closedGates.Clear();
            stations.Clear();
            pendingEvent = false;
        }

        private sealed class MeshBatch
        {
            internal readonly List<CombineInstance> Solids = new();
            internal readonly List<CombineInstance> Trim = new();
        }
    }
}
