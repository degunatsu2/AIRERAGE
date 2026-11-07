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
    }
}
