using Airerage.Core.Simulation;
using Airerage.Runtime.Data;

namespace Airerage.Editor
{
    /// <summary>
    /// キャラクター選択に出す文（性能の種類のハッシュタグ、Q・E・R の技のカタカナの名前と説明。ユーザーの要望、2026-10-17。docs/15 6.6）。
    /// シーンのビルダーがキャラクターのアセットを作り直すときに設定する（値はここの 1 か所で決める）。クールタイム・スタック数・必要な
    /// ゲージは、キャラクターの調整値から画面が出す。説明は「何ができるか」を短く書き、調整で変わる数値は書かない。
    /// 説明には半角の空白を入れない（uGUI は空白で改行するので、「2 つ」の間で行が変わる）。
    /// 英語の文も持つ（オプションの LANGUAGE が ENGLISH のとき。2026-10-27）。名前は英語の大文字。
    /// </summary>
    internal static class CharacterProfiles
    {
        private const string AfterEffect = "効果が終わってから";
        private const string AfterEffectEn = "after the effect ends";

        /// <summary>名前が displayName のキャラクターの文を設定する（知らないキャラクターなら何もしない）。</summary>
        public static void Apply(CharacterConfigAsset character, string displayName)
        {
            switch (displayName)
            {
                case CharacterPresets.StandardName:
                    character.SetProfile(new[] { "#特徴無し" }, new[] { "#Balanced" },
                        new SkillText("ダッシュ", "前へ素早く踏み込んで攻撃する。")
                            .English("DASH", "Lunge forward quickly and strike."),
                        new SkillText("ライズ", "斜め上へ飛び上がりながら、頭上を攻撃する。上への移動にも使える。")
                            .English("RISE", "Fly up diagonally while striking above you. Also good for climbing."),
                        new SkillText("バースト", "その場でためてから、周りを大きく吹き飛ばす。")
                            .English("BURST", "Charge up in place, then blast everyone around you far away."));
                    character.SetCostume(CharacterCostume.Standard);
                    break;
                case CharacterPresets.RiskTakerName:
                    character.SetProfile(new[] { "#コイン重視", "#大器晩成" }, new[] { "#CoinFocused", "#LateBloomer" },
                        new SkillText("ターンオンザライト", "ワイヤーをつなげるミラーボールを置く。つないでいる人はゲージがたまる。")
                            .English("TURN ON THE LIGHT", "Place a mirror ball you can hook your wire onto. Anyone hooked to it fills their gauge."),
                        new SkillText("セーフティボックス", "コインが増えていく金庫を置く。長押しで手持ちのコインを預ける。壊されるとコインが散らばる。",
                            "壊されてから")
                            .English("SAFETY BOX", "Place a safe whose coins keep growing. Hold to deposit your coins. If it is destroyed, the coins scatter.",
                                "after it is destroyed"),
                        new SkillText("フィーバータイム", "しばらくの間、手に入るコインとゲージが増える。")
                            .English("FEVER TIME", "For a while, you earn more coins and gauge."));
                    character.SetCostume(CharacterCostume.RiskTaker);
                    break;
                case CharacterPresets.StormRunnerName:
                    character.SetProfile(new[] { "#高速", "#高機動", "#風" }, new[] { "#Fast", "#Agile", "#Wind" },
                        new SkillText("ウインドクローク", "しばらくの間、すべての行動が速くなる。効果中にもう一度押すと、照準の方向へ突風を撃って押し出す。",
                            AfterEffect)
                            .English("WIND CLOAK", "For a while, all your actions get faster. Press again during the effect to fire a gust toward your aim and push things away.",
                                AfterEffectEn),
                        new SkillText("スカイロケット", "照準の方向へ吹く風の帯を置く。入ったものを風の向きへ運ぶ。")
                            .English("SKY ROCKET", "Place a wind lane blowing toward your aim. Anything inside is carried along with the wind."),
                        new SkillText("キャッスルインジエアー", "しばらくの間、すべての行動が大きく速くなり、ワイヤーを空中やどこにでもつなげる。")
                            .English("CASTLE IN THE AIR", "For a while, all your actions get much faster, and your wire can attach to thin air or anything at all."));
                    character.SetCostume(CharacterCostume.StormRunner);
                    break;
                case CharacterPresets.SpaceTravelerName:
                    character.SetProfile(new[] { "#変則的", "#遠距離" }, new[] { "#Tricky", "#LongRange" },
                        new SkillText("ワープポータル", "その場にポータルを置く。2つあると間をワープできる。長押しで置いたポータルにそのまま入る。")
                            .English("WARP PORTAL", "Place a portal where you stand. With two, you can warp between them. Hold to step right into the portal you place."),
                        new SkillText("スペースミサイル", "長押しで狙い、離すと照準の方向へまっすぐ飛ぶミサイルを撃つ。長く飛ぶほど強い。")
                            .English("SPACE MISSILE", "Hold to aim, release to fire a missile straight toward your aim. The farther it flies, the harder it hits."),
                        new SkillText("コズミックレイ", "2つのポータルの間を、壁を貫くビームで攻撃する。ビームが拠点に触れると手持ちのコインを確定する。")
                            .English("COSMIC RAY", "Fire a wall-piercing beam between your two portals. If the beam touches the bank, your held coins are banked."));
                    character.SetCostume(CharacterCostume.SpaceTraveler);
                    break;
                case CharacterPresets.PastryChefName:
                    character.SetProfile(new[] { "#設置", "#罠", "#お菓子" }, new[] { "#Setups", "#Traps", "#Sweets" },
                        new SkillText("フラッフィベイス", "触れた人を弾き飛ばすわたがしの足場を置く。短く押すと足元に、長押しで照準の方向へ向けて置く。ワイヤーはつながらない。")
                            .English("FLUFFY BASE", "Place a cotton candy platform that bounces anyone who touches it. Tap to place it at your feet, hold to angle it toward your aim. Wires can't attach to it."),
                        new SkillText("スイートトラップ", "照準の方向へクリームを飛ばす。長押し中にワイヤーでオレンジ（速くなり、ワイヤーがつながる）、攻撃で抹茶（遅くなり、壁と天井にも付く。ワイヤーはつながらない）。")
                            .English("SWEET TRAP", "Throw sticky cream toward your aim. While holding, press Wire for orange (faster, wires attach) or Attack for matcha (slower, sticks to walls and ceilings, no wires)."),
                        new SkillText("ティーパーティ", "上へマカロンを次々と投げる。跳ね返りながら相手に当たる。色で威力が違い、虹色はとても強い。")
                            .English("TEA PARTY", "Toss macarons upward one after another. They bounce around and hit opponents. Power depends on the color, and rainbow ones are very strong."));
                    character.SetCostume(CharacterCostume.PastryChef);
                    break;
                case CharacterPresets.SoldererName:
                    character.SetProfile(new[] { "#高速", "#ヒット＆アウェイ", "#電気" }, new[] { "#Fast", "#HitAndRun", "#Electric" },
                        new SkillText("イナズマステップ", "照準の方向へ短く突撃する。続けて2回まで出せる。長押しすると、その瞬間に強い雷をまとって長く突撃する。使った回数でクールタイムが延びる。")
                            .English("INAZUMA STEP", "Dash a short way toward your aim, up to twice in a row. Hold to wrap yourself in strong lightning at that moment and dash farther. The cooldown grows with each use."),
                        new SkillText("インバータ", "オン・オフを切り替える。オンの間はワイヤーが短く電気を帯び、つないだ足場を雷が伝って感電させる（3秒に1回）。空振りで押し続けると鞭になり、攻撃しても消えない。")
                            .English("INVERTER", "Toggle on/off. While on, your short wire is charged: hook a platform to shock anyone on it (every 3s). Fire into empty air and hold for a whip that lasts through attacks."),
                        new SkillText("ボールライトニング", "大きな電気の球を3つ、それぞれ放つ瞬間の照準の方向へ撃つ。ゆっくり進んで壁で跳ね返り、止まった後もしばらく残る。当たった相手を感電させて消える。")
                            .English("BALL LIGHTNING", "Fire three big electric balls, each toward your aim as it is released. They drift, bounce off walls and linger after stopping. Each shocks the opponent it hits, then vanishes."));
                    character.SetCostume(CharacterCostume.Solderer);
                    break;
                case CharacterPresets.ThiefName:
                    character.SetProfile(new[] { "#盗む", "#コイン重視", "#変則的" }, new[] { "#Steal", "#CoinFocused", "#Tricky" },
                        new SkillText("タレントスナッチ", "照準の方向へ速い弾を撃ち、当たった相手のQをコピーするか、ステージのギミックや仕掛けを奪う。もう一度押すと、コピーした技を使うか、奪った物を目の前に置く（2つまで）。",
                            "使うか外れてから")
                            .English("TALENT SNATCH", "Fire a fast shot toward your aim to copy the Q of the opponent it hits, or steal a gimmick or trap. Press again to use the copied skill or place what you stole (up to two).",
                                "after using it or missing"),
                        new SkillText("ミスディレクション", "その場に分身を残し、透明になって照準の方向へ短く移動する。3秒たつか、R以外の行動をするか、もう一度押すと解ける。分身は攻撃を受けると煙になって消える。")
                            .English("MISDIRECTION", "Leave a decoy, turn invisible and slip a short way toward your aim. Ends after 3 seconds, on any action but R, or on another press. The decoy vanishes in smoke when hit."),
                        new SkillText("バンクハイスト", "拠点の近くでだけ使える。その場で止まって盗み続け、もう一度押すと、続けた長さに応じてコインを得る。長いほど大きく増える。攻撃を受けると失敗する。")
                            .English("BANK HEIST", "Only near the bank. Stand still and keep stealing, then press again to collect coins for how long you kept at it. The longer, the bigger the haul. Getting hit ruins it."));
                    character.SetCostume(CharacterCostume.Thief);
                    break;
                case CharacterPresets.JuggernautName:
                    character.SetProfile(new[] { "#地上戦", "#鈍重" }, new[] { "#GroundFighter", "#Heavy" },
                        new SkillText("フラック", "地上で構え、もう一度押すと照準の方向へ爆発する重い砲弾を撃つ。上下で弾道の強さ（弱・中・強・弩）のダイアルを回す。続けて3回まで撃て、Q以外の行動で構えが解ける。")
                            .English("FLAK", "Brace on the ground and press again to fire an exploding shell toward your aim. Turn the power dial with up/down. Up to three shots; any action but Q ends the stance."),
                        new SkillText("マイントラップ", "照準の方向へ地雷を山なりに撃つ。地形かキャラクターに張り付き、もう一度押すと爆発する。", "爆発してから")
                            .English("MINE TRAP", "Lob a mine toward your aim. It sticks to terrain or a character; press again to detonate it.", "after it detonates"),
                        new SkillText("メーデー", "ジェット機に吊られて飛び去り、ステージ全体から空いた場所を選んで運んでもらう。その間は攻撃を受けない。")
                            .English("MAYDAY", "A jet lifts you away; pick any open spot on the whole stage and it carries you there. You can't be hit meanwhile."));
                    character.SetCostume(CharacterCostume.Juggernaut);
                    break;
            }
        }
    }
}
