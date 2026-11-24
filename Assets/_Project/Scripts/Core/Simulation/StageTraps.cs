using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Economy;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// ステージの仕掛け（StageTrap。ユーザーの要望、2026-10-26）の決まり。SimWorld が全員を進めた後に呼ぶ（Step。ティックを進めた後の
    /// ティック）。当たった人は StageZones.Strike で吹き飛び、威力の枚数のコインを落とす（同じティックの CoinDropRules.DropOnHazards）。
    /// - 赤外線（Tripwire）: 待っている間にだれかがレーダーに触れると、触れたティックを覚える。ArmTicks の予告の後に撃ち、ミサイルは
    ///   向きへまっすぐ飛ぶ（前のティックの位置から今の位置までに重なった人のうち、発射口に近い人に当たって消える）。撃ってから
    ///   FlightTicks で飛び終わり、さらに CooldownTicks で待つ状態に戻る。
    /// - ピッチャーマシン（Pitcher）: ボールの道のりは決まっている（StageTrap.TryGetBall）。ボールに重なった人は、バンパーのように
    ///   ボールの中心から外向きに弾き返される（BallBumpPower。ユーザーの要望、2026-10-26）。速さが MinHitSpeed 以上のボールなら、
    ///   その向きに吹き飛び、コインを落とす（ボールは人で止まらず、跳ね返りもしない。当たった人は危険の後の当たらない間に入るので、
    ///   続けては吹き飛ばない）。
    /// </summary>
    public static class StageTraps
    {
        /// <summary>仕掛けに当たった後、ほかのステージの危険に当たらないティック数【仮】。</summary>
        public const int StrikeImmunityTicks = 45;

        /// <summary>ボールに当たったときの、上向きに足す速さ（m/s）【仮】。</summary>
        public const float BallLift = 7f;

        /// <summary>ボールに触れたときに弾き返す速さ（m/s。バンパーと同じ決まり。StageZones.BumpAlong）【仮】。</summary>
        public const float BallBumpPower = 20f;

        /// <summary>ドーナツに触れたときに弾き返す速さ（m/s。バンパーと同じ決まり）【仮】。</summary>
        public const float DonutBumpPower = 18f;

        /// <summary>
        /// ドーナツのアンカーの、接続先の番号（WireState.AnchorBody）の始まり。動く足場は 1〜127、ドーナツは
        /// DonutBodyBase + 仕掛けの番号 × StageTrap.MaxDonuts + ドーナツの番号。
        /// </summary>
        public const int DonutBodyBase = 128;

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

        /// <summary>
        /// 赤外線の仕掛け index が、ティック tick に撃った後の待ち（ミサイルが飛んでいる間を含む。また触れられるようになるまで）か。
        /// そうなら、残り left と全体 total（ティック）。表示（赤外線を消して、発射装置に待ちの円形のゲージを出す）に使う。
        /// </summary>
        public static bool TryGetReload(StageTrap trap, StageState stage, int index, int tick, out int left, out int total)
        {
            total = trap.FlightTicks + trap.CooldownTicks;
            left = 0;
            int tripped = stage.TrapTickOf(index);
            if (trap.Kind != StageTrapKind.Tripwire || tripped <= 0)
            {
                return false;
            }
            int fired = tripped + trap.ArmTicks;
            if (tick < fired)
            {
                return false;
            }
            left = fired + total - tick;
            return left > 0;
        }

        private static Vector2 MissileAt(StageTrap trap, int age)
        {
            float distance = MathF.Min(trap.Range, trap.Speed * age * SimTime.DeltaTime);
            return trap.Muzzle + trap.Direction * distance;
        }

        /// <summary>仕掛けをティック tick（進めた後のティック）に進める。seed は試合の種（ドーナツのコースを選ぶ）。</summary>
        public static void Step(IReadOnlyList<StageTrap> traps, StageState stage, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            int tick, int seed)
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
                else if (trap.Kind == StageTrapKind.Pitcher)
                {
                    StepPitcher(trap, states, configs, tick);
                }
                else if (trap.Kind == StageTrapKind.DonutFryer)
                {
                    StepDonuts(trap, states, configs, tick, seed);
                }
                // 鬼火の輪はコインを渡すので、台帳のある StepWispRings で進める。
            }
        }

        /// <summary>
        /// 鬼火の輪をティック tick（進めた後のティック）に進める: くぐられて消えている輪は、また現れるティックになったら戻す。ある輪は、
        /// 試合中（active）なら、触れている人のうち輪の中心にいちばん近い人（同じなら番号の小さい人）に Reward 枚のコインを渡して消える
        /// （台帳の CoinEventKind.WispGift。くぐったティックと人を StageState に）。復帰を待っている人・メーデーで吊られている人・抜けた人は除く。
        /// </summary>
        public static void StepWispRings(IReadOnlyList<StageTrap> traps, StageState stage, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            CoinLedger ledger, int tick, int seed, bool active)
        {
            if (traps == null)
            {
                return;
            }
            for (int i = 0; i < traps.Count; i++)
            {
                StageTrap trap = traps[i];
                if (trap.Kind != StageTrapKind.WispRing)
                {
                    continue;
                }
                int taken = stage.TrapTickOf(i);
                if (taken > 0)
                {
                    if (tick < trap.RespawnTickAfter(taken, seed))
                    {
                        continue;
                    }
                    stage.SetTrapOwner(i, 0, -1);
                }
                if (!active)
                {
                    continue;
                }
                Vector2 at = trap.WispAt(tick, seed);
                int who = WispToucher(at, trap.HalfSize, states, configs);
                if (who >= 0 && ledger.Reward(new CoinEventId(tick, CoinEventKind.WispGift, i), states, who, trap.Reward))
                {
                    stage.SetTrapOwner(i, tick, who);
                }
            }
        }

        // 輪（中心 at、半分の大きさ half）に触れている人のうち、中心にいちばん近い人（いなければ -1）。
        private static int WispToucher(Vector2 at, Vector2 half, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (state.Combat.IsWaitingToRespawn || state.Combat.IsAway || state.Combat.HasLeft)
                {
                    continue;
                }
                float dx = state.Position.X - at.X;
                float dy = state.Position.Y - at.Y;
                if (MathF.Abs(dx) >= half.X + configs[c].Movement.HalfWidth || MathF.Abs(dy) >= half.Y + configs[c].Movement.HalfHeight)
                {
                    continue;
                }
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    best = c;
                    bestDistance = distance;
                }
            }
            return best;
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

        // ドーナツに触れた人を、ドーナツの中心から外向きに弾き返す（バンパーと同じ。ドーナツは丸いので、円に重なった人だけ）。
        private static void StepDonuts(StageTrap trap, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick, int seed)
        {
            float radius = trap.HalfSize.X;
            for (int slot = 0; slot < trap.DonutSlots; slot++)
            {
                if (!trap.TryGetDonut(tick, seed, slot, out _, out Vector2 donut, out _))
                {
                    continue;
                }
                for (int c = 0; c < states.Count; c++)
                {
                    CharacterState state = states[c];
                    if (!StageZones.CanBeHitByStage(state) || state.Combat.IsUntouchable)
                    {
                        continue;
                    }
                    Vector2 half = Half(configs[c]);
                    // 箱のいちばん近い点が、ドーナツの円の中か。
                    float nearX = MathF.Max(state.Position.X - half.X, MathF.Min(donut.X, state.Position.X + half.X));
                    float nearY = MathF.Max(state.Position.Y - half.Y, MathF.Min(donut.Y, state.Position.Y + half.Y));
                    float dx = nearX - donut.X;
                    float dy = nearY - donut.Y;
                    if (dx * dx + dy * dy > radius * radius)
                    {
                        continue;
                    }
                    var away = new Vector2(state.Position.X - donut.X, state.Position.Y - donut.Y);
                    float distance = away.Length();
                    Vector2 normal = distance > 1e-4f ? away / distance : Vector2.UnitY;
                    if (StageZones.BumpAlong(ref state, normal, DonutBumpPower))
                    {
                        states[c] = state;
                    }
                }
            }
        }

        /// <summary>
        /// ティック tick の初めに、出ているドーナツの真ん中をアンカーポイントにする（動く足場のアンカーの後に足す。次のティックの
        /// SetPlatforms で消える）。ワイヤーの接続先の番号は DonutBodyBase から。
        /// </summary>
        public static void AddAnchors(Objects.WorldCollision collision, IReadOnlyList<StageTrap> traps, int tick, int seed)
        {
            if (traps == null || collision == null)
            {
                return;
            }
            for (int i = 0; i < traps.Count; i++)
            {
                StageTrap trap = traps[i];
                if (trap.Kind != StageTrapKind.DonutFryer)
                {
                    continue;
                }
                for (int slot = 0; slot < trap.DonutSlots; slot++)
                {
                    if (trap.TryGetDonut(tick, seed, slot, out _, out Vector2 donut, out _))
                    {
                        collision.AddMovingAnchor(new Vector3(donut.X, donut.Y, 0f), trap.HalfSize.X, DonutBody(i, slot));
                    }
                }
            }
        }

        /// <summary>仕掛け trapIndex の番号 slot のドーナツの、ワイヤーの接続先の番号。</summary>
        public static int DonutBody(int trapIndex, int slot)
        {
            return DonutBodyBase + trapIndex * StageTrap.MaxDonuts + slot;
        }

        /// <summary>
        /// ティック tick の初めに、ドーナツにつないだワイヤーの接続点を、ドーナツの動いた分だけ動かす。ドーナツが消えた・ステージの外
        /// （bounds）へ出たら、ワイヤーを外す（つないだまま外へ引きずり出されない）。
        /// </summary>
        public static void CarryWires(IReadOnlyList<StageTrap> traps, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick,
            int seed, StageBounds? bounds)
        {
            if (traps == null)
            {
                return;
            }
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (state.Wire.Phase != Wire.WirePhase.Attached || state.Wire.AnchorBody < DonutBodyBase)
                {
                    continue;
                }
                int body = state.Wire.AnchorBody - DonutBodyBase;
                int index = body / StageTrap.MaxDonuts;
                int slot = body % StageTrap.MaxDonuts;
                Vector2 now = Vector2.Zero;
                int age = 0;
                bool alive = index < traps.Count && traps[index].Kind == StageTrapKind.DonutFryer
                    && traps[index].TryGetDonut(tick, seed, slot, out age, out now, out _);
                alive = alive && age > 0 && (!bounds.HasValue || bounds.Value.Contains(new Vector3(now.X, now.Y, 0f)));
                if (!alive)
                {
                    Wire.WireController.Release(ref state, Wire.WireReleaseReason.AnchorGone, configs[c].Wire);
                    states[c] = state;
                    continue;
                }
                traps[index].TryGetDonut(tick - 1, seed, slot, out _, out Vector2 before, out _);
                var move = new Vector3(now.X - before.X, now.Y - before.Y, 0f);
                state.Wire.Anchor += move;
                state.Wire.HookPosition += move;
                states[c] = state;
            }
        }

        private static void StepPitcher(StageTrap trap, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick)
        {
            if (!trap.TryGetBall(tick, out _, out Vector2 position, out Vector2 velocity, out bool blinking) || blinking)
            {
                return;
            }
            StrikeWithBall(trap, position, velocity, states, configs);
        }

        /// <summary>
        /// 位置 position・速度 velocity のピッチャーマシン trap のボールに重なった人を、弾き返すか（遅いボール）、吹き飛ばす（速いボール）。
        /// THIEF が置いたピッチャーマシンのボールにも使う（ThiefRules）。
        /// </summary>
        public static void StrikeWithBall(StageTrap trap, Vector2 position, Vector2 velocity, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, int sparedTeam = 0)
        {
            float speed = velocity.Length();
            bool fast = speed >= trap.MinHitSpeed && speed > 0f;
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (!StageZones.CanBeHitByStage(state) || state.Combat.IsUntouchable || (sparedTeam != 0 && state.Combat.Team == sparedTeam))
                {
                    // sparedTeam（チーム対戦で、ボールを打つ仕掛けを置いた人のチーム。D-81）の人には当たらない。
                    continue;
                }
                if (!Overlaps(state.Position, Half(configs[c]), position - trap.HalfSize, position + trap.HalfSize))
                {
                    continue;
                }
                // ボールの中心から外向き（真ん中に重なっていれば上）。
                var away = new Vector2(state.Position.X - position.X, state.Position.Y - position.Y);
                float distance = away.Length();
                Vector2 normal = distance > 1e-4f ? away / distance : Vector2.UnitY;
                if (fast && StageZones.CanBeStruck(state))
                {
                    var knock = new Vector3(normal.X * trap.Knockback, normal.Y * trap.Knockback + BallLift, 0f);
                    StageZones.Strike(ref state, knock, trap.Power, StrikeImmunityTicks, configs[c].Wire);
                }
                else if (!StageZones.BumpAlong(ref state, normal, BallBumpPower))
                {
                    continue;
                }
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

        /// <summary>線分 from→to が箱 min〜max と交わるか（交われば、from から最初に入るまでの割合 along）。</summary>
        public static bool SweptBox(Vector2 from, Vector2 to, Vector2 min, Vector2 max, out float along)
        {
            return Swept(from, to, min, max, out along);
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
