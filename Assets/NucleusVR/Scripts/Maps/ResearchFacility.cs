using System;
using System.Collections.Generic;
using NucleusVR.Core;
using UnityEngine;

namespace NucleusVR.Maps
{
    internal sealed partial class ResearchFacility : MonoBehaviour
    {
        private const float HalfRoom = 8f;
        private const float DoorWidth = 3.8f;
        private const float DoorHeight = 3.12f;
        private const float WallDepth = 0.35f;
        private static readonly Vector3 Entry = new(0f, 0.06f, -5.9f);
        private static readonly Vector3[] Centers =
        {
            Vector3.zero, new(-21f, 0f, 0f), new(21f, 0f, 0f),
            new(-21f, 0f, 23f), new(0f, 0f, 23f), new(21f, 0f, 23f)
        };
        private static readonly (int A, int B)[] Corridors =
        {
            (0, 1), (0, 2), (0, 4), (1, 3), (3, 4), (4, 5), (2, 5)
        };

        [Flags]
        private enum Directions { West = 1, East = 2, South = 4, North = 8 }

        private sealed class Zone
        {
            internal Transform Root;
            internal Transform Geometry;
            internal float Height;
            internal Material Wall;
        }

        private sealed class Lamp
        {
            internal Light Light;
            internal MeshRenderer Strip;
            internal Color DefaultColor;
            internal float DefaultIntensity;
        }

        private readonly List<Material> finishes = new();
        private readonly List<Lamp> lamps = new();
        private readonly MeshRenderer[] circuitIndicators = new MeshRenderer[3];
        private readonly GameObject[] fragments = new GameObject[3];
        private Action<ushort, Transform> register;
        private Material metal, charcoal, cladding, trim, glass, rock, water, pipe;
        private Material mint, amber, blue, dark, red, gold;
        private Material playerPad;
        private GameObject room404Door, developerDoor, bossDoor, coreDoor, core;
        private byte phase = byte.MaxValue;
        private uint displayedSecrets = uint.MaxValue;
        private uint displayedPuzzle = uint.MaxValue;
        private float nextBlackoutTick;

        public Vector3 Spawn => transform.TransformPoint(Entry);
        public Vector3[] PatrolNodes { get; private set; } = Array.Empty<Vector3>();
        public (int From, int To)[] PatrolLinks { get; private set; } = Array.Empty<(int, int)>();

        public void Build(Material lit, Material signal, Action<ushort, Transform> register)
        {
            if (lit == null || signal == null)
                throw new ArgumentException("Research Facility requires the Boot materials.");
            this.register = register ?? throw new ArgumentNullException(nameof(register));

            metal = Finish(lit, "Facility wall enamel", 54, 68, 74);
            charcoal = Finish(lit, "Treadplate", 32, 43, 49);
            cladding = Finish(lit, "Laboratory cladding", 103, 120, 117);
            trim = Finish(lit, "Anodized frame", 73, 91, 94);
            glass = Finish(lit, "Frosted cell screens", 39, 87, 88);
            rock = Finish(lit, "Cavern basalt", 59, 53, 60);
            water = Finish(lit, "Turbid water surface", 15, 60, 58);
            pipe = Finish(lit, "Oxidized service pipe", 133, 74, 46);
            mint = Finish(signal, "Work light", 112, 220, 185);
            amber = Finish(signal, "Amber safety light", 243, 167, 85);
            blue = Finish(signal, "Blue instrument light", 103, 177, 230);
            dark = Finish(signal, "Emergency standby", 37, 46, 52);
            red = Finish(signal, "Lockdown light", 216, 71, 55);
            gold = Finish(signal, "Golden Core", 250, 202, 99);
            playerPad = Finish(signal, "Interactive panel", 29, 115, 105);

            Zone lab = Room(0, "Main Laboratory", 4.75f, Directions.West | Directions.East | Directions.North, cladding);
            Zone maintenance = Room(1, "Maintenance Tunnels", 3.55f, Directions.East | Directions.North, metal);
            Zone containment = Room(2, "Containment Wing", 4.8f, Directions.West | Directions.North, metal);
            Zone sewer = Room(3, "Sewers", 4.38f, Directions.South | Directions.East, metal, true);
            Zone caves = Room(4, "Underground Caverns", 7.2f, Directions.South | Directions.West | Directions.East, rock);
            Zone research = Room(5, "Secret Research Sector", 4.65f, Directions.West | Directions.South, metal);

            BuildLaboratory(lab);
            BuildMaintenance(maintenance);
            BuildContainment(containment);
            BuildSewer(sewer);
            BuildCaverns(caves);
            BuildSecretSector(research);
            BuildCorridors();
            Present(0u, 0, 0);
        }

