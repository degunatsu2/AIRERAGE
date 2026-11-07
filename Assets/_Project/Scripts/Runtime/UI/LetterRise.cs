using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 文字の演出 W2「レターライズ」（D-90。ユーザーが選んだ W1・W2・W16・W17。2026-11-08）: Play すると、左の文字から 1 文字ずつ順に
    /// 文字の大きさの 1/3 だけはね上がって戻る（見本のページと同じ: 0.26 秒、前半ではね上がり後半で戻る、それぞれ cubic-bezier(.34,1.7,.5,1)、
    /// 1 文字ごとに 0.014 秒遅らせる）。字はずっと読める。
    /// 文字の網（Text の頂点）だけを動かすので、札（SlantedPlate）や裏の札には何も起きない。縁取り（Outline）の写しも、同じ位置の文字と
    /// 一緒に動く（何文字目かは、文字の左端の位置から決める）。
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class LetterRise : BaseMeshEffect
    {
        /// <summary>1 文字がはね上がって戻るまでの秒数。</summary>
        public const float RiseSeconds = 0.26f;
        /// <summary>次の文字が動き出すまでの秒数。</summary>
        public const float LetterDelay = 0.014f;
        /// <summary>ボタンの名前のはね上がる高さ（文字の大きさに対する割合。見本は 21px の字で 7px）。</summary>
        public const float ButtonRatio = 1f / 3f;
        /// <summary>見出しの文字のはね上がる高さ（見本は 46px の字で 7px）。</summary>
        public const float HeadingRatio = 0.15f;

        private static readonly List<UIVertex> Vertices = new List<UIVertex>();

        // 画面の切り替えの写しにも引き継ぐため、保存する値にする。
        [SerializeField, HideInInspector] private float _playedAt = -10f;
        [SerializeField, HideInInspector] private float _ratio = ButtonRatio;
        private Text _text;

        private Text Label => _text != null ? _text : _text = GetComponent<Text>();

        /// <summary>動いている途中か（テスト用）。</summary>
        public bool Playing => Application.isPlaying && Time.unscaledTime - _playedAt < Duration;

        // 最後の文字が戻るまでの秒数。
        private float Duration => RiseSeconds + LetterDelay * Mathf.Max(0, Label.text.Length - 1);

        /// <summary>はね上がる高さ（文字の大きさに対する割合）。</summary>
        public float Ratio
        {
            get => _ratio;
            set => _ratio = value;
        }

        /// <summary>はね上がる動きを delay 秒後に始める。</summary>
        public void Play(float delay = 0f)
        {
            _playedAt = Time.unscaledTime + delay;
            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
        }

        /// <summary>other の動きの始まりと高さをそのまま写す（画面の切り替えの写し用）。</summary>
        public void CopyState(LetterRise other)
        {
            if (other == null || (other._playedAt == _playedAt && other._ratio == _ratio))
            {
                return;
            }
            _playedAt = other._playedAt;
            _ratio = other._ratio;
            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
        }

        /// <summary>動きを止めて元の形に戻す。</summary>
        public void Stop()
        {
            _playedAt = -10f;
            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
        }

        private void Update()
        {
            if (graphic != null && Application.isPlaying && Time.unscaledTime - _playedAt < Duration + 0.05f)
            {
                graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || !Application.isPlaying)
            {
                return;
            }
            float t = Time.unscaledTime - _playedAt;
            if (t < 0f || t >= Duration)
            {
                return;
            }
            Vertices.Clear();
            vh.GetUIVertexStream(Vertices);
            if (Vertices.Count < 6)
            {
                return;
            }
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            for (int i = 0; i < Vertices.Count; i++)
            {
                float x = Vertices[i].position.x;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
            }
            int letters = Mathf.Max(1, CountLetters(Label.text));
            float span = Mathf.Max(1f, maxX - minX);
            float height = Label.fontSize * _ratio;
            // 文字の網は 1 文字につき三角形 2 つ（頂点 6 つ）。その左端の位置から何文字目かを決める。
            for (int q = 0; q + 5 < Vertices.Count; q += 6)
            {
                float left = float.MaxValue;
                for (int k = 0; k < 6; k++)
                {
                    left = Mathf.Min(left, Vertices[q + k].position.x);
                }
                int index = Mathf.Clamp(Mathf.FloorToInt((left - minX) / span * letters + 0.25f), 0, letters - 1);
                float lift = Rise(t - index * LetterDelay) * height;
                if (lift == 0f)
                {
                    continue;
                }
                for (int k = 0; k < 6; k++)
                {
                    UIVertex v = Vertices[q + k];
                    v.position.y += lift;
                    Vertices[q + k] = v;
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(Vertices);
        }

        /// <summary>1 文字の高さの動き（t は動き出してからの秒数。1 がはね上がりきった所。少し行きすぎて戻る）。</summary>
        public static float Rise(float t)
        {
            if (t <= 0f || t >= RiseSeconds)
            {
                return 0f;
            }
            float up = RiseSeconds * 0.4f;
            if (t < up)
            {
                return UiEase.CubicBezier(0.34f, 1.7f, 0.5f, 1f, t / up);
            }
            return 1f - UiEase.CubicBezier(0.34f, 1.7f, 0.5f, 1f, (t - up) / (RiseSeconds - up));
        }

        // 空白を除いた文字の数（網を作る文字の数）。
        private static int CountLetters(string text)
        {
            int count = 0;
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
