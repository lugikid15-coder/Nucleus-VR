using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NucleusVR.Anatomy
{
    public enum GoreQuality : byte
    {
        Off,
        Low,
        Medium,
        High
    }

    /// <summary>Shares visual assets across destructible subjects while leaving collision and damage to their owners.</summary>
    public sealed class AnatomyVisuals : IDisposable
    {
        private const int SeveranceRings = 2;
        private readonly Material template;
        private readonly Dictionary<string, Mesh> meshes = new();
        private readonly Dictionary<Color32, Material> materials = new();
        private bool disposed;

        public readonly struct OrganMount
        {
            internal OrganMount(AnatomyRegion region, Vector3 position, Vector3 euler)
            {
                Region = region;
                LocalPosition = position;
                LocalEuler = euler;
            }

            public AnatomyRegion Region { get; }
            public Vector3 LocalPosition { get; }
            public Vector3 LocalEuler { get; }
        }

        public readonly struct Connection
        {
            internal Connection(int from, int to, float radius)
            {
                From = from;
                To = to;
                Radius = radius;
            }

            public int From { get; }
            public int To { get; }
            public float Radius { get; }
        }

        public static IReadOnlyList<Vector3> VesselLandmarks => AnatomyVisualShapes.VesselLandmarks;
        public static IReadOnlyList<Vector3> NerveLandmarks => AnatomyVisualShapes.NerveLandmarks;
        public static IReadOnlyList<Connection> VesselConnections => AnatomyVisualShapes.VesselGraph;
        public static IReadOnlyList<Connection> NerveConnections => AnatomyVisualShapes.NerveGraph;

        public AnatomyVisuals(Material lit)
        {
            if (lit == null)
                throw new ArgumentNullException(nameof(lit));

            template = lit;
        }

        /// <summary>Mounts in meters relative to the centered head or torso pivot, with +Z forward.</summary>
        public static OrganMount GetOrganMount(OrganKind organ)
        {
            return organ switch
            {
                OrganKind.Brain => new OrganMount(AnatomyRegion.Head, new Vector3(0f, 0.028f, 0.072f), Vector3.zero),
                OrganKind.Heart => TorsoMount(0.012f, 0.064f, 0.169f, -11f),
                OrganKind.Lungs => TorsoMount(0f, 0.094f, 0.104f),
                OrganKind.Liver => TorsoMount(0.104f, -0.048f, 0.134f, 7f),
                OrganKind.Stomach => TorsoMount(-0.095f, -0.066f, 0.140f, -8f),
                OrganKind.Intestines => TorsoMount(0f, -0.146f, 0.159f),
                OrganKind.Kidneys => TorsoMount(0f, -0.163f, 0.103f),
                _ => throw new ArgumentOutOfRangeException(nameof(organ))
            };
        }

        public RegionView BuildRegion(Transform pivot, Vector3 fullDimensions, AnatomyRegion region)
        {
            RequirePivot(pivot);
            if (region >= AnatomyRegion.Count)
                throw new ArgumentOutOfRangeException(nameof(region));
            if (!Positive(fullDimensions.x) || !Positive(fullDimensions.y) || !Positive(fullDimensions.z))
                throw new ArgumentOutOfRangeException(nameof(fullDimensions));

            bool head = region == AnatomyRegion.Head;
            AnatomyRegion boneProfile = region switch
            {
                AnatomyRegion.RightUpperArm => AnatomyRegion.LeftUpperArm,
                AnatomyRegion.RightForearm => AnatomyRegion.LeftForearm,
                _ => region
            };
            Transform group = NewGroup(pivot, "Anatomical Region");
            group.localScale = fullDimensions;
            var view = new RegionView(
                new Layer(group, "Skin", MeshFor($"{boneProfile} Skin", () => AnatomyVisualShapes.Shell(
                    "Intact skin", 0f, 0f, 0f, 0, false, false, boneProfile)), Pigment(63, 79, 81)),
                new Layer(group, "Muscle", MeshFor("Muscle", () => AnatomyVisualShapes.Shell(
                    "Muscle", 0f, 0f, 0f, 0, false, true)), Pigment(168, 56, 56)),
                new Layer(group, "Bone", MeshFor($"{boneProfile} Bones", () => AnatomyVisualShapes.Bones(
                    boneProfile, false)), Pigment(224, 197, 160)),
                new Layer(group, "Arteries", MeshFor("Arteries", () => AnatomyVisualShapes.Branches(false)),
                    Pigment(234, 92, 103)),
                new Layer(group, "Veins", MeshFor("Veins", () => AnatomyVisualShapes.Branches(false, true)),
                    Pigment(97, 63, 133)),
                new Layer(group, "Nerves", MeshFor("Nerves", () => AnatomyVisualShapes.Branches(true)),
                    Pigment(220, 173, 97)),
                new Layer(group, "Internal Tissue", MeshFor(head ? "Head Tissue" : "Tissue",
                    () => AnatomyVisualShapes.Tissue(head)), Pigment(111, 43, 65)),
                new Layer(group, "Cut Surface", MeshFor(head ? "Head Cut" : "Cut",
                    () => AnatomyVisualShapes.Severance(head, false)), Pigment(147, 54, 60)),
                new Layer(group, "Cut Bone", MeshFor(head ? "Head Cut Bone" : "Cut Bone",
                    () => AnatomyVisualShapes.Severance(head, true)), Pigment(239, 210, 173)),
                MeshFor($"{boneProfile} Wounded Skin", () => AnatomyVisualShapes.Shell(
                    "Wounded skin", 38f, 0.35f, 0.10f, 0, false, false, boneProfile)),
                MeshFor($"{boneProfile} Exposed Skin", () => AnatomyVisualShapes.Shell(
                    "Exposed skin", 79f, 0.84f, 0f, 0, false, false, boneProfile)),
                MeshFor(head ? "Severed Head Skin Low" : "Severed Skin Low", () => AnatomyVisualShapes.Shell(
                    "Severed wounded skin", 38f, 0.35f, 0.10f, SeveranceRings, head, false,
                    head ? AnatomyRegion.Head : AnatomyRegion.Count)),
                MeshFor(head ? "Severed Head Skin" : "Severed Skin", () => AnatomyVisualShapes.Shell(
                    "Severed skin", 79f, 0.84f, 0f, SeveranceRings, head, false,
                    head ? AnatomyRegion.Head : AnatomyRegion.Count)),
                MeshFor("Exposed Muscle", () => AnatomyVisualShapes.Shell(
                    "Exposed muscle", 66f, 0.75f, 0f, 0, false, true)),
                MeshFor(head ? "Severed Head Muscle Low" : "Severed Muscle Low", () => AnatomyVisualShapes.Shell(
                    "Severed muscle", 0f, 0f, 0f, SeveranceRings, head, true)),
                MeshFor(head ? "Severed Head Muscle" : "Severed Muscle", () => AnatomyVisualShapes.Shell(
                    "Severed exposed muscle", 66f, 0.75f, 0f, SeveranceRings, head, true)),
                MeshFor($"{boneProfile} Fragments", () => AnatomyVisualShapes.Bones(boneProfile, true))
            );
            view.ShowState(AnatomyStage.Intact, GoreQuality.Off);
            return view;
        }

        public OrganView BuildOrgan(Transform pivot, OrganKind organ, float size = 1f)
        {
            RequirePivot(pivot);
            if (organ >= OrganKind.Count)
                throw new ArgumentOutOfRangeException(nameof(organ));
            if (!Positive(size))
                throw new ArgumentOutOfRangeException(nameof(size));

            Transform group = NewGroup(pivot, $"{organ} Visual");
            group.localScale = Vector3.one * size;
            Color32 light = OrganPigment(organ);
            var layer = new Layer(group, $"{organ} Surface",
                MeshFor(organ.ToString(), () => AnatomyVisualShapes.Organ(organ, false)),
                Pigment(light.r, light.g, light.b));
            return new OrganView(layer, MeshFor($"Damaged {organ}", () => AnatomyVisualShapes.Organ(organ, true)),
                Pigment((byte)(light.r * 0.73f), (byte)(light.g * 0.62f), (byte)(light.b * 0.69f)),
                Pigment(130, 104, 110), size);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            foreach (Mesh mesh in meshes.Values)
                Release(mesh);
            foreach (Material material in materials.Values)
                Release(material);
            meshes.Clear();
            materials.Clear();
        }

        private void RequirePivot(Transform pivot)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AnatomyVisuals));
            if (pivot == null)
                throw new ArgumentNullException(nameof(pivot));
        }

        private Mesh MeshFor(string key, Func<Mesh> make)
        {
            if (!meshes.TryGetValue(key, out Mesh mesh))
            {
                mesh = make();
                meshes.Add(key, mesh);
            }
            return mesh;
        }

        private Material Pigment(byte r, byte g, byte b)
        {
            var color = new Color32(r, g, b, 255);
            if (materials.TryGetValue(color, out Material material))
                return material;

            material = new Material(template) { name = $"Anatomy Pigment {r}-{g}-{b}" };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            else if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", Texture2D.whiteTexture);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.32f);
            materials.Add(color, material);
            return material;
        }

        private static Color32 OrganPigment(OrganKind organ)
        {
            return organ switch
            {
                OrganKind.Brain => new Color32(220, 138, 150, 255),
                OrganKind.Heart => new Color32(184, 58, 68, 255),
                OrganKind.Lungs => new Color32(210, 105, 125, 255),
                OrganKind.Liver => new Color32(122, 49, 71, 255),
                OrganKind.Stomach => new Color32(207, 126, 95, 255),
                OrganKind.Intestines => new Color32(199, 148, 128, 255),
                OrganKind.Kidneys => new Color32(168, 67, 102, 255),
                _ => throw new ArgumentOutOfRangeException(nameof(organ))
            };
        }

        private static OrganMount TorsoMount(float x, float y, float z, float roll = 0f)
        {
            return new OrganMount(AnatomyRegion.Torso, new Vector3(x, y, z), new Vector3(0f, 0f, roll));
        }

        private static Transform NewGroup(Transform parent, string name)
        {
            var group = new GameObject(name);
            group.layer = parent.gameObject.layer;
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static bool Positive(float value) => value > 0f && !float.IsInfinity(value);

        private static void Release(Object asset)
        {
            if (asset == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(asset);
            else
                Object.DestroyImmediate(asset);
        }

        internal sealed class Layer
        {
            private readonly MeshFilter filter;
            private readonly MeshRenderer renderer;

            internal Layer(Transform parent, string name, Mesh standard, Material material)
            {
                Transform target = NewGroup(parent, name);
                filter = target.gameObject.AddComponent<MeshFilter>();
                renderer = target.gameObject.AddComponent<MeshRenderer>();
                DefaultMesh = standard;
                DefaultMaterial = material;
                filter.sharedMesh = standard;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.enabled = false;
            }

            internal Mesh DefaultMesh { get; }
            internal Material DefaultMaterial { get; }

            internal void Display(bool visible, Mesh mesh = null, Material material = null)
            {
                renderer.enabled = visible;
                if (!visible)
                    return;

                Mesh targetMesh = mesh != null ? mesh : DefaultMesh;
                Material targetMaterial = material != null ? material : DefaultMaterial;
                if (filter.sharedMesh != targetMesh)
                    filter.sharedMesh = targetMesh;
                if (renderer.sharedMaterial != targetMaterial)
                    renderer.sharedMaterial = targetMaterial;
            }
        }

        public sealed class RegionView
        {
            private readonly Layer skin;
            private readonly Layer muscle;
            private readonly Layer bone;
            private readonly Layer arteries;
            private readonly Layer veins;
            private readonly Layer nerves;
            private readonly Layer tissue;
            private readonly Layer cutSurface;
            private readonly Layer cutBone;
            private readonly Mesh wounded;
            private readonly Mesh exposed;
            private readonly Mesh severedLow;
            private readonly Mesh severed;
            private readonly Mesh exposedMuscle;
            private readonly Mesh severedMuscleLow;
            private readonly Mesh severedMuscle;
            private readonly Mesh fragments;

            internal RegionView(Layer skin, Layer muscle, Layer bone, Layer arteries, Layer veins, Layer nerves,
                Layer tissue, Layer cutSurface, Layer cutBone, Mesh wounded, Mesh exposed, Mesh severedLow,
                Mesh severed, Mesh exposedMuscle, Mesh severedMuscleLow, Mesh severedMuscle, Mesh fragments)
            {
                this.skin = skin;
                this.muscle = muscle;
                this.bone = bone;
                this.arteries = arteries;
                this.veins = veins;
                this.nerves = nerves;
                this.tissue = tissue;
                this.cutSurface = cutSurface;
                this.cutBone = cutBone;
                this.wounded = wounded;
                this.exposed = exposed;
                this.severedLow = severedLow;
                this.severed = severed;
                this.exposedMuscle = exposedMuscle;
                this.severedMuscleLow = severedMuscleLow;
                this.severedMuscle = severedMuscle;
                this.fragments = fragments;
            }

            public void ShowState(AnatomyStage stage, GoreQuality quality, bool fractured = false, bool detached = false)
            {
                bool wound = quality != GoreQuality.Off && stage >= AnatomyStage.Wounded;
                bool open = quality != GoreQuality.Off && stage >= AnatomyStage.Exposed;
                bool layers = quality >= GoreQuality.Medium && open;
                bool broken = fractured || stage >= AnatomyStage.Fractured;
                bool severance = quality != GoreQuality.Off && (detached || stage == AnatomyStage.Severed);
                bool branches = quality == GoreQuality.High && open;

                Mesh skinMesh = skin.DefaultMesh;
                Mesh muscleMesh = muscle.DefaultMesh;
                if (severance)
                {
                    skinMesh = open ? severed : severedLow;
                    muscleMesh = open ? severedMuscle : severedMuscleLow;
                }
                else if (open)
                {
                    skinMesh = exposed;
                    muscleMesh = exposedMuscle;
                }
                else if (wound)
                {
                    skinMesh = wounded;
                }

                skin.Display(true, skinMesh);
                muscle.Display(wound || severance, muscleMesh);
                bone.Display(layers, broken ? fragments : null);
                arteries.Display(branches);
                veins.Display(branches);
                nerves.Display(branches);
                tissue.Display(layers);
                cutSurface.Display(severance);
                cutBone.Display(severance && quality >= GoreQuality.Medium);
            }
        }

        public sealed class OrganView
        {
            private readonly Layer surface;
            private readonly Mesh alternate;
            private readonly Material damagedMaterial;
            private readonly Material lowDetailMaterial;

            internal OrganView(Layer surface, Mesh alternate, Material damagedMaterial, Material lowDetailMaterial,
                float size)
            {
                this.surface = surface;
                this.alternate = alternate;
                this.damagedMaterial = damagedMaterial;
                this.lowDetailMaterial = lowDetailMaterial;

                Bounds bounds = surface.DefaultMesh.bounds;
                LocalBounds = new Bounds(bounds.center * size, bounds.size * size);
            }

            /// <summary>Intact organ bounds around its supplied physics pivot.</summary>
            public Bounds LocalBounds { get; }

            public void ShowState(bool damaged, GoreQuality quality, bool visible = true)
            {
                bool injured = damaged && quality >= GoreQuality.Medium;
                Material material = quality == GoreQuality.Low ? lowDetailMaterial : injured ? damagedMaterial : null;
                surface.Display(visible && quality != GoreQuality.Off, injured ? alternate : null, material);
            }
        }
    }
}
