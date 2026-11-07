using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 試合へ入るときの切り替え（D-87 の T10 シネマスコープ。ユーザーの決定、2026-11-07）。いつも読み込む App のシーンに置く:
    /// GameFlow が試合のシーンを読み込む前に Close（黒い帯が上下から斜めに閉じ、行き先のステージの名前が滑り込む。0.5 秒）、読み込みが
    /// 済んだら Open（白い線が走って帯が開き、試合の始まりの演出へそのまま続く）。閉じている間は、メニューの選択と決定を止める。
    /// </summary>
    public sealed class ScreenCurtain : MonoBehaviour
    {
        public const float CloseSeconds = 0.5f;
        public const float OpenSeconds = 0.56f;

        [SerializeField] private GameObject _root;
        [SerializeField] private CurtainGraphic _bars;
        [SerializeField] private Text _label;

        private float _closeAt = -10f;
        private float _openAt = -10f;
        private bool _opening;
        private bool _navHeld;
        private Vector2 _labelHome;

        /// <summary>帯が閉じきっているか。</summary>
        public bool Closed { get; private set; }

        /// <summary>帯を出しているか（閉じる途中・閉じている・開く途中）。</summary>
        public bool Showing => _root != null && _root.activeSelf;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(GameObject root, CurtainGraphic bars, Text label)
        {
            _root = root;
            _bars = bars;
            _label = label;
        }

        private void Awake()
        {
            if (_label != null)
            {
                _labelHome = _label.rectTransform.anchoredPosition;
            }
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private void OnDisable()
        {
            ReleaseNavigation();
        }

        /// <summary>帯を閉じる（閉じきるまで待つコルーチン）。label は帯に出す行き先の名前。</summary>
        public IEnumerator Close(string label)
        {
            if (_root == null || _bars == null)
            {
                yield break;
            }
            if (_label != null)
            {
                _label.text = label ?? string.Empty;
            }
            _root.SetActive(true);
            _closeAt = Time.unscaledTime;
            _opening = false;
            Closed = false;
            HoldNavigation();
            Animate();
            while (Time.unscaledTime - _closeAt < CloseSeconds)
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
            _openAt = Time.unscaledTime;
            _opening = true;
        }

        private void Update()
        {
            if (_root != null && _root.activeSelf)
            {
                Animate();
            }
        }

        private void Animate()
        {
            float now = Time.unscaledTime;
            if (_opening)
            {
                float t = now - _openAt;
                _bars.Set(1f, t / 0.12f, (t - 0.12f) / 0.44f);
                SetLabel(0f, 1f - UiEase.Clamp01((t - 0.12f) / 0.16f));
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
            float c = now - _closeAt;
            _bars.Set(c / 0.26f, 0f, 0f);
            float p = UiEase.Clamp01((c - 0.18f) / 0.28f);
            SetLabel(260f * (1f - UiEase.OutCubic(p)), p);
        }

        private void SetLabel(float offset, float alpha)
        {
            if (_label == null)
            {
                return;
            }
            _label.rectTransform.anchoredPosition = _labelHome + new Vector2(offset, 0f);
            Color c = _label.color;
            c.a = alpha;
            _label.color = c;
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
