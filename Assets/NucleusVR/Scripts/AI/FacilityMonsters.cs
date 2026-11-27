using System;
using System.Collections.Generic;
using NucleusVR.Core;
using NucleusVR.Monsters;
using UnityEngine;

namespace NucleusVR.AI
{
    internal enum FacilityBehavior : byte
    {
        Dormant, Patrol, Investigate, Search, Chase, Windup, Recovery, Observe, Lure, Defeated
    }

    internal sealed class FacilityMonsters : MonoBehaviour
    {
        private const int Walls = 1 << CalibrationLayers.Course;
        private const float Step = 0.08f;
        private const int Experiment = 4;

        private readonly struct Species
        {
            public readonly string Name;
            public readonly int Home;
            public readonly float Height, Radius, Speed, Sight, Hearing, Health, Reach, Damage, Windup, Recovery;

            public Species(string name, int home, float height, float radius, float speed, float sight, float hearing,
                float health, float reach, float damage, float windup, float recovery)
            {
                Name = name;
                Home = home;
                Height = height;
                Radius = radius;
                Speed = speed;
                Sight = sight;
                Hearing = hearing;
                Health = health;
                Reach = reach;
                Damage = damage;
                Windup = windup;
                Recovery = recovery;
            }
        }

        private static readonly Species[] SpeciesData =
        {
            new("The Crawler", 3, 0.85f, 0.46f, 3.15f, 9f, 17f, 85f, 1.10f, 16f, 0.46f, 1.0f),
            new("The Watcher", 1, 2.24f, 0.34f, 1.90f, 14f, 7f, 85f, 1.20f, 12f, 0.82f, 1.2f),
            new("The Brute", 2, 2.05f, 0.76f, 1.82f, 10f, 11f, 150f, 1.72f, 32f, 0.95f, 1.65f),
            new("The Mimic", 4, 1.24f, 0.46f, 2.55f, 9f, 13f, 105f, 1.26f, 21f, 0.64f, 1.2f),
            new("The Nucleus Experiment", 5, 2.55f, 0.65f, 2.90f, 16f, 12f, 270f, 1.85f, 34f, 0.75f, 1.60f)
        };

        private sealed class Monster
        {
            public FacilityMonsterAppearance Visual;
            public Vector3 Position, Spawn, LastSight, Sound;
            public float Yaw, Health, Age, Cooldown, SoundLife, SoundScore;
            public int MoveNode, GoalNode, PreviousPatrol;
            public ulong Target;
            public FacilityBehavior State;
        }

        private readonly Monster[] creatures = new Monster[SpeciesData.Length];
        private FacilityPathGraph graph;
        private Action<ulong, int, float, Vector3> attack;
        private System.Random patrol;
        private Vector3 lowerBound, upperBound;
        private float budget;

        public int Count => graph == null ? 0 : creatures.Length;

        public void Build(Vector3[] nodes, (int From, int To)[] links, Material lit, Material signal,
            Action<ulong, int, float, Vector3> onAttack)
        {
            if (graph != null)
                return;
            if (nodes == null || nodes.Length < SpeciesData.Length + 1 || links == null)
                throw new ArgumentException("The facility requires all six room centers and their corridor links.");
            if (lit == null || signal == null)
                throw new ArgumentException("The facility requires shell and signal materials.");

            lowerBound = upperBound = nodes[0];
            foreach (Vector3 point in nodes)
            {
                if (!Finite(point))
                    throw new ArgumentException("Facility navigation requires finite positions.", nameof(nodes));
                lowerBound = Vector3.Min(lowerBound, point);
                upperBound = Vector3.Max(upperBound, point);
            }
            lowerBound -= new Vector3(8f, 1f, 8f);
            upperBound += new Vector3(8f, 1f, 8f);
            graph = new FacilityPathGraph(nodes, links);
            attack = onAttack;
            for (int i = 0; i < creatures.Length; i++)
            {
                Species info = SpeciesData[i];
                var root = new GameObject(info.Name);
                root.transform.SetParent(transform, false);
                var visual = root.AddComponent<FacilityMonsterAppearance>();
                visual.Build(i, lit, signal);
                creatures[i] = new Monster { Visual = visual, Spawn = graph.Point(info.Home) };
            }
            ResetEncounter();
        }

