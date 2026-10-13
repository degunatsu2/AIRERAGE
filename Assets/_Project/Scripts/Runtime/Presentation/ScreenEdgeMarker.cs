using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 画面の外にある物の方向を、画面の端の矢印で示すための計算と描画（100 枚のコイン、拠点。docs/12_match_plan.md）。
    /// 座標は OnGUI の座標（左上が原点、下向きが +y）。
    /// </summary>
    public static class ScreenEdgeMarker
    {
        /// <summary>
        /// world が画面（端から margin だけ内側）の外なら true を返し、edge に矢印を置く画面の端の位置、angle にその方向
        /// （度。右が 0、時計回り）を返す。画面の中なら false で、edge はその位置。
        /// </summary>
        public static bool Locate(Camera camera, Vector3 world, float margin, out Vector2 edge, out float angle)
        {
            Vector3 p = camera.WorldToScreenPoint(world);
            var size = new Vector2(Screen.width, Screen.height);
            var point = new Vector2(p.x, size.y - p.y);
            if (p.z < 0f)
            {
                // カメラの後ろは、写すと反対側になるので折り返す。
                point = size - point;
            }
            Vector2 center = size * 0.5f;
            Vector2 half = Vector2.Max(center - new Vector2(margin, margin), Vector2.one);
            Vector2 d = point - center;
            if (p.z > 0f && Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y)
            {
                edge = point;
                angle = 0f;
                return false;
            }
            float t = Mathf.Min(half.x / Mathf.Max(1e-3f, Mathf.Abs(d.x)), half.y / Mathf.Max(1e-3f, Mathf.Abs(d.y)));
            edge = center + d * t;
            angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>矢印（右向きの三角のテクスチャ）を、at を中心に angle だけ回して描く。</summary>
        public static void DrawArrow(Texture2D arrow, Vector2 at, float angle, float size, Color color)
        {
            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            GUIUtility.RotateAroundPivot(angle, at);
            GUI.color = color;
            GUI.DrawTexture(new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size), arrow);
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        /// <summary>矢印の内側（ラベルを置く位置）。angle の方向と反対へ distance だけ戻した位置。</summary>
        public static Vector2 Inward(Vector2 edge, float angle, float distance)
        {
            float radians = angle * Mathf.Deg2Rad;
            return edge - new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * distance;
        }

        /// <summary>右向きの三角のテクスチャ（白。色は描くときに付ける）。使い終わったら Destroy する。</summary>
        public static Texture2D CreateArrowTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "EdgeArrow", hideFlags = HideFlags.HideAndDontSave };
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 左の辺（x = 0）から右の頂点（x = size）へ細くなる三角。縁を少しぼかす。
                    float px = x + 0.5f;
                    float py = Mathf.Abs(y + 0.5f - half);
                    float limit = half * (1f - px / size);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(limit - py + 0.5f)));
                }
            }
            texture.Apply();
            return texture;
        }
    }
}
