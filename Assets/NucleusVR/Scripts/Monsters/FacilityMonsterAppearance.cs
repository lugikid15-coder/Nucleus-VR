using System.Collections.Generic;
using NucleusVR.AI;
using UnityEngine;
using UnityEngine.Rendering;

namespace NucleusVR.Monsters
{
    internal sealed class FacilityMonsterAppearance : MonoBehaviour
    {
        private static readonly Color32[] ShellColors =
        {
            new(61, 96, 86, 255), new(70, 72, 91, 255), new(107, 76, 67, 255),
            new(103, 99, 70, 255), new(79, 64, 86, 255)
        };
        private static readonly Color32[] SignalColors =
        {
            new(149, 217, 112, 255), new(188, 157, 249, 255), new(244, 145, 74, 255),
            new(240, 208, 111, 255), new(239, 86, 133, 255)
        };

        private readonly List<Object> generated = new();
        private Transform body;
        private Transform motion;
        private MeshRenderer luminous;
        private AudioSource speaker;
        private AudioClip notice;
        private AudioClip movement;
        private FacilityBehavior behavior;
        private int species;
        private float phase, age, footfall;

        public void Build(int index, Material lit, Material signal)
        {
            species = index;
            body = new GameObject("Chassis").transform;
            body.SetParent(transform, false);
            var shell = new Surface();
            var light = new Surface();
            var joints = new Surface();
            switch (species)
            {
                case 0: Crawler(shell, light, joints); break;
                case 1: Watcher(shell, light, joints); break;
                case 2: Brute(shell, light, joints); break;
                case 3: Mimic(shell, light, joints); break;
                case 4: Experiment(shell, light, joints); break;
            }

            Material shellFinish = Own(new Material(lit) { name = name + " shell", color = ShellColors[species] });
            Material signalFinish = Own(new Material(signal) { name = name + " signals", color = SignalColors[species] });
            BuildSurface(body, "Shell", shell, shellFinish, true);
            luminous = BuildSurface(body, "Indicators", light, signalFinish, false);
            motion = BuildSurface(body, "Articulation", joints, shellFinish, false).transform;

            speaker = gameObject.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            speaker.spatialBlend = 1f;
            speaker.spread = 22f;
            speaker.minDistance = 1.3f;
            speaker.maxDistance = 22f;
            speaker.rolloffMode = AudioRolloffMode.Linear;
            speaker.dopplerLevel = 0f;
            notice = CreateCue(false);
            movement = CreateCue(true);
        }

        public void ResetPose(Vector3 position, float yaw, FacilityBehavior state)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            behavior = state;
            phase = species * 0.44f;
            age = footfall = 0f;
            body.gameObject.SetActive(state != FacilityBehavior.Dormant);
            luminous.enabled = state != FacilityBehavior.Defeated;
            body.localPosition = Vector3.zero;
            body.localRotation = Quaternion.identity;
            motion.localRotation = Quaternion.identity;
            speaker.Stop();
        }

        public void SetBehavior(FacilityBehavior state)
        {
            behavior = state;
            age = 0f;
            body.gameObject.SetActive(state != FacilityBehavior.Dormant);
            luminous.enabled = state != FacilityBehavior.Defeated;
            if (state is FacilityBehavior.Windup or FacilityBehavior.Lure or FacilityBehavior.Observe or FacilityBehavior.Defeated)
                speaker.PlayOneShot(notice, state == FacilityBehavior.Observe ? 0.25f : 0.49f);
        }

