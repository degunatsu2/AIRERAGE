using Airerage.Core.Items;
using Airerage.Runtime.Settings;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// アイテムの名前と効果の説明（ユーザーの案。docs/20_item_plan.md）。オプションの LANGUAGE に合わせて日本語か英語にする（Loc）。
    /// 数はアイテムの値（ItemTuning の初めの値）に合わせて書く。
    /// </summary>
    public static class ItemTexts
    {
        /// <summary>アイテム id の名前（なしは空）。</summary>
        public static string Name(ItemId id)
        {
            switch (id)
            {
                case ItemId.QuickBooster:
                    return Loc.T("QUICK BOOSTER", "クイックブースター");
                case ItemId.EnergyBooster:
                    return Loc.T("ENERGY BOOSTER", "エナジーブースター");
                case ItemId.UltimateBooster:
                    return Loc.T("ULTIMATE BOOSTER", "アルティメットブースター");
                case ItemId.QuickBoosterEx:
                    return Loc.T("QUICK BOOSTER EX", "クイックブースター極");
                case ItemId.EnergyBoosterEx:
                    return Loc.T("ENERGY BOOSTER EX", "エナジーブースター極");
                case ItemId.UltimateBoosterEx:
                    return Loc.T("ULTIMATE BOOSTER EX", "アルティメットブースター極");
                case ItemId.PhotonShield:
                    return Loc.T("PHOTON SHIELD", "光子の盾");
                case ItemId.MagazineMagazine:
                    return Loc.T("MAGAZINE MAGAZINE", "マガジンマガジン");
                case ItemId.GreenBanana:
                    return Loc.T("GREEN BANANA", "グリーンバナナ");
                case ItemId.DiamondDumbbell:
                    return Loc.T("DIAMOND DUMBBELL", "ダイアモンドアレイ");
                case ItemId.EnergyShot:
                    return Loc.T("ENERGY SHOT", "エネルギーショット");
                case ItemId.Yaguraotoshi:
                    return Loc.T("YAGURAOTOSHI", "名刀『櫓落とし』");
                case ItemId.EarBone:
                    return Loc.T("EAR BONE", "イヤーボーン");
                case ItemId.IcarusWing:
                    return Loc.T("ICARUS WING", "イカロスウイング");
                case ItemId.SandstoneBall:
                    return Loc.T("SANDSTONE BALL", "砂岩ボール");
                default:
                    return string.Empty;
            }
        }

        /// <summary>パッシブかアクティブか（と、アクティブのクールタイム）の短い札。</summary>
        public static string Kind(ItemId id)
        {
            if (!ItemCatalog.IsValid(id))
            {
                return string.Empty;
            }
            if (!ItemCatalog.IsActive(id))
            {
                return Loc.T("PASSIVE", "パッシブ");
            }
            int seconds = new ItemTuning().CooldownOf(id) / 60;
            return Loc.T($"ACTIVE  CT {seconds}s", $"アクティブ・CT{seconds}秒");
        }

        /// <summary>アイテム id の効果の説明（なしは空）。</summary>
        public static string Description(ItemId id)
        {
            switch (id)
            {
                case ItemId.QuickBooster:
                    return Loc.T("Q cooldown -20%.", "Qのクールタイムが20%減る。");
                case ItemId.EnergyBooster:
                    return Loc.T("E cooldown -20%.", "Eのクールタイムが20%減る。");
                case ItemId.UltimateBooster:
                    return Loc.T("R gauge cost -20%.", "Rのゲージの消費量が20%減る。");
                case ItemId.QuickBoosterEx:
                    return Loc.T("Q cooldown -40%, but E cooldown +20%.", "Qのクールタイムが40%減るが、Eのクールタイムが20%増える。");
                case ItemId.EnergyBoosterEx:
                    return Loc.T("E cooldown -40%, but Q cooldown +20%.", "Eのクールタイムが40%減るが、Qのクールタイムが20%増える。");
                case ItemId.UltimateBoosterEx:
                    return Loc.T("R gauge cost -40%, but Q and E cooldowns +10% each.",
                        "Rのゲージの消費量が40%減るが、QとEのクールタイムが10%ずつ増える。");
                case ItemId.PhotonShield:
                    return Loc.T("Guard cooldown -20%.", "ガードのクールタイムが20%減る。");
                case ItemId.MagazineMagazine:
                    return Loc.T("Projectile skills and items get +1 stack.", "投射物の技とアイテムのスタック数が1増える。");
                case ItemId.GreenBanana:
                    return Loc.T("Placed objects of skills and items are 1.5x larger.", "設置物の技とアイテムの見た目と判定が1.5倍になる。");
                case ItemId.DiamondDumbbell:
                    return Loc.T("Direct-hit skills and items deal 1.5x damage.", "直接攻撃の技とアイテムの威力が1.5倍になる。");
                case ItemId.EnergyShot:
                    return Loc.T("Fires a blue-flamed bolt toward the pointer. Damage 10.",
                        "マウスポインターの方向へ、青い炎をまとった光弾を撃つ。威力10。");
                case ItemId.Yaguraotoshi:
                    return Loc.T("Three slashes (5 each), a 0.5 s chant, then a dash toward the pointer and a flash along the path (30, FATAL).",
                        "照準の方向へ縦・横・斜めに3回斬り（威力5）、0.5秒の詠唱の後に突進し、移動の前と後を結ぶ一閃（威力30・FATAL）。");
                case ItemId.EarBone:
                    return Loc.T("After a 0.2 s chant, a wind pushes everything else out of the circle around you. No damage.",
                        "0.2秒の詠唱の後、自分の周りの円の中のほかの物を、すべて円の外へ押し出す風を起こす。威力はない。");
                case ItemId.IcarusWing:
                    return Loc.T("After a 0.2 s chant, fly freely for 1 s at your ground speed with the stick.",
                        "0.2秒の詠唱の後、1秒間羽が生え、上下左右の入力で空中を地上の速さで自由に動ける。");
                case ItemId.SandstoneBall:
                    return Loc.T("Drops a sandstone ball in front. Attacks launch it; a moving ball hits by its speed. Vanishes after 5 s.",
                        "目の前に砂岩のボールを出す。攻撃で打つとその吹き飛びで飛び、動いているボールは速さに応じた攻撃になる。5秒で消える（消えてからクールタイム）。");
                default:
                    return string.Empty;
            }
        }
    }
}
