using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 見出しの札（2026-11-08 のユーザーの要望）: 白地に黒の文字の斜めの札に、イメージカラーの縁と、表の札から少し角度のずれた裏の札
    /// （SlantedPlate の裏の札。いつも出して、ゆっくり揺らす）。メインメニューの右の見出しと、ステージ選択の右の見出し。
    /// Show で文字と色を変えると、前の札は左へ抜けて消え、新しい札が右から伸びて入り、裏の札が少し遅れて伸びて出てからゆっくり揺れ、縁を
    /// 光が 1 周し、文字が少し遅れて左下からすべり込み（W1）、1 文字ずつはね上がる（W2。D-90。2026-11-08。見本のページと同じ動き）。
    /// 札は 2 枚（_faces）を交互に使う。札の幅は文字に合わせ、_maxWidth を超えるなら字を小さくする。
    /// </summary>
    public sealed class HeadingPlate : MonoBehaviour
    {
        /// <summary>新しい札が入りきるまでの秒数。</summary>
        public const float InSeconds = 0.34f;
        /// <summary>前の札が抜けて消えるまでの秒数。</summary>
        public const float OutSeconds = 0.2f;

        private const float TextDelay = 0.07f;
        private const float EnterTravel = 140f;
        private const float LeaveTravel = 70f;
        // W1: 見本のページの見出し（高さ 96）で、左へ 14・下へ 3 ピクセルの所から 0.16 秒ですべり込む。
        private const float SlantSeconds = 0.16f;
        private const float SlantUnitHeight = 96f;
        private static readonly Vector2 SlantFrom = new Vector2(-14f, -3f);

        [SerializeField] private RectTransform[] _faces = new RectTransform[0];
        [SerializeField] private SlantedPlate[] _plates = new SlantedPlate[0];
        [SerializeField] private Text[] _texts = new Text[0];
        [Tooltip("文字の左右の余白（ピクセル）")]
        [SerializeField] private float _padding = 56f;
        [Tooltip("札のいちばん広い幅（ピクセル。超えるなら字を小さくする）")]
        [SerializeField] private float _maxWidth = 1000f;
        [SerializeField] private int _fontSize = 110;
        [SerializeField] private int _minFontSize = 36;

        private CanvasGroup[] _groups = new CanvasGroup[0];
        private Vector2[] _homes = new Vector2[0];
        private Vector2[] _textHomes = new Vector2[0];
        private Color[] _textColors = new Color[0];
        private LetterRise[] _rises = new LetterRise[0];
        private int _current = -1;
        private int _previous = -1;
        private float _changedAt = -10f;
        private bool _ready;

        /// <summary>今出している文字（まだなければ空）。</summary>
        public string Word => _current >= 0 && _texts[_current] != null ? _texts[_current].text : string.Empty;

        /// <summary>今出している札（テスト用。まだなければ null）。</summary>
        public SlantedPlate Plate => _current >= 0 ? _plates[_current] : null;

        /// <summary>今出している文字の色（テスト用）。</summary>
        public Color TextColor => _current >= 0 && _texts[_current] != null ? _textColors[_current] : Color.clear;

        /// <summary>今出している札の幅（テスト用）。</summary>
        public float Width => _current >= 0 ? _faces[_current].sizeDelta.x : 0f;

        /// <summary>入る・抜ける動きの途中か（テスト用）。</summary>
        public bool Moving => Time.unscaledTime - _changedAt < InSeconds;

        /// <summary>設定する（シーンのビルダー用）。faces・plates・texts は同じ数（2 つ）。</summary>
        public void Configure(RectTransform[] faces, SlantedPlate[] plates, Text[] texts, float padding, float maxWidth, int fontSize)
        {
            _faces = faces;
            _plates = plates;
            _texts = texts;
            _padding = padding;
            _maxWidth = maxWidth;
            _fontSize = fontSize;
        }

        private void Awake()
        {
            Prepare();
        }

        private void Prepare()
        {
            if (_ready)
            {
                return;
            }
            _ready = true;
            int count = _faces.Length;
            _groups = new CanvasGroup[count];
            _homes = new Vector2[count];
            _textHomes = new Vector2[count];
            _textColors = new Color[count];
            _rises = new LetterRise[count];
            for (int i = 0; i < count; i++)
            {
                if (_texts[i] != null)
                {
                    _rises[i] = _texts[i].GetComponent<LetterRise>();
                    if (_rises[i] == null)
                    {
                        _rises[i] = _texts[i].gameObject.AddComponent<LetterRise>();
                    }
                    _rises[i].Ratio = LetterRise.HeadingRatio;
                }
                _groups[i] = _faces[i].GetComponent<CanvasGroup>();
                if (_groups[i] == null)
                {
                    _groups[i] = _faces[i].gameObject.AddComponent<CanvasGroup>();
                }
                _groups[i].interactable = false;
                _groups[i].blocksRaycasts = false;
                _homes[i] = _faces[i].anchoredPosition;
                _textHomes[i] = _texts[i] != null ? _texts[i].rectTransform.anchoredPosition : Vector2.zero;
                _textColors[i] = _texts[i] != null ? _texts[i].color : Color.black;
            }
        }

        private void OnEnable()
        {
            // 画面を出し直したときは、前の札の動きを残さない。
            Prepare();
            _current = -1;
            _previous = -1;
            _changedAt = -10f;
            for (int i = 0; i < _faces.Length; i++)
            {
                _groups[i].alpha = 0f;
                _faces[i].anchoredPosition = _homes[i];
                _faces[i].localScale = Vector3.one;
                if (_texts[i] != null)
                {
                    _texts[i].text = string.Empty;
                }
                if (_plates[i] != null)
                {
                    _plates[i].ResetEffects();
                }
            }
        }

        /// <summary>word をイメージカラー accent の札で出す（同じ文字と色なら何もしない）。</summary>
        public void Show(string word, Color accent)
        {
            Prepare();
            if (_faces.Length < 2)
            {
                return;
            }
            if (_current >= 0 && Word == word && _plates[_current] != null && _plates[_current].BorderColor == accent)
            {
                return;
            }
            _previous = _current;
            _current = _current < 0 ? 0 : 1 - _current;
            if (_previous >= 0 && _plates[_previous] != null)
            {
                _plates[_previous].BackTarget = 0f;
            }
            RectTransform face = _faces[_current];
            face.SetAsLastSibling();
            Text text = _texts[_current];
            text.text = word;
            Fit(face, text);
            SlantedPlate plate = _plates[_current];
            plate.ResetEffects();
            plate.BorderColor = accent;
            plate.AccentColor = accent;
            plate.FuseColor = Color.Lerp(accent, Color.white, 0.35f);
            plate.BackTarget = 1f;
            plate.RunFuse();
            plate.SwingBack(0.08f);
            if (_rises[_current] != null)
            {
                _rises[_current].Play(TextDelay);
            }
            _changedAt = Time.unscaledTime;
            if (!Application.isPlaying)
            {
                // 再生していないとき（画面を撮る道具）は、動かさずに入りきった形で見せる。
                _changedAt = -10f;
                plate.SetBackNow(1f);
            }
            Animate();
        }

        // 字の大きさと札の幅を、文字に合わせる（_maxWidth を超えるなら字を小さくする）。
        private void Fit(RectTransform face, Text text)
        {
            float height = face.sizeDelta.y;
            SlantedPlate plate = face.GetComponent<SlantedPlate>();
            float slant = plate != null ? height * plate.Slant : 0f;
            int size = _fontSize;
            text.fontSize = size;
            while (size > _minFontSize && text.preferredWidth + _padding * 2f + slant > _maxWidth)
            {
                size -= 2;
                text.fontSize = size;
            }
            float width = Mathf.Min(_maxWidth, text.preferredWidth + _padding * 2f + slant);
            face.sizeDelta = new Vector2(width, height);
        }

        private void Update()
        {
            Animate();
        }

        private void Animate()
        {
            float t = Time.unscaledTime - _changedAt;
            if (_previous >= 0)
            {
                float p = UiEase.Clamp01(t / OutSeconds);
                _faces[_previous].anchoredPosition = _homes[_previous] + new Vector2(-LeaveTravel * UiEase.InCubic(p), 0f);
                _faces[_previous].localScale = Vector3.one;
                _groups[_previous].alpha = 1f - p;
                SetTextAlpha(_previous, 1f - p, Vector2.zero);
            }
            if (_current >= 0)
            {
                float p = UiEase.Clamp01(t / InSeconds);
                // 右から伸びて入る（札の右端を基準に、横に縮んだ所から少し行きすぎて止まる）。
                _faces[_current].anchoredPosition = _homes[_current] + new Vector2(EnterTravel * (1f - UiEase.OutBack(p, 1.2f)), 0f);
                _faces[_current].localScale = new Vector3(Mathf.Lerp(0.6f, 1f, UiEase.OutCubic(p)), 1f, 1f);
                _groups[_current].alpha = UiEase.Clamp01(t / 0.1f);
                // W1: 左下から札の傾きに沿ってすべり込み、濃くなる。
                float q = UiEase.SnapOut((t - TextDelay) / SlantSeconds);
                float unit = _faces[_current].sizeDelta.y / SlantUnitHeight;
                SetTextAlpha(_current, q, SlantFrom * unit * (1f - q));
            }
        }

        private void SetTextAlpha(int index, float alpha, Vector2 offset)
        {
            Text text = _texts[index];
            if (text == null)
            {
                return;
            }
            Color c = _textColors[index];
            c.a *= alpha;
            text.color = c;
            text.rectTransform.anchoredPosition = _textHomes[index] + offset;
        }
    }
}
