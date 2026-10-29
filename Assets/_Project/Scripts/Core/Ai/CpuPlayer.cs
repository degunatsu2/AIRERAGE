using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Wire;

namespace Airerage.Core.Ai
{
    /// <summary>CPU が今していること（目標の種類）。</summary>
    public enum CpuGoalKind : byte
    {
        /// <summary>何もしない（試合の前後、復帰を待つ間）か、行き先を探して歩き回る。</summary>
        Idle,

        /// <summary>フィールドのコインを拾いに行く。</summary>
        Coin,

        /// <summary>拠点へ所持コインを確定しに行く。</summary>
        Bank,

        /// <summary>相手を攻める。</summary>
        Fight,

        /// <summary>ステージの外へ落ちそうなので、内側の高い所へ戻る。</summary>
        Recover,
    }

    /// <summary>
    /// CPU の 1 人分（docs/16 T7-1。D-70 の推奨【仮】: 何をするかは点数で選び、動き方はルールで決める）。
    /// ワールドの状態を読んで、そのティックの入力（PlayerInput）を作る。人の操作と同じ扱いで、シミュレーションの状態は書き換えない。
    /// - 目標を選ぶ（DecideEveryTicks ごと）: 落ちそうなら戻る。そうでなければ、コインを拾う・拠点へ行く・相手を攻めるのうち点数の高いもの。
    /// - 動き（毎ティック）: 目標へ走る・跳ぶ・空中ジャンプ・ワイヤー（つながる所を WireTargeting.PredictShot で確かめて撃ち、目標の方へ
    ///   振れたら離す）。
    ///   目標を選ぶことと動きは CpuMotion（THIEF の分身と共通。覚えていることは CpuMotionMemory）。
    /// - 攻撃: 相手が判定の届く所にいれば（今の位置で判定の箱と相手の箱が重なるか）、反応の遅れの後に出す。撃破の範囲の端の近くでは撃破技。
    /// - 守り: 近くの相手が攻撃を出したら、確率でガード（CpuTuning.TimedGuard なら判定が出る直前に押してジャストガードを狙う）。
    ///   CpuTuning.CancelOnHit なら、当たった攻撃を次の攻撃でキャンセルしてつなぐ。
    /// - 技（Q・E・R）: 使えて、技のデータから見て今が使い時なら（CpuSkills.Plan）、確率で使う。押す長さ・照準・長押しの間に押す
    ///   ボタンも CpuSkills が決める。落ちそうなときの技（足場・ワープなど）は確率を上げる。
    /// 乱数はティックと番号と試合の種から作るので、同じ入力の並びなら何度でも同じ動きになる。覚えていること（目標など）はこの中に持つ。
    /// </summary>
    public sealed class CpuPlayer
    {
        // ガードを考える相手の近さ（m）。
        private const float GuardRange = 3.5f;

        // バンクハイストの詠唱を止める、相手の近さ（m）と、止める詠唱の長さ（ティック。15 秒）。
        private const float HeistDangerDistance = 7f;
        private const int HeistTargetTicks = 900;

        // 撃破技を優先する、相手と撃破の範囲の端との距離（m）。
        private const float KoEdgeDistance = 12f;

        // フラックの構え（JUGGERNAUT の Q）: 撃つ間（ティック）、構えをやめるまで（ティック）、やめた後にまた構えるまで（ティック）、
        // 狙う相手の距離（m）。
        private const int FlakShotGapTicks = 30;
        private const int StanceGiveUpTicks = 240;
        private const int StanceRetryTicks = 300;
        private const float FlakTargetRange = 30f;

        // メーデー（JUGGERNAUT の R）: 場所を決めるまで待つティック（反応の遅れ）と、ステージの真ん中から上へずらす高さ（m）。
        private const int MaydayDecideTicks = 20;
        private const float MaydayCenterLift = 4f;

        private static readonly AttackSlot[] AirAttacks = { AttackSlot.AirSide, AttackSlot.AirUp, AttackSlot.AirNeutral, AttackSlot.AirDown };
        private static readonly AttackSlot[] Skills = { AttackSlot.Special1, AttackSlot.Special2, AttackSlot.Super };

        private readonly int _index;
        private readonly CpuTuning _tuning;
        private readonly int _salt;
        private readonly CpuMotion _movement;

