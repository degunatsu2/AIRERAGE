using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// SPACE TRAVELER の置かれた物（ワープポータル、ミサイルの軌跡、コズミックレイのビーム）の線の描き方（WorldObjectsView から使う）。
    /// 見た目の動きは表示の時間で決める。
    /// </summary>
    public static class SpaceObjectLines
    {
        /// <summary>ポータルとビームの基本の色（紫）。</summary>
        public static readonly Color Purple = new Color(0.62f, 0.35f, 1f);

        private const int RingPoints = 40;
        private const int SwirlPoints = 30;
        private const float Z = -0.55f;

        /// <summary>ポータル: 脈打つ輪と、回るらせん。color は持ち主の色を混ぜた紫。visible が false なら消す（点滅）。</summary>
        public static void DrawPortal(LineRenderer ring, LineRenderer swirl, Vector3 center, float radius, Color color, float time, bool visible)
        {
            if (ring != null)
            {
                ring.enabled = visible;
                ring.loop = true;
                ring.positionCount = RingPoints;
                float pulse = 1f + 0.06f * Mathf.Sin(time * 5f);
                for (int k = 0; k < RingPoints; k++)
                {
                    float a = Mathf.PI * 2f * k / RingPoints;
                    ring.SetPosition(k, center + new Vector3(Mathf.Cos(a) * radius * pulse, Mathf.Sin(a) * radius * pulse, Z));
                }
                SetColor(ring, color, 0.9f);
            }
            if (swirl != null)
            {
                swirl.enabled = visible;
                swirl.loop = false;
                swirl.positionCount = SwirlPoints;
                float spin = time * 4f;
                for (int k = 0; k < SwirlPoints; k++)
                {
                    float u = k / (float)(SwirlPoints - 1);
                    float a = spin + u * Mathf.PI * 4f;
                    float r = radius * (0.15f + 0.75f * u);
                    swirl.SetPosition(k, center + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, Z));
                }
                swirl.startColor = new Color(1f, 1f, 1f, 0.9f);
                swirl.endColor = new Color(color.r, color.g, color.b, 0.3f);
            }
        }

        /// <summary>つながっている 2 つのポータルの間の、薄い線（show が false なら消す）。</summary>
        public static void DrawLink(LineRenderer link, Vector3 from, Vector3 to, Color color, float time, bool show)
        {
            if (link == null)
            {
                return;
            }
            link.enabled = show;
            if (!show)
            {
                return;
            }
            link.loop = false;
            link.positionCount = 2;
            link.SetPosition(0, new Vector3(from.x, from.y, Z + 0.1f));
            link.SetPosition(1, new Vector3(to.x, to.y, Z + 0.1f));
            SetColor(link, color, 0.18f + 0.07f * Mathf.Sin(time * 3f));
        }

        /// <summary>ミサイルの軌跡（新しい点ほど濃い）。</summary>
        public static void DrawTrail(LineRenderer trail, Vector3[] points, int count, Color color)
        {
            if (trail == null)
            {
                return;
            }
            trail.enabled = count >= 2;
            trail.loop = false;
            trail.positionCount = count;
            for (int k = 0; k < count; k++)
            {
                trail.SetPosition(k, points[k]);
            }
            trail.startColor = new Color(color.r, color.g, color.b, 0f);
            trail.endColor = new Color(1f, 1f, 1f, 0.85f);
        }

        /// <summary>コズミックレイのビーム（太い紫の光と、白い芯）。flash は当たった瞬間の明るさ（0〜1）。</summary>
        public static void DrawBeam(LineRenderer glow, LineRenderer core, Vector3 from, Vector3 to, float halfWidth, float flash, float time)
        {
            float flicker = 0.85f + 0.15f * Mathf.Sin(time * 40f);
            if (glow != null)
            {
                glow.enabled = true;
                glow.loop = false;
                glow.positionCount = 2;
                glow.SetPosition(0, new Vector3(from.x, from.y, Z));
                glow.SetPosition(1, new Vector3(to.x, to.y, Z));
                glow.widthMultiplier = halfWidth * 2f * (1f + 0.25f * flash) * flicker;
                SetColor(glow, Color.Lerp(Purple, Color.white, 0.3f * flash), 0.45f + 0.3f * flash);
            }
            if (core != null)
            {
                core.enabled = true;
                core.loop = false;
                core.positionCount = 2;
                core.SetPosition(0, new Vector3(from.x, from.y, Z - 0.02f));
                core.SetPosition(1, new Vector3(to.x, to.y, Z - 0.02f));
                core.widthMultiplier = halfWidth * 0.6f * flicker;
                SetColor(core, Color.white, 0.95f);
            }
        }

        /// <summary>拠点へ飛んでいくコイン（progress 0〜1。弧を描いて飛ぶ、金色の流れ）。</summary>
        public static void DrawCoinFlight(LineRenderer line, Vector3 from, Vector3 to, float progress)
        {
            const int points = 12;
            float head = Mathf.Clamp01(progress * 1.3f);
            float tail = Mathf.Clamp01(head - 0.35f);
            line.loop = false;
            line.positionCount = points;
            for (int k = 0; k < points; k++)
            {
                float t = Mathf.Lerp(tail, head, k / (float)(points - 1));
                line.SetPosition(k, Vector3.Lerp(from, to, t) + new Vector3(0f, Mathf.Sin(t * Mathf.PI) * 3f, Z));
            }
            float alpha = 1f - Mathf.Clamp01((progress - 0.85f) / 0.15f);
            line.startColor = new Color(1f, 0.82f, 0.2f, 0.2f * alpha);
            line.endColor = new Color(1f, 0.95f, 0.5f, alpha);
        }

        private static void SetColor(LineRenderer line, Color color, float alpha)
        {
            color.a = alpha;
            line.startColor = color;
            line.endColor = color;
        }
    }
}
