using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 日本語の折り返しの決まり（LineBreakRules。句読点を行の頭に置かない）で文を出す文字（uGUI の Text に付ける）。改行を入れる位置は
    /// 枠の幅で変わるので、元の文（Value）を覚えておき、枠の大きさが変わったら（画面の大きさを変えたときなど）入れ直す。
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class LineBreakText : MonoBehaviour
    {
        private Text _text;
        private string _value = string.Empty;
        private float _wrappedWidth = -1f;
        private bool _dirty;

        /// <summary>出している元の文（改行を入れる前）。</summary>
        public string Value => _value;

        /// <summary>text に value を出す（LineBreakText がなければ付ける）。</summary>
        public static void Set(Text text, string value)
        {
            LineBreakText wrapper = text.GetComponent<LineBreakText>();
            if (wrapper == null)
            {
                wrapper = text.gameObject.AddComponent<LineBreakText>();
            }
            wrapper.Set(value);
        }

        /// <summary>value を出す。</summary>
        public void Set(string value)
        {
            _value = value ?? string.Empty;
            Refresh();
        }

        /// <summary>今の枠の幅で改行を入れ直す（画像を撮る道具が、画面の大きさを変えた後に呼ぶ）。</summary>
        public void Refresh()
        {
            if (_text == null)
            {
                _text = GetComponent<Text>();
            }
            _text.text = LineBreakRules.Wrap(_text, _value);
            _wrappedWidth = _text.rectTransform.rect.width;
            _dirty = false;
        }

        private void OnRectTransformDimensionsChange()
        {
            // 並べ直しの途中で文字を変えられないので、次の LateUpdate で入れ直す。
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (_dirty && !Mathf.Approximately(_wrappedWidth, ((RectTransform)transform).rect.width))
            {
                Refresh();
            }
            _dirty = false;
        }
    }
}