        public void ResetEncounter()
        {
            if (graph == null)
                return;
            budget = 0f;
            patrol = new System.Random(3061);
            for (int i = 0; i < creatures.Length; i++)
            {
                Monster monster = creatures[i];
                monster.Position = monster.Spawn;
                monster.LastSight = monster.Sound = monster.Spawn;
                monster.Yaw = 90f * (i % 4);
                monster.Health = SpeciesData[i].Health;
                monster.Age = monster.SoundLife = monster.SoundScore = 0f;
                monster.Cooldown = 1f + i * 0.7f;
                monster.MoveNode = monster.GoalNode = monster.PreviousPatrol = -1;
                monster.Target = 0;
                monster.State = i == Experiment ? FacilityBehavior.Dormant : FacilityBehavior.Patrol;
                monster.Visual.ResetPose(monster.Position, monster.Yaw, monster.State);
            }
        }

        public Vector3 PositionAt(int index) => creatures[index].Position;
        public float YawAt(int index) => creatures[index].Yaw;
        public byte StateAt(int index) => (byte)creatures[index].State;

        public void ApplyPeerPose(int index, Vector3 position, float yaw, byte state)
        {
            if ((uint)index >= (uint)Count || state > (byte)FacilityBehavior.Defeated || !Finite(position) ||
                !float.IsFinite(yaw) || position.x < lowerBound.x || position.x > upperBound.x ||
                position.y < lowerBound.y || position.y > upperBound.y ||
                position.z < lowerBound.z || position.z > upperBound.z)
                return;
            Monster monster = creatures[index];
            monster.Position = position;
            monster.Yaw = Mathf.Repeat(yaw, 360f);
            Change(monster, (FacilityBehavior)state);
        }

        public bool IsDefeated(int index) =>
            (uint)index < (uint)Count && creatures[index].State == FacilityBehavior.Defeated;

        public int TryStrike(Vector3 point, float radius, float damage)
        {
            if (!Finite(point) || !float.IsFinite(radius) || !float.IsFinite(damage) || radius <= 0f || damage <= 0f)
                return -1;
            int hit = -1;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < Count; i++)
            {
                Monster monster = creatures[i];
                if (monster.State is FacilityBehavior.Dormant or FacilityBehavior.Defeated)
                    continue;
                Species species = SpeciesData[i];
                Vector3 center = monster.Position;
                center.y = Mathf.Clamp(point.y, center.y + species.Radius,
                    center.y + Mathf.Max(species.Radius, species.Height - species.Radius * 0.55f));
                float reach = species.Radius + Mathf.Min(radius, 0.5f);
                float distance = (center - point).sqrMagnitude;
                if (distance > reach * reach || distance >= closest || Blocked(point, center))
                    continue;
                closest = distance;
                hit = i;
            }

            if (hit >= 0)
            {
                Monster monster = creatures[hit];
                monster.Health = Mathf.Max(0f, monster.Health - Mathf.Min(40f, damage));
                if (monster.Health <= 0f)
                    Change(monster, FacilityBehavior.Defeated);
                else
                    Hear(point, 0.72f);
            }
            return hit;
        }

        public void Hear(Vector3 point, float strength)
        {
            if (!Finite(point) || !float.IsFinite(strength) || strength <= 0f)
                return;
            strength = Mathf.Clamp01(strength);
            for (int i = 0; i < Count; i++)
            {
                Monster monster = creatures[i];
                if (monster.State is FacilityBehavior.Dormant or FacilityBehavior.Defeated)
                    continue;
                float radius = SpeciesData[i].Hearing * Mathf.Lerp(0.24f, 1f, strength);
                float distance = Vector3.Distance(monster.Position, point);
                if (distance > radius)
                    continue;
                float score = strength * (1f - distance / radius);
                if (monster.SoundLife > 0f && monster.SoundScore > score)
                    continue;
                monster.Sound = point;
                monster.SoundLife = 3.2f;
                monster.SoundScore = score;
            }
        }

