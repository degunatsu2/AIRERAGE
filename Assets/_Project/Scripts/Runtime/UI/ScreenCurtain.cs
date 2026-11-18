using System.Collections;
using Airerage.Runtime.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 試合へ入るときの切り替え（D-99 の T12 ゲート。2026-11-17 のユーザーの決定。前は D-87 の T10 の黒い帯）。いつも読み込む App の
    /// シーンに置く: GameFlow が試合のシーンを読み込む前に Close（行き先のステージの色の扉が左右から閉じ、合わせ目に名前の札が弾んで出る。
    /// 0.5 秒）、読み込みが済んだら Open（札が縮んで扉が左右へ開き、シネマスコープの帯が付いた試合の始まりの演出へそのまま続く）。
    /// 閉じている間は、メニューの選択と決定を止める。
    /// </summary>
    public sealed class ScreenCurtain : MonoBehaviour
    {
        public const float CloseSeconds = 0.5f;
        public const float OpenSeconds = 0.56f;

        // 1 フレームで進める時間の上限（秒）。読み込み直後の重いフレーム（1 秒以上かかることがある）で、開く演出を一度に使い切らない
        // （2026-11-18 の指摘: 閉じた後、開く演出が省略されて始まる）。
        private const float MaxStep = 0.05f;

        // 開き始める前に待つ、落ち着いたフレーム（この秒数より短いフレーム）の数と、待つ長さの上限（秒。実時間）。
        private const float SettledFrameSeconds = 0.1f;
        private const int SettledFrames = 2;
        private const float MaxSettleSeconds = 1.5f;

        [SerializeField] private GameObject _root;
        [SerializeField] private CurtainGraphic _bars;
        [SerializeField] private Text _label;
        [SerializeField] private Text _sub;

        private float _closeTime;
        private float _openTime;
        private bool _opening;
        private int _settledFrames;
        private float _settleWaited;
        private bool _navHeld;

        /// <summary>帯が閉じきっているか。</summary>
        public bool Closed { get; private set; }

        /// <summary>帯を出しているか（閉じる途中・閉じている・開く途中）。</summary>
        public bool Showing => _root != null && _root.activeSelf;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(GameObject root, CurtainGraphic bars, Text label, Text sub = null)
        {
            _root = root;
            _bars = bars;
            _label = label;
            _sub = sub;
        }

        /// <summary>札の名前（テスト用）。</summary>
        public string Label => _label != null ? _label.text : string.Empty;

        private void Awake()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private void OnDisable()
        {
            ReleaseNavigation();
        }

        /// <summary>扉を閉じる（閉じきるまで待つコルーチン）。label は札に出す行き先の名前。</summary>
        public IEnumerator Close(string label)
        {
            return Close(label, string.Empty, new Color(0.35f, 0.85f, 1f));
        }

        /// <summary>扉を閉じる（閉じきるまで待つコルーチン）。label と sub は札の名前と日本語の名前、color は行き先のステージの色。</summary>
        public IEnumerator Close(string label, string sub, Color color)
        {
            if (_root == null || _bars == null)
            {
                yield break;
            }
            _bars.SetStageColor(color);
            if (_label != null)
            {
                _label.text = label ?? string.Empty;
            }
            if (_sub != null)
            {
                _sub.text = sub ?? string.Empty;
            }
            _root.SetActive(true);
            // 帯が閉じるのに合わせて、進む風の音（2026-11-07。docs/18 4.19）。
            UiSounds.Find().Play(SoundId.UiWhoosh);
            _closeTime = 0f;
            _opening = false;
            Closed = false;
            HoldNavigation();
            Animate();
            while (_closeTime < CloseSeconds)
            {
                yield return null;
            }
            Closed = true;
            Animate();
        }

        /// <summary>帯を開く（閉じていなければ何もしない）。</summary>
        public void Open()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }
            _openTime = 0f;
            _settledFrames = 0;
            _settleWaited = 0f;
            _opening = true;
        }

        /// <summary>開く演出が進んだ時間（秒。テスト用）。</summary>
        public float OpenProgressSeconds => _opening ? _openTime : 0f;

        private void Update()
        {
            if (_root != null && _root.activeSelf)
            {
                Step(Time.unscaledDeltaTime);
                Animate();
            }
        }

        // 演出の時間を進める。重いフレームでも MaxStep までしか進めない。開くときは、読み込み直後の詰まったフレームが落ち着くまで
        // 待ってから進める（試合のシーンの最初の数フレームは重い）。
        private void Step(float delta)
        {
            float step = Mathf.Min(Mathf.Max(0f, delta), MaxStep);
            if (!_opening)
            {
                _closeTime += step;
                return;
            }
            if (_settledFrames < SettledFrames && _settleWaited < MaxSettleSeconds)
            {
                _settleWaited += delta;
                _settledFrames = delta < SettledFrameSeconds ? _settledFrames + 1 : 0;
                return;
            }
            _openTime += step;
        }

        private void Animate()
        {
            if (_opening)
            {
                // 札が縮んで消え（0.12 秒）、扉が左右へ開く（0.06〜0.56 秒）。
                float t = _openTime;
                float plate = 1f - UiEase.Clamp01(t / 0.12f);
                _bars.Set(1f, plate, (t - 0.06f) / 0.5f);
                SetLabel(UiEase.OutBack(plate), plate);
                if (t >= OpenSeconds)
                {
                    _opening = false;
                    Closed = false;
                    _bars.Set(0f, 0f, 0f);
                    _root.SetActive(false);
                    ReleaseNavigation();
                }
                return;
            }
            // 扉が閉じ（0.32 秒）、合わさった後に札が弾んで出る（0.26〜0.5 秒）。
            float c = _closeTime;
            float pop = UiEase.Clamp01((c - 0.26f) / 0.24f);
            _bars.Set(c / 0.32f, pop, 0f);
            SetLabel(UiEase.OutBack(pop), pop);
        }

        // 札の文字（札と一緒に大きさと濃さを変える）。
        private void SetLabel(float scale, float alpha)
        {
            foreach (Text text in new[] { _label, _sub })
            {
                if (text == null)
                {
                    continue;
                }
                text.rectTransform.localScale = Vector3.one * Mathf.Max(0f, scale);
                Color c = text.color;
                c.a = alpha;
                text.color = c;
            }
        }

        private void HoldNavigation()
        {
            NavigationGate gate = NavigationGate.Current;
            if (gate != null && !_navHeld)
            {
                gate.Hold(this);
                _navHeld = true;
            }
        }

        private void ReleaseNavigation()
        {
            if (!_navHeld)
            {
                return;
            }
            _navHeld = false;
            NavigationGate gate = NavigationGate.Current;
            if (gate != null)
            {
                gate.Release(this);
            }
        }
    }
}
