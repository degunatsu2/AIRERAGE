using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
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
    /// - 攻撃: 相手が判定の届く所にいれば（今の位置で判定の箱と相手の箱が重なるか）、反応の遅れの後に出す。撃破の範囲の端の近くでは撃破技。
    /// - 守り: 近くの相手が攻撃を出したら、確率でガード。
    /// - 技（Q・E・R）: 使えて、技のデータから見て今が使い時なら（CpuSkills.Plan）、確率で使う。押す長さ・照準・長押しの間に押す
    ///   ボタンも CpuSkills が決める。落ちそうなときの技（足場・ワープなど）は確率を上げる。
    /// 乱数はティックと番号と試合の種から作るので、同じ入力の並びなら何度でも同じ動きになる。覚えていること（目標など）はこの中に持つ。
    /// </summary>
    public sealed class CpuPlayer
    {
        // 目標に着いたとみなす距離（m）。
        private const float ArriveDistance = 0.8f;

        // 撃破の範囲の端から、この距離より外側に出たら戻る（m）。
        private const float EdgeMargin = 5f;

        // ワイヤーにつないでいる最大のティック（振れ切らないうちでも離す）。
        private const int WireHoldMaxTicks = 80;

        // この間（ティック）に 1m も動けなければ、行き詰まったとみなして歩き回る。
        private const int StuckTicks = 120;
        private const int WanderTicks = 90;

        // ワイヤーを離してから、次に撃つまでの間（ティック。戻るときは待たない。撃ちすぎてせわしなく見えないように）。
        private const int WireGapTicks = 15;

        // ガードを考える相手の近さ（m）。
        private const float GuardRange = 3.5f;

        // 撃破技を優先する、相手と撃破の範囲の端との距離（m）。
        private const float KoEdgeDistance = 12f;

        private static readonly AttackSlot[] AirAttacks = { AttackSlot.AirSide, AttackSlot.AirUp, AttackSlot.AirNeutral, AttackSlot.AirDown };
        private static readonly AttackSlot[] Skills = { AttackSlot.Special1, AttackSlot.Special2, AttackSlot.Super };

        // ワイヤーを試す方向（目標の方向から、上へ回す角度。度）。
        private static readonly float[] WireLifts = { 25f, 45f, 65f, 80f, 0f };

        private readonly int _index;
        private readonly CpuTuning _tuning;
        private readonly int _salt;
        private readonly List<Vector2> _candidates = new List<Vector2>();

        private int _nextDecisionTick;
        private InputButtons _lastButtons;
        private int _attackReadyTick;
        private int _inRangeSinceTick = -1;
        private int _guardUntilTick = -1;
        private int _guardDecidedSerial = -1;
        private int _guardDecidedFor = -1;
        private int _wireAttachedTick = -1;
        private int _wireReadyTick;
        private Vector3 _stuckFrom;
        private int _stuckSinceTick;
        private int _wanderUntilTick = -1;
        private CpuSkillUse _skill;
        private bool _skillPlanned;
        private int _skillStartTick = -1;

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
        }

        public int Index => _index;

        /// <summary>今していること。</summary>
        public CpuGoalKind Goal { get; private set; }

        /// <summary>今の目的地。</summary>
        public Vector2 Target { get; private set; }

        /// <summary>攻めている相手の番号（Fight でなければ -1）。</summary>
        public int TargetCharacter { get; private set; } = -1;

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
                Goal = CpuGoalKind.Idle;
                TargetCharacter = -1;
                _lastButtons = InputButtons.None;
                _wireAttachedTick = -1;
                _stuckSinceTick = tick;
                _stuckFrom = self.Position;
                _skillPlanned = false;
                _skillStartTick = -1;
                return default;
            }

            CharacterConfig config = SpeedBoosts.EffectiveConfig(self, world.GetConfig(_index));
            if (tick >= _nextDecisionTick)
            {
                Decide(world, self, tick);
                _nextDecisionTick = tick + _tuning.DecideEveryTicks;
            }
            if (Goal == CpuGoalKind.Fight && TargetCharacter >= 0)
            {
                CharacterState opponent = world.GetState(TargetCharacter);
                if (opponent.Combat.IsUntouchable)
                {
                    _nextDecisionTick = tick;
                }
                Target = Flat(opponent.Position);
            }

            float moveX = 0f;
            float moveY = 0f;
            var buttons = InputButtons.None;
            Vector2 aim = Vector2.Zero;
            if (tick < _guardUntilTick)
            {
                buttons |= InputButtons.Guard;
            }
            else
            {
                Steer(world, self, config, tick, ref moveX, ref moveY, ref buttons, ref aim);
                if (_skillStartTick < 0)
                {
                    Attack(world, self, config, tick, ref moveX, ref moveY, ref buttons);
                    ConsiderGuard(world, self, tick, ref buttons);
                }
                PressSkill(world, self, tick, ref buttons, ref aim);
            }

            PlayerInput input = PlayerInput.FromAxes(Clamp(moveX), Clamp(moveY), buttons);
            if (aim != Vector2.Zero)
            {
                input = input.WithAim(aim);
            }
            _lastButtons = buttons;
            return input;
        }

        private void Decide(SimWorld world, in CharacterState self, int tick)
        {
            DecideGoal(world, self, tick);
            DecideSkill(world, self, tick);
        }

        // 目標を選ぶ。落ちそうなら戻る。行き詰まっていたら歩き回る。それ以外は、コイン・拠点・相手のうち点数の高いもの。
        private void DecideGoal(SimWorld world, in CharacterState self, int tick)
        {
            Vector2 position = Flat(self.Position);
            if (Vector3.Distance(self.Position, _stuckFrom) > 1f)
            {
                _stuckFrom = self.Position;
                _stuckSinceTick = tick;
            }
            else if (tick - _stuckSinceTick > StuckTicks && Goal != CpuGoalKind.Idle)
            {
                _wanderUntilTick = tick + WanderTicks;
                _stuckSinceTick = tick;
            }

            TargetCharacter = -1;
            if (world.Bounds.HasValue && (InDanger(world.Bounds.Value, position) || OverVoid(world, self)))
            {
                Goal = CpuGoalKind.Recover;
                Target = SafePoint(world, position);
                return;
            }
            if (tick < _wanderUntilTick)
            {
                Goal = CpuGoalKind.Idle;
                Target = WanderPoint(world, position, tick);
                return;
            }

            float best = 0f;
            Goal = CpuGoalKind.Idle;
            Target = position;

            // 拠点: 持っているコインが多い（アディショナルタイムなら少しでも）。
            IReadOnlyList<BankSpot> banks = world.BankSpots;
            bool overtime = world.Phase == MatchPhase.Overtime;
            if (banks.Count > 0 && (self.Coins.Held >= _tuning.BankAtHeldCoins || (overtime && self.Coins.Held > 0)))
            {
                int spot = BankRules.SpotAt(world.Rules, banks.Count, tick);
                if (spot >= 0)
                {
                    float score = 3f * Jitter(tick, 1);
                    if (score > best)
                    {
                        best = score;
                        Goal = CpuGoalKind.Bank;
                        Target = banks[spot].Position;
                    }
                }
            }

            // コイン: 価値 ÷ 距離。
            IReadOnlyList<FieldCoin> coins = world.Coins.Field.Coins;
            for (int i = 0; i < coins.Count; i++)
            {
                FieldCoin coin = coins[i];
                if (coin.PickableFromTick > tick)
                {
                    continue;
                }
                float distance = Vector2.Distance(position, coin.Position);
                float score = 4f * MathF.Sqrt(coin.Value) / (distance + 6f) * Jitter(tick, 10 + i);
                if (score > best)
                {
                    best = score;
                    Goal = CpuGoalKind.Coin;
                    Target = coin.Position;
                }
            }

            // 相手: 攻める欲しさ ÷ 距離。コインを多く持っている相手ほど狙う。
            for (int other = 0; other < world.CharacterCount; other++)
            {
                if (other == _index)
                {
                    continue;
                }
                CharacterState opponent = world.GetState(other);
                if (opponent.Combat.IsUntouchable)
                {
                    continue;
                }
                float distance = Vector2.Distance(position, Flat(opponent.Position));
                float score = _tuning.Aggression * 6f / (distance + 6f) * (1f + opponent.Coins.Held / 40f) * Jitter(tick, 100 + other);
                if (score > best)
                {
                    best = score;
                    Goal = CpuGoalKind.Fight;
                    Target = Flat(opponent.Position);
                    TargetCharacter = other;
                }
            }
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
            bool overVoid = world.Bounds.HasValue && OverVoid(world, self);
            var situation = new CpuSituation(Goal, Target, near, nearDistance, overVoid, _tuning.BankAtHeldCoins);
            CharacterConfig config = world.GetConfig(_index);
            foreach (AttackSlot slot in Skills)
            {
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

        // 目標へ動く: 走る・跳ぶ・空中ジャンプ・ワイヤー。
        private void Steer(SimWorld world, in CharacterState self, CharacterConfig config, int tick, ref float moveX, ref float moveY,
            ref InputButtons buttons, ref Vector2 aim)
        {
            Vector2 position = Flat(self.Position);
            Vector2 toTarget = Target - position;
            bool fighting = Goal == CpuGoalKind.Fight;
            // 攻める相手とは、少し間をあける（重なると判定が外れやすい）。
            float stopX = fighting ? 1.2f : ArriveDistance;
            if (MathF.Abs(toTarget.X) > stopX)
            {
                moveX = MathF.Sign(toTarget.X);
            }

            bool blocked = moveX != 0f && Blocked(world.Collision, self, config, moveX);
            bool wantUp = toTarget.Y > 1.2f || blocked;
            WireState wire = self.Wire;

            // 跳ぶ: 地上で上へ行きたい・前がふさがっている。空中で落ち始めたら空中ジャンプ。
            bool jumpHeldLastTick = (_lastButtons & InputButtons.Jump) != 0;
            if (!jumpHeldLastTick && wantUp && wire.Phase != WirePhase.Attached)
            {
                bool canAirJump = self.AirJumpsUsed < config.Movement.AirJumps && self.Velocity.Y < 2f;
                if (self.Grounded || self.WallSide != 0 || canAirJump)
                {
                    buttons |= InputButtons.Jump;
                }
            }

            switch (wire.Phase)
            {
                case WirePhase.Ready:
                    bool needWire = Goal == CpuGoalKind.Recover
                        || (toTarget.Y > 3f && !self.Grounded)
                        || (toTarget.Y > 4f)
                        || (MathF.Abs(toTarget.X) > 9f && !self.Grounded);
                    bool wireHeldLastTick = (_lastButtons & InputButtons.Wire) != 0;
                    bool waited = tick >= _wireReadyTick || Goal == CpuGoalKind.Recover;
                    if (needWire && waited && !wireHeldLastTick && TryAimWire(world, self, config.Wire, tick, out Vector2 direction))
                    {
                        buttons |= InputButtons.Wire;
                        aim = direction;
                        WiresFired++;
                    }
                    _wireAttachedTick = -1;
                    break;

                case WirePhase.Firing:
                    buttons |= InputButtons.Wire;
                    break;

                case WirePhase.Attached:
                    if (_wireAttachedTick < 0)
                    {
                        _wireAttachedTick = tick;
                    }
                    if (!ShouldRelease(self, toTarget, tick))
                    {
                        buttons |= InputButtons.Wire;
                        // 目標が上なら巻き取る（D-50: 上で巻き取る）。
                        moveY = toTarget.Y > 1f ? 1f : 0f;
                    }
                    else
                    {
                        _wireReadyTick = tick + WireGapTicks;
                    }
                    break;
            }
        }

        // ワイヤーを撃つ方向を選ぶ: 上にあるアンカーと、目標の方向から上へ回した方向のうち、つながって目標に近づくもの。
        private bool TryAimWire(SimWorld world, in CharacterState self, WireTuning tuning, int tick, out Vector2 direction)
        {
            direction = Vector2.Zero;
            Vector2 position = Flat(self.Position);
            Vector2 toTarget = Target - position;
            float side = toTarget.X != 0f ? MathF.Sign(toTarget.X) : self.Facing;
            _candidates.Clear();
            foreach (Vector3 anchor in world.Collision.AnchorPoints)
            {
                Vector2 point = Flat(anchor);
                if (point.Y > position.Y + 0.5f && Vector2.Distance(point, position) < tuning.MaxLength * 0.95f)
                {
                    _candidates.Add(point - position);
                }
            }
            float baseAngle = MathF.Atan2(MathF.Max(toTarget.Y, 0f), side * MathF.Max(MathF.Abs(toTarget.X), 0.01f));
            foreach (float lift in WireLifts)
            {
                float angle = side > 0f ? baseAngle + lift * MathF.PI / 180f : baseAngle - lift * MathF.PI / 180f;
                if (lift >= 80f)
                {
                    angle = MathF.PI / 2f;
                }
                _candidates.Add(new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
            }

            float best = float.MaxValue;
            var origin = self.Position;
            foreach (Vector2 candidate in _candidates)
            {
                if (candidate.LengthSquared() < 1e-6f)
                {
                    continue;
                }
                Vector2 unit = Vector2.Normalize(candidate);
                WireShotPreview preview = WireTargeting.PredictShot(origin, unit, self.Facing, tuning, world.Collision);
                if (preview.Result != WireShotResult.Attach || preview.EndPoint.Y < origin.Y - 0.5f)
                {
                    continue;
                }
                Vector2 end = Flat(preview.EndPoint);
                // 目標に近く、高い所ほどよい（振って届くように）。
                float score = Vector2.Distance(end, Target) - 0.3f * (end.Y - position.Y);
                if (score < best)
                {
                    best = score;
                    direction = unit;
                }
            }
            if (best == float.MaxValue)
            {
                return false;
            }
            // 狙いのずれ（強さ）。
            float error = (Random(tick, 300) * 2f - 1f) * _tuning.AimErrorDegrees * MathF.PI / 180f;
            float cos = MathF.Cos(error);
            float sin = MathF.Sin(error);
            direction = new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
            return true;
        }

        // つないだワイヤーを離すか: 長くつなぎすぎた、目標に着いた、アンカーの高さまで上がった、目標の方へ振れて上がっている。
        private bool ShouldRelease(in CharacterState self, Vector2 toTarget, int tick)
        {
            if (tick - _wireAttachedTick > WireHoldMaxTicks || toTarget.Length() < 2f)
            {
                return true;
            }
            Vector3 anchor = self.Wire.Anchor;
            if (self.Position.Y > anchor.Y - 1.2f)
            {
                return true;
            }
            float side = MathF.Sign(toTarget.X);
            bool pastAnchor = MathF.Sign(self.Position.X - anchor.X) == side && MathF.Abs(self.Position.X - anchor.X) > 1.5f;
            bool swingingToward = MathF.Sign(self.Velocity.X) == side && self.Velocity.Y > 1f;
            return Goal != CpuGoalKind.Recover && pastAnchor && swingingToward;
        }

        // 攻撃: 相手が判定の届く所にいれば、反応の遅れの後に出す。
        private void Attack(SimWorld world, in CharacterState self, CharacterConfig config, int tick, ref float moveX, ref float moveY,
            ref InputButtons buttons)
        {
            if (tick < _attackReadyTick || (buttons & InputButtons.Jump) != 0)
            {
                return;
            }
            bool canAct = self.Action.IsFree || self.Wire.Phase == WirePhase.Attached;
            if (!canAct || (_lastButtons & InputButtons.Attack) != 0)
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
                if (opponent.Combat.IsUntouchable)
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
                    _guardUntilTick = tick + 18;
                    buttons = InputButtons.Guard;
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

        // 進む方向の目の前（体の高さ）が地形でふさがっているか。
        private static bool Blocked(ICollisionQuery collision, in CharacterState self, CharacterConfig config, float side)
        {
            var origin = new Vector3(self.Position.X, self.Position.Y - config.Movement.HalfHeight * 0.4f, 0f);
            float reach = config.Movement.HalfWidth + 0.9f;
            return collision.Raycast(origin, new Vector3(side, 0f, 0f), reach, out _);
        }

        private static bool InDanger(StageBounds bounds, Vector2 position)
        {
            return position.Y < bounds.MinY + EdgeMargin || position.X < bounds.MinX + EdgeMargin || position.X > bounds.MaxX - EdgeMargin;
        }

        // 空中で、真下に何もない（このまま落ちるとステージの外へ出る）。
        private static bool OverVoid(SimWorld world, in CharacterState self)
        {
            if (self.Grounded || self.Wire.Phase == WirePhase.Attached)
            {
                return false;
            }
            float depth = self.Position.Y - world.Bounds.Value.MinY;
            return depth > 0f && !world.Collision.Raycast(self.Position, new Vector3(0f, -1f, 0f), depth, out _);
        }

        private static bool NearEdge(StageBounds bounds, Vector2 position)
        {
            return position.X < bounds.MinX + KoEdgeDistance || position.X > bounds.MaxX - KoEdgeDistance || position.Y > bounds.MaxY - KoEdgeDistance
                || position.Y < bounds.MinY + KoEdgeDistance;
        }

        // 戻る先: ステージの内側で、今より高い所（アンカーがあれば、中央に近いもの）。
        private static Vector2 SafePoint(SimWorld world, Vector2 position)
        {
            StageBounds bounds = world.Bounds.Value;
            float centerX = (bounds.MinX + bounds.MaxX) * 0.5f;
            Vector2 best = new Vector2(centerX, Math.Max(position.Y + 6f, bounds.MinY + EdgeMargin + 6f));
            float bestScore = float.MaxValue;
            foreach (Vector3 anchor in world.Collision.AnchorPoints)
            {
                if (anchor.Y < position.Y)
                {
                    continue;
                }
                float score = MathF.Abs(anchor.X - centerX) + Vector2.Distance(Flat(anchor), position) * 0.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = Flat(anchor);
                }
            }
            return best;
        }

        // 行き詰まったときの行き先（ステージの内側の、ランダムな少し高い所）。
        private Vector2 WanderPoint(SimWorld world, Vector2 position, int tick)
        {
            float x = position.X + (Random(tick, 700) * 2f - 1f) * 10f;
            float y = position.Y + 4f;
            if (world.Bounds.HasValue)
            {
                StageBounds bounds = world.Bounds.Value;
                x = Math.Clamp(x, bounds.MinX + EdgeMargin + 2f, bounds.MaxX - EdgeMargin - 2f);
            }
            return new Vector2(x, y);
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
                if (opponent.Combat.IsUntouchable)
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
        private float Random(int tick, int purpose)
        {
            return StableHash.Range(_salt * 7919 + _index, purpose, tick, 10000) / 10000f;
        }

        private bool Chance(int tick, int purpose, float probability)
        {
            return Random(tick, purpose) < probability;
        }

        // 点数のゆらぎ（0.85〜1.15 倍。CPU どうしが同じ動きにならないように）。
        private float Jitter(int tick, int purpose)
        {
            return 0.85f + 0.3f * Random(tick / Math.Max(1, _tuning.DecideEveryTicks), purpose);
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