        // 動き（目標・ワイヤー・行き詰まり）で覚えていること（CpuMotion）。
        private CpuMotionMemory _motion = CpuMotionMemory.Initial;
        private int _attackReadyTick;
        private int _inRangeSinceTick = -1;
        private int _guardStartTick = -1;
        private int _guardUntilTick = -1;
        private int _guardDecidedSerial = -1;
        private int _guardDecidedFor = -1;
        private CpuSkillUse _skill;
        private bool _skillPlanned;
        private int _skillStartTick = -1;
        private int _stanceSinceTick = -1;
        private int _nextFlakTick;
        private int _flakRetryTick;

        /// <param name="index">操作するキャラクターの番号。</param>
        /// <param name="salt">乱数を CPU ごとに変える値（同じ番号でも、試合ごとに変えたいときに）。</param>
        public CpuPlayer(int index, CpuTuning tuning, int salt = 0)
        {
            if (tuning == null)
            {
                throw new ArgumentNullException(nameof(tuning));
            }
            tuning.Validate().ThrowIfInvalid();
            _index = index;
            _tuning = tuning;
            _salt = salt;
            _movement = new CpuMotion(tuning);
        }

        public int Index => _index;

        /// <summary>今していること。</summary>
        public CpuGoalKind Goal => _motion.Goal;

        /// <summary>今の目的地。</summary>
        public Vector2 Target => _motion.Target;

        /// <summary>攻めている相手の番号（Fight でなければ -1）。</summary>
        public int TargetCharacter => _motion.TargetCharacter;

        /// <summary>出した攻撃の数（テストと調整の目安）。</summary>
        public int AttacksStarted { get; private set; }

        /// <summary>撃ったワイヤーの数。</summary>
        public int WiresFired { get; private set; }

        /// <summary>技（Q・E・R）を押し始めた数。</summary>
        public int SkillsStarted { get; private set; }

        /// <summary>
        /// 次のティックの入力を作る（world は今のティックの状態。world.Step に渡す前に呼ぶ）。
        /// </summary>
        public PlayerInput Think(SimWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            int tick = world.Tick;
            CharacterState self = world.GetState(_index);
            MatchPhase phase = world.Phase;
            if (self.Combat.IsWaitingToRespawn || (phase != MatchPhase.Playing && phase != MatchPhase.Overtime))
            {
                _motion.Goal = CpuGoalKind.Idle;
                _motion.TargetCharacter = -1;
                _motion.LastButtons = InputButtons.None;
                _motion.WireAttachedTick = -1;
                _motion.StuckSinceTick = tick;
                _motion.StuckFrom = self.Position;
                _skillPlanned = false;
                _skillStartTick = -1;
                return default;
            }

            if (self.Combat.IsAway)
            {
                // メーデーで吊られている（JUGGERNAUT の R）: 場所を選ぶ。
                return MaydayInput(world, self, tick);
            }
            if (!self.Combat.FlakStance)
            {
                _stanceSinceTick = -1;
            }
            else if (HoldStance(world, self, tick, out PlayerInput stance))
            {
                return stance;
            }

            if (self.Combat.HeistTicks > 0)
            {
                // バンクハイストの詠唱中（動けない）: 相手が近づいたか、十分に詠唱したら、もう一度 R で止めてコインを得る。
                bool stop = HeistShouldStop(world, self) && (_motion.LastButtons & InputButtons.Super) == 0;
                _motion.LastButtons = stop ? InputButtons.Super : InputButtons.None;
                return PlayerInput.FromAxes(0f, 0f, _motion.LastButtons);
            }

            CharacterConfig config = SpeedBoosts.EffectiveConfig(self, world.GetConfig(_index));
            if (tick >= _motion.NextDecisionTick)
            {
                Decide(world, self, tick);
                _motion.NextDecisionTick = tick + _tuning.DecideEveryTicks;
            }
            CpuMotion.FollowTarget(world, tick, ref _motion);

            float moveX = 0f;
            float moveY = 0f;
            var buttons = InputButtons.None;
            Vector2 aim = Vector2.Zero;
            bool guardPending = tick < _guardStartTick;
            if (tick >= _guardStartTick && tick < _guardUntilTick)
            {
                buttons |= InputButtons.Guard;
            }
            else
            {
                if (_movement.Steer(world, self, config, Key, tick, ref _motion, ref moveX, ref moveY, ref buttons, ref aim))
                {
                    WiresFired++;
                }
                if (_skillStartTick < 0 && !guardPending)
                {
                    Attack(world, self, config, tick, ref moveX, ref moveY, ref buttons);
                    ConsiderGuard(world, self, tick, ref buttons);
                }
                PressSkill(world, self, tick, ref buttons, ref aim);
                DetonateMine(world, ref buttons);
            }

            PlayerInput input = PlayerInput.FromAxes(Clamp(moveX), Clamp(moveY), buttons);
            if (aim != Vector2.Zero)
            {
                input = input.WithAim(aim);
            }
            _motion.LastButtons = buttons;
            return input;
        }

