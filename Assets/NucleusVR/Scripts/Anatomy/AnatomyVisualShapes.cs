using System;
using System.Collections.Generic;
using UnityEngine;

namespace NucleusVR.Anatomy
{
    internal static class AnatomyVisualShapes
    {
        internal const int ShellRings = 12;
        internal const int ShellSides = 16;
        private static readonly Quaternion NoRotation = Quaternion.identity;

        // Each edge links two authored landmarks from the core out to a smaller peripheral branch.
        internal static readonly IReadOnlyList<AnatomyVisuals.Connection> VesselGraph =
            Array.AsReadOnly(new[]
            {
                Branch(0, 1, 0.025f), Branch(1, 2, 0.018f), Branch(2, 3, 0.012f),
                Branch(1, 4, 0.014f), Branch(4, 5, 0.009f), Branch(4, 6, 0.009f),
                Branch(1, 7, 0.015f), Branch(7, 8, 0.009f), Branch(7, 9, 0.008f),
                Branch(2, 10, 0.012f), Branch(10, 11, 0.006f), Branch(2, 12, 0.007f)
            });
        internal static readonly IReadOnlyList<AnatomyVisuals.Connection> NerveGraph =
            Array.AsReadOnly(new[]
            {
                Branch(0, 1, 0.013f), Branch(1, 2, 0.011f), Branch(2, 3, 0.009f),
                Branch(0, 4, 0.008f), Branch(4, 5, 0.006f), Branch(1, 7, 0.009f),
                Branch(7, 8, 0.006f), Branch(2, 10, 0.008f), Branch(10, 11, 0.006f),
                Branch(2, 12, 0.006f), Branch(12, 6, 0.005f)
            });

        internal static readonly IReadOnlyList<Vector3> VesselLandmarks = Array.AsReadOnly(new[]
        {
            P(-0.09f, 0.36f, 0.23f), P(-0.14f, 0.13f, 0.34f), P(-0.11f, -0.13f, 0.34f),
            P(-0.12f, -0.35f, 0.22f), P(-0.28f, 0.11f, 0.29f), P(-0.32f, 0.22f, 0.21f),
            P(-0.36f, -0.05f, 0.23f), P(0.16f, 0.19f, 0.34f), P(0.30f, 0.26f, 0.26f),
            P(0.32f, 0.11f, 0.29f), P(0.10f, -0.24f, 0.32f), P(0.27f, -0.21f, 0.25f),
            P(-0.24f, -0.24f, 0.30f)
        });
        internal static readonly IReadOnlyList<Vector3> NerveLandmarks = Array.AsReadOnly(new[]
        {
            P(0.12f, 0.37f, 0.25f), P(0.20f, 0.16f, 0.32f), P(0.19f, -0.12f, 0.34f),
            P(0.16f, -0.34f, 0.24f), P(0.31f, 0.27f, 0.25f), P(0.33f, 0.18f, 0.23f),
            P(-0.30f, -0.25f, 0.23f), P(0.34f, 0.08f, 0.28f), P(0.37f, -0.04f, 0.20f),
            P(0.30f, -0.19f, 0.25f), P(0.27f, -0.24f, 0.25f), P(0.29f, -0.30f, 0.22f),
            P(-0.19f, -0.16f, 0.31f)
        });

        public static Mesh Shell(string name, float window, float height, float center, int trim, bool bottom,
            bool muscle, AnatomyRegion region = AnatomyRegion.Count)
        {
            var mesh = new AnatomyVisualMesh();
            Vector3 radii = muscle ? P(0.448f, 0.46f, 0.425f) : P(0.50f, 0.50f, 0.50f);
            mesh.Lobe(Vector3.zero, radii, NoRotation, ShellSides, ShellRings,
                window, height, center, trim, bottom, muscle ? 0.048f : 0f);
            if (!muscle)
                SkinFeatures(mesh, region, window >= 30f);
            return mesh.Finish(name);
        }

