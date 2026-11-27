using NucleusVR.AI;
using NucleusVR.Core;
using NucleusVR.Multiplayer;
using System.Collections.Generic;
using UnityEngine;
using ScenePhysics = UnityEngine.Physics;

namespace NucleusVR.Gameplay
{
    internal sealed partial class GameFlow
    {
        private const float CombatInterval = 0.065f;
        private const float HitCooldown = 0.65f;
        private const float HandForceThreshold = 1.8f;
        private readonly Dictionary<ulong, CombatSample> samples = new();
        private float combatAt;
        private byte heardEvent;

        private sealed class CombatSample
        {
            public Vector3 Head, Origin, Left, Right, LeftVelocity, RightVelocity;
            public bool LeftTracked, RightTracked, Available;
            public float LeftGrip, RightGrip, Time, NextAttack, ProtectedUntil;
            public float HoldStarted;
        }

        private void SimulateFacility(float delta)
        {
            float now = Time.unscaledTime;
            if (now >= eventAt)
                AdvanceFacilityEvent(now);

            bool sampled = SamplePlayers(now);
            targets.Clear();
            foreach (GamePlayer player in players.Values)
            {
                if (!samples.TryGetValue(player.Id, out CombatSample sample) || !sample.Available ||
                    !NearWorld(sample.Origin, facilityWorld.Spawn))
                    continue;
                targets.Add(new FacilityTarget(player.Id, sample.Head));
                if (!sampled || now < sample.NextAttack)
                    continue;
                if (sample.LeftVelocity.sqrMagnitude > HandForceThreshold * HandForceThreshold &&
                    TryStrike(sample, sample.Left, sample.LeftVelocity) ||
                    sample.RightVelocity.sqrMagnitude > HandForceThreshold * HandForceThreshold &&
                    TryStrike(sample, sample.Right, sample.RightVelocity))
                {
                    sample.NextAttack = now + HitCooldown;
                    if (monsters.IsDefeated(4))
                    {
                        secrets.CompleteBoss();
                        authorityState.Notice = "The Experiment is down. Return to the containment release for its record.";
                        dirty = true;
                    }
                }
            }
            monsters.Simulate(delta, targets, authorityState.Event is 1 or 2, (secrets.Progress & (1u << 5)) != 0);
        }

        private bool TryStrike(CombatSample player, Vector3 point, Vector3 velocity)
        {
            if (ScenePhysics.Linecast(player.Head, point, 1 << CalibrationLayers.Course, QueryTriggerInteraction.Ignore))
                return false;
            return monsters.TryStrike(point, 0.33f, Mathf.Clamp(velocity.magnitude * 4f, 8f, 32f)) >= 0;
        }

        private void AdvanceFacilityEvent(float now)
        {
            if (authorityState.Event != 0)
            {
                authorityState.Event = 0;
                eventAt = now + 49f + 7f * (eventIndex % 3);
                authorityState.Notice = "Facility systems stabilizing.";
            }
            else
            {
                authorityState.Event = (byte)(1 + eventIndex++ % 3);
                eventAt = now + (authorityState.Event == 2 ? 10f : 8f);
                authorityState.Notice = authorityState.Event switch
                {
                    1 => "Power failure. Follow the emergency strips.",
                    2 => "Containment alert. Stay clear of the cell corridor.",
                    _ => "Unidentified transmission on the maintenance band."
                };
            }
            dirty = true;
        }

        private void HearContact(Collider surface, Vector3 point, float intensity)
        {
            float now = Time.unscaledTime;
            if (Authority && authorityState.Mode == WorldMode.Facility && intensity > 0.5f && now >= heardAt)
            {
                heardAt = now + 0.25f;
                monsters?.Hear(point, Mathf.Clamp01(intensity));
            }
        }

        private void FacilityAttack(ulong target, int species, float damage, Vector3 direction)
        {
            if (!Authority || authorityState.Mode != WorldMode.Facility || !players.TryGetValue(target, out GamePlayer player))
                return;
            float now = Time.unscaledTime;
            if (!samples.TryGetValue(target, out CombatSample pose) || !pose.Available || now < pose.ProtectedUntil)
                return;
            pose.ProtectedUntil = now + 1.2f;
            Damage(player, damage, direction * 1.4f);
            if (player.Health == 0)
            {
                Recall(target);
                authorityState.Notice = "A researcher returned to the lab after an attack.";
                samples[target] = new CombatSample { ProtectedUntil = now + 3f };
            }
        }

