using System;
using System.Collections.Generic;
using Airerage.Core.Items;
using Airerage.Core.Match;
using UnityEngine;

namespace Airerage.Runtime.Settings
{
    /// <summary>
    /// アイテムのモードの設定（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。オプションの ITEMS のページで決め、PlayerPrefs に保存する。
    /// - モード（DEFAULT ／ ITEM DROP ／ PRE-ITEM）・初期アイテム数（1〜3）・ドロップ頻度（少・中・多）: ローカル対戦・シングルモード・
    ///   トレーニングのルール（ネットワーク対戦はホストが待機所で決める。待機所の初めの値にもする）。
    /// - 初期アイテム（最大 3 つ。Z・X・C の順）: プリアイテムで持って始めるアイテム（ローカルの人と、ネットワーク対戦の自分の初めの値）。
    /// - ドロップするアイテム（ビット）: アイテムドロップで出るアイテム（ローカルと、ネットワーク対戦のホストの初めの値）。
    /// 読み込みと保存だけを受け持つ（使う側が Load で読む）。
    /// </summary>
    [Serializable]
    public sealed class ItemSettings
    {
        private const string Prefix = "Options.Items.";
        private const string ModeKey = Prefix + "Mode";
        private const string CountKey = Prefix + "StartCount";
        private const string RateKey = Prefix + "DropRate";
        // アイテムの番号を並べ直した（2026-11-05）ので、前の保存とは別の名前で保存する。
        private const string StartKey = Prefix + "StartItems2";
        private const string PoolKey = Prefix + "DropPool2";

        private static readonly string[] AllKeys = { ModeKey, CountKey, RateKey, StartKey, PoolKey };

        public ItemMode Mode = ItemMode.Default;
        public int StartCount = ItemCatalog.BagSlots;

        /// <summary>ドロップ頻度（0 少・1 中・2 多）。</summary>
        public int DropRate = 1;

        /// <summary>初期アイテム（Z・X・C の順。なしは None。最初が ItemId.Random ならランダムセレクト）。</summary>
        public ItemId[] StartItems = new ItemId[ItemCatalog.BagSlots];

        /// <summary>ドロップするアイテム（ItemCatalog.Bit の和。0 にはしない）。</summary>
        public ulong DropPool = ItemCatalog.AllMask;

        /// <summary>保存した設定を読む（保存していない項目は初めの値）。</summary>
        public static ItemSettings Load()
        {
            int mode = PlayerPrefs.GetInt(ModeKey, (int)ItemMode.Default);
            var settings = new ItemSettings
            {
                Mode = mode >= 0 && mode <= (int)ItemMode.PreItem ? (ItemMode)mode : ItemMode.Default,
                StartCount = Mathf.Clamp(PlayerPrefs.GetInt(CountKey, ItemCatalog.BagSlots), 1, ItemCatalog.BagSlots),
                DropRate = Mathf.Clamp(PlayerPrefs.GetInt(RateKey, 1), 0, 2),
                StartItems = ParseItems(PlayerPrefs.GetString(StartKey, string.Empty)),
                DropPool = ValidPool(ReadPool(PlayerPrefs.GetString(PoolKey, string.Empty))),
            };
            return settings;
        }

