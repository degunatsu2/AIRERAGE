using Airerage.Core.Match;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 試合の始まりの演出の時間割（D-98・D-99。2026-11-17 のユーザーの決定: 切り替え T12〈ゲート〉＋ 開始前の演出 I1〈シネマスコープの
    /// ステージ名と名前〉＋ I2〈空撮〉、最後に集中線とズーム〈タイトル → メインメニューのスカイダイブ〉からラウンドコール）。
    /// 2026-11-18 の要望: 上の帯の右に Loading のゲージ（グリッチ。100% で Complete を 2 回点滅させてからズーム）、ラウンドコールは
    /// ON YOUR MARKS → GET READY…… → ROLL! と、文字の下に始まりまでのゲージ（緑 → 黄 → 赤）、試合中の表示は演出の後にタイマーと一緒に
    /// フェードインで出す。呼び出しが 1 回増えた分、カウントダウンを 6 秒（MatchRules.CountdownTicks 360）に延ばした。
    /// 演出はカウントダウン（全員が同じティックで進む）に合わせ、カウントダウンの全体を基準の秒（DesignSeconds）で書く。
    /// カウントダウンが短いとき（テスト）は同じ割合で縮める。表示だけで、シミュレーションには何も書かない。
    /// </summary>
    public static class MatchIntroTimeline
    {
        /// <summary>時間割の基準の長さ（秒。カウントダウンの全体）。</summary>
        public const float DesignSeconds = 6f;

        /// <summary>ステージの名前が上の帯に滑り込む。</summary>
        public const float NameIn = 0.35f;

        /// <summary>プレイヤーの札が下の帯に滑り込み始める（人ごとに PlateInterval ずつ遅らせる）。</summary>
        public const float PlatesStart = 0.9f;
        public const float PlateInterval = 0.2f;
        public const float PlateSlide = 0.32f;

        /// <summary>上の帯の右の Loading のゲージが 0% から増え始める。</summary>
        public const float LoadingStart = 0.45f;

        /// <summary>Loading が 100% になり、Complete を出す（2 回点滅させる）。</summary>
        public const float LoadingEnd = 2.0f;

        /// <summary>Complete の点滅（消える → 出るを 2 回）の 1 回の長さ。</summary>
        public const float CompleteBlink = 0.2f;

        /// <summary>名前と札（と Complete）が去り始める。</summary>
        public const float LabelsOut = 2.7f;

        /// <summary>空撮の終わり（集中線とズームの始まり）。</summary>
        public const float AerialEnd = 3.0f;

        /// <summary>突進の終わりの白い光（帯が外れる）。</summary>
        public const float Flash = 3.4f;

        /// <summary>カメラが定位置に戻る。</summary>
        public const float DiveEnd = 3.9f;

        /// <summary>試合中の表示（右上の札・技の札・タイマーなど）がフェードインし始める（HudFadeSeconds で出きる）。</summary>
        public const float HudRevealStart = 3.9f;
        public const float HudFadeSeconds = 0.5f;

        /// <summary>「ON YOUR MARKS」を出し始める（ゲージもここから満ち始める）。</summary>
        public const float ReadyStart = 3.9f;

        /// <summary>「GET READY……」に替わる（試合が始まるまで）。</summary>
        public const float SetStart = 4.95f;

        /// <summary>始まってから「ROLL!」を出す秒数（実時間）。</summary>
        public const float GoSeconds = 0.8f;

        /// <summary>ラウンドコールの文字（ON YOUR MARKS → GET READY…… → 始まったら ROLL!）。</summary>
        public const string CallMarks = "ON YOUR MARKS";
        public const string CallSet = "GET READY……";
        public const string CallGo = "ROLL!";

        /// <summary>基準の秒 t のラウンドコール（カウントダウンの間。始まった後の ROLL! は呼ぶ側が出す）。</summary>
        public static string CallAt(float t)
        {
            return t >= SetStart ? CallSet : t >= ReadyStart ? CallMarks : string.Empty;
        }

        /// <summary>始まりまでのゲージ（0〜1。ON YOUR MARKS で 0、試合の始まりで 1）。</summary>
        public static float StartGauge(float t)
        {
            return Mathf.Clamp01((t - ReadyStart) / (DesignSeconds - ReadyStart));
        }

        /// <summary>試合中の表示の出し具合（0〜1。演出の間は 0、HudRevealStart から HudFadeSeconds で 1）。</summary>
        public static float HudReveal(float t)
        {
            return Mathf.Clamp01((t - HudRevealStart) / HudFadeSeconds);
        }

        /// <summary>
        /// Loading のゲージの数（0〜100 の整数）。少し止まっては跳ぶ（グリッチのように）。同じ t なら同じ数（表示だけ）。
        /// </summary>
        public static int LoadingPercent(float t)
        {
            if (t <= LoadingStart)
            {
                return 0;
            }
            if (t >= LoadingEnd)
            {
                return 100;
            }
            const int steps = 13;
            float k = (t - LoadingStart) / (LoadingEnd - LoadingStart);
            int step = Mathf.Min(steps - 1, Mathf.FloorToInt(k * steps));
            // 段ごとの重み（決まった乱れ）を足し合わせて、跳ぶ先の数を決める。
            float total = 0f;
            float reached = 0f;
            for (int i = 0; i < steps; i++)
            {
                float weight = 0.35f + Hash01(i);
                total += weight;
                if (i < step)
                {
                    reached += weight;
                }
            }
            return Mathf.Clamp(Mathf.FloorToInt(reached / total * 100f), 0, 99);
        }

        /// <summary>Loading が跳んだ直後（グリッチを強く出す間）か。</summary>
        public static bool LoadingJumped(float t)
        {
            if (t <= LoadingStart || t >= LoadingEnd + 0.08f)
            {
                return false;
            }
            const int steps = 13;
            float k = (t - LoadingStart) / (LoadingEnd - LoadingStart) * steps;
            return k - Mathf.Floor(k) < 0.35f || t >= LoadingEnd;
        }

        /// <summary>Complete を出しているか（LoadingEnd から、消える → 出るを 2 回くり返してから出たまま）。</summary>
        public static bool CompleteVisible(float t)
        {
            if (t < LoadingEnd)
            {
                return false;
            }
            float blink = (t - LoadingEnd) / CompleteBlink;
            // 0〜1 出る、1〜1.5 消える、1.5〜2.5 出る、2.5〜3 消える、3〜 出たまま（2 回点滅）。
            return !((blink >= 1f && blink < 1.5f) || (blink >= 2.5f && blink < 3f));
        }

        // 0〜1 の決まった乱れ。
        private static float Hash01(int i)
        {
            return Mathf.Repeat(Mathf.Sin(i * 12.9898f + 4.1414f) * 43758.5453f, 1f);
        }

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
