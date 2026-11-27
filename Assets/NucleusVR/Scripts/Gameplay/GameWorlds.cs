using System;
using System.Collections.Generic;
using NucleusVR.AI;
using NucleusVR.Core;
using NucleusVR.Maps;
using UnityEngine;

namespace NucleusVR.Gameplay
{
    internal sealed partial class GameFlow
    {
        private sealed class GameWorld
        {
            public GameWorld(GameObject root) => Root = root;
            public GameObject Root { get; }
            public Vector3 Spawn;
            public TextMesh Readout;
            public readonly List<GameSite> Sites = new();
            public GameObject[] Companions = Array.Empty<GameObject>();

            public void Show(bool enabled)
            {
                Root.SetActive(enabled);
                foreach (GameObject companion in Companions)
                    companion.SetActive(enabled);
            }
        }

        private sealed class GameSite
        {
            public GameSite(ushort id, Collider shape) { Id = id; Shape = shape; }
            public ushort Id { get; }
            public Collider Shape { get; }
        }

        private GameWorld CreateWorld(string name, Vector3 position)
        {
            var item = new GameObject(name);
            item.transform.SetParent(transform, false);
            item.transform.position = position;
            return new GameWorld(item);
        }

        private void BuildHub(GameWorld world)
        {
            Transform panel = Panel(world, "Game modes", new Vector3(-4.16f, 0f, -4.67f), 1.38f);
            Pad(world, panel, Explore, "Normal Mode", new Vector3(0f, 1.42f, -0.1f), 1.13f);
            Pad(world, panel, Monster, "Be the Monster", new Vector3(0f, 1.14f, -0.1f), 1.13f);
            Pad(world, panel, Sandbox, "Physics Sandbox", new Vector3(0f, 0.86f, -0.1f), 1.13f);
            FacilityGeometry.Label(panel, "Grip a pad · Host selects mode", new Vector3(0f, 0.54f, -0.1f),
                0.083f, Color.white);
        }

        private void BuildBriefing(GameWorld world)
        {
            Transform root = world.Root.transform;
            world.Spawn = root.TransformPoint(new Vector3(0f, 0.06f, -0.1f));
            Room(world, 8f, 7f);
            Transform panel = Panel(world, "Hunt briefing", new Vector3(0f, 0f, 1.75f), 3.04f);
            Pad(world, panel, VoteBackrooms, "Backrooms", new Vector3(-0.96f, 1.3f, -0.1f), 0.91f);
            Pad(world, panel, VoteFactory, "Factory", new Vector3(0f, 1.3f, -0.1f), 0.91f);
            Pad(world, panel, VoteMines, "Mines", new Vector3(0.96f, 1.3f, -0.1f), 0.91f);
            Pad(world, panel, StartRound, "Start voting", new Vector3(-0.72f, 0.94f, -0.1f), 1.15f);
            Pad(world, panel, Return, "Return to hub", new Vector3(0.72f, 0.94f, -0.1f), 1.15f);
            Pad(world, panel, LeaveRoom, "Leave online room", new Vector3(0f, 0.64f, -0.1f), 1.28f);
            world.Readout = FacilityGeometry.Label(root, "", new Vector3(0f, 2.02f, 3.30f), 0.11f, Color.white);
            FacilityGeometry.Label(root, "Survivors activate three stations and escape.\nThe monster hunts using hand strikes and two-handed grips.",
                new Vector3(0f, 2.43f, 3.30f), 0.19f, Color.white);
        }

        private void Room(GameWorld world, float width, float length)
        {
            Transform root = world.Root.transform;
            Surface(root, "Floor", new Vector3(0f, -0.16f, 0f), new Vector3(width, 0.32f, length));
            Surface(root, "Ceiling", new Vector3(0f, 3.06f, 0f), new Vector3(width, 0.23f, length));
            foreach (int side in new[] { -1, 1 })
            {
                Surface(root, "Side wall", new Vector3(side * width / 2f, 1.5f, 0f),
                    new Vector3(0.23f, 3f, length));
                Surface(root, "End wall", new Vector3(0f, 1.5f, side * length / 2f),
                    new Vector3(width, 3f, 0.25f));
            }
            FacilityGeometry.Block(root, "Luminous strip", new Vector3(0f, 2.88f, 0f),
                new Vector3(0.09f, 0.05f, length * 0.8f), control, CalibrationLayers.Course, false);
            var light = new GameObject("Briefing light").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = new Vector3(0f, 2.66f, 0f);
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            light.color = new Color32(173, 218, 223, 255);
        }

