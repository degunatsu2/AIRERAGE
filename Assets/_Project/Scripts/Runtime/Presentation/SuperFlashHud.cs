using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 必殺技を出したときの暗転（docs/13_character_plan.md T5-3、T5-4）: 画面を暗くし、出した人の周りだけを丸く明るく残す
    /// （スポットライト）。その下に技の名前を出す。だれが出しても出す（オンラインではほかのプレイヤーも）。
    /// 表示だけの演出で、シミュレーションは止めない（必殺技のためのあいだ、出した人がその場で止まる）。
    /// ほかの表示（タイマー、右上の表など）より奥に描く（GUI.depth）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class SuperFlashHud : MonoBehaviour
    {
        private const int HoleSize = 256;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("出した人の位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("暗転の長さ（秒）")]
        [SerializeField] private float _seconds = 0.7f;
        [Tooltip("いちばん暗いときの暗さ（0〜1）")]
        [SerializeField] private float _darkness = 0.6f;
        [Tooltip("明るく残す丸の半径（ピクセル）")]
        [SerializeField] private float _spotRadius = 170f;

        private ISimulationFeed _feed;
        private ActionState[] _previous = new ActionState[0];
        private Texture2D _hole;
        private GUIStyle _name;
        private int _target = -1;
        private float _startedAt = -10f;

        /// <summary>暗転した回数（テスト用）。</summary>
        public int FlashCount { get; private set; }

        /// <summary>今暗転しているか。</summary>
        public bool IsFlashing => _target >= 0 && Time.time - _startedAt < _seconds;

        /// <summary>最後に暗転させたキャラクターの番号（なければ -1）。</summary>
        public int LastTarget => _target;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_hole != null)
            {
                Destroy(_hole);
            }
        }

        private void Update()
        {
            int count = _feed.CharacterCount;
            if (_previous.Length != count)
            {
                _previous = new ActionState[count];
                for (int i = 0; i < count; i++)
                {
                    _previous[i] = _feed.GetState(i).Action;
                }
                return;
            }
            for (int i = 0; i < count; i++)
            {
                ActionState now = _feed.GetState(i).Action;
                ActionState before = _previous[i];
                bool super = now.Kind == ActionKind.Attack && now.Attack == AttackSlot.Super;
                bool wasSuper = before.Kind == ActionKind.Attack && before.Attack == AttackSlot.Super;
                if (super && (!wasSuper || now.Ticks < before.Ticks))
                {
                    _target = i;
                    _startedAt = Time.time;
                    FlashCount++;
                }
                _previous[i] = now;
            }
        }

        private void OnGUI()
        {
            if (!IsFlashing || _camera == null || _target >= _feed.CharacterCount)
            {
                return;
            }
            EnsureResources();
            float progress = (Time.time - _startedAt) / _seconds;
            float alpha = _darkness * (progress < 0.12f ? progress / 0.12f : progress < 0.6f ? 1f : 1f - (progress - 0.6f) / 0.4f);
            CharacterState state = _feed.GetState(_target);
            Vector3 p = _camera.WorldToScreenPoint(new Vector3(state.Position.X, state.Position.Y, 0f));
            if (p.z <= 0f)
            {
                return;
            }
            var center = new Vector2(p.x, Screen.height - p.y);
            float r = _spotRadius;
            float left = center.x - r;
            float top = center.y - r;
            float right = center.x + r;
            float bottom = center.y + r;

            GUI.depth = 1;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0.02f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Mathf.Max(0f, top)), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, bottom, Screen.width, Mathf.Max(0f, Screen.height - bottom)), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, top, Mathf.Max(0f, left), r * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(right, top, Mathf.Max(0f, Screen.width - right), r * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(left, top, r * 2f, r * 2f), _hole);

            string moveName = _feed.GetConfig(_target).Combat.Super.Name;
            if (!string.IsNullOrEmpty(moveName))
            {
                Color gold = Color.HSVToRGB(Mathf.Repeat(Time.time / 1.2f, 1f), 0.45f, 1f);
                gold.a = Mathf.Clamp01(alpha / _darkness);
                GuidePlate.DrawText(new Rect(center.x - 200f, bottom - 24f, 400f, 44f), $"{_feed.LabelOf(_target)}  {moveName}", _name, gold);
            }
            GUI.color = previous;
        }

        private void EnsureResources()
        {
            if (_hole != null)
            {
                return;
            }
            // 真ん中が透明で、外へ向かってなめらかに暗くなる丸（四隅は暗い）。
            _hole = TimerPlate.Create("SuperFlashHole", HoleSize, HoleSize, (x, y) =>
            {
                float dx = (x + 0.5f) / HoleSize * 2f - 1f;
                float dy = (y + 0.5f) / HoleSize * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, d));
            });
            _name = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _name.normal.textColor = Color.white;
        }
    }
}
