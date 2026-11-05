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
                case ItemId.SkyBoots:
                    return Loc.T("SKY BOOTS", "スカイブーツ");
                case ItemId.GoldMagnet:
                    return Loc.T("GOLD MAGNET", "ゴールドマグネット");
                case ItemId.VaultKey:
                    return Loc.T("VAULT KEY", "金庫の鍵");
                case ItemId.IronBoots:
                    return Loc.T("IRON BOOTS", "鉄の長靴");
                case ItemId.SteelWire:
                    return Loc.T("STEEL WIRE", "鋼のワイヤー");
                case ItemId.LongHook:
                    return Loc.T("LONG HOOK", "ロングフック");
                case ItemId.SpareBattery:
                    return Loc.T("SPARE BATTERY", "予備電池");
                case ItemId.GoldBell:
                    return Loc.T("GOLD BELL", "金の鈴");
                case ItemId.PhoenixFeather:
                    return Loc.T("PHOENIX FEATHER", "不死鳥の羽根");
                case ItemId.AdversityCharm:
                    return Loc.T("ADVERSITY CHARM", "逆境のお守り");
                case ItemId.SpikeSoles:
                    return Loc.T("SPIKE SOLES", "スパイクの靴底");
                case ItemId.FeatherHat:
                    return Loc.T("FEATHER HAT", "羽毛の帽子");
                case ItemId.GrappleBomb:
                    return Loc.T("GRAPPLE BOMB", "グラップリングボム");
                case ItemId.Reflector:
                    return Loc.T("REFLECTOR", "反射板");
                case ItemId.FoldingPlatform:
                    return Loc.T("FOLDING PLATFORM", "折りたたみの足場");
                case ItemId.BigMagnet:
                    return Loc.T("BIG MAGNET", "大きな磁石");
                case ItemId.ReturnRemote:
                    return Loc.T("RETURN REMOTE", "帰還のリモコン");
                case ItemId.Hourglass:
                    return Loc.T("HOURGLASS", "砂時計");
                case ItemId.ChainHook:
                    return Loc.T("CHAIN HOOK", "チェーンフック");
                case ItemId.BubbleBarrier:
                    return Loc.T("BUBBLE BARRIER", "シャボンのバリア");
                case ItemId.PocketRocket:
                    return Loc.T("POCKET ROCKET", "ポケットロケット");
                case ItemId.PocketTrampoline:
                    return Loc.T("POCKET TRAMPOLINE", "携帯トランポリン");
                case ItemId.DecoyDoll:
                    return Loc.T("DECOY DOLL", "身代わり人形");
                case ItemId.BottledThunder:
                    return Loc.T("BOTTLED THUNDER", "瓶詰めの雷雲");
                case ItemId.BankSeal:
                    return Loc.T("BANK SEAL", "確定の印鑑");
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
                case ItemId.SkyBoots:
                    return Loc.T("+1 air jump.",
                        "空中ジャンプが1回増える。");
                case ItemId.GoldMagnet:
                    return Loc.T("Coin pickup range x2.",
                        "コインを拾える範囲が2倍になる。");
                case ItemId.VaultKey:
                    return Loc.T("Banking gives a 10% bonus of the banked coins.",
                        "拠点で確定したとき、確定した枚数の10%のボーナスをもらえる。");
                case ItemId.IronBoots:
                    return Loc.T("Knockback taken -20%, but gravity +30%.",
                        "受ける吹き飛びが20%減るが、受ける重力が30%増える。");
                case ItemId.SteelWire:
                    return Loc.T("Wire cancel cooldown -50%.",
                        "ワイヤーキャンセルのクールタイムが50%減る。");
                case ItemId.LongHook:
                    return Loc.T("Wire range +25%.",
                        "ワイヤーの射程が25%伸びる。");
                case ItemId.SpareBattery:
                    return Loc.T("Gauge from attacks +25%.",
                        "攻撃で得るゲージが25%増える。");
                case ItemId.GoldBell:
                    return Loc.T("Just guard window x1.5.",
                        "ジャストガードの受付時間が1.5倍になる。");
                case ItemId.PhoenixFeather:
                    return Loc.T("Respawn wait -1 s, and coins dropped on KO are halved.",
                        "撃破からの復帰待ちが1秒短くなり、撃破されたときに落とすコインが半分になる。");
                case ItemId.AdversityCharm:
                    return Loc.T("While last in banked coins: damage, gauge and coins +20%. Always: you drop all held coins on KO and take 1.5x damage and knockback.",
                        "確定コインの順位が最下位の間、威力・ゲージ・コインの獲得量が20%増える。代わりに、撃破されると所持コインを全部落とし、受ける威力と吹き飛びが1.5倍になる。");
                case ItemId.SpikeSoles:
                    return Loc.T("Wall cling +2 s, no slipping on ice. The clung wall freezes for 10 s; opponents touching it freeze for 1 s.",
                        "壁の張り付きが2秒延び、氷の上で滑らない。張り付いた壁を10秒間凍らせ、触れた相手を1秒間凍らせる。");
                case ItemId.FeatherHat:
                    return Loc.T("Gravity taken -30%, but knockback taken +20%.",
                        "受ける重力が30%減るが、受ける吹き飛びが20%増える。");
                case ItemId.GrappleBomb:
                    return Loc.T("Throws a bomb toward the pointer. It sticks to terrain or a player and explodes after 1.5 s. Damage 15.",
                        "照準の方向へ爆弾を投げる。地形か人に刺さり、1.5秒後に爆発する。威力15。");
                case ItemId.Reflector:
                    return Loc.T("For 1 s, reflects projectiles from the front.",
                        "1秒間、正面からの投射物を跳ね返す。");
                case ItemId.FoldingPlatform:
                    return Loc.T("Places a platform toward the pointer for 4 s. Wires can attach to it.",
                        "照準の方向に4秒消えない足場を置く。ワイヤーもつなげる。");
                case ItemId.BigMagnet:
                    return Loc.T("For 3 s, pulls field coins within 8 m.",
                        "3秒間、半径8mのフィールドのコインを吸い寄せる。");
                case ItemId.ReturnRemote:
                    return Loc.T("First use marks the spot; use again within 10 s to teleport back.",
                        "1回目でその場に印を付け、10秒以内の2回目で印へ瞬間移動する。");
                case ItemId.Hourglass:
                    return Loc.T("After a 0.3 s chant, opponents within 5 m move at 0.5x for 1.5 s.",
                        "0.3秒の詠唱の後、半径5mの相手の動きを1.5秒間0.5倍にする。");
                case ItemId.ChainHook:
                    return Loc.T("Throws a chain toward the pointer and pulls the hit opponent in front of you. Damage 3.",
                        "照準の方向へ鎖を飛ばし、当たった相手を目の前へ引き寄せる。威力3。");
                case ItemId.BubbleBarrier:
                    return Loc.T("For 3 s, blocks the next hit (then pops).",
                        "3秒間、次の1回の被弾を無効にする（割れる）。");
                case ItemId.PocketRocket:
                    return Loc.T("Rockets toward the pointer for 0.4 s. Faster than the wire. No damage.",
                        "照準の方向へ0.4秒急加速する。ワイヤーより速い。攻撃の判定はない。");
                case ItemId.PocketTrampoline:
                    return Loc.T("Places a jump pad at your feet for 5 s. Anyone can use it.",
                        "足元にジャンプ台を置く。5秒で消え、だれでも使える。");
                case ItemId.DecoyDoll:
                    return Loc.T("Places a doll of yourself for 10 s. It breaks after 3 hits and draws CPUs.",
                        "その場に自分の姿の人形を10秒置く。3回当てると壊れる。CPUを引きつける。");
                case ItemId.BottledThunder:
                    return Loc.T("Makes a thundercloud toward the pointer for 3 s. Strikes opponents below 3 times (2 each) with shock.",
                        "照準の先に3秒の雷雲を出し、下にいる相手を3回感電させる（威力2ずつ）。");
                case ItemId.BankSeal:
                    return Loc.T("After a 2 s chant, banks half of your held coins on the spot. Getting hit stops it.",
                        "2秒の詠唱の後、その場で所持コインの半分を確定する。詠唱中に当てられると止まる。");
                default:
                    return string.Empty;
            }
        }
    }
}
