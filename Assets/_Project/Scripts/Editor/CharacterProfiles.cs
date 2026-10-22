using Airerage.Core.Simulation;
using Airerage.Runtime.Data;

namespace Airerage.Editor
{
    /// <summary>
    /// キャラクター選択に出す文（性能の種類のハッシュタグ、Q・E・R の技のカタカナの名前と説明。ユーザーの要望、2026-10-17。docs/15 6.6）。
    /// シーンのビルダーがキャラクターのアセットを作り直すときに設定する（値はここの 1 か所で決める）。クールタイム・スタック数・必要な
    /// ゲージは、キャラクターの調整値から画面が出す。説明は「何ができるか」を短く書き、調整で変わる数値は書かない。
    /// 説明には半角の空白を入れない（uGUI は空白で改行するので、「2 つ」の間で行が変わる）。
    /// </summary>
    internal static class CharacterProfiles
    {
        private const string AfterEffect = "効果が終わってから";

        /// <summary>名前が displayName のキャラクターの文を設定する（知らないキャラクターなら何もしない）。</summary>
        public static void Apply(CharacterConfigAsset character, string displayName)
        {
            switch (displayName)
            {
                case CharacterPresets.StandardName:
                    character.SetProfile(new[] { "#特徴無し" },
                        new SkillText("ダッシュ", "前へ素早く踏み込んで攻撃する。"),
                        new SkillText("ライズ", "斜め上へ飛び上がりながら、頭上を攻撃する。上への移動にも使える。"),
                        new SkillText("バースト", "その場でためてから、周りを大きく吹き飛ばす。"));
                    character.SetCostume(CharacterCostume.Standard);
                    break;
                case CharacterPresets.RiskTakerName:
                    character.SetProfile(new[] { "#コイン重視", "#大器晩成" },
                        new SkillText("ターンオンザライト", "ワイヤーをつなげるミラーボールを置く。つないでいる人はゲージがたまる。"),
                        new SkillText("セーフティボックス", "コインが増えていく金庫を置く。長押しで手持ちのコインを預ける。壊されるとコインが散らばる。",
                            "壊されてから"),
                        new SkillText("フィーバータイム", "しばらくの間、手に入るコインとゲージが増える。"));
                    character.SetCostume(CharacterCostume.RiskTaker);
                    break;
                case CharacterPresets.StormRunnerName:
                    character.SetProfile(new[] { "#高速", "#高機動", "#風" },
                        new SkillText("ウインドクローク", "しばらくの間、すべての行動が速くなる。効果中にもう一度押すと、照準の方向へ突風を撃って押し出す。",
                            AfterEffect),
                        new SkillText("スカイロケット", "照準の方向へ吹く風の帯を置く。入ったものを風の向きへ運ぶ。"),
                        new SkillText("キャッスルインジエアー", "しばらくの間、すべての行動が大きく速くなり、ワイヤーを空中やどこにでもつなげる。"));
                    character.SetCostume(CharacterCostume.StormRunner);
                    break;
                case CharacterPresets.SpaceTravelerName:
                    character.SetProfile(new[] { "#変則的", "#遠距離" },
                        new SkillText("ワープポータル", "その場にポータルを置く。2つあると間をワープできる。長押しで置いたポータルにそのまま入る。"),
                        new SkillText("スペースミサイル", "長押しで狙い、離すと照準の方向へまっすぐ飛ぶミサイルを撃つ。長く飛ぶほど強い。"),
                        new SkillText("コズミックレイ", "2つのポータルの間を、壁を貫くビームで攻撃する。ビームが拠点に触れると手持ちのコインを確定する。"));
                    character.SetCostume(CharacterCostume.SpaceTraveler);
                    break;
                case CharacterPresets.PastryChefName:
                    character.SetProfile(new[] { "#設置", "#罠", "#お菓子" },
                        new SkillText("フラッフィベイス", "触れた人を弾き飛ばすわたがしの足場を置く。短く押すと足元に、長押しで照準の方向へ向けて置く。ワイヤーはつながらない。"),
                        new SkillText("スイートトラップ", "照準の方向へクリームを飛ばして張り付かせる。長押し中にワイヤーでオレンジ（速くなり、ワイヤーがつながる）、攻撃で抹茶（遅くなり、壁と天井に張り付く。ワイヤーはつながらない）。"),
                        new SkillText("ティーパーティ", "上へマカロンを次々と投げる。跳ね返りながら相手に当たる。色で威力が違い、虹色はとても強い。"));
                    character.SetCostume(CharacterCostume.PastryChef);
                    break;
                case CharacterPresets.SoldererName:
                    character.SetProfile(new[] { "#高速", "#ヒット＆アウェイ", "#電気" },
                        new SkillText("イナズマステップ", "照準の方向へ短く突撃する。続けて2回まで出せる。長押しすると、その瞬間に強い雷をまとって長く突撃する。使った回数でクールタイムが延びる。"),
                        new SkillText("インバータ", "押すたびに切り替える。オンの間はワイヤーが短く電気を帯び、足場につなぐと雷が地面を伝って触れた人を感電させる（3秒に1回）。何もない所へ飛ばして押し続けると鞭になり、攻撃しても消えない。"),
                        new SkillText("ボールライトニング", "大きな電気の球を3つ、それぞれ放つ瞬間の照準の方向へ撃つ。ゆっくり進んで壁で跳ね返り、止まった後もしばらく残る。当たった相手を感電させて消える。"));
                    character.SetCostume(CharacterCostume.Solderer);
                    break;
            }
        }
    }
}