        private void Decide(SimWorld world, in CharacterState self, int tick)
        {
            _movement.DecideGoal(world, self, _index, Key, tick, ref _motion);
            DecideSkill(world, self, tick);
        }

        // 技を選ぶ: 使える技のうち、今が使い時のもの（CpuSkills.Plan）を確率で。押すのは PressSkill。
        private void DecideSkill(SimWorld world, in CharacterState self, int tick)
        {
            _skillPlanned = false;
            if (_skillStartTick >= 0 || !(self.Action.IsFree || self.Wire.Phase == WirePhase.Attached) || self.Combat.Holding)
            {
                return;
            }
            Vector2 position = Flat(self.Position);
            int near = NearestOpponent(world, position, float.MaxValue, out float nearDistance);
            bool overVoid = world.Bounds.HasValue && CpuMotion.OverVoid(world, self);
            var situation = new CpuSituation(Goal, Target, near, nearDistance, overVoid, _tuning.BankAtHeldCoins);
            CharacterConfig config = world.GetConfig(_index);
            foreach (AttackSlot slot in Skills)
            {
                if (slot == AttackSlot.Special1 && config.Combat.Special1.Effect == SkillEffect.Flak && tick < _flakRetryTick)
                {
                    // 構えても撃てなかった直後は、しばらく構えない。
                    continue;
                }
                if (!ActionController.CanUse(self, slot, config.Combat) || !CpuSkills.Plan(world, _index, self, config, situation, slot, out CpuSkillUse use))
                {
                    continue;
                }
                float chance = use.Urgent ? Math.Min(1f, _tuning.SkillChance * 2f + 0.2f) : _tuning.SkillChance;
                if (Chance(tick, 200 + (int)slot, chance))
                {
                    _skill = use;
                    _skillPlanned = true;
                    return;
                }
            }
        }

        // 選んだ技を押す: 押し始めは、攻撃・ガードをしていないティック。押している間は攻撃・ワイヤー・ガードを押さず（長押しの間の
        // ワイヤー・攻撃はクリームの味を選んでしまう）、照準は相手の方へ向け続ける。のけぞったら、やめる。
        private void PressSkill(SimWorld world, in CharacterState self, int tick, ref InputButtons buttons, ref Vector2 aim)
        {
            if (_skillStartTick < 0)
            {
                bool free = self.Action.IsFree || self.Wire.Phase == WirePhase.Attached;
                if (!_skillPlanned || !free || (buttons & (InputButtons.Attack | InputButtons.Guard)) != 0)
                {
                    return;
                }
                _skillPlanned = false;
                _skillStartTick = tick;
                SkillsStarted++;
                buttons &= ~InputButtons.Jump;
            }
            int elapsed = tick - _skillStartTick;
            if (elapsed >= _skill.HoldTicks || self.Action.Kind == ActionKind.Hitstun)
            {
                _skillStartTick = -1;
                return;
            }
            buttons &= ~(InputButtons.Attack | InputButtons.Wire | InputButtons.Guard);
            buttons |= ActionController.ButtonOf(_skill.Slot);
            if (_skill.Choice != InputButtons.None && elapsed == _skill.ChoiceTick)
            {
                buttons |= _skill.Choice;
            }
            if (_skill.AimAt >= 0)
            {
                aim = Flat(world.GetState(_skill.AimAt).Position) - Flat(self.Position);
            }
            else if (_skill.Aim != Vector2.Zero)
            {
                aim = _skill.Aim;
            }
        }

