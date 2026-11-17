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
    /// 字体の絵の作り直しが毎フレーム続く（描き直した文字がまた作り直しを呼ぶ）ときは、描き直しが重さを増やさないよう、30 フレーム続いたら
    /// 2 秒休む（2026-11-17 の指摘: 試合中にウインドウを戻すと、たまに止まる。考えられる原因の 1 つを防ぐ）。
    /// </summary>
    public sealed class FontRebuildGuard : MonoBehaviour
    {
        // 作り直しを見てから描き直す残りのフレーム数（作り直しの直後のフレームに、もう一度作り直されることがあるので 2 回）。
        private const int Passes = 2;

        /// <summary>続けて描き直してよいフレーム数（これを超えたら休む）。</summary>
        public const int MaxConsecutiveFrames = 30;

        /// <summary>休む秒数。</summary>
        public const float BackoffSeconds = 2f;

        private int _pending;
        private int _consecutive;
        private int _lastRefreshFrame = -10;
        private float _backoffUntil = float.NegativeInfinity;
        private bool _warned;

        /// <summary>描き直しが続きすぎて休んでいるか（テスト用）。</summary>
        public bool BackingOff => Time.unscaledTime < _backoffUntil;

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
            if (BackingOff)
            {
                _pending = 0;
                return;
            }
            _pending--;
            _consecutive = Time.frameCount == _lastRefreshFrame + 1 ? _consecutive + 1 : 1;
            _lastRefreshFrame = Time.frameCount;
            if (_consecutive > MaxConsecutiveFrames)
            {
                _backoffUntil = Time.unscaledTime + BackoffSeconds;
                _consecutive = 0;
                if (!_warned)
                {
                    Debug.LogWarning($"{name}: 字体の絵の作り直しが {MaxConsecutiveFrames} フレーム続いたので、文字の描き直しを {BackoffSeconds} 秒休む", this);
                    _warned = true;
                }
                return;
            }
            RefreshAll();
        }

        /// <summary>字体の絵が作り直されたことを知らせる（テスト用。Font.textureRebuilt と同じ）。</summary>
        public void NotifyRebuilt()
        {
            _pending = Passes;
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
