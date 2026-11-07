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
    /// 2026-11-06（ユーザーの要望）: 札はスクロールの中身に 2 列で並べ（実行中に Layout。画面に収まらない分はスクロール。カーソルの札が
    /// 見える所まで動く ScrollToSelection）、RANDOM STAGES ではいちばん上に CHECK ALL ／ UNCHECK ALL、下に DONE（アイテムを選ぶ画面と同じ形）。
    /// チェックが 0 個の間は閉じられない（案内を出す）。チェックの箱は、札が選ばれて動いてもその場にとどまる（MenuOption の pinned）。
    /// 説明は、オプションの LANGUAGE（GameSettings.LoadLanguage）の言語で出す（字体はどちらの言語でも日本語の字体 M PLUS 1。句読点を行の頭に置かずに折り返す。2026-10-27、2026-10-29）。
    /// </summary>
    public sealed class StageSelectScreen : MonoBehaviour
    {
        [Tooltip("ステージの札（一覧と同じ順）")]
        [SerializeField] private MenuOption[] _options = new MenuOption[0];
        [Tooltip("札のステージ（_options と同じ順）")]
        [SerializeField] private StageDefinition[] _stages = new StageDefinition[0];
        [Tooltip("右の見出しの札（ステージの名前。白地に黒の文字、縁と裏の札はステージのイメージカラー。2026-11-08。それまでは帯と白い文字）")]
        [SerializeField] private HeadingPlate _heading;
        [SerializeField] private Text _description;
        [Tooltip("説明の後ろの白い札（縁と裏の札はステージのイメージカラー。高さは説明の文に合わせる。2026-11-08）")]
        [SerializeField] private SlantedPlate _descriptionPlate;
        [Tooltip("ネットワーク対戦の案内（ホストが選んでいる、など。なくてもよい）")]
        [SerializeField] private Text _note;
        [Tooltip("RANDOM の札（ステージの札の後ろ。なくてもよい）")]
        [SerializeField] private MenuOption _random;
        [Tooltip("札の左端のチェックの箱と、付いているときの印（_options と同じ順。RANDOM STAGES のときだけ出す。なくてもよい）")]
        [SerializeField] private GameObject[] _checkBoxes = new GameObject[0];
        [SerializeField] private GameObject[] _checkMarks = new GameObject[0];
        [Tooltip("札を入れるスクロールの中身（なければ、ビルダーが置いた位置のまま）")]
        [SerializeField] private RectTransform _content;
        [Tooltip("RANDOM STAGES の、すべてにチェック ／ すべて外す ／ DONE の札（なくてもよい）")]
        [SerializeField] private MenuOption _checkAll;
        [SerializeField] private MenuOption _uncheckAll;
        [SerializeField] private MenuOption _done;

        // 札の並び（スクロールの中身の中の位置。左上から）。アイテムを選ぶ画面と同じ。
        private const int Columns = 2;
        // 札の左のチェックの箱（幅 34、札から 8 離す）が入るだけの余白。札は、チェックの箱を付ける前と同じ所（画面の左から 130）に来る
        // （スクロールの左端が 86。チェックの箱の分だけ右に寄っていた。ユーザーの指摘、2026-11-08）。
        private const float PlateLeft = 44f;
        // 説明の札の上下の余白と、いちばん低い高さ。
        private const float DescriptionPadding = 22f;
        private const float DescriptionMinHeight = 84f;
        private const float ColumnStep = 430f;
        private const float RowStep = 76f;
        private const float SectionGap = 14f;

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

        /// <summary>RANDOM STAGES の札（なければ null。テスト用）。</summary>
        public MenuOption DoneOption => _done;
        public MenuOption CheckAllOption => _checkAll;
        public MenuOption UncheckAllOption => _uncheckAll;

        /// <summary>RANDOM にカーソルがあるか。</summary>
        public bool RandomHighlighted { get; private set; }

        public int Count => _options.Length;

        /// <summary>番号 index の札（テスト用）。</summary>
        public MenuOption Option(int index) => _options[index];

        /// <summary>右に出している名前（テスト用）。</summary>
        public string ShownName => _heading != null ? _heading.Word : string.Empty;

        /// <summary>右の見出しの札（テスト用）。</summary>
        public HeadingPlate Heading => _heading;

        /// <summary>この画面のイメージカラー（入ってきた項目の色。2026-11-08）。</summary>
        public Color Accent { get; private set; } = MenuAccents.Default;

        /// <summary>
        /// この画面のイメージカラーを accent にする（以前は水色だった文字・チェックの印・スクロールの印と、CHECK ALL・UNCHECK ALL・DONE の
        /// 札。ステージの札はステージのイメージカラー、RANDOM は MenuAccents.Random のまま。2026-11-08 のユーザーの要望）。
        /// </summary>
        public void SetAccent(Color accent)
        {
            Accent = accent;
            AccentTint.Apply(this, accent);
            foreach (MenuOption extra in new[] { _checkAll, _uncheckAll, _done })
            {
                if (extra != null)
                {
                    extra.SetAccent(accent, true);
                }
            }
        }

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
                        ClosePool();
                    }
                    else if (!ReadOnly && !Rolling)
                    {
                        Cancelled?.Invoke();
                    }
                };
            }
            foreach (MenuOption extra in new[] { _checkAll, _uncheckAll, _done })
            {
                if (extra != null)
                {
                    extra.Cancelled += () =>
                    {
                        if (PoolMode)
                        {
                            ClosePool();
                        }
                    };
                }
            }
            if (_checkAll != null)
            {
                _checkAll.Clicked.AddListener(() => SetAllPool(true));
            }
            if (_uncheckAll != null)
            {
                _uncheckAll.Clicked.AddListener(() => SetAllPool(false));
            }
            if (_done != null)
            {
                _done.Clicked.AddListener(() =>
                {
                    if (PoolMode)
                    {
                        ClosePool();
                    }
                });
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
            Layout();
            RefreshChecks();
            SetNote(PoolNote);
        }

        private static string PoolNote => Loc.T("CHOOSE THE STAGES RANDOM CAN PICK", "RANDOM で選ばれるステージを選んでください");

        // RANDOM STAGES: 番号 index のチェックを付け外しする（0 個にしてもよいが、そのままでは閉じられない）。
        private void TogglePool(int index)
        {
            StageDefinition stage = _stages[index];
            RandomStagePool.SetIncluded(stage, !RandomStagePool.IsIncluded(stage));
            SetNote(PoolNote);
            RefreshChecks();
        }

        // RANDOM STAGES: すべてにチェックを付ける・すべて外す。
        private void SetAllPool(bool on)
        {
            if (!PoolMode)
            {
                return;
            }
            foreach (StageDefinition stage in _stages)
            {
                RandomStagePool.SetIncluded(stage, on);
            }
            SetNote(PoolNote);
            RefreshChecks();
        }

        // RANDOM STAGES を閉じる（チェックが 0 個なら閉じずに案内を出す）。
        private void ClosePool()
        {
            for (int i = 0; i < _stages.Length; i++)
            {
                if (RandomStagePool.IsIncluded(_stages[i]))
                {
                    PoolClosed?.Invoke();
                    return;
                }
            }
            SetNote(Loc.T("CHECK AT LEAST ONE STAGE", "1つ以上のステージにチェックを付けてください"));
        }

        // 札の並び: ステージの札（2 列）→ RANDOM（ステージ選択）。RANDOM STAGES では、いちばん上に CHECK ALL ／ UNCHECK ALL。
        private void Layout()
        {
            if (_content == null)
            {
                return;
            }
            float y = 0f;
            bool pool = PoolMode;
            foreach (MenuOption extra in new[] { _checkAll, _uncheckAll, _done })
            {
                if (extra != null)
                {
                    extra.gameObject.SetActive(pool);
                }
            }
            if (_random != null)
            {
                _random.gameObject.SetActive(!pool);
            }
            if (pool && _checkAll != null && _uncheckAll != null)
            {
                _checkAll.SetLabel(Loc.T("CHECK ALL", "すべてチェック"));
                _uncheckAll.SetLabel(Loc.T("UNCHECK ALL", "すべて外す"));
                _checkAll.SetHome(new Vector2(PlateLeft, y));
                _uncheckAll.SetHome(new Vector2(PlateLeft + ColumnStep, y));
                y -= RowStep + SectionGap;
            }
            int count = _options.Length + (!pool && _random != null ? 1 : 0);
            for (int k = 0; k < count; k++)
            {
                MenuOption option = k < _options.Length ? _options[k] : _random;
                option.SetHome(new Vector2(PlateLeft + (k % Columns) * ColumnStep, y - (k / Columns) * RowStep));
            }
            y -= (count + Columns - 1) / Columns * RowStep;
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, -y + 12f);
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
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
            Layout();
            RefreshChecks();
            _remoteIndex = -1;
            _selectIndex = Mathf.Clamp(index, 0, Mathf.Max(0, _stages.Length - 1));
            Highlight(_stages.Length > 0 ? _stages[_selectIndex] : null);
            if (_note != null)
            {
                _note.text = !network ? string.Empty
                    : readOnly ? Loc.T("THE HOST IS CHOOSING THE STAGE", "ホストがステージを選んでいます")
                    : Loc.T("YOU CHOOSE THE STAGE FOR EVERYONE", "全員のステージを選んでください");
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
            // 画面の大きさが変わって説明を折り返し直したときも、札の高さを文に合わせる。
            RefitDescription();
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

        /// <summary>今出している説明（テスト用）。</summary>
        public string ShownDescription => _description == null ? string.Empty
            : _description.GetComponent<LineBreakText>() is LineBreakText wrapper ? wrapper.Value : _description.text;

        // 説明を、オプションの言語で出す（字体はどちらの言語でも M PLUS 1。ビルダーが設定する）。
        private void ShowDescription(string english, string japanese)
        {
            if (_description == null)
            {
                return;
            }
            bool ja = GameSettings.LoadLanguage() == GameLanguage.Japanese && !string.IsNullOrEmpty(japanese);
            // 句読点を行の頭に置かずに折り返す（LineBreakText）。
            LineBreakText.Set(_description, ja ? japanese : english);
            RefitDescription();
        }

        /// <summary>説明の後ろの札（テスト用。なければ null）。</summary>
        public SlantedPlate DescriptionPlate => _descriptionPlate;

        /// <summary>説明の札の高さを文に合わせる（上の端はそのまま、下へ伸びる。画面を撮る道具も、折り返しを入れ直した後に呼ぶ）。</summary>
        public void RefitDescription()
        {
            if (_descriptionPlate == null || _description == null)
            {
                return;
            }
            var rect = _descriptionPlate.rectTransform;
            float height = Mathf.Max(DescriptionMinHeight, _description.preferredHeight + DescriptionPadding * 2f);
            if (!Mathf.Approximately(rect.sizeDelta.y, height))
            {
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            }
        }

        // 説明の札の縁と裏の札を、見出しと同じイメージカラーにする。
        private void TintDescriptionPlate(Color accent)
        {
            if (_descriptionPlate == null)
            {
                return;
            }
            _descriptionPlate.BorderColor = accent;
            _descriptionPlate.AccentColor = accent;
            if (!Application.isPlaying)
            {
                // 再生していないとき（画面を撮る道具）は、裏の札を出しきった形で見せる。
                _descriptionPlate.SetBackNow(1f);
            }
            else if (_descriptionPlate.BackTarget < 1f)
            {
                _descriptionPlate.BackTarget = 1f;
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
                // RANDOM（イメージカラーは以前の帯の灰色）。
                if (_heading != null)
                {
                    _heading.Show("RANDOM", MenuAccents.Random);
                }
                TintDescriptionPlate(MenuAccents.Random);
                ShowDescription(RandomDescription, RandomDescriptionJa);
                return;
            }
            if (_heading != null)
            {
                _heading.Show(stage.DisplayName, new Color(stage.Color.r, stage.Color.g, stage.Color.b, 1f));
            }
            TintDescriptionPlate(new Color(stage.Color.r, stage.Color.g, stage.Color.b, 1f));
            ShowDescription(stage.Description, stage.DescriptionJa);
        }
    }
}
