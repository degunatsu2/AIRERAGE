using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ガードとジャストガードの表示（D-21、ユーザーの指示 2026-10-13。docs/11_combat_plan.md T3-5）。
    /// - ガード中: プレイヤーの色の光る輪がキャラクターを包み、上に「GUARD!」。攻撃をガードした瞬間は輪が強く光る。
    /// - ジャストガードに成功した瞬間: 金色の強い光（広がる輪と光の筋）と「PARRY!」を少しの間出す。
    /// CharacterView が毎フレーム Render を呼び、ガードした数・ジャストガードした数の増加を伝える。状態は読むだけ。
    /// 見た目の動き（脈打つ、広がる）は表示の時間で決める（シミュレーションには影響しない）。
    /// </summary>
    public sealed class GuardEffectView : MonoBehaviour
    {
        public const string GuardLabel = "GUARD!";
        public const string ParryLabel = "PARRY!";

        private const int CirclePoints = 48;
        private const int RayCount = 12;
        private const float Z = -0.75f;

        [Tooltip("ガード中の光る輪")]
        [SerializeField] private LineRenderer _glow;
        [Tooltip("ジャストガードの広がる輪")]
        [SerializeField] private LineRenderer _burst;
        [Tooltip("ジャストガードの光の筋")]
        [SerializeField] private LineRenderer _rays;
        [Tooltip("文字の位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("プレイヤーの色（ガードの光）")]
        [SerializeField] private Color _playerColor = new Color(0.95f, 0.55f, 0.2f);
        [SerializeField] private Color _parryColor = new Color(1f, 0.8f, 0.1f);
        [SerializeField] private float _radius = 1.15f;
        [Tooltip("ジャストガードの光を出す時間（秒）")]
        [SerializeField] private float _parryDuration = 0.6f;
        [Tooltip("ガードした瞬間に強く光る時間（秒）")]
        [SerializeField] private float _blockFlashDuration = 0.15f;
        [SerializeField] private int _fontSize = 30;

        private GUIStyle _style;
        private Vector3 _center;
        private float _parryAt = -10f;
        private float _blockAt = -10f;

        /// <summary>ガードの輪と「GUARD!」を出しているか。</summary>
        public bool ShowingGuard { get; private set; }

        /// <summary>ジャストガードの光と「PARRY!」を出しているか。</summary>
        public bool ShowingParry => Time.time - _parryAt < _parryDuration;

        private void Awake()
        {
            SetEnabled(_glow, false);
            SetEnabled(_burst, false);
            SetEnabled(_rays, false);
        }

        /// <summary>プレイヤーの色を設定する（シーンのビルダー用）。</summary>
        public void Configure(Color playerColor)
        {
            _playerColor = playerColor;
        }

        /// <summary>ジャストガードに成功した（光と「PARRY!」を出し始める）。</summary>
        public void OnJustGuard()
        {
            _parryAt = Time.time;
        }

        /// <summary>ふつうのガードで攻撃を受けた（輪を強く光らせる）。</summary>
        public void OnGuarded()
        {
            _blockAt = Time.time;
        }

        /// <summary>毎フレーム呼ぶ。guarding はガード中か。center はキャラクターの中心。</summary>
        public void Render(Vector3 center, bool guarding)
        {
            _center = center;
            ShowingGuard = guarding;
            float time = Time.time;

            if (guarding && _glow != null)
            {
                float flash = Mathf.Clamp01(1f - (time - _blockAt) / _blockFlashDuration);
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 25f);
                Color color = Color.Lerp(_playerColor, Color.white, 0.25f * pulse + 0.6f * flash);
                color.a = 0.75f + 0.25f * flash;
                DrawCircle(_glow, center, _radius * (1f + 0.05f * pulse + 0.15f * flash), 0.14f + 0.06f * pulse + 0.12f * flash, color);
            }
            else
            {
                SetEnabled(_glow, false);
            }

            float parry = (time - _parryAt) / _parryDuration;
            if (parry >= 0f && parry < 1f)
            {
                Color gold = _parryColor;
                gold.a = 1f - parry;
                if (_burst != null)
                {
                    DrawCircle(_burst, center, Mathf.Lerp(0.6f, 3.2f, Mathf.Sqrt(parry)), Mathf.Lerp(0.35f, 0.05f, parry), gold);
                }
                if (_rays != null)
                {
                    DrawRays(center, parry, gold, time);
                }
            }
            else
            {
                SetEnabled(_burst, false);
                SetEnabled(_rays, false);
            }
        }

        private void DrawRays(Vector3 center, float parry, Color color, float time)
        {
            // 内側と外側の点を交互に結ぶ星形。外へ伸びながら消える。
            float inner = Mathf.Lerp(0.5f, 1.4f, parry);
            float outer = Mathf.Lerp(1.6f, 4f, Mathf.Sqrt(parry));
            float rotation = time * 2f;
            _rays.positionCount = RayCount * 2;
            _rays.loop = true;
            for (int i = 0; i < RayCount * 2; i++)
            {
                float angle = rotation + Mathf.PI * i / RayCount;
                float radius = i % 2 == 0 ? outer : inner;
                _rays.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, Z));
            }
            _rays.widthMultiplier = Mathf.Lerp(0.14f, 0.03f, parry);
            _rays.startColor = _rays.endColor = color;
            _rays.enabled = true;
        }

        private static void DrawCircle(LineRenderer line, Vector3 center, float radius, float width, Color color)
        {
            line.positionCount = CirclePoints;
            line.loop = true;
            for (int i = 0; i < CirclePoints; i++)
            {
                float angle = Mathf.PI * 2f * i / CirclePoints;
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
            bool parry = ShowingParry;
            if ((!ShowingGuard && !parry) || _camera == null)
            {
                return;
            }
            Vector3 screen = _camera.WorldToScreenPoint(_center + new Vector3(0f, 2.3f, 0f));
            if (screen.z <= 0f)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            string label = parry ? ParryLabel : GuardLabel;
            float scale = parry ? 1.4f - 0.3f * Mathf.Clamp01((Time.time - _parryAt) / _parryDuration) : 1f;
            _style.fontSize = Mathf.RoundToInt(_fontSize * scale);
            Color color = parry ? _parryColor : Color.Lerp(_playerColor, Color.white, 0.3f);
            var rect = new Rect(screen.x - 150f, Screen.height - screen.y - 30f, 300f, 60f);

            Color previous = GUI.color;
            GUI.color = new Color(0.1f, 0.05f, 0f, 0.85f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), label, _style);
            GUI.color = color;
            GUI.Label(rect, label, _style);
            GUI.color = previous;
        }
    }
}
