using UnityEngine;

namespace NucleusVR.Maps
{
    internal sealed partial class ResearchFacility
    {
        private void BuildLaboratory(Zone zone)
        {
            Sign(zone, "NUCLEUS RESEARCH FACILITY", 0f, 3.43f, -0.95f, 0.29f, mint.color);
            Sign(zone, "MAIN LABORATORY", 0f, 3.02f, -0.95f, 0.18f, Color.white);
            Sign(zone, "West  /  Maintenance      East  /  Containment      North  /  Caverns",
                0f, 2.64f, -0.95f, 0.12f, Color.white);
            BuildBlock(zone, "Observation station casing", -4.70f, 1.58f, 6.72f, 4.6f, 2.52f, 0.29f, trim);
            BuildBlock(zone, "Frosted observation glass", -4.70f, 1.72f, 6.55f, 4.3f, 1.91f, 0.08f, glass, false);
            BuildBlock(zone, "Observation pane divider", -4.69f, 1.73f, 6.49f, 0.13f, 1.94f, 0.06f, cladding, false);
            BuildBlock(zone, "Analysis workbench", -4.9f, 0.55f, 5.52f, 3.18f, 1.10f, 0.63f, cladding);
            for (int index = 0; index < 3; index++)
            {
                float x = -5.88f + index * 0.98f;
                BuildBlock(zone, "Analysis monitor", x, 1.35f, 5.62f, 0.60f, 0.40f, 0.18f, charcoal);
                BuildBlock(zone, "Monitor phosphor", x, 1.35f, 5.52f, 0.52f, 0.30f, 0.025f,
                    index == 1 ? amber : glass, false);
            }
            Sign(zone, "Specimen notes: three deposits, one signal.", -4.8f, 2.45f, 6.44f, 0.11f, Color.white);
            fragments[0] = Station(zone, 101, "Collect Core fragment", -4.82f, 0.99f, 4.32f, cap: gold).gameObject;

            BuildBlock(zone, "Terminal bank", 5.26f, 1.39f, 5.07f, 4.03f, 2.79f, 0.36f, charcoal);
            BuildBlock(zone, "Terminal inset", 5.26f, 1.83f, 4.86f, 3.55f, 1.08f, 0.048f, glass, false);
            Sign(zone, "NCRF ACCESS TERMINAL", 5.26f, 2.14f, 4.82f, 0.124f, mint.color);
            Sign(zone, "Follow the prompts to open the archive.", 5.26f, 1.70f, 4.81f, 0.096f, Color.white);
            Station(zone, 116, "HELP", 4.04f, 1.05f, 4.37f);
            Station(zone, 117, "STATUS", 5.24f, 1.05f, 4.37f);
            Station(zone, 118, "ARCHIVE", 6.44f, 1.05f, 4.37f);

            BuildBlock(zone, "Treatment bed", -5.12f, 0.56f, -4.58f, 1.34f, 1.12f, 3.12f, cladding);
            BuildBlock(zone, "Bed support rail", -5.86f, 1.05f, -4.58f, 0.14f, 0.25f, 3.1f, trim);
            BuildBlock(zone, "Bed support rail", -4.37f, 1.05f, -4.58f, 0.14f, 0.25f, 3.1f, trim);
            BuildBlock(zone, "Treatment curtain wall", -3.38f, 1.40f, -5.75f, 0.16f, 2.80f, 3.46f, trim);
            Station(zone, 120, "Return to calibration", 4.38f, 1.02f, -4.91f);
        }

        private void BuildMaintenance(Zone zone)
        {
            Sign(zone, "MAINTENANCE TUNNELS", 0f, 2.81f, 1.96f, 0.20f, mint.color);
            Sign(zone, "Sewer access north", -4.68f, 2.54f, 0f, 0.12f, Color.white);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int index = 0; index < 3; index++)
                {
                    BuildBlock(zone, "Steam pipe", side * (7.53f - index * 0.29f), 2.98f - index * 0.27f,
                        0f, 0.24f, 0.24f, 15.35f, pipe);
                }
                for (int support = -1; support <= 1; support++)
                {
                    if (side > 0 && support == 0)
                        continue;

                    BuildBlock(zone, "Pipe mounting post", side * 7.55f, 1.63f, support * 5.68f,
                        0.24f, 3.26f, 0.18f, trim);
                }
            }
            BuildBlock(zone, "Utility baffle", -2.92f, 1.42f, -5.14f, 0.35f, 2.84f, 5.65f, metal);
            BuildBlock(zone, "Utility baffle", 3.68f, 1.42f, -4.38f, 2.50f, 2.84f, 0.33f, metal);
            BuildBlock(zone, "Electrical trunk", -5.43f, 1.25f, -5.37f, 2.81f, 2.5f, 0.51f, charcoal);
            Circuit(zone, 104, 1, "B / Blue circuit", -5.39f, 1.01f, -4.42f, blue);
            Sign(zone, "Sewer amber → Maintenance blue → Containment green", -5.25f, 1.96f, -5.07f, 0.077f, Color.white);

