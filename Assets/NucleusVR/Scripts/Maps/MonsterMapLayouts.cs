using UnityEngine;

namespace NucleusVR.Maps
{
    internal sealed partial class MonsterMaps
    {
        // Each line reads west to east. Rows advance toward +Z from the entrance.
        private static readonly string[] MazeEast =
        {
            "11011", "01111", "10101", "11010", "01101", "01110"
        };

        private static readonly string[] MazeNorth =
        {
            "101101", "110110", "011101", "101111", "111001"
        };

        private const float MazeCell = 4.4f;
        private const float MazeWidth = MazeCell * 6f;

        private void BuildBackrooms(Material lit, Material signal)
        {
            Material carpet = Finish(lit, new Color32(111, 92, 54, 255), "Old carpet");
            Material stained = Finish(lit, new Color32(85, 73, 49, 255), "Carpet wear");
            Material wallpaper = Finish(lit, new Color32(187, 160, 95, 255), "Ochre wallpaper");
            Material trim = Finish(lit, new Color32(139, 116, 65, 255), "Wall pattern");
            Material tile = Finish(lit, new Color32(198, 185, 145, 255), "Acoustic ceiling");
            Material tube = Finish(signal, new Color32(250, 239, 171, 255), "Warm fluorescent");

            Ground("Carpet", MazeWidth, 0f, MazeWidth, carpet);
            Solid("Ceiling", new Vector3(0f, 3.25f, MazeWidth * 0.5f),
                new Vector3(MazeWidth, 0.22f, MazeWidth), tile, castsShadow: false);
            Perimeter(MazeWidth, 0f, MazeWidth, 3.27f, wallpaper);

            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 6; col++)
                {
                    Vector3 cell = MazeCenter(col, row);
                    Solid("Recessed fluorescent", cell + new Vector3(0f, 3.075f, 0.04f),
                        new Vector3(1.35f, 0.08f, 0.24f), tube, false, castsShadow: false);
                    float offset = ((col * 5 + row * 3) % 7 - 3) * 0.29f;
                    Solid("Carpet discolouration", cell + new Vector3(offset, 0.003f, offset * 0.64f),
                        new Vector3(0.46f + (col % 3) * 0.24f, 0.004f, 0.92f), stained, false,
                        Quaternion.Euler(0f, (col * 13 + row * 31) % 180, 0f), false);

                    if (col < 5 && MazeEast[row][col] == '0')
                    {
                        Vector3 boundary = cell + Vector3.right * (MazeCell * 0.5f);
                        if (row == 2 && col == 1)
                            CrawlSpace(boundary, wallpaper, trim);
                        else
                            MazeWall(boundary, true, wallpaper, trim);
                    }
                    if (row < 5 && MazeNorth[row][col] == '0')
                        MazeWall(cell + Vector3.forward * (MazeCell * 0.5f), false, wallpaper, trim);
                }
            }

