using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 撃破技のチャージ（発生の間）を派手に示す（D-38、ユーザーの要望 2026-10-13）。キャラクターの周りに金色の輪が集まり、
    /// 光の筋が回り、上に「CHARGE!」を出す。CharacterView が毎フレーム Show / Hide を呼ぶ。状態は読むだけ。
    /// 見た目の動き（脈打つ、回る）は表示の時間で決める（シミュレーションには影響しない）。
    /// </summary>
    public sealed class ChargeEffectView : MonoBehaviour
    {
        public const string Label = "CHARGE!";

        private const int RingPoints = 48;
        private const int RayCount = 10;

        // キャラクターの手前に描く。
        private const float Z = -0.7f;

        [Tooltip("集まってくる輪")]
        [SerializeField] private LineRenderer _ring;
        [Tooltip("体の周りで脈打つ輪")]
        [SerializeField] private LineRenderer _pulse;
        [Tooltip("回る光の筋（星形）")]
        [SerializeField] private LineRenderer _rays;
        [Tooltip("「CHARGE!」の位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [SerializeField] private Color _color = new Color(1f, 0.78f, 0.1f, 1f);
        [SerializeField] private Color _highlight = new Color(1f, 0.97f, 0.7f, 1f);
        [Tooltip("輪の半径（m）。チャージの始めから終わりにかけて小さくなる")]
        [SerializeField] private float _startRadius = 2.6f;
        [SerializeField] private float _endRadius = 0.9f;
        [SerializeField] private int _fontSize = 34;

        private GUIStyle _style;
        private Vector3 _center;

        /// <summary>今表示しているか。</summary>
        public bool IsShowing { get; private set; }

        /// <summary>チャージの進み具合（0〜1）。</summary>
        public float Progress { get; private set; }

        private void Awake()
        {
            Hide();
        }

        /// <summary>center（キャラクターの中心）の周りに、進み具合 progress（0〜1）のチャージを描く。</summary>
        public void Show(Vector3 center, float progress)
        {
            IsShowing = true;
            Progress = Mathf.Clamp01(progress);
            _center = center;
            float time = Time.time;

            if (_ring != null)
            {
                float radius = Mathf.Lerp(_startRadius, _endRadius, Progress);
                Color color = _color;
                color.a = Mathf.Lerp(0.45f, 1f, Progress);
                DrawCircle(_ring, center, radius, 0.08f + 0.12f * Progress, color);
            }
            if (_pulse != null)
            {
                float radius = _endRadius * (1f + 0.15f * Mathf.Sin(time * 40f));
                DrawCircle(_pulse, center, radius, 0.06f + 0.06f * Progress, _highlight);
            }
            if (_rays != null)
            {
                DrawRays(center, time);
            }
        }

        public void Hide()
        {
            IsShowing = false;
            Progress = 0f;
            SetEnabled(_ring, false);
            SetEnabled(_pulse, false);
            SetEnabled(_rays, false);
        }

        private void DrawRays(Vector3 center, float time)
        {
            // 内側と外側の点を交互に結ぶ星形。チャージが進むほど筋が長く、ちらつく。
            float rotation = time * 6f;
            float outer = 1.3f + 1.1f * Progress;
            _rays.positionCount = RayCount * 2;
            _rays.loop = true;
            for (int i = 0; i < RayCount * 2; i++)
            {
                float angle = rotation + Mathf.PI * i / RayCount;
                float flicker = 1f + 0.12f * Mathf.Sin(time * 50f + i * 1.7f);
                float radius = i % 2 == 0 ? outer * flicker : 0.6f;
                _rays.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, Z));
            }
            Color color = Color.Lerp(_color, _highlight, 0.5f + 0.5f * Mathf.Sin(time * 30f));
            color.a = Mathf.Lerp(0.5f, 0.95f, Progress);
            _rays.startColor = _rays.endColor = color;
            _rays.widthMultiplier = 0.05f + 0.05f * Progress;
            _rays.enabled = true;
        }

        private static void DrawCircle(LineRenderer line, Vector3 center, float radius, float width, Color color)
        {
            line.positionCount = RingPoints;
            line.loop = true;
            for (int i = 0; i < RingPoints; i++)
            {
                float angle = Mathf.PI * 2f * i / RingPoints;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, Z));
            }
            line.widthMultiplier = width;
            line.startColor = line.endColor = color;
            line.enabled = true;
        }

        private static void SetEnabled(LineRenderer line, bool enabled)
        {
            if (line != null)
            {
                line.enabled = enabled;
            }
        }

        private void OnGUI()
        {
            if (!IsShowing || _camera == null)
            {
                return;
            }
            Vector3 screen = _camera.WorldToScreenPoint(_center + new Vector3(0f, 1.9f, 0f));
            if (screen.z <= 0f)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            // 進むほど大きく、脈打つ。
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 30f);
            _style.fontSize = Mathf.RoundToInt(_fontSize * Mathf.Lerp(0.8f, 1.15f, Progress) * pulse);
            var rect = new Rect(screen.x - 150f, Screen.height - screen.y - 30f, 300f, 60f);

            Color previous = GUI.color;
            GUI.color = new Color(0.25f, 0.15f, 0f, 0.9f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), Label, _style);
            GUI.color = Color.Lerp(_color, _highlight, 0.5f + 0.5f * Mathf.Sin(Time.time * 30f));
            GUI.Label(rect, Label, _style);
            GUI.color = previous;
        }
    }
}