        public void Simulate(float dt, IReadOnlyList<FacilityTarget> players, bool blackout, bool bossUnlocked)
        {
            if (graph == null || players == null || !float.IsFinite(dt) || dt <= 0f)
                return;
            budget += Mathf.Min(dt, Step * 3f);
            while (budget >= Step)
            {
                budget -= Step;
                for (int i = 0; i < creatures.Length; i++)
                    Think(i, creatures[i], SpeciesData[i], players, blackout, bossUnlocked);
            }
        }

        private void Think(int index, Monster monster, Species species, IReadOnlyList<FacilityTarget> players,
            bool blackout, bool bossUnlocked)
        {
            if (monster.State == FacilityBehavior.Defeated)
                return;
            if (monster.State == FacilityBehavior.Dormant)
            {
                if (bossUnlocked)
                    Change(monster, FacilityBehavior.Patrol);
                return;
            }

            monster.Age += Step;
            monster.Cooldown -= Step;
            monster.SoundLife -= Step;
            bool seen = FindPlayer(monster, species, players, blackout, out FacilityTarget target);
            if (monster.State == FacilityBehavior.Windup)
            {
                if (seen && target.Id == monster.Target)
                    Face(monster, target.Position);
                if (monster.Age >= species.Windup)
                {
                    if (seen && target.Id == monster.Target && InReach(monster, species, target.Position))
                    {
                        Vector3 direction = Planar(target.Position - monster.Position).normalized;
                        attack?.Invoke(target.Id, index, species.Damage, direction);
                    }
                    Change(monster, FacilityBehavior.Recovery);
                }
                return;
            }
            if (monster.State == FacilityBehavior.Recovery && monster.Age < species.Recovery)
                return;

            if (seen)
            {
                monster.LastSight = target.Position;
                if (InReach(monster, species, target.Position))
                {
                    monster.Target = target.Id;
                    Face(monster, target.Position);
                    Change(monster, FacilityBehavior.Windup);
                }
                else if (index == 1)
                {
                    Change(monster, FacilityBehavior.Observe);
                    Face(monster, target.Position);
                    if (Planar(target.Position - monster.Position).sqrMagnitude < 16f)
                        BackAway(monster, species, target.Position);
                }
                else
                {
                    Change(monster, FacilityBehavior.Chase);
                    float speed = index == Experiment && blackout ? species.Speed * 1.16f : species.Speed;
                    Move(monster, species, target.Position, speed, true);
                }
                return;
            }

            if (monster.State is FacilityBehavior.Chase or FacilityBehavior.Recovery)
                Change(monster, FacilityBehavior.Search);
            if (monster.State == FacilityBehavior.Search)
            {
                if (monster.Age >= 4.1f)
                    Change(monster, FacilityBehavior.Patrol);
                else if (!Move(monster, species, monster.LastSight, species.Speed * 0.58f, true))
                    monster.Yaw = Mathf.Repeat(monster.Yaw + 92f * Step, 360f);
                return;
            }

            if (index == 1 && monster.State == FacilityBehavior.Observe)
            {
                if (blackout && monster.Age > 1.1f && TryVanishingRoute(monster, species, players))
                    Change(monster, FacilityBehavior.Patrol);
                else if (monster.Age > 2.7f)
                    Change(monster, FacilityBehavior.Patrol);
                return;
            }
            if (index == 3 && monster.State == FacilityBehavior.Lure)
            {
                if (monster.Age >= 1.6f)
                    Change(monster, FacilityBehavior.Patrol);
                return;
            }

            if (monster.SoundLife > 0f)
            {
                monster.LastSight = monster.Sound;
                monster.SoundLife = 0f;
                Change(monster, FacilityBehavior.Investigate);
            }
            if (monster.State == FacilityBehavior.Investigate)
            {
                if (monster.Age >= 7f || !Move(monster, species, monster.LastSight, species.Speed * 0.72f, true))
                    Change(monster, FacilityBehavior.Search);
                return;
            }

            if (index == 3 && monster.Cooldown <= 0f && Nearby(monster.Position, players, 13f))
            {
                monster.Cooldown = 10f + patrol.Next(7);
                Change(monster, FacilityBehavior.Lure);
                return;
            }

            Change(monster, FacilityBehavior.Patrol);
            if (monster.GoalNode < 0)
                monster.GoalNode = ChoosePatrol(index, monster.PreviousPatrol);
            if (!Move(monster, species, graph.Point(monster.GoalNode), species.Speed * 0.42f, false))
            {
                monster.PreviousPatrol = monster.GoalNode;
                monster.GoalNode = -1;
            }
        }