            Vector3 entrance = MazeCenter(2, 0);
            Spawn(entrance + Vector3.up * 0.04f, MazeCenter(5, 4) + Vector3.up * 0.04f);
            Caption("RING A • ELECTRICAL", entrance + new Vector3(0f, 2.26f, 1.45f), 0.23f);
            Objective(201, "01  RECEPTION PANEL", MazeCenter(0, 1) + new Vector3(-1.25f, 1.22f, 1.28f));
            Objective(202, "02  EXCHANGE PANEL", MazeCenter(4, 3) + new Vector3(1.25f, 1.22f, 1.28f));
            Objective(203, "03  SERVICE PANEL", MazeCenter(1, 5) + new Vector3(-1.25f, 1.22f, 1.28f));
            Vector3 west = MazeCenter(0, 5);
            Vector3 east = MazeCenter(5, 5);
            EscapeBay(204, "MAINTENANCE OUTLET", west.x, west.z - 0.48f, wallpaper, 1.87f);
            EscapeBay(205, "EMERGENCY DOOR", east.x, east.z - 0.48f, wallpaper, 1.87f);
            Alarm(MazeCenter(4, 3) + new Vector3(1.09f, 2.8f, 1.43f), new Vector3(0.32f, 0.11f, 0.13f));
        }

        private static Vector3 MazeCenter(int col, int row) =>
            new((col - 2.5f) * MazeCell, 0f, (row + 0.5f) * MazeCell);

        private void MazeWall(Vector3 center, bool alongZ, Material wallpaper, Material trim)
        {
            Vector3 wallSize = alongZ ? new Vector3(0.17f, 3.29f, MazeCell + 0.04f) :
                new Vector3(MazeCell + 0.04f, 3.29f, 0.17f);
            Solid("Partition", center + Vector3.up * 1.64f, wallSize, wallpaper);
            Vector3 band = alongZ ? new Vector3(0.183f, 0.11f, MazeCell) :
                new Vector3(MazeCell, 0.11f, 0.183f);
            Solid("Wallpaper border", center + Vector3.up * 1.02f, band, trim, false, castsShadow: false);
            Solid("Baseboard", center + Vector3.up * 0.12f, band, trim, false, castsShadow: false);
        }

        private void CrawlSpace(Vector3 center, Material wallpaper, Material trim)
        {
            Solid("Concealed hatch lintel", center + Vector3.up * 2.26f,
                new Vector3(0.22f, 2.07f, MazeCell + 0.04f), wallpaper);
            for (int side = -1; side <= 1; side += 2)
                Solid("Concealed hatch side", center + new Vector3(0f, 0.61f, side * 1.47f),
                    new Vector3(0.22f, 1.23f, 1.49f), wallpaper);
            Solid("Hatch threshold", center + Vector3.up * 0.018f,
                new Vector3(0.31f, 0.035f, 1.45f), trim, false, castsShadow: false);
        }

        private void BuildFactory(Material lit, Material signal)
        {
            Material floor = Finish(lit, new Color32(76, 79, 81, 255), "Concrete");
            Material siding = Finish(lit, new Color32(51, 75, 82, 255), "Machinery casing");
            Material deck = Finish(lit, new Color32(111, 124, 126, 255), "Galvanized platform");
            Material hazard = Finish(signal, new Color32(224, 154, 51, 255), "Hazard paint");
            Material furnace = Finish(lit, new Color32(125, 63, 42, 255), "Boiler shield");

            Ground("Factory slab", 29f, -3f, 29f, floor);
            Perimeter(29f, -3f, 29f, 6.8f, siding);
            Solid("Factory roof", new Vector3(0f, 6.89f, 13f), new Vector3(29.2f, 0.22f, 32.2f), siding,
                castsShadow: false);
            Spawn(new Vector3(0f, 0.04f, -0.25f), new Vector3(-10.6f, 0.04f, 22.6f));
            Caption("BLAST FURNACE • EVACUATION", new Vector3(0f, 2.35f, 2.9f), 0.27f);

            for (int bay = 0; bay < 3; bay++)
            {
                float z = 5.1f + bay * 7.2f;
                Solid("West machine bank", new Vector3(-11.9f, 0.97f, z),
                    new Vector3(2.55f, 1.94f, 3.45f), siding);
                Solid("Motor cover", new Vector3(-11.7f, 2.18f, z),
                    new Vector3(1.85f, 0.48f, 1.68f), steel);
                Solid("Machine louver", new Vector3(-11.9f, 1.32f, z - 1.74f),
                    new Vector3(1.56f, 0.65f, 0.065f), furnace, false, castsShadow: false);
                Alarm(new Vector3(-10.9f, 1.63f, z - 1.79f), new Vector3(0.18f, 0.16f, 0.05f));
            }

            Solid("Transfer conveyor", new Vector3(-4.0f, 0.34f, 13.2f),
                new Vector3(2.8f, 0.68f, 7.0f), siding);
            for (int index = 0; index < 16; index++)
                Solid("Conveyor cleat", new Vector3(-4.0f, 0.696f, 10.2f + 0.38f * index),
                    new Vector3(2.55f, 0.025f, 0.085f), deck, false, castsShadow: false);

            Solid("Boiler housing", new Vector3(10.9f, 1.4f, 15.8f), new Vector3(4.2f, 2.8f, 3.35f), furnace);
            Solid("Boiler stack", new Vector3(11.2f, 3.85f, 16.0f), new Vector3(1.15f, 2.1f, 1.2f), steel);
            Solid("Pressure pipe", new Vector3(11.4f, 2.25f, 13.9f),
                new Vector3(3.55f, 0.21f, 0.28f), deck, false, castsShadow: false);
            Alarm(new Vector3(10.85f, 2.31f, 14.08f), new Vector3(0.28f, 0.09f, 0.09f));

            Solid("Evacuation catwalk", new Vector3(4.45f, 2.54f, 17.6f),
                new Vector3(4.95f, 0.28f, 8.0f), deck);
            Ramp("Catwalk access incline", 4.45f, 7.25f, 13.72f, 0.02f, 2.68f, 3.75f, deck);
            Ramp("Catwalk descent", 4.45f, 21.48f, 26.5f, 2.68f, 0.02f, 3.75f, deck);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = 4.45f + side * 2.36f;
                Solid("Catwalk toe guard", new Vector3(x, 2.91f, 17.6f),
                    new Vector3(0.16f, 0.43f, 7.8f), hazard);
                Solid("Catwalk crossbrace", new Vector3(x, 1.25f, 18.4f),
                    new Vector3(0.23f, 2.58f, 0.24f), steel);
            }
            Solid("Overhead gantry", new Vector3(0f, 5.75f, 17.7f), new Vector3(25.0f, 0.46f, 0.55f), hazard);
            Solid("Crane trolley", new Vector3(-6.8f, 5.22f, 17.7f), new Vector3(1.7f, 0.67f, 1.19f), furnace);
            Solid("Crane line", new Vector3(-6.8f, 4.17f, 17.7f), new Vector3(0.12f, 1.45f, 0.12f),
                steel, false, castsShadow: false);
            Solid("Hanging grapple", new Vector3(-6.8f, 3.37f, 17.7f), new Vector3(0.57f, 0.34f, 0.46f), hazard);

            Objective(201, "01  RESTORE BUS", new Vector3(-8.0f, 1.22f, 6.9f));
            Objective(202, "02  RESET PRESSURE", new Vector3(10.25f, 1.22f, 12.85f));
            Objective(203, "03  ARM EVACUATION", new Vector3(4.45f, 3.9f, 19.6f));
            EscapeBay(204, "LOADING DOCK", 10.8f, 24.7f, siding, 2.85f);
        }

        private void BuildMines(Material lit, Material signal)
        {
            Material rock = Finish(lit, new Color32(69, 63, 60, 255), "Cut bedrock");
            Material gravel = Finish(lit, new Color32(107, 94, 82, 255), "Gravel");
            Material timber = Finish(lit, new Color32(115, 83, 59, 255), "Mine supports");
            Material rail = Finish(lit, new Color32(138, 153, 157, 255), "Running rails");
            Material water = Finish(lit, new Color32(49, 88, 83, 255), "Flooded limestone");
            Material marker = Finish(signal, new Color32(100, 212, 178, 255), "Mine safety lamps");

            Ground("Mine passage floor", 27.2f, -3f, 30f, gravel);
            Perimeter(27.2f, -3f, 30f, 6.5f, rock);
            Solid("Cavern roof", new Vector3(0, 6.25f, 13.5f), new Vector3(27.1f, 0.56f, 33.0f), rock,
                castsShadow: false);
            Spawn(new Vector3(0f, 0.04f, -0.25f), new Vector3(6.25f, 0.04f, 21.2f));
            Caption("SUBLEVEL EXTRACTION", new Vector3(0f, 2.34f, 2.7f), 0.26f);

            for (int index = 0; index < 6; index++)
            {
                float z = 1.8f + 4.45f * index;
                for (int side = -1; side <= 1; side += 2)
                {
                    Solid("Timber upright", new Vector3(side * 2.2f, 1.7f, z),
                        new Vector3(0.29f, 3.42f, 0.35f), timber);
                    float x = side * (10.9f + 0.29f * (index % 3));
                    Solid("Fractured rock face", new Vector3(x, 2.04f, z),
                        new Vector3(4.45f, 4.34f, 3.08f), rock, true,
                        Quaternion.Euler(0f, side * (21f + index * 17f), side * 10f));
                }
                Solid("Timber overhead support", new Vector3(0f, 3.28f, z),
                    new Vector3(4.68f, 0.26f, 0.4f), timber);
                Solid("Support lamp", new Vector3(0f, 3.09f, z),
                    new Vector3(0.32f, 0.14f, 0.22f), marker, false, castsShadow: false);
            }

            for (int side = -1; side <= 1; side += 2)
                Solid("Lift rail", new Vector3(side * 0.49f, 0.041f, 12.8f),
                    new Vector3(0.075f, 0.078f, 28.2f), rail, false, castsShadow: false);
            for (int index = 0; index < 42; index++)
                Solid("Track sleeper", new Vector3(0f, 0.021f, -0.8f + index * 0.64f),
                    new Vector3(1.54f, 0.033f, 0.118f), timber, false, castsShadow: false);

            Solid("Maintenance minecart", new Vector3(-4.9f, 0.48f, 6.8f),
                new Vector3(1.54f, 0.95f, 2.2f), steel);
            Solid("Cart cargo", new Vector3(-4.9f, 0.995f, 6.8f),
                new Vector3(1.32f, 0.2f, 1.99f), rock, false);
            Solid("Shallow mineral pool", new Vector3(-7.5f, 0.012f, 12.7f),
                new Vector3(4.35f, 0.022f, 5.35f), water, false, castsShadow: false);
            Solid("Flood marker", new Vector3(-6.7f, 0.034f, 12.7f),
                new Vector3(0.29f, 0.026f, 4.7f), marker, false, castsShadow: false);

            Solid("Lift service deck", new Vector3(-4.95f, 0.96f, 19.65f),
                new Vector3(3.85f, 0.35f, 3.88f), timber);
            Ramp("Lift service incline", -4.95f, 13.95f, 17.82f, 0.02f, 1.135f, 3.85f, timber);
            Solid("Contactor back screen", new Vector3(-4.95f, 2.12f, 21.5f),
                new Vector3(3.85f, 1.97f, 0.21f), rock);
            Solid("Shaft hoist head", new Vector3(0f, 4.05f, 25.0f),
                new Vector3(3.3f, 0.48f, 0.78f), steel);
            for (int side = -1; side <= 1; side += 2)
                Solid("Vertical lift guide", new Vector3(side * 0.98f, 4.54f, 25.5f),
                    new Vector3(0.16f, 2.3f, 0.16f), rail);
            Alarm(new Vector3(-4.7f, 3.06f, 21.32f), new Vector3(0.27f, 0.10f, 0.09f));

            Objective(201, "01  FIT DRIVE FUSE", new Vector3(-6.52f, 1.23f, 7.7f));
            Objective(202, "02  RELEASE BRAKE", new Vector3(6.16f, 1.23f, 13.95f));
            Objective(203, "03  TEST CONTACTOR", new Vector3(-4.95f, 2.36f, 20.8f));
            EscapeBay(204, "EXTRACTION LIFT", 0f, 25.0f, steel, 3.2f);
            EscapeBay(205, "EMERGENCY RAISE", 6.75f, 24.55f, rock, 2.88f);
        }

        private void Ground(string label, float width, float minZ, float maxZ, Material floor) =>
            Solid(label, new Vector3(0f, -0.14f, (minZ + maxZ) * 0.5f),
                new Vector3(width + 0.44f, 0.28f, maxZ - minZ + 0.44f), floor, castsShadow: false);

        private void Perimeter(float width, float minZ, float maxZ, float height, Material wall)
        {
            float length = maxZ - minZ;
            for (int side = -1; side <= 1; side += 2)
            {
                Solid("Outer wall", new Vector3(side * width * 0.5f, height * 0.5f, (minZ + maxZ) * 0.5f),
                    new Vector3(0.22f, height, length + 0.22f), wall);
                Solid("End wall", new Vector3(0f, height * 0.5f, side == -1 ? minZ : maxZ),
                    new Vector3(width, height, 0.22f), wall);
            }
        }

        private void Ramp(string label, float x, float startZ, float endZ, float startY, float endY,
            float width, Material finish)
        {
            float run = endZ - startZ;
            float rise = endY - startY;
            float angle = -Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            Quaternion rotation = Quaternion.Euler(angle, 0f, 0f);
            Vector3 midpoint = new(x, (startY + endY) * 0.5f, (startZ + endZ) * 0.5f);
            Solid(label, midpoint - rotation * Vector3.up * 0.11f,
                new Vector3(width, 0.22f, Mathf.Sqrt(run * run + rise * rise) + 0.05f), finish, true, rotation);
        }
    }
}
