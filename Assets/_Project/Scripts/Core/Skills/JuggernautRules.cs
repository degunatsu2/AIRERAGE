using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Skills
{
    /// <summary>
    /// JUGGERNAUT の技の規則（ユーザーの案、2026-10-29。docs/14 1.7。値は JuggernautTuning）。
    /// - 空中横 CANNON SHOT: 前へ、重力を受けない砲弾（WorldObjectKind.Shell、CannonShell）を撃つ。最初に当たった人に当たって消え、
    ///   地形・届く距離の終わりで消える。
    /// - Q FLAK: 地上で Q を押すと構える（CombatState.FlakStance。回数は使わない。ActionController.StartAttack が EnterStance を呼ぶ）。
    ///   構えの間に Q を押すと、照準の方向へ重力を受ける砲弾（FlakShell）を撃つ（1 回分を使う）。砲弾はキャラクター・地形・壊せる物に
    ///   触れると爆発する。上下で弾道の段階（弱・中・強・弩）を選び、段階はその試合の間覚えておく（FlakLevelShift。撃破されても残す）。
    ///   回数がなくなるか、Q 以外の行動（ジャンプ・攻撃・ワイヤー・ガード・E・R・左右の移動）か、空中に出る・のけぞると、構えは解ける
    ///   （UpdateStance）。
    /// - E MINE TRAP: 照準の方向へ地雷（Mine）を撃つ（重力で山なりに飛ぶ。2026-10-29）。地形（動く足場を含む）かキャラクターに張り付いて一緒に動き（ユーザーの回答、
    ///   2026-10-29）、もう一度 E で爆発する。クールタイムは、爆発した（消えた）後から数える。
    /// - R MAYDAY: ジェット機に吊られて飛び去り（MaydayLift）、ステージ全体から空いた場所を選び（MaydaySelect。入力の照準に場所を入れる。
    ///   EncodeTarget）、そこへ運ばれる（MaydayCarry）。その間は無敵で、その場にいない（CombatState.IsAway: 攻撃・コイン・拠点・撃破・
    ///   足場に触れない）。選ぶ時間が過ぎたら、元の場所へ戻る（ユーザーの回答、2026-10-29）。地形と重なる場所は選べない
    ///   （MaydayRejectCount を増やす。表示が N/A とエラーの音を出す）。位置は、吊られている間は元の場所、運ばれている間は選んだ場所の
    ///   まま（上下に動いて見えるのは表示だけ）。
    /// 爆発は、当たった後もしばらく残る印（ShellBlast・MineBlast。BlastTicks）にする（表示が場所と時刻を読む。当たるのは爆発したティック
    /// だけ）。物は番号の順、キャラクターは番号の順に調べる（結果が順番で変わらない）。
    /// </summary>
    public static class JuggernautRules
    {
        /// <summary>メーデーの段階（CombatState.MaydayPhase。0 ならしていない）。</summary>
        public const byte MaydayLift = 1;
        public const byte MaydaySelect = 2;
        public const byte MaydayCarry = 3;

        /// <summary>メーデーで場所を決めるボタン（攻撃・ジャンプ・R）。</summary>
        public const InputButtons MaydayConfirmButtons = InputButtons.Attack | InputButtons.Jump | InputButtons.Super;

        /// <summary>砲弾（WorldObjectKind.Shell）の Variant。</summary>
        public const byte CannonShell = 0;
        public const byte FlakShell = 1;
        public const byte ShellBlast = 2;

        /// <summary>地雷（WorldObjectKind.Mine）の Variant。</summary>
        public const byte MineFlying = 0;
        public const byte MineOnTerrain = 1;
        public const byte MineOnCharacter = 2;
        public const byte MineBlast = 3;

        // 構えの間に、上下で段階を変える・左右で構えを解く、スティックの倒し具合。
        private const float StanceAxisThreshold = 0.5f;

        // 構えを解く、Q 以外のボタン。
        private const InputButtons StanceBreakers = InputButtons.Jump | InputButtons.Attack | InputButtons.Wire | InputButtons.Guard
            | InputButtons.Special2 | InputButtons.Super;

        /// <summary>JUGGERNAUT（R がメーデー）か。</summary>
        public static bool IsJuggernaut(CharacterConfig config)
        {
            return config != null && config.Combat.Super.Effect == SkillEffect.Mayday;
        }

        /// <summary>JUGGERNAUT の効果か（SkillRules.FireEffects がここへ回す）。</summary>
        public static bool Handles(SkillEffect effect)
        {
            return effect == SkillEffect.Cannon || effect == SkillEffect.Flak || effect == SkillEffect.Mine || effect == SkillEffect.Mayday;
        }

        // ---------------------------------------------------------------- Q フラックの構え

        /// <summary>フラックを構えられる・撃てるか（地上で、ワイヤーを撃っていない・つないでいない。ActionController.CanUse）。</summary>
        public static bool CanFlak(in CharacterState state)
        {
            return state.Grounded && !state.Combat.IsAway && state.Wire.Phase != WirePhase.Attached && state.Wire.Phase != WirePhase.Firing;
        }

        /// <summary>
        /// 照準を毎ティック覚えておく間か（メーデーで場所を選んでいる間。照準に場所が入っている）。オンラインでは、この間は照準を送る
        /// （ActionController.TracksAimNow → InputNormalizer）。
        /// </summary>
        public static bool TracksAim(in CharacterState state)
        {
            return state.Combat.MaydayPhase == MaydaySelect;
        }

        /// <summary>構える（フラックの Q を、構えていないときに押した。ActionController.StartAttack から）。回数は使わない。</summary>
        public static void EnterStance(ref CharacterState state, in InputFrame input)
        {
            state.Combat.FlakStance = true;
            // 押したときに上下を入れていても、段階は変えない（入れ直したら変える）。
            state.Combat.FlakAxis = AxisOf(input.Move);
            state.Combat.AttackBufferTicksLeft = 0;
        }

        /// <summary>
        /// 構えの間の毎ティック（ActionController.Update から、攻撃を始める前に）: 解ける条件なら解き、上下を入れ直したら弾道の段階を変える。
        /// 撃っている間（フラックの攻撃）は構えたまま。
        /// </summary>
        public static void UpdateStance(ref CharacterState state, in InputFrame input, CharacterConfig config)
        {
            ref CombatState c = ref state.Combat;
            if (!c.FlakStance)
            {
                return;
            }
            CombatTuning combat = config.Combat;
            bool shooting = state.Action.Kind == ActionKind.Attack && state.Action.Attack == AttackSlot.Special1;
            bool free = state.Action.IsFree || shooting;
            Vector2 move = input.Move;
            bool walking = MathF.Abs(move.X) >= StanceAxisThreshold && MathF.Abs(move.X) >= MathF.Abs(move.Y);
            bool spent = combat.Special1MaxCharges > 1 ? c.Special1Spent >= combat.Special1MaxCharges : c.Special1CooldownTicksLeft != 0;
            if (combat.Special1.Effect != SkillEffect.Flak || !CanFlak(state) || !free || c.Holding || walking
                || (input.Pressed & StanceBreakers) != 0 || (spent && !shooting))
            {
                c.FlakStance = false;
                c.FlakAxis = 0;
                return;
            }
            sbyte axis = AxisOf(move);
            if (axis != 0 && axis != c.FlakAxis)
            {
                JuggernautTuning tuning = config.Skills.Juggernaut;
                int level = Math.Clamp(FlakLevel(state, tuning) + axis, 0, tuning.FlakLevels - 1);
                c.FlakLevelShift = (sbyte)(level - tuning.FlakDefaultLevel);
            }
            c.FlakAxis = axis;
        }

        /// <summary>今の弾道の段階（0 = 弱 … FlakLevels - 1 = 弩）。</summary>
        public static int FlakLevel(in CharacterState state, JuggernautTuning tuning)
        {
            return Math.Clamp(tuning.FlakDefaultLevel + state.Combat.FlakLevelShift, 0, Math.Max(0, tuning.FlakLevels - 1));
        }

        /// <summary>
        /// フラックの砲弾を照準 aim で撃つときの、出る位置（砲塔の根もと）と、最初のティックに加えて進む砲身の分（lead）と、速度（撃つときと、
        /// 表示の予測線で同じものを使う）。照準がなければ（長さがワイヤーの照準の遊びより小さければ）、向いている方の斜め 45 度上へ。
        /// 砲身の先から出さずに根もとから砲身の分を進めるのは、砲身の先が地形に入っていても、地形を抜けずに当たるように（地面へ向けて
        /// 撃ったときなど）。
        /// </summary>
        public static void FlakLaunch(in CharacterState state, CharacterConfig config, Vector2 aim, out Vector2 origin, out Vector2 lead,
            out Vector2 velocity)
        {
            JuggernautTuning tuning = config.Skills.Juggernaut;
            Vector2 direction = aim.Length() >= config.Wire.AimDeadzone && aim.Length() > SimMath.Epsilon
                ? Vector2.Normalize(aim)
                : Vector2.Normalize(new Vector2(state.Facing >= 0 ? 1f : -1f, 1f));
            origin = new Vector2(state.Position.X, state.Position.Y + tuning.FlakMuzzleHeight);
            lead = direction * tuning.FlakMuzzleDistance;
            velocity = direction * tuning.FlakSpeedAt(FlakLevel(state, tuning));
        }

        /// <summary>フラックの砲弾の 1 ティックの移動（重力をかけてから進める。velocity も進める）。</summary>
        public static Vector2 FlakStep(ref Vector2 velocity, float gravity)
        {
            velocity.Y -= gravity * SimTime.DeltaTime;
            return velocity * SimTime.DeltaTime;
        }

        /// <summary>
        /// フラックの砲弾の道のり（表示の予測線）: origin から velocity で撃ったとき（最初のティックは lead も進む。FlakLaunch）の、
        /// ティックごとの位置を points に入れて数を返す。地形に当たったら、当たった点で終える（hit が true）。
        /// </summary>
        public static int PredictFlak(ICollisionQuery terrain, Vector2 origin, Vector2 lead, Vector2 velocity, float gravity, Vector2[] points,
            out bool hit)
        {
            hit = false;
            if (points == null || points.Length == 0)
            {
                return 0;
            }
            int count = 0;
            Vector2 position = origin;
            points[count++] = position;
            while (count < points.Length)
            {
                Vector2 delta = FlakStep(ref velocity, gravity) + lead;
                lead = Vector2.Zero;
                if (Cast(terrain, position, delta, out Vector2 point, out _))
                {
                    points[count++] = point;
                    hit = true;
                    break;
                }
                position += delta;
                points[count++] = position;
            }
            return count;
        }

        // ---------------------------------------------------------------- 効果を起こす

        /// <summary>JUGGERNAUT の技の効果を起こす（SkillRules.FireEffects から。tick は進めた後のティック）。</summary>
        public static void Fire(SkillEffect effect, WorldObjects objects, List<CharacterState> states, int index, CharacterConfig config, int tick)
        {
            CharacterState state = states[index];
            JuggernautTuning tuning = config.Skills.Juggernaut;
            var position = new Vector2(state.Position.X, state.Position.Y);
            switch (effect)
            {
                case SkillEffect.Cannon:
                {
                    // 体の中から出し、最初のティックに砲身の分（End）も進む（砲身の先が壁に入っていても、壁を抜けない）。
                    float step = tuning.CannonSpeed * SimTime.DeltaTime;
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.Shell, Variant = CannonShell, Owner = index, Slot = state.Action.Attack,
                        Position = position + new Vector2(0f, tuning.CannonOffsetY), End = new Vector2(tuning.CannonOffsetX * state.Facing, 0f),
                        Velocity = new Vector2(tuning.CannonSpeed * state.Facing, 0f),
                        SpawnTick = tick, ExpireTick = tick + Math.Max(1, (int)MathF.Ceiling(tuning.CannonRange / step)),
                    });
                    break;
                }

                case SkillEffect.Flak:
                {
                    FlakLaunch(state, config, state.Action.Aim, out Vector2 origin, out Vector2 lead, out Vector2 velocity);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.Shell, Variant = FlakShell, Owner = index, Slot = state.Action.Attack,
                        Position = origin, End = lead, Velocity = velocity, SpawnTick = tick, ExpireTick = tick + tuning.FlakLifetimeTicks,
                    });
                    state.Combat.FlakShotCount++;
                    states[index] = state;
                    break;
                }

                case SkillEffect.Mine:
                {
                    // 体の中から出し、最初のティックに MineLaunchOffset も進む（足もとへ向けても、地面を抜けずに張り付く）。
                    Vector2 direction = SkillRules.AimDirection(state.Action.Aim, state.Facing, config.Wire.AimDeadzone);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.Mine, Variant = MineFlying, Owner = index, Slot = state.Action.Attack,
                        Position = position, End = direction * tuning.MineLaunchOffset, Velocity = direction * tuning.MineSpeed,
                        SpawnTick = tick, ExpireTick = int.MaxValue,
                    });
                    // クールタイムは、爆発した（消えた）後から数える（ユーザーの案）。
                    SetCooldown(ref state.Combat, state.Action.Attack, CombatState.CooldownWhileActive);
                    states[index] = state;
                    break;
                }

                case SkillEffect.Mayday:
                    StartMayday(ref state, config);
                    states[index] = state;
                    break;
            }
        }

        // ---------------------------------------------------------------- 物（砲弾・地雷）

        /// <summary>
        /// JUGGERNAUT の物を進める（SkillRules.StepObjects の後に呼ぶ。寿命は SkillRules が決める。投射物の命中は hits に加える）。
        /// terrain は地形（動く足場を含む）。platforms と stage は、動く足場に張り付いた地雷を動かすのに使う（null なら動かさない）。
        /// pressed はこのティックに押したボタン（キャラクターの番号順。もう一度の E で地雷が爆発する）。
        /// </summary>
        public static void StepObjects(WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            StageBounds? bounds, int tick, List<HitRecord> hits, IReadOnlyList<StagePlatform> platforms, StageState stage,
            IReadOnlyList<InputButtons> pressed)
        {
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Ended || (item.Kind != WorldObjectKind.Shell && item.Kind != WorldObjectKind.Mine))
                {
                    continue;
                }
                JuggernautTuning tuning = configs[item.Owner].Skills.Juggernaut;
                if (item.Kind == WorldObjectKind.Shell)
                {
                    StepShell(ref item, objects, terrain, states, configs, bounds, tuning, hits, tick);
                }
                else
                {
                    bool detonate = pressed != null && item.Owner < pressed.Count && (pressed[item.Owner] & InputButtons.Special2) != 0;
                    StepMine(ref item, objects, terrain, states, configs, bounds, tuning, hits, tick, platforms, stage, detonate);
                }
                objects[o] = item;
            }
        }

        /// <summary>爆発の印か（砲弾・地雷が爆発した後、表示のために残っている間）。</summary>
        public static bool IsBlast(in WorldObject item)
        {
            return (item.Kind == WorldObjectKind.Shell && item.Variant == ShellBlast) || (item.Kind == WorldObjectKind.Mine && item.Variant == MineBlast);
        }

        /// <summary>飛んでいる砲弾・地雷か（ボウルで防げる。張り付いた地雷と爆発の印は飛んでいない）。</summary>
        public static bool IsFlying(in WorldObject item)
        {
            return (item.Kind == WorldObjectKind.Shell && item.Variant != ShellBlast) || (item.Kind == WorldObjectKind.Mine && item.Variant == MineFlying);
        }

        // 砲弾: 空中横の砲弾はまっすぐ飛び、最初に当たった人に当たって消える（地形で消える）。フラックは重力で落ちながら飛び、
        // キャラクター・地形・壊せる物に触れると爆発する。ステージの外で消える。
        private static void StepShell(ref WorldObject shell, WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, StageBounds? bounds, JuggernautTuning tuning, List<HitRecord> hits, int tick)
        {
            if (shell.Variant == ShellBlast)
            {
                return;
            }
            bool flak = shell.Variant == FlakShell;
            float radius = flak ? tuning.FlakRadius : tuning.CannonRadius;
            Vector2 delta = (flak ? FlakStep(ref shell.Velocity, tuning.FlakGravity) : shell.Velocity * SimTime.DeltaTime) + TakeLead(ref shell);
            if (Cast(terrain, shell.Position, delta, out Vector2 point, out _))
            {
                shell.Position = point;
                if (flak)
                {
                    Explode(ref shell, objects, states, configs, tuning.FlakExplosionRadius, tuning.FlakHit, hits, tick, tuning, ShellBlast);
                }
                else
                {
                    shell.Ended = true;
                }
                return;
            }
            shell.Position += delta;
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(shell.Position.X, shell.Position.Y, 0f)))
            {
                shell.Ended = true;
                return;
            }
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == shell.Owner || target.Combat.IsUntouchable || !Overlaps(target, configs[v], shell.Position, radius))
                {
                    continue;
                }
                if (flak)
                {
                    Explode(ref shell, objects, states, configs, tuning.FlakExplosionRadius, tuning.FlakHit, hits, tick, tuning, ShellBlast);
                }
                else
                {
                    int facing = shell.Velocity.X > 0f ? 1 : shell.Velocity.X < 0f ? -1 : states[shell.Owner].Facing;
                    hits.Add(new HitRecord(shell.Owner, v, shell.Slot, 0, tuning.CannonHit, facing, AttackPower.Normal, fromObject: true));
                    shell.Ended = true;
                }
                return;
            }
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject other = objects[o];
                if (other.Kind != WorldObjectKind.Safe || other.Ended || !OverlapsSafe(other, configs, shell.Position, radius))
                {
                    continue;
                }
                if (flak)
                {
                    Explode(ref shell, objects, states, configs, tuning.FlakExplosionRadius, tuning.FlakHit, hits, tick, tuning, ShellBlast);
                }
                else
                {
                    other.Damage += Math.Max(1, tuning.CannonHit.Damage);
                    objects[o] = other;
                    shell.Ended = true;
                }
                return;
            }
        }

        // 地雷: 飛んでいる間は、地形に当たったらそこに、キャラクター（持ち主を除く）に触れたらその人に張り付く。張り付いたら、
        // 張り付いた相手と一緒に動く。もう一度 E で爆発する。張り付かずに飛び切るか、ステージの外へ出るか、張り付いた人が撃破・抜ける・
        // 吊られる・張り付いた足場がなくなったら、爆発せずに消える（クールタイムは SkillRules.EndObjects が始める）。
        private static void StepMine(ref WorldObject mine, WorldObjects objects, ICollisionQuery terrain, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, StageBounds? bounds, JuggernautTuning tuning, List<HitRecord> hits, int tick,
            IReadOnlyList<StagePlatform> platforms, StageState stage, bool detonate)
        {
            if (mine.Variant == MineBlast)
            {
                return;
            }
            if (detonate)
            {
                Explode(ref mine, objects, states, configs, tuning.MineExplosionRadius, tuning.MineHit, hits, tick, tuning, MineBlast);
                CharacterState owner = states[mine.Owner];
                owner.Combat.MineBlastCount++;
                StartCooldown(ref owner.Combat, mine.Slot, configs[mine.Owner].Combat);
                states[mine.Owner] = owner;
                return;
            }
            switch (mine.Variant)
            {
                case MineFlying:
                {
                    if (tick - mine.SpawnTick > tuning.MineFlightTicks)
                    {
                        mine.Ended = true;
                        return;
                    }
                    // 重力で山なりに飛ぶ（フラックの砲弾と同じ進め方）。
                    Vector2 delta = FlakStep(ref mine.Velocity, tuning.MineGravity) + TakeLead(ref mine);
                    if (Cast(terrain, mine.Position, delta, out Vector2 point, out CastHit hit))
                    {
                        mine.Position = point;
                        mine.Variant = MineOnTerrain;
                        mine.Velocity = new Vector2(hit.Normal.X, hit.Normal.Y);
                        mine.Stored = 0;
                        mine.End = Vector2.Zero;
                        if (hit.Body > 0 && platforms != null && hit.Body <= platforms.Count
                            && StagePlatforms.TryGetCenter(platforms, stage, hit.Body - 1, tick - 1, false, out Vector2 center))
                        {
                            // 動く足場・扉: 中心からの位置を覚えて、一緒に動く。
                            mine.Stored = hit.Body;
                            mine.End = point - center;
                        }
                        return;
                    }
                    mine.Position += delta;
                    if (bounds.HasValue && !bounds.Value.Contains(new Vector3(mine.Position.X, mine.Position.Y, 0f)))
                    {
                        mine.Ended = true;
                        return;
                    }
                    for (int v = 0; v < states.Count; v++)
                    {
                        CharacterState target = states[v];
                        if (v == mine.Owner || target.Combat.IsUntouchable || !Overlaps(target, configs[v], mine.Position, tuning.MineRadius))
                        {
                            continue;
                        }
                        mine.Variant = MineOnCharacter;
                        mine.Stored = v;
                        mine.End = mine.Position - new Vector2(target.Position.X, target.Position.Y);
                        mine.Velocity = Vector2.Zero;
                        return;
                    }
                    break;
                }

                case MineOnTerrain:
                    if (mine.Stored <= 0)
                    {
                        break;
                    }
                    if (platforms != null && mine.Stored <= platforms.Count
                        && StagePlatforms.TryGetCenter(platforms, stage, mine.Stored - 1, tick - 1, false, out Vector2 now))
                    {
                        mine.Position = now + mine.End;
                    }
                    else
                    {
                        mine.Ended = true;
                    }
                    break;

                case MineOnCharacter:
                {
                    CharacterState carrier = states[mine.Stored];
                    if (carrier.Combat.IsWaitingToRespawn || carrier.Combat.HasLeft || carrier.Combat.IsAway)
                    {
                        mine.Ended = true;
                        return;
                    }
                    mine.Position = new Vector2(carrier.Position.X, carrier.Position.Y) + mine.End;
                    break;
                }
            }
        }

        // 爆発: 範囲の相手（持ち主を除く）に当て、範囲の壊せる物に威力の分のダメージを与えて、爆発の印に変える。
        private static void Explode(ref WorldObject item, WorldObjects objects, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            float radius, AttackHit hit, List<HitRecord> hits, int tick, JuggernautTuning tuning, byte blast)
        {
            for (int v = 0; v < states.Count; v++)
            {
                CharacterState target = states[v];
                if (v == item.Owner || target.Combat.IsUntouchable
                    || !BankRules.Touches(target.Position, configs[v].Movement.HalfExtents, item.Position, radius))
                {
                    continue;
                }
                int facing = target.Position.X >= item.Position.X ? 1 : -1;
                hits.Add(new HitRecord(item.Owner, v, item.Slot, 0, hit, facing, AttackPower.Normal, fromObject: true));
            }
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject other = objects[o];
                if (other.Kind == WorldObjectKind.Safe && !other.Ended && OverlapsSafe(other, configs, item.Position, radius))
                {
                    other.Damage += Math.Max(1, hit.Damage);
                    objects[o] = other;
                }
            }
            item.Variant = blast;
            item.Velocity = Vector2.Zero;
            item.SpawnTick = tick;
            item.ExpireTick = tick + tuning.BlastTicks;
            item.End = new Vector2(radius, 0f);
            item.Stored = 0;
        }

        // ---------------------------------------------------------------- R メーデー

        // メーデーを始める: ワイヤー・構え・ため・攻撃をやめ、その場で止まって吊られ始める。
        private static void StartMayday(ref CharacterState state, CharacterConfig config)
        {
            if (state.Wire.Phase == WirePhase.Attached)
            {
                WireController.Release(ref state, WireReleaseReason.Lifted, config.Wire);
            }
            else if (state.Wire.Phase == WirePhase.Firing)
            {
                WireController.Interrupt(ref state, config.Wire);
            }
            var from = new Vector2(state.Position.X, state.Position.Y);
            ref CombatState c = ref state.Combat;
            c.MaydayPhase = MaydayLift;
            c.MaydayTicks = 0;
            c.MaydayFrom = from;
            c.MaydayTarget = from;
            c.FlakStance = false;
            c.Holding = false;
            c.AttackBufferTicksLeft = 0;
            state.Action = default;
            state.Velocity = Vector3.Zero;
            state.WallSide = 0;
            state.JumpBufferTicksLeft = 0;
            state.JumpSquatTicksLeft = 0;
        }

        /// <summary>
        /// メーデーで吊られている人を 1 ティック進める（SimWorld が、CharacterSimulator の代わりに呼ぶ）。場所を選んでいる間は、入力の照準を
        /// 場所に読み替え（DecodeTarget）、決めるボタン（MaydayConfirmButtons）を押したら、空いていればそこへ運ぶ。collision は地形
        /// （動く足場を含む）、bounds は選べる範囲を決めるステージの範囲。
        /// </summary>
        public static void StepMayday(ref CharacterState state, PlayerInput input, CharacterConfig config, ICollisionQuery collision, StageBounds? bounds)
        {
            JuggernautTuning tuning = config.Skills.Juggernaut;
            InputButtons pressed = input.Buttons & ~state.PreviousButtons;
            state.PreviousButtons = input.Buttons;
            state.Velocity = Vector3.Zero;
            if (state.Combat.InvincibleTicksLeft > 0)
            {
                state.Combat.InvincibleTicksLeft--;
            }
            ref CombatState c = ref state.Combat;
            c.MaydayTicks++;
            switch (c.MaydayPhase)
            {
                case MaydayLift:
                    if (c.MaydayTicks >= tuning.MaydayLiftTicks)
                    {
                        c.MaydayPhase = MaydaySelect;
                        c.MaydayTicks = 0;
                    }
                    break;

                case MaydaySelect:
                {
                    TargetArea(bounds, tuning, out Vector2 center, out Vector2 half);
                    c.MaydayTarget = DecodeTarget(input, center, half);
                    Vector2 bodyHalf = new Vector2(config.Movement.HalfWidth, config.Movement.HalfHeight);
                    if ((pressed & MaydayConfirmButtons) != 0)
                    {
                        if (IsFreeTarget(collision, c.MaydayTarget, bodyHalf))
                        {
                            BeginCarry(ref state, c.MaydayTarget);
                        }
                        else
                        {
                            // 地形と重なる場所は選べない（表示が N/A とエラーの音）。
                            c.MaydayRejectCount++;
                        }
                    }
                    else if (c.MaydayTicks >= tuning.MaydaySelectTicks)
                    {
                        // 選ばないまま時間が来たら、元の場所へ戻る（埋まっていれば、いちばん近い空いた所へ）。
                        Vector2 back = collision != null ? PlacementRules.FreePosition(collision, c.MaydayFrom, bodyHalf, Vector2.Zero) : c.MaydayFrom;
                        BeginCarry(ref state, back);
                    }
                    break;
                }

                case MaydayCarry:
                    if (c.MaydayTicks >= tuning.MaydayCarryTicks)
                    {
                        // 降ろされた（その場から落ち始める）。
                        c.MaydayPhase = 0;
                        c.MaydayTicks = 0;
                        state.Grounded = false;
                    }
                    break;

                default:
                    c.MaydayPhase = 0;
                    c.MaydayTicks = 0;
                    break;
            }
        }

        private static void BeginCarry(ref CharacterState state, Vector2 target)
        {
            state.Combat.MaydayPhase = MaydayCarry;
            state.Combat.MaydayTicks = 0;
            state.Combat.MaydayTarget = target;
            state.Position = new Vector3(target.X, target.Y, 0f);
        }

        /// <summary>
        /// メーデーで選べる範囲（中心と半分の大きさ）: ステージの範囲（撃破の線）の内側へ MaydayEdgeMargin だけ入った所まで。範囲がなければ
        /// 原点から ±MaydayDefaultHalfWidth・±MaydayDefaultHalfHeight。
        /// </summary>
        public static void TargetArea(StageBounds? bounds, JuggernautTuning tuning, out Vector2 center, out Vector2 half)
        {
            if (!bounds.HasValue)
            {
                center = Vector2.Zero;
                half = new Vector2(tuning.MaydayDefaultHalfWidth, tuning.MaydayDefaultHalfHeight);
                return;
            }
            StageBounds b = bounds.Value;
            center = new Vector2((b.MinX + b.MaxX) * 0.5f, (b.MinY + b.MaxY) * 0.5f);
            half = new Vector2(MathF.Max(1f, (b.MaxX - b.MinX) * 0.5f - tuning.MaydayEdgeMargin),
                MathF.Max(1f, (b.MaxY - b.MinY) * 0.5f - tuning.MaydayEdgeMargin));
        }

        /// <summary>選ぶ場所 target を、入力の照準に入れる（範囲の中の位置を -1〜1 にして量子化する。範囲の外は端にする）。</summary>
        public static PlayerInput EncodeTarget(PlayerInput input, Vector2 target, Vector2 center, Vector2 half)
        {
            Vector2 rel = target - center;
            return new PlayerInput(input.MoveX, input.MoveY, input.Buttons, Quantize(rel.X / half.X), Quantize(rel.Y / half.Y));
        }

        /// <summary>入力の照準に入っている場所（EncodeTarget の逆。照準がなければ範囲の中心）。</summary>
        public static Vector2 DecodeTarget(PlayerInput input, Vector2 center, Vector2 half)
        {
            return center + new Vector2(input.AimX / (float)PlayerInput.AxisMax * half.X, input.AimY / (float)PlayerInput.AxisMax * half.Y);
        }

        /// <summary>場所 target に、半分の大きさ half の体を置けるか（地形・動く足場と重ならない）。</summary>
        public static bool IsFreeTarget(ICollisionQuery collision, Vector2 target, Vector2 half)
        {
            return collision == null || !PlacementRules.Overlaps(collision, target, half);
        }

        /// <summary>メーデーの今の段階の進み具合（0〜1。表示用）。</summary>
        public static float MaydayProgress(in CharacterState state, JuggernautTuning tuning)
        {
            int length;
            switch (state.Combat.MaydayPhase)
            {
                case MaydayLift: length = tuning.MaydayLiftTicks; break;
                case MaydaySelect: length = tuning.MaydaySelectTicks; break;
                case MaydayCarry: length = tuning.MaydayCarryTicks; break;
                default: return 0f;
            }
            return Math.Clamp(state.Combat.MaydayTicks / (float)length, 0f, 1f);
        }

        // ---------------------------------------------------------------- 小物

        // 飛んでいる砲弾・地雷が、最初のティックに加えて進む分（End。体の中から砲身の先・出す距離まで）。使ったら 0 にする。
        private static Vector2 TakeLead(ref WorldObject item)
        {
            Vector2 lead = item.End;
            item.End = Vector2.Zero;
            return lead;
        }

        private static sbyte AxisOf(Vector2 move)
        {
            if (MathF.Abs(move.Y) < StanceAxisThreshold || MathF.Abs(move.Y) < MathF.Abs(move.X))
            {
                return 0;
            }
            return move.Y > 0f ? (sbyte)1 : (sbyte)-1;
        }

        private static sbyte Quantize(float value)
        {
            return (sbyte)MathF.Round(Math.Clamp(value, -1f, 1f) * PlayerInput.AxisMax);
        }

        // from から delta だけ進む間に地形に当たるか（当たった点と当たり）。
        private static bool Cast(ICollisionQuery terrain, Vector2 from, Vector2 delta, out Vector2 point, out CastHit hit)
        {
            point = from;
            hit = default;
            float distance = delta.Length();
            if (terrain == null || distance <= SimMath.Epsilon)
            {
                return false;
            }
            var origin = new Vector3(from.X, from.Y, 0f);
            var direction = new Vector3(delta.X / distance, delta.Y / distance, 0f);
            if (!terrain.Raycast(origin, direction, distance, out hit))
            {
                return false;
            }
            point = new Vector2(hit.Point.X, hit.Point.Y);
            return true;
        }

        private static bool Overlaps(in CharacterState target, CharacterConfig config, Vector2 point, float radius)
        {
            Vector3 half = config.Movement.HalfExtents;
            return MathF.Abs(target.Position.X - point.X) < half.X + radius && MathF.Abs(target.Position.Y - point.Y) < half.Y + radius;
        }

        private static bool OverlapsSafe(in WorldObject safe, IReadOnlyList<CharacterConfig> configs, Vector2 point, float radius)
        {
            SafeTuning tuning = configs[safe.Owner].Skills.Safe;
            return MathF.Abs(safe.Position.X - point.X) < tuning.HalfWidth + radius && MathF.Abs(safe.Position.Y - point.Y) < tuning.HalfHeight + radius;
        }

        private static void SetCooldown(ref CombatState combat, AttackSlot slot, int ticks)
        {
            if (slot == AttackSlot.Special1)
            {
                combat.Special1CooldownTicksLeft = ticks;
            }
            else if (slot == AttackSlot.Special2)
            {
                combat.Special2CooldownTicksLeft = ticks;
            }
        }

        // 止めていたクールタイムを数え始める（地雷が爆発した）。
        private static void StartCooldown(ref CombatState combat, AttackSlot slot, CombatTuning tuning)
        {
            int cooldown = slot == AttackSlot.Special1 ? combat.Special1CooldownTicksLeft : slot == AttackSlot.Special2 ? combat.Special2CooldownTicksLeft : 0;
            if (cooldown == CombatState.CooldownWhileActive)
            {
                SetCooldown(ref combat, slot, tuning.CooldownTicksOf(slot));
            }
        }
    }
}
