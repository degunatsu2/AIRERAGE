using System;
using System.Collections.Generic;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// ステージ選択の画面（docs/15 T6-4。D-65）。ステージの一覧（StageCatalog の順）の札を縦に並べ、カーソルのあるステージの名前と説明を
    /// 右に出す。決定で Confirmed（ステージ）、戻る操作で Cancelled。
    /// ネットワーク対戦（docs/15 T6-7）: ホストは同じように選び、カーソルを動かすたびに HighlightedIndexChanged（全員へ送る）。
    /// ほかの人は選べず（ReadOnly）、ホストのカーソル（ShowRemoteCursor）を見る。
    /// 最後の札は RANDOM（決めたときに、ステージの一覧からランダムに選ぶ。ユーザーの要望、2026-10-19）。決めると、ルーレットのように
    /// カーソルがステージの札を回りながら遅くなり、選んだステージで止まってから Confirmed（2026-10-21。回っている間は操作を受け付けない。
    /// ネットワーク対戦では、ホストのカーソルが全員に送られるので、全員が同じルーレットを見る）。カーソルのあるステージは
    /// HighlightedStageChanged で知らせる（メニューの背景に映す。RANDOM では null）。
    /// RANDOM で選ぶのは、チェックを付けたステージだけ（RandomStagePool。ネットワーク対戦では全員が付けているステージだけ。
    /// ユーザーの要望、2026-10-26）。オプションから開く「RANDOM STAGES」（ShowRandomPool）では、札の左端のチェックを決定で付け外しし、
    /// 戻る操作で PoolClosed（RANDOM の札は出さない。最後の 1 つは外せない）。
    /// 説明は、オプションの LANGUAGE（GameSettings.LoadLanguage）の言語で出す（日本語は PC の日本語の字体 JapaneseFont。2026-10-27）。
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
        [Tooltip("札の左端のチェックの箱と、付いているときの印（_options と同じ順。RANDOM STAGES のときだけ出す。なくてもよい）")]
        [SerializeField] private GameObject[] _checkBoxes = new GameObject[0];
        [SerializeField] private GameObject[] _checkMarks = new GameObject[0];

        // ルーレット: 回る回数、最初と最後の 1 つ進む間隔（秒）、止まってから決めるまで（秒）。
        private const int RouletteLoops = 2;
        private const float RouletteFirstStep = 0.05f;
        private const float RouletteLastStep = 0.38f;
        private const float RouletteHold = 0.7f;

        private int _selectAtFrame = -1;
        private int _selectIndex;
        private int _remoteIndex = -1;
        private int _rouletteStep = -1;
        private int _rouletteSteps;
        private int _rouletteTarget;
        private float _rouletteNextAt;
        private bool _rouletteMoving;
        private ulong _randomPool = RandomStagePool.All;
        private Font _japanese;
        private Font _latin;

        /// <summary>RANDOM の札の説明（英語と日本語）。</summary>
        public const string RandomDescription = "Picks one of the stages at random.";
        public const string RandomDescriptionJa = "ステージをランダムに選びます。";

        /// <summary>オプションの RANDOM STAGES（RANDOM で選ぶステージのチェックを付け外しする）で出しているか。</summary>
        public bool PoolMode { get; private set; }

        /// <summary>RANDOM STAGES を閉じた（戻る操作）。</summary>
        public event Action PoolClosed;

        /// <summary>番号 index のステージに、RANDOM で選ぶチェックが付いているか（RANDOM STAGES の表示。テスト用）。</summary>
        public bool CheckShown(int index) => index < _checkMarks.Length && _checkMarks[index] != null && _checkMarks[index].activeSelf;

        /// <summary>RANDOM で選べるステージの番号のビット（RandomStagePool.Mask。ネットワーク対戦のホストは全員の共通）。</summary>
        public ulong RandomPool => _randomPool;

        /// <summary>RANDOM のルーレットが回っているか（止まって決めるまで）。</summary>
        public bool Rolling => _rouletteStep >= 0;

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
            if (_description != null)
            {
                _latin = _description.font;
            }
            for (int i = 0; i < _options.Length; i++)
            {
                StageDefinition stage = _stages[i];
                int index = i;
                _options[i].Selected += () =>
                {
                    if (ReadOnly || (Rolling && !_rouletteMoving))
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
                    if (PoolMode)
                    {
                        TogglePool(index);
                    }
                    else if (!ReadOnly && !Rolling)
                    {
                        Confirmed?.Invoke(stage);
                    }
                });
                _options[i].Cancelled += () =>
                {
                    if (PoolMode)
                    {
                        PoolClosed?.Invoke();
                    }
                    else if (!ReadOnly && !Rolling)
                    {
                        Cancelled?.Invoke();
                    }
                };
            }
            if (_random != null)
            {
                _random.Selected += () =>
                {
                    if (ReadOnly || Rolling)
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
                    if (!ReadOnly && !Rolling)
                    {
                        List<int> pool = RandomStagePool.Indices(_stages.Length, _randomPool);
                        StartRoulette(pool[UnityEngine.Random.Range(0, pool.Count)]);
                    }
                });
                _random.Cancelled += () =>
                {
                    if (!ReadOnly && !Rolling)
                    {
                        Cancelled?.Invoke();
                    }
                };
            }
        }

        /// <summary>出す（カーソルは最初のステージ。押したボタンで決まらないよう、札を選ぶのは次のフレーム）。RANDOM は自分のチェックから選ぶ。</summary>
        public void Show()
        {
            Open(false, false, 0);
            _randomPool = RandomStagePool.Mask(_stages);
        }

        /// <summary>
        /// ネットワーク対戦で出す。host ならカーソル index から選べ、そうでなければホストのカーソルを見るだけ（ShowRemoteCursor）。
        /// randomPool は RANDOM で選べるステージ（全員がチェックを付けているもの。ホストだけが使う）。
        /// </summary>
        public void ShowNetwork(bool host, int index, ulong randomPool = RandomStagePool.All)
        {
            Open(true, !host, index);
            _randomPool = randomPool;
        }

        /// <summary>
        /// オプションの RANDOM STAGES として出す: 札の左端にチェックを出し、決定で付け外しする（すぐに保存する）。戻る操作で PoolClosed。
        /// </summary>
        public void ShowRandomPool()
        {
            Open(false, false, 0);
            PoolMode = true;
            if (_random != null)
            {
                _random.gameObject.SetActive(false);
            }
            RefreshChecks();
            SetNote("CHECKED STAGES CAN BE PICKED BY RANDOM");
        }

        // RANDOM STAGES: 番号 index のチェックを付け外しする（最後の 1 つは外せない）。
        private void TogglePool(int index)
        {
            StageDefinition stage = _stages[index];
            bool included = RandomStagePool.IsIncluded(stage);
            if (included && RandomStagePool.Indices(_stages.Length, RandomStagePool.Mask(_stages)).Count <= 1)
            {
                SetNote("AT LEAST ONE STAGE MUST STAY CHECKED");
                return;
            }
            RandomStagePool.SetIncluded(stage, !included);
            SetNote("CHECKED STAGES CAN BE PICKED BY RANDOM");
            RefreshChecks();
        }

        private void RefreshChecks()
        {
            for (int i = 0; i < _checkBoxes.Length; i++)
            {
                if (_checkBoxes[i] != null)
                {
                    _checkBoxes[i].SetActive(PoolMode);
                }
                if (i < _checkMarks.Length && _checkMarks[i] != null && i < _stages.Length)
                {
                    _checkMarks[i].SetActive(PoolMode && RandomStagePool.IsIncluded(_stages[i]));
                }
            }
        }

        private void SetNote(string text)
        {
            if (_note != null)
            {
                _note.text = text;
            }
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
            PoolMode = false;
            if (_random != null)
            {
                _random.gameObject.SetActive(true);
            }
            RefreshChecks();
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
            _rouletteStep = -1;
            PoolMode = false;
            gameObject.SetActive(false);
        }

        // ルーレットを始める: 今のカーソル（RANDOM）から札を RouletteLoops 回りしてから target で止まる。
        private void StartRoulette(int target)
        {
            _rouletteTarget = target;
            _rouletteSteps = RouletteLoops * _options.Length + target + 1;
            _rouletteStep = 0;
            _rouletteNextAt = Time.unscaledTime;
            _selectAtFrame = -1;
        }

        // ルーレットを進める（1 つ進むたびに札を選ぶ音を鳴らし、カーソルを送る）。止まったら少し見せてから決める。
        private void UpdateRoulette()
        {
            if (Time.unscaledTime < _rouletteNextAt)
            {
                return;
            }
            if (_rouletteStep >= _rouletteSteps)
            {
                _rouletteStep = -1;
                Confirmed?.Invoke(_stages[_rouletteTarget]);
                return;
            }
            int index = _rouletteStep % _options.Length;
            _rouletteStep++;
            _rouletteMoving = true;
            _options[index].Button.Select();
            _rouletteMoving = false;
            if (_rouletteStep >= _rouletteSteps)
            {
                _rouletteNextAt = Time.unscaledTime + RouletteHold;
                return;
            }
            // だんだん遅くなる（終わりに近いほど間隔が長い）。
            float progress = _rouletteStep / (float)_rouletteSteps;
            _rouletteNextAt = Time.unscaledTime + Mathf.Lerp(RouletteFirstStep, RouletteLastStep, progress * progress * progress);
        }

        private void Update()
        {
            if (Rolling)
            {
                UpdateRoulette();
                return;
            }
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

        private void OnDestroy()
        {
            if (_japanese != null)
            {
                Destroy(_japanese);
            }
        }

        /// <summary>今出している説明（テスト用）。</summary>
        public string ShownDescription => _description != null ? _description.text : string.Empty;

        // 説明を、オプションの言語で出す（日本語なら日本語の字体。PC になければ Unity に入っている字体のまま）。
        private void ShowDescription(string english, string japanese)
        {
            if (_description == null)
            {
                return;
            }
            bool ja = GameSettings.LoadLanguage() == GameLanguage.Japanese && !string.IsNullOrEmpty(japanese);
            if (ja && _japanese == null)
            {
                _japanese = JapaneseFont.Create(32);
            }
            if (_latin == null)
            {
                _latin = _description.font;
            }
            _description.font = ja && _japanese != null ? _japanese : _latin;
            _description.text = ja ? japanese : english;
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
                ShowDescription(RandomDescription, RandomDescriptionJa);
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
            ShowDescription(stage.Description, stage.DescriptionJa);
            if (_band != null)
            {
                _band.color = new Color(stage.Color.r, stage.Color.g, stage.Color.b, 0.35f);
            }
        }
    }
}
