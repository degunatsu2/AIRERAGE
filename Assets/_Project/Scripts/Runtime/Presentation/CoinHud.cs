using System.Collections.Generic;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// コインの数の表示（docs/12_match_plan.md T4-4）: 全員の頭の上に所持コインの数（金色の丸と数字）。所持コインが増えた瞬間に、
    /// その人の位置から「+n」が浮かび上がって消える。状態を読むだけで、書き換えない。
    /// 増えたかどうかは、前のフレームの数と比べる（巻き戻しで減ったときは何も出さない）。
    /// </summary>
    public sealed class CoinHud : MonoBehaviour
    {
        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [SerializeField] private float _popupSeconds = 1f;
        [SerializeField] private float _popupRise = 1.4f;
        [SerializeField] private Color _color = new Color(1f, 0.85f, 0.25f);

        private readonly List<Popup> _popups = new List<Popup>();
        private int[] _shownHeld = new int[0];
        private ISimulationFeed _feed;
        private Texture2D _dot;
        private GUIStyle _count;
        private GUIStyle _popup;

        /// <summary>今浮かんでいる「+n」の数と、最後に出した枚数（テスト用）。</summary>
        public int PopupCount => _popups.Count;
        public int LastPopupAmount { get; private set; }

        private struct Popup
        {
            public Vector3 Position;
            public int Amount;
            public float StartedAt;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _camera == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）かカメラが設定されていません", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_dot != null)
            {
                Destroy(_dot);
            }
        }

        private void Update()
        {
            int count = _feed.CharacterCount;
            if (_shownHeld.Length != count)
            {
                _shownHeld = new int[count];
                for (int i = 0; i < count; i++)
                {
                    _shownHeld[i] = _feed.GetState(i).Coins.Held;
                }
            }
            for (int i = 0; i < count; i++)
            {
                CharacterState state = _feed.GetState(i);
                int held = state.Coins.Held;
                if (held > _shownHeld[i])
                {
                    LastPopupAmount = held - _shownHeld[i];
                    _popups.Add(new Popup
                    {
                        Position = new Vector3(state.Position.X, state.Position.Y + 1.2f, 0f),
                        Amount = LastPopupAmount,
                        StartedAt = Time.time,
                    });
                }
                _shownHeld[i] = held;
            }
            _popups.RemoveAll(p => Time.time - p.StartedAt > _popupSeconds);
        }

        private void OnGUI()
        {
            if (_count == null)
            {
                _count = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _popup = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _dot = CreateDot(32);
            }
            Color previous = GUI.color;
            int count = _feed.CharacterCount;
            for (int i = 0; i < count; i++)
            {
                CharacterState state = _feed.GetState(i);
                if (state.Combat.IsWaitingToRespawn)
                {
                    continue;
                }
                if (!ToScreen(new Vector3(state.Position.X, state.Position.Y + 1.6f, 0f), out Vector2 screen))
                {
                    continue;
                }
                GUI.color = _color;
                GUI.DrawTexture(new Rect(screen.x - 26f, screen.y - 8f, 16f, 16f), _dot);
                Shadowed(new Rect(screen.x - 6f, screen.y - 14f, 80f, 28f), state.Coins.Held.ToString(), _count, Color.white);
            }
            foreach (Popup popup in _popups)
            {
                float t = Mathf.Clamp01((Time.time - popup.StartedAt) / _popupSeconds);
                if (!ToScreen(popup.Position + new Vector3(0f, _popupRise * t, 0f), out Vector2 screen))
                {
                    continue;
                }
                Color color = _color;
                color.a = 1f - t * t;
                Shadowed(new Rect(screen.x - 80f, screen.y - 20f, 160f, 40f), $"+{popup.Amount}", _popup, color);
            }
            GUI.color = previous;
        }

        private bool ToScreen(Vector3 world, out Vector2 screen)
        {
            Vector3 p = _camera.WorldToScreenPoint(world);
            screen = new Vector2(p.x, Screen.height - p.y);
            return p.z > 0f;
        }

        private static void Shadowed(Rect rect, string text, GUIStyle style, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.75f * color.a);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
        }

        // 丸い点のテクスチャ（コインの印）。
        private static Texture2D CreateDot(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "CoinDot", hideFlags = HideFlags.HideAndDontSave };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
                }
            }
            texture.Apply();
            return texture;
        }
    }
}
