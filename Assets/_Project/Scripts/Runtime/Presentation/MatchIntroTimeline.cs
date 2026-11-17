using Airerage.Core.Match;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 試合の始まりの演出の時間割（D-98・D-99。2026-11-17 のユーザーの決定: 切り替え T12〈ゲート〉＋ 開始前の演出 I1〈シネマスコープの
    /// ステージ名と名前〉＋ I2〈空撮〉、最後に集中線とズーム〈タイトル → メインメニューのスカイダイブ〉からラウンドコール〈READY → GO!〉）。
    /// 演出はカウントダウン（全員が同じティックで進む）に合わせ、カウントダウン 5 秒（MatchRules.CountdownTicks 300）を基準の秒で書く。
    /// カウントダウンが短いとき（テスト）は同じ割合で縮める。表示だけで、シミュレーションには何も書かない。
    /// </summary>
    public static class MatchIntroTimeline
    {
        /// <summary>時間割の基準の長さ（秒。カウントダウンの全体）。</summary>
        public const float DesignSeconds = 5f;

        /// <summary>ステージの名前が上の帯に滑り込む。</summary>
        public const float NameIn = 0.35f;

        /// <summary>プレイヤーの札が下の帯に滑り込み始める（人ごとに PlateInterval ずつ遅らせる）。</summary>
        public const float PlatesStart = 0.9f;
        public const float PlateInterval = 0.2f;
        public const float PlateSlide = 0.32f;

        /// <summary>名前と札が去り始める。</summary>
        public const float LabelsOut = 2.7f;

        /// <summary>空撮の終わり（集中線とズームの始まり）。</summary>
        public const float AerialEnd = 3.0f;

        /// <summary>突進の終わりの白い光（帯が外れる）。</summary>
        public const float Flash = 3.4f;

        /// <summary>カメラが定位置に戻る。</summary>
        public const float DiveEnd = 3.9f;

        /// <summary>「READY」を出し始める（カウントダウンの終わりまで）。</summary>
        public const float ReadyStart = 3.9f;

        /// <summary>始まってから「GO!」を出す秒数（実時間）。</summary>
        public const float GoSeconds = 0.8f;

        /// <summary>
        /// カウントダウンの中の基準の秒（0〜DesignSeconds）。between はティックとティックの間の進み（0〜1。表示を滑らかにする）。
        /// カウントダウンがなければ DesignSeconds（演出は終わっている）。
        /// </summary>
        public static float DesignTime(MatchRules rules, int tick, float between)
        {
            if (rules == null || rules.CountdownTicks <= 0)
            {
                return DesignSeconds;
            }
            float progress = (tick + Mathf.Clamp01(between)) / rules.CountdownTicks;
            return Mathf.Clamp01(progress) * DesignSeconds;
        }

        /// <summary>ティックの間の進みを数える（ティックが変わった時刻からの経過。止まっている間は 1 で止まる）。</summary>
        public sealed class TickClock
        {
            private int _tick = int.MinValue;
            private float _changedAt;

            public float Between(int tick)
            {
                if (tick != _tick)
                {
                    _tick = tick;
                    _changedAt = Time.unscaledTime;
                }
                return Mathf.Clamp01((Time.unscaledTime - _changedAt) * Airerage.Core.Common.SimTime.TicksPerSecond);
            }
        }
    }
}
