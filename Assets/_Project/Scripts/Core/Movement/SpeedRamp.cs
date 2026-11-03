using System;

namespace Airerage.Core.Movement
{
    /// <summary>
    /// 速さの伸びを、前の上限（ソフトの上限 soft）を超えたところから曲線的に緩やかにする（2026-11-03 のユーザーの要望: 上限を 40 m/s に
    /// 上げたら、上がりきるのが速すぎた。前の上限までは今までどおり、その先は上限に近いほどゆっくり伸びる）。
    /// soft を超える分の伸びに、倍率 scale × √(1 − u) を掛ける（u は soft から上限 max までのどこにいるか。0〜1）。上限の手前ほど伸びが
    /// 小さくなり、決まった時間で上限に届く。減る向きの変化（減速）はそのまま。
    /// </summary>
    public static class SpeedRamp
    {
        /// <summary>
        /// 速さが before から after へ変わるときの、伸びを緩やかにした速さ（max を超えない）。after が before 以下か soft 以下なら after の
        /// まま（前の上限までの伸びは変えない）。before がすでに max 以上なら、それ以上は伸ばさない。
        /// </summary>
        public static float Limit(float before, float after, float soft, float max, float scale)
        {
            if (!(after > before) || !(after > soft) || !(max > soft))
            {
                return after;
            }
            float start = Math.Max(before, soft);
            if (start >= max)
            {
                return Math.Min(after, start);
            }
            float u = (start - soft) / (max - soft);
            float factor = scale * MathF.Sqrt(1f - u);
            return Math.Min(max, start + (after - start) * factor);
        }

        /// <summary>
        /// 一定の加速度 acceleration（m/s²）で止まった状態から伸び続けたとき、seconds 秒で max に届く倍率（soft までは今までどおり
        /// acceleration で伸びる。soft から max までは √ の曲線で、2 (max − soft) / (acceleration × 残りの秒) の倍率になる）。0〜1。
        /// </summary>
        public static float ScaleFor(float soft, float max, float acceleration, float seconds)
        {
            if (!(acceleration > 0f) || !(max > soft) || !(seconds > 0f))
            {
                return 1f;
            }
            float above = seconds - Math.Max(0f, soft) / acceleration;
            if (!(above > 0f))
            {
                return 1f;
            }
            return Math.Min(1f, 2f * (max - soft) / (acceleration * above));
        }
    }
}