        public void Present(Vector3 position, float yaw, float delta)
        {
            if (behavior == FacilityBehavior.Dormant)
                return;
            delta = Mathf.Min(delta, 0.1f);
            age += delta;
            float blend = 1f - Mathf.Exp(-22f * delta);
            Vector3 previous = transform.position;
            Vector3 travel = position - previous;
            // Unobserved Watcher relocations and fresh snapshots must snap through walls.
            Vector3 pose = travel.sqrMagnitude > 12f ? position : Vector3.Lerp(previous, position, blend);
            transform.SetPositionAndRotation(pose, Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, yaw, 0f), blend));

            bool moving = travel.sqrMagnitude > 0.0025f && behavior is
                FacilityBehavior.Patrol or FacilityBehavior.Investigate or FacilityBehavior.Search or FacilityBehavior.Chase;
            phase += delta * (moving ? 11f + (species == 0 ? 7f : 0f) : 2.7f);
            float wave = Mathf.Sin(phase);
            float bow = behavior switch
            {
                FacilityBehavior.Windup => -17f * Mathf.SmoothStep(0f, 1f, age * 2f),
                FacilityBehavior.Recovery => 17f * Mathf.Exp(-age * 4f),
                FacilityBehavior.Defeated => Mathf.SmoothStep(0f, 87f, age * 2.3f),
                _ => 0f
            };
            body.localPosition = new Vector3(0f, behavior == FacilityBehavior.Defeated ? 0.26f : Mathf.Abs(wave) *
                (moving ? 0.045f : 0.012f), 0f);
            body.localRotation = Quaternion.Euler(bow, 0f, moving ? wave * 3f : 0f);
            motion.localRotation = Quaternion.Euler(behavior == FacilityBehavior.Windup ? -Mathf.Min(25f, age * 52f) :
                wave * (moving ? 8f : 1.4f), behavior == FacilityBehavior.Lure ? Mathf.Sin(age * 15f) * 12f : 0f, 0f);
            footfall -= delta;
            if (moving && footfall <= 0f)
            {
                footfall = species is 2 or 4 ? 0.39f : 0.24f;
                speaker.PlayOneShot(movement, behavior == FacilityBehavior.Chase ? 0.33f : 0.18f);
            }
        }

        private MeshRenderer BuildSurface(Transform parent, string label, Surface surface, Material material, bool shadow)
        {
            var item = new GameObject(label);
            item.transform.SetParent(parent, false);
            var filter = item.AddComponent<MeshFilter>();
            filter.sharedMesh = Own(surface.Mesh(label));
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return renderer;
        }

        private AudioClip CreateCue(bool step)
        {
            const int rate = 16000;
            float seconds = step ? 0.14f : species == 3 ? 1.06f : 0.48f;
            var pcm = new float[Mathf.CeilToInt(seconds * rate)];
            var random = new System.Random(311 + species * 13 + (step ? 1 : 0));
            float pitch = species switch { 0 => 139f, 1 => 255f, 2 => 52f, 3 => 419f, _ => 81f };
            for (int i = 0; i < pcm.Length; i++)
            {
                float time = (float)i / rate;
                float fade = Mathf.Min(1f, time * 195f) * Mathf.Pow(1f - time / seconds, step ? 2f : 1.1f);
                float angle = time * pitch * (6.283185f - 2.1f * time);
                float hiss = (float)random.NextDouble() * 2f - 1f;
                float signal = Mathf.Sin(angle) * 0.59f + Mathf.Sin(angle * 2.46f) * 0.19f;
                if (species == 3 && !step)
                    signal *= Mathf.Pow(Mathf.Abs(Mathf.Sin(time * 13.8f)), 4f);
                pcm[i] = (signal * (step ? 0.29f : 0.61f) + hiss * (step ? 0.32f : 0.105f)) * fade;
            }
            var clip = Own(AudioClip.Create(step ? "Tread" : "Signal", pcm.Length, 1, rate, false));
            clip.SetData(pcm, 0);
            return clip;
        }

        private T Own<T>(T asset) where T : Object
        {
            generated.Add(asset);
            return asset;
        }

        private void OnDestroy()
        {
            foreach (Object asset in generated)
                if (asset != null)
                    Destroy(asset);
        }

        private static void Crawler(Surface shell, Surface light, Surface joints)
        {
            shell.Box(new(0f, 0.46f, -0.12f), new(0.68f, 0.48f, 1.13f), -10f);
            shell.Box(new(0f, 0.42f, 0.47f), new(0.52f, 0.27f, 0.46f), 9f);
            for (int side = -1; side <= 1; side += 2)
            {
                light.Box(new(side * 0.145f, 0.47f, 0.71f), new(0.13f, 0.047f, 0.029f));
                for (int leg = 0; leg < 3; leg++)
                {
                    float z = -0.52f + leg * 0.45f;
                    Vector3 knee = new(side * (0.56f + 0.04f * leg), 0.44f, z + 0.13f);
                    joints.Beam(new(side * 0.27f, 0.43f, z), knee, 0.13f);
                    joints.Beam(knee, new(side * 0.67f, 0.10f, z + 0.32f), 0.081f);
                }
            }
        }

        private static void Watcher(Surface shell, Surface light, Surface joints)
        {
            shell.Box(new(0f, 1.3f, 0f), new(0.34f, 0.75f, 0.32f), -9f);
            shell.Box(new(0f, 1.95f, 0.05f), new(0.56f, 0.51f, 0.37f), 10f);
            shell.Box(new(0f, 2.21f, 0f), new(0.22f, 0.14f, 0.30f), -6f);
            light.Box(new(0f, 1.98f, 0.253f), new(0.34f, 0.042f, 0.028f), 10f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 knee = new(side * 0.14f, 0.57f, -0.09f);
                shell.Beam(new(side * 0.11f, 1.11f, 0f), knee, 0.14f);
                shell.Beam(knee, new(side * 0.17f, 0.12f, 0.11f), 0.09f);
                joints.Beam(new(side * 0.17f, 1.62f, 0f), new(side * 0.32f, 1.22f, -0.03f), 0.11f);
                joints.Beam(new(side * 0.32f, 1.22f, -0.03f), new(side * 0.31f, 0.77f, 0.09f), 0.065f);
            }
        }

        private static void Brute(Surface shell, Surface light, Surface joints)
        {
            shell.Box(new(0f, 1.15f, -0.07f), new(1.12f, 0.89f, 0.69f), 7f);
            shell.Box(new(0f, 1.69f, -0.08f), new(1.37f, 0.46f, 0.78f), 11f);
            shell.Box(new(0f, 1.70f, 0.43f), new(0.59f, 0.48f, 0.48f), 14f);
            light.Box(new(0f, 1.72f, 0.69f), new(0.39f, 0.056f, 0.034f));
            for (int side = -1; side <= 1; side += 2)
            {
                shell.Beam(new(side * 0.37f, 0.91f, -0.06f), new(side * 0.41f, 0.18f, 0.10f), 0.34f);
                shell.Box(new(side * 0.40f, 0.16f, 0.17f), new(0.39f, 0.25f, 0.56f));
                joints.Beam(new(side * 0.55f, 1.67f, 0f), new(side * 0.78f, 1.10f, 0.17f), 0.29f);
                joints.Beam(new(side * 0.78f, 1.11f, 0.17f), new(side * 0.85f, 0.60f, 0.32f), 0.39f);
                joints.Box(new(side * 0.85f, 0.56f, 0.39f), new(0.43f, 0.33f, 0.46f), -9f);
            }
        }

        private static void Mimic(Surface shell, Surface light, Surface joints)
        {
            shell.Box(new(0f, 0.65f, 0f), new(0.62f, 0.69f, 0.63f));
            shell.Box(new(0f, 0.67f, 0.32f), new(0.48f, 0.48f, 0.18f), 9f);
            for (int bar = 0; bar < 3; bar++)
                light.Box(new(0f, 0.57f + 0.09f * bar, 0.433f), new(0.34f, 0.044f, 0.026f));
            for (int side = -1; side <= 1; side += 2)
            {
                joints.Beam(new(side * 0.24f, 0.48f, -0.15f), new(side * 0.42f, 0.085f, -0.32f), 0.12f);
                joints.Beam(new(side * 0.23f, 0.48f, 0.21f), new(side * 0.39f, 0.09f, 0.46f), 0.10f);
            }
            joints.Beam(new(0.10f, 0.93f, -0.09f), new(0.25f, 1.36f, -0.19f), 0.073f);
            joints.Box(new(0.26f, 1.35f, -0.19f), new(0.14f, 0.18f, 0.14f), 15f);
        }

        private static void Experiment(Surface shell, Surface light, Surface joints)
        {
            shell.Box(new(0f, 1.33f, -0.08f), new(0.70f, 0.99f, 0.52f), -4f);
            shell.Box(new(0f, 1.89f, -0.06f), new(0.97f, 0.49f, 0.64f), -4f);
            shell.Box(new(0f, 2.31f, 0.16f), new(0.48f, 0.43f, 0.42f), 12f);
            light.Box(new(0f, 2.35f, 0.396f), new(0.29f, 0.079f, 0.036f), 12f);
            light.Box(new(0f, 1.64f, 0.29f), new(0.24f, 0.34f, 0.19f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 knee = new(side * 0.28f, 0.62f, -0.15f);
                shell.Beam(new(side * 0.24f, 1.08f, -0.03f), knee, 0.26f);
                shell.Beam(knee, new(side * 0.33f, 0.14f, 0.18f), 0.22f);
                joints.Beam(new(side * 0.39f, 1.98f, 0f), new(side * 0.67f, 1.52f, -0.08f), 0.25f);
                joints.Beam(new(side * 0.67f, 1.55f, -0.08f), new(side * 0.78f, 0.91f, 0.35f), 0.29f);
                joints.Beam(new(side * 0.28f, 1.87f, -0.24f), new(side * 0.47f, 2.58f, -0.39f), 0.15f);
                for (int rib = 0; rib < 3; rib++)
                {
                    float y = 1.32f + 0.15f * rib;
                    shell.Beam(new(side * 0.37f, y, 0.17f), new(side * 0.15f, y + 0.12f, 0.37f), 0.075f);
                }
            }
        }

        private sealed class Surface
        {
            private readonly List<Vector3> vertices = new();
            private readonly List<int> triangles = new();

            private static readonly Vector3[] Corners =
            {
                new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
                new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)
            };
            private static readonly int[] Faces =
            {
                0, 3, 2, 1, 4, 5, 6, 7, 0, 4, 7, 3, 1, 2, 6, 5, 3, 7, 6, 2, 0, 1, 5, 4
            };

            public void Beam(Vector3 from, Vector3 to, float width)
            {
                Vector3 axis = to - from;
                Box((from + to) * 0.5f, new Vector3(width, axis.magnitude + width * 0.28f, width),
                    Quaternion.FromToRotation(Vector3.up, axis));
            }

            public void Box(Vector3 center, Vector3 size, float pitch = 0f) =>
                Box(center, size, Quaternion.Euler(pitch, 0f, 0f));

            private void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                Vector3 half = size * 0.5f;
                for (int face = 0; face < Faces.Length; face += 4)
                {
                    int offset = vertices.Count;
                    for (int corner = 0; corner < 4; corner++)
                        vertices.Add(center + rotation * Vector3.Scale(Corners[Faces[face + corner]], half));
                    triangles.Add(offset);
                    triangles.Add(offset + 1);
                    triangles.Add(offset + 2);
                    triangles.Add(offset);
                    triangles.Add(offset + 2);
                    triangles.Add(offset + 3);
                }
            }

            public Mesh Mesh(string label)
            {
                var mesh = new Mesh { name = label };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
