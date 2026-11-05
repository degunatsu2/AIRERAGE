using System;
using System.Numerics;
using Airerage.Core.Input;

namespace Airerage.Core.Items
{
    /// <summary>
    /// キャラクターのアイテムバッグ（3 枠。Z・X・C）と、使っているアクティブアイテムの段階（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。
    /// 値だけで完結させ、巻き戻しと比較ができるようにする（CharacterState.Items。StateCodec で書く）。撃破されても持ち物とクールタイムは残る。
    /// </summary>
    public struct ItemBag : IEquatable<ItemBag>
    {
        /// <summary>枠ごとのアイテム（ItemId。0 は空き）。</summary>
        public byte Item0;
        public byte Item1;
        public byte Item2;

        /// <summary>
        /// 枠ごとの、スタックが 1 つ戻るまでのティック（0 なら数えていない）。CombatState.CooldownWhileActive（-1）なら、効果（砂岩ボール）が
        /// 終わるまで数えない。
        /// </summary>
        public int Cooldown0;
        public int Cooldown1;
        public int Cooldown2;

        /// <summary>枠ごとの、使ったスタックの数（戻るまでの数。スタックが 1 つなら、使うと 1 でクールタイムが終わると 0）。</summary>
        public byte Spent0;
        public byte Spent1;
        public byte Spent2;

        /// <summary>前のティックに押していたアイテムのボタン（Item1〜3・ItemDrop。押した瞬間を求める。技の入力とは別に持つ）。</summary>
        public InputButtons PreviousButtons;

        /// <summary>押して狙っている枠（ビット。離したら使う。押している間に捨てるキーを押すと、使わずに捨てる）。</summary>
        public byte Armed;

        /// <summary>使っているアクティブアイテム（ItemId。0 ならなし。詠唱・振り・突進・飛行の間）。</summary>
        public byte Act;

        /// <summary>使っているアイテムの枠と、使い始めてからのティック。</summary>
        public byte ActSlot;
        public int ActTicks;

        /// <summary>使ったときの照準の向き（長さ 1）と、突進の始まりと終わり（櫓落とし）。</summary>
        public Vector2 ActAim;
        public Vector2 ActFrom;
        public Vector2 ActTo;

        /// <summary>今の振り・一閃でもう当てた相手（ビット）。</summary>
        public uint ActHitMask;

        /// <summary>拾った回数と最後に拾ったアイテム（拾ったときの表示と音）。</summary>
        public int PickupCount;
        public byte LastPicked;

        /// <summary>アクティブアイテムを使った回数と最後に使ったアイテム（音と表示）、捨てた回数。</summary>
        public int UseCount;
        public byte LastUsed;
        public int DropCount;

        /// <summary>イヤーボーンの風を起こした回数（表示）と、櫓落としの一閃を出した回数（表示）。</summary>
        public int BurstCount;
        public int FlashCount;

        /// <summary>枠 slot のアイテム。</summary>
        public ItemId Get(int slot)
        {
            switch (slot)
            {
                case 0:
                    return (ItemId)Item0;
                case 1:
                    return (ItemId)Item1;
                case 2:
                    return (ItemId)Item2;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, "アイテムの枠は 0〜2");
            }
        }

        /// <summary>枠 slot にアイテム id を入れる（クールタイムと使ったスタックは 0 に戻す）。</summary>
        public void Set(int slot, ItemId id)
        {
            switch (slot)
            {
                case 0:
                    Item0 = (byte)id;
                    break;
                case 1:
                    Item1 = (byte)id;
                    break;
                case 2:
                    Item2 = (byte)id;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, "アイテムの枠は 0〜2");
            }
            SetCooldown(slot, 0);
            SetSpent(slot, 0);
        }

        public int GetCooldown(int slot)
        {
            return slot == 0 ? Cooldown0 : slot == 1 ? Cooldown1 : Cooldown2;
        }

        public void SetCooldown(int slot, int ticks)
        {
            if (slot == 0)
            {
                Cooldown0 = ticks;
            }
            else if (slot == 1)
            {
                Cooldown1 = ticks;
            }
            else
            {
                Cooldown2 = ticks;
            }
        }

        public int GetSpent(int slot)
        {
            return slot == 0 ? Spent0 : slot == 1 ? Spent1 : Spent2;
        }

        public void SetSpent(int slot, int spent)
        {
            byte value = (byte)Math.Max(0, Math.Min(byte.MaxValue, spent));
            if (slot == 0)
            {
                Spent0 = value;
            }
            else if (slot == 1)
            {
                Spent1 = value;
            }
            else
            {
                Spent2 = value;
            }
        }

        /// <summary>アイテム id をいくつ持っているか。</summary>
        public int CountOf(ItemId id)
        {
            return (Item0 == (byte)id ? 1 : 0) + (Item1 == (byte)id ? 1 : 0) + (Item2 == (byte)id ? 1 : 0);
        }

        /// <summary>何か持っているか。</summary>
        public bool IsEmpty => Item0 == 0 && Item1 == 0 && Item2 == 0;

        /// <summary>アクティブアイテムを使っている（詠唱・振り・突進・飛行の）間か。</summary>
        public bool Acting => Act != 0;

        /// <summary>撃破されて戻ったときのバッグ（持ち物とクールタイムは残し、使っている途中のアイテムは止める）。</summary>
        public ItemBag AfterKnockout()
        {
            ItemBag bag = this;
            bag.Act = 0;
            bag.ActTicks = 0;
            bag.ActHitMask = 0;
            bag.Armed = 0;
            return bag;
        }

        public bool Equals(ItemBag other)
        {
            return Item0 == other.Item0 && Item1 == other.Item1 && Item2 == other.Item2
                && Cooldown0 == other.Cooldown0 && Cooldown1 == other.Cooldown1 && Cooldown2 == other.Cooldown2
                && Spent0 == other.Spent0 && Spent1 == other.Spent1 && Spent2 == other.Spent2
                && PreviousButtons == other.PreviousButtons && Armed == other.Armed && Act == other.Act && ActSlot == other.ActSlot && ActTicks == other.ActTicks
                && ActAim.Equals(other.ActAim) && ActFrom.Equals(other.ActFrom) && ActTo.Equals(other.ActTo) && ActHitMask == other.ActHitMask
                && PickupCount == other.PickupCount && LastPicked == other.LastPicked && UseCount == other.UseCount
                && LastUsed == other.LastUsed && DropCount == other.DropCount && BurstCount == other.BurstCount
                && FlashCount == other.FlashCount;
        }

        public override bool Equals(object obj)
        {
            return obj is ItemBag other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Item0, Item1, Item2, Cooldown0, Cooldown1, Cooldown2, Act, ActTicks);
        }

        public override string ToString()
        {
            return $"[{(ItemId)Item0}, {(ItemId)Item1}, {(ItemId)Item2}] act={(ItemId)Act}@{ActTicks}";
        }
    }
}