        // 攻撃: 相手が判定の届く所にいれば、反応の遅れの後に出す。
        private void Attack(SimWorld world, in CharacterState self, CharacterConfig config, int tick, ref float moveX, ref float moveY,
            ref InputButtons buttons)
        {
            // HELL: 当たったら、終わるのを待たずに次の攻撃でキャンセルしてつなぐ（D-17 の 1 回）。
            bool comboCancel = _tuning.CancelOnHit && self.Action.Kind == ActionKind.Attack && self.Action.Connected && !self.Combat.AttackCancelUsed;
            if ((tick < _attackReadyTick && !comboCancel) || (buttons & InputButtons.Jump) != 0)
            {
                return;
            }
            bool canAct = comboCancel || self.Action.IsFree || self.Wire.Phase == WirePhase.Attached;
            if (!canAct || (_motion.LastButtons & InputButtons.Attack) != 0)
            {
                return;
            }
            CombatTuning combat = config.Combat;
            int targetIndex = -1;
            AttackSlot slot = AttackSlot.Ground;
            for (int other = 0; other < world.CharacterCount && targetIndex < 0; other++)
            {
                if (other == _index)
                {
                    continue;
                }
                CharacterState opponent = world.GetState(other);
                if (opponent.Combat.IsUntouchable || CpuMotion.IsHidden(opponent))
                {
                    continue;
                }
                Vector3 opponentHalf = world.GetConfig(other).Movement.HalfExtents;
                int facing = opponent.Position.X >= self.Position.X ? 1 : -1;
                if (self.Grounded)
                {
                    if (Reaches(self, facing, combat.Ground, opponent.Position, opponentHalf))
                    {
                        targetIndex = other;
                        slot = AttackSlot.Ground;
                    }
                    continue;
                }
                bool nearEdge = world.Bounds.HasValue && NearEdge(world.Bounds.Value, Flat(opponent.Position));
                if ((nearEdge || Chance(tick / 8, 400 + other, 0.35f)) && Reaches(self, facing, combat.Get(combat.KoAttack), opponent.Position, opponentHalf))
                {
                    targetIndex = other;
                    slot = combat.KoAttack;
                    continue;
                }
                int start = StableHash.Range(world.Rules.Seed, _salt + 500 + _index, tick / 8, AirAttacks.Length);
                for (int k = 0; k < AirAttacks.Length; k++)
                {
                    AttackSlot candidate = AirAttacks[(start + k) % AirAttacks.Length];
                    if (Reaches(self, facing, combat.Get(candidate), opponent.Position, opponentHalf))
                    {
                        targetIndex = other;
                        slot = candidate;
                        break;
                    }
                }
            }
            if (targetIndex < 0)
            {
                _inRangeSinceTick = -1;
                return;
            }
            if (_inRangeSinceTick < 0)
            {
                _inRangeSinceTick = tick;
            }
            if (tick - _inRangeSinceTick < _tuning.ReactionTicks)
            {
                return;
            }
            float side = world.GetState(targetIndex).Position.X >= self.Position.X ? 1f : -1f;
            switch (slot)
            {
                case AttackSlot.AirUp:
                    moveX = 0f;
                    moveY = 1f;
                    break;
                case AttackSlot.AirDown:
                    moveX = 0f;
                    moveY = -1f;
                    break;
                case AttackSlot.AirNeutral:
                    moveX = 0f;
                    moveY = 0f;
                    break;
                default:
                    moveX = side;
                    moveY = 0f;
                    break;
            }
            buttons |= InputButtons.Attack;
            buttons &= ~InputButtons.Jump;
            AttacksStarted++;
            _inRangeSinceTick = -1;
            _attackReadyTick = tick + combat.Get(slot).TotalTicks + _tuning.AttackIntervalTicks;
        }

        // 近くの相手が攻撃を出したら、確率でガードする（相手の攻撃ごとに 1 回だけ決める）。
        private void ConsiderGuard(SimWorld world, in CharacterState self, int tick, ref InputButtons buttons)
        {
            if (!self.Action.IsFree || (buttons & InputButtons.Attack) != 0)
            {
                return;
            }
            for (int other = 0; other < world.CharacterCount; other++)
            {
                if (other == _index)
                {
                    continue;
                }
                CharacterState opponent = world.GetState(other);
                if (opponent.Action.Kind != ActionKind.Attack || Vector3.Distance(opponent.Position, self.Position) > GuardRange)
                {
                    continue;
                }
                bool facingMe = (self.Position.X - opponent.Position.X) * opponent.Facing >= 0f;
                if (!facingMe || (_guardDecidedFor == other && _guardDecidedSerial == opponent.Combat.AttackSerial))
                {
                    continue;
                }
                _guardDecidedFor = other;
                _guardDecidedSerial = opponent.Combat.AttackSerial;
                if (Chance(tick, 600 + other, _tuning.GuardChance))
                {
                    // TimedGuard（HELL）: 相手の判定が出る 1 ティック前にガードが始まるように待つ（ジャストガードを狙う）。
                    int delay = 0;
                    if (_tuning.TimedGuard)
                    {
                        AttackData attack = world.GetConfig(other).Combat.Get(opponent.Action.Attack);
                        delay = Math.Max(0, attack.FirstHitTick - opponent.Action.Ticks - 2);
                    }
                    _guardStartTick = tick + delay;
                    _guardUntilTick = _guardStartTick + 18;
                    if (delay == 0)
                    {
                        buttons = InputButtons.Guard;
                    }
                    return;
                }
            }
        }

