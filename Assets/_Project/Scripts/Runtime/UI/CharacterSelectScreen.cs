using System;
using System.Text;
using Airerage.Core.Combat;
using Airerage.Runtime.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// キャラクター選択の画面（docs/15 T6-3。D-65）。キャラクターの一覧（CharacterRoster の順）の札（ロゴと名前）を横に並べ、左右で選ぶ。
    /// カーソルのある札のキャラクターの名前・ロゴ・技（地上技、撃破技、Q、E、R）を下に出す。決定で Confirmed（キャラクターの番号）、
    /// 戻る操作で Cancelled。今はトレーニングの 1 人（ローカル対戦の 4 人の参加は T6-5）。
    /// </summary>
    public sealed class CharacterSelectScreen : MonoBehaviour
    {
        [SerializeField] private CharacterRoster _roster;
        [Tooltip("キャラクターの札（一覧と同じ順）")]
        [SerializeField] private MenuOption[] _cards = new MenuOption[0];
        [SerializeField] private RawImage _logo;
        [SerializeField] private Text _name;
        [SerializeField] private Text _moves;

        private int _selectAtFrame = -1;
        private int _initial;

        /// <summary>決定した（キャラクターの番号）。</summary>
        public event Action<int> Confirmed;

        /// <summary>戻る操作をした。</summary>
        public event Action Cancelled;

        /// <summary>カーソルのあるキャラクターの番号。</summary>
        public int Highlighted { get; private set; }

        public int Count => _cards.Length;

        /// <summary>番号 index の札（テスト用）。</summary>
        public MenuOption Card(int index) => _cards[index];

        /// <summary>下に出している名前と技（テスト用）。</summary>
        public string ShownName => _name != null ? _name.text : string.Empty;
        public string ShownMoves => _moves != null ? _moves.text : string.Empty;

        private void Awake()
        {
            if (_roster == null || _cards.Length != _roster.Count)
            {
                Debug.LogError($"{name}: キャラクターの一覧がないか、札の数（{_cards.Length}）が一覧と違います", this);
                enabled = false;
                return;
            }
            for (int i = 0; i < _cards.Length; i++)
            {
                int index = i;
                _cards[i].Selected += () => Highlight(index);
                _cards[i].Clicked.AddListener(() => Confirmed?.Invoke(index));
                _cards[i].Cancelled += () => Cancelled?.Invoke();
            }
        }

        /// <summary>出す（カーソルは initial のキャラクター。押したボタンで決まらないよう、札を選ぶのは次のフレーム）。</summary>
        public void Show(int initial)
        {
            gameObject.SetActive(true);
            _initial = Mathf.Clamp(initial, 0, Mathf.Max(0, _cards.Length - 1));
            Highlight(_initial);
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
                if (_initial < _cards.Length)
                {
                    _cards[_initial].SelectQuietly();
                }
            }
        }

        private void Highlight(int index)
        {
            Highlighted = index;
            CharacterConfigAsset character = _roster.Get(index);
            if (character == null)
            {
                return;
            }
            if (_name != null)
            {
                _name.text = character.DisplayName;
            }
            if (_logo != null)
            {
                _logo.texture = character.Logo;
                _logo.enabled = character.Logo != null;
            }
            if (_moves != null)
            {
                _moves.text = MovesOf(character.Config.Combat);
            }
        }

        // 技の名前の一覧（名前のない技は出さない）。
        private static string MovesOf(CombatTuning combat)
        {
            var text = new StringBuilder();
            Append(text, "GROUND", combat.Ground.Name);
            Append(text, "KO", combat.Get(combat.KoAttack).Name);
            Append(text, "Q", combat.Special1.Name);
            Append(text, "E", combat.Special2.Name);
            Append(text, "R", combat.Super.Name);
            return text.ToString();
        }

        private static void Append(StringBuilder text, string slot, string move)
        {
            if (string.IsNullOrEmpty(move))
            {
                return;
            }
            if (text.Length > 0)
            {
                text.Append('\n');
            }
            text.Append(slot.PadRight(8)).Append(move);
        }
    }
}
