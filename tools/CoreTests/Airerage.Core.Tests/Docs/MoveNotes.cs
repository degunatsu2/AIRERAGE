using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;

namespace Airerage.Core.Tests.Docs
{
    /// <summary>
    /// 性能表（docs/17）の備考に載せる、技の説明の手書きの文（どんな技か。数値は書かない。数値は MoveTable がコードから読む）。
    /// 技の中身を変えたら、ここの文も合わせる。
    /// </summary>
    internal static class MoveNotes
    {
        private static readonly Dictionary<string, string> Notes = new Dictionary<string, string>
        {
            [Key(CharacterPresets.StandardName, AttackSlot.Ground)] = "前へ踏み込む 1 段目と、斜め上へ飛び出す 2 段目の二段技",
            [Key(CharacterPresets.StandardName, AttackSlot.AirNeutral)] = "体の周りを広く。軽く浮かせる（コンボの始動）",
            [Key(CharacterPresets.StandardName, AttackSlot.AirSide)] = "前へ長く。横へ大きく飛ばす",
            [Key(CharacterPresets.StandardName, AttackSlot.AirUp)] = "頭上。真上へ打ち上げる",
            [Key(CharacterPresets.StandardName, AttackSlot.AirDown)] = "チャージの後、斜め下へ飛び込んでたたき落とす",
            [Key(CharacterPresets.StandardName, AttackSlot.Special1)] = "少しためてから前へまっすぐ突進（高さを保つ）",
            [Key(CharacterPresets.StandardName, AttackSlot.Special2)] = "斜め上へ飛び上がりながら頭上を打ち上げる（戻りにも使える）",
            [Key(CharacterPresets.StandardName, AttackSlot.Super)] = "その場で止まってためた後、体の周りを大きく吹き飛ばす",

            [Key(CharacterPresets.RiskTakerName, AttackSlot.Ground)] = "ためている間はその場から動かず、離すと大きく踏み込む一撃。当たると斜め上へ飛び出す。外すと隙が大きい",
            [Key(CharacterPresets.RiskTakerName, AttackSlot.AirUp)] = "コインを投げ上げて頭上を攻撃（投射物）",
            [Key(CharacterPresets.RiskTakerName, AttackSlot.AirDown)] = "頭上で回るルーレットが、攻撃が出たときに止まる",
            [Key(CharacterPresets.RiskTakerName, AttackSlot.Special1)] = "ターンオンザライト",
            [Key(CharacterPresets.RiskTakerName, AttackSlot.Special2)] = "セーフティボックス。クールタイムは壊れてから数える",
            [Key(CharacterPresets.RiskTakerName, AttackSlot.Super)] = "フィーバータイム",

            [Key(CharacterPresets.StormRunnerName, AttackSlot.Ground)] = "向いている方向へ走り続け、別の行動をするまで終わらない。足場が途切れたら・壁で浅く跳ぶ。逆向きの入力で反転",
            [Key(CharacterPresets.StormRunnerName, AttackSlot.AirNeutral)] = "竜巻をまとって回る 3 段",
            [Key(CharacterPresets.StormRunnerName, AttackSlot.AirSide)] = "速さを落とさずに横へ切り抜ける",
            [Key(CharacterPresets.StormRunnerName, AttackSlot.AirDown)] = "斜め下へ急降下して蹴る",
            [Key(CharacterPresets.StormRunnerName, AttackSlot.Special1)] = "ウインドクローク。クールタイムは効果が終わってから数える",
            [Key(CharacterPresets.StormRunnerName, AttackSlot.Special2)] = "スカイロケット。全員と投射物に効く",
            [Key(CharacterPresets.StormRunnerName, AttackSlot.Super)] = "キャッスルインジエアー",

            [Key(CharacterPresets.SpaceTravelerName, AttackSlot.Ground)] = "少しためた後、斜め後ろへ高く飛び上がる。飛び上がる瞬間に前を攻撃して強く上へ飛ばす",
            [Key(CharacterPresets.SpaceTravelerName, AttackSlot.AirNeutral)] = "体の周りに星を散らす 2 段。1 段目は少し引き寄せる",
            [Key(CharacterPresets.SpaceTravelerName, AttackSlot.AirSide)] = "前へ瞬間移動してから蹴る",
            [Key(CharacterPresets.SpaceTravelerName, AttackSlot.AirDown)] = "ためた後、真下へ重く落ちて叩きつける",
            [Key(CharacterPresets.SpaceTravelerName, AttackSlot.Special1)] = "片方に入るともう片方へ移る（全員と投射物。速度はそのまま）",
            [Key(CharacterPresets.SpaceTravelerName, AttackSlot.Special2)] = "スペースミサイル。触れたものかステージの外で爆発。壊せる物は壊す",
            [Key(CharacterPresets.SpaceTravelerName, AttackSlot.Super)] = "壁を貫通。撃つ間は止まる。撃ち終わりにビームが拠点に触れていれば所持コインを全部確定",

            [Key(CharacterPresets.PastryChefName, AttackSlot.Ground)] = "ゴムベラで回り、前 → 後ろ → 前の斜め上へ飛び上がる（技の後も慣性で飛ぶ）",
            [Key(CharacterPresets.PastryChefName, AttackSlot.AirNeutral)] = "両手のパレットナイフで、傾いた細長い判定 2 本",
            [Key(CharacterPresets.PastryChefName, AttackSlot.AirSide)] = "ハンドミキサーの細い判定の 4 段（最後で吹き飛ばす。その場にとどまる）",
            [Key(CharacterPresets.PastryChefName, AttackSlot.AirUp)] = "ボウルを持って手を上へ。判定は少し弱い",
            [Key(CharacterPresets.PastryChefName, AttackSlot.AirDown)] = "ボウルを乗せたデジタルスケールを落とす（持ち主には当たらない）",
            [Key(CharacterPresets.PastryChefName, AttackSlot.Special1)] = "フラッフィベイス。ワイヤーはつながらない",
            [Key(CharacterPresets.PastryChefName, AttackSlot.Special2)] = "スイートトラップ。抹茶にはワイヤーがつながらない",
            [Key(CharacterPresets.PastryChefName, AttackSlot.Super)] = "ティーパーティ。持ち主以外に、相手ごとに 1 回だけ当たる",

            [Key(CharacterPresets.SoldererName, AttackSlot.Ground)] = "地面を蹴って真上に高く跳ぶ。蹴るときに前後へ雷の衝撃波",
            [Key(CharacterPresets.SoldererName, AttackSlot.AirNeutral)] = "大の字で全身から放電。持続が長く弱い",
            [Key(CharacterPresets.SoldererName, AttackSlot.AirSide)] = "指先を斜め下へ。指先のクリティカル（出た瞬間だけ）と腕の判定",
            [Key(CharacterPresets.SoldererName, AttackSlot.AirUp)] = "雷をまとった両足でサマーソルト（前の下 → 上 → 後ろ）",
            [Key(CharacterPresets.SoldererName, AttackSlot.AirDown)] = "踏みつけ",
            [Key(CharacterPresets.SoldererName, AttackSlot.Special1)] = "雷をまとって突撃（長押しは回りながら長く）",
            [Key(CharacterPresets.SoldererName, AttackSlot.Special2)] = "インバータ",
            [Key(CharacterPresets.SoldererName, AttackSlot.Super)] = "ボールライトニング",
            [Key(CharacterPresets.ThiefName, AttackSlot.Ground)] = "真下へ煙球を投げながら、斜め後ろへ飛び上がる",
            [Key(CharacterPresets.ThiefName, AttackSlot.AirNeutral)] = "慣性のまま前転し、体の周りを小さな判定が回る",
            [Key(CharacterPresets.ThiefName, AttackSlot.AirSide)] = "慣性のまま、少しためてから前を薙ぎ払う（横に狭く縦に広い弧）",
            [Key(CharacterPresets.ThiefName, AttackSlot.AirUp)] = "慣性のまま、頭上をナイフで 5 回突く（体の幅を 5 つに分けた 2・4・1・5・3 の列に、短く細い判定）",
            [Key(CharacterPresets.ThiefName, AttackSlot.AirDown)] = "慣性のまま、少しためてから下を薙ぎ払う（縦に狭く横に広い弧）",
            [Key(CharacterPresets.ThiefName, AttackSlot.Special1)] = "タレントスナッチ",
            [Key(CharacterPresets.ThiefName, AttackSlot.Special2)] = "ミスディレクション",
            [Key(CharacterPresets.ThiefName, AttackSlot.Super)] = "バンクハイスト",
        };

        /// <summary>キャラクター character の技 slot の説明（なければ、名前のない技は「STANDARD と同じ技」）。</summary>
        public static string Of(string character, AttackSlot slot, AttackData attack)
        {
            if (Notes.TryGetValue(Key(character, slot), out string note))
            {
                return note;
            }
            return string.IsNullOrEmpty(attack.Name) && character != CharacterPresets.StandardName ? "STANDARD と同じ技" : null;
        }

        private static string Key(string character, AttackSlot slot)
        {
            return character + "/" + slot;
        }
    }
}