        private void SimulateRound()
        {
            bool eventActive = huntConfiguration != null
                ? huntConfiguration.EventActive(round.Map, round.RemainingSeconds)
                : (int)round.RemainingSeconds % 53 <= 6 || round.RemainingSeconds <= 18;
            byte eventPhase = eventActive ? (byte)1 : (byte)0;
            if (authorityState.Event != eventPhase)
            {
                authorityState.Event = eventPhase;
                dirty = true;
            }
            float now = Time.unscaledTime;
            if (!SamplePlayers(now) || !round.MonsterId.HasValue ||
                !players.TryGetValue(round.MonsterId.Value, out GamePlayer monster))
                return;

            GamePlayer held = null;
            foreach (GamePlayer player in players.Values)
                if (player.CarrierSlot == monster.Slot)
                {
                    held = player;
                    break;
                }

            if (!samples.TryGetValue(monster.Id, out CombatSample hunter) || !hunter.Available)
            {
                if (held != null)
                {
                    held.CarrierSlot = byte.MaxValue;
                    dirty = true;
                }
                return;
            }

            if (held != null)
            {
                ContinueHold(hunter, held, now);
                return;
            }

            if (now < hunter.NextAttack)
                return;
            foreach (GamePlayer player in players.Values)
            {
                if (round.GetRole(player.Id) != MonsterRole.Survivor ||
                    !samples.TryGetValue(player.Id, out CombatSample victim) || !victim.Available ||
                    (victim.Head - hunter.Head).sqrMagnitude > 5f ||
                    ScenePhysics.Linecast(hunter.Head, victim.Head, 1 << CalibrationLayers.Course, QueryTriggerInteraction.Ignore))
                    continue;

                Vector3 chest = victim.Head - Vector3.up * 0.45f;
                if (BothHeld(hunter) && Contact(hunter.Left, chest, 0.53f) && Contact(hunter.Right, chest, 0.53f))
                {
                    player.CarrierSlot = monster.Slot;
                    player.CarrierTarget = CarryAnchor(hunter, victim);
                    hunter.HoldStarted = now;
                    authorityState.Notice = "A survivor has been grabbed.";
                    dirty = true;
                    return;
                }
                bool leftHit = Contact(hunter.Left, chest, 0.49f) &&
                               hunter.LeftVelocity.sqrMagnitude > HandForceThreshold * HandForceThreshold;
                bool rightHit = Contact(hunter.Right, chest, 0.49f) &&
                                hunter.RightVelocity.sqrMagnitude > HandForceThreshold * HandForceThreshold;
                if (!leftHit && !rightHit)
                    continue;
                Vector3 direction = victim.Head - hunter.Head;
                HitSurvivor(monster.Id, player, 36f, direction.normalized * 2.2f);
                hunter.NextAttack = now + HitCooldown;
                return;
            }
        }

        private void ContinueHold(CombatSample hunter, GamePlayer held, float now)
        {
            if (!samples.TryGetValue(held.Id, out CombatSample victim) || !victim.Available ||
                round.GetRole(held.Id) != MonsterRole.Survivor)
            {
                held.CarrierSlot = byte.MaxValue;
                dirty = true;
                return;
            }

            if (BothHeld(hunter) && now - hunter.HoldStarted < 3.5f &&
                (victim.Head - hunter.Head).sqrMagnitude < 5f)
            {
                held.CarrierTarget = CarryAnchor(hunter, victim);
                return;
            }
            held.CarrierSlot = byte.MaxValue;
            Vector3 throwVelocity = (hunter.LeftVelocity + hunter.RightVelocity) * 0.5f;
            if (throwVelocity.sqrMagnitude > 2.4f * 2.4f)
                HitSurvivor(round.MonsterId.Value, held, 47f, Vector3.ClampMagnitude(throwVelocity, 4f));
            else
                authorityState.Notice = "A survivor broke free.";
            hunter.NextAttack = now + 0.65f;
            dirty = true;
        }

        private static Vector3 CarryAnchor(CombatSample hunter, CombatSample victim)
        {
            Vector3 hands = (hunter.Left + hunter.Right) * 0.5f;
            Vector3 away = hands - hunter.Head;
            away.y = 0f;
            Vector3 target = hands + away.normalized * 0.45f;
            target.y = victim.Origin.y;
            return target;
        }

