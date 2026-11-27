using System;
using System.Collections.Generic;
using NucleusVR.AI;
using NucleusVR.Anatomy;
using NucleusVR.Core;
using NucleusVR.Maps;
using NucleusVR.Movement;
using NucleusVR.Multiplayer;
using NucleusVR.Player;
using NucleusVR.Props;
using NucleusVR.Secrets;
using NucleusVR.VR;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NucleusVR.Gameplay
{
    internal sealed partial class GameFlow : MonoBehaviour
    {
        private const ushort Explore = 1, Monster = 2, Sandbox = 3, Return = 4;
        private const ushort VoteBackrooms = 20, VoteFactory = 21, VoteMines = 22, StartRound = 23;
        private const ushort RefreshSpawn = 24, LeaveHunt = 25, LeaveRoom = 26, LeaveArenaRoom = 27;
        private const float Reach = 0.18f;
        private readonly Dictionary<ulong, GamePlayer> players = new();
        private readonly List<ulong> departed = new();
        private readonly List<FacilityTarget> targets = new();
        private readonly List<GameWorld> activeWorlds = new();
        private readonly GameWorld[] arenas = new GameWorld[3];
        private readonly MonsterMaps[] maps = new MonsterMaps[3];
        private MonsterRound round;
        private HuntConfiguration huntConfiguration;
        private FacilitySecrets secrets = new();
        private GameState authorityState = new();
        private GameState view;
        private GameWorld hub, facilityWorld, briefing, workshopWorld;
        private ResearchFacility facility;
        private FacilityMonsters monsters;
        private SandboxWorkshop workshop;
        private TrackedRig tracking;
        private HandLocomotion movement;
        private DesktopRigDriver desktop;
        private DirectSession session;
        private OnlineSession onlineSession;
        private EquipmentHands equipment;
        private AnatomyHands anatomyHands;
        private GorillaAvatar localAvatar;
        private NetworkManager networkManager;
        private CalibrationInteraction calibration;
        private Material lit, signal, control;
        private bool online, wasAuthority, dirty, leftPressed, rightPressed, located, locatedForRoom;
        private float publishAt, baselineAt, eventAt, heardAt;
        private float carryUpdatedAt;
        private float nextFallCheck;
        private uint localEpoch, respawnSeen;
        private ushort hitSeen;
        private MonsterRole localRole;
        private int eventIndex;
        private string localNotice;
        private string modeStatus = "";
        private float localNoticeUntil;
        private uint savedDiscoveries;
        private AudioSource alarm;
        private AudioClip alarmTone;
        private TextMesh vrStatus;

        public bool InCalibration => view == null || view.Mode == WorldMode.Calibration;
        public string ModeTitle => view?.Mode switch
        {
            WorldMode.Facility => "NUCLEUS RESEARCH FACILITY",
            WorldMode.Monster => "BE THE MONSTER",
            WorldMode.Sandbox => "PHYSICS SANDBOX",
            _ => "CALIBRATION BAY"
        };
        public string LocalStatus => Time.unscaledTime < localNoticeUntil ? localNotice :
            InCalibration ? calibration?.Status ?? "" : modeStatus;
        private ulong LocalId => online && session != null ? session.LocalPlayerId : 0;
        private bool Authority => !online || session.IsGameplayAuthority;

        public void Build(Material surface, Material indicator, TrackedRig rig, HandLocomotion motor,
            DesktopRigDriver desktopDriver, EquipmentHands hands, CalibrationInteraction course, DirectSession network,
            GorillaAvatar avatar, GameObject calibrationRoom, GameObject anatomyLab, HuntConfiguration huntSettings)
        {
            lit = surface;
            signal = indicator;
            tracking = rig;
            movement = motor;
            desktop = desktopDriver;
            equipment = hands;
            anatomyHands = rig.GetComponent<AnatomyHands>();
            session = network;
            onlineSession = network != null ? network.GetComponent<OnlineSession>() : null;
            networkManager = network != null ? network.GetComponent<NetworkManager>() : null;
            localAvatar = avatar;
            huntConfiguration = huntSettings;
            round = new MonsterRound(huntSettings != null ? huntSettings.RoundSettings : null);
            calibration = course;
            savedDiscoveries = FacilityDiscoveryStore.Load();
            control = new Material(signal) { name = "Gameplay control", color = new Color32(83, 208, 196, 255) };
            BuildAlarm();
            vrStatus = FacilityGeometry.Label(tracking.Head, "", new Vector3(0f, -0.39f, 1.31f),
                0.066f, new Color32(223, 241, 238, 255));
            vrStatus.gameObject.SetActive(false);

            hub = CreateWorld("Calibration mode controls", Vector3.zero);
            hub.Companions = new[] { calibrationRoom, anatomyLab };
            hub.Spawn = CalibrationCourse.Spawn;
            BuildHub(hub);
            briefing = CreateWorld("Monster briefing", new Vector3(130f, 0f, -58f));
            BuildBriefing(briefing);
            briefing.Root.SetActive(false);

            calibration.Gameplay = this;
            movement.SurfaceTapped += HearContact;
            if (session != null)
            {
                session.GameplayUseReceived += UseFromNetwork;
                session.GameplayStateReceived += Receive;
                session.GameplayMotionReceived += Receive;
            }
            wasAuthority = true;
            AddPlayer(0);
            Present(BuildState(), false);
        }

        private void Update()
        {
            if (hub == null)
                return;

            bool networkActive = session != null && session.IsSessionActive;
            bool nowAuthority = !networkActive || session.IsGameplayAuthority;
            if (networkActive != online || nowAuthority != wasAuthority)
            {
                bool disconnected = online && !networkActive;
                bool hostStarted = nowAuthority && networkActive;
                online = networkActive;
                wasAuthority = nowAuthority;
                locatedForRoom = false;
                if (disconnected || hostStarted)
                {
                    ResetAuthorityWorld();
                    if (disconnected)
                        Present(BuildState(), true);
                }
                if (online && nowAuthority)
                    RegisterActiveSites();
            }

            if (!Authority)
            {
                if (Time.unscaledTime - carryUpdatedAt > 0.65f)
                    movement.SetCarryTarget(null);
                return;
            }

            SynchronizeRoster();
            CheckFalls();
            float delta = Mathf.Min(Time.unscaledDeltaTime, 0.15f);
            if (authorityState.Mode == WorldMode.Monster)
            {
                MonsterRoundPhase previousPhase = round.Phase;
                if (previousPhase == MonsterRoundPhase.Waiting && round.PlayerCount >= round.Settings.MinimumPlayers)
                    round.TryBeginVoting();
                round.Tick(delta);
                if (round.Phase != previousPhase)
                    RoundPhaseChanged();
                if (round.Phase == MonsterRoundPhase.Active)
                    SimulateRound();
            }
            else if (authorityState.Mode == WorldMode.Facility)
            {
                SimulateFacility(delta);
            }

            if (dirty || Time.unscaledTime >= publishAt)
                Publish();
        }

        private void LateUpdate()
        {
            if (hub == null)
                return;

            bool showStatus = !desktop.IsDesktop && tracking.HeadTracked && !InCalibration;
            if (vrStatus.gameObject.activeSelf != showStatus)
                vrStatus.gameObject.SetActive(showStatus);
            if (showStatus)
                vrStatus.text = LocalStatus;
            if (!tracking.HeadTracked)
            {
                leftPressed = rightPressed = false;
                return;
            }

            if (desktop.IsDesktop && !desktop.MenuOpen)
            HandleDesktopControls();
            if (desktop.IsDesktop && desktop.MenuOpen)
                return;

            TryGrip(movement.LeftContact, tracking.LeftHandTracked ? tracking.LeftGrip : 0f, true, ref leftPressed);
            TryGrip(movement.RightContact, tracking.RightHandTracked ? tracking.RightGrip : 0f, false, ref rightPressed);
        }

        private void HandleDesktopControls()
        {
            Keyboard keys = Keyboard.current;
            if (keys == null)
                return;
            if (!online)
            {
                if (keys.digit1Key.wasPressedThisFrame) Use(LocalId, Explore);
                if (keys.digit2Key.wasPressedThisFrame) Use(LocalId, Monster);
                if (keys.digit3Key.wasPressedThisFrame) Use(LocalId, Sandbox);
                if (keys.digit4Key.wasPressedThisFrame) Use(LocalId, Return);
                if (keys.rKey.wasPressedThisFrame && !InCalibration) Recall(LocalId);
                if (view?.Mode == WorldMode.Monster && view.Phase == MonsterRoundPhase.Voting)
                {
                    if (keys.digit7Key.wasPressedThisFrame) Use(LocalId, VoteBackrooms);
                    if (keys.digit8Key.wasPressedThisFrame) Use(LocalId, VoteFactory);
                    if (keys.digit9Key.wasPressedThisFrame) Use(LocalId, VoteMines);
                }
            }
            if (view?.Mode == WorldMode.Sandbox && keys.gKey.wasPressedThisFrame)
                workshop.CycleGore();
        }

        private void TryGrip(Transform hand, float grip, bool left, ref bool pressed)
        {
            if (grip < 0.35f)
                pressed = false;
            if (pressed || grip < 0.66f)
                return;

            pressed = true;
            GameSite closest = null;
            float best = Reach * Reach;
            foreach (GameWorld world in activeWorlds)
                foreach (GameSite site in world.Sites)
                {
                    Collider shape = site.Shape;
                    if (!shape.enabled || !shape.gameObject.activeInHierarchy)
                        continue;
                    float distance = (shape.ClosestPoint(hand.position) - hand.position).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        closest = site;
                    }
                }

            if (closest == null)
                return;
            if (closest.Id == 306 && view?.Mode == WorldMode.Sandbox)
                workshop.CycleGore();
            else if (closest.Id is LeaveRoom or LeaveArenaRoom)
            {
                if (onlineSession != null && onlineSession.InRoom)
                    onlineSession.Leave();
                else if (online)
                    session.Disconnect();
                else
                    SetLocalNotice("No online room is connected.");
            }
            else if (!Authority && (closest.Id <= Return || closest.Id is LeaveHunt or StartRound ||
                     (view?.Mode == WorldMode.Sandbox && closest.Id >= 301)))
                SetLocalNotice("This control is available to the room host.");
            else if (online)
                session.RequestGameplayUse(closest.Id, left);
            else
                Use(LocalId, closest.Id);
        }

        private void UseFromNetwork(ulong actor, ushort site, bool leftHand, Vector3 point)
        {
            if (Authority && FindActiveSite(site))
                Use(actor, site);
        }

        private bool FindActiveSite(ushort id)
        {
            foreach (GameWorld world in activeWorlds)
                foreach (GameSite site in world.Sites)
                    if (site.Id == id)
                        return true;
            return false;
        }

        private void Use(ulong actor, ushort site)
        {
            if (!players.TryGetValue(actor, out GamePlayer player))
                return;

            bool canManage = !online || actor == LocalId;
            if ((site >= Explore && site <= Return) || site == LeaveHunt)
            {
                if (canManage)
                    Enter(site is Return or LeaveHunt ? WorldMode.Calibration : (WorldMode)site);
                return;
            }

            if (site == RefreshSpawn)
            {
                if (authorityState.Mode == WorldMode.Sandbox || authorityState.Mode == WorldMode.Facility)
                    Recall(actor);
                return;
            }

            switch (authorityState.Mode)
            {
                case WorldMode.Facility:
                    UseFacility(player, site);
                    break;
                case WorldMode.Monster:
                    UseRound(player, site, canManage);
                    break;
                case WorldMode.Sandbox:
                    if (canManage)
                        UseWorkshop(site);
                    break;
            }
        }

        private void Enter(WorldMode mode)
        {
            if (authorityState.Mode == mode)
                return;
            authorityState.Mode = mode;
            authorityState.Notice = mode switch
            {
                WorldMode.Facility => "Explore the facility. Grip marked controls to investigate.",
                WorldMode.Monster => "Vote for a map while the round is forming.",
                WorldMode.Sandbox => "Grip the control pads. Strike, grab and inspect the specimens.",
                _ => ""
            };
            authorityState.Event = 0;
            eventAt = Time.unscaledTime + 46f;
            round.Reset();
            ResetVitals();
            if (mode == WorldMode.Monster)
                round.TryBeginVoting(!online);
            ChangeWorld();
        }

        private void UseFacility(GamePlayer player, ushort site)
        {
            if (site == 120)
            {
                Recall(player.Id);
                return;
            }
            if (!secrets.Use(site, out string feedback))
                return;
            player.Discoveries |= secrets.LastReward;
            authorityState.Notice = feedback;
            dirty = true;
        }

        private void UseRound(GamePlayer player, ushort site, bool canManage)
        {
            if (site >= VoteBackrooms && site <= VoteMines &&
                round.TryVote(player.Id, (MonsterMap)(site - VoteBackrooms)))
            {
                dirty = true;
                return;
            }
            if (site == StartRound && canManage && round.TryBeginVoting(!online))
            {
                authorityState.Notice = "Vote for the next map.";
                dirty = true;
                return;
            }
            if (site is >= 201 and <= 203 && round.TryActivateObjective(player.Id, site - 201))
            {
                authorityState.Notice = round.ExitUnlocked ? "Exit unlocked. Get out." : "Objective activated.";
                dirty = true;
            }
            else if (site is 204 or 205 && round.TryEscape(player.Id))
            {
                authorityState.Notice = "A survivor escaped.";
                player.Role = round.GetRole(player.Id);
                player.CarrierSlot = byte.MaxValue;
                AuthorizeSpawn(player);
                if (round.Phase == MonsterRoundPhase.Finished)
                    RoundPhaseChanged();
                dirty = true;
            }
        }

        private void UseWorkshop(ushort site)
        {
            switch (site)
            {
                case 301: authorityState.WorkshopSubjects ^= 1; break;
                case 302: authorityState.WorkshopSubjects ^= 2; break;
                case 303: authorityState.WorkshopFlags ^= 1; break;
                case 304: authorityState.WorkshopFlags ^= 2; break;
                case 305: authorityState.WorkshopReset++; break;
                case 307: authorityState.WorkshopFlags ^= 4; break;
                default: return;
            }
            dirty = true;
        }

        private void RoundPhaseChanged()
        {
            switch (round.Phase)
            {
                case MonsterRoundPhase.Active:
                    authorityState.Notice = huntConfiguration != null ? huntConfiguration.Instructions(round.Map) :
                        "Activate three stations and escape.";
                    ResetVitals();
                    ChangeWorld();
                    break;
                case MonsterRoundPhase.Finished:
                    foreach (GamePlayer player in players.Values)
                        player.CarrierSlot = byte.MaxValue;
                    authorityState.Notice = round.Outcome switch
                    {
                        MonsterRoundOutcome.SurvivorsWon => "Survivors win.",
                        MonsterRoundOutcome.MonsterWon => "The monster wins.",
                        _ => "The round ended early."
                    };
                    ChangeWorld();
                    break;
                default:
                    dirty = true;
                    break;
            }
        }

        private void ResetAuthorityWorld()
        {
            players.Clear();
            samples.Clear();
            round.Reset(clearPlayers: true);
            authorityState = new GameState { WorldEpoch = authorityState.WorldEpoch + 1 };
            secrets = new FacilitySecrets();
            monsters?.ResetEncounter();
            AddPlayer(LocalId);
            if (online)
                AuthorizeSpawn(players[LocalId]);
            dirty = true;
        }

        private void ResetVitals()
        {
            samples.Clear();
            foreach (GamePlayer player in players.Values)
            {
                player.Health = 100;
                player.CarrierSlot = byte.MaxValue;
            }
        }

        private void SynchronizeRoster()
        {
            if (!online)
                return;

            var roster = session.ConnectedPlayers;
            departed.Clear();
            foreach (GamePlayer player in players.Values)
            {
                bool found = false;
                for (int i = 0; i < roster.Count; i++)
                    if (roster[i] == player.Id)
                    {
                        found = true;
                        break;
                    }
                if (!found)
                    departed.Add(player.Id);
            }
            foreach (ulong id in departed)
            {
                players.Remove(id);
                samples.Remove(id);
                MonsterRoundPhase previous = round.Phase;
                round.Leave(id);
                if (round.Phase != previous)
                    RoundPhaseChanged();
                dirty = true;
            }
            for (int i = 0; i < roster.Count; i++)
                if (AddPlayer(roster[i]))
                    AuthorizeSpawn(players[roster[i]]);
        }

        private bool AddPlayer(ulong id)
        {
            if (players.ContainsKey(id) || players.Count >= GameState.MaxPlayers)
                return false;
            int occupied = 0;
            foreach (GamePlayer player in players.Values)
                occupied |= 1 << player.Slot;
            byte slot = 0;
            while ((occupied & (1 << slot)) != 0)
                slot++;
            players.Add(id, new GamePlayer(id) { Slot = slot });
            round.TryJoin(id);
            dirty = true;
            return true;
        }

        private void ChangeWorld()
        {
            authorityState.WorldEpoch++;
            BuildForCurrentMode(authorityState.Mode, round.Phase, round.Map);
            foreach (GamePlayer player in players.Values)
            {
                player.Role = round.GetRole(player.Id);
                AuthorizeSpawn(player);
            }
            dirty = true;
        }

        private void CheckFalls()
        {
            if (authorityState.Mode == WorldMode.Calibration || Time.unscaledTime < nextFallCheck)
                return;
            nextFallCheck = Time.unscaledTime + 0.24f;
            bool restoreHealth = authorityState.Mode != WorldMode.Monster;
            if (!online)
            {
                if (movement.transform.position.y < -3f)
                    Recall(LocalId, restoreHealth);
                return;
            }

            foreach (GamePlayer player in players.Values)
                if (session.TryGetGameplayPlayer(player.Id, out GameplayPlayerPose pose) && pose.Origin.y < -3f)
                    Recall(player.Id, restoreHealth);
        }

        private void Recall(ulong id, bool restoreHealth = true)
        {
            if (!players.TryGetValue(id, out GamePlayer player))
                return;
            player.Respawn++;
            if (restoreHealth)
                player.Health = 100;
            player.CarrierSlot = byte.MaxValue;
            samples.Remove(id);
            AuthorizeSpawn(player);
            dirty = true;
        }

        private void AuthorizeSpawn(GamePlayer player)
        {
            if (!online)
                return;
            Vector3 spawn = SpawnFor(authorityState.Mode, round.Phase, round.Map, player);
            session.AuthorizeGameplayWarp(player.Id, spawn);
        }

        private GameState BuildState()
        {
            var state = new GameState
            {
                Mode = authorityState.Mode, WorldEpoch = authorityState.WorldEpoch,
                Map = round.Map, Phase = round.Phase, Outcome = round.Outcome,
                Seconds = (ushort)Mathf.Clamp(Mathf.CeilToInt((float)round.RemainingSeconds), 0, ushort.MaxValue),
                Objectives = (byte)round.CompletedObjectives, Practice = round.IsPractice,
                Secrets = secrets.Progress, Puzzle = secrets.PuzzleBits, Event = authorityState.Event,
                WorkshopFlags = authorityState.WorkshopFlags, WorkshopSubjects = authorityState.WorkshopSubjects,
                WorkshopReset = authorityState.WorkshopReset, Notice = authorityState.Notice,
                Players = new GamePlayer[players.Count]
            };
            int index = 0;
            foreach (GamePlayer player in players.Values)
            {
                player.Role = round.GetRole(player.Id);
                state.Players[index++] = player;
            }
            for (int i = 0; i < state.Votes.Length; i++)
                state.Votes[i] = (byte)round.GetVoteCount((MonsterMap)i);
            if (state.Mode == WorldMode.Facility && monsters != null)
            {
                state.Monsters = new MonsterPose[monsters.Count];
                for (int i = 0; i < monsters.Count; i++)
                    state.Monsters[i] = new MonsterPose(monsters.PositionAt(i), monsters.YawAt(i), monsters.StateAt(i));
            }
            return state;
        }

        private void Publish()
        {
            GameState state = BuildState();
            Present(state, located || state.Mode != WorldMode.Calibration);
            if (online)
            {
                byte[] payload = state.Encode();
                if (dirty || Time.unscaledTime >= baselineAt)
                {
                    session.PublishGameplayState(payload);
                    baselineAt = Time.unscaledTime + 1f;
                }
                else if (state.Mode is WorldMode.Facility or WorldMode.Monster)
                    session.PublishGameplayMotion(payload);
            }
            dirty = false;
            publishAt = Time.unscaledTime + (state.Mode is WorldMode.Facility or WorldMode.Monster ? 0.2f : 0.65f);
        }

        private void Receive(byte[] data)
        {
            if (session == null || session.IsGameplayAuthority || !GameState.TryDecode(data, out GameState state))
                return;
            if (state.Player(session.LocalPlayerId) == null)
                return;
            online = true;
            wasAuthority = false;
            Present(state, true);
        }

        private void Present(GameState state, bool allowMove)
        {
            GamePlayer local = state.Player(LocalId);
            if (local == null)
                return;

            view = state;
            BuildForCurrentMode(state.Mode, state.Phase, state.Map);
            if (state.Mode == WorldMode.Facility)
            {
                facility.Present(state.Secrets, state.Event, state.Puzzle);
                for (int i = 0; !Authority && i < state.Monsters.Length && i < monsters.Count; i++)
                {
                    MonsterPose pose = state.Monsters[i];
                    monsters.ApplyPeerPose(i, pose.Position, pose.Yaw, pose.State);
                }
                if ((local.Discoveries & ~savedDiscoveries) != 0)
                {
                    FacilityDiscoveryStore.Record(local.Discoveries);
                    savedDiscoveries |= local.Discoveries;
                }
            }
            if (state.Mode == WorldMode.Sandbox)
                workshop.Present(state.WorkshopFlags, state.WorkshopSubjects, state.WorkshopReset, Authority);
            if (state.Mode == WorldMode.Monster && state.Phase == MonsterRoundPhase.Active)
                maps[(int)state.Map].Present((uint)((1 << state.Objectives) - 1),
                    state.Objectives == MonsterRound.ObjectiveCount, state.Event);

            bool activeHunt = state.Mode == WorldMode.Monster && state.Phase == MonsterRoundPhase.Active;
            bool monsterRole = activeHunt && local.Role == MonsterRole.Monster;
            movement.SetRoleDrive(monsterRole ? 1.3f : 1f);
            localAvatar.SetHuntAppearance(monsterRole);
            if (online && networkManager.SpawnManager != null)
                foreach (NetworkObject entity in networkManager.SpawnManager.SpawnedObjectsList)
                    if (entity.IsPlayerObject && entity.TryGetComponent(out NetworkRig rig))
                        rig.HuntMonster = activeHunt && state.Player(entity.OwnerClientId)?.Role == MonsterRole.Monster;
            bool relocate = allowMove && (!locatedForRoom || localEpoch != state.WorldEpoch ||
                            respawnSeen != local.Respawn || localRole != local.Role);
            if (relocate)
            {
                equipment.ReleaseAll();
                anatomyHands?.ReleaseAll();
                Vector3 spawn = SpawnFor(state.Mode, state.Phase, state.Map, local);
                movement.ResetTo(spawn, Quaternion.identity);
                PlayCue(state.Mode == WorldMode.Facility ? 0.16f : 0.10f);
            }
            else if (hitSeen != local.Hit)
            {
                movement.ApplyGameplayImpulse(new Vector3(local.ImpulseX / 20f, 0f, local.ImpulseZ / 20f));
                PlayCue(0.18f);
            }
            bool carried = state.Mode == WorldMode.Monster && state.Phase == MonsterRoundPhase.Active &&
                           local.Role == MonsterRole.Survivor && local.CarrierSlot != byte.MaxValue;
            movement.SetCarryTarget(carried ? local.CarrierTarget : null);
            if (carried)
                carryUpdatedAt = Time.unscaledTime;
            if (heardEvent != state.Event && state.Event != 0)
                PlayCue(0.24f);
            heardEvent = state.Event;
            localRole = local.Role;
            hitSeen = local.Hit;
            respawnSeen = local.Respawn;
            localEpoch = state.WorldEpoch;
            locatedForRoom = located = true;
            modeStatus = FormatStatus(state, local);
            foreach (GameWorld world in activeWorlds)
                if (world.Readout != null)
                    world.Readout.text = modeStatus;
        }

        private string FormatStatus(GameState state, GamePlayer local)
        {
            if (state == null || state.Mode == WorldMode.Calibration)
                return calibration?.Status ?? "";
            if (state.Mode == WorldMode.Facility)
                return $"Health {local?.Health ?? 100} · Discoveries {CountDiscoveries(savedDiscoveries)}/8\n{state.Notice}";
            if (state.Mode == WorldMode.Sandbox)
                return state.Notice;
            return state.Phase switch
            {
                MonsterRoundPhase.Waiting => $"Waiting for at least {round.Settings.MinimumPlayers} players.",
                MonsterRoundPhase.Voting =>
                    $"Vote · {state.Seconds}s · Backrooms {state.Votes[0]}  Factory {state.Votes[1]}  Mines {state.Votes[2]}",
                MonsterRoundPhase.Active => $"{state.Map} · {local?.Role} · {state.Seconds}s\n" +
                    (local?.Role == MonsterRole.Monster
                        ? "Swing to hit. Squeeze both grips to hold a nearby survivor."
                        : $"Health {local?.Health ?? 100} · Objectives {state.Objectives}/3\n{state.Notice}"),
                _ => state.Notice
            };
        }

        private static int CountDiscoveries(uint mask)
        {
            int count = 0;
            for (int i = 0; i < 8; i++)
                count += (int)((mask >> i) & 1);
            return count;
        }

        private void SetLocalNotice(string message)
        {
            localNotice = message;
            localNoticeUntil = Time.unscaledTime + 3f;
        }

        private void BuildAlarm()
        {
            alarm = gameObject.AddComponent<AudioSource>();
            alarm.playOnAwake = false;
            alarm.spatialBlend = 0f;
            const int rate = 16000;
            float[] wave = new float[rate / 4];
            for (int i = 0; i < wave.Length; i++)
            {
                float progress = i / (float)wave.Length;
                wave[i] = Mathf.Sin(2f * Mathf.PI * (410f - 110f * progress) * i / rate) *
                          Mathf.Sin(progress * Mathf.PI) * 0.34f;
            }
            alarmTone = AudioClip.Create("Facility receiver pulse", wave.Length, 1, rate, false);
            alarmTone.SetData(wave, 0);
        }

        private void PlayCue(float volume)
        {
            if (alarm != null)
                alarm.PlayOneShot(alarmTone, volume);
        }

        private void OnDestroy()
        {
            if (movement != null)
                movement.SurfaceTapped -= HearContact;
            if (session != null)
            {
                session.GameplayUseReceived -= UseFromNetwork;
                session.GameplayStateReceived -= Receive;
                session.GameplayMotionReceived -= Receive;
            }
            if (control != null) Destroy(control);
            if (alarmTone != null) Destroy(alarmTone);
            if (vrStatus != null) Destroy(vrStatus.gameObject);
        }
    }
}