        private GameObject Surface(Transform parent, string name, Vector3 point, Vector3 scale) =>
            FacilityGeometry.Block(parent, name, point, scale, lit, CalibrationLayers.Course);

        private Transform Panel(GameWorld world, string title, Vector3 position, float width)
        {
            var panel = new GameObject(title).transform;
            panel.SetParent(world.Root.transform, false);
            panel.localPosition = position;
            Surface(panel, "Control backing", new Vector3(0f, 1.05f, 0f), new Vector3(width, 1.58f, 0.14f));
            FacilityGeometry.Label(panel, title, new Vector3(0f, 1.67f, -0.083f), 0.17f, control.color);
            return panel;
        }

        private void Pad(GameWorld world, Transform panel, ushort id, string caption, Vector3 at, float width)
        {
            GameObject button = FacilityGeometry.Block(panel, caption, at,
                new Vector3(width, 0.21f, 0.08f), control, CalibrationLayers.Course, false);
            FacilityGeometry.Label(panel, caption, at + new Vector3(0f, 0f, -0.048f),
                0.095f, new Color32(14, 27, 29, 255));
            AddSite(world, id, button.transform);
        }

        private void AddSite(GameWorld world, ushort id, Transform anchor)
        {
            var marker = new GameObject("Interaction sensor " + id);
            marker.layer = CalibrationLayers.Player;
            marker.transform.SetParent(anchor, false);
            var collider = marker.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            Vector3 scale = anchor.lossyScale;
            float width = Mathf.Clamp(Mathf.Abs(scale.x), 0.33f, 1.17f);
            collider.size = new Vector3(width / Mathf.Max(0.01f, Mathf.Abs(scale.x)),
                0.20f / Mathf.Max(0.01f, Mathf.Abs(scale.y)),
                0.17f / Mathf.Max(0.01f, Mathf.Abs(scale.z)));
            world.Sites.Add(new GameSite(id, collider));
        }

        private void BuildExitBoard(GameWorld world, string title)
        {
            Vector3 position = world.Root.transform.InverseTransformPoint(world.Spawn) +
                               new Vector3(1.45f, -0.06f, 0.9f);
            Transform panel = Panel(world, title, position, 1.6f);
            Pad(world, panel, Return, "Return to hub", new Vector3(0f, 1.29f, -0.1f), 1.3f);
            Pad(world, panel, RefreshSpawn, "Reset position", new Vector3(0f, 1.03f, -0.1f), 1.3f);
            Pad(world, panel, LeaveRoom, "Leave online room", new Vector3(0f, 0.77f, -0.1f), 1.3f);
            world.Readout = FacilityGeometry.Label(panel, "", new Vector3(0f, 0.47f, -0.10f), 0.072f, Color.white);
        }

