using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 説明の札（2026-11-08 のユーザーの要望）: 白地に黒の文字の斜めの札。縁と裏の札はイメージカラー（見出しの札と同じ色）。札の高さは文に
    /// 合わせる（上の端はそのまま、下へ伸びる。画面の大きさが変わって折り返し直したときも合わせ直す）。句読点を行の頭に置かずに折り返す
    /// （LineBreakText）。ステージ選択とメインメニューの右の見出しの下。
    /// </summary>
    [RequireComponent(typeof(SlantedPlate))]
    public sealed class DescriptionPlate : MonoBehaviour
    {
        [SerializeField] private Text _text;
        [Tooltip("文の上下の余白（ピクセル）")]
        [SerializeField] private float _padding = 22f;
        [Tooltip("札のいちばん低い高さ（ピクセル）")]
        [SerializeField] private float _minHeight = 84f;

        private SlantedPlate _plate;

        /// <summary>札。</summary>
        public SlantedPlate Plate => _plate != null ? _plate : _plate = GetComponent<SlantedPlate>();

        /// <summary>文字。</summary>
        public Text Text => _text;

        /// <summary>今出している文（折り返しの改行を入れる前）。</summary>
        public string Value => _text == null ? string.Empty
            : _text.GetComponent<LineBreakText>() is LineBreakText wrapper ? wrapper.Value : _text.text;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Text text, float padding, float minHeight)
        {
            _text = text;
            _padding = padding;
            _minHeight = minHeight;
        }

        /// <summary>文 value を、イメージカラー accent の札で出す。</summary>
        public void Show(string value, Color accent)
        {
            if (_text != null)
            {
                LineBreakText.Set(_text, value);
            }
            SetAccent(accent);
            Refit();
        }

        /// <summary>縁と裏の札の色を accent にし、裏の札を出す。</summary>
        public void SetAccent(Color accent)
        {
            SlantedPlate plate = Plate;
            accent.a = 1f;
            plate.BorderColor = accent;
            plate.AccentColor = accent;
            if (!Application.isPlaying)
            {
                // 再生していないとき（画面を撮る道具）は、裏の札を出しきった形で見せる。
                plate.SetBackNow(1f);
            }
            else if (plate.BackTarget < 1f)
            {
                plate.BackTarget = 1f;
            }
        }

        /// <summary>札の高さを文に合わせる（画面を撮る道具も、折り返しを入れ直した後に呼ぶ）。</summary>
        public void Refit()
        {
            if (_text == null)
            {
                return;
            }
            RectTransform rect = Plate.rectTransform;
            float height = Mathf.Max(_minHeight, _text.preferredHeight + _padding * 2f);
            if (!Mathf.Approximately(rect.sizeDelta.y, height))
            {
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            }
        }

        private void LateUpdate()
        {
            Refit();
        }
    }
}
