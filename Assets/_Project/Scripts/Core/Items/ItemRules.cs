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

namespace Airerage.Core.Items
{
    /// <summary>
    /// アイテムの規則（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。SimWorld が 1 ティックの中で次の順に呼ぶ:
    /// 1. BeforeMove（キャラクターを動かす前、1 人ずつ）: クールタイムを数え、キー（Z・X・C と捨てるキー）で拾う・捨てる・使う。
    ///    使っているアイテムの段階（詠唱・振り・突進・飛行）に合わせて、動きの入力と速度を決める。
    ///    - 空いた枠のキーを押すと、重なっている落ちているアイテムのうちいちばん近いものを、その枠に拾う。
    ///    - アクティブアイテムの枠のキーは、押して離すと使う（照準の方向に出すアイテムは、押している間に水色の矢印で狙う。2026-10-29 の
    ///      ユーザーの指示 AttackData.AimHold と同じ。押している間に捨てるキーを押すと、使わずに捨てる）。
    ///    - 枠のキーを押しながら捨てるキーを押すと、そのアイテムを足もとに捨てる（クールタイムはアイテムと一緒に残る）。
    /// 2. AfterMove（1 人ずつ）: 詠唱・振り・突進の間は、その場に止める（重力で落ちない）。飛行の間は飛ぶ速さを保つ。
    /// 3. StepWorld（全員を動かした後、命中の前）: 使っているアイテムの攻撃（櫓落としの振りと一閃）とイヤーボーンの風、アイテムの物
    ///    （落ちているアイテム・エネルギーショット・砂岩ボール）を進め、アイテムドロップのモードならアイテムを落とす。攻撃の命中は hits に
    ///    集めて、HitResolver がキャラクターの攻撃と一緒に当てる。
    /// パッシブアイテムの効果は、調整値を変えて表す（ConfigFor。クールタイム・ゲージ・スタック数・設置物の大きさ・技の威力）。
    /// </summary>
    public static partial class ItemRules
    {
        /// <summary>アイテムバッグの枠のキー（Z・X・C）。</summary>
        public const InputButtons SlotButtons = InputButtons.Item1 | InputButtons.Item2 | InputButtons.Item3;

        /// <summary>アイテムのキーすべて（枠のキーと捨てるキー）。</summary>
        public const InputButtons Buttons = SlotButtons | InputButtons.ItemDrop;

        private const int DropSalt = 9101;
        private const int PlaceSalt = 9203;

        // 落ちているアイテムの Stored（足場に止まっているか）。
        private const int Falling = 0;
        private const int Resting = 1;

        /// <summary>枠 slot（0〜2）のキー。</summary>
        public static InputButtons SlotButton(int slot)
        {
            switch (slot)
            {
                case 0:
                    return InputButtons.Item1;
                case 1:
                    return InputButtons.Item2;
                case 2:
                    return InputButtons.Item3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, "アイテムの枠は 0〜2");
            }
        }

        /// <summary>
        /// 持って始めるアイテム（プリアイテム）: items の前から、使えるアイテムを count 個まで、Z・X・C の枠の順に入れる。
        /// </summary>
        public static ItemBag StartingBag(IReadOnlyList<ItemId> items, int count)
        {
            var bag = new ItemBag();
            if (items == null)
            {
                return bag;
            }
            int slot = 0;
            int limit = Math.Min(count, ItemCatalog.BagSlots);
            for (int i = 0; i < items.Count && slot < limit; i++)
            {
                if (ItemCatalog.IsValid(items[i]))
                {
                    bag.Set(slot++, items[i]);
                }
            }
            return bag;
        }

        private const int RandomStartSalt = 9307;

        /// <summary>
        /// 持って始めるアイテムを決める: items の最初が「ランダムセレクト」（ItemId.Random）なら、試合の種 seed と人の番号 player から、
        /// count 個の違うアイテムを選ぶ（全員が同じ結果になる。ユーザーの要望、2026-11-05）。そうでなければ items のまま。
        /// </summary>
        public static ItemId[] ResolveStartItems(IReadOnlyList<ItemId> items, int count, int seed, int player)
        {
            if (items == null || items.Count == 0)
            {
                return new ItemId[0];
            }
            if (items[0] != ItemId.Random)
            {
                var copy = new ItemId[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    copy[i] = items[i];
                }
                return copy;
            }
            var left = new List<ItemId>();
            for (int i = 0; i < ItemCatalog.Count; i++)
            {
                left.Add(ItemCatalog.At(i));
            }
            int wanted = Math.Max(0, Math.Min(Math.Min(count, ItemCatalog.BagSlots), left.Count));
            var picked = new ItemId[wanted];
            for (int k = 0; k < wanted; k++)
            {
                int index = StableHash.Range(seed, RandomStartSalt + player, k, left.Count);
                picked[k] = left[index];
                left.RemoveAt(index);
            }
            return picked;
        }

        // ---------------------------------------------------------------- パッシブ

        /// <summary>
        /// 持っているパッシブアイテムを数えたもの（調整値の写しを使い回すための鍵。アイテムごとに 2 ビット。パッシブを持っていなければ 0）。
        /// </summary>
        public static int PassiveKey(in ItemBag bag)
        {
            // パッシブの番号を小さい順に 1 バイトずつ並べる（持つ順に関係なく、同じ持ち物なら同じ鍵）。
            int a = PassiveOrZero(bag.Get(0));
            int b = PassiveOrZero(bag.Get(1));
            int c = PassiveOrZero(bag.Get(2));
            if (a > b)
            {
                (a, b) = (b, a);
            }
            if (b > c)
            {
                (b, c) = (c, b);
            }
            if (a > b)
            {
                (a, b) = (b, a);
            }
            return a | (b << 8) | (c << 16);
        }

        private static int PassiveOrZero(ItemId id)
        {
            return ItemCatalog.IsPassive(id) ? (int)id : 0;
        }

        /// <summary>Q（slot が Special1）・E（Special2）のクールタイムにかける数（パッシブのブースター。重なれば掛ける）。</summary>
        public static float CooldownScale(in ItemBag bag, ItemTuning tuning, AttackSlot slot)
        {
            bool q = slot == AttackSlot.Special1;
            float scale = 1f;
            scale *= Pow(1f - tuning.BoosterPercent / 100f, bag.CountOf(q ? ItemId.QuickBooster : ItemId.EnergyBooster));
            scale *= Pow(1f - tuning.BoosterExPercent / 100f, bag.CountOf(q ? ItemId.QuickBoosterEx : ItemId.EnergyBoosterEx));
            // 極の引き換え: クイック極は E、エナジー極は Q（ユーザーの文は「E が増加」だが、クイック極と対になる Q と読んだ【仮】）。
            scale *= Pow(1f + tuning.BoosterExPenaltyPercent / 100f, bag.CountOf(q ? ItemId.EnergyBoosterEx : ItemId.QuickBoosterEx));
            scale *= Pow(1f + tuning.UltimateExPenaltyPercent / 100f, bag.CountOf(ItemId.UltimateBoosterEx));
            return scale;
        }

        /// <summary>R のゲージの消費量にかける数。</summary>
        public static float GaugeScale(in ItemBag bag, ItemTuning tuning)
        {
            return Pow(1f - tuning.BoosterPercent / 100f, bag.CountOf(ItemId.UltimateBooster))
                * Pow(1f - tuning.BoosterExPercent / 100f, bag.CountOf(ItemId.UltimateBoosterEx));
        }

