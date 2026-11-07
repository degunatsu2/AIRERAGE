using System;
using System.Collections.Generic;
using Airerage.Runtime.Audio;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>画面の切り替えの演出の種類（D-87。ユーザーの決定、2026-11-07）。</summary>
    public enum TransitionKind
    {
        /// <summary>T1 ガストアウト: 札が風に吹かれたように流れて消え、次の札が反対側から吹き込む（ほとんどの画面。戻るときは向きが逆）。</summary>
        Gust,

        /// <summary>
        /// T2 スラントワイプ → T9 ロスターカスケード: 斜めの札 3 枚（1 枚目は選んだモードの色）が画面を覆って開き、キャラクター選択の
        /// アイコンが順にめくれて並ぶ。キャラクター選択からメインメニューへ戻るときも（「MAIN MENU」、水色、逆向き。2026-11-07）。
        /// </summary>
        SlantCascade,

        /// <summary>T4 スカイダイブ: タイトルが手前へ抜け、速さの線と白い光とともに空の中のメインメニューへ（カメラも前へ突進する）。</summary>
        SkyDive,
    }

    /// <summary>
    /// メニューの画面の切り替えの演出（D-87）。画面を入れ替える処理（swap）はすぐに行い、見た目だけを動かす:
    /// 1. 出ていく画面の写しを作り（操作に反応する部品を外した、絵と文字だけの写し）、写しを動かして消す。
    /// 2. 出てくる画面は、動きが終わるまで透明にしておき、その写しを動かして出す。終わったら写しを消して本物を見せる。
    ///    出てくる画面の写しは、動いている本物に毎フレーム合わせる（札の位置・濃さ・色・演出の状態。選んだ項目が変わったときや、ほかの項目が
    ///    暗く下がる B13 が、切り替えの途中でも見えるように。2026-11-08）。
    /// 出てくる画面は入れ替えた瞬間から操作できる（演出で待たせない。今までの切り替えと同じ）。動いている間にキー・ボタンを押すと、
    /// 演出を最後まで早送りして本物の画面を見せる（押した入力は、今までどおり画面が受け取る）。
    /// 動かす単位は、画面の直下の子（TransitionGroup の印のある入れ物は、その子を 1 つずつ）。画面の 35% より大きい背景は動かさずに
    /// 薄く消す・出す。
    /// メニューの Canvas の最後の子（いちばん手前）に置く。写しはこの直前に置く。
    /// </summary>
    public sealed class ScreenTransition : MonoBehaviour
    {
        private const float BackdropArea = 0.35f;
        private static readonly Color BandCyan = new Color(0.35f, 0.85f, 1f);
        private static readonly Color BandWhite = new Color(0.93f, 0.97f, 1f);
        private static readonly Color BandNavy = new Color(0.04f, 0.09f, 0.15f);

        [Tooltip("画面の親（メニューの Canvas）。直下の子が画面")]
        [SerializeField] private RectTransform _screens;
        [Tooltip("入れ替えの対象にしない子（終了の確認など）")]
        [SerializeField] private GameObject[] _ignore = new GameObject[0];
        [Tooltip("スカイダイブで前へ突進させる背景のカメラ（なくてもよい）")]
        [SerializeField] private MenuCameraDrift _camera;

        private sealed class Part
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector2 Home;
            public Vector3 Scale;
            public float BaseAlpha;
            public Vector2 Center;
            public bool Tile;
            public bool Backdrop;
            // 写した元の本物（出てくる画面の写しは、本物の動きに合わせる。2026-11-08）。
            public RectTransform Source;
            public Vector2 SourceHome;
            public CanvasGroup SourceGroup;
            public bool Live;
            public bool Posed;
            public Vector2 LastOffset;
            public Vector2 LastScale;
            public float LastAlpha;
        }

        // 写しの中の物と、その元の本物（出てくる画面の写しを、本物に合わせ続けるため）。
        private sealed class Mirror
        {
            public RectTransform Source;
            public RectTransform Copy;
            public CanvasGroup SourceGroup;
            public CanvasGroup CopyGroup;
            public Graphic SourceGraphic;
            public Graphic CopyGraphic;
        }

        private sealed class Ghost
        {
            public RectTransform Source;
            public RectTransform Root;
            public CanvasGroup Group;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<Mirror> Mirrors = new List<Mirror>();
        }

        private sealed class Track
        {
            public float Start;
            public float Duration;
            public bool Hold;
            public bool Done;
            public Action<float> Apply;
        }

        private sealed class Cue
        {
            public float Time;
            public Action Action;
            public bool Fired;
        }

        private sealed class Hidden
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public bool Added;
        }

        private readonly List<Track> _tracks = new List<Track>();
        private readonly List<Cue> _cues = new List<Cue>();
        private readonly List<Ghost> _outgoing = new List<Ghost>();
        private readonly List<Ghost> _incoming = new List<Ghost>();
        private readonly List<GameObject> _temps = new List<GameObject>();
        private readonly List<Hidden> _hidden = new List<Hidden>();
        private readonly List<RectTransform> _before = new List<RectTransform>();

        private RectTransform _layer;
        private RectTransform _workshop;
        private float _t0;
        private int _startFrame;
        private bool _running;
        private bool _inSwap;
        private bool _incomingPending;
        private bool _skip;
        private int _dir = 1;
        private string _label;
        private Color _accent = BandCyan;
        private IDisposable _anyButton;
        private bool _frozen;

        /// <summary>演出の途中か。</summary>
        public bool Running => _running;

        /// <summary>画面を入れ替える処理の途中か（その中から Run が呼ばれたら、演出を重ねずにそのまま入れ替える）。</summary>
        public bool InSwap => _inSwap;

        /// <summary>最後に始めた演出の種類（テスト用）。</summary>
        public TransitionKind LastKind { get; private set; }

        /// <summary>最後に始めた演出の向き（1: 進む、-1: 戻る。テスト用）。</summary>
        public int LastDirection => _dir;

        /// <summary>今動いている写しの数（テスト用）。</summary>
        public int GhostCount => _outgoing.Count + _incoming.Count;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(RectTransform screens, GameObject[] ignore, MenuCameraDrift camera)
        {
            _screens = screens;
            _ignore = ignore ?? new GameObject[0];
            _camera = camera;
        }

        private void Awake()
        {
            _layer = (RectTransform)transform;
            var go = new GameObject("Workshop", typeof(RectTransform));
            go.SetActive(false);
            _workshop = (RectTransform)go.transform;
            _workshop.SetParent(_layer, false);
            Stretch(_workshop);
        }

        private void OnDisable()
        {
            Complete();
        }

        /// <summary>
        /// 画面を入れ替え、kind の演出をする。direction は 1 なら進む（右から入る）、-1 なら戻る（左から入る）。origin は決定した札
        /// （B10 の衝撃を出してから写す）。label は斜めの札に出す行き先の名前（SlantCascade）。accent は斜めの札の 1 枚目の色（なければ
        /// 決定した札の項目の色、それもなければ水色。選んだモードのイメージカラー。2026-11-07）。
        /// </summary>
        public void Run(TransitionKind kind, int direction, RectTransform origin, Action swap, string label = null, Color? accent = null)
        {
            if (_inSwap)
            {
                swap?.Invoke();
                return;
            }
            Complete();
            if (_screens == null || _layer == null || !isActiveAndEnabled)
            {
                swap?.Invoke();
                return;
            }
            LastKind = kind;
            _dir = direction >= 0 ? 1 : -1;
            _label = label;
            // 風の音（2026-11-07。docs/18 4.19）: 進むときは空へ吸い込まれる風、戻るときは後ろへ遠ざかる風。スカイダイブはタイトルの
            // 決定の音に風が入っているので鳴らさない。
            if (kind != TransitionKind.SkyDive)
            {
                UiSounds.Find().Play(_dir > 0 ? SoundId.UiWhoosh : SoundId.UiWhooshBack);
            }
            MenuOption decided = origin != null ? origin.GetComponent<MenuOption>() : null;
            if (decided != null)
            {
                decided.Punch();
            }
            _accent = accent ?? (decided != null ? decided.Accent : BandCyan);

            _before.Clear();
            CollectActive(_before);
            foreach (RectTransform root in _before)
            {
                _outgoing.Add(MakeGhost(root));
            }

            _inSwap = true;
            try
            {
                swap?.Invoke();
            }
            finally
            {
                _inSwap = false;
            }

            var after = new List<RectTransform>();
            CollectActive(after);
            for (int i = _outgoing.Count - 1; i >= 0; i--)
            {
                // 入れ替えの後も出ている画面（同じ画面を出し直した、ずっと出ている物）は動かさない。
                if (after.Contains(_outgoing[i].Source))
                {
                    DestroyGhost(_outgoing[i]);
                    _outgoing.RemoveAt(i);
                }
            }
            foreach (RectTransform root in after)
            {
                if (!_before.Contains(root))
                {
                    Hide(root);
                }
            }
            _t0 = Time.unscaledTime;
            _startFrame = Time.frameCount;
            _running = true;
            _incomingPending = true;
            _skip = false;
            BuildOutgoing();
            _anyButton = InputSystem.onAnyButtonPress.Call(_ => _skip = true);
            Step(0f);
        }

        /// <summary>
        /// 演出の進みを t 秒の所に合わせて止める（途中のコマの画像を撮るテスト用。進める向きにだけ使う。出てくる画面の写しがまだなら
        /// 作る。Complete で終える）。
        /// </summary>
        public void SeekForTest(float t)
        {
            if (!_running)
            {
                return;
            }
            _frozen = true;
            _t0 = Time.unscaledTime - t;
            if (_incomingPending && t > 0.02f)
            {
                _incomingPending = false;
                BuildIncoming();
            }
            Step(t);
        }

        /// <summary>演出を最後まで飛ばして終える（途中に押されたとき、次の演出を始めるとき）。</summary>
        public void Complete()
        {
            if (!_running)
            {
                return;
            }
            _incomingPending = false;
            Step(float.MaxValue);
            Finish();
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }
            if (_skip)
            {
                Complete();
                return;
            }
            if (_frozen)
            {
                return;
            }
            float t = Time.unscaledTime - _t0;
            if (_incomingPending && Time.frameCount >= _startFrame + 2)
            {
                // 出てくる画面は、最初の項目を選ぶなどの用意が済んでから写す（入れ替えの 2 フレーム後）。
                _incomingPending = false;
                BuildIncoming();
            }
            Step(t);
            if (!_incomingPending && t >= EndTime())
            {
                Finish();
            }
        }

        // ---- 演出の組み立て -----------------------------------------------------------------------------------------------

        private void BuildOutgoing()
        {
            switch (LastKind)
            {
                case TransitionKind.SkyDive:
                {
                    if (_camera != null)
                    {
                        _camera.Dive();
                    }
                    var lines = NewFull("SpeedLines").gameObject.AddComponent<SpeedLines>();
                    lines.Configure(Vector2.zero, UnityEngine.Random.Range(1, int.MaxValue));
                    AddTrack(0f, 0.64f, false, p => lines.Progress = p >= 1f ? -1f : p);
                    AddFlash(0.3f, 0.24f, 0.75f);
                    foreach (Ghost ghost in _outgoing)
                    {
                        Ghost g = ghost;
                        AddTrack(0f, 0.38f, false, p =>
                        {
                            g.Root.localScale = Vector3.one * (1f + 1.6f * UiEase.InCubic(p));
                            g.Group.alpha = 1f - p;
                        });
                    }
                    AddCue(0.38f, DestroyOutgoing);
                    break;
                }
                case TransitionKind.SlantCascade:
                {
                    float end = 0f;
                    foreach (Ghost ghost in _outgoing)
                    {
                        for (int i = 0; i < ghost.Parts.Count; i++)
                        {
                            Part part = ghost.Parts[i];
                            float start = 0.09f + i * 0.01f;
                            AddTrack(start, 0.22f, false, p => Pose(part, new Vector2(-_dir * 60f * UiEase.InCubic(p), 0f), Vector2.one, 1f - p));
                            end = Mathf.Max(end, start + 0.22f);
                        }
                    }
                    // 札は画面の 1.6 倍の幅（画面の大きさに合わせて伸びる）。動く量も、そのときの画面の幅から決める。
                    var bands = new RectTransform[3];
                    Color[] colors = { _accent, BandWhite, BandNavy };
                    for (int k = 0; k < 3; k++)
                    {
                        RectTransform band = CreateBand(colors[k], k == 2 ? _label : null);
                        band.anchoredPosition = new Vector2(_dir * Travel(), 0f);
                        bands[k] = band;
                        AddTrack(0.09f + k * 0.05f, 0.22f, false,
                            p => band.anchoredPosition = new Vector2(Mathf.Lerp(_dir * Travel(), 0f, UiEase.InOutCubic(p)), 0f));
                    }
                    const float covered = 0.41f;
                    AddCue(Mathf.Max(covered, end), DestroyOutgoing);
                    for (int m = 0; m < 3; m++)
                    {
                        RectTransform band = bands[2 - m];
                        AddTrack(covered + 0.09f + m * 0.05f, 0.28f, false,
                            p => band.anchoredPosition = new Vector2(Mathf.Lerp(0f, -_dir * Travel(), UiEase.InOutCubic(p)), 0f));
                    }
                    break;
                }
                default:
                {
                    // 風の線は出さない（札の動きだけ。ユーザーの要望、2026-11-07）。
                    float end = 0f;
                    foreach (Ghost ghost in _outgoing)
                    {
                        Ghost g = ghost;
                        int moving = 0;
                        foreach (Part part in ghost.Parts)
                        {
                            Part q = part;
                            if (q.Backdrop)
                            {
                                continue;
                            }
                            float start = moving * 0.016f;
                            moving++;
                            AddTrack(start, 0.2f, false, p =>
                                Pose(q, new Vector2(-_dir * 600f * UiEase.InCubic(p), 0f), new Vector2(1f + 0.15f * p, 1f), 1f - p));
                            end = Mathf.Max(end, start + 0.2f);
                        }
                        AddTrack(0.1f, 0.2f, false, p => g.Group.alpha = 1f - p);
                    }
                    AddCue(Mathf.Max(0.3f, end), DestroyOutgoing);
                    break;
                }
            }
        }

        private void BuildIncoming()
        {
            var after = new List<RectTransform>();
            CollectActive(after);
            foreach (RectTransform root in after)
            {
                // 入れ替えの次のフレームに出てきた画面も隠す。
                if (!_before.Contains(root) && _hidden.Find(h => h.Root == root) == null)
                {
                    Hide(root);
                }
            }
            foreach (Hidden hidden in _hidden)
            {
                if (hidden.Root != null && hidden.Root.gameObject.activeInHierarchy)
                {
                    _incoming.Add(MakeGhost(hidden.Root));
                }
            }
            float half = _screens.rect.height * 0.5f;
            float halfWidth = _screens.rect.width * 0.5f;
            foreach (Ghost ghost in _incoming)
            {
                Ghost g = ghost;
                g.Group.alpha = 0f;
                foreach (Part part in g.Parts)
                {
                    part.Live = part.Source != null;
                }
                switch (LastKind)
                {
                    case TransitionKind.SkyDive:
                    {
                        AddTrack(0.38f, 0.44f, true, p =>
                        {
                            g.Root.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, UiEase.OutCubic(p));
                            g.Group.alpha = UiEase.Clamp01(p * 1.6f);
                        });
                        int j = 0;
                        foreach (Part part in g.Parts)
                        {
                            Part q = part;
                            if (q.Backdrop)
                            {
                                continue;
                            }
                            AddTrack(0.4f + j++ * 0.026f, 0.36f, true,
                                p => Pose(q, new Vector2(0f, -36f * (1f - UiEase.OutCubic(p))), Vector2.one, UiEase.Clamp01(p * 1.8f)));
                        }
                        break;
                    }
                    case TransitionKind.SlantCascade:
                    {
                        const float covered = 0.41f;
                        AddTrack(covered, 0.05f, true, p => g.Group.alpha = p);
                        bool cascade = g.Parts.Exists(part => part.Tile);
                        int corner = 0;
                        int j = 0;
                        foreach (Part part in g.Parts)
                        {
                            Part q = part;
                            if (q.Backdrop)
                            {
                                continue;
                            }
                            if (!cascade)
                            {
                                AddTrack(covered + 0.05f + j++ * 0.02f, 0.3f, true,
                                    p => Pose(q, new Vector2(_dir * 70f * (1f - UiEase.OutCubic(p)), 0f), Vector2.one, p));
                            }
                            else if (q.Tile)
                            {
                                // アイコン: 真ん中から外へ順に、横にめくれて並ぶ。
                                float delay = covered + 0.06f + q.Center.magnitude * 0.00028f;
                                AddTrack(delay, 0.38f, true, p => Pose(q, new Vector2(0f, -26f * (1f - UiEase.OutCubic(p))),
                                    new Vector2(Mathf.Max(0.001f, UiEase.OutBack(p, 1.2f)), 1f), UiEase.Clamp01(p * 2f)));
                            }
                            else if (Mathf.Abs(q.Center.x) > halfWidth * 0.6f && Mathf.Abs(q.Center.y) > half * 0.6f)
                            {
                                // 四隅の札: 画面の外から勢いよくはまる。
                                var from = new Vector2(Mathf.Sign(q.Center.x) * 420f, Mathf.Sign(q.Center.y) * 260f);
                                AddTrack(covered + 0.05f + corner++ * 0.05f, 0.42f, true,
                                    p => Pose(q, Vector2.LerpUnclamped(from, Vector2.zero, UiEase.OutBack(p, 1.4f)), Vector2.one, UiEase.Clamp01(p * 3f)));
                            }
                            else if (q.Center.y > half * 0.7f)
                            {
                                // 見出し: 上から下りてくる。
                                AddTrack(covered + 0.05f, 0.38f, true,
                                    p => Pose(q, new Vector2(0f, 70f * (1f - UiEase.OutCubic(p))), Vector2.one, UiEase.Clamp01(p * 2f)));
                            }
                            else
                            {
                                AddTrack(covered + 0.15f, 0.3f, true, p => Pose(q, new Vector2(0f, -20f * (1f - UiEase.OutCubic(p))), Vector2.one, p));
                            }
                        }
                        break;
                    }
                    default:
                    {
                        AddTrack(0.08f, 0.12f, true, p => g.Group.alpha = p);
                        int j = 0;
                        foreach (Part part in g.Parts)
                        {
                            Part q = part;
                            if (q.Backdrop)
                            {
                                continue;
                            }
                            AddTrack(0.12f + j++ * 0.02f, 0.33f, true, p =>
                            {
                                float x = p < 0.72f
                                    ? Mathf.Lerp(_dir * 480f, -_dir * 10f, UiEase.OutCubic(p / 0.72f))
                                    : Mathf.Lerp(-_dir * 10f, 0f, (p - 0.72f) / 0.28f);
                                Pose(q, new Vector2(x, 0f), new Vector2(Mathf.Lerp(1.12f, 1f, UiEase.OutCubic(p)), 1f), UiEase.Clamp01(p * 2.2f));
                            });
                        }
                        break;
                    }
                }
            }
        }

        // ---- 時間の進み ----------------------------------------------------------------------------------------------------

        private void Step(float t)
        {
            for (int i = 0; i < _cues.Count; i++)
            {
                Cue cue = _cues[i];
                if (!cue.Fired && t >= cue.Time)
                {
                    cue.Fired = true;
                    cue.Action();
                }
            }
            foreach (Track track in _tracks)
            {
                // 終わった動きは、最後の形を 1 回当てたらもう触らない（写しを消した後に、消した物を動かさないように）。
                if (track.Done || (t < track.Start && !track.Hold))
                {
                    continue;
                }
                float p = track.Duration <= 0f ? 1f : UiEase.Clamp01((t - track.Start) / track.Duration);
                track.Apply(p);
                if (p >= 1f)
                {
                    track.Done = true;
                }
            }
            foreach (Ghost ghost in _incoming)
            {
                Follow(ghost);
            }
        }

        // 出てくる画面の写しを、動いている本物に合わせる（札は今の演出の形のまま、本物の動いた分・濃さ・色・演出の状態を当てる）。
        private static void Follow(Ghost ghost)
        {
            foreach (Part part in ghost.Parts)
            {
                if (part.Live && part.Posed)
                {
                    Pose(part, part.LastOffset, part.LastScale, part.LastAlpha);
                }
            }
            foreach (Mirror mirror in ghost.Mirrors)
            {
                if (mirror.Source != null && mirror.Copy != null)
                {
                    bool active = mirror.Source.gameObject.activeSelf;
                    if (mirror.Copy.gameObject.activeSelf != active)
                    {
                        mirror.Copy.gameObject.SetActive(active);
                    }
                    if (!active)
                    {
                        continue;
                    }
                    FollowTransform(mirror);
                }
                FollowGraphic(mirror.SourceGraphic, mirror.CopyGraphic);
            }
        }

        private static void FollowTransform(Mirror mirror)
        {
            if (mirror.Copy.anchoredPosition != mirror.Source.anchoredPosition)
            {
                mirror.Copy.anchoredPosition = mirror.Source.anchoredPosition;
            }
            if (mirror.Copy.sizeDelta != mirror.Source.sizeDelta)
            {
                mirror.Copy.sizeDelta = mirror.Source.sizeDelta;
            }
            if (mirror.Copy.localScale != mirror.Source.localScale)
            {
                mirror.Copy.localScale = mirror.Source.localScale;
            }
            if (mirror.Copy.localRotation != mirror.Source.localRotation)
            {
                mirror.Copy.localRotation = mirror.Source.localRotation;
            }
            if (mirror.SourceGroup != null && mirror.CopyGroup != null && mirror.CopyGroup.alpha != mirror.SourceGroup.alpha)
            {
                mirror.CopyGroup.alpha = mirror.SourceGroup.alpha;
            }
        }

        private static void FollowGraphic(Graphic source, Graphic copy)
        {
            if (source == null || copy == null)
            {
                return;
            }
            if (copy.enabled != source.enabled)
            {
                copy.enabled = source.enabled;
            }
            switch (source)
            {
                case SlantedPlate plate:
                    ((SlantedPlate)copy).CopyEffects(plate);
                    break;
                case MenuIcon icon:
                    ((MenuIcon)copy).Selected = icon.Selected;
                    copy.color = icon.color;
                    break;
                case MainMenuPanel panel:
                    ((MainMenuPanel)copy).CopyState(panel);
                    break;
                case ChevronMarks marks:
                    ((ChevronMarks)copy).CopyState(marks);
                    break;
                case Text text:
                    var copyText = (Text)copy;
                    // 文字の演出（W1 の濃さ・W16 の説明の濃さ・W2 のはね。2026-11-08）。
                    float alpha = text.canvasRenderer.GetAlpha();
                    if (!Mathf.Approximately(copyText.canvasRenderer.GetAlpha(), alpha))
                    {
                        copyText.canvasRenderer.SetAlpha(alpha);
                    }
                    LetterRise rise = text.GetComponent<LetterRise>();
                    if (rise != null)
                    {
                        LetterRise copyRise = copyText.GetComponent<LetterRise>();
                        if (copyRise != null)
                        {
                            copyRise.CopyState(rise);
                        }
                    }
                    if (copyText.text != text.text)
                    {
                        copyText.text = text.text;
                    }
                    if (copyText.color != text.color)
                    {
                        copyText.color = text.color;
                    }
                    break;
                case RawImage image:
                    var copyImage = (RawImage)copy;
                    if (copyImage.texture != image.texture)
                    {
                        copyImage.texture = image.texture;
                    }
                    if (copyImage.color != image.color)
                    {
                        copyImage.color = image.color;
                    }
                    break;
                default:
                    if (copy.color != source.color)
                    {
                        copy.color = source.color;
                    }
                    break;
            }
        }

        private float EndTime()
        {
            float end = 0f;
            foreach (Track track in _tracks)
            {
                end = Mathf.Max(end, track.Start + track.Duration);
            }
            foreach (Cue cue in _cues)
            {
                end = Mathf.Max(end, cue.Time);
            }
            return end;
        }

        private void AddTrack(float start, float duration, bool hold, Action<float> apply)
        {
            _tracks.Add(new Track { Start = start, Duration = duration, Hold = hold, Apply = apply });
        }

        private void AddCue(float time, Action action)
        {
            _cues.Add(new Cue { Time = time, Action = action });
        }

        private void Finish()
        {
            DestroyOutgoing();
            foreach (Ghost ghost in _incoming)
            {
                DestroyGhost(ghost);
            }
            _incoming.Clear();
            foreach (GameObject temp in _temps)
            {
                if (temp != null)
                {
                    Destroy(temp);
                }
            }
            _temps.Clear();
            foreach (Hidden hidden in _hidden)
            {
                Reveal(hidden);
            }
            _hidden.Clear();
            _tracks.Clear();
            _cues.Clear();
            _anyButton?.Dispose();
            _anyButton = null;
            _running = false;
            _frozen = false;
        }

        // ---- 写し ----------------------------------------------------------------------------------------------------------

        // 画面（root）の写し: 操作に反応する部品と画面の仕組みを外し、絵と文字と並べ方だけを残す。この部品の直前に置く。
        private Ghost MakeGhost(RectTransform source)
        {
            GameObject copy = Instantiate(source.gameObject, _workshop, false);
            copy.name = source.name + " (Transition)";
            Strip(copy);
            var root = (RectTransform)copy.transform;
            root.SetParent(_screens, false);
            root.SetSiblingIndex(_layer.GetSiblingIndex());
            CanvasGroup group = copy.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = copy.AddComponent<CanvasGroup>();
            }
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            var ghost = new Ghost { Source = source, Root = root, Group = group };
            float screenArea = Mathf.Max(1f, _screens.rect.width * _screens.rect.height);
            CollectParts(root, source, ghost.Parts, false, screenArea);
            foreach (Part part in ghost.Parts)
            {
                AddMirrors(ghost, part.Source, part.Rect);
            }
            ghost.Parts.Sort((a, b) =>
            {
                int rowA = Mathf.RoundToInt(-a.Center.y / 30f);
                int rowB = Mathf.RoundToInt(-b.Center.y / 30f);
                return rowA != rowB ? rowA.CompareTo(rowB) : a.Center.x.CompareTo(b.Center.x);
            });
            return ghost;
        }

        private void CollectParts(RectTransform node, RectTransform sourceNode, List<Part> parts, bool tiles, float screenArea)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                if (!(node.GetChild(i) is RectTransform child) || !child.gameObject.activeSelf)
                {
                    continue;
                }
                // 写しは本物と同じ並び（写した直後）。同じ順の子が元の本物。
                RectTransform sourceChild = sourceNode != null && i < sourceNode.childCount ? sourceNode.GetChild(i) as RectTransform : null;
                if (child.GetComponent<TransitionGroup>() != null)
                {
                    CollectParts(child, sourceChild, parts, true, screenArea);
                    continue;
                }
                CanvasGroup group = child.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = child.gameObject.AddComponent<CanvasGroup>();
                }
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_screens, child);
                parts.Add(new Part
                {
                    Rect = child,
                    Group = group,
                    Home = child.anchoredPosition,
                    Scale = child.localScale,
                    BaseAlpha = group.alpha,
                    Center = bounds.center,
                    Tile = tiles,
                    Backdrop = !tiles && bounds.size.x * bounds.size.y >= screenArea * BackdropArea,
                    Source = sourceChild,
                    SourceHome = sourceChild != null ? sourceChild.anchoredPosition : Vector2.zero,
                    SourceGroup = sourceChild != null ? sourceChild.GetComponent<CanvasGroup>() : null,
                });
            }
        }

        // 札（part）の中の物を、元の本物と組にする（同じ並びの子どうし）。
        private static void AddMirrors(Ghost ghost, RectTransform source, RectTransform copy)
        {
            if (source == null || copy == null)
            {
                return;
            }
            Graphic sourceGraphic = source.GetComponent<Graphic>();
            Graphic copyGraphic = copy.GetComponent<Graphic>();
            if (sourceGraphic != null && copyGraphic != null && sourceGraphic.GetType() == copyGraphic.GetType())
            {
                // 札そのもの（part の根）は位置と濃さを Pose が決めるので、絵と演出の状態だけを合わせる。
                ghost.Mirrors.Add(new Mirror { SourceGraphic = sourceGraphic, CopyGraphic = copyGraphic });
            }
            AddDescendants(ghost, source, copy);
        }

        // 中の物を、同じ並びの子どうしで組にする（孫より下も）。
        private static void AddDescendants(Ghost ghost, RectTransform source, RectTransform copy)
        {
            int count = Mathf.Min(source.childCount, copy.childCount);
            for (int i = 0; i < count; i++)
            {
                if (!(source.GetChild(i) is RectTransform s) || !(copy.GetChild(i) is RectTransform c))
                {
                    continue;
                }
                Graphic sg = s.GetComponent<Graphic>();
                Graphic cg = c.GetComponent<Graphic>();
                bool sameGraphic = sg != null && cg != null && sg.GetType() == cg.GetType();
                ghost.Mirrors.Add(new Mirror
                {
                    Source = s,
                    Copy = c,
                    SourceGroup = s.GetComponent<CanvasGroup>(),
                    CopyGroup = c.GetComponent<CanvasGroup>(),
                    SourceGraphic = sameGraphic ? sg : null,
                    CopyGraphic = sameGraphic ? cg : null,
                });
                AddDescendants(ghost, s, c);
            }
        }

        // 札を、置き場所から offset だけずらし、scale 倍・alpha 倍にする。出てくる画面の写しは、本物の今の位置のずれ（選んで右へ出る・
        // ほかを選んで下がる）と大きさ・濃さを元にする（2026-11-08）。
        private static void Pose(Part part, Vector2 offset, Vector2 scale, float alpha)
        {
            if (part.Rect == null)
            {
                return;
            }
            part.Posed = true;
            part.LastOffset = offset;
            part.LastScale = scale;
            part.LastAlpha = alpha;
            Vector2 live = Vector2.zero;
            Vector3 baseScale = part.Scale;
            float baseAlpha = part.BaseAlpha;
            if (part.Live && part.Source != null)
            {
                live = part.Source.anchoredPosition - part.SourceHome;
                baseScale = part.Source.localScale;
                if (part.SourceGroup != null)
                {
                    baseAlpha = part.SourceGroup.alpha;
                }
            }
            part.Rect.anchoredPosition = part.Home + live + offset;
            part.Rect.localScale = new Vector3(baseScale.x * scale.x, baseScale.y * scale.y, baseScale.z);
            part.Group.alpha = baseAlpha * alpha;
        }

        // 写しから外す部品（画面の仕組み・操作に反応する部品）。残すのは絵・文字・マスク・並べ方と、この演出の印だけ。
        private static void Strip(GameObject root)
        {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                Component[] components = node.GetComponents<Component>();
                // 後から足した部品ほど、先に足した部品に頼っている（RequireComponent）ので、後ろから外す。
                for (int i = components.Length - 1; i >= 0; i--)
                {
                    if (components[i] is MonoBehaviour behaviour && !Keep(behaviour))
                    {
                        DestroyImmediate(behaviour);
                    }
                }
            }
        }

        private static bool Keep(MonoBehaviour behaviour)
        {
            if (behaviour is Selectable || behaviour is ScrollRect)
            {
                return false;
            }
            return behaviour is Graphic || behaviour is Mask || behaviour is RectMask2D || behaviour is BaseMeshEffect
                || behaviour is ILayoutElement || behaviour is ILayoutController || behaviour is TransitionGroup;
        }

        private void DestroyOutgoing()
        {
            foreach (Ghost ghost in _outgoing)
            {
                DestroyGhost(ghost);
            }
            _outgoing.Clear();
        }

        private static void DestroyGhost(Ghost ghost)
        {
            if (ghost.Root != null)
            {
                ghost.Root.gameObject.SetActive(false);
                Destroy(ghost.Root.gameObject);
            }
        }

        // ---- 本物の画面 ----------------------------------------------------------------------------------------------------

        private void CollectActive(List<RectTransform> roots)
        {
            for (int i = 0; i < _screens.childCount; i++)
            {
                if (!(_screens.GetChild(i) is RectTransform child) || !child.gameObject.activeSelf || child == _layer)
                {
                    continue;
                }
                // クリックの火花の層は画面ではない（ずっと出ている）。
                if (IsGhost(child) || Array.IndexOf(_ignore, child.gameObject) >= 0 || child.GetComponent<ClickSparks>() != null)
                {
                    continue;
                }
                roots.Add(child);
            }
        }

        private bool IsGhost(RectTransform root)
        {
            return _outgoing.Exists(g => g.Root == root) || _incoming.Exists(g => g.Root == root);
        }

        // 見た目だけ透明にする（操作はそのまま受け付ける）。
        private void Hide(RectTransform root)
        {
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            bool added = false;
            if (group == null)
            {
                group = root.gameObject.AddComponent<CanvasGroup>();
                added = true;
            }
            group.alpha = 0f;
            _hidden.Add(new Hidden { Root = root, Group = group, Added = added });
        }

        private static void Reveal(Hidden hidden)
        {
            if (hidden.Group == null)
            {
                return;
            }
            hidden.Group.alpha = 1f;
            if (hidden.Added)
            {
                Destroy(hidden.Group);
            }
        }

        // ---- 演出の部品 ----------------------------------------------------------------------------------------------------

        private RectTransform NewFull(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_layer, false);
            Stretch(rect);
            _temps.Add(go);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void AddFlash(float start, float duration, float peak)
        {
            var flash = NewFull("Flash").gameObject.AddComponent<Image>();
            flash.raycastTarget = false;
            flash.color = new Color(1f, 1f, 1f, 0f);
            AddTrack(start, duration, false, p =>
                flash.color = new Color(1f, 1f, 1f, p < 0.3f ? peak * p / 0.3f : peak * (1f - (p - 0.3f) / 0.7f)));
        }

        // 斜めの札が画面の外から外へ動く量（札の幅の半分と画面の幅の半分と、斜めの分）。
        private float Travel() => _screens.rect.width * 1.35f;

        // 画面を覆う斜めの札（画面の 1.6 倍の幅で、画面の大きさに合わせて伸びる。紺の札には行き先の名前）。
        private RectTransform CreateBand(Color color, string label)
        {
            var go = new GameObject("Band", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_layer, false);
            rect.anchorMin = new Vector2(-0.3f, 0f);
            rect.anchorMax = new Vector2(1.3f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, -2f);
            rect.offsetMax = new Vector2(0f, 2f);
            float width = _screens.rect.width;
            _temps.Add(go);
            var plate = go.AddComponent<SlantedPlate>();
            plate.Configure(0.44f, 0f, color, Color.clear);
            plate.raycastTarget = false;
            if (!string.IsNullOrEmpty(label))
            {
                var bar = new GameObject("Bar", typeof(RectTransform));
                var barRect = (RectTransform)bar.transform;
                barRect.SetParent(rect, false);
                barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0.5f, 0.5f);
                barRect.sizeDelta = new Vector2(60f, 14f);
                var barPlate = bar.AddComponent<SlantedPlate>();
                barPlate.Configure(1.2f, 0f, _accent, Color.clear);
                barPlate.raycastTarget = false;
                var text = new GameObject("Label", typeof(RectTransform));
                var textRect = (RectTransform)text.transform;
                textRect.SetParent(rect, false);
                textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
                textRect.sizeDelta = new Vector2(width, 140f);
                var t = text.AddComponent<Text>();
                t.font = GameFonts.LatinBlack;
                t.fontSize = 84;
                t.fontStyle = FontStyle.Italic;
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.color = Color.white;
                t.raycastTarget = false;
                t.text = label;
                textRect.anchoredPosition = new Vector2(40f, 0f);
                barRect.anchoredPosition = new Vector2(-t.preferredWidth * 0.5f - 30f, 0f);
            }
            return rect;
        }
    }
}