        // 攻撃 attack の判定のどれかが、今の位置で facing を向いて出したときに、相手の箱に届くか（攻撃中の動きは考えない）。
        private static bool Reaches(in CharacterState self, int facing, AttackData attack, Vector3 target, Vector3 targetHalf)
        {
            if (attack == null || attack.Hits == null)
            {
                return false;
            }
            CharacterState probe = self;
            probe.Facing = facing;
            var targetCenter = new Vector2(target.X, target.Y);
            var half = new Vector2(targetHalf.X + 0.2f, targetHalf.Y + 0.2f);
            foreach (AttackHit hit in attack.Hits)
            {
                if (hit.Critical || !attack.Uses(hit.Charge, 0))
                {
                    continue;
                }
                HitResolver.Hitbox(probe, hit, out Vector2 center, out Vector2 boxHalf);
                if (HitShapes.Overlaps(center, boxHalf, HitResolver.HitAngle(probe, hit), targetCenter, half))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool NearEdge(StageBounds bounds, Vector2 position)
        {
            return position.X < bounds.MinX + KoEdgeDistance || position.X > bounds.MaxX - KoEdgeDistance || position.Y > bounds.MaxY - KoEdgeDistance
                || position.Y < bounds.MinY + KoEdgeDistance;
        }

        // フラックの構えの間（JUGGERNAUT の Q）: 動かずに、相手に届く弾道の照準で Q を押し直して撃つ。届かなければ上下で弾道の段階を
        // 変える。どの段階でも届かないか、構えてから StanceGiveUpTicks たったら、構えをやめる（false。ふだんの動きで構えが解ける）。
        private bool HoldStance(SimWorld world, in CharacterState self, int tick, out PlayerInput input)
        {
            input = default;
            if (_stanceSinceTick < 0)
            {
                _stanceSinceTick = tick;
            }
            CharacterConfig config = world.GetConfig(_index);
            int opponent = NearestOpponent(world, Flat(self.Position), FlakTargetRange, out _);
            if (tick - _stanceSinceTick > StanceGiveUpTicks || opponent < 0)
            {
                _flakRetryTick = tick + StanceRetryTicks;
                return false;
            }
            JuggernautTuning tuning = config.Skills.Juggernaut;
            int level = JuggernautRules.FlakLevel(self, tuning);
            if (CpuSkills.TrySolveFlak(world, self, config, opponent, level, out Vector2 aim))
            {
                bool fire = tick >= _nextFlakTick && (_motion.LastButtons & InputButtons.Special1) == 0
                    && ActionController.CanUse(self, AttackSlot.Special1, config.Combat);
                if (fire)
                {
                    _nextFlakTick = tick + FlakShotGapTicks;
                    SkillsStarted++;
                }
                _motion.LastButtons = fire ? InputButtons.Special1 : InputButtons.None;
                input = PlayerInput.FromAxes(0f, 0f, _motion.LastButtons).WithAim(aim);
                return true;
            }
            for (int other = 0; other < tuning.FlakLevels; other++)
            {
                if (other != level && CpuSkills.TrySolveFlak(world, self, config, opponent, other, out _))
                {
                    // 届く段階へ、上下を 1 回ずつ押して変える（押す・離すをくり返す）。
                    float y = tick % 2 == 0 ? (other > level ? 1f : -1f) : 0f;
                    _motion.LastButtons = InputButtons.None;
                    input = PlayerInput.FromAxes(0f, y, InputButtons.None);
                    return true;
                }
            }
            _flakRetryTick = tick + StanceRetryTicks;
            return false;
        }

        // 自分の地雷（JUGGERNAUT の E）の爆発の範囲に相手が入ったら、もう一度 E を押して爆発させる。
        private void DetonateMine(SimWorld world, ref InputButtons buttons)
        {
            if ((_motion.LastButtons & InputButtons.Special2) != 0)
            {
                return;
            }
            WorldObjects objects = world.Objects;
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Kind != WorldObjectKind.Mine || item.Owner != _index || item.Ended || JuggernautRules.IsBlast(item))
                {
                    continue;
                }
                float radius = world.GetConfig(_index).Skills.Juggernaut.MineExplosionRadius * 0.85f;
                if (NearestOpponent(world, item.Position, radius, out _) >= 0)
                {
                    buttons |= InputButtons.Special2;
                    return;
                }
            }
        }

