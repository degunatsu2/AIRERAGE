using System;

namespace Airerage.Core.Items
{
    /// <summary>
    /// アイテムの種類（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。番号は状態・ネットワーク・設定の保存に使うので、後から変えない
    /// （新しいアイテムは後ろに足す）。0 は「なし」。
    /// </summary>
    public enum ItemId : byte
    {
        None = 0,

        // パッシブ（持っているだけで効く）。
        QuickBooster = 1,
        EnergyBooster = 2,
        UltimateBooster = 3,
        QuickBoosterEx = 4,
        EnergyBoosterEx = 5,
        UltimateBoosterEx = 6,
        PhotonShield = 7,
        MagazineMagazine = 8,
        GreenBanana = 9,
        DiamondDumbbell = 10,

        // アクティブ（対応するキーで使い、クールタイムに入る）。
        EnergyShot = 11,
        Yaguraotoshi = 12,
        EarBone = 13,
        IcarusWing = 14,
        SandstoneBall = 15,
    }

    /// <summary>
    /// アイテムのモード（ユーザーの要望、2026-11-05）。MatchRules.ItemMode。ローカル対戦はオプション、ネットワーク対戦は待機所でホストが選ぶ。
    /// </summary>
    public enum ItemMode : byte
    {
        /// <summary>アイテムなし（今までのルール）。</summary>
        Default = 0,

        /// <summary>ステージにランダムでアイテムが落ちてくる。</summary>
        ItemDrop = 1,

        /// <summary>各自が選んだアイテム（1〜3 個）を持って始める。</summary>
        PreItem = 2,
    }

    /// <summary>アイテムの一覧と、種類ごとの変わらない性質（パッシブかアクティブか）。</summary>
    public static class ItemCatalog
    {
        /// <summary>アイテムの数（番号は 1〜Count）。</summary>
        public const int Count = 15;

        /// <summary>持てる数（アイテムバッグの枠。Z・X・C）。</summary>
        public const int BagSlots = 3;

        /// <summary>すべてのアイテムのビット（MatchRules.ItemDropPool の初めの値）。</summary>
        public const uint AllMask = (1u << Count) - 1u;

        /// <summary>番号 id のアイテムがあるか（1〜Count）。</summary>
        public static bool IsValid(ItemId id)
        {
            return id >= (ItemId)1 && (int)id <= Count;
        }

        /// <summary>アクティブ（キーで使う）アイテムか。</summary>
        public static bool IsActive(ItemId id)
        {
            return id >= ItemId.EnergyShot && (int)id <= Count;
        }

        /// <summary>パッシブ（持っているだけで効く）アイテムか。</summary>
        public static bool IsPassive(ItemId id)
        {
            return IsValid(id) && !IsActive(id);
        }

        /// <summary>アイテムの、選んでよいアイテムの集まり（ビット）でのビット。</summary>
        public static uint Bit(ItemId id)
        {
            return IsValid(id) ? 1u << ((int)id - 1) : 0u;
        }

        /// <summary>番号 index（0〜Count-1）のアイテム。</summary>
        public static ItemId At(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"アイテムの番号は 0〜{Count - 1}");
            }
            return (ItemId)(index + 1);
        }

        /// <summary>mask の中のアイテムの数。</summary>
        public static int CountIn(uint mask)
        {
            int count = 0;
            for (int i = 0; i < Count; i++)
            {
                count += (mask & (1u << i)) != 0 ? 1 : 0;
            }
            return count;
        }
    }
}