        private int ChoosePatrol(int index, int previous)
        {
            int home = SpeciesData[index].Home;
            if (previous != home)
                return home;
            // Room 0 stays a relatively quiet arrival point. Routes may still cross it.
            return index switch
            {
                0 => patrol.Next(2) == 0 ? 1 : 4,
                1 => patrol.Next(2) == 0 ? 3 : 4,
                2 => patrol.Next(2) == 0 ? 4 : 5,
                3 => patrol.Next(2) == 0 ? 3 : 5,
                _ => patrol.Next(2) == 0 ? 2 : 4
            };
        }

        private static bool FindPlayer(Monster monster, Species species, IReadOnlyList<FacilityTarget> players,
            bool blackout, out FacilityTarget selected)
        {
            selected = default;
            float vision = species.Sight * (blackout ? 0.74f : 1f);
            float best = vision * vision;
            bool found = false;
            Vector3 eye = monster.Position + Vector3.up * (species.Height * 0.78f);
            Vector3 forward = Quaternion.Euler(0f, monster.Yaw, 0f) * Vector3.forward;
            for (int i = 0; i < players.Count; i++)
            {
                FacilityTarget player = players[i];
                if (!Finite(player.Position) || monster.State == FacilityBehavior.Windup && player.Id != monster.Target)
                    continue;
                Vector3 delta = player.Position - eye;
                float distance = delta.sqrMagnitude;
                if (distance > best || distance > 9f && Vector3.Dot(forward, Planar(delta).normalized) < -0.25f ||
                    Blocked(eye, player.Position))
                    continue;
                best = distance;
                selected = player;
                found = true;
            }
            return found;
        }

        private static bool InReach(Monster monster, Species species, Vector3 position)
        {
            Vector3 delta = position - monster.Position;
            return Planar(delta).sqrMagnitude <= species.Reach * species.Reach &&
                   delta.y >= -0.25f && delta.y <= Mathf.Max(2.12f, species.Height + 0.3f) &&
                   !Blocked(monster.Position + Vector3.up * (species.Height * 0.68f), position);
        }

        private bool Move(Monster monster, Species species, Vector3 goal, float speed, bool local)
        {
            goal.y = monster.Position.y;
            const float arrival = 0.18f;
            Vector3 delta = goal - monster.Position;
            if (delta.sqrMagnitude <= arrival * arrival)
                return false;
            if (local && delta.sqrMagnitude <= 64f && HasClearance(monster.Position, species, delta, delta.magnitude))
            {
                monster.MoveNode = -1;
                return Advance(monster, species, goal, speed);
            }

            int destination = graph.Nearest(goal);
            int current = monster.MoveNode;
            if (current < 0)
                current = graph.Nearest(monster.Position);
            Vector3 waypoint = graph.Point(current);
            if (Planar(waypoint - monster.Position).sqrMagnitude <= arrival * arrival)
            {
                int next = graph.Next(current, destination);
                if (next < 0 || next == current)
                    return false;
                current = next;
                waypoint = graph.Point(current);
            }
            monster.MoveNode = current;
            if (Advance(monster, species, waypoint, speed))
                return true;

            // A pursuit can end beside scenery. Rejoin the closest reachable corridor.
            monster.MoveNode = NearestAccessible(monster, species);
            return monster.MoveNode >= 0 && monster.MoveNode != current &&
                   Advance(monster, species, graph.Point(monster.MoveNode), speed);
        }