        /// <summary>保存する（PlayerPrefs.Save まで行う）。</summary>
        public void Save()
        {
            PlayerPrefs.SetInt(ModeKey, (int)Mode);
            PlayerPrefs.SetInt(CountKey, Mathf.Clamp(StartCount, 1, ItemCatalog.BagSlots));
            PlayerPrefs.SetInt(RateKey, Mathf.Clamp(DropRate, 0, 2));
            PlayerPrefs.SetString(StartKey, FormatItems(StartItems));
            PlayerPrefs.SetString(PoolKey, ValidPool(DropPool).ToString(System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        /// <summary>保存した設定を消す（初めの値に戻す。テストでも使う）。</summary>
        public static void DeleteSaved()
        {
            foreach (string key in AllKeys)
            {
                PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
        }

        /// <summary>ルールにアイテムのモードを入れる（ローカル対戦・シングルモード・トレーニング）。</summary>
        public void ApplyTo(MatchRules rules)
        {
            if (rules == null)
            {
                return;
            }
            rules.ItemMode = Mode;
            rules.StartItemCount = Mathf.Clamp(StartCount, 1, ItemCatalog.BagSlots);
            rules.ItemDropRate = Mathf.Clamp(DropRate, 0, 2);
            rules.ItemDropPool = (long)ValidPool(DropPool);
        }

        /// <summary>初期アイテムのうち、上限 count までの選んだもの（なしは詰める）。</summary>
        public ItemId[] StartItemsUpTo(int count)
        {
            // ランダムセレクトの印は、そのまま渡す（試合を始めるときに、試合の種から選ぶ。SimWorld.SetItems）。
            return RandomStart ? new[] { ItemId.Random } : Trim(StartItems, count);
        }

        /// <summary>items の選んだもの（なしは詰める）を count 個まで。</summary>
        public static ItemId[] Trim(IReadOnlyList<ItemId> items, int count)
        {
            var picked = new List<ItemId>();
            for (int i = 0; items != null && i < items.Count && picked.Count < Mathf.Clamp(count, 0, ItemCatalog.BagSlots); i++)
            {
                if (ItemCatalog.IsValid(items[i]) && !picked.Contains(items[i]))
                {
                    picked.Add(items[i]);
                }
            }
            return picked.ToArray();
        }

        /// <summary>ドロップするアイテムの集まり（範囲の外のビットを落とし、1 つもなければすべて）。</summary>
        public static ulong ValidPool(ulong pool)
        {
            ulong valid = pool & ItemCatalog.AllMask;
            return valid == 0 ? ItemCatalog.AllMask : valid;
        }

        private static ulong ReadPool(string text)
        {
            return ulong.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong pool)
                ? pool
                : ItemCatalog.AllMask;
        }

        /// <summary>ランダムセレクトを選んでいるか（初期アイテムの最初が ItemId.Random）。</summary>
        public bool RandomStart => StartItems != null && StartItems.Length > 0 && StartItems[0] == ItemId.Random;

        /// <summary>count 個まで、ランダムに違うアイテムを選ぶ（ローカルの CPU の初期アイテム）。</summary>
        public static ItemId[] RandomItems(int count, System.Random random)
        {
            var all = new List<ItemId>();
            for (int i = 0; i < ItemCatalog.Count; i++)
            {
                all.Add(ItemCatalog.At(i));
            }
            var picked = new List<ItemId>();
            int wanted = Mathf.Clamp(count, 0, ItemCatalog.BagSlots);
            while (picked.Count < wanted && all.Count > 0)
            {
                int index = random.Next(all.Count);
                picked.Add(all[index]);
                all.RemoveAt(index);
            }
            return picked.ToArray();
        }

        /// <summary>ドロップ頻度の文字（LOW ／ MID ／ HIGH）。</summary>
        public static string RateLabel(int rate)
        {
            switch (Mathf.Clamp(rate, 0, 2))
            {
                case 0:
                    return Loc.T("LOW", "少");
                case 2:
                    return Loc.T("HIGH", "多");
                default:
                    return Loc.T("MID", "中");
            }
        }

        /// <summary>モードの文字（DEFAULT ／ ITEM DROP ／ PRE-ITEM）。</summary>
        public static string ModeLabel(ItemMode mode)
        {
            switch (mode)
            {
                case ItemMode.ItemDrop:
                    return Loc.T("ITEM DROP", "アイテムドロップ");
                case ItemMode.PreItem:
                    return Loc.T("PRE-ITEM", "プリアイテム");
                default:
                    return Loc.T("DEFAULT", "デフォルト");
            }
        }

        private static ItemId[] ParseItems(string text)
        {
            var items = new ItemId[ItemCatalog.BagSlots];
            string[] parts = (text ?? string.Empty).Split(',');
            for (int i = 0; i < items.Length && i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int value) && (ItemCatalog.IsValid((ItemId)value) || (i == 0 && value == (int)ItemId.Random)))
                {
                    items[i] = (ItemId)value;
                }
            }
            var result = new ItemId[ItemCatalog.BagSlots];
            if (items[0] == ItemId.Random)
            {
                result[0] = ItemId.Random;
                return result;
            }
            ItemId[] trimmed = Trim(items, ItemCatalog.BagSlots);
            Array.Copy(trimmed, result, trimmed.Length);
            return result;
        }

        private static string FormatItems(IReadOnlyList<ItemId> items)
        {
            var parts = new string[ItemCatalog.BagSlots];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = items != null && i < items.Count ? ((int)items[i]).ToString() : "0";
            }
            return string.Join(",", parts);
        }
    }
}
