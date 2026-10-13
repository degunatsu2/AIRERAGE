using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Input;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Simulation
{
    /// <summary>
    /// CharacterState をバイト列にする（入力の記録の保存、PC 間の比較に使う）。
    /// 浮動小数点はビットのまま書くので、復元すると元と1ビットも違わない。
    /// チェックサムはこのバイト列から計算する（プロセスや PC が違っても同じ値になる。HashCode とは違う）。
    /// フィールドを追加したら、Write と Read の両方に加え、FormatVersion を上げる。
    /// </summary>
    public static class StateCodec
    {
        /// <summary>バイト列の形式の版。</summary>
        public const int FormatVersion = 5;

        /// <summary>FNV-1a 64bit の初期値。</summary>
        public const ulong FnvOffsetBasis = 14695981039346656037UL;

        private const ulong FnvPrime = 1099511628211UL;

        public static byte[] ToBytes(in CharacterState state)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                Write(writer, state);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static CharacterState FromBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                CharacterState state = Read(reader);
                if (stream.Position != stream.Length)
                {
                    throw new FormatException($"状態のバイト列の長さが合いません（{stream.Length} バイト中 {stream.Position} バイトを読んだ）");
                }
                return state;
            }
        }

        /// <summary>状態のチェックサム（FNV-1a 64bit）。</summary>
        public static ulong Checksum(in CharacterState state)
        {
            ulong hash = FnvOffsetBasis;
            foreach (byte b in ToBytes(state))
            {
                hash = MixByte(hash, b);
            }
            return hash;
        }

        /// <summary>ワールドの全状態（ティック数と全キャラクターの状態）のチェックサム。</summary>
        public static ulong WorldChecksum(int tick, IReadOnlyList<CharacterState> states)
        {
            ulong hash = Mix(FnvOffsetBasis, tick);
            for (int i = 0; i < states.Count; i++)
            {
                hash = Mix(hash, Checksum(states[i]));
            }
            return hash;
        }

        /// <summary>チェックサム hash に 64bit の値を加える（下位のバイトから順に FNV-1a で混ぜる）。</summary>
        public static ulong Mix(ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash = MixByte(hash, (byte)(value >> (8 * i)));
            }
            return hash;
        }

        /// <summary>チェックサム hash に 32bit の値を加える。</summary>
        public static ulong Mix(ulong hash, int value)
        {
            for (int i = 0; i < 4; i++)
            {
                hash = MixByte(hash, (byte)(value >> (8 * i)));
            }
            return hash;
        }

        private static ulong MixByte(ulong hash, byte value)
        {
            hash ^= value;
            return hash * FnvPrime;
        }

        public static void Write(BinaryWriter writer, in CharacterState state)
        {
            Write(writer, state.Position);
            Write(writer, state.Velocity);
            writer.Write(state.Facing);
            writer.Write(state.Grounded);
            writer.Write(state.CoyoteTicksLeft);
            writer.Write(state.JumpBufferTicksLeft);
            writer.Write(state.AirJumpsUsed);
            writer.Write((ushort)state.PreviousButtons);

            WireState wire = state.Wire;
            writer.Write((byte)wire.Phase);
            writer.Write(wire.PhaseTicks);
            Write(writer, wire.HookPosition);
            Write(writer, wire.HookDirection);
            writer.Write(wire.HookTraveled);
            writer.Write(wire.HasTargetPoint);
            Write(writer, wire.TargetPoint);
            Write(writer, wire.Anchor);
            RopePivots pivots = wire.Pivots;
            writer.Write(pivots.Count);
            Write(writer, pivots.P0);
            Write(writer, pivots.P1);
            Write(writer, pivots.P2);
            Write(writer, pivots.P3);
            writer.Write(pivots.Clockwise);
            writer.Write(wire.RopeLength);
            writer.Write(wire.AttachSpeed);
            writer.Write(wire.CooldownTicksLeft);
            writer.Write((byte)wire.LastReleaseReason);
            writer.Write((byte)wire.LastMissReason);

            ActionState action = state.Action;
            writer.Write((byte)action.Kind);
            writer.Write((byte)action.Attack);
            writer.Write(action.Ticks);
            writer.Write(action.Duration);
            writer.Write(action.HitMask);
            writer.Write(action.StrikeSpeed);

            CombatState combat = state.Combat;
            writer.Write(combat.HitstopTicksLeft);
            writer.Write(combat.HasLastAttacker);
            writer.Write(combat.LastAttacker);
            writer.Write(combat.LastDamagedTick);
            writer.Write(combat.ComboCount);
            writer.Write(combat.ComboDamage);
        }

        public static CharacterState Read(BinaryReader reader)
        {
            var state = new CharacterState
            {
                Position = ReadVector3(reader),
                Velocity = ReadVector3(reader),
                Facing = reader.ReadInt32(),
                Grounded = reader.ReadBoolean(),
                CoyoteTicksLeft = reader.ReadInt32(),
                JumpBufferTicksLeft = reader.ReadInt32(),
                AirJumpsUsed = reader.ReadInt32(),
                PreviousButtons = (InputButtons)reader.ReadUInt16(),
            };
            state.Wire = new WireState
            {
                Phase = (WirePhase)reader.ReadByte(),
                PhaseTicks = reader.ReadInt32(),
                HookPosition = ReadVector3(reader),
                HookDirection = ReadVector3(reader),
                HookTraveled = reader.ReadSingle(),
                HasTargetPoint = reader.ReadBoolean(),
                TargetPoint = ReadVector3(reader),
                Anchor = ReadVector3(reader),
                Pivots = new RopePivots
                {
                    Count = reader.ReadByte(),
                    P0 = ReadVector3(reader),
                    P1 = ReadVector3(reader),
                    P2 = ReadVector3(reader),
                    P3 = ReadVector3(reader),
                    Clockwise = reader.ReadByte(),
                },
                RopeLength = reader.ReadSingle(),
                AttachSpeed = reader.ReadSingle(),
                CooldownTicksLeft = reader.ReadInt32(),
                LastReleaseReason = (WireReleaseReason)reader.ReadByte(),
                LastMissReason = (WireMissReason)reader.ReadByte(),
            };
            state.Action = new ActionState
            {
                Kind = (ActionKind)reader.ReadByte(),
                Attack = (AttackSlot)reader.ReadByte(),
                Ticks = reader.ReadInt32(),
                Duration = reader.ReadInt32(),
                HitMask = reader.ReadUInt32(),
                StrikeSpeed = reader.ReadSingle(),
            };
            state.Combat = new CombatState
            {
                HitstopTicksLeft = reader.ReadInt32(),
                HasLastAttacker = reader.ReadBoolean(),
                LastAttacker = reader.ReadByte(),
                LastDamagedTick = reader.ReadInt32(),
                ComboCount = reader.ReadInt32(),
                ComboDamage = reader.ReadInt32(),
            };
            return state;
        }

        private static void Write(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            return new Vector3(x, y, z);
        }
    }
}
