using System;
using Airerage.Runtime.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// ステージ選択の画面（docs/15 T6-4。D-65）。ステージの一覧（StageCatalog の順）の札を縦に並べ、カーソルのあるステージの名前と説明を
    /// 右に出す。決定で Confirmed（ステージ）、戻る操作で Cancelled。
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

        private int _selectAtFrame = -1;

        /// <summary>決定した（ステージ）。</summary>
        public event Action<StageDefinition> Confirmed;

        /// <summary>戻る操作をした。</summary>
        public event Action Cancelled;

        /// <summary>カーソルのあるステージ。</summary>
        public StageDefinition Highlighted { get; private set; }

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
                _options[i].Selected += () => Highlight(stage);
                _options[i].Clicked.AddListener(() => Confirmed?.Invoke(stage));
                _options[i].Cancelled += () => Cancelled?.Invoke();
            }
        }

        /// <summary>出す（カーソルは最初のステージ。押したボタンで決まらないよう、札を選ぶのは次のフレーム）。</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            Highlight(_stages.Length > 0 ? _stages[0] : null);
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
                _options[0].SelectQuietly();
            }
        }

        private void Highlight(StageDefinition stage)
        {
            Highlighted = stage;
            if (stage == null)
            {
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
