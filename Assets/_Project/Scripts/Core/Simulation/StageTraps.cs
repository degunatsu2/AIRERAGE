using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Common;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// ステージの仕掛け（StageTrap。ユーザーの要望、2026-10-26）の決まり。SimWorld が全員を進めた後に呼ぶ（Step。ティックを進めた後の
    /// ティック）。当たった人は StageZones.Strike で吹き飛び、威力の枚数のコインを落とす（同じティックの CoinDropRules.DropOnHazards）。
    /// - 赤外線（Tripwire）: 待っている間にだれかがレーダーに触れると、触れたティックを覚える。ArmTicks の予告の後に撃ち、ミサイルは
    ///   向きへまっすぐ飛ぶ（前のティックの位置から今の位置までに重なった人のうち、発射口に近い人に当たって消える）。撃ってから
    ///   FlightTicks で飛び終わり、さらに CooldownTicks で待つ状態に戻る。
    /// - ピッチャーマシン（Pitcher）: ボールの道のりは決まっている（StageTrap.TryGetBall）。速さが MinHitSpeed 以上のボールに重なった人が
    ///   吹き飛ぶ（ボールは止まらない。当たった人は危険の後の当たらない間に入るので、続けては当たらない）。
    /// </summary>
    public static class StageTraps
    {
        /// <summary>仕掛けに当たった後、ほかのステージの危険に当たらないティック数【仮】。</summary>
        public const int StrikeImmunityTicks = 45;

        /// <summary>ボールに当たったときの、上向きに足す速さ（m/s）【仮】。</summary>
        public const float BallLift = 7f;

        /// <summary>ミサイルに当たったときの、上向きに足す速さ（吹き飛びの速さに対する割合）【仮】。</summary>
        public const float MissileLiftRatio = 0.35f;

        /// <summary>
        /// 赤外線の仕掛け index の、ティック tick の様子: 予告（arming。触れられてから撃つまで）か、ミサイルが飛んでいるか（position）。
        /// どちらでもなければ false。
        /// </summary>
        public static bool TryGetMissile(StageTrap trap, StageState stage, int index, int tick, out bool arming, out Vector2 position)
        {
            arming = false;
            position = trap.Muzzle;
            int tripped = stage.TrapTickOf(index);
            if (trap.Kind != StageTrapKind.Tripwire || tripped <= 0)
            {
                return false;
            }
            int age = tick - (tripped + trap.ArmTicks);
            if (age < 0)
            {
                arming = true;
                return true;
            }
            if (stage.TrapSpentOf(index) || age > trap.FlightTicks)
            {
                return false;
            }
            position = MissileAt(trap, age);
            return true;
        }

        private static Vector2 MissileAt(StageTrap trap, int age)
        {
            float distance = MathF.Min(trap.Range, trap.Speed * age * SimTime.DeltaTime);
            return trap.Muzzle + trap.Direction * distance;
        }

        /// <summary>仕掛けをティック tick（進めた後のティック）に進める。</summary>
        public static void Step(IReadOnlyList<StageTrap> traps, StageState stage, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            int tick)
        {
            if (traps == null)
            {
                return;
            }
            for (int i = 0; i < traps.Count; i++)
            {
                StageTrap trap = traps[i];
                if (trap.Kind == StageTrapKind.Tripwire)
                {
                    StepTripwire(trap, stage, i, states, configs, tick);
                }
                else
                {
                    StepPitcher(trap, states, configs, tick);
                }
            }
        }

        private static void StepTripwire(StageTrap trap, StageState stage, int index, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            int tick)
        {
            int tripped = stage.TrapTickOf(index);
            if (tripped > 0 && tick >= tripped + trap.ArmTicks + trap.FlightTicks + trap.CooldownTicks)
            {
                stage.SetTrap(index, 0, false);
                tripped = 0;
            }
            if (tripped <= 0)
            {
                for (int c = 0; c < states.Count; c++)
                {
                    if (StageZones.CanBeStruck(states[c]) && Overlaps(states[c].Position, Half(configs[c]), trap.BeamMin, trap.BeamMax))
                    {
                        stage.SetTrap(index, tick, false);
                        return;
                    }
                }
                return;
            }
            int age = tick - (tripped + trap.ArmTicks);
            if (age <= 0 || age > trap.FlightTicks || stage.TrapSpentOf(index))
            {
                return;
            }
            // 前のティックの位置から今の位置まで（ミサイルの大きさだけ広げる）に重なった人のうち、発射口に近い人に当たる。
            Vector2 from = MissileAt(trap, age - 1);
            Vector2 to = MissileAt(trap, age);
            int victim = -1;
            float best = float.MaxValue;
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (!StageZones.CanBeStruck(state))
                {
                    continue;
                }
                Vector2 half = Half(configs[c]) + trap.HalfSize;
                var p = new Vector2(state.Position.X, state.Position.Y);
                if (!Swept(from, to, p - half, p + half, out float along))
                {
                    continue;
                }
                if (along < best)
                {
                    best = along;
                    victim = c;
                }
            }
            if (victim < 0)
            {
                return;
            }
            CharacterState hit = states[victim];
            var velocity = new Vector3(trap.Direction.X * trap.Knockback, trap.Direction.Y * trap.Knockback + trap.Knockback * MissileLiftRatio, 0f);
            StageZones.Strike(ref hit, velocity, trap.Power, StrikeImmunityTicks, configs[victim].Wire);
            states[victim] = hit;
            stage.SetTrap(index, tripped, true);
        }

        private static void StepPitcher(StageTrap trap, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick)
        {
            if (!trap.TryGetBall(tick, out _, out Vector2 position, out Vector2 velocity, out bool blinking) || blinking)
            {
                return;
            }
            float speed = velocity.Length();
            if (speed < trap.MinHitSpeed || !(speed > 0f))
            {
                return;
            }
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (!StageZones.CanBeStruck(state))
                {
                    continue;
                }
                if (!Overlaps(state.Position, Half(configs[c]), position - trap.HalfSize, position + trap.HalfSize))
                {
                    continue;
                }
                Vector2 direction = velocity / speed;
                var knock = new Vector3(direction.X * trap.Knockback, direction.Y * trap.Knockback + BallLift, 0f);
                StageZones.Strike(ref state, knock, trap.Power, StrikeImmunityTicks, configs[c].Wire);
                states[c] = state;
            }
        }

        private static Vector2 Half(CharacterConfig config)
        {
            return new Vector2(config.Movement.HalfWidth, config.Movement.HalfHeight);
        }

        private static bool Overlaps(Vector3 position, Vector2 half, Vector2 min, Vector2 max)
        {
            return position.X + half.X >= min.X && position.X - half.X <= max.X && position.Y + half.Y >= min.Y && position.Y - half.Y <= max.Y;
        }

        // 線分 from→to が箱 min〜max と交わるか（交われば、from から最初に入るまでの割合 along）。
        private static bool Swept(Vector2 from, Vector2 to, Vector2 min, Vector2 max, out float along)
        {
            float t0 = 0f;
            float t1 = 1f;
            along = 0f;
            if (!Clip(from.X, to.X - from.X, min.X, max.X, ref t0, ref t1) || !Clip(from.Y, to.Y - from.Y, min.Y, max.Y, ref t0, ref t1))
            {
                return false;
            }
            along = t0;
            return true;
        }

        private static bool Clip(float start, float delta, float min, float max, ref float t0, ref float t1)
        {
            if (delta == 0f)
            {
                return start >= min && start <= max;
            }
            float a = (min - start) / delta;
            float b = (max - start) / delta;
            if (a > b)
            {
                float swap = a;
                a = b;
                b = swap;
            }
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            return t0 <= t1;
        }
    }
}
