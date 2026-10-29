using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 拠点（バンク。docs/12_match_plan.md T4-7）の表示。状態を読むだけで、書き換えない。場所はティック数とルールから求める
    /// （BankRules.SpotAt）。
    /// - 今の拠点: 脈打つ輪（確定に使う円と同じ半径）、回る弧、上へ伸びる光の柱、「BANK」の文字。
    /// - 予告（移る 10 秒前から）: 次の地点に点滅する輪と「NEXT n」、今の拠点に「MOVE n」。予告が始まった瞬間に、呼び鈴の音を
    ///   鳴らす（ユーザーの要望、2026-10-14）。画面のお知らせ（タイマーの下の「BANK MOVES」の札）は NoticeHud。
    /// - 画面の外にあるときは、画面の端の矢印と誘導表示の札（GuidePlate。左に拠点の絵、右に「BANK」。縁は水色）で場所を示す
    ///   （予告中は次の地点も「NEXT」の札で）。
    /// - だれかが確定したら、その人の位置から「+n BANKED」が浮かび、輪がその人の色で光る（確定した数を前のフレームと比べる）。
    /// 線の動きは Time.time で決める（巻き戻しとは関係ない）。
    /// </summary>
    public sealed class BankView : MonoBehaviour
    {
        private const int RingSegments = 56;
        private const int SweepSegments = 14;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [SerializeField] private LineRenderer _ring;
        [SerializeField] private LineRenderer _sweep;
        [SerializeField] private LineRenderer _beam;
        [SerializeField] private LineRenderer _next;
        [Tooltip("予告の音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("画面の外のときの誘導表示（矢印と札）を描く層")]
        [SerializeField] private EdgeGuideLayer _guides;
        [Tooltip("拠点の色（輪、誘導表示の札の縁と斜めの線。水色）")]
        [SerializeField] private Color _color = new Color(0.35f, 0.85f, 1f);
        [Tooltip("予告が始まってから、これだけ遅れて表示を始めたときは音を鳴らさない（ティック。途中から見始めたとき）")]
        [SerializeField] private int _noticeSoundWindowTicks = 30;
        [Tooltip("光の柱の高さ（m）")]
        [SerializeField] private float _beamHeight = 9f;
        [SerializeField] private float _popupSeconds = 1.3f;
        [SerializeField] private float _flashSeconds = 0.5f;

        private readonly List<Popup> _popups = new List<Popup>();
        private int[] _shownBanked = new int[0];
        private ISimulationFeed _feed;
        private Texture2D _bankIcon;
        private GUIStyle _label;
        private GUIStyle _popup;
        private float _flashStartedAt = float.NegativeInfinity;
        private Color _flashColor = Color.white;
        private Vector3 _bankCenter;
        private Vector3 _nextCenter;
        private float _radius;
        private int _secondsUntilMove;
        private bool _nextOffscreen;

        /// <summary>今の拠点の地点の番号（なければ -1）と、予告している次の地点の番号（予告していなければ -1）。テスト用。</summary>
        public int CurrentSpot { get; private set; } = -1;
        public int NextSpot { get; private set; } = -1;

        /// <summary>今の拠点が画面の外か（端に矢印を出しているか）。テスト用。</summary>
        public bool BankOffscreen { get; private set; }


        /// <summary>予告が始まった回数（テスト用）。</summary>
        public int NoticeCount { get; private set; }

        /// <summary>浮かんでいる「+n BANKED」の数と、最後に確定した枚数（テスト用）。</summary>
        public int PopupCount => _popups.Count;
        public int LastBankedAmount { get; private set; }

        private struct Popup
        {
            public Vector3 Position;
            public int Amount;
            public Color Color;
            public float StartedAt;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _camera == null || _ring == null || _sweep == null || _beam == null || _next == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）、カメラ、線のどれかが設定されていません", this);
                enabled = false;
                return;
            }
            _ring.positionCount = RingSegments;
            _ring.loop = true;
            _next.positionCount = RingSegments;
            _next.loop = true;
            _sweep.positionCount = SweepSegments;
            _beam.positionCount = 2;
            _bankIcon = GuideIcons.Bank(64, _color);
            if (_guides == null)
            {
                Debug.LogError($"{name}: 誘導表示の層（EdgeGuideLayer）が設定されていません。画面の外の拠点を示せません", this);
            }
        }

        private void OnDestroy()
        {
            if (_bankIcon != null)
            {
                Destroy(_bankIcon);
            }
        }

        private void Update()
        {
            DetectBanking();
            _popups.RemoveAll(p => Time.time - p.StartedAt > _popupSeconds);

            IReadOnlyList<BankSpot> spots = _feed.BankSpots;
            MatchRules rules = _feed.Rules;
            if (rules == null || spots == null || spots.Count == 0)
            {
                CurrentSpot = -1;
                NextSpot = -1;
                BankOffscreen = false;
                SetVisible(false, false);
                return;
            }
            int tick = _feed.CurrentTick;
            CurrentSpot = BankRules.SpotAt(rules, spots.Count, tick);
            int next = BankRules.NextSpotAt(rules, spots.Count, tick, out int ticksUntilMove);
            int previousNext = NextSpot;
            NextSpot = next >= 0 && ticksUntilMove <= rules.BankNoticeTicks ? next : -1;
            if (NextSpot >= 0 && previousNext < 0)
            {
                // 予告が始まった（巻き戻しではティックは戻らないので、1 回の予告で 1 回だけ）。
                NoticeCount++;
                if (_audio != null && rules.BankNoticeTicks - ticksUntilMove <= _noticeSoundWindowTicks)
                {
                    _audio.Play(SoundId.BankNotice);
                }
            }
            _secondsUntilMove = Mathf.CeilToInt(ticksUntilMove / (float)SimTime.TicksPerSecond);
            _radius = rules.BankRadius;
            _bankCenter = ToWorld(spots[CurrentSpot].Position);
            SetVisible(true, NextSpot >= 0);

            float time = Time.time;
            float flash = Mathf.Clamp01(1f - (time - _flashStartedAt) / _flashSeconds);
            Color ringColor = Color.Lerp(_color, _flashColor, flash);
            float pulse = 1f + 0.05f * Mathf.Sin(time * 4f) + 0.15f * flash;
            DrawRing(_ring, _bankCenter, _radius * pulse, ringColor, 0.95f);

            // 輪の内側を回る弧。
            float start = time * 2.2f;
            for (int i = 0; i < SweepSegments; i++)
            {
                float angle = start + i / (float)(SweepSegments - 1) * Mathf.PI * 0.6f;
                _sweep.SetPosition(i, _bankCenter + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * _radius * 0.78f);
            }
            SetGradient(_sweep, ringColor, 0f, 0.8f);

            // 上へ伸びて薄くなる光の柱。
            _beam.SetPosition(0, _bankCenter + new Vector3(0f, -_radius * 0.6f, 0f));
            _beam.SetPosition(1, _bankCenter + new Vector3(0f, _beamHeight, 0f));
            SetGradient(_beam, ringColor, 0.35f + 0.25f * flash, 0f);

            if (NextSpot >= 0)
            {
                _nextCenter = ToWorld(spots[NextSpot].Position);
                float blink = 0.35f + 0.45f * (0.5f + 0.5f * Mathf.Sin(time * 9f));
                DrawRing(_next, _nextCenter, _radius, _color, blink);
            }

            // 画面の外なら、誘導表示の札（左に拠点の絵、右に「BANK」。予告中は次の地点も「NEXT」で）。
            BankOffscreen = _guides != null && _guides.Add(_bankCenter, _color, _bankIcon, "BANK");
            _nextOffscreen = NextSpot >= 0 && _guides != null && _guides.Add(_nextCenter, _color, _bankIcon, "NEXT", 0.7f);
        }

        // 確定した数が増えた人を探し、「+n BANKED」を出して輪を光らせる（巻き戻しで減ったときは何も出さない）。
        private void DetectBanking()
        {
            int count = _feed.CharacterCount;
            if (_shownBanked.Length != count)
            {
                _shownBanked = new int[count];
                for (int i = 0; i < count; i++)
                {
                    _shownBanked[i] = _feed.GetState(i).Coins.Banked;
                }
                return;
            }
            for (int i = 0; i < count; i++)
            {
                CharacterState state = _feed.GetState(i);
                int banked = state.Coins.Banked;
                if (banked > _shownBanked[i])
                {
                    LastBankedAmount = banked - _shownBanked[i];
                    _flashColor = _feed.ColorOf(i);
                    _flashStartedAt = Time.time;
                    _popups.Add(new Popup
                    {
                        Position = new Vector3(state.Position.X, state.Position.Y + 1.6f, 0f),
                        Amount = LastBankedAmount,
                        Color = _flashColor,
                        StartedAt = Time.time,
                    });
                }
                _shownBanked[i] = banked;
            }
        }

        private void OnGUI()
        {
            if (CurrentSpot < 0 && _popups.Count == 0)
            {
                return;
            }
            if (_label == null)
            {
                _label = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
                _popup = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            }
            Color previous = GUI.color;
            if (CurrentSpot >= 0)
            {
                string move = NextSpot >= 0 ? $"MOVE {_secondsUntilMove}" : null;
                DrawMarker(_bankCenter, BankOffscreen, "BANK", move, 1f);
                if (NextSpot >= 0)
                {
                    DrawMarker(_nextCenter, _nextOffscreen, $"NEXT {_secondsUntilMove}", null, 0.7f);
                }
            }
            foreach (Popup popup in _popups)
            {
                float t = Mathf.Clamp01((Time.time - popup.StartedAt) / _popupSeconds);
                if (!ToScreen(popup.Position + new Vector3(0f, 1.6f * t, 0f), out Vector2 screen))
                {
                    continue;
                }
                Color color = Color.Lerp(Color.white, _color, 0.5f);
                color.a = 1f - t * t;
                Shadowed(new Rect(screen.x - 120f, screen.y - 20f, 240f, 40f), $"+{popup.Amount} BANKED", _popup, color);
            }
            GUI.color = previous;
        }

        // 画面の中なら輪の上に文字（外のときは EdgeGuideLayer が札を描く）。
        private void DrawMarker(Vector3 center, bool offscreen, string title, string sub, float alpha)
        {
            Color color = _color;
            color.a = alpha;
            if (offscreen)
            {
                return;
            }
            if (!ToScreen(center + new Vector3(0f, _radius + 0.7f, 0f), out Vector2 screen))
            {
                return;
            }
            Shadowed(new Rect(screen.x - 90f, screen.y - 16f, 180f, 30f), title, _label, color);
            if (sub != null)
            {
                Shadowed(new Rect(screen.x - 90f, screen.y - 42f, 180f, 26f), sub, _label, color);
            }
        }

        private void SetVisible(bool bank, bool next)
        {
            _ring.enabled = bank;
            _sweep.enabled = bank;
            _beam.enabled = bank;
            _next.enabled = next;
        }

        private static void DrawRing(LineRenderer line, Vector3 center, float radius, Color color, float alpha)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
            color.a = alpha;
            line.startColor = line.endColor = color;
        }

        // 線の始まりから終わりへ、透明度を変える。
        private static void SetGradient(LineRenderer line, Color color, float startAlpha, float endAlpha)
        {
            Color start = color;
            start.a = startAlpha;
            Color end = color;
            end.a = endAlpha;
            line.startColor = start;
            line.endColor = end;
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

        private static Vector3 ToWorld(System.Numerics.Vector2 point)
        {
            return new Vector3(point.X, point.Y, 0f);
        }
    }
}
