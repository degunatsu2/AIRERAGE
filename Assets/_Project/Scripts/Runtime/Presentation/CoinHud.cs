using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// コインの数の表示（docs/12_match_plan.md T4-4）: 全員の頭の上に所持コインの数（金色の丸と数字）。所持コインが増えた瞬間に、
    /// その人の位置から「+n」が浮かび上がって消える。状態を読むだけで、書き換えない。
    /// 増えたかどうかは、前のフレームの数と比べる（巻き戻しで減ったときは何も出さない）。
    /// 100 枚のコイン（虹色。docs/12 3.5）: 予告（残り 1 分。ユーザーの要望、2026-10-14）の間は、出る地点の上に「100 IN n」
    /// （画面のお知らせの帯と札は NoticeHud）。予告の間も出た後も、場所が画面の外なら、画面の端の矢印と
    /// 誘導表示の札（左に虹色のコイン、右に「JACKPOT」。縁と斜めの線は虹色）で示す。予告の時刻と場所はティック数とルールから決まる。
    /// 撃破ドロップのかたまり（D-49。ステージのどこかに固まって落ちる）: 画面の中なら上に「n COINS」、外なら誘導表示の札（左に
    /// コインの山、右に「COIN n」。縁と斜めの線は金色）。近くの撃破ドロップのコインを1つのかたまりとして数える。
    /// 誘導表示の札は EdgeGuideLayer が描く。
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
        [Tooltip("画面の外のときの誘導表示（矢印と札）を描く層")]
        [SerializeField] private EdgeGuideLayer _guides;
        [Tooltip("撃破ドロップのコインを1つのかたまりとして数える距離（m）")]
        [SerializeField] private float _clusterRadius = 3f;

        private readonly List<Popup> _popups = new List<Popup>();
        private int[] _shownHeld = new int[0];
        private ISimulationFeed _feed;
        private readonly List<Cluster> _clusters = new List<Cluster>();
        private Texture2D _dot;
        private Texture2D _jackpotIcon;
        private Texture2D _pileIcon;
        private GUIStyle _count;
        private GUIStyle _popup;
        private Vector3 _jackpotPosition;
        private int _secondsUntilJackpot;

        /// <summary>今浮かんでいる「+n」の数と、最後に出した枚数（テスト用）。</summary>
        public int PopupCount => _popups.Count;
        public int LastPopupAmount { get; private set; }

        /// <summary>100 枚のコインが出ているか、画面の外にあって端に矢印を出しているか、「JACKPOT」を出しているか（テスト用）。</summary>
        public bool JackpotPresent { get; private set; }
        public bool JackpotOffscreen { get; private set; }

        /// <summary>100 枚のコインを予告している間か（出る前。テスト用）。</summary>
        public bool JackpotIncoming { get; private set; }

        /// <summary>撃破ドロップのかたまりの数と、そのうち画面の外にある数（テスト用）。</summary>
        public int KoDropClusterCount => _clusters.Count;
        public int OffscreenKoDropClusters { get; private set; }

        private sealed class Cluster
        {
            public Vector2 Sum;
            public Vector2 First;
            public int Coins;
            public int Value;
            public bool Offscreen;

            public Vector3 Center => new Vector3(Sum.x / Coins, Sum.y / Coins, 0f);
        }

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
                return;
            }
            if (_guides == null)
            {
                Debug.LogError($"{name}: 誘導表示の層（EdgeGuideLayer）が設定されていません。画面の外のコインを示せません", this);
            }
            _jackpotIcon = GuideIcons.RainbowCoin(64);
            _pileIcon = GuideIcons.CoinPile(64);
        }

        private void OnDestroy()
        {
            if (_dot != null)
            {
                Destroy(_dot);
            }
            if (_jackpotIcon != null)
            {
                Destroy(_jackpotIcon);
            }
            if (_pileIcon != null)
            {
                Destroy(_pileIcon);
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
            UpdateJackpot();
            UpdateKoDrops();
        }

        // 撃破ドロップのコインを、近いもの同士でかたまりにまとめ、画面の外のかたまりは誘導表示を出す。
        private void UpdateKoDrops()
        {
            _clusters.Clear();
            OffscreenKoDropClusters = 0;
            CoinField field = _feed.Coins;
            if (field == null)
            {
                return;
            }
            foreach (FieldCoin coin in field.Coins)
            {
                if (coin.Origin != CoinEventKind.KoDrop)
                {
                    continue;
                }
                var position = new Vector2(coin.Position.X, coin.Position.Y);
                Cluster cluster = _clusters.Find(c => Vector2.Distance(c.First, position) <= _clusterRadius);
                if (cluster == null)
                {
                    cluster = new Cluster { First = position };
                    _clusters.Add(cluster);
                }
                cluster.Sum += position;
                cluster.Coins++;
                cluster.Value += coin.Value;
            }
            foreach (Cluster cluster in _clusters)
            {
                cluster.Offscreen = _guides != null && _guides.Add(cluster.Center, _color, _pileIcon, $"COIN {cluster.Value}");
                OffscreenKoDropClusters += cluster.Offscreen ? 1 : 0;
            }
        }

        // 100 枚のコインを探す。出る前の予告の間は、出る地点を示す。画面の外なら誘導表示の札を出す。
        private void UpdateJackpot()
        {
            JackpotPresent = false;
            JackpotIncoming = false;
            JackpotOffscreen = false;
            CoinField field = _feed.Coins;
            MatchRules rules = _feed.Rules;
            if (field == null || rules == null)
            {
                return;
            }
            bool found = false;
            foreach (FieldCoin coin in field.Coins)
            {
                if (CoinView.LookOf(coin.Value) != CoinLook.Rainbow)
                {
                    continue;
                }
                found = true;
                JackpotPresent = true;
                _jackpotPosition = new Vector3(coin.Position.X, coin.Position.Y, 0f);
                break;
            }
            if (!found)
            {
                // まだ出ていなければ、予告の間か（予告の時刻と場所は、ティック数とルールから決まる）。
                int tick = _feed.CurrentTick;
                int announce = FieldCoinRules.JackpotAnnounceTick(rules);
                int spawn = FieldCoinRules.JackpotSpawnTick(rules);
                int spot = FieldCoinRules.JackpotSpot(rules, _feed.CoinSpawns);
                if (announce < 0 || spot < 0 || tick < announce || tick > spawn)
                {
                    return;
                }
                JackpotIncoming = true;
                _secondsUntilJackpot = Mathf.CeilToInt((spawn + 1 - tick) / (float)SimTime.TicksPerSecond);
                System.Numerics.Vector2 position = _feed.CoinSpawns[spot].Position;
                _jackpotPosition = new Vector3(position.X, position.Y, 0f);
            }
            string text = JackpotIncoming ? $"JACKPOT {_secondsUntilJackpot}" : "JACKPOT";
            JackpotOffscreen = _guides != null && _guides.Add(_jackpotPosition, Color.white, _jackpotIcon, text, JackpotIncoming ? 0.85f : 1f, rainbow: true);
        }

        private void OnGUI()
        {
            if (_count == null)
            {
                _count = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft });
                _popup = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
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
            DrawJackpot();
            foreach (Cluster cluster in _clusters)
            {
                if (!cluster.Offscreen && ToScreen(cluster.Center + new Vector3(0f, 2.3f, 0f), out Vector2 above))
                {
                    Shadowed(new Rect(above.x - 90f, above.y - 16f, 180f, 32f), $"{cluster.Value} COINS", _popup, _color);
                }
            }
            GUI.color = previous;
        }

        // 予告の間は、出る地点の上に「100 IN n」（画面の外の札は EdgeGuideLayer、画面のお知らせは NoticeHud が描く）。
        private void DrawJackpot()
        {
            if (!JackpotIncoming || JackpotOffscreen)
            {
                return;
            }
            Color rainbow = Color.HSVToRGB(Mathf.Repeat(Time.time / 1.6f, 1f), 0.65f, 1f);
            if (ToScreen(_jackpotPosition + new Vector3(0f, 1.6f, 0f), out Vector2 above))
            {
                Shadowed(new Rect(above.x - 100f, above.y - 18f, 200f, 36f), $"{CoinView.RainbowValue} IN {_secondsUntilJackpot}", _popup, rainbow);
            }
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
