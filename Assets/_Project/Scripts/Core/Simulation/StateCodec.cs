using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Airerage.Core.Economy;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Wire;

namespace Airerage.Core.Simulation
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
        public const int FormatVersion = 26;

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

        /// <summary>
        /// ワールドの全状態（ティック数、全キャラクターの状態、ワールド全体のコイン、試合の進み具合、置かれた物）のチェックサム。
        /// objects が null なら、置かれた物はない（空の一覧と同じ値）。
        /// </summary>
        public static ulong WorldChecksum(int tick, IReadOnlyList<CharacterState> states, CoinField coins, Match.MatchState match,
            WorldObjects objects = null)
        {
            ulong hash = Mix(FnvOffsetBasis, tick);
            for (int i = 0; i < states.Count; i++)
            {
                hash = Mix(hash, Checksum(states[i]));
            }
            hash = Mix(hash, Checksum(coins));
            hash = Mix(hash, Checksum(objects));
            hash = Mix(hash, match.OvertimeStartTick);
            hash = Mix(hash, match.EndTick);
            return Mix(hash, match.Winner);
        }

        /// <summary>ワールド全体のコイン（フィールドのコイン、次の番号、生成・破棄の枚数）のチェックサム。</summary>
        public static ulong Checksum(CoinField coins)
        {
            if (coins == null)
            {
                throw new ArgumentNullException(nameof(coins));
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(coins.Coins.Count);
                foreach (FieldCoin coin in coins.Coins)
                {
                    writer.Write(coin.Id);
                    writer.Write(coin.Position.X);
                    writer.Write(coin.Position.Y);
                    writer.Write(coin.Value);
                    writer.Write(coin.PickableFromTick);
                    writer.Write(coin.ExpireTick);
                    writer.Write(coin.Source);
                    writer.Write((byte)coin.Origin);
                }
                writer.Write(coins.SpawnReadyTicks.Count);
                foreach (int ready in coins.SpawnReadyTicks)
                {
                    writer.Write(ready);
                }
                writer.Write(coins.NextCoinId);
                writer.Write(coins.Generated);
                writer.Write(coins.Destroyed);
                writer.Write(coins.Stored);
                writer.Flush();
                ulong hash = FnvOffsetBasis;
                foreach (byte b in stream.ToArray())
                {
                    hash = MixByte(hash, b);
                }
                return hash;
            }
        }

        /// <summary>置かれた物（番号の順のすべての値と、次の番号）のチェックサム。null は空の一覧と同じ。</summary>
        public static ulong Checksum(WorldObjects objects)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(objects != null ? objects.Count : 0);
                if (objects != null)
                {
                    foreach (WorldObject item in objects.Items)
                    {
                        writer.Write(item.Id);
                        writer.Write((byte)item.Kind);
                        writer.Write(item.Owner);
                        writer.Write((byte)item.Slot);
                        writer.Write(item.Position.X);
                        writer.Write(item.Position.Y);
                        writer.Write(item.Velocity.X);
                        writer.Write(item.Velocity.Y);
                        writer.Write(item.SpawnTick);
                        writer.Write(item.ExpireTick);
                        writer.Write(item.Damage);
                        writer.Write(item.ChargeMilli);
                        writer.Write(item.Stored);
                        writer.Write(item.HitMask);
                        writer.Write(item.BonusPercent);
                        writer.Write(item.LastPushId);
                        writer.Write(item.InPortalId);
                        writer.Write(item.End.X);
                        writer.Write(item.End.Y);
                        writer.Write(item.HitKey0);
                        writer.Write(item.HitKey1);
                        writer.Write(item.HitKey2);
                        writer.Write(item.HitKey3);
                        writer.Write(item.Variant);
                        writer.Write(item.Ended);
                    }
                }
                writer.Write(objects != null ? objects.NextId : 1);
                writer.Flush();
                ulong hash = FnvOffsetBasis;
                foreach (byte b in stream.ToArray())
                {
                    hash = MixByte(hash, b);
                }
                return hash;
            }
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
            writer.Write(wire.Whip);

            ActionState action = state.Action;
            writer.Write((byte)action.Kind);
            writer.Write((byte)action.Attack);
            writer.Write(action.Ticks);
            writer.Write(action.Duration);
            writer.Write(action.HitMask);
            writer.Write(action.StrikeSpeed);
            writer.Write(action.Connected);
            writer.Write(action.CancelFromTick);
            writer.Write(action.EffectDone);
            writer.Write(action.Charge);
            writer.Write(action.Aim.X);
            writer.Write(action.Aim.Y);
            writer.Write(action.RunSpeed);
            writer.Write(action.TurnTicksLeft);
            writer.Write(action.TickRemainder);
            writer.Write(action.Advance);
            writer.Write(action.LastHitTicks);
            writer.Write(action.Recast);

            CombatState combat = state.Combat;
            writer.Write(combat.HitstopTicksLeft);
            writer.Write(combat.HasLastAttacker);
            writer.Write(combat.LastAttacker);
            writer.Write(combat.LastDamagedTick);
            writer.Write(combat.ComboCount);
            writer.Write(combat.ComboDamage);
            writer.Write(combat.Knockouts);
            writer.Write(combat.Falls);
            writer.Write(combat.AttackCancelUsed);
            writer.Write(combat.WireCancelCooldownTicksLeft);
            writer.Write(combat.AttackBufferTicksLeft);
            writer.Write((byte)combat.BufferedAttack);
            writer.Write(combat.BufferedFacing);
            writer.Write(combat.Gauge);
            writer.Write(combat.Special1CooldownTicksLeft);
            writer.Write(combat.Special2CooldownTicksLeft);
            writer.Write(combat.GuardCooldownTicksLeft);
            writer.Write(combat.BounceCount);
            writer.Write(combat.FatalHitCount);
            writer.Write(combat.JustGuardCount);
            writer.Write(combat.GuardedCount);
            writer.Write((ushort)combat.HitstopPresses);
            writer.Write(combat.RespawnTicksLeft);
            writer.Write(combat.InvincibleTicksLeft);
            writer.Write(combat.RewardDamage);
            writer.Write(combat.RewardAttacker);
            writer.Write(combat.Holding);
            writer.Write((byte)combat.HoldSlot);
            writer.Write(combat.HoldTicks);
            writer.Write(combat.AttackSerial);
            writer.Write(combat.CoinBoostTicksLeft);
            writer.Write(combat.CoinBonusMilli);
            writer.Write(combat.JackpotHitCount);
            writer.Write(combat.GaugeBonusRemainder);
            writer.Write(combat.SpeedBoostTicksLeft);
            writer.Write(combat.SkyBoostTicksLeft);
            writer.Write(combat.Special1Spent);
            writer.Write(combat.Special2Spent);
            writer.Write(combat.SkillAim.X);
            writer.Write(combat.SkillAim.Y);
            writer.Write(combat.SlowFallSerial);
            writer.Write(combat.PortalCount);
            writer.Write(combat.InPortalId);
            writer.Write(combat.WarpCooldownTicks);
            writer.Write(combat.WarpCount);
            writer.Write(combat.FrozenTicksLeft);
            writer.Write(combat.FreezeCount);
            writer.Write(combat.HazardImmunityTicks);
            writer.Write(combat.HazardHitCount);
            writer.Write(combat.HazardDropPower);
            writer.Write(combat.HoldVariant);
            writer.Write(combat.CreamSpeedPercent);
            writer.Write(combat.CreamWallJumpPercent);
            writer.Write(combat.CreamNoCling);
            writer.Write(combat.CreamStickTicksLeft);
            writer.Write(combat.CreamStickSpent);
            writer.Write(combat.ChainCount);
            writer.Write(combat.ChainTicksLeft);
            writer.Write(combat.InverterOn);
            writer.Write(combat.StunTicksLeft);
            writer.Write(combat.StunGuardTicks);
            writer.Write(combat.StunCount);
            writer.Write(combat.WhipGuardTicks);
            writer.Write(combat.ShieldBlockCount);
            writer.Write(combat.HasLeft);
            writer.Write(combat.WaveCooldownTicks);

            writer.Write(state.JumpPadCount);
            writer.Write(state.BoostRingCount);
            writer.Write(state.WallSide);
            writer.Write(state.WallClingTicks);
            writer.Write(state.WallJumpCount);
            writer.Write(state.Coins.Held);
            writer.Write(state.Coins.Banked);
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
                Whip = reader.ReadBoolean(),
            };
            state.Action = new ActionState
            {
                Kind = (ActionKind)reader.ReadByte(),
                Attack = (AttackSlot)reader.ReadByte(),
                Ticks = reader.ReadInt32(),
                Duration = reader.ReadInt32(),
                HitMask = reader.ReadUInt32(),
                StrikeSpeed = reader.ReadSingle(),
                Connected = reader.ReadBoolean(),
                CancelFromTick = reader.ReadInt32(),
                EffectDone = reader.ReadBoolean(),
                Charge = reader.ReadInt32(),
                Aim = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle()),
                RunSpeed = reader.ReadSingle(),
                TurnTicksLeft = reader.ReadInt32(),
                TickRemainder = reader.ReadInt32(),
                Advance = reader.ReadInt32(),
                LastHitTicks = reader.ReadInt32(),
                Recast = reader.ReadBoolean(),
            };
            state.Combat = new CombatState
            {
                HitstopTicksLeft = reader.ReadInt32(),
                HasLastAttacker = reader.ReadBoolean(),
                LastAttacker = reader.ReadByte(),
                LastDamagedTick = reader.ReadInt32(),
                ComboCount = reader.ReadInt32(),
                ComboDamage = reader.ReadInt32(),
                Knockouts = reader.ReadInt32(),
                Falls = reader.ReadInt32(),
                AttackCancelUsed = reader.ReadBoolean(),
                WireCancelCooldownTicksLeft = reader.ReadInt32(),
                AttackBufferTicksLeft = reader.ReadInt32(),
                BufferedAttack = (AttackSlot)reader.ReadByte(),
                BufferedFacing = reader.ReadSByte(),
                Gauge = reader.ReadInt32(),
                Special1CooldownTicksLeft = reader.ReadInt32(),
                Special2CooldownTicksLeft = reader.ReadInt32(),
                GuardCooldownTicksLeft = reader.ReadInt32(),
                BounceCount = reader.ReadInt32(),
                FatalHitCount = reader.ReadInt32(),
                JustGuardCount = reader.ReadInt32(),
                GuardedCount = reader.ReadInt32(),
                HitstopPresses = (InputButtons)reader.ReadUInt16(),
                RespawnTicksLeft = reader.ReadInt32(),
                InvincibleTicksLeft = reader.ReadInt32(),
                RewardDamage = reader.ReadInt32(),
                RewardAttacker = reader.ReadByte(),
                Holding = reader.ReadBoolean(),
                HoldSlot = (AttackSlot)reader.ReadByte(),
                HoldTicks = reader.ReadInt32(),
                AttackSerial = reader.ReadInt32(),
                CoinBoostTicksLeft = reader.ReadInt32(),
                CoinBonusMilli = reader.ReadInt32(),
                JackpotHitCount = reader.ReadInt32(),
                GaugeBonusRemainder = reader.ReadInt32(),
                SpeedBoostTicksLeft = reader.ReadInt32(),
                SkyBoostTicksLeft = reader.ReadInt32(),
                Special1Spent = reader.ReadInt32(),
                Special2Spent = reader.ReadInt32(),
                SkillAim = new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle()),
                SlowFallSerial = reader.ReadInt32(),
                PortalCount = reader.ReadInt32(),
                InPortalId = reader.ReadInt32(),
                WarpCooldownTicks = reader.ReadInt32(),
                WarpCount = reader.ReadInt32(),
                FrozenTicksLeft = reader.ReadInt32(),
                FreezeCount = reader.ReadInt32(),
                HazardImmunityTicks = reader.ReadInt32(),
                HazardHitCount = reader.ReadInt32(),
                HazardDropPower = reader.ReadInt32(),
                HoldVariant = reader.ReadByte(),
                CreamSpeedPercent = reader.ReadInt32(),
                CreamWallJumpPercent = reader.ReadInt32(),
                CreamNoCling = reader.ReadBoolean(),
                CreamStickTicksLeft = reader.ReadInt32(),
                CreamStickSpent = reader.ReadBoolean(),
                ChainCount = reader.ReadInt32(),
                ChainTicksLeft = reader.ReadInt32(),
                InverterOn = reader.ReadBoolean(),
                StunTicksLeft = reader.ReadInt32(),
                StunGuardTicks = reader.ReadInt32(),
                StunCount = reader.ReadInt32(),
                WhipGuardTicks = reader.ReadInt32(),
                ShieldBlockCount = reader.ReadInt32(),
                HasLeft = reader.ReadBoolean(),
                WaveCooldownTicks = reader.ReadInt32(),
            };
            state.JumpPadCount = reader.ReadInt32();
            state.BoostRingCount = reader.ReadInt32();
            state.WallSide = reader.ReadInt32();
            state.WallClingTicks = reader.ReadInt32();
            state.WallJumpCount = reader.ReadInt32();
            state.Coins = new CoinPurse { Held = reader.ReadInt32(), Banked = reader.ReadInt32() };
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
