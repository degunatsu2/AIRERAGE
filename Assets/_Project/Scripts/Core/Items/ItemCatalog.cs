using System;

namespace Airerage.Core.Items
{
    /// <summary>
    /// アイテムの種類（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。番号は表の No と同じで、パッシブ → アクティブの順に並べる
    /// （ユーザーの指示、2026-11-05。初期の 15 個に追加の 25 個を合わせて並べ直した）。番号は状態・ネットワーク・設定の保存に使うので、
    /// これから足すときは、パッシブとアクティブの境（FirstActive）がずれないように番号を振り直し、状態の形式の版を上げる。0 は「なし」。
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
        SkyBoots = 11,
        GoldMagnet = 12,
        VaultKey = 13,
        IronBoots = 14,
        SteelWire = 15,
        LongHook = 16,
        SpareBattery = 17,
        GoldBell = 18,
        PhoenixFeather = 19,
        AdversityCharm = 20,
        SpikeSoles = 21,
        FeatherHat = 22,

        // アクティブ（対応するキーで使い、クールタイムに入る）。
        EnergyShot = 23,
        Yaguraotoshi = 24,
        EarBone = 25,
        IcarusWing = 26,
        SandstoneBall = 27,
        GrappleBomb = 28,
        Reflector = 29,
        FoldingPlatform = 30,
        BigMagnet = 31,
        ReturnRemote = 32,
        Hourglass = 33,
        ChainHook = 34,
        BubbleBarrier = 35,
        PocketRocket = 36,
        PocketTrampoline = 37,
        DecoyDoll = 38,
        BottledThunder = 39,
        BankSeal = 40,

        /// <summary>
        /// 「ランダムセレクト」の印（プリアイテムの初期アイテムの選び方。試合を始めるときに、初期アイテム数だけランダムなアイテムに置き換える。
        /// 状態には入らない）。
        /// </summary>
        Random = 255,
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
        public const int Count = 40;

        /// <summary>アクティブアイテムの最初の番号（これより前はパッシブ）。</summary>
        public const ItemId FirstActive = ItemId.EnergyShot;

        /// <summary>パッシブアイテムの数と、アクティブアイテムの数。</summary>
        public const int PassiveCount = (int)FirstActive - 1;
        public const int ActiveCount = Count - PassiveCount;

        /// <summary>持てる数（アイテムバッグの枠。Z・X・C）。</summary>
        public const int BagSlots = 3;

        /// <summary>すべてのアイテムのビット（MatchRules.ItemDropPool の初めの値）。</summary>
        public const ulong AllMask = Count >= 64 ? ulong.MaxValue : (1UL << Count) - 1UL;

        /// <summary>番号 id のアイテムがあるか（1〜Count。ランダムセレクトの印は含まない）。</summary>
        public static bool IsValid(ItemId id)
        {
            return id >= (ItemId)1 && (int)id <= Count;
        }

        /// <summary>アクティブ（キーで使う）アイテムか。</summary>
        public static bool IsActive(ItemId id)
        {
            return id >= FirstActive && (int)id <= Count;
        }

        /// <summary>パッシブ（持っているだけで効く）アイテムか。</summary>
        public static bool IsPassive(ItemId id)
        {
            return IsValid(id) && !IsActive(id);
        }

        /// <summary>アイテムの、選んでよいアイテムの集まり（ビット）でのビット。</summary>
        public static ulong Bit(ItemId id)
        {
            return IsValid(id) ? 1UL << ((int)id - 1) : 0UL;
        }

        /// <summary>番号 index（0〜Count-1）のアイテム（表の No の順。パッシブ → アクティブ）。</summary>
        public static ItemId At(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"アイテムの番号は 0〜{Count - 1}");
            }
            return (ItemId)(index + 1);
        }

        /// <summary>mask の中のアイテムの数。</summary>
        public static int CountIn(ulong mask)
        {
            int count = 0;
            for (int i = 0; i < Count; i++)
            {
                count += (mask & (1UL << i)) != 0 ? 1 : 0;
            }
            return count;
        }
    }
}
