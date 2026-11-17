using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 字体の絵（文字を並べた絵）が作り直されたら、出ているすべての文字を次のフレームとその次のフレームに描き直させる
    /// （2026-11-16・17 の指摘: 見出しの文字が時たま白い四角になる）。
    /// 字体の絵は、新しい字や大きさを求められると作り直され、文字の位置が変わる。Unity は作り直した字体を使う文字だけを描き直すので、
    /// 別の字体から足りない字を借りている字体（Orbitron が M PLUS 1 から借りる）や、描く途中で作り直しが起きたときに、古い位置を指した
    /// ままの文字が残ることがある（古い位置が別の太い字の中を指すと、白い四角に見える）。どの字体でも作り直しがあれば全部の文字を
    /// 描き直すので、残っても 1〜2 フレームで直る。いつも読み込むシーン（App）に置く。
    /// </summary>
    public sealed class FontRebuildGuard : MonoBehaviour
    {
        // 作り直しを見てから描き直す残りのフレーム数（作り直しの直後のフレームに、もう一度作り直されることがあるので 2 回）。
        private const int Passes = 2;

        private int _pending;

        /// <summary>今までに描き直させた回数（テスト用）。</summary>
        public int Refreshes { get; private set; }

        private void OnEnable()
        {
            Font.textureRebuilt += OnTextureRebuilt;
        }

        private void OnDisable()
        {
            Font.textureRebuilt -= OnTextureRebuilt;
        }

        private void OnTextureRebuilt(Font font)
        {
            _pending = Passes;
        }

        private void LateUpdate()
        {
            if (_pending <= 0)
            {
                return;
            }
            _pending--;
            RefreshAll();
        }

        /// <summary>出ているすべての文字を描き直させる（文字の網と、字体の絵の差し替え）。</summary>
        public void RefreshAll()
        {
            foreach (Text text in FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (text.isActiveAndEnabled)
                {
                    text.SetAllDirty();
                }
            }
            Refreshes++;
        }
    }
}