        public void Present(uint secrets, byte eventPhase, uint puzzleBits = 0)
        {
            if (secrets != displayedSecrets || puzzleBits != displayedPuzzle)
            {
                displayedSecrets = secrets;
                displayedPuzzle = puzzleBits;
                SetOpen(room404Door, Discovered(secrets, 3));
                SetOpen(developerDoor, Discovered(secrets, 6));
                SetOpen(bossDoor, Discovered(secrets, 5));

                int circuitCount = (int)((puzzleBits >> 3) & 3u);
                bool complete = Discovered(secrets, 0);
                SetOpen(coreDoor, complete || (puzzleBits & 7) == 7 && circuitCount == 3);
                if (core != null)
                    core.SetActive(!complete);
                for (int index = 0; index < fragments.Length; index++)
                {
                    if (fragments[index] != null)
                        fragments[index].SetActive(!complete && (puzzleBits & (1u << index)) == 0);
                    if (circuitIndicators[index] != null)
                        circuitIndicators[index].sharedMaterial = index < circuitCount ? mint : dark;
                }
            }

            if (phase != eventPhase)
            {
                phase = eventPhase;
                nextBlackoutTick = 0f;
                UpdateLighting(false);
            }
        }

        private void Update()
        {
            if (phase != 1 || Time.unscaledTime < nextBlackoutTick)
                return;

            nextBlackoutTick = Time.unscaledTime + 0.13f;
            int slice = Mathf.FloorToInt(Time.unscaledTime * 7.3f);
            UpdateLighting(slice % 13 is 4 or 5 or 9);
        }

        private void UpdateLighting(bool lifted)
        {
            foreach (Lamp lamp in lamps)
            {
                lamp.Strip.sharedMaterial = phase switch
                {
                    1 => lifted ? amber : dark,
                    2 => red,
                    3 => amber,
                    4 => blue,
                    _ => mint
                };
                lamp.Light.color = phase switch
                {
                    1 => new Color(0.92f, 0.58f, 0.31f),
                    2 => new Color(1f, 0.29f, 0.20f),
                    3 => new Color(1f, 0.59f, 0.28f),
                    4 => new Color(0.48f, 0.66f, 0.90f),
                    _ => lamp.DefaultColor
                };
                lamp.Light.intensity = phase switch
                {
                    1 => lamp.DefaultIntensity * (lifted ? 0.39f : 0.08f),
                    2 => lamp.DefaultIntensity * 0.58f,
                    3 => lamp.DefaultIntensity * 0.69f,
                    _ => lamp.DefaultIntensity
                };
            }
        }

        private Zone Room(int index, string name, float height, Directions open, Material wall, bool drains = false)
        {
            Transform root = Group(transform, name, Centers[index]);
            var zone = new Zone { Root = root, Geometry = Group(root, "Architecture", Vector3.zero), Height = height, Wall = wall };
            if (!drains)
                BuildBlock(zone, "Foundation", 0f, -0.18f, 0f, 16f, 0.36f, 16f, charcoal);
            BuildBlock(zone, "Ceiling", 0f, height + 0.125f, 0f, 16.5f, 0.25f, 16.5f, charcoal);
            Boundary(zone, Directions.West, open);
            Boundary(zone, Directions.East, open);
            Boundary(zone, Directions.South, open);
            Boundary(zone, Directions.North, open);

            TintLamp(zone, -5.45f, height - 0.15f, 0f, new Color(0.77f, 0.91f, 0.89f), height > 6f ? 2.1f : 1.6f);
            TintLamp(zone, 5.45f, height - 0.15f, 0f, new Color(0.70f, 0.83f, 1f), height > 6f ? 2.1f : 1.6f);
            return zone;
        }