        private static void SkinFeatures(AnatomyVisualMesh mesh, AnatomyRegion region, bool opened)
        {
            if (region == AnatomyRegion.Head)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Lobe(mesh, P(side * 0.450f, 0.005f, -0.085f), P(0.103f, 0.165f, 0.106f), z: side * 12f);
                    if (!opened)
                        Lobe(mesh, P(side * 0.195f, 0.141f, 0.420f), P(0.172f, 0.090f, 0.087f), z: side * 12f);
                }

                if (!opened)
                {
                    Lobe(mesh, P(0f, -0.10f, 0.441f), P(0.295f, 0.123f, 0.162f));
                    Lobe(mesh, P(0f, -0.216f, 0.392f), P(0.233f, 0.087f, 0.155f));
                }
            }
            else if (region == AnatomyRegion.Torso)
            {
                Lobe(mesh, P(0f, 0.452f, -0.006f), P(0.152f, 0.151f, 0.166f));
                if (opened)
                    return;

                for (int side = -1; side <= 1; side += 2)
                    Lobe(mesh, P(side * 0.212f, 0.162f, 0.323f), P(0.226f, 0.196f, 0.177f), z: -side * 14f);
            }
            else if (!opened && region == AnatomyRegion.LeftForearm)
            {
                Lobe(mesh, P(0f, -0.268f, 0.028f), P(0.346f, 0.235f, 0.320f), z: 8f);
            }
        }

        public static Mesh Tissue(bool head)
        {
            var mesh = new AnatomyVisualMesh();
            for (int side = -1; side <= 1; side += 2)
            {
                Lobe(mesh, P(side * 0.31f, 0.06f, 0.20f), P(0.12f, 0.26f, 0.12f), z: side * 12f);
                Lobe(mesh, P(side * 0.17f, -0.20f, 0.15f), P(0.16f, 0.12f, 0.14f), z: side * 25f);
                if (!head)
                    continue;
                Lobe(mesh, P(side * 0.19f, 0.17f, 0.18f), P(0.13f, 0.09f, 0.12f));
            }
            return mesh.Finish("Exposed connective tissue");
        }

        public static Mesh Branches(bool nerves, bool veins = false)
        {
            var mesh = new AnatomyVisualMesh();
            IReadOnlyList<Vector3> nodes = nerves ? NerveLandmarks : VesselLandmarks;
            IReadOnlyList<AnatomyVisuals.Connection> graph = nerves ? NerveGraph : VesselGraph;
            foreach (AnatomyVisuals.Connection edge in graph)
            {
                Vector3 a = nodes[edge.From];
                Vector3 b = nodes[edge.To];
                if (veins)
                {
                    a = P(-a.x, a.y - 0.014f, a.z - 0.021f);
                    b = P(-b.x, b.y - 0.014f, b.z - 0.021f);
                }
                Vector3 mid = (a + b) * 0.5f + P(0.013f, 0f, 0.026f);
                mesh.Tube(new[] { a, mid, b }, edge.Radius * (veins ? 0.74f : 1f), 0.72f, nerves ? 5 : 6);
            }
            return mesh.Finish(nerves ? "Branching nerves" : veins ? "Branching veins" : "Branching arteries");
        }

        public static Mesh Severance(bool head, bool inner)
        {
            var mesh = new AnatomyVisualMesh();
            float direction = head ? -1f : 1f;
            float y = direction * Mathf.Cos(Mathf.PI * 2f / ShellRings) * 0.5f;
            float radius = Mathf.Sin(Mathf.PI * 2f / ShellRings) * 0.5f;
            if (!inner)
            {
                mesh.Disc(P(0f, y, 0f), radius, radius, head);
            }
            else
            {
                mesh.Disc(P(-0.064f, y + 0.003f * direction, 0.022f), radius * 0.36f, radius * 0.40f, head);
                mesh.Disc(P(0.068f, y + 0.004f * direction, 0.024f), radius * 0.24f, radius * 0.27f, head);
            }
            return mesh.Finish(inner ? "Severed bone cross-section" : "Severed tissue cross-section");
        }

        public static Mesh Bones(AnatomyRegion region, bool fractured)
        {
            var mesh = new AnatomyVisualMesh();
            if (region == AnatomyRegion.Head)
                Skull(mesh, fractured);
            else if (region == AnatomyRegion.Torso)
                Ribs(mesh, fractured);
            else
                LongBones(mesh, region == AnatomyRegion.LeftForearm || region == AnatomyRegion.RightForearm, fractured);
            return mesh.Finish(fractured ? "Broken bone and exposed fragments" : "Exposed bone");
        }

        private static void Skull(AnatomyVisualMesh mesh, bool broken)
        {
            Lobe(mesh, P(0f, 0.06f, -0.10f), P(0.335f, 0.34f, 0.27f));
            for (int side = -1; side <= 1; side += 2)
            {
                mesh.Tube(new[] { P(side * 0.24f, 0.16f, 0.17f), P(side * 0.29f, -0.04f, 0.17f),
                    P(side * 0.22f, -0.23f, 0.21f), P(side * 0.09f, -0.28f, 0.29f) }, 0.056f, 0.78f);
                Lobe(mesh, P(side * 0.18f, 0.03f, 0.25f), P(0.10f, 0.052f, 0.055f), z: 18f * side);
            }
            if (broken)
                Lobe(mesh, P(0.19f, 0.25f, 0.255f), P(0.065f, 0.055f, 0.041f), z: 42f, sides: 5);
        }

        private static void Ribs(AnatomyVisualMesh mesh, bool broken)
        {
            mesh.Tube(new[] { P(0f, 0.43f, -0.18f), P(0f, 0.19f, -0.25f),
                P(0f, -0.17f, -0.24f), P(0f, -0.41f, -0.12f) }, 0.074f, 0.78f);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int rib = 0; rib < 4; rib++)
                {
                    float y = 0.34f - rib * 0.132f;
                    bool cracked = broken && rib == 1 && side == 1;
                    float tip = cracked ? 0.38f : 0.19f;
                    mesh.Tube(new[] { P(side * 0.035f, y, -0.22f), P(side * 0.27f, y - 0.035f, -0.16f),
                        P(side * 0.36f, y - 0.070f, 0.01f), P(side * tip, y - 0.082f, 0.26f) },
                        0.033f, 0.78f);
                    if (cracked)
                        Lobe(mesh, P(side * 0.25f, y - 0.09f, 0.28f), P(0.045f, 0.059f, 0.035f),
                            z: 38f, sides: 5);
                }
            }
        }

        private static void LongBones(AnatomyVisualMesh mesh, bool doubleBone, bool broken)
        {
            int count = doubleBone ? 2 : 1;
            for (int bone = 0; bone < count; bone++)
            {
                float x = doubleBone ? (bone == 0 ? -0.13f : 0.14f) : 0f;
                float radius = doubleBone ? 0.075f : 0.108f;
                Vector3 lower = P(x, -0.39f, 0.085f);
                Vector3 upper = P(x + 0.032f, 0.39f, 0.095f);
                if (broken)
                {
                    mesh.Tube(new[] { lower, P(x - 0.015f, -0.20f, 0.1f), P(x + 0.024f, -0.043f, 0.22f) },
                        radius, 0.70f);
                    mesh.Tube(new[] { upper, P(x + 0.04f, 0.22f, 0.10f), P(x - 0.03f, 0.055f, 0.19f) },
                        radius, 0.75f);
                    Lobe(mesh, P(x + 0.10f, -0.032f, 0.24f), P(0.037f, 0.084f, 0.035f), z: -30f, sides: 5);
                }
                else
                {
                    mesh.Tube(new[] { lower, P(x - 0.024f, 0f, 0.135f), upper }, radius, 0.9f, 8);
                }
                Lobe(mesh, lower, P(radius * 1.46f, 0.077f, radius * 1.25f));
                Lobe(mesh, upper, P(radius * 1.40f, 0.077f, radius * 1.22f));
            }
        }

        public static Mesh Organ(OrganKind organ, bool damaged)
        {
            var mesh = new AnatomyVisualMesh();
            switch (organ)
            {
                case OrganKind.Brain: Brain(mesh, damaged); break;
                case OrganKind.Heart: Heart(mesh, damaged); break;
                case OrganKind.Lungs: Lungs(mesh, damaged); break;
                case OrganKind.Liver: Liver(mesh, damaged); break;
                case OrganKind.Stomach: Stomach(mesh, damaged); break;
                case OrganKind.Intestines: Intestines(mesh, damaged); break;
                case OrganKind.Kidneys: Kidneys(mesh, damaged); break;
                default: throw new ArgumentOutOfRangeException(nameof(organ));
            }
            return mesh.Finish(damaged ? $"Damaged {organ}" : organ.ToString());
        }

        private static void Brain(AnatomyVisualMesh mesh, bool damaged)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float volume = damaged && side == 1 ? 0.64f : 1f;
                Lobe(mesh, P(side * 0.052f, 0f, -0.014f), P(0.064f, 0.070f, 0.090f) * volume, y: side * 10f);
                for (int fold = 0; fold < 4; fold++)
                {
                    float y = (fold - 1.5f) * 0.025f * volume;
                    float extent = 0.076f - Mathf.Abs(y) * 0.62f;
                    mesh.Tube(new[]
                    {
                        P(side * 0.034f, y, extent * volume),
                        P(side * (0.052f + 0.038f * volume), y + 0.011f, extent * 0.80f * volume),
                        P(side * (0.052f + 0.062f * volume), y - 0.008f, -0.016f),
                        P(side * (0.052f + 0.036f * volume), y + 0.009f, -extent * volume)
                    }, 0.010f * volume, 0.86f);
                }
            }
            Lobe(mesh, P(0f, -0.055f, -0.041f), P(0.042f, 0.036f, 0.042f));
        }

        private static void Heart(AnatomyVisualMesh mesh, bool damaged)
        {
            Lobe(mesh, P(-0.013f, -0.009f, 0f), P(0.050f, 0.061f, 0.036f), z: -19f);
            Lobe(mesh, P(0.036f, 0.005f, damaged ? -0.026f : 0.003f),
                damaged ? P(0.022f, 0.040f, 0.025f) : P(0.036f, 0.049f, 0.037f), z: 12f);
            mesh.Tube(new[] { P(0.009f, 0.025f, -0.013f), P(0.012f, 0.064f, -0.018f),
                P(-0.014f, 0.080f, -0.014f), P(-0.039f, 0.075f, 0.006f) }, 0.015f, 0.88f);
            mesh.Tube(new[] { P(0.031f, 0.025f, -0.002f), P(0.044f, 0.058f, 0.002f),
                P(0.062f, 0.059f, 0.013f) }, 0.011f, 0.82f);
        }

        private static void Lungs(AnatomyVisualMesh mesh, bool damaged)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float volume = damaged && side == 1 ? 0.65f : 1f;
                Lobe(mesh, P(side * 0.106f, 0.026f, -0.008f),
                    P(0.063f, 0.087f, 0.049f) * volume, z: side * 12f);
                Lobe(mesh, P(side * 0.117f, -0.037f, 0.004f),
                    P(0.067f, 0.051f, 0.053f) * volume, z: side * 24f);
                mesh.Tube(new[] { P(0f, 0.074f, 0.026f), P(side * 0.051f, 0.019f, 0.032f),
                    P(side * 0.106f, -0.012f, 0.034f) }, 0.015f, 0.50f);
            }
            mesh.Tube(new[] { P(0f, 0.119f, 0.014f), P(0f, 0.073f, 0.025f), P(0f, 0.044f, 0.026f) },
                0.017f, 0.97f);
        }

        private static void Liver(AnatomyVisualMesh mesh, bool damaged)
        {
            Lobe(mesh, P(-0.025f, 0f, 0.004f), P(0.075f, 0.047f, 0.042f), z: -13f);
            Lobe(mesh, P(0.053f, 0.011f, damaged ? -0.020f : -0.005f),
                damaged ? P(0.026f, 0.027f, 0.029f) : P(0.061f, 0.035f, 0.031f), z: 18f);
            Lobe(mesh, P(-0.022f, -0.027f, -0.004f), P(0.032f, 0.026f, 0.032f));
        }

        private static void Stomach(AnatomyVisualMesh mesh, bool damaged)
        {
            Lobe(mesh, P(0.016f, 0.005f, 0f), P(0.060f, 0.071f, 0.039f), z: -21f);
            Lobe(mesh, P(-0.036f, -0.028f, damaged ? -0.025f : 0.011f),
                damaged ? P(0.021f, 0.028f, 0.027f) : P(0.053f, 0.035f, 0.036f), z: 13f);
            mesh.Tube(new[]
            {
                P(0.032f, 0.102f, -0.024f), P(0.028f, 0.064f, -0.015f),
                P(0.049f, 0.028f, 0.001f)
            }, 0.015f, 1.30f);
            mesh.Tube(new[] { P(-0.054f, -0.038f, 0.004f), P(-0.089f, -0.055f, -0.009f),
                P(-0.085f, -0.079f, -0.009f) }, 0.014f, 0.82f);
        }

        private static void Intestines(AnatomyVisualMesh mesh, bool damaged)
        {
            mesh.Tube(new[] { P(-0.115f, -0.065f, -0.018f), P(-0.124f, 0.032f, -0.017f),
                P(-0.101f, 0.067f, -0.006f), P(0.087f, 0.066f, -0.007f),
                P(0.118f, 0.039f, -0.015f), P(0.116f, -0.051f, -0.015f),
                P(0.077f, -0.076f, -0.012f) }, 0.017f, 0.86f, 8);
            mesh.Tube(new[] { P(-0.074f, 0.041f, 0.017f), P(0.068f, 0.039f, 0.024f),
                P(0.082f, 0.018f, 0.021f), P(0.049f, 0f, 0.031f), P(-0.073f, 0.006f, 0.027f) },
                0.015f, 1.04f, 8);
            if (damaged)
                mesh.Tube(new[] { P(-0.074f, -0.021f, 0.007f), P(-0.051f, -0.044f, 0.030f),
                    P(0.007f, -0.041f, 0.032f) }, 0.014f, 0.50f);
            else
                mesh.Tube(new[] { P(-0.076f, 0.006f, 0.027f), P(-0.086f, -0.017f, 0.020f),
                    P(-0.058f, -0.035f, 0.032f), P(0.071f, -0.027f, 0.033f),
                    P(0.08f, -0.048f, 0.019f), P(0.005f, -0.060f, 0.028f),
                    P(-0.025f, -0.067f, 0.020f) }, 0.015f, 0.87f, 8);
        }

        private static void Kidneys(AnatomyVisualMesh mesh, bool damaged)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float volume = damaged && side == 1 ? 0.60f : 1f;
                Lobe(mesh, P(side * 0.170f, 0.012f, 0.004f), P(0.039f, 0.052f, 0.026f) * volume,
                    z: side * 25f);
                Lobe(mesh, P(side * 0.146f, -0.014f, -0.001f), P(0.031f, 0.032f, 0.024f) * volume,
                    z: side * -26f);
                mesh.Tube(new[] { P(side * 0.141f, 0f, 0.012f), P(side * 0.108f, -0.018f, 0.001f),
                    P(side * 0.097f, -0.04f, -0.011f) }, 0.009f, 0.66f);
            }
        }

        private static AnatomyVisuals.Connection Branch(int from, int to, float radius)
        {
            return new AnatomyVisuals.Connection(from, to, radius);
        }

        private static Vector3 P(float x, float y, float z) => new(x, y, z);

        private static void Lobe(AnatomyVisualMesh mesh, Vector3 center, Vector3 radii,
            float y = 0f, float z = 0f, int sides = 10)
        {
            mesh.Lobe(center, radii, Quaternion.Euler(0f, y, z), sides, 6);
        }
    }
}
