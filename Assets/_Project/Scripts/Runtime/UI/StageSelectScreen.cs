using System;
using Airerage.Runtime.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// ステージ選択の画面（docs/15 T6-4。D-65）。ステージの一覧（StageCatalog の順）の札を縦に並べ、カーソルのあるステージの名前と説明を
    /// 右に出す。決定で Confirmed（ステージ）、戻る操作で Cancelled。
    /// ネットワーク対戦（docs/15 T6-7）: ホストは同じように選び、カーソルを動かすたびに HighlightedIndexChanged（全員へ送る）。
    /// ほかの人は選べず（ReadOnly）、ホストのカーソル（ShowRemoteCursor）を見る。
    /// 最後の札は RANDOM（決めたときに、ステージの一覧からランダムに選ぶ。ユーザーの要望、2026-10-19）。カーソルのあるステージは
    /// HighlightedStageChanged で知らせる（メニューの背景に映す。RANDOM では null）。
    /// </summary>
    public sealed class StageSelectScreen : MonoBehaviour
    {
        [Tooltip("ステージの札（一覧と同じ順）")]
        [SerializeField] private MenuOption[] _options = new MenuOption[0];
        [Tooltip("札のステージ（_options と同じ順）")]
        [SerializeField] private StageDefinition[] _stages = new StageDefinition[0];
        [SerializeField] private Text _name;
        [SerializeField] private Text _description;
        [SerializeField] private Image _band;
        [Tooltip("ネットワーク対戦の案内（ホストが選んでいる、など。なくてもよい）")]
        [SerializeField] private Text _note;
        [Tooltip("RANDOM の札（ステージの札の後ろ。なくてもよい）")]
        [SerializeField] private MenuOption _random;

        private int _selectAtFrame = -1;
        private int _selectIndex;
        private int _remoteIndex = -1;

        /// <summary>決定した（ステージ）。</summary>
        public event Action<StageDefinition> Confirmed;

        /// <summary>戻る操作をした。</summary>
        public event Action Cancelled;

        /// <summary>カーソルのあるステージ。</summary>
        public StageDefinition Highlighted { get; private set; }

        /// <summary>ネットワーク対戦で出しているか（MenuScreens はこのときの決定・戻るを扱わない）。</summary>
        public bool NetworkMode { get; private set; }

        /// <summary>選べない（ネットワーク対戦のホスト以外。ホストのカーソルを見るだけ）。</summary>
        public bool ReadOnly { get; private set; }

        /// <summary>ネットワーク対戦のホストがカーソルを動かした（ステージの番号。RANDOM は RandomIndex）。</summary>
        public event Action<int> HighlightedIndexChanged;

        /// <summary>カーソルのあるステージが変わった（RANDOM では null）。</summary>
        public event Action<StageDefinition> HighlightedStageChanged;

        /// <summary>RANDOM の札の番号（ステージの札の後ろ）。</summary>
        public int RandomIndex => _options.Length;

        /// <summary>RANDOM の札（なければ null。テスト用）。</summary>
        public MenuOption RandomOption => _random;

        /// <summary>RANDOM にカーソルがあるか。</summary>
        public bool RandomHighlighted { get; private set; }

        public int Count => _options.Length;

        /// <summary>番号 index の札（テスト用）。</summary>
        public MenuOption Option(int index) => _options[index];

        /// <summary>右に出している名前（テスト用）。</summary>
        public string ShownName => _name != null ? _name.text : string.Empty;

        private void Awake()
        {
            if (_options.Length == 0 || _options.Length != _stages.Length)
            {
                Debug.LogError($"{name}: ステージの札がないか、札（{_options.Length}）とステージ（{_stages.Length}）の数が違います", this);
                enabled = false;
                return;
            }
            for (int i = 0; i < _options.Length; i++)
            {
                StageDefinition stage = _stages[i];
                int index = i;
                _options[i].Selected += () =>
                {
                    if (ReadOnly)
                    {
                        return;
                    }
                    Highlight(stage);
                    if (NetworkMode)
                    {
                        HighlightedIndexChanged?.Invoke(index);
                    }
                };
                _options[i].Clicked.AddListener(() =>
                {
                    if (!ReadOnly)
                    {
                        Confirmed?.Invoke(stage);
                    }
                });
                _options[i].Cancelled += () =>
                {
                    if (!ReadOnly)
                    {
                        Cancelled?.Invoke();
                    }
                };
            }
            if (_random != null)
            {
                _random.Selected += () =>
                {
                    if (ReadOnly)
                    {
                        return;
                    }
                    Highlight(null);
                    if (NetworkMode)
                    {
                        HighlightedIndexChanged?.Invoke(RandomIndex);
                    }
                };
                _random.Clicked.AddListener(() =>
                {
                    if (!ReadOnly)
                    {
                        Confirmed?.Invoke(_stages[UnityEngine.Random.Range(0, _stages.Length)]);
                    }
                });
                _random.Cancelled += () =>
                {
                    if (!ReadOnly)
                    {
                        Cancelled?.Invoke();
                    }
                };
            }
        }

        /// <summary>出す（カーソルは最初のステージ。押したボタンで決まらないよう、札を選ぶのは次のフレーム）。</summary>
        public void Show()
        {
            Open(false, false, 0);
        }

        /// <summary>
        /// ネットワーク対戦で出す。host ならカーソル index から選べ、そうでなければホストのカーソルを見るだけ（ShowRemoteCursor）。
        /// </summary>
        public void ShowNetwork(bool host, int index)
        {
            Open(true, !host, index);
        }

        /// <summary>ネットワーク対戦（選べない人）: ホストのカーソルを index に出す。</summary>
        public void ShowRemoteCursor(int index)
        {
            if (!ReadOnly || index == _remoteIndex || OptionAt(index) == null)
            {
                return;
            }
            _remoteIndex = index;
            Highlight(index < _stages.Length ? _stages[index] : null);
            OptionAt(index).SelectQuietly();
        }

        private void Open(bool network, bool readOnly, int index)
        {
            gameObject.SetActive(true);
            NetworkMode = network;
            ReadOnly = readOnly;
            _remoteIndex = -1;
            _selectIndex = Mathf.Clamp(index, 0, Mathf.Max(0, _stages.Length - 1));
            Highlight(_stages.Length > 0 ? _stages[_selectIndex] : null);
            if (_note != null)
            {
                _note.text = !network ? string.Empty : readOnly ? "THE HOST IS CHOOSING THE STAGE" : "YOU CHOOSE THE STAGE FOR EVERYONE";
            }
            _selectAtFrame = Time.frameCount + 1;
        }

        public void Hide()
        {
            _selectAtFrame = -1;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_selectAtFrame >= 0 && Time.frameCount >= _selectAtFrame)
            {
                _selectAtFrame = -1;
                if (ReadOnly)
                {
                    int remote = _remoteIndex;
                    _remoteIndex = -1;
                    ShowRemoteCursor(remote >= 0 ? remote : _selectIndex);
                }
                else if (_selectIndex < _options.Length)
                {
                    _options[_selectIndex].SelectQuietly();
                }
            }
            else if (ReadOnly && _remoteIndex >= 0 && UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != OptionAt(_remoteIndex).gameObject)
            {
                // 選べない人がカーソルを動かしても、ホストのカーソルに戻す。
                OptionAt(_remoteIndex).SelectQuietly();
            }
        }

        // 番号 index の札（RandomIndex なら RANDOM の札。範囲の外なら null）。
        private MenuOption OptionAt(int index)
        {
            if (index >= 0 && index < _options.Length)
            {
                return _options[index];
            }
            return index == RandomIndex ? _random : null;
        }

        private void Highlight(StageDefinition stage)
        {
            bool changed = stage != Highlighted || RandomHighlighted != (stage == null);
            Highlighted = stage;
            RandomHighlighted = stage == null;
            if (changed)
            {
                HighlightedStageChanged?.Invoke(stage);
            }
            if (stage == null)
            {
                // RANDOM。
                if (_name != null)
                {
                    _name.text = "RANDOM";
                }
                if (_description != null)
                {
                    _description.text = "Picks one of the stages at random.";
                }
                if (_band != null)
                {
                    _band.color = new Color(0.7f, 0.7f, 0.75f, 0.35f);
                }
                return;
            }
            if (_name != null)
            {
                _name.text = stage.DisplayName;
            }
            if (_description != null)
            {
                _description.text = stage.Description;
            }
            if (_band != null)
            {
                _band.color = new Color(stage.Color.r, stage.Color.g, stage.Color.b, 0.35f);
            }
        }
    }
}