        /// <summary>ガードのクールタイムにかける数（光子の盾）。</summary>
        public static float GuardScale(in ItemBag bag, ItemTuning tuning)
        {
            return Pow(1f - tuning.ShieldPercent / 100f, bag.CountOf(ItemId.PhotonShield));
        }

        /// <summary>投射物の技とアイテムのスタック数に足す数（マガジンマガジン）。</summary>
        public static int ExtraCharges(in ItemBag bag, ItemTuning tuning)
        {
            return bag.CountOf(ItemId.MagazineMagazine) * tuning.MagazineCharges;
        }

        /// <summary>設置物の大きさにかける数（グリーンバナナ）。</summary>
        public static float SizeScale(in ItemBag bag, ItemTuning tuning)
        {
            return Pow(tuning.BananaScale, bag.CountOf(ItemId.GreenBanana));
        }

        /// <summary>直接攻撃の技とアイテムの威力の上乗せ（%。ダイアモンドアレイ）。</summary>
        public static int DamageBonusPercent(in ItemBag bag, ItemTuning tuning)
        {
            return bag.CountOf(ItemId.DiamondDumbbell) * tuning.DiamondDamagePercent;
        }

        /// <summary>
        /// 投射物を撃つ技か（マガジンマガジンでスタック数が増える）: コイントス、スペースミサイル、クリーム、タレントスナッチ、フラック、
        /// マイントラップ【仮】。
        /// </summary>
        public static bool IsProjectileSkill(SkillEffect effect)
        {
            switch (effect)
            {
                case SkillEffect.Projectile:
                case SkillEffect.Missile:
                case SkillEffect.Cream:
                case SkillEffect.TalentSnatch:
                case SkillEffect.Flak:
                case SkillEffect.Mine:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// キャラクターの持っているパッシブアイテムで値を変えた調整値（パッシブを持っていなければ config のまま）。Q・E のクールタイム、
        /// R のゲージの消費量、ガードのクールタイム、投射物の技のスタック数、設置物の大きさ、Q・E・R の威力（AttackPower の上乗せ）。
        /// 同じ持ち物には同じ写しを使えるよう、呼ぶ側（SimWorld）が PassiveKey で覚えておく。
        /// </summary>
        public static CharacterConfig ConfigFor(in ItemBag bag, CharacterConfig config, ItemTuning tuning)
        {
            if (PassiveKey(bag) == 0)
            {
                return config;
            }
            CombatTuning combat = config.Combat.Clone();
            combat.Special1CooldownTicks = Scale(combat.Special1CooldownTicks, CooldownScale(bag, tuning, AttackSlot.Special1), 0);
            combat.Special2CooldownTicks = Scale(combat.Special2CooldownTicks, CooldownScale(bag, tuning, AttackSlot.Special2), 0);
            combat.SuperGaugeCost = Scale(combat.SuperGaugeCost, GaugeScale(bag, tuning), 1);
            combat.GuardCooldownTicks = Scale(combat.GuardCooldownTicks, GuardScale(bag, tuning), 0);
            int extra = ExtraCharges(bag, tuning);
            if (extra > 0)
            {
                if (combat.Special1 != null && IsProjectileSkill(combat.Special1.Effect))
                {
                    combat.Special1MaxCharges += extra;
                }
                if (combat.Special2 != null && IsProjectileSkill(combat.Special2.Effect))
                {
                    combat.Special2MaxCharges += extra;
                }
            }
            combat.SkillDamageBonusPercent += DamageBonusPercent(bag, tuning);
            ApplyPassiveCombat(bag, combat, tuning);
            float size = SizeScale(bag, tuning);
            SkillTuning skills = size != 1f ? ScaledPlacements(config.Skills, size) : config.Skills;
            return new CharacterConfig
            {
                Movement = PassiveMovement(bag, config.Movement, tuning), Wire = PassiveWire(bag, config.Wire, tuning), Combat = combat, Skills = skills,
            };
        }

        // 設置物（ミラーボール、セーフティボックス、風の帯、ワープポータル、わたがしの足場、クリーム、地雷）の大きさを scale 倍にした写し【仮】。
        private static SkillTuning ScaledPlacements(SkillTuning skills, float scale)
        {
            SkillTuning copy = skills.Clone();
            copy.MirrorBall = skills.MirrorBall.Clone();
            copy.MirrorBall.Radius *= scale;
            copy.Safe = skills.Safe.Clone();
            copy.Safe.HalfWidth *= scale;
            copy.Safe.HalfHeight *= scale;
            copy.WindZone = skills.WindZone.Clone();
            copy.WindZone.Length *= scale;
            copy.WindZone.HalfWidth *= scale;
            copy.Portal = skills.Portal.Clone();
            copy.Portal.Radius *= scale;
            copy.CottonCandy = skills.CottonCandy.Clone();
            copy.CottonCandy.HalfLength *= scale;
            copy.CottonCandy.HalfThickness *= scale;
            copy.Cream = skills.Cream.Clone();
            copy.Cream.PatchHalfLength *= scale;
            copy.Juggernaut = skills.Juggernaut.Clone();
            copy.Juggernaut.MineRadius *= scale;
            copy.Juggernaut.MineExplosionRadius *= scale;
            return copy;
        }

        private static int Scale(int value, float scale, int min)
        {
            return Math.Max(min, (int)MathF.Round(value * scale));
        }

        private static float Pow(float value, int count)
        {
            float result = 1f;
            for (int i = 0; i < count; i++)
            {
                result *= value;
            }
            return result;
        }

        // ---------------------------------------------------------------- スタックとクールタイム

        /// <summary>アクティブアイテム id のスタック数（投射物のアイテムはマガジンマガジンで増える）。</summary>
        public static int MaxCharges(in ItemBag bag, ItemId id, ItemTuning tuning)
        {
            return IsProjectileItem(id) ? 1 + ExtraCharges(bag, tuning) : 1;
        }

        /// <summary>投射物のアクティブアイテムか（マガジンマガジンでスタックが増える）: エネルギーショット、グラップリングボム、チェーンフック【仮】。</summary>
        public static bool IsProjectileItem(ItemId id)
        {
            return id == ItemId.EnergyShot || id == ItemId.GrappleBomb || id == ItemId.ChainHook;
        }

        /// <summary>枠 slot のアクティブアイテムを今使えるスタックの数。</summary>
        public static int ChargesLeft(in ItemBag bag, int slot, ItemTuning tuning)
        {
            ItemId id = bag.Get(slot);
            if (!ItemCatalog.IsActive(id) || bag.GetCooldown(slot) == CombatState.CooldownWhileActive)
            {
                return 0;
            }
            return Math.Max(0, MaxCharges(bag, id, tuning) - bag.GetSpent(slot));
        }

        // スタックを 1 つ使う（数えていなければ数え始める）。waitForEffect なら、効果が終わるまで数えない（砂岩ボール）。
        private static void Spend(ref ItemBag bag, int slot, int cooldown, bool waitForEffect)
        {
            bag.SetSpent(slot, bag.GetSpent(slot) + 1);
            if (waitForEffect)
            {
                bag.SetCooldown(slot, CombatState.CooldownWhileActive);
            }
            else if (bag.GetCooldown(slot) <= 0)
            {
                bag.SetCooldown(slot, cooldown);
            }
        }

        // クールタイムを 1 ティック進める（0 になったらスタックを 1 つ戻し、まだ使ったスタックがあれば次を数える）。
        private static void CountDown(ref ItemBag bag, ItemTuning tuning)
        {
            for (int slot = 0; slot < ItemCatalog.BagSlots; slot++)
            {
                int cooldown = bag.GetCooldown(slot);
                if (cooldown <= 0)
                {
                    continue;
                }
                cooldown--;
                if (cooldown == 0)
                {
                    int spent = Math.Max(0, bag.GetSpent(slot) - 1);
                    bag.SetSpent(slot, spent);
                    if (spent > 0)
                    {
                        cooldown = tuning.CooldownOf(bag.Get(slot));
                    }
                }
                bag.SetCooldown(slot, cooldown);
            }
        }

        /// <summary>
        /// 砂岩ボールが消えた（持ち主 owner の、ボールを待っている枠のクールタイムを数え始める）。
        /// </summary>
        public static void ReleaseBall(List<CharacterState> states, int owner, ItemTuning tuning)
        {
            if (owner < 0 || owner >= states.Count)
            {
                return;
            }
            CharacterState state = states[owner];
            for (int slot = 0; slot < ItemCatalog.BagSlots; slot++)
            {
                if (state.Items.Get(slot) == ItemId.SandstoneBall && state.Items.GetCooldown(slot) == CombatState.CooldownWhileActive)
                {
                    state.Items.SetCooldown(slot, tuning.BallCooldownTicks);
                    states[owner] = state;
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- 1. 動く前

        /// <summary>照準の方向に出すアクティブアイテムか（押している間に水色の矢印で狙う）。</summary>
        public static bool IsAimed(ItemId id)
        {
            switch (id)
            {
                case ItemId.EnergyShot:
                case ItemId.Yaguraotoshi:
                case ItemId.SandstoneBall:
                case ItemId.GrappleBomb:
                case ItemId.FoldingPlatform:
                case ItemId.ChainHook:
                case ItemId.PocketRocket:
                case ItemId.BottledThunder:
                case ItemId.DecoyDoll:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>照準の方向に出すアイテムを、キーを押して狙っている間か（ワイヤーの照準の表示を隠す。SkillRules.AimsWithCursor）。</summary>
        public static bool AimsWithCursor(in CharacterState state)
        {
            ItemBag bag = state.Items;
            for (int slot = 0; slot < ItemCatalog.BagSlots; slot++)
            {
                if ((bag.Armed & (1 << slot)) != 0 && IsAimed(bag.Get(slot)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>狙っている枠（押して離すと使う。なければ -1）。</summary>
        public static int ArmedSlot(in ItemBag bag)
        {
            for (int slot = 0; slot < ItemCatalog.BagSlots; slot++)
            {
                if ((bag.Armed & (1 << slot)) != 0)
                {
                    return slot;
                }
            }
            return -1;
        }

        /// <summary>
        /// 1. キャラクター index を動かす前に、アイテムのキーを処理する（tick は進めた後のティック。playing でなければ（カウントダウン）、
        /// クールタイムだけ数える）。キャラクターの動きに使う入力を返す（使っているアイテムの間は、動きの入力を止める・変える）。
        /// </summary>
        public static PlayerInput BeforeMove(ref CharacterState state, PlayerInput input, int index, CharacterConfig config, ItemTuning tuning,
            WorldObjects objects, ICollisionQuery collision, int tick, bool playing)
        {
            ItemBag bag = state.Items;
            CountDown(ref bag, tuning);
            CountStatuses(ref bag, tuning);
            InputButtons held = input.Buttons & Buttons;
            InputButtons pressed = held & ~bag.PreviousButtons;
            InputButtons released = bag.PreviousButtons & ~held;
            bag.PreviousButtons = held;
            CombatState combat = state.Combat;
            if (!playing || combat.HasLeft || combat.IsWaitingToRespawn || combat.IsAway)
            {
                bag.Armed = 0;
                if (combat.IsWaitingToRespawn || combat.HasLeft)
                {
                    ReleaseRemote(ref bag, tuning);
                    bag = bag.AfterKnockout();
                }
                state.Items = bag;
                return input;
            }
            if (bag.Acting && Interrupted(state))
            {
                EndAct(ref bag);
            }
            if (bag.Acting && input.IsHeld(InputButtons.Wire) && (state.PreviousButtons & InputButtons.Wire) == 0
                && state.Combat.WireCancelCooldownTicksLeft == 0 && state.Wire.Phase == WirePhase.Ready && state.Combat.HitstopTicksLeft == 0)
            {
                // 使っているアイテムをワイヤーでキャンセルする（攻撃→ワイヤーと同じ。クールタイムが始まる。ユーザーの要望、2026-11-06）。
                // 動きを止める入力に変えないので、このティックにワイヤーを撃つ。
                EndAct(ref bag);
                state.Combat.WireCancelCooldownTicksLeft = config.Combat.WireCancelCooldownTicks;
            }
            if (bag.Armed != 0 && input.HasAim)
            {
                bag.ActAim = Vector2.Normalize(input.Aim);
            }

            bool dropHeld = (held & InputButtons.ItemDrop) != 0;
            bool dropPressed = (pressed & InputButtons.ItemDrop) != 0;
            bool discarded = false;
            for (int slot = 0; slot < ItemCatalog.BagSlots; slot++)
            {
                InputButtons key = SlotButton(slot);
                bool slotHeld = (held & key) != 0;
                if (dropHeld && slotHeld && (dropPressed || (pressed & key) != 0))
                {
                    bag.Armed &= (byte)~(1 << slot);
                    if (bag.Get(slot) != ItemId.None && !(bag.Acting && bag.ActSlot == slot))
                    {
                        Discard(state, ref bag, slot, index, config, objects, tuning, tick);
                    }
                    discarded = true;
                }
            }
            if (!discarded)
            {
                for (int slot = 0; slot < ItemCatalog.BagSlots; slot++)
                {
                    InputButtons key = SlotButton(slot);
                    ItemId id = bag.Get(slot);
                    byte bit = (byte)(1 << slot);
                    if ((pressed & key) != 0 && !dropHeld)
                    {
                        if (id == ItemId.None)
                        {
                            if (TryPickup(state, ref bag, slot, index, config, objects, tuning, tick))
                            {
                                break;
                            }
                        }
                        else if (ItemCatalog.IsActive(id) && (ChargesLeft(bag, slot, tuning) > 0 || CanRecall(bag, id)) && !bag.Acting)
                        {
                            // 押した: 狙い始める（離したら使う）。照準がなければ向いている方向。
                            bag.Armed |= bit;
                            bag.ActAim = AimOf(input, state);
                        }
                    }
                    if ((released & key) != 0 && (bag.Armed & bit) != 0)
                    {
                        bag.Armed &= (byte)~bit;
                        if (CanRecall(bag, id) && CanActivate(state))
                        {
                            // 帰還のリモコンの 2 回目: 印へ瞬間移動する（当たった攻撃はキャンセルする）。
                            if (state.Action.Kind == ActionKind.Attack)
                            {
                                state.Action = default;
                            }
                            Recall(ref state, ref bag, slot, config, tuning);
                            break;
                        }
                        if (ItemCatalog.IsActive(id) && ChargesLeft(bag, slot, tuning) > 0 && !bag.Acting && CanActivate(state))
                        {
                            Activate(ref state, ref bag, slot, id, index, config, objects, collision, tuning, tick);
                            break;
                        }
                    }
                }
            }
            state.Items = bag;
            return bag.Acting ? ActInput(ref state, input, config, tuning, collision) : input;
        }

        // 照準の向き（照準の入力があればその向き、なければ移動のスティック、それもなければ向いている方向）。
        private static Vector2 AimOf(PlayerInput input, in CharacterState state)
        {
            if (input.HasAim)
            {
                return Vector2.Normalize(input.Aim);
            }
            Vector2 move = input.Move;
            if (move.LengthSquared() > 0.09f)
            {
                return Vector2.Normalize(move);
            }
            return new Vector2(state.Facing < 0 ? -1f : 1f, 0f);
        }

        /// <summary>
        /// アクティブアイテムを使い始められるか（動ける状態で、のけぞり・長押し・構えの間でない）。攻撃の間は、その攻撃が当たっていれば
        /// キャンセルして使える（技と同じ。止まっている間（ヒットストップ）でもよい。ユーザーの要望、2026-11-06）。
        /// </summary>
        public static bool CanActivate(in CharacterState state)
        {
            CombatState c = state.Combat;
            bool cancel = state.Action.Kind == ActionKind.Attack && state.Action.Connected;
            return !c.IsWaitingToRespawn && !c.HasLeft && !c.IsAway && !c.IsCaptured && !c.IsDrowning && !c.IsFrozen && !c.IsStunned
                && (c.HitstopTicksLeft == 0 || cancel) && state.Action.Kind != ActionKind.Hitstun
                && (state.Action.Kind != ActionKind.Attack || cancel)
                && !c.Holding && !c.FlakStance && !state.Items.Acting;
        }

        // 使っているアイテムを止める状態（のけぞり・感電・凍結・握られている・溺れている・飛び去っている・撃破）。
        private static bool Interrupted(in CharacterState state)
        {
            CombatState c = state.Combat;
            return state.Action.Kind == ActionKind.Hitstun || c.IsStunned || c.IsFrozen || c.IsCaptured || c.IsDrowning || c.IsAway
                || c.IsWaitingToRespawn;
        }

        private static void EndAct(ref ItemBag bag)
        {
            bag.Act = 0;
            bag.ActTicks = 0;
            bag.ActHitMask = 0;
        }

        // 枠 slot のアイテムを足もとに捨てる（クールタイムと使ったスタックはアイテムに残す）。
        private static void Discard(in CharacterState state, ref ItemBag bag, int slot, int index, CharacterConfig config, WorldObjects objects,
            ItemTuning tuning, int tick)
        {
            ItemId id = bag.Get(slot);
            int cooldown = bag.GetCooldown(slot);
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.DroppedItem,
                Owner = index,
                Position = new Vector2(state.Position.X, state.Position.Y),
                Velocity = new Vector2(0f, tuning.DiscardPopSpeed),
                SpawnTick = tick,
                ExpireTick = int.MaxValue,
                Variant = (byte)id,
                Stored = Falling,
                ChargeMilli = tick + tuning.DiscardPickupDelayTicks,
                Damage = Math.Max(0, cooldown),
                HitMask = (uint)bag.GetSpent(slot),
            });
            bag.Set(slot, ItemId.None);
            bag.DropCount++;
        }

        // 重なっている落ちているアイテムのうち、いちばん近いもの（同じ距離なら番号の小さいもの）を枠 slot に拾う。拾えたら true。
        private static bool TryPickup(in CharacterState state, ref ItemBag bag, int slot, int index, CharacterConfig config, WorldObjects objects,
            ItemTuning tuning, int tick)
        {
            Vector3 half = config.Movement.HalfExtents;
            int best = -1;
            float bestDistance = float.MaxValue;
            // 拾える範囲は、アイテムの箱とキャラクターの箱の重なりより少し広い（ユーザーの要望、2026-11-05）。
            float reach = tuning.DropHalfSize + tuning.DropPickupMargin;
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Kind != WorldObjectKind.DroppedItem || item.Ended || (item.Owner == index && tick < item.ChargeMilli))
                {
                    continue;
                }
                float dx = item.Position.X - state.Position.X;
                float dy = item.Position.Y - state.Position.Y;
                if (MathF.Abs(dx) > half.X + reach || MathF.Abs(dy) > half.Y + reach)
                {
                    continue;
                }
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    best = o;
                    bestDistance = distance;
                }
            }
            if (best < 0)
            {
                return false;
            }
            WorldObject picked = objects[best];
            picked.Ended = true;
            objects[best] = picked;
            var id = (ItemId)picked.Variant;
            bag.Set(slot, id);
            bag.SetCooldown(slot, picked.Damage);
            bag.SetSpent(slot, (int)picked.HitMask);
            bag.PickupCount++;
            bag.LastPicked = (byte)id;
            return true;
        }

        // アクティブアイテムを使う。
        private static void Activate(ref CharacterState state, ref ItemBag bag, int slot, ItemId id, int index, CharacterConfig config,
            WorldObjects objects, ICollisionQuery collision, ItemTuning tuning, int tick)
        {
            if (state.Action.Kind == ActionKind.Attack)
            {
                // 当たった攻撃をキャンセルして使う（技と同じ。攻撃→攻撃の 1 回とは別枠。ユーザーの要望、2026-11-06）。
                state.Action = default;
            }
            Vector2 aim = bag.ActAim.LengthSquared() > 0.01f ? Vector2.Normalize(bag.ActAim) : new Vector2(state.Facing < 0 ? -1f : 1f, 0f);
            var center = new Vector2(state.Position.X, state.Position.Y);
            bag.UseCount++;
            bag.LastUsed = (byte)id;
            switch (id)
            {
                case ItemId.EnergyShot:
                    Spend(ref bag, slot, tuning.ShotCooldownTicks, false);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.ItemShot,
                        Owner = index,
                        Position = Reach(collision, center, aim, tuning.ShotMuzzle, tuning.ShotRadius),
                        Velocity = aim * tuning.ShotSpeed,
                        SpawnTick = tick,
                        ExpireTick = int.MaxValue,
                        Variant = (byte)id,
                    });
                    break;
                case ItemId.GrappleBomb:
                case ItemId.FoldingPlatform:
                case ItemId.ChainHook:
                case ItemId.PocketTrampoline:
                case ItemId.DecoyDoll:
                case ItemId.BottledThunder:
                case ItemId.Reflector:
                case ItemId.BubbleBarrier:
                case ItemId.BigMagnet:
                case ItemId.ReturnRemote:
                    ActivateMore(ref state, ref bag, slot, id, index, config, objects, collision, tuning, tick, aim, center);
                    break;
                case ItemId.SandstoneBall:
                    Spend(ref bag, slot, tuning.BallCooldownTicks, true);
                    float radius = tuning.BallRadius * SizeScale(bag, tuning);
                    objects.Add(new WorldObject
                    {
                        Kind = WorldObjectKind.SandBall,
                        Owner = index,
                        Position = Reach(collision, center, aim, tuning.BallSpawnDistance, radius),
                        SpawnTick = tick,
                        ExpireTick = int.MaxValue,
                        Variant = (byte)id,
                        End = new Vector2(radius, 0f),
                        ChargeMilli = tick,
                    });
                    break;
                default:
                    Spend(ref bag, slot, tuning.CooldownOf(id), false);
                    bag.Act = (byte)id;
                    bag.ActSlot = (byte)slot;
                    bag.ActTicks = 0;
                    bag.ActHitMask = 0;
                    bag.ActAim = aim;
                    bag.ActFrom = center;
                    bag.ActTo = center;
                    // ワイヤーは外す（詠唱と振りの間は、その場に止まる）。
                    WireController.Interrupt(ref state, config.Wire);
                    if (id == ItemId.Yaguraotoshi && MathF.Abs(aim.X) > 1e-3f)
                    {
                        state.Facing = aim.X > 0f ? 1 : -1;
                    }
                    break;
            }
        }

        // center から向き dir へ distance だけ離れた点（途中に地形があれば、その手前。半径 radius の物を置ける所）。
        private static Vector2 Reach(ICollisionQuery collision, Vector2 center, Vector2 dir, float distance, float radius)
        {
            if (collision == null || distance <= 0f)
            {
                return center + dir * distance;
            }
            var half = new Vector3(radius, radius, 0.5f);
            if (collision.SweepBox(new Vector3(center.X, center.Y, 0f), half, new Vector3(dir.X, dir.Y, 0f) * distance, out CastHit hit))
            {
                return center + dir * Math.Max(0f, hit.Distance - 0.02f);
            }
            return center + dir * distance;
        }

        // 使っているアイテムの段階に合わせた、動きの入力（詠唱・振り・突進の間は止める。飛行の間は移動のスティックで飛ぶ）。
        private static PlayerInput ActInput(ref CharacterState state, PlayerInput input, CharacterConfig config, ItemTuning tuning,
            ICollisionQuery collision)
        {
            ItemBag bag = state.Items;
            int t = bag.ActTicks;
            var still = new PlayerInput(0, 0, InputButtons.None, input.AimX, input.AimY);
            switch ((ItemId)bag.Act)
            {
                case ItemId.Yaguraotoshi:
                {
                    int dashStart = tuning.SwordSlashesTicks + tuning.SwordCastTicks;
                    if (t == dashStart)
                    {
                        // 詠唱が終わった: 照準の方向へ突進する（地形の手前まで）。
                        var from = new Vector2(state.Position.X, state.Position.Y);
                        Vector3 half = config.Movement.HalfExtents;
                        Vector2 to = from + bag.ActAim * tuning.SwordDashDistance;
                        if (collision != null && collision.SweepBox(state.Position, half,
                                new Vector3(bag.ActAim.X, bag.ActAim.Y, 0f) * tuning.SwordDashDistance, out CastHit hit))
                        {
                            to = from + bag.ActAim * Math.Max(0f, hit.Distance - 0.02f);
                        }
                        bag.ActFrom = from;
                        bag.ActTo = to;
                    }
                    if (t >= dashStart && t < dashStart + tuning.SwordDashTicks)
                    {
                        float progress = (t - dashStart + 1) / (float)tuning.SwordDashTicks;
                        Vector2 at = Vector2.Lerp(bag.ActFrom, bag.ActTo, progress);
                        state.Position = new Vector3(at.X, at.Y, 0f);
                    }
                    state.Velocity = Vector3.Zero;
                    state.Items = bag;
                    return still;
                }
                case ItemId.PocketRocket:
                {
                    // 照準の方向へ急加速する（重力を受けない。地形に当たったら止まる。ジャンプとワイヤーは使わない）。
                    Vector2 velocity = bag.ActAim * tuning.RocketSpeed;
                    state.Velocity = new Vector3(velocity.X, velocity.Y, 0f);
                    return new PlayerInput(0, 0, input.Buttons & ~(InputButtons.Jump | InputButtons.Wire), input.AimX, input.AimY);
                }
                case ItemId.IcarusWing:
                    if (t < tuning.WingCastTicks)
                    {
                        state.Velocity = Vector3.Zero;
                        return still;
                    }
                    // 飛んでいる: 上下左右の入力で、地上の速さで自由に動く（重力を受けない）。ジャンプとワイヤーは使わない。
                    Vector2 move = input.Move;
                    state.Velocity = new Vector3(move.X, move.Y, 0f) * config.Movement.RunSpeed;
                    return new PlayerInput(0, 0, input.Buttons & ~(InputButtons.Jump | InputButtons.Wire), input.AimX, input.AimY);
                default:
                    state.Velocity = Vector3.Zero;
                    return still;
            }
        }

        // ---------------------------------------------------------------- 2. 動いた後

        /// <summary>その場に止まっている段階か（詠唱・振り・突進。重力で落ちず、動いた分を戻す）。</summary>
        public static bool Holds(in CharacterState state, ItemTuning tuning)
        {
            ItemBag bag = state.Items;
            switch ((ItemId)bag.Act)
            {
                case ItemId.Yaguraotoshi:
                case ItemId.EarBone:
                case ItemId.Hourglass:
                case ItemId.BankSeal:
                    return true;
                case ItemId.IcarusWing:
                    return bag.ActTicks < tuning.WingCastTicks;
                default:
                    return false;
            }
        }

        /// <summary>飛んでいる段階か（イカロスウイングとポケットロケット。重力を受けず、決めた速さで動く）。</summary>
        public static bool Flying(in CharacterState state, ItemTuning tuning)
        {
            ItemBag bag = state.Items;
            return (bag.Act == (byte)ItemId.IcarusWing && bag.ActTicks >= tuning.WingCastTicks) || bag.Act == (byte)ItemId.PocketRocket;
        }

        /// <summary>
        /// 2. キャラクターを動かした後: 止まっている段階なら、動かす前の位置 held に戻して止める。飛んでいる段階なら、重力で落ちた分の
        /// 速さを戻す（次のティックも飛ぶ速さで動く）。
        /// </summary>
        public static void AfterMove(ref CharacterState state, Vector3 held, Vector3 flightVelocity, ItemTuning tuning)
        {
            if (state.Action.Kind == ActionKind.Hitstun)
            {
                return;
            }
            if (Holds(state, tuning))
            {
                state.Position = held;
                state.Velocity = Vector3.Zero;
            }
            else if (Flying(state, tuning))
            {
                state.Velocity = flightVelocity;
            }
        }

        /// <summary>詠唱の段階なら、その長さ（ティック）と経った長さ（キャラクターの下の棒のゲージ）。詠唱していなければ false。</summary>
        public static bool TryGetCast(in CharacterState state, ItemTuning tuning, out int total, out int elapsed)
        {
            ItemBag bag = state.Items;
            int t = bag.ActTicks;
            switch ((ItemId)bag.Act)
            {
                case ItemId.Yaguraotoshi:
                    total = tuning.SwordCastTicks;
                    elapsed = t - tuning.SwordSlashesTicks;
                    return total > 0 && elapsed >= 0 && elapsed < total;
                case ItemId.EarBone:
                case ItemId.IcarusWing:
                case ItemId.Hourglass:
                case ItemId.BankSeal:
                    total = tuning.CastTicksOf((ItemId)bag.Act);
                    elapsed = t;
                    return total > 0 && elapsed < total;
                default:
                    total = 0;
                    elapsed = 0;
                    return false;
            }
        }

        /// <summary>イカロスウイングで飛べる残り（ティック。飛んでいなければ 0）。</summary>
        public static int FlightTicksLeft(in CharacterState state, ItemTuning tuning)
        {
            return Flying(state, tuning) && state.Items.Act == (byte)ItemId.IcarusWing
                ? tuning.WingCastTicks + tuning.WingFlightTicks - state.Items.ActTicks
                : 0;
        }

        // ---------------------------------------------------------------- 3. 全員を動かした後

        /// <summary>
        /// 3. 使っているアイテムの攻撃と風、アイテムの物、アイテムドロップを進める（tick は進めた後のティック、active は試合中か）。
        /// 攻撃の命中は hits に加える。
        /// </summary>
        public static void StepWorld(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, ItemTuning tuning, WorldObjects objects,
            ICollisionQuery collision, StageBounds? bounds, MatchRules rules, IReadOnlyList<CoinSpawnPoint> spawns, int tick, bool active,
            List<HitRecord> hits, CoinLedger ledger = null)
        {
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].Items.Acting)
                {
                    StepAct(states, configs, i, tuning, objects, hits, ledger, tick);
                }
            }
            StepStatuses(states, configs, tuning, objects, ledger, tick);
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Ended)
                {
                    continue;
                }
                switch (item.Kind)
                {
                    case WorldObjectKind.DroppedItem:
                        StepDropped(ref item, collision, bounds, tuning, tick);
                        break;
                    case WorldObjectKind.ItemShot:
                        StepShot(ref item, states, configs, collision, bounds, tuning, hits);
                        break;
                    case WorldObjectKind.SandBall:
                        StepBall(ref item, states, configs, collision, bounds, tuning, tick, hits);
                        break;
                    default:
                        if (!StepMore(ref item, states, configs, collision, bounds, tuning, tick, hits))
                        {
                            continue;
                        }
                        break;
                }
                objects[o] = item;
            }
            SpreadDropped(objects, collision, tuning);
            if (active)
            {
                SpawnDrops(objects, rules, spawns, bounds, tuning, tick);
            }
        }

        // 使っているアイテムの段階を 1 ティック進める（櫓落としの振りと一閃、イヤーボーンの風）。
        private static void StepAct(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int index, ItemTuning tuning,
            WorldObjects objects, List<HitRecord> hits, CoinLedger ledger, int tick)
        {
            CharacterState state = states[index];
            ItemBag bag = state.Items;
            int t = bag.ActTicks;
            int end;
            switch ((ItemId)bag.Act)
            {
                case ItemId.Yaguraotoshi:
                {
                    int slashes = tuning.SwordSlashesTicks;
                    int dashStart = slashes + tuning.SwordCastTicks;
                    end = dashStart + tuning.SwordDashTicks;
                    if (t < slashes)
                    {
                        int slash = t / tuning.SwordSlashTicks;
                        int inSlash = t % tuning.SwordSlashTicks;
                        if (inSlash == 0)
                        {
                            bag.ActHitMask = 0;
                        }
                        // 振りの判定は、振りの真ん中の数ティックだけ（縦・横・斜め）。
                        if (inSlash >= 2 && inSlash < Math.Max(3, tuning.SwordSlashTicks - 3))
                        {
                            float aimAngle = MathF.Atan2(bag.ActAim.Y, bag.ActAim.X);
                            float turn = slash == 0 ? MathF.PI / 2f : slash == 1 ? 0f : MathF.PI / 4f;
                            Vector2 center = new Vector2(state.Position.X, state.Position.Y) + bag.ActAim * tuning.SwordReach;
                            var half = new Vector2(tuning.SwordSlashHalfLength, tuning.SwordSlashHalfWidth);
                            HitBox(states, configs, index, center, half, aimAngle + turn, tuning.SwordSlashHit, false, ref bag, tuning, hits);
                        }
                    }
                    else if (t == end - 1)
                    {
                        // 突進し終えた: 移動前と移動後の地点を結ぶ一閃（当たると FATAL）。
                        Vector2 from = bag.ActFrom;
                        Vector2 to = bag.ActTo;
                        Vector2 delta = to - from;
                        float length = delta.Length();
                        float angle = length > 1e-4f ? MathF.Atan2(delta.Y, delta.X) : MathF.Atan2(bag.ActAim.Y, bag.ActAim.X);
                        bag.ActHitMask = 0;
                        HitBox(states, configs, index, (from + to) * 0.5f, new Vector2(length * 0.5f + tuning.SwordFlashHalfWidth, tuning.SwordFlashHalfWidth),
                            angle, tuning.SwordFlashHit, true, ref bag, tuning, hits);
                        bag.FlashCount++;
                    }
                    break;
                }
                case ItemId.EarBone:
                    end = tuning.EarCastTicks + 1;
                    if (t == tuning.EarCastTicks)
                    {
                        Blow(states, configs, index, objects, tuning);
                        state = states[index];
                        bag.BurstCount++;
                    }
                    break;
                case ItemId.IcarusWing:
                    end = tuning.WingCastTicks + tuning.WingFlightTicks;
                    break;
                case ItemId.PocketRocket:
                    end = tuning.RocketTicks;
                    break;
                case ItemId.Hourglass:
                    end = tuning.HourglassCastTicks + 1;
                    if (t == tuning.HourglassCastTicks)
                    {
                        Slow(states, index, tuning);
                        state = states[index];
                        bag.BurstCount++;
                    }
                    break;
                case ItemId.BankSeal:
                    end = tuning.SealCastTicks + 1;
                    if (t == tuning.SealCastTicks)
                    {
                        states[index] = state;
                        Seal(states, index, ref bag, ledger, tuning, tick);
                        state = states[index];
                    }
                    break;
                default:
                    end = 0;
                    break;
            }
            bag.ActTicks = t + 1;
            if (bag.ActTicks >= end)
            {
                EndAct(ref bag);
            }
            state.Items = bag;
            states[index] = state;
        }

        // 傾けた箱（中心 center、半分の大きさ half、傾き angle）に重なった相手に、攻撃 data を当てる（同じ振りでは 1 人に 1 回）。
        private static void HitBox(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int attacker, Vector2 center, Vector2 half,
            float angle, AttackHit data, bool fatal, ref ItemBag bag, ItemTuning tuning, List<HitRecord> hits)
        {
            CharacterState me = states[attacker];
            int facing = MathF.Cos(angle) >= 0f ? 1 : -1;
            int bonus = DamageBonusPercent(me.Items, tuning);
            var power = new AttackPower(1f, bonus, 0, fatal, sources: bonus != 0 ? PowerSource.Item : PowerSource.None);
            for (int v = 0; v < states.Count; v++)
            {
                if (v == attacker || (bag.ActHitMask & (1u << v)) != 0 || states[v].Combat.IsUntouchable || TeamRules.Allies(states, attacker, v))
                {
                    continue;
                }
                Vector3 target = states[v].Position;
                Vector3 targetHalf = configs[v].Movement.HalfExtents;
                if (HitShapes.Overlaps(center, half, angle, new Vector2(target.X, target.Y), new Vector2(targetHalf.X, targetHalf.Y)))
                {
                    // アイテムの攻撃（技の枠はないので Ground。HitRecord.Attack は表示とデバッグにしか使わない）。
                    hits.Add(new HitRecord(attacker, v, AttackSlot.Ground, 0, data, facing, power));
                    bag.ActHitMask |= 1u << v;
                }
            }
        }

        // イヤーボーンの風: 自分を中心とした円の中にいる、自分以外のキャラクターと動く物を、円の外へ押し出す（攻撃ではない）。
        private static void Blow(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int index, WorldObjects objects, ItemTuning tuning)
        {
            var center = new Vector2(states[index].Position.X, states[index].Position.Y);
            for (int v = 0; v < states.Count; v++)
            {
                if (v == index)
                {
                    continue;
                }
                CharacterState other = states[v];
                if (other.Combat.IsUntouchable || other.Combat.IsAway)
                {
                    continue;
                }
                var position = new Vector2(other.Position.X, other.Position.Y);
                Vector2 away = position - center;
                if (away.LengthSquared() > tuning.EarRadius * tuning.EarRadius)
                {
                    continue;
                }
                Vector2 dir = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : new Vector2(other.Facing < 0 ? -1f : 1f, 0f);
                WireController.Interrupt(ref other, configs[v].Wire);
                Vector2 push = dir * tuning.EarPushSpeed + new Vector2(0f, tuning.EarPushUp);
                other.Velocity = new Vector3(push.X, push.Y, 0f);
                other.Grounded = false;
                states[v] = other;
            }
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Ended || !Pushable(item))
                {
                    continue;
                }
                Vector2 away = item.Position - center;
                if (away.LengthSquared() > tuning.EarRadius * tuning.EarRadius)
                {
                    continue;
                }
                Vector2 dir = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : Vector2.UnitY;
                item.Velocity = dir * tuning.EarPushSpeed;
                if (item.Kind == WorldObjectKind.DroppedItem)
                {
                    item.Stored = Falling;
                    item.Velocity += new Vector2(0f, tuning.EarPushUp);
                }
                objects[o] = item;
            }
        }

        // 風で押し出せる物（飛んでいる物と、落ちているアイテム・砂岩ボール）【仮】。
        private static bool Pushable(in WorldObject item)
        {
            switch (item.Kind)
            {
                case WorldObjectKind.DroppedItem:
                case WorldObjectKind.ItemShot:
                case WorldObjectKind.SandBall:
                case WorldObjectKind.Projectile:
                case WorldObjectKind.Missile:
                case WorldObjectKind.CreamShot:
                case WorldObjectKind.Macaron:
                case WorldObjectKind.TrapBall:
                case WorldObjectKind.Shell:
                case WorldObjectKind.SnatchShot:
                case WorldObjectKind.SmokeBall:
                    return true;
                case WorldObjectKind.Mine:
                    return item.Variant == 0;
                default:
                    return false;
            }
        }

        // 落ちているアイテム: 重力で落ち、足場に着いたら止まる（足場がなくなれば、また落ちる）。ステージの外に出たら消える。
        private static void StepDropped(ref WorldObject item, ICollisionQuery collision, StageBounds? bounds, ItemTuning tuning, int tick)
        {
            var half = new Vector3(tuning.DropHalfSize, tuning.DropHalfSize, 0.5f);
            var center = new Vector3(item.Position.X, item.Position.Y, 0f);
            if (item.Stored == Resting)
            {
                if (collision != null && collision.SweepBox(center, half, new Vector3(0f, -0.05f, 0f), out _))
                {
                    return;
                }
                item.Stored = Falling;
            }
            float dt = SimTime.DeltaTime;
            item.Velocity = new Vector2(item.Velocity.X, Math.Max(-tuning.DropMaxFallSpeed, item.Velocity.Y - tuning.DropGravity * dt));
            Vector2 position = item.Position;
            Vector2 velocity = item.Velocity;
            if (MoveAxis(collision, ref position, half, velocity.X * dt, true))
            {
                velocity.X = 0f;
            }
            if (MoveAxis(collision, ref position, half, velocity.Y * dt, false))
            {
                if (velocity.Y < 0f)
                {
                    // 足場に着いた: その場に止まる。初めて着いたときから、拾われずに 30 秒で消える（ユーザーの要望、2026-11-05。
                    // 消すのは SkillRules.StepObjects の ExpireTick）。
                    item.Stored = Resting;
                    velocity = Vector2.Zero;
                    if (item.ExpireTick == int.MaxValue)
                    {
                        item.ExpireTick = tick + tuning.DropLifetimeTicks;
                    }
                }
                else
                {
                    velocity.Y = 0f;
                }
            }
            item.Position = position;
            item.Velocity = velocity;
            if (bounds.HasValue && !bounds.Value.Contains(new Vector3(item.Position.X, item.Position.Y, 0f)))
            {
                item.Ended = true;
            }
        }

        // 箱（半分の大きさ half）を、横（horizontal）か縦に distance だけ動かす。地形に当たったら手前で止めて true。
        private static bool MoveAxis(ICollisionQuery collision, ref Vector2 position, Vector3 half, float distance, bool horizontal)
        {
            if (distance == 0f)
            {
                return false;
            }
            var delta = horizontal ? new Vector3(distance, 0f, 0f) : new Vector3(0f, distance, 0f);
            if (collision != null && collision.SweepBox(new Vector3(position.X, position.Y, 0f), half, delta, out CastHit hit))
            {
                float moved = MathF.Sign(distance) * Math.Max(0f, hit.Distance - 0.01f);
                position += horizontal ? new Vector2(moved, 0f) : new Vector2(0f, moved);
                return true;
            }
            position += horizontal ? new Vector2(distance, 0f) : new Vector2(0f, distance);
            return false;
        }

        // エネルギーショット: まっすぐ飛び、最初に当たった相手に当たって消える。地形・届く距離・ステージの外で消える。
        private static void StepShot(ref WorldObject shot, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            ICollisionQuery collision, StageBounds? bounds, ItemTuning tuning, List<HitRecord> hits)
        {
            Vector2 delta = shot.Velocity * SimTime.DeltaTime;
            var half = new Vector3(tuning.ShotRadius * 0.5f, tuning.ShotRadius * 0.5f, 0.5f);
            if (collision != null && collision.SweepBox(new Vector3(shot.Position.X, shot.Position.Y, 0f), half, new Vector3(delta.X, delta.Y, 0f),
                    out CastHit hit))
            {
                shot.Position += Vector2.Normalize(delta) * hit.Distance;
                shot.Ended = true;
                return;
            }
            shot.Position += delta;
            shot.ChargeMilli += (long)MathF.Round(delta.Length() * 1000f);
            if (shot.ChargeMilli >= (long)(tuning.ShotRange * 1000f)
                || (bounds.HasValue && !bounds.Value.Contains(new Vector3(shot.Position.X, shot.Position.Y, 0f))))
            {
                shot.Ended = true;
                return;
            }
            for (int v = 0; v < states.Count; v++)
            {
                if (v == shot.Owner || states[v].Combat.IsUntouchable || TeamRules.Allies(states, shot.Owner, v))
                {
                    continue;
                }
                Vector3 target = states[v].Position;
                Vector3 targetHalf = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.X - shot.Position.X) < targetHalf.X + tuning.ShotRadius
                    && MathF.Abs(target.Y - shot.Position.Y) < targetHalf.Y + tuning.ShotRadius)
                {
                    int facing = shot.Velocity.X >= 0f ? 1 : -1;
                    hits.Add(new HitRecord(shot.Owner, v, AttackSlot.Ground, 0, tuning.ShotHit, facing, AttackPower.Normal, fromObject: true));
                    shot.Ended = true;
                    return;
                }
            }
        }

        // 砂岩ボール: 重力で落ち、地形で跳ね返る。速く動いているときに触れた人に当たる。5 秒で消え、持ち主のクールタイムが始まる。
        private static void StepBall(ref WorldObject ball, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            ICollisionQuery collision, StageBounds? bounds, ItemTuning tuning, int tick, List<HitRecord> hits)
        {
            if (tick - ball.SpawnTick >= tuning.BallLifetimeTicks
                || (bounds.HasValue && !bounds.Value.Contains(new Vector3(ball.Position.X, ball.Position.Y, 0f))))
            {
                ball.Ended = true;
                ReleaseBall(states, ball.Owner, tuning);
                return;
            }
            float dt = SimTime.DeltaTime;
            float radius = ball.End.X > 0f ? ball.End.X : tuning.BallRadius;
            var half = new Vector3(radius, radius, 0.5f);
            Vector2 velocity = ball.Velocity;
            velocity.Y = Math.Max(-tuning.BallMaxFallSpeed, velocity.Y - tuning.BallGravity * dt);
            // 横と縦に分けて動かす（転がっている間に、地面に引っかからないように）。
            Vector2 position = ball.Position;
            if (MoveAxis(collision, ref position, half, velocity.X * dt, true))
            {
                velocity.X = -velocity.X * tuning.BallRestitution;
            }
            if (MoveAxis(collision, ref position, half, velocity.Y * dt, false))
            {
                bool landed = velocity.Y < 0f;
                velocity.Y = -velocity.Y * tuning.BallRestitution;
                if (landed && MathF.Abs(velocity.Y) < 2f)
                {
                    // 地面を転がる: だんだん遅くなる。
                    velocity.Y = 0f;
                    float speedX = MathF.Abs(velocity.X);
                    velocity.X = MathF.Sign(velocity.X) * Math.Max(0f, speedX - tuning.BallRollFriction * dt);
                }
            }
            ball.Position = position;
            ball.Velocity = velocity;

            float speed = velocity.Length();
            if (speed < tuning.BallMinHarmSpeed)
            {
                return;
            }
            int attacker = ball.Stored > 0 ? ball.Stored - 1 : ball.Owner;
            for (int v = 0; v < states.Count; v++)
            {
                if ((ball.HitMask & (1u << v)) != 0 || states[v].Combat.IsUntouchable || v == attacker || TeamRules.Allies(states, attacker, v))
                {
                    continue;
                }
                Vector3 target = states[v].Position;
                Vector3 targetHalf = configs[v].Movement.HalfExtents;
                if (MathF.Abs(target.X - ball.Position.X) >= targetHalf.X + radius || MathF.Abs(target.Y - ball.Position.Y) >= targetHalf.Y + radius)
                {
                    continue;
                }
                Vector2 dir = velocity / speed;
                float knockback = speed * tuning.BallKnockbackPerSpeed;
                var data = new AttackHit
                {
                    Damage = Math.Max(1, (int)MathF.Round(speed * tuning.BallDamagePerSpeed)),
                    Gauge = tuning.BallGauge,
                    HitstopTicks = tuning.BallHitstopTicks,
                    HitstunTicks = tuning.BallHitstunTicks,
                    KnockbackX = MathF.Abs(dir.X) * knockback,
                    KnockbackY = dir.Y * knockback,
                };
                hits.Add(new HitRecord(attacker, v, AttackSlot.Ground, 0, data, dir.X >= 0f ? 1 : -1, AttackPower.Normal, fromObject: true));
                ball.HitMask |= 1u << v;
                // 当たったら勢いが落ちる【仮】。
                ball.Velocity *= 0.5f;
            }
        }

        /// <summary>
        /// 攻撃 attacker の判定（中心 center、半分の大きさ half）が重なった砂岩ボールを、吹き飛び knockback（m/s）で打つ（HitResolver から）。
        /// 打ったら true。同じ攻撃の同じ判定では 1 回だけ（key。HitResolver.ObjectHitKey）。
        /// </summary>
        public static bool StrikeBalls(WorldObjects objects, int attacker, int key, Vector2 center, Vector2 half, Vector2 knockback, int tick,
            ItemTuning tuning)
        {
            if (objects == null || tuning == null)
            {
                return false;
            }
            bool struck = false;
            for (int i = 0; i < objects.Count; i++)
            {
                WorldObject ball = objects[i];
                if (ball.Kind != WorldObjectKind.SandBall || ball.Ended || ball.HasHitKey(key))
                {
                    continue;
                }
                float radius = ball.End.X > 0f ? ball.End.X : tuning.BallRadius;
                if (MathF.Abs(ball.Position.X - center.X) >= half.X + radius || MathF.Abs(ball.Position.Y - center.Y) >= half.Y + radius)
                {
                    continue;
                }
                Vector2 velocity = knockback * tuning.BallStrikeScale;
                float speed = velocity.Length();
                if (speed > tuning.BallMaxSpeed)
                {
                    velocity *= tuning.BallMaxSpeed / speed;
                }
                ball.Velocity = velocity;
                ball.Stored = attacker + 1;
                ball.ChargeMilli = tick;
                ball.HitMask = 0;
                ball.AddHitKey(key);
                objects[i] = ball;
                struck = true;
            }
            return struck;
        }

        // アイテムドロップ: 試合が始まってから決まった間隔で 1 個ずつ、コインの出現地点のどれかに落とす（アイテムと地点は試合の種と回数で決まる）。
        private static void SpawnDrops(WorldObjects objects, MatchRules rules, IReadOnlyList<CoinSpawnPoint> spawns, StageBounds? bounds,
            ItemTuning tuning, int tick)
        {
            if (rules.ItemMode != ItemMode.ItemDrop || rules.ItemDropPool == 0)
            {
                return;
            }
            int interval = tuning.DropIntervalOf(rules.ItemDropRate);
            int elapsed = tick - rules.CountdownTicks;
            if (elapsed <= 0 || elapsed % interval != 0)
            {
                return;
            }
            int onField = 0;
            for (int o = 0; o < objects.Count; o++)
            {
                onField += objects[o].Kind == WorldObjectKind.DroppedItem && !objects[o].Ended ? 1 : 0;
            }
            if (onField >= tuning.MaxFieldItems)
            {
                return;
            }
            int n = elapsed / interval;
            ulong pool = (ulong)rules.ItemDropPool;
            int choices = ItemCatalog.CountIn(pool);
            if (choices == 0)
            {
                return;
            }
            int pick = StableHash.Range(rules.Seed, DropSalt, n, choices);
            ItemId id = ItemId.None;
            for (int i = 0, seen = 0; i < ItemCatalog.Count; i++)
            {
                if ((pool & (1UL << i)) != 0 && seen++ == pick)
                {
                    id = ItemCatalog.At(i);
                    break;
                }
            }
            Vector2 position;
            if (spawns != null && spawns.Count > 0)
            {
                position = spawns[StableHash.Range(rules.Seed, PlaceSalt, n, spawns.Count)].Position;
            }
            else if (bounds.HasValue)
            {
                StageBounds b = bounds.Value;
                float x = b.MinX + (b.MaxX - b.MinX) * StableHash.Range(rules.Seed, PlaceSalt, n, 1000) / 1000f;
                position = new Vector2(x, b.MaxY - 4f);
            }
            else
            {
                position = new Vector2(0f, 10f);
            }
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.DroppedItem,
                Owner = 0,
                Position = position,
                SpawnTick = tick,
                ExpireTick = int.MaxValue,
                Variant = (byte)id,
                Stored = Falling,
                // 持ち主はいない（Owner は 0 だが、拾えるまでの待ちはない）。
                ChargeMilli = 0,
            });
        }
    }
}