        private void HitSurvivor(ulong hunter, GamePlayer player, float damage, Vector3 impulse)
        {
            Damage(player, damage, impulse);
            if (player.Health != 0)
            {
                authorityState.Notice = "The monster hit a survivor.";
                return;
            }
            MonsterRoundPhase before = round.Phase;
            if (round.TryCapture(hunter, player.Id))
            {
                authorityState.Notice = "A survivor was captured.";
                player.Role = round.GetRole(player.Id);
                AuthorizeSpawn(player);
            }
            if (round.Phase != before)
                RoundPhaseChanged();
        }

        private void Damage(GamePlayer player, float amount, Vector3 impulse)
        {
            player.Health = (byte)Mathf.Max(0, player.Health - Mathf.Clamp(Mathf.RoundToInt(amount), 1, 100));
            player.Hit++;
            player.ImpulseX = (sbyte)Mathf.RoundToInt(Mathf.Clamp(impulse.x * 20f, -100f, 100f));
            player.ImpulseZ = (sbyte)Mathf.RoundToInt(Mathf.Clamp(impulse.z * 20f, -100f, 100f));
            dirty = true;
        }

        private bool SamplePlayers(float now)
        {
            if (now < combatAt)
                return false;
            combatAt = now + CombatInterval;
            foreach (GamePlayer player in players.Values)
            {
                if (!samples.TryGetValue(player.Id, out CombatSample sample))
                    samples.Add(player.Id, sample = new CombatSample());
                Vector3 origin, head, left, right;
                bool leftTracked, rightTracked;
                float leftGrip, rightGrip;
                if (online)
                {
                    if (!session.TryGetGameplayPlayer(player.Id, out GameplayPlayerPose pose))
                    {
                        sample.Available = false;
                        continue;
                    }
                    origin = pose.Origin;
                    head = pose.Head;
                    left = pose.LeftHand;
                    right = pose.RightHand;
                    leftTracked = pose.LeftTracked;
                    rightTracked = pose.RightTracked;
                    leftGrip = pose.LeftGrip;
                    rightGrip = pose.RightGrip;
                }
                else
                {
                    if (!tracking.HeadTracked)
                    {
                        sample.Available = false;
                        continue;
                    }
                    origin = movement.transform.position;
                    head = tracking.Head.position;
                    left = movement.LeftContact.position;
                    right = movement.RightContact.position;
                    leftTracked = tracking.LeftHandTracked;
                    rightTracked = tracking.RightHandTracked;
                    leftGrip = tracking.LeftGrip;
                    rightGrip = tracking.RightGrip;
                }
                Vector3 displacement = origin - sample.Origin;
                float elapsed = now - sample.Time;
                bool continuous = sample.Available && elapsed > 0.001f && elapsed < 0.2f &&
                                  displacement.sqrMagnitude < 1.5f * 1.5f;
                sample.LeftVelocity = continuous && sample.LeftTracked && leftTracked
                    ? Vector3.ClampMagnitude((left - sample.Left - displacement) / elapsed, 12f) : Vector3.zero;
                sample.RightVelocity = continuous && sample.RightTracked && rightTracked
                    ? Vector3.ClampMagnitude((right - sample.Right - displacement) / elapsed, 12f) : Vector3.zero;
                if (online && continuous && elapsed > 0.02f && displacement.sqrMagnitude / (elapsed * elapsed) > 2.7f * 2.7f &&
                    authorityState.Mode == WorldMode.Facility && NearWorld(origin, facilityWorld.Spawn))
                    monsters.Hear(origin, 0.4f);
                sample.Origin = origin;
                sample.Head = head;
                sample.Left = left;
                sample.Right = right;
                sample.LeftTracked = leftTracked;
                sample.RightTracked = rightTracked;
                sample.LeftGrip = leftGrip;
                sample.RightGrip = rightGrip;
                sample.Time = now;
                sample.Available = true;
            }
            return true;
        }

        private static bool BothHeld(CombatSample sample) =>
            sample.LeftTracked && sample.RightTracked && sample.LeftGrip > 0.69f && sample.RightGrip > 0.69f;

        private static bool Contact(Vector3 hand, Vector3 target, float radius) =>
            (hand - target).sqrMagnitude < radius * radius;

        private static bool NearWorld(Vector3 point, Vector3 spawn) => (point - spawn).sqrMagnitude < 50f * 50f;
    }
}
