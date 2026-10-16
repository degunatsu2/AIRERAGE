using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 風の線の影（ユーザーの要望、2026-10-16: 風の見た目が背景と被って見づらいので、濃い緑色の影を付ける）。
    /// 風の線 1 本に影の線 1 本を作り、毎フレーム Sync で、同じ形を少しずらして太く・濃い緑で後ろに描く。
    /// </summary>
    public sealed class WindShadow
    {
        /// <summary>影の色（濃い緑）。</summary>
        public static readonly Color ShadowColor = new Color(0.02f, 0.24f, 0.07f);

        // 影のずらし（右下、奥）と太さの倍率。
        private static readonly Vector3 Offset = new Vector3(0.05f, -0.06f, 0.04f);
        private const float WidthScale = 2.2f;
        private const float AlphaScale = 0.8f;

        private readonly LineRenderer _main;
        private readonly LineRenderer _shadow;
        private Vector3[] _buffer = new Vector3[16];
        private readonly Gradient _gradient = new Gradient();
        private readonly GradientColorKey[] _colors = { new GradientColorKey(ShadowColor, 0f), new GradientColorKey(ShadowColor, 1f) };
        private readonly GradientAlphaKey[] _alphas = new GradientAlphaKey[2];

        /// <summary>main の影を、main と同じ親の下に作る（main より先に描く）。</summary>
        public WindShadow(LineRenderer main)
        {
            _main = main;
            _shadow = Object.Instantiate(main, main.transform.parent);
            _shadow.name = main.name + "Shadow";
            _shadow.sortingOrder = main.sortingOrder - 1;
            _shadow.enabled = false;
        }

        public LineRenderer Main => _main;

        public LineRenderer Shadow => _shadow;

        /// <summary>影を、今の main の形・太さ・濃さに合わせる（main を描いた後に呼ぶ）。</summary>
        public void Sync()
        {
            _shadow.enabled = _main.enabled;
            if (!_main.enabled)
            {
                return;
            }
            int count = _main.positionCount;
            if (_buffer.Length < count)
            {
                _buffer = new Vector3[Mathf.NextPowerOfTwo(count)];
            }
            _main.GetPositions(_buffer);
            _shadow.loop = _main.loop;
            _shadow.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                _shadow.SetPosition(i, _buffer[i] + Offset);
            }
            _shadow.widthMultiplier = _main.widthMultiplier * WidthScale;
            // キーを 2 つにして色を決める（startColor だけでは、途中のキーが残ることがある）。
            _alphas[0] = new GradientAlphaKey(_main.startColor.a * AlphaScale, 0f);
            _alphas[1] = new GradientAlphaKey(_main.endColor.a * AlphaScale, 1f);
            _gradient.SetKeys(_colors, _alphas);
            _shadow.colorGradient = _gradient;
        }
    }
}