        private int NearestAccessible(Monster monster, Species species)
        {
            float best = float.PositiveInfinity;
            int nearest = -1;
            for (int i = 0; i < graph.Count; i++)
            {
                Vector3 delta = Planar(graph.Point(i) - monster.Position);
                if (delta.sqrMagnitude >= best || !HasClearance(monster.Position, species, delta, delta.magnitude))
                    continue;
                best = delta.sqrMagnitude;
                nearest = i;
            }
            return nearest;
        }

        private static bool Advance(Monster monster, Species species, Vector3 goal, float speed)
        {
            Vector3 delta = Planar(goal - monster.Position);
            float distance = delta.magnitude;
            if (distance < 0.001f)
                return false;
            float step = Mathf.Min(distance, speed * Step);
            if (!HasClearance(monster.Position, species, delta, step))
                return false;
            monster.Position += delta * (step / distance);
            Face(monster, goal);
            return true;
        }

        private static void BackAway(Monster monster, Species species, Vector3 player)
        {
            Vector3 escape = Planar(monster.Position - player).normalized;
            float step = species.Speed * 0.32f * Step;
            if (HasClearance(monster.Position, species, escape, step))
                monster.Position += escape * step;
        }

        private bool TryVanishingRoute(Monster monster, Species species, IReadOnlyList<FacilityTarget> players)
        {
            Vector3 eye = monster.Position + Vector3.up * species.Height * 0.8f;
            if (ObservedFromNearby(eye, players, 0f))
                return false;

            int current = graph.Nearest(monster.Position);
            int destination = ChoosePatrol(1, current);
            int next = graph.Next(current, destination);
            if (next < 0 || next == current)
                return false;
            Vector3 point = graph.Point(next);
            eye = point + Vector3.up * species.Height * 0.8f;
            if (ObservedFromNearby(eye, players, 6f))
                return false;
            monster.Position = point;
            monster.MoveNode = -1;
            return true;
        }

        private static bool ObservedFromNearby(Vector3 eye, IReadOnlyList<FacilityTarget> players, float minimum)
        {
            for (int i = 0; i < players.Count; i++)
            {
                Vector3 point = players[i].Position;
                float distance = (eye - point).sqrMagnitude;
                if (distance < minimum * minimum || distance < 256f && !Blocked(eye, point))
                    return true;
            }
            return false;
        }

        private static bool Nearby(Vector3 position, IReadOnlyList<FacilityTarget> players, float range)
        {
            for (int i = 0; i < players.Count; i++)
                if ((players[i].Position - position).sqrMagnitude < range * range)
                    return true;
            return false;
        }

        private static bool HasClearance(Vector3 from, Species species, Vector3 direction, float distance)
        {
            if (distance <= 0.001f || direction.sqrMagnitude <= 0.000001f)
                return true;
            float radius = species.Radius * 0.84f;
            Vector3 lower = from + Vector3.up * (radius + 0.13f);
            Vector3 upper = from + Vector3.up * Mathf.Max(radius + 0.13f, species.Height - radius);
            return !UnityEngine.Physics.CapsuleCast(lower, upper, radius, direction.normalized, distance,
                Walls, QueryTriggerInteraction.Ignore);
        }

        private static bool Blocked(Vector3 start, Vector3 end) =>
            UnityEngine.Physics.Linecast(start, end, Walls, QueryTriggerInteraction.Ignore);

        private static void Face(Monster monster, Vector3 point)
        {
            Vector3 direction = Planar(point - monster.Position);
            if (direction.sqrMagnitude > 0.0001f)
                monster.Yaw = Mathf.Repeat(Mathf.MoveTowardsAngle(monster.Yaw,
                    Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 360f * Step), 360f);
        }

        private static void Change(Monster monster, FacilityBehavior next)
        {
            if (monster.State == next)
                return;
            monster.State = next;
            monster.Age = 0f;
            monster.MoveNode = -1;
            monster.Visual.SetBehavior(next);
        }

        private void LateUpdate()
        {
            for (int i = 0; i < Count; i++)
            {
                Monster monster = creatures[i];
                monster.Visual.Present(monster.Position, monster.Yaw, Time.unscaledDeltaTime);
            }
        }

        private static Vector3 Planar(Vector3 point) => new(point.x, 0f, point.z);
        private static bool Finite(Vector3 point) =>
            float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
    }
}
