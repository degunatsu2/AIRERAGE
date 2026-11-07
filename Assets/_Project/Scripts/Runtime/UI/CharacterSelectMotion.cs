using System.Collections.Generic;
using Airerage.Runtime.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// キャラクター選択の演出（D-89。ユーザーが選んだ C1・C2・C3・C10・C12・C13・C14・C17・C18・C20 と、C4 の代わりの
    /// カーソルの光。2026-11-07。C5 周りの札の波・C11 名前のデコード・C19 光の掃きは、思っていたものと違ったので消した。2026-11-08）。画面（CharacterSelectScreen）の状態を毎フレーム見て、変わったときに演出を出す（入力や通信の処理には
    /// 手を入れない。ネットワーク対戦のほかの人の動きにも同じ演出が出る）:
    /// - 乗せたとき: C1 ロゴが札から少し飛び出す、C2 キャラクターの色（不透明でグラデーションなし。2026-11-08）が斜めに流れ込む、C3 キャラクターごとのロゴの動き
    ///   （CharacterConfigAsset.LogoMotion）、C10 四隅の枠のロゴと名前がスロットのように入れ替わる、C18 RANDOM のロゴが全員の顔を
    ///   回る（ルーレットは CharacterSelectScreen）。
    /// - 決めたとき: C13 ロゴが弧を描いて自分の枠へ飛び、着くと枠が光る、C12 枠に「READY!」の判が叩きつけられ、枠が揺れて火花が散る、
    ///   C14 キャラクターの色の輪が広がり、輪が通った札が光る、C17 全員そろうと「ALL READY」の帯が横切る。
    /// - そのほか: C20 取り消すと判がはがれ、ロゴが札へ戻る、カーソル（プレイヤーの色の枠）は
    ///   メインメニューの札と同じ導火線の光が一定の周期で縁を回り、縁がゆっくり明滅する。
    /// 画面の切り替えの写しでは動かない（写しにはこの部品を写さない）。
    /// </summary>
    [RequireComponent(typeof(CharacterSelectScreen))]
    public sealed class CharacterSelectMotion : MonoBehaviour
    {
        private const float PopUpSeconds = 0.26f;
        private const float PopDownSeconds = 0.18f;
        private const float PopRise = 16f;
        private const float PopScale = 0.18f;
        private const float FloodInSeconds = 0.24f;
        private const float FloodOutSeconds = 0.16f;
        private const float ReelSeconds = 0.2f;
        private const float ReelTravel = 34f;
        private const float FlySeconds = 0.32f;
        private const float FlyBackSeconds = 0.28f;
        private const float StampDelay = 0.24f;
        private const float StampSeconds = 0.16f;
        private const float PeelSeconds = 0.24f;
        private const float ShakeSeconds = 0.17f;
        private const float ShockSeconds = 0.56f;
        private const float ShockRadius = 1100f;
        private const float TintSeconds = 0.3f;
        private const float BandSeconds = 1.4f;
        private const float CursorFusePeriod = 1.6f;
        private const float RandomFaceSeconds = 0.09f;

        private sealed class IconFx
        {
            public RectTransform Root;
            public Vector2 Home;
            public RectTransform Logo;
            public Vector2 LogoHome;
            public Graphic LogoGraphic;
            public SlantedFill Flood;
            public SlantedFill Tint;
            public Color Theme;
            public LogoMotion Motion;
            public bool Hovered;
            public float ChangedAt = -10f;
            public float HoverStart = -10f;
            public float Pop;
            public float PopFrom;
            public float Flood01;
            public float FloodFrom;
            public float TintAt = -10f;
            public Color TintColor;
            public RawImage Face;
            public Text Question;
            public int FaceIndex;
            public float FaceAt;
        }

        private sealed class SlotFx
        {
            public int Cursor = -1;
            public int Character = -1;
            public bool Ready;
            public string Name;
            public Texture LastLogo;
            public bool LastLogoShown;
            public string LastName = string.Empty;
            public string LastTags = string.Empty;
            public float ReelAt = -10f;
            public int ReelDir = 1;
            public RawImage ReelLogo;
            public Text ReelName;
            public Text ReelTags;
            public CanvasGroup LogoGroup;
            public CanvasGroup NameGroup;
            public CanvasGroup TagsGroup;
            public Vector2 LogoHome;
            public Vector2 NameHome;
            public Vector2 TagsHome;
            public RectTransform Stamp;
            public CanvasGroup StampGroup;
            public UiBurst StampBurst;
            public float StampAt = -10f;
            public bool StampHit;
            public float PeelAt = -10f;
            public Vector2 StampHome;
            public Vector2 CornerHome;
            public float ShakeAt = -10f;
            public float LandAt = -10f;
            public bool Landed;
            public float FuseAt;
        }

        private sealed class Flyer
        {
            public RawImage Image;
            public Vector2 From;
            public Vector2 To;
            public Vector2 Control;
            public float FromSize;
            public float ToSize;
            public float StartAt;
            public float Seconds;
            public float Alpha;
        }

        private CharacterSelectScreen _screen;
        private RectTransform _fx;
        private IconFx[] _icons = new IconFx[0];
        private SlotFx[] _slots = new SlotFx[0];
        private readonly List<Flyer> _flyers = new List<Flyer>();
        private readonly List<GameObject> _temps = new List<GameObject>();
        private RectTransform _band;
        private float _bandAt = -10f;
        private bool _allReady;
        private bool _synced;
        private readonly Vector3[] _corners = new Vector3[4];

        /// <summary>「ALL READY」の帯が横切っている途中か（テスト用）。</summary>
        public bool BandShowing => Time.unscaledTime - _bandAt < BandSeconds;

        /// <summary>slot の人の枠に「READY!」の判を出しているか（テスト用）。</summary>
        public bool StampShown(int slot) => slot < _slots.Length && _slots[slot].Stamp != null && _slots[slot].Stamp.gameObject.activeSelf;

        /// <summary>今飛んでいるロゴの数（テスト用）。</summary>
        public int FlyingLogos => _flyers.Count;

        /// <summary>index のアイコンのロゴの持ち上がり（0〜1。テスト用）。</summary>
        public float PopOf(int index) => index < _icons.Length ? _icons[index].Pop : 0f;

        /// <summary>index のアイコンのキャラクターの色の流れ込み（0〜1。テスト用）。</summary>
        public float FloodOf(int index) => index < _icons.Length ? _icons[index].Flood01 : 0f;

        private void Awake()
        {
            _screen = GetComponent<CharacterSelectScreen>();
            var fxGo = new GameObject("MotionFx", typeof(RectTransform));
            fxGo.transform.SetParent(transform, false);
            _fx = (RectTransform)fxGo.transform;
            Stretch(_fx);
            _fx.SetAsLastSibling();

            _icons = new IconFx[_screen.IconCount];
            for (int i = 0; i < _icons.Length; i++)
            {
                _icons[i] = BuildIcon(i);
            }
            _slots = new SlotFx[CharacterSelectScreen.MaxPlayers];
            for (int p = 0; p < _slots.Length; p++)
            {
                _slots[p] = BuildSlot(p);
            }
            _band = BuildBand();
        }

        private void OnEnable()
        {
            _synced = false;
        }

        private void OnDisable()
        {
            ResetAll();
        }

        // ---- 組み立て --------------------------------------------------------------------------------------------------

        private IconFx BuildIcon(int index)
        {
            CharacterIcon icon = _screen.Icon(index);
            var fx = new IconFx { Root = icon.Rect, Home = _screen.IconHome(index) };
            CharacterConfigAsset character = _screen.CharacterAt(index);
            fx.Theme = character != null ? character.ThemeColor : _screen.Accent;
            fx.Motion = character != null ? character.LogoMotion : LogoMotion.None;
            fx.Logo = icon.Rect.Find("Logo") as RectTransform;
            if (fx.Logo != null)
            {
                fx.LogoHome = fx.Logo.anchoredPosition;
                fx.LogoGraphic = fx.Logo.GetComponent<RawImage>();
                fx.Question = fx.Logo.GetComponentInChildren<Text>(true);
            }
            SlantedPlate plate = icon.GetComponent<SlantedPlate>();
            float slant = plate != null ? plate.Slant : 0.1f;
            // 札の中（札の色の上、ロゴと名前の下）に、色の流れ込みと衝撃波の光を重ねる。
            fx.Tint = AddFill(icon.Rect, "Tint", slant, false);
            fx.Flood = AddFill(icon.Rect, "Flood", slant, false);
            if (character == null && fx.Logo != null)
            {
                // RANDOM: 乗せている間、全員の顔を回す（C18）。
                var faceGo = new GameObject("Face", typeof(RectTransform));
                faceGo.transform.SetParent(fx.Logo, false);
                var faceRect = (RectTransform)faceGo.transform;
                Stretch(faceRect);
                fx.Face = faceGo.AddComponent<RawImage>();
                fx.Face.raycastTarget = false;
                fx.Face.enabled = false;
            }
            return fx;
        }

        private static SlantedFill AddFill(RectTransform parent, string name, float slant, bool soft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(0);
            var rect = (RectTransform)go.transform;
            Stretch(rect);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var fill = go.AddComponent<SlantedFill>();
            fill.Configure(slant, soft);
            fill.Set(0f, 0f, Color.clear, Color.clear);
            return fill;
        }

        private SlotFx BuildSlot(int slot)
        {
            var fx = new SlotFx { CornerHome = _screen.CornerRect(slot).anchoredPosition, FuseAt = slot * 0.2f };
            RawImage logo = _screen.CornerLogo(slot);
            Text name = _screen.CornerName(slot);
            Text tags = _screen.CornerTags(slot);
            fx.LogoGroup = GroupOf(logo.gameObject);
            fx.NameGroup = GroupOf(name.gameObject);
            fx.TagsGroup = GroupOf(tags.gameObject);
            fx.LogoHome = logo.rectTransform.anchoredPosition;
            fx.NameHome = name.rectTransform.anchoredPosition;
            fx.TagsHome = tags.rectTransform.anchoredPosition;
            // 入れ替わるときに出ていく前の絵と名前（C10）。
            fx.ReelLogo = Instantiate(logo, logo.transform.parent);
            fx.ReelLogo.name = "ReelLogo";
            fx.ReelName = Instantiate(name, name.transform.parent);
            fx.ReelName.name = "ReelName";
            fx.ReelTags = Instantiate(tags, tags.transform.parent);
            fx.ReelTags.name = "ReelTags";
            fx.ReelLogo.gameObject.SetActive(false);
            fx.ReelName.gameObject.SetActive(false);
            fx.ReelTags.gameObject.SetActive(false);
            BuildStamp(fx, slot);
            return fx;
        }

        // 「READY!」の判（C12）: 枠の右寄りに、プレイヤーの色の縁の斜めの札と文字。
        private void BuildStamp(SlotFx fx, int slot)
        {
            RectTransform corner = _screen.CornerRect(slot);
            var go = new GameObject("Stamp", typeof(RectTransform));
            go.transform.SetParent(corner, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(200f, 66f);
            fx.StampHome = new Vector2(-122f, -8f);
            rect.anchoredPosition = fx.StampHome;
            var plate = go.AddComponent<SlantedPlate>();
            plate.Configure(0.18f, 4f, new Color(0.03f, 0.05f, 0.09f, 0.85f), _screen.PlayerColor(slot));
            plate.raycastTarget = false;
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(rect, false);
            Stretch((RectTransform)textGo.transform);
            var text = textGo.AddComponent<Text>();
            text.text = "READY!";
            text.fontSize = 36;
            text.fontStyle = FontStyle.BoldAndItalic;
            GameFonts.Use(text);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            outline.effectDistance = new Vector2(2f, -2f);
            var burstGo = new GameObject("Burst", typeof(RectTransform));
            burstGo.transform.SetParent(rect, false);
            Stretch((RectTransform)burstGo.transform);
            fx.StampBurst = burstGo.AddComponent<UiBurst>();
            fx.StampGroup = go.AddComponent<CanvasGroup>();
            fx.StampGroup.interactable = false;
            fx.StampGroup.blocksRaycasts = false;
            fx.Stamp = rect;
            go.SetActive(false);
        }

        // 「ALL READY」の帯（C17）: 画面の下寄りを横切る、白い斜めの帯。
        private RectTransform BuildBand()
        {
            var go = new GameObject("AllReadyBand", typeof(RectTransform));
            go.transform.SetParent(_fx, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(2300f, 92f);
            rect.anchoredPosition = new Vector2(4000f, 150f);
            var plate = go.AddComponent<SlantedPlate>();
            plate.Configure(0.4f, 4f, new Color(0.95f, 0.98f, 1f, 0.96f), MenuAccents.Default);
            plate.raycastTarget = false;
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(rect, false);
            Stretch((RectTransform)textGo.transform);
            var text = textGo.AddComponent<Text>();
            text.text = "ALL READY";
            text.fontSize = 54;
            text.fontStyle = FontStyle.BoldAndItalic;
            GameFonts.Use(text);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.04f, 0.09f, 0.15f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            go.SetActive(false);
            return rect;
        }

        // ---- 毎フレーム -------------------------------------------------------------------------------------------------

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (!_synced)
            {
                Sync(now);
                return;
            }
            DetectSlots(now);
            DetectAllReady(now);
            AnimateIcons(now);
            AnimateSlots(now);
            AnimateFlyers(now);
            AnimateBand(now);
            AnimateCursors(now);
        }

        // 画面を出した最初のフレーム: 今の状態を覚えるだけ（演出は出さない）。RANDOM の色と「ALL READY」の帯の縁は、画面のイメージカラー
        // （入ってきた項目の色。2026-11-08）。
        private void Sync(float now)
        {
            for (int i = 0; i < _icons.Length; i++)
            {
                if (_screen.CharacterAt(i) == null)
                {
                    _icons[i].Theme = _screen.Accent;
                }
            }
            SlantedPlate bandPlate = _band.GetComponent<SlantedPlate>();
            bandPlate.BorderColor = _screen.Accent;
            bandPlate.AccentColor = _screen.Accent;
            for (int p = 0; p < _slots.Length; p++)
            {
                SlotFx fx = _slots[p];
                fx.Cursor = _screen.CursorShown(p) ? _screen.CursorIndex(p) : -1;
                fx.Character = _screen.CornerShown(p) && _screen.IsJoined(p) ? _screen.CharacterOf(p) : -1;
                fx.Ready = _screen.CornerShown(p) && _screen.IsJoined(p) && _screen.IsReady(p);
                fx.Name = _screen.CornerShown(p) ? _screen.ShownName(p) : string.Empty;
                fx.FuseAt = now + p * 0.2f;
                Remember(p);
                if (fx.Ready)
                {
                    fx.Stamp.gameObject.SetActive(true);
                    fx.StampAt = now - 1f;
                    fx.StampHit = true;
                }
            }
            for (int i = 0; i < _icons.Length; i++)
            {
                IconFx icon = _icons[i];
                icon.Hovered = Hovered(i);
                icon.Pop = icon.Hovered ? 1f : 0f;
                icon.PopFrom = icon.Pop;
                icon.Flood01 = icon.Pop;
                icon.FloodFrom = icon.Flood01;
                icon.ChangedAt = now - 1f;
                icon.HoverStart = now;
            }
            _allReady = _screen.AllReady;
            _synced = true;
        }

        private bool Hovered(int index)
        {
            for (int p = 0; p < _slots.Length; p++)
            {
                if (_screen.CursorShown(p) && _screen.CursorIndex(p) == index)
                {
                    return true;
                }
            }
            return false;
        }

        // 人ごとに、カーソル・枠のキャラクター・READY が変わったかを調べて演出を出す。
        private void DetectSlots(float now)
        {
            for (int p = 0; p < _slots.Length; p++)
            {
                SlotFx fx = _slots[p];
                bool shown = _screen.CornerShown(p) && _screen.IsJoined(p);
                int cursor = _screen.CursorShown(p) ? _screen.CursorIndex(p) : -1;
                fx.Cursor = cursor;
                int character = shown ? _screen.CharacterOf(p) : -1;
                string name = shown ? _screen.ShownName(p) : string.Empty;
                if (shown && name != fx.Name && !string.IsNullOrEmpty(fx.Name) && !string.IsNullOrEmpty(name))
                {
                    // C10: 枠のロゴと名前が入れ替わる（動いた向きから入る）。
                    StartReel(fx, character >= fx.Character ? 1 : -1, now);
                }
                fx.Name = name;
                fx.Character = character;
                bool ready = shown && _screen.IsReady(p);
                if (ready && !fx.Ready)
                {
                    OnLock(p, now);
                }
                else if (!ready && fx.Ready && shown)
                {
                    OnUnlock(p, now);
                }
                else if (!ready && fx.Ready)
                {
                    fx.Stamp.gameObject.SetActive(false);
                }
                fx.Ready = ready;
                Remember(p);
            }
        }

        // 次のフレームの入れ替わりに使う、今の枠の絵と名前。
        private void Remember(int slot)
        {
            SlotFx fx = _slots[slot];
            RawImage logo = _screen.CornerLogo(slot);
            fx.LastLogo = logo.texture;
            fx.LastLogoShown = logo.enabled;
            fx.LastName = fx.Name ?? string.Empty;
            fx.LastTags = _screen.CornerTags(slot).text;
        }

        private void StartReel(SlotFx fx, int dir, float now)
        {
            fx.ReelAt = now;
            fx.ReelDir = dir;
            fx.ReelLogo.texture = fx.LastLogo;
            fx.ReelLogo.enabled = fx.LastLogoShown;
            fx.ReelLogo.gameObject.SetActive(fx.LastLogoShown && fx.LastLogo != null);
            fx.ReelName.text = fx.LastName;
            fx.ReelName.gameObject.SetActive(true);
            fx.ReelTags.text = fx.LastTags;
            fx.ReelTags.gameObject.SetActive(true);
        }

        // 決めたとき: C13 ロゴが自分の枠へ飛ぶ、C14 色の輪、C12 判（ロゴが着く少し前から）。
        private void OnLock(int slot, float now)
        {
            SlotFx fx = _slots[slot];
            int index = _screen.CharacterOf(slot);
            if (index < 0 || index >= _icons.Length)
            {
                return;
            }
            IconFx icon = _icons[index];
            CharacterConfigAsset character = _screen.CharacterAt(index);
            RawImage cornerLogo = _screen.CornerLogo(slot);
            if (character != null && character.Logo != null && icon.Logo != null)
            {
                Rect from = RectIn(icon.Logo);
                Rect to = RectIn(cornerLogo.rectTransform);
                Fly(character.Logo, from, to, FlySeconds, now);
                fx.LandAt = now + FlySeconds;
                fx.Landed = false;
            }
            Shock(index, now);
            fx.PeelAt = -10f;
            fx.StampAt = now + StampDelay;
            fx.StampHit = false;
            fx.Stamp.gameObject.SetActive(true);
            fx.Stamp.localScale = Vector3.one * 2.3f;
            fx.StampGroup.alpha = 0f;
            fx.Stamp.GetComponent<SlantedPlate>().BorderColor = _screen.PlayerColor(slot);
        }

        // 取り消したとき（C20）: 判がはがれ、ロゴが枠から札へ戻る。
        private void OnUnlock(int slot, float now)
        {
            SlotFx fx = _slots[slot];
            if (fx.Stamp.gameObject.activeSelf)
            {
                fx.PeelAt = now;
            }
            int index = _screen.CharacterOf(slot);
            CharacterConfigAsset character = index >= 0 && index < _icons.Length ? _screen.CharacterAt(index) : null;
            if (character != null && character.Logo != null && _icons[index].Logo != null)
            {
                Fly(character.Logo, RectIn(_screen.CornerLogo(slot).rectTransform), RectIn(_icons[index].Logo), FlyBackSeconds, now);
            }
        }

        private void DetectAllReady(float now)
        {
            bool ready = _screen.AllReady;
            if (ready && !_allReady)
            {
                // C17: 全員そろったら帯が横切り、全員の枠が光る。
                _bandAt = now;
                _band.gameObject.SetActive(true);
                _band.SetAsLastSibling();
                for (int p = 0; p < _slots.Length; p++)
                {
                    if (_screen.CornerShown(p) && _screen.IsJoined(p))
                    {
                        _screen.CornerPlate(p).Flash();
                    }
                }
            }
            _allReady = ready;
        }

        // ---- 動き ---------------------------------------------------------------------------------------------------------

        private void AnimateIcons(float now)
        {
            for (int i = 0; i < _icons.Length; i++)
            {
                IconFx icon = _icons[i];
                bool hovered = Hovered(i);
                if (hovered != icon.Hovered)
                {
                    icon.Hovered = hovered;
                    icon.ChangedAt = now;
                    icon.PopFrom = icon.Pop;
                    icon.FloodFrom = icon.Flood01;
                    if (hovered)
                    {
                        icon.HoverStart = now;
                    }
                }
                float t = now - icon.ChangedAt;
                // C1・C2: 乗せると、ロゴが少し行きすぎて持ち上がり、キャラクターの色が流れ込む。
                icon.Pop = hovered
                    ? Mathf.LerpUnclamped(icon.PopFrom, 1f, UiEase.OutBack(Mathf.Clamp01(t / PopUpSeconds), 1.7f))
                    : Mathf.Lerp(icon.PopFrom, 0f, UiEase.OutCubic(t / PopDownSeconds));
                icon.Flood01 = hovered
                    ? Mathf.Lerp(icon.FloodFrom, 1f, UiEase.OutCubic(t / FloodInSeconds))
                    : Mathf.Lerp(icon.FloodFrom, 0f, UiEase.OutCubic(t / FloodOutSeconds));
                // 流れ込む色はグラデーションなしの不透明なキャラクターの色（ユーザーの要望、2026-11-08）。
                Color solid = icon.Theme;
                solid.a = 1f;
                icon.Flood.Set(0f, icon.Flood01, solid, solid);

                // C14: 色の輪が通ったときに光る。
                float tint = now - icon.TintAt;
                if (tint >= 0f && tint < TintSeconds)
                {
                    float a = tint < 0.08f ? tint / 0.08f : 1f - (tint - 0.08f) / (TintSeconds - 0.08f);
                    Color c = icon.TintColor;
                    c.a = 0.55f * a;
                    icon.Tint.Set(0f, 1f, c, c);
                }
                else
                {
                    icon.Tint.Set(0f, 0f, Color.clear, Color.clear);
                }

                if (icon.Root.anchoredPosition != icon.Home)
                {
                    icon.Root.anchoredPosition = icon.Home;
                }
                AnimateLogo(icon, hovered, now);
            }
        }

        // ロゴの持ち上がり（C1）とキャラクターごとの動き（C3）。RANDOM は顔を回す（C18）。
        private void AnimateLogo(IconFx icon, bool hovered, float now)
        {
            if (icon.Logo == null)
            {
                return;
            }
            float since = now - icon.HoverStart;
            float weight = hovered ? Mathf.Clamp01(since / 0.1f) : 1f - Mathf.Clamp01((now - icon.ChangedAt) / 0.12f);
            Vector2 offset = Vector2.zero;
            Vector2 scale = Vector2.one;
            float rotation = 0f;
            float alpha = 1f;
            if (weight > 0f)
            {
                ApplyMotion(icon.Motion, since, ref offset, ref scale, ref rotation, ref alpha);
                if (icon.Face != null)
                {
                    // RANDOM: 揺れる。
                    rotation = 10f * Mathf.Sin(since * Mathf.PI * 2f / 0.9f);
                }
                offset *= weight;
                scale = Vector2.LerpUnclamped(Vector2.one, scale, weight);
                rotation *= weight;
                alpha = Mathf.Lerp(1f, alpha, weight);
            }
            float pop = icon.Pop;
            float grow = 1f + PopScale * pop;
            Vector2 size = icon.Logo.rect.size;
            // ロゴは上の辺が基準なので、大きくしても真ん中が動かないように下げる分を戻す。
            var position = icon.LogoHome + new Vector2(0f, PopRise * pop + (grow * scale.y - 1f) * size.y * 0.5f) + offset;
            icon.Logo.anchoredPosition = position;
            icon.Logo.localScale = new Vector3(grow * scale.x, grow * scale.y, 1f);
            icon.Logo.localEulerAngles = new Vector3(0f, 0f, rotation);
            if (icon.LogoGraphic != null)
            {
                Color c = icon.LogoGraphic.color;
                if (!Mathf.Approximately(c.a, alpha))
                {
                    c.a = alpha;
                    icon.LogoGraphic.color = c;
                }
            }
            if (icon.Face != null)
            {
                bool cycling = hovered;
                if (cycling && (now >= icon.FaceAt || !icon.Face.enabled))
                {
                    int count = Mathf.Max(1, _icons.Length - 1);
                    icon.FaceIndex = (icon.FaceIndex + 1) % count;
                    CharacterConfigAsset face = _screen.CharacterAt(icon.FaceIndex);
                    icon.Face.texture = face != null ? face.Logo : null;
                    icon.FaceAt = now + RandomFaceSeconds;
                }
                icon.Face.enabled = cycling && icon.Face.texture != null;
                if (icon.Question != null)
                {
                    icon.Question.enabled = !icon.Face.enabled;
                }
            }
        }

        // キャラクターごとのロゴの動き（C3。t は乗せてからの秒数）。
        private static void ApplyMotion(LogoMotion motion, float t, ref Vector2 offset, ref Vector2 scale, ref float rotation, ref float alpha)
        {
            const float Tau = Mathf.PI * 2f;
            switch (motion)
            {
                case LogoMotion.Hop:
                {
                    float f = t % 0.62f / 0.62f;
                    float lift = Mathf.Sin(f * Mathf.PI);
                    offset.y = 10f * lift;
                    float squash = f < 0.12f ? f / 0.12f : f > 0.88f ? (1f - f) / 0.12f : 0f;
                    scale = new Vector2(1f + 0.08f * squash - 0.04f * lift, 1f - 0.1f * squash + 0.05f * lift);
                    break;
                }
                case LogoMotion.Flip:
                    scale.x = 0.08f + 0.92f * Mathf.Abs(Mathf.Cos(t * Mathf.PI / 0.9f));
                    break;
                case LogoMotion.Sway:
                    offset.x = 6f * Mathf.Sin(t * Tau);
                    rotation = -8f * Mathf.Sin(t * Tau + 0.6f);
                    break;
                case LogoMotion.Orbit:
                    rotation = 14f * Mathf.Sin(t * Tau / 2.2f);
                    offset.y = 6f * (0.5f - 0.5f * Mathf.Cos(t * Tau / 2.2f));
                    break;
                case LogoMotion.Puff:
                {
                    float s = Mathf.Sin(t * Tau);
                    scale = new Vector2(1f + 0.08f * s, 1f - 0.08f * s);
                    break;
                }
                case LogoMotion.Zap:
                {
                    int step = Mathf.FloorToInt(t * 24f);
                    float jx = ((step * 37) % 7 - 3) * 0.8f;
                    float jy = ((step * 53) % 5 - 2) * 0.9f;
                    offset = new Vector2(jx, jy);
                    alpha = Mathf.FloorToInt(t * 12f) % 3 == 1 ? 0.55f : 1f;
                    break;
                }
                case LogoMotion.Peek:
                {
                    float f = t % 1.3f / 1.3f;
                    float x = f < 0.2f ? -8f * UiEase.OutCubic(f / 0.2f)
                        : f < 0.45f ? -8f
                        : f < 0.6f ? Mathf.Lerp(-8f, 7f, UiEase.InOutCubic((f - 0.45f) / 0.15f))
                        : f < 0.85f ? 7f
                        : Mathf.Lerp(7f, 0f, UiEase.OutCubic((f - 0.85f) / 0.15f));
                    offset.x = x;
                    rotation = -x;
                    break;
                }
                case LogoMotion.Recoil:
                {
                    float f = t % 1f;
                    if (f >= 0.55f && f < 0.6f)
                    {
                        offset.x = -9f * (f - 0.55f) / 0.05f;
                        rotation = -4f * (f - 0.55f) / 0.05f;
                    }
                    else if (f >= 0.6f && f < 0.85f)
                    {
                        float back = UiEase.OutCubic((f - 0.6f) / 0.25f);
                        offset.x = -9f * (1f - back);
                        rotation = -4f * (1f - back);
                    }
                    break;
                }
            }
        }

        private void AnimateSlots(float now)
        {
            for (int p = 0; p < _slots.Length; p++)
            {
                SlotFx fx = _slots[p];
                // C10: 入れ替わり。新しい絵と名前は動いた向きから入り、前の絵と名前は反対へ抜けて消える。
                float reel = (now - fx.ReelAt) / ReelSeconds;
                if (fx.ReelAt > -5f && reel >= 0f)
                {
                    float u = Mathf.Clamp01(reel);
                    float inOffset = -fx.ReelDir * ReelTravel * (1f - UiEase.OutBack(u, 1.4f));
                    float inAlpha = Mathf.Clamp01(u * 2f);
                    SetReel(_screen.CornerLogo(p).rectTransform, fx.LogoGroup, fx.LogoHome, inOffset, inAlpha);
                    SetReel(_screen.CornerName(p).rectTransform, fx.NameGroup, fx.NameHome, inOffset, inAlpha);
                    SetReel(_screen.CornerTags(p).rectTransform, fx.TagsGroup, fx.TagsHome, inOffset, inAlpha);
                    float v = Mathf.Clamp01(reel / 0.85f);
                    float outOffset = fx.ReelDir * ReelTravel * UiEase.InCubic(v);
                    MoveCopy(fx.ReelLogo, fx.LogoHome, outOffset, 1f - v);
                    MoveCopy(fx.ReelName, fx.NameHome, outOffset, 1f - v);
                    MoveCopy(fx.ReelTags, fx.TagsHome, outOffset, 1f - v);
                    if (reel >= 1f)
                    {
                        fx.ReelLogo.gameObject.SetActive(false);
                        fx.ReelName.gameObject.SetActive(false);
                        fx.ReelTags.gameObject.SetActive(false);
                        fx.ReelAt = -10f;
                    }
                }

                // 枠が着地で光り、ロゴが少しはねる（C13）。
                float land = now - fx.LandAt;
                if (land >= 0f && land < 0.3f)
                {
                    if (!fx.Landed)
                    {
                        fx.Landed = true;
                        _screen.CornerPlate(p).Flash();
                    }
                    float s = 1f + 0.15f * (1f - UiEase.OutCubic(land / 0.3f));
                    _screen.CornerLogo(p).rectTransform.localScale = new Vector3(s, s, 1f);
                }
                else if (fx.LandAt > 0f)
                {
                    _screen.CornerLogo(p).rectTransform.localScale = Vector3.one;
                    fx.LandAt = -10f;
                }

                AnimateStamp(p, fx, now);

                // 判が叩きつけられた瞬間に、枠が揺れる。
                float shake = (now - fx.ShakeAt) / ShakeSeconds;
                RectTransform corner = _screen.CornerRect(p);
                if (shake >= 0f && shake < 1f)
                {
                    float amp = 5f * (1f - shake);
                    corner.anchoredPosition = fx.CornerHome + new Vector2(Mathf.Sin(shake * 40f) * amp, Mathf.Cos(shake * 33f) * amp * 0.5f);
                }
                else if (corner.anchoredPosition != fx.CornerHome)
                {
                    corner.anchoredPosition = fx.CornerHome;
                }
            }
        }

        private static void SetReel(RectTransform rect, CanvasGroup group, Vector2 home, float offset, float alpha)
        {
            rect.anchoredPosition = home + new Vector2(0f, offset);
            group.alpha = alpha;
        }

        private static void MoveCopy(Graphic copy, Vector2 home, float offset, float alpha)
        {
            if (!copy.gameObject.activeSelf)
            {
                return;
            }
            copy.rectTransform.anchoredPosition = home + new Vector2(0f, offset);
            Color c = copy.color;
            c.a = alpha;
            copy.color = c;
        }

        // C12: 判が大きい所から叩きつけられ、少しめり込んでから止まる。C20: はがれて飛ぶ。
        private void AnimateStamp(int slot, SlotFx fx, float now)
        {
            if (!fx.Stamp.gameObject.activeSelf)
            {
                return;
            }
            float peel = (now - fx.PeelAt) / PeelSeconds;
            if (fx.PeelAt > 0f && peel >= 0f)
            {
                if (peel >= 1f)
                {
                    fx.Stamp.gameObject.SetActive(false);
                    fx.PeelAt = -10f;
                    return;
                }
                float e = UiEase.OutCubic(peel);
                fx.Stamp.anchoredPosition = fx.StampHome + new Vector2(40f, 30f) * e;
                fx.Stamp.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(-12f, 16f, e));
                fx.StampGroup.alpha = 1f - peel;
                return;
            }
            float t = now - fx.StampAt;
            fx.Stamp.anchoredPosition = fx.StampHome;
            fx.Stamp.localEulerAngles = new Vector3(0f, 0f, t < 0f ? -24f : Mathf.Lerp(-24f, -12f, Mathf.Clamp01(t / StampSeconds)));
            if (t < 0f)
            {
                fx.StampGroup.alpha = 0f;
                fx.Stamp.localScale = Vector3.one * 2.3f;
                return;
            }
            float u = t / StampSeconds;
            float scale = u < 1f ? Mathf.Lerp(2.3f, 0.92f, UiEase.InCubic(u)) : Mathf.Lerp(0.92f, 1f, UiEase.OutCubic((t - StampSeconds) / 0.08f));
            fx.Stamp.localScale = new Vector3(scale, scale, 1f);
            fx.StampGroup.alpha = Mathf.Clamp01(u * 1.5f);
            if (!fx.StampHit && u >= 0.8f)
            {
                fx.StampHit = true;
                fx.ShakeAt = now;
                fx.StampBurst.Play();
            }
        }

        private void AnimateFlyers(float now)
        {
            for (int i = _flyers.Count - 1; i >= 0; i--)
            {
                Flyer flyer = _flyers[i];
                float u = (now - flyer.StartAt) / flyer.Seconds;
                if (u >= 1f)
                {
                    Destroy(flyer.Image.gameObject);
                    _flyers.RemoveAt(i);
                    continue;
                }
                RectTransform rect = flyer.Image.rectTransform;
                if (u < 0f)
                {
                    flyer.Image.enabled = false;
                    continue;
                }
                flyer.Image.enabled = true;
                float e = u < 0.5f ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) * 0.5f;
                Vector2 a = Vector2.Lerp(flyer.From, flyer.Control, e);
                Vector2 b = Vector2.Lerp(flyer.Control, flyer.To, e);
                rect.anchoredPosition = Vector2.Lerp(a, b, e);
                float size = Mathf.Lerp(flyer.FromSize, flyer.ToSize, e);
                rect.sizeDelta = new Vector2(size, size);
                rect.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(u * Mathf.PI) * -18f);
                flyer.Image.color = new Color(1f, 1f, 1f, flyer.Alpha);
            }
            for (int i = _temps.Count - 1; i >= 0; i--)
            {
                if (_temps[i] == null)
                {
                    _temps.RemoveAt(i);
                }
            }
        }

        private void AnimateBand(float now)
        {
            if (!_band.gameObject.activeSelf)
            {
                return;
            }
            float u = (now - _bandAt) / BandSeconds;
            if (u >= 1f)
            {
                _band.gameObject.SetActive(false);
                return;
            }
            float width = ((RectTransform)transform).rect.width;
            float x = u < 0.3f ? Mathf.Lerp(width + 1200f, 0f, UiEase.OutCubic(u / 0.3f))
                : u < 0.75f ? Mathf.Lerp(0f, -70f, (u - 0.3f) / 0.45f)
                : Mathf.Lerp(-70f, -(width + 2400f), UiEase.InCubic((u - 0.75f) / 0.25f));
            _band.anchoredPosition = new Vector2(x, 150f);
        }

        // C4 の代わり: カーソルの枠に、メインメニューの札と同じ導火線の光が一定の周期で回り、縁がゆっくり明滅する。
        private void AnimateCursors(float now)
        {
            for (int p = 0; p < _slots.Length; p++)
            {
                if (!_screen.CursorShown(p))
                {
                    continue;
                }
                SlantedPlate frame = _screen.CursorRect(p).GetComponent<SlantedPlate>();
                if (frame == null)
                {
                    continue;
                }
                Color color = _screen.PlayerColor(p);
                frame.Glow = true;
                frame.AccentColor = color;
                frame.FuseColor = Color.Lerp(color, Color.white, 0.45f);
                if (now >= _slots[p].FuseAt)
                {
                    frame.RunFuse();
                    _slots[p].FuseAt = now + CursorFusePeriod;
                }
            }
        }

        // ---- 部品 ---------------------------------------------------------------------------------------------------------

        // 絵を from から to へ弧を描いて飛ばす（残像 2 枚つき）。
        private void Fly(Texture texture, Rect from, Rect to, float seconds, float now)
        {
            Vector2 control = new Vector2((from.center.x + to.center.x) * 0.5f, Mathf.Max(from.center.y, to.center.y) + 160f);
            for (int k = 0; k < 3; k++)
            {
                var go = new GameObject(k == 0 ? "FlyingLogo" : "FlyingLogoTrail", typeof(RectTransform));
                go.transform.SetParent(_fx, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                var image = go.AddComponent<RawImage>();
                image.texture = texture;
                image.raycastTarget = false;
                image.enabled = false;
                if (k > 0)
                {
                    go.transform.SetSiblingIndex(0);
                }
                _flyers.Add(new Flyer
                {
                    Image = image,
                    From = from.center,
                    To = to.center,
                    Control = control,
                    FromSize = Mathf.Max(from.width, from.height),
                    ToSize = Mathf.Max(to.width, to.height),
                    StartAt = now + k * 0.04f,
                    Seconds = seconds,
                    Alpha = k == 0 ? 1f : 0.35f / k,
                });
            }
        }

        // C14: index のアイコンからキャラクターの色の輪を広げ、輪が通った札を光らせる。
        private void Shock(int index, float now)
        {
            IconFx icon = _icons[index];
            Rect center = RectIn(icon.Root);
            var go = new GameObject("Shock", typeof(RectTransform));
            go.transform.SetParent(_fx, false);
            go.transform.SetSiblingIndex(0);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center.center;
            rect.sizeDelta = Vector2.zero;
            var ring = go.AddComponent<RingPulse>();
            ring.Play(icon.Theme, ShockRadius, ShockSeconds);
            _temps.Add(go);
            Destroy(go, ShockSeconds + 0.1f);
            for (int j = 0; j < _icons.Length; j++)
            {
                float distance = (_icons[j].Home - icon.Home).magnitude;
                _icons[j].TintAt = now + ring.TimeToReach(distance);
                _icons[j].TintColor = icon.Theme;
            }
        }

        // rect の、効果の層（_fx。真ん中が原点）の中での位置と大きさ。
        private Rect RectIn(RectTransform rect)
        {
            rect.GetWorldCorners(_corners);
            Vector2 a = _fx.InverseTransformPoint(_corners[0]);
            Vector2 b = _fx.InverseTransformPoint(_corners[2]);
            Vector2 center = _fx.rect.center;
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - center.x, Mathf.Min(a.y, b.y) - center.y,
                Mathf.Max(a.x, b.x) - center.x, Mathf.Max(a.y, b.y) - center.y);
        }

        private static CanvasGroup GroupOf(GameObject go)
        {
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = go.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            return group;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // 画面を閉じたとき: 動かした物を元に戻し、飛んでいる物を消す。
        private void ResetAll()
        {
            foreach (IconFx icon in _icons)
            {
                if (icon.Root != null)
                {
                    icon.Root.anchoredPosition = icon.Home;
                }
                if (icon.Logo != null)
                {
                    icon.Logo.anchoredPosition = icon.LogoHome;
                    icon.Logo.localScale = Vector3.one;
                    icon.Logo.localEulerAngles = Vector3.zero;
                }
                if (icon.LogoGraphic != null)
                {
                    Color c = icon.LogoGraphic.color;
                    c.a = 1f;
                    icon.LogoGraphic.color = c;
                }
                icon.Pop = 0f;
                icon.Flood01 = 0f;
                icon.Flood?.Set(0f, 0f, Color.clear, Color.clear);
                icon.Tint?.Set(0f, 0f, Color.clear, Color.clear);
                icon.TintAt = -10f;
                if (icon.Face != null)
                {
                    icon.Face.enabled = false;
                }
                if (icon.Question != null)
                {
                    icon.Question.enabled = true;
                }
            }
            for (int p = 0; p < _slots.Length; p++)
            {
                SlotFx fx = _slots[p];
                if (fx.Stamp == null)
                {
                    continue;
                }
                fx.Stamp.gameObject.SetActive(false);
                fx.ReelLogo.gameObject.SetActive(false);
                fx.ReelName.gameObject.SetActive(false);
                fx.ReelTags.gameObject.SetActive(false);
                fx.LogoGroup.alpha = 1f;
                fx.NameGroup.alpha = 1f;
                fx.TagsGroup.alpha = 1f;
                _screen.CornerLogo(p).rectTransform.anchoredPosition = fx.LogoHome;
                _screen.CornerLogo(p).rectTransform.localScale = Vector3.one;
                _screen.CornerName(p).rectTransform.anchoredPosition = fx.NameHome;
                _screen.CornerTags(p).rectTransform.anchoredPosition = fx.TagsHome;
                _screen.CornerRect(p).anchoredPosition = fx.CornerHome;
                fx.ReelAt = -10f;
                fx.PeelAt = -10f;
                fx.LandAt = -10f;
                fx.ShakeAt = -10f;
            }
            foreach (Flyer flyer in _flyers)
            {
                if (flyer.Image != null)
                {
                    Destroy(flyer.Image.gameObject);
                }
            }
            _flyers.Clear();
            foreach (GameObject temp in _temps)
            {
                if (temp != null)
                {
                    Destroy(temp);
                }
            }
            _temps.Clear();
            if (_band != null)
            {
                _band.gameObject.SetActive(false);
            }
            _bandAt = -10f;
        }
    }
}
