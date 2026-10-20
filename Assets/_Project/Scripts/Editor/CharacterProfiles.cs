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
            }
        }
    }
}