        private void Boundary(Zone zone, Directions side, Directions open)
        {
            bool wallAlongZ = side is Directions.West or Directions.East;
            float at = side is Directions.East or Directions.North ? HalfRoom : -HalfRoom;
            if ((open & side) == 0)
            {
                Wall(zone, "Outer wall", wallAlongZ, at, 0f, 0f, 2f * HalfRoom + WallDepth,
                    zone.Height, zone.Wall);
                return;
            }

            float remaining = HalfRoom - DoorWidth * 0.5f;
            float segment = remaining * 0.5f + DoorWidth * 0.5f;
            Wall(zone, "Portal wall", wallAlongZ, at, -segment, 0f, remaining, zone.Height, zone.Wall);
            Wall(zone, "Portal wall", wallAlongZ, at, segment, 0f, remaining, zone.Height, zone.Wall);
            Wall(zone, "Portal lintel", wallAlongZ, at, 0f, DoorHeight, DoorWidth, zone.Height - DoorHeight, trim);
            Wall(zone, "Portal signal", wallAlongZ, at * 0.976f, 0f, DoorHeight - 0.035f,
                DoorWidth - 0.14f, 0.065f, mint, false);
        }

        private void Wall(Zone zone, string name, bool alongZ, float at, float along, float bottom, float length,
            float height, Material material, bool solid = true)
        {
            BuildBlock(zone, name, alongZ ? at : along, bottom + height * 0.5f, alongZ ? along : at,
                alongZ ? WallDepth : length, height, alongZ ? length : WallDepth, material, solid);
        }

        private void BuildCorridors()
        {
            var nodes = new Vector3[Centers.Length + Corridors.Length];
            var links = new (int, int)[Corridors.Length * 2];
            for (int index = 0; index < Centers.Length; index++)
                nodes[index] = transform.TransformPoint(Centers[index]);

            for (int index = 0; index < Corridors.Length; index++)
            {
                (int first, int second) = Corridors[index];
                Vector3 difference = Centers[second] - Centers[first];
                Vector3 midpoint = (Centers[first] + Centers[second]) * 0.5f;
                Transform hallway = Group(transform, $"Passage {first + 1}-{second + 1}", midpoint);
                if (Mathf.Abs(difference.x) > Mathf.Abs(difference.z))
                    hallway.localRotation = Quaternion.Euler(0f, 90f, 0f);
                float run = difference.magnitude - 2f * HalfRoom + 0.42f;
                AddBlock(hallway, "Passage floor", new Vector3(0f, -0.18f, 0f),
                    new Vector3(DoorWidth + 0.35f, 0.36f, run), charcoal);
                AddBlock(hallway, "Passage ceiling", new Vector3(0f, 3.28f, 0f),
                    new Vector3(4.2f, 0.25f, run), metal);
                for (int side = -1; side <= 1; side += 2)
                {
                    AddBlock(hallway, "Passage wall", new Vector3(side * 2.09f, 1.64f, 0f),
                        new Vector3(0.38f, 3.28f, run), metal);
                    AddBlock(hallway, "Grip rail", new Vector3(side * 1.82f, 0.93f, 0f),
                        new Vector3(0.17f, 0.17f, run - 0.35f), trim);
                }
                AddBlock(hallway, "Passage strip", new Vector3(0f, 3.13f, 0f),
                    new Vector3(0.065f, 0.045f, run - 0.45f), mint, false);

                int node = Centers.Length + index;
                nodes[node] = transform.TransformPoint(midpoint);
                links[index * 2] = (first, node);
                links[index * 2 + 1] = (node, second);
            }
            PatrolNodes = nodes;
            PatrolLinks = links;
        }

        private void TintLamp(Zone zone, float x, float y, float z, Color tone, float intensity)
        {
            GameObject strip = BuildBlock(zone, "Ceiling task strip", x, y, z, 0.13f, 0.07f, 3.4f, mint, false);
            Transform source = Group(zone.Root, "Local task light", new Vector3(x, y - 0.19f, z));
            var light = source.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.range = 6.3f;
            light.intensity = intensity;
            light.color = tone;
            lamps.Add(new Lamp { Light = light, Strip = strip.GetComponent<MeshRenderer>(), DefaultColor = tone, DefaultIntensity = intensity });
        }

