using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace NucleusVR.Gameplay
{
    internal enum WorldMode : byte { Calibration, Facility, Monster, Sandbox }

    internal sealed class GameState
    {
        public const int MaxPlayers = 10;
        public WorldMode Mode;
        public uint WorldEpoch;
        public MonsterMap Map;
        public MonsterRoundPhase Phase;
        public MonsterRoundOutcome Outcome;
        public ushort Seconds;
        public byte Objectives, Event, WorkshopFlags = 7, WorkshopSubjects = 1;
        public uint Secrets, WorkshopReset, Puzzle;
        public bool Practice;
        public readonly byte[] Votes = new byte[3];
        public string Notice = "";
        public GamePlayer[] Players = Array.Empty<GamePlayer>();
        public MonsterPose[] Monsters = Array.Empty<MonsterPose>();

        public GamePlayer Player(ulong id)
        {
            foreach (GamePlayer player in Players)
                if (player.Id == id)
                    return player;
            return null;
        }

        public byte[] Encode()
        {
            using var stream = new MemoryStream(512);
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)1);
            writer.Write((byte)Mode);
            writer.Write(WorldEpoch);
            writer.Write((byte)Map);
            writer.Write((byte)Phase);
            writer.Write((byte)Outcome);
            writer.Write(Seconds);
            writer.Write(Objectives);
            writer.Write(Event);
            writer.Write(WorkshopFlags);
            writer.Write(WorkshopSubjects);
            writer.Write(Secrets);
            writer.Write(WorkshopReset);
            writer.Write(Puzzle);
            writer.Write(Practice);
            writer.Write(Votes);
            byte[] note = Encoding.UTF8.GetBytes(Notice);
            if (note.Length > 340)
                note = Array.Empty<byte>();
            writer.Write((ushort)note.Length);
            writer.Write(note);
            writer.Write((byte)Players.Length);
            foreach (GamePlayer player in Players)
            {
                writer.Write(player.Id);
                writer.Write((byte)player.Role);
                writer.Write(player.Health);
                writer.Write(player.Discoveries);
                writer.Write(player.Respawn);
                writer.Write(player.Slot);
                writer.Write(player.Hit);
                writer.Write(player.ImpulseX);
                writer.Write(player.ImpulseZ);
                writer.Write(player.CarrierSlot);
                if (player.CarrierSlot != byte.MaxValue)
                {
                    writer.Write(player.CarrierTarget.x);
                    writer.Write(player.CarrierTarget.y);
                    writer.Write(player.CarrierTarget.z);
                }
            }
            writer.Write((byte)Monsters.Length);
            foreach (MonsterPose monster in Monsters)
            {
                writer.Write(monster.Position.x);
                writer.Write(monster.Position.y);
                writer.Write(monster.Position.z);
                writer.Write(monster.Yaw);
                writer.Write(monster.State);
            }
            return stream.ToArray();
        }

        public static bool TryDecode(byte[] bytes, out GameState state)
        {
            state = null;
            if (bytes == null || bytes.Length > 1024)
                return false;
            try
            {
                using var stream = new MemoryStream(bytes, false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadByte() != 1)
                    return false;
                var decoded = new GameState
                {
                    Mode = (WorldMode)reader.ReadByte(),
                    WorldEpoch = reader.ReadUInt32(),
                    Map = (MonsterMap)reader.ReadByte(),
                    Phase = (MonsterRoundPhase)reader.ReadByte(),
                    Outcome = (MonsterRoundOutcome)reader.ReadByte(),
                    Seconds = reader.ReadUInt16(),
                    Objectives = reader.ReadByte(),
                    Event = reader.ReadByte(),
                    WorkshopFlags = reader.ReadByte(),
                    WorkshopSubjects = reader.ReadByte(),
                    Secrets = reader.ReadUInt32(),
                    WorkshopReset = reader.ReadUInt32(),
                    Puzzle = reader.ReadUInt32(),
                    Practice = reader.ReadBoolean()
                };
                if (decoded.Mode > WorldMode.Sandbox || decoded.Map > MonsterMap.Mines ||
                    decoded.Phase > MonsterRoundPhase.Finished || decoded.Outcome > MonsterRoundOutcome.Aborted ||
                    decoded.Objectives > MonsterRound.ObjectiveCount)
                    return false;
                for (int i = 0; i < decoded.Votes.Length; i++)
                    decoded.Votes[i] = reader.ReadByte();
                int noteLength = reader.ReadUInt16();
                if (noteLength > 340 || noteLength > stream.Length - stream.Position)
                    return false;
                decoded.Notice = Encoding.UTF8.GetString(reader.ReadBytes(noteLength));
                int count = reader.ReadByte();
                if (count > MaxPlayers)
                    return false;
                decoded.Players = new GamePlayer[count];
                int slots = 0;
                for (int i = 0; i < count; i++)
                {
                    var player = new GamePlayer(reader.ReadUInt64())
                    {
                        Role = (MonsterRole)reader.ReadByte(),
                        Health = reader.ReadByte(),
                        Discoveries = reader.ReadUInt32(),
                        Respawn = reader.ReadUInt32(),
                        Slot = reader.ReadByte(),
                        Hit = reader.ReadUInt16(),
                        ImpulseX = reader.ReadSByte(),
                        ImpulseZ = reader.ReadSByte(),
                        CarrierSlot = reader.ReadByte()
                    };
                    if (player.Role > MonsterRole.Escaped || player.Health > 100 || player.Slot >= MaxPlayers ||
                        (player.CarrierSlot >= MaxPlayers && player.CarrierSlot != byte.MaxValue) ||
                        (slots & (1 << player.Slot)) != 0)
                        return false;
                    for (int previous = 0; previous < i; previous++)
                        if (decoded.Players[previous].Id == player.Id)
                            return false;
                    if (player.CarrierSlot != byte.MaxValue)
                    {
                        var target = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        if (!Finite(target.x, 1000f) || !Finite(target.y, 1000f) || !Finite(target.z, 1000f))
                            return false;
                        player.CarrierTarget = target;
                    }
                    slots |= 1 << player.Slot;
                    decoded.Players[i] = player;
                }
                int monsters = reader.ReadByte();
                if (monsters > 5)
                    return false;
                decoded.Monsters = new MonsterPose[monsters];
                for (int i = 0; i < monsters; i++)
                {
                    var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    float yaw = reader.ReadSingle();
                    if (!Finite(position.x, 1000f) || !Finite(position.y, 1000f) || !Finite(position.z, 1000f) ||
                        !Finite(yaw, 360f))
                        return false;
                    decoded.Monsters[i] = new MonsterPose(position, yaw, reader.ReadByte());
                }
                if (stream.Position != stream.Length)
                    return false;
                state = decoded;
                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        private static bool Finite(float value, float max) => Mathf.Abs(value) <= max;
    }

    internal sealed class GamePlayer
    {
        public GamePlayer(ulong id) => Id = id;
        public ulong Id { get; }
        public MonsterRole Role;
        public byte Health = 100, Slot, CarrierSlot = byte.MaxValue;
        public uint Discoveries, Respawn;
        public Vector3 CarrierTarget;
        public ushort Hit;
        public sbyte ImpulseX, ImpulseZ;
    }

    internal readonly struct MonsterPose
    {
        public MonsterPose(Vector3 position, float yaw, byte state)
        {
            Position = position;
            Yaw = yaw;
            State = state;
        }

        public Vector3 Position { get; }
        public float Yaw { get; }
        public byte State { get; }
    }
}
