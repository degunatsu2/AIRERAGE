using System;
using System.Globalization;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Runtime.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// キャラクター選択の画面（docs/15 T6-3。D-65）。キャラクターの一覧（CharacterRoster の順）の札（ロゴと名前）を横に並べ、左右で選ぶ。
    /// カーソルのある札のキャラクターを下に出す: 左にロゴ、名前、名前の隣に性能の種類のハッシュタグ、その下に Q・E・R の技（カタカナの
    /// 名前、何ができるか、クールタイムとスタック数。R は必要なゲージ。ユーザーの要望、2026-10-17）。決定で Confirmed（キャラクターの
    /// 番号）、戻る操作で Cancelled。今はトレーニングの 1 人（ローカル対戦の 4 人の参加は T6-5）。
    /// </summary>
    public sealed class CharacterSelectScreen : MonoBehaviour
    {
        /// <summary>下に出す技の枠（左から Q、E、R）。</summary>
        public static readonly AttackSlot[] SkillSlots = { AttackSlot.Special1, AttackSlot.Special2, AttackSlot.Super };

        [SerializeField] private CharacterRoster _roster;
        [Tooltip("キャラクターの札（一覧と同じ順）")]
        [SerializeField] private MenuOption[] _cards = new MenuOption[0];
        [SerializeField] private RawImage _logo;
        [SerializeField] private Text _name;
        [SerializeField] private Text _tags;
        [Tooltip("技の名前・説明・クールタイム（SkillSlots と同じ順。Q、E、R）")]
        [SerializeField] private Text[] _skillNames = new Text[0];
        [SerializeField] private Text[] _skillDescriptions = new Text[0];
        [SerializeField] private Text[] _skillCooldowns = new Text[0];

        private int _selectAtFrame = -1;
        private int _initial;
        private Font _japanese;

        /// <summary>決定した（キャラクターの番号）。</summary>
        public event Action<int> Confirmed;

        /// <summary>戻る操作をした。</summary>
        public event Action Cancelled;

        /// <summary>カーソルのあるキャラクターの番号。</summary>
        public int Highlighted { get; private set; }

        public int Count => _cards.Length;

        /// <summary>番号 index の札（テスト用）。</summary>
        public MenuOption Card(int index) => _cards[index];

        /// <summary>下に出している名前・ハッシュタグ・技（テスト用。技は SkillSlots の順）。</summary>
        public string ShownName => _name != null ? _name.text : string.Empty;
        public string ShownTags => _tags != null ? _tags.text : string.Empty;
        public string ShownSkillName(int index) => _skillNames[index].text;
        public string ShownSkillDescription(int index) => _skillDescriptions[index].text;
        public string ShownSkillCooldown(int index) => _skillCooldowns[index].text;

        private void Awake()
        {
            int skills = SkillSlots.Length;
            if (_roster == null || _cards.Length != _roster.Count || _name == null || _tags == null
                || _skillNames.Length != skills || _skillDescriptions.Length != skills || _skillCooldowns.Length != skills)
            {
                Debug.LogError($"{name}: キャラクターの一覧がないか、札の数（{_cards.Length}）が一覧と違うか、名前・ハッシュタグ・技の文字がありません", this);
                enabled = false;
                return;
            }
            UseJapaneseFont();
            for (int i = 0; i < _cards.Length; i++)
            {
                int index = i;
                _cards[i].Selected += () => Highlight(index);
                _cards[i].Clicked.AddListener(() => Confirmed?.Invoke(index));
                _cards[i].Cancelled += () => Cancelled?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_japanese != null)
            {
                Destroy(_japanese);
            }
        }

        // ハッシュタグと技の文字（日本語）を、PC の日本語の字体で書く（なければ Unity に入っている字体のまま）。
        private void UseJapaneseFont()
        {
            _japanese = JapaneseFont.Create(32);
            if (_japanese == null)
            {
                return;
            }
            _tags.font = _japanese;
            for (int i = 0; i < SkillSlots.Length; i++)
            {
                _skillNames[i].font = _japanese;
                _skillDescriptions[i].font = _japanese;
                _skillCooldowns[i].font = _japanese;
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
            _name.text = character.DisplayName;
            _tags.text = TagsText(character.Tags);
            if (_logo != null)
            {
                _logo.texture = character.Logo;
                _logo.enabled = character.Logo != null;
            }
            CombatTuning combat = character.Config.Combat;
            for (int i = 0; i < SkillSlots.Length; i++)
            {
                AttackSlot slot = SkillSlots[i];
                SkillText text = character.TextOf(slot);
                string skillName = text != null && !string.IsNullOrEmpty(text.Name) ? text.Name : combat.Get(slot).Name;
                _skillNames[i].text = skillName;
                _skillDescriptions[i].text = text != null ? text.Description : string.Empty;
                _skillCooldowns[i].text = CooldownText(combat, slot, text != null ? text.CooldownNote : string.Empty);
            }
        }

        /// <summary>ハッシュタグを 1 行に並べる。</summary>
        public static string TagsText(string[] tags)
        {
            return tags == null ? string.Empty : string.Join("  ", tags);
        }

        /// <summary>
        /// 技の説明の下に出す行: 特殊攻撃はクールタイム（数え始めるときの注記と、2 回以上続けて使えるならスタック数）、必殺技は必要なゲージ。
        /// </summary>
        public static string CooldownText(CombatTuning combat, AttackSlot slot, string cooldownNote)
        {
            if (slot == AttackSlot.Super)
            {
                return $"必要ゲージ {GaugeText(combat.SuperGaugeCost)}";
            }
            string text = $"クールタイム {SecondsText(combat.CooldownTicksOf(slot))}";
            if (!string.IsNullOrEmpty(cooldownNote))
            {
                text += $"（{cooldownNote}）";
            }
            int charges = combat.MaxChargesOf(slot);
            if (charges >= 2)
            {
                text += $"  ／  スタック {charges}";
            }
            return text;
        }

        // 秒（端数があれば小数 1 桁）。
        private static string SecondsText(int ticks)
        {
            return ticks % SimTime.TicksPerSecond == 0
                ? $"{ticks / SimTime.TicksPerSecond} 秒"
                : ((float)ticks / SimTime.TicksPerSecond).ToString("0.0", CultureInfo.InvariantCulture) + " 秒";
        }

        // ゲージ（0.1% 単位）を % で（端数があれば小数 1 桁。D-60）。
        private static string GaugeText(int gauge)
        {
            int per = CombatTuning.GaugePerPercent;
            return gauge % per == 0 ? $"{gauge / per}%" : $"{gauge / per}.{gauge % per}%";
        }
    }
}