        private GameObject Gate(Zone zone, string title, int xSign, int zSign)
        {
            const float partition = 2.45f;
            const float opening = 2.21f;
            const float doorAt = 5.10f;
            const float insideHeight = 3.45f;
            float face = zSign * partition;
            float longSpan = HalfRoom - partition;
            Wall(zone, "Vault side partition", true, xSign * partition, zSign * (partition + longSpan * 0.5f),
                0f, longSpan + 0.18f, insideHeight, metal);
            float innerEnd = doorAt - opening * 0.5f;
            float outerEnd = doorAt + opening * 0.5f;
            Wall(zone, "Vault portal partition", false, face, xSign * ((partition + innerEnd) * 0.5f),
                0f, innerEnd - partition, insideHeight, metal);
            Wall(zone, "Vault portal partition", false, face, xSign * ((outerEnd + HalfRoom) * 0.5f),
                0f, HalfRoom - outerEnd, insideHeight, metal);
            Wall(zone, "Vault door lintel", false, face, xSign * doorAt,
                2.65f, opening, insideHeight - 2.65f, trim);
            BuildBlock(zone, "Vault ceiling", xSign * (partition + longSpan * 0.5f), insideHeight + 0.12f,
                zSign * (partition + longSpan * 0.5f), longSpan, 0.24f, longSpan, charcoal);
            float facing = zSign < 0 ? 180f : 0f;
            Sign(zone, title, xSign * doorAt, 2.94f, face - zSign * 0.191f, 0.135f, mint.color, facing);
            return AddBlock(zone.Root, title + " bulkhead", new Vector3(xSign * doorAt, 1.325f, face),
                new Vector3(opening, 2.65f, 0.34f), zone.Wall);
        }

        private Transform Station(Zone zone, ushort id, string title, float x, float y, float z, float facing = 0f,
            float baseY = 0f, Material cap = null)
        {
            float height = Mathf.Max(0.16f, y - baseY - 0.095f);
            BuildBlock(zone, "Console pedestal", x, baseY + height * 0.5f, z, 0.62f, height, 0.44f, trim);
            var plate = AddBlock(zone.Root, title + " pad", new Vector3(x, y, z),
                new Vector3(0.67f, 0.19f, 0.37f), cap != null ? cap : playerPad, false);
            plate.transform.localRotation = Quaternion.Euler(0f, facing, 0f);
            float front = facing == 180f ? 0.23f : -0.23f;
            Sign(zone, title, x, y + 0.24f, z + front, 0.105f, Color.white, facing);
            register(id, plate.transform);
            return plate.transform;
        }

        private TextMesh Sign(Zone zone, string text, float x, float y, float z, float size, Color color,
            float facing = 0f)
        {
            TextMesh sign = FacilityGeometry.Label(zone.Root, text, new Vector3(x, y, z), size, color);
            sign.transform.localRotation = Quaternion.Euler(0f, facing, 0f);
            return sign;
        }

        private GameObject BuildBlock(Zone zone, string name, float x, float y, float z,
            float width, float height, float depth, Material material, bool solid = true) =>
            AddBlock(zone.Geometry, name, new Vector3(x, y, z), new Vector3(width, height, depth), material, solid);

        private static GameObject AddBlock(Transform parent, string name, Vector3 center, Vector3 dimensions,
            Material material, bool solid = true) =>
            FacilityGeometry.Block(parent, name, center, dimensions, material, CalibrationLayers.Course, solid);

        private static Transform Group(Transform parent, string name, Vector3 center)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = center;
            return child;
        }

        private Material Finish(Material source, string label, byte r, byte g, byte b)
        {
            var material = new Material(source) { name = label, color = new Color32(r, g, b, 255) };
            finishes.Add(material);
            return material;
        }

        private static bool Discovered(uint progress, int bit) => (progress & (1u << bit)) != 0;

        private static void SetOpen(GameObject gate, bool open)
        {
            if (gate != null)
                gate.SetActive(!open);
        }

        private void OnDestroy()
        {
            foreach (Material finish in finishes)
                Destroy(finish);
        }
    }
}
