using UnityEngine;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの演出の動きの曲線（進み 0〜1 → 0〜1。D-87・D-88）。範囲の外は 0 と 1 に切る。
    /// </summary>
    public static class UiEase
    {
        public static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;

        /// <summary>だんだん遅く止まる。</summary>
        public static float OutCubic(float t)
        {
            float u = 1f - Clamp01(t);
            return 1f - u * u * u;
        }

        /// <summary>だんだん速くなる（勢いよく出ていく）。</summary>
        public static float InCubic(float t)
        {
            t = Clamp01(t);
            return t * t * t;
        }

        /// <summary>ゆっくり出てゆっくり止まる。</summary>
        public static float InOutCubic(float t)
        {
            t = Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        /// <summary>少し行きすぎてから戻って止まる（overshoot が大きいほど行きすぎる）。</summary>
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Clamp01(t);
            float u = t - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        /// <summary>
        /// CSS の cubic-bezier(x1, y1, x2, y2) と同じ曲線（t は時間の割合 0〜1。y1・y2 が 1 を超えると、値も 1 を少し超えてから戻る）。
        /// 見本のページ（ブラウザー）で決めた動きを、そのままゲームで再現するため（2026-11-08）。
        /// </summary>
        public static float CubicBezier(float x1, float y1, float x2, float y2, float t)
        {
            t = Clamp01(t);
            if (t <= 0f || t >= 1f)
            {
                return t;
            }
            // x(u) = t となる u をニュートン法で探し、だめなら二分法で探す。
            float u = t;
            for (int i = 0; i < 8; i++)
            {
                float error = Bezier(x1, x2, u) - t;
                if (Mathf.Abs(error) < 1e-5f)
                {
                    return Bezier(y1, y2, u);
                }
                float slope = BezierSlope(x1, x2, u);
                if (Mathf.Abs(slope) < 1e-6f)
                {
                    break;
                }
                u = Clamp01(u - error / slope);
            }
            float lo = 0f;
            float hi = 1f;
            u = t;
            for (int i = 0; i < 24; i++)
            {
                float x = Bezier(x1, x2, u);
                if (Mathf.Abs(x - t) < 1e-5f)
                {
                    break;
                }
                if (x < t)
                {
                    lo = u;
                }
                else
                {
                    hi = u;
                }
                u = (lo + hi) * 0.5f;
            }
            return Bezier(y1, y2, u);
        }

        /// <summary>見本のページの --e-out（cubic-bezier(.16, 1, .3, 1)。速く出てゆっくり止まる）。</summary>
        public static float SnapOut(float t) => CubicBezier(0.16f, 1f, 0.3f, 1f, t);

        /// <summary>CSS の ease-in-out（cubic-bezier(.42, 0, .58, 1)）。</summary>
        public static float CssEaseInOut(float t) => CubicBezier(0.42f, 0f, 0.58f, 1f, t);

        /// <summary>CSS の ease（cubic-bezier(.25, .1, .25, 1)。transition の初めの値）。</summary>
        public static float CssEase(float t) => CubicBezier(0.25f, 0.1f, 0.25f, 1f, t);

        // 端が (0,0)・(1,1) の 3 次ベジェ曲線の 1 つの座標と、その傾き。
        private static float Bezier(float p1, float p2, float u)
        {
            float v = 1f - u;
            return 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u;
        }

        private static float BezierSlope(float p1, float p2, float u)
        {
            float v = 1f - u;
            return 3f * v * v * p1 + 6f * v * u * (p2 - p1) + 3f * u * u * (1f - p2);
        }
    }
}