        // メーデーで吊られている間（JUGGERNAUT の R）: 場所を選ぶ間に、拠点（コインを多く持っていれば）か、ステージの真ん中の少し上の、
        // 空いた所を選んで決める（照準に場所を入れる。JuggernautRules.EncodeTarget）。
        private PlayerInput MaydayInput(SimWorld world, in CharacterState self, int tick)
        {
            if (self.Combat.MaydayPhase != JuggernautRules.MaydaySelect || self.Combat.MaydayTicks < MaydayDecideTicks)
            {
                _motion.LastButtons = InputButtons.None;
                return default;
            }
            CharacterConfig config = world.GetConfig(_index);
            JuggernautRules.TargetArea(world.Bounds, config.Skills.Juggernaut, out Vector2 center, out Vector2 half);
            Vector2 desired = center + new Vector2(0f, MaydayCenterLift);
            IReadOnlyList<BankSpot> banks = world.BankSpots;
            if (banks.Count > 0 && self.Coins.Held >= _tuning.BankAtHeldCoins)
            {
                desired = banks[BankRules.SpotAt(world.Rules, banks.Count, tick)].Position;
            }
            var body = new Vector2(config.Movement.HalfWidth, config.Movement.HalfHeight);
            Vector2 free = PlacementRules.FreePosition(world.Collision, desired, body, Vector2.Zero);
            bool confirm = (_motion.LastButtons & InputButtons.Attack) == 0;
            _motion.LastButtons = confirm ? InputButtons.Attack : InputButtons.None;
            PlayerInput input = PlayerInput.FromAxes(0f, 0f, _motion.LastButtons);
            for (int step = 0; step <= 10; step++)
            {
                // 照準に入れると少し丸まるので、丸めた後の場所が空いているところまで上へずらす。
                PlayerInput encoded = JuggernautRules.EncodeTarget(input, free + new Vector2(0f, 0.5f * step), center, half);
                if (JuggernautRules.IsFreeTarget(world.Collision, JuggernautRules.DecodeTarget(encoded, center, half), body))
                {
                    return encoded;
                }
            }
            return JuggernautRules.EncodeTarget(input, free, center, half);
        }

        // バンクハイストの詠唱を止めるか: 相手が近づいた（HeistDangerDistance）か、HeistTargetTicks 詠唱した（15 秒で 95 枚）。
        private bool HeistShouldStop(SimWorld world, in CharacterState self)
        {
            NearestOpponent(world, Flat(self.Position), HeistDangerDistance, out float distance);
            return distance <= HeistDangerDistance || self.Combat.HeistTicks >= HeistTargetTicks;
        }

        private int NearestOpponent(SimWorld world, Vector2 position, float range, out float distanceToNearest)
        {
            int nearest = -1;
            float best = range;
            for (int other = 0; other < world.CharacterCount; other++)
            {
                if (other == _index)
                {
                    continue;
                }
                CharacterState opponent = world.GetState(other);
                if (opponent.Combat.IsUntouchable || CpuMotion.IsHidden(opponent))
                {
                    continue;
                }
                float distance = Vector2.Distance(position, Flat(opponent.Position));
                if (distance < best)
                {
                    best = distance;
                    nearest = other;
                }
            }
            distanceToNearest = nearest >= 0 ? best : float.MaxValue;
            return nearest;
        }

        // 0〜1 の乱数（ティック・目的・番号・CPU の値から決まる）。
        // CPU ごとの乱数の値（CpuMotion にも渡す）。
        private int Key => _salt * 7919 + _index;

        private float Random(int tick, int purpose)
        {
            return CpuMotion.Random(Key, tick, purpose);
        }

        private bool Chance(int tick, int purpose, float probability)
        {
            return Random(tick, purpose) < probability;
        }

        private static Vector2 Flat(Vector3 value)
        {
            return new Vector2(value.X, value.Y);
        }

        private static float Clamp(float value)
        {
            return Math.Clamp(value, -1f, 1f);
        }
    }
}