        private void BuildForCurrentMode(WorldMode mode, MonsterRoundPhase phase, MonsterMap map)
        {
            GameWorld primary;
            GameWorld secondary = null;
            switch (mode)
            {
                case WorldMode.Facility:
                    if (facilityWorld == null)
                    {
                        facilityWorld = CreateWorld("Nucleus research facility", new Vector3(72f, 0f, 0f));
                        facility = facilityWorld.Root.AddComponent<ResearchFacility>();
                        facility.Build(lit, signal, (id, target) => AddSite(facilityWorld, id, target));
                        facilityWorld.Spawn = facility.Spawn;
                        monsters = facilityWorld.Root.AddComponent<FacilityMonsters>();
                        monsters.Build(facility.PatrolNodes, facility.PatrolLinks, lit, signal, FacilityAttack);
                        BuildExitBoard(facilityWorld, "Research station");
                    }
                    primary = facilityWorld;
                    break;
                case WorldMode.Sandbox:
                    if (workshopWorld == null)
                    {
                        workshopWorld = CreateWorld("Physics sandbox", new Vector3(0f, 0f, 75f));
                        workshop = workshopWorld.Root.AddComponent<SandboxWorkshop>();
                        workshop.Build(lit, signal, session, (id, target) => AddSite(workshopWorld, id, target));
                        workshopWorld.Spawn = workshop.Spawn;
                        BuildExitBoard(workshopWorld, "Workshop access");
                    }
                    primary = workshopWorld;
                    break;
                case WorldMode.Monster:
                    primary = briefing;
                    if (phase == MonsterRoundPhase.Active)
                    {
                        int index = (int)map;
                        if (arenas[index] == null)
                        {
                            var arena = CreateWorld(map.ToString(), new Vector3(152f + index * 68f, 0f, 0f));
                            maps[index] = arena.Root.AddComponent<MonsterMaps>();
                            maps[index].Build(map, lit, signal, (id, target) => AddSite(arena, id, target));
                            arena.Spawn = maps[index].SurvivorSpawn;
                            BuildArenaExit(arena);
                            arenas[index] = arena;
                        }
                        secondary = arenas[index];
                    }
                    break;
                default:
                    primary = hub;
                    break;
            }
            SwitchActiveWorlds(primary, secondary);
        }

        private void SwitchActiveWorlds(GameWorld primary, GameWorld secondary)
        {
            int expected = secondary == null ? 1 : 2;
            if (activeWorlds.Count == expected && activeWorlds[0] == primary &&
                (expected == 1 || activeWorlds[1] == secondary))
                return;

            equipment.ReleaseAll();
            anatomyHands?.ReleaseAll();
            foreach (GameWorld world in activeWorlds)
            {
                if (online && Authority)
                    foreach (GameSite site in world.Sites)
                        session.UnregisterGameplaySite(site.Id);
                if (world != primary && world != secondary)
                    world.Show(false);
            }

            activeWorlds.Clear();
            ShowWorld(primary);
            if (secondary != null)
                ShowWorld(secondary);
            if (online && Authority)
                RegisterActiveSites();
        }

        private void ShowWorld(GameWorld world)
        {
            world.Show(true);
            activeWorlds.Add(world);
        }

        private void BuildArenaExit(GameWorld arena)
        {
            Vector3 origin = arena.Root.transform.InverseTransformPoint(arena.Spawn);
            var panel = new GameObject("Hunt room control").transform;
            panel.SetParent(arena.Root.transform, false);
            panel.localPosition = origin + new Vector3(0.75f, 0f, -0.88f);
            panel.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Pad(arena, panel, LeaveHunt, "Host: end hunt", new Vector3(0f, 1.12f, -0.07f), 0.86f);
            Pad(arena, panel, LeaveArenaRoom, "Leave room", new Vector3(0f, 0.87f, -0.07f), 0.86f);
        }

        private void RegisterActiveSites()
        {
            foreach (GameWorld world in activeWorlds)
                foreach (GameSite site in world.Sites)
                    session.RegisterGameplaySite(site.Id, site.Shape, Reach);
        }

        private Vector3 SpawnFor(WorldMode mode, MonsterRoundPhase phase, MonsterMap map, GamePlayer player)
        {
            Vector3 offset = player.Slot == 0 ? Vector3.zero :
                new Vector3((player.Slot % 3 - 1) * 0.44f, 0f, -(1 + player.Slot / 3) * 0.30f);
            if (mode == WorldMode.Facility)
                return facilityWorld.Spawn + offset;
            if (mode == WorldMode.Sandbox)
                return workshopWorld.Spawn + offset;
            if (mode == WorldMode.Calibration)
                return hub.Spawn + offset;
            if (phase != MonsterRoundPhase.Active)
                return briefing.Spawn + offset;
            MonsterMaps arena = maps[(int)map];
            return player.Role switch
            {
                MonsterRole.Monster => arena.MonsterSpawn,
                MonsterRole.Survivor => arena.SurvivorSpawn + offset,
                _ => briefing.Spawn + offset
            };
        }
    }
}