            BuildBlock(zone, "Receiver rack", -4.62f, 1.19f, 5.73f, 2.48f, 2.38f, 0.41f, charcoal);
            BuildBlock(zone, "Signal level", -4.62f, 1.62f, 5.48f, 1.93f, 0.18f, 0.06f, amber, false);
            BuildBlock(zone, "Receiver aerial", -5.17f, 2.58f, 5.52f, 0.06f, 0.56f, 0.06f, pipe, false);
            Sign(zone, "Radio receiver / three channels", -4.62f, 1.97f, 5.43f, 0.104f, mint.color);
            Station(zone, 113, "Tune radio", -4.62f, 0.97f, 4.96f);

            BuildBlock(zone, "Pipe inspection niche", 5.87f, 0.47f, -6.74f, 1.88f, 0.95f, 0.34f, charcoal);
            BuildBlock(zone, "Inspection pipe", 6.34f, 0.78f, -6.34f, 0.42f, 0.46f, 1.02f, pipe);
            TinyCreature(zone, 5.70f, 0.78f, -6.20f);
            Station(zone, 112, "Inspect tiny creature", 5.56f, 0.66f, -5.95f, cap: blue);
        }

        private void TinyCreature(Zone zone, float x, float y, float z)
        {
            BuildBlock(zone, "Tiny creature crouched body", x, y, z, 0.29f, 0.18f, 0.31f, rock, false);
            GameObject head = BuildBlock(zone, "Tiny creature face", x, y + 0.123f, z - 0.075f,
                0.23f, 0.18f, 0.19f, water, false);
            head.transform.localRotation = Quaternion.Euler(0f, 0f, 9f);
            for (int side = -1; side <= 1; side += 2)
            {
                BuildBlock(zone, "Tiny eye", x + side * 0.060f, y + 0.128f, z - 0.18f,
                    0.025f, 0.022f, 0.009f, gold, false);
                BuildBlock(zone, "Tiny ear", x + side * 0.126f, y + 0.181f, z - 0.06f,
                    0.071f, 0.081f, 0.064f, water, false);
            }
        }

        private void BuildContainment(Zone zone)
        {
            Sign(zone, "CONTAINMENT WING", 0f, 2.84f, -3.47f, 0.22f, Color.white);
            BuildBlock(zone, "Security console body", -5.18f, 1.06f, 5.43f, 3.65f, 2.12f, 0.55f, charcoal);
            BuildBlock(zone, "Security console glass", -5.18f, 1.65f, 5.13f, 3.31f, 0.65f, 0.06f, glass, false);
            Sign(zone, "CONTAINMENT FEED / SIGNAL LOST", -5.18f, 1.68f, 5.09f, 0.11f, amber.color);
            Circuit(zone, 106, 2, "C / Green circuit", -5.15f, 0.99f, 4.66f, mint);

            BuildBlock(zone, "Failed holding cell", 5.15f, 0.57f, 6.68f, 4.55f, 1.14f, 1.69f, rock);
            for (int index = 0; index < 5; index++)
            {
                float x = 3.38f + index * 0.76f;
                float height = index is 1 or 2 ? 1.2f : 2.55f;
                GameObject bar = BuildBlock(zone, "Bent cell bar", x, height * 0.5f + 0.62f, 5.79f,
                    0.13f, height, 0.15f, trim);
                if (index is 1 or 2)
                    bar.transform.localRotation = Quaternion.Euler(0f, 0f, index == 1 ? -24f : 17f);
            }
            Sign(zone, "HOLDING CELL 4 / EMPTY", 5.1f, 3.44f, 5.71f, 0.12f, amber.color);

            room404Door = Gate(zone, "Signal archive", 1, -1);
            BuildBlock(zone, "Room404 screen bank", 5.18f, 1.61f, -6.84f, 3.56f, 2.49f, 0.34f, charcoal);
            BuildBlock(zone, "Room404 inverse screen", 5.18f, 1.67f, -6.63f, 3.24f, 1.40f, 0.06f, glass, false);
            Sign(zone, "ROOM 404", 5.18f, 2.06f, -6.56f, 0.22f, blue.color, 180f);
            Sign(zone, "No record of this room exists.\nWhy do you remember it?", 5.18f, 1.62f, -6.55f, 0.12f, Color.white, 180f);
            Station(zone, 110, "Mark discovery", 4.46f, 1.05f, -4.55f, 180f);
            Station(zone, 111, "Read transcript", 5.89f, 1.05f, -5.66f, 180f);
        }

        private void BuildSewer(Zone zone)
        {
            const float dry = 3.84f;
            BuildBlock(zone, "Raised central channel", 0f, -0.18f, 0f, dry, 0.36f, 16f, charcoal);
            BuildBlock(zone, "Cross-channel bridge", -4.96f, -0.18f, 0f, 6.08f, 0.36f, dry, charcoal);
            BuildBlock(zone, "Cross-channel bridge", 4.96f, -0.18f, 0f, 6.08f, 0.36f, dry, charcoal);
            for (int xSign = -1; xSign <= 1; xSign += 2)
            for (int zSign = -1; zSign <= 1; zSign += 2)
            {
                float x = xSign * 4.96f;
                float z = zSign * 4.96f;
                BuildBlock(zone, "Drain basin floor", x, -0.63f, z, 6.08f, 0.36f, 6.08f, metal);
                BuildBlock(zone, "Standing water", x, -0.36f, z, 6.06f, 0.037f, 6.06f, water, false);
                for (int ripple = 0; ripple < 3; ripple++)
                    BuildBlock(zone, "Water reflection", x, -0.337f, z - 1.52f + ripple * 1.14f,
                        2.33f - ripple * 0.45f, 0.014f, 0.020f, glass, false);
            }

            Sign(zone, "SEWERS", 0f, 2.68f, -3.60f, 0.22f, mint.color);
            DrainBore(zone, -5.95f, 1.22f, 7.19f);
            DrainBore(zone, 5.95f, 1.22f, 7.19f);
            BuildBlock(zone, "Fragment access bridge", -3.91f, -0.115f, 4.10f, 4.10f, 0.23f, 1.88f, trim);
            BuildBlock(zone, "Circuit access bridge", 3.87f, -0.115f, -4.13f, 4.0f, 0.23f, 1.92f, trim);
            fragments[1] = Station(zone, 102, "Collect Core fragment", -5.37f, 0.92f, 4.10f, cap: gold).gameObject;
            Circuit(zone, 105, 0, "A / Amber circuit", 5.24f, 0.97f, -4.13f, amber);
            BuildBlock(zone, "Deposited drain debris", -6.71f, -0.21f, -5.98f, 1.83f, 0.48f, 1.61f, rock);
        }

        private void DrainBore(Zone zone, float x, float y, float z)
        {
            BuildBlock(zone, "Drain shadow", x, y, z + 0.27f, 2.42f, 2.12f, 0.15f, charcoal);
            const int segments = 10;
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / segments;
                GameObject rim = BuildBlock(zone, "Drain rim", x + 1.12f * Mathf.Cos(angle),
                    y + 1.02f * Mathf.Sin(angle), z, 0.29f, 0.73f, 0.56f, pipe);
                rim.transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
            }
        }

        private void BuildCaverns(Zone zone)
        {
            Sign(zone, "UNDERGROUND CAVERNS", 0f, 3.28f, -3.49f, 0.23f, amber.color);
            for (int side = -1; side <= 1; side += 2)
            for (int index = 0; index < 4; index++)
            {
                float x = side * (6.38f + (index % 2) * 0.43f);
                float z = side * (3.51f + index * 1.06f);
                float height = 1.78f + (index % 3) * 0.44f;
                GameObject boulder = BuildBlock(zone, "Cavern basalt formation", x, height * 0.40f, z,
                    1.80f, height, 1.43f, rock);
                boulder.transform.localRotation = Quaternion.Euler(3f + index * 4f, side * (14f + 11f * index),
                    side * (12f - index * 2f));
            }

            const float terraceY = 2.44f;
            BuildBlock(zone, "Cavern terrace", -6.62f, terraceY - 0.19f, 5.12f, 2.42f, 0.38f, 3.05f, rock);
            for (int index = 0; index < 6; index++)
            {
                float top = 0.39f * (index + 1);
                float x = -2.49f - index * 0.62f;
                BuildBlock(zone, "Climbable basalt ledge", x, top * 0.5f, 5.12f,
                    0.63f, top, 2.05f, rock);
                BuildBlock(zone, "Climb guide", x + 0.25f, top + 0.018f, 5.12f,
                    0.038f, 0.032f, 1.57f, amber, false);
            }
            Sign(zone, "Grip ledges to reach the upper deposit", -3.59f, 2.35f, 3.74f, 0.101f, Color.white);
            fragments[2] = Station(zone, 103, "Collect Core fragment", -6.59f, terraceY + 0.88f, 5.18f,
                baseY: terraceY, cap: gold).gameObject;
            for (int index = 0; index < 4; index++)
            {
                float x = -7.45f + index * 0.27f;
                GameObject shard = BuildBlock(zone, "Mineral deposit", x, 2.52f + index * 0.025f, 5.68f,
                    0.13f, 0.37f, 0.14f, blue, false);
                shard.transform.localRotation = Quaternion.Euler(15f, index * 12f, -15f + index * 10f);
            }
        }

        private void BuildSecretSector(Zone zone)
        {
            Sign(zone, "SECRET RESEARCH SECTOR", 0f, 2.85f, -3.48f, 0.20f, mint.color);
            coreDoor = Gate(zone, "Core vault", -1, -1);
            developerDoor = Gate(zone, "Development archive", 1, -1);
            bossDoor = Gate(zone, "Sealed experiment", 1, 1);
            Sign(zone, "Three fragments and three aligned circuits", -5.10f, 2.32f, -2.20f,
                0.090f, amber.color, 180f);

            core = Station(zone, 107, "Collect Golden Core", -5.10f, 1.11f, -5.22f, 180f, cap: gold).gameObject;
            BuildBlock(zone, "Core seal frame", -5.10f, 1.86f, -6.70f, 3f, 0.24f, 0.35f, trim);
            BuildBlock(zone, "Core seal post", -6.51f, 0.93f, -6.70f, 0.18f, 1.86f, 0.36f, trim);
            BuildBlock(zone, "Core seal post", -3.69f, 0.93f, -6.70f, 0.18f, 1.86f, 0.36f, trim);

            BuildBlock(zone, "Developer workbench", 5.10f, 0.59f, -6.89f, 3.18f, 1.18f, 0.62f, charcoal);
            Sign(zone, "NUCLEUS VR CONTRIBUTORS", 5.07f, 2.56f, -7.27f, 0.142f, mint.color, 180f);
            Sign(zone, "Our first bug report escaped.", 5.08f, 2.19f, -7.26f, 0.104f, Color.white, 180f);
            for (int index = 0; index < 4; index++)
            {
                GameObject memento = BuildBlock(zone, "Desk memento", 4.28f + index * 0.38f, 1.34f, -6.73f,
                    0.19f, 0.29f, 0.19f, index == 2 ? gold : cladding, false);
                memento.transform.localRotation = Quaternion.Euler(0f, index * 29f, -13f + index * 9f);
            }
            Station(zone, 114, "Record archive discovery", 5.10f, 1.03f, -4.81f, 180f);

            Station(zone, 115, "Release experiment seal", 6.53f, 1.11f, 1.89f, cap: red);
            Sign(zone, "TRANSCRIPT + ARCHIVE ACCESS REQUIRED", 5.09f, 3.22f, 2.23f, 0.087f, amber.color);
            BuildBlock(zone, "Experiment restraint", 5.19f, 0.32f, 5.28f, 2.2f, 0.64f, 1.96f, rock);
            for (int side = -1; side <= 1; side += 2)
                BuildBlock(zone, "Broken restraint support", 5.19f + side * 1.06f, 1.19f, 5.66f,
                    0.18f, 1.78f, 0.23f, pipe);

            BuildBlock(zone, "Forgotten holding plinth", -5.10f, 0.63f, 6.69f, 2.57f, 1.26f, 0.86f, charcoal);
            BuildBlock(zone, "Examination vial", -5.09f, 1.80f, 6.60f, 0.64f, 1.10f, 0.64f, glass);
            BuildBlock(zone, "Examination vial collar", -5.09f, 2.37f, 6.60f, 0.76f, 0.17f, 0.73f, metal);
            Sign(zone, "H-07 / THE FORGOTTEN EXPERIMENT", -5.10f, 2.69f, 6.20f, 0.12f, blue.color);
            Sign(zone, "It remembered songs no one taught it.", -5.10f, 1.77f, 6.24f, 0.097f, Color.white);
            Station(zone, 119, "Inspect specimen H-07", -5.10f, 1.10f, 5.65f);
        }

        private void Circuit(Zone zone, ushort id, int progressIndex, string title, float x, float y, float z, Material tint)
        {
            Station(zone, id, title, x, y, z, cap: tint);
            GameObject indicator = BuildBlock(zone, "Circuit completion light", x + 0.42f, y + 0.02f,
                z - 0.08f, 0.12f, 0.16f, 0.12f, dark, false);
            circuitIndicators[progressIndex] = indicator.GetComponent<MeshRenderer>();
        }
    }
}
