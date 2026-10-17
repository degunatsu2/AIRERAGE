using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Data
{
    /// <summary>
    /// キャラクター 1 体のアセット（docs/03_architecture.md 4.4、docs/13 T5-5）。シミュレーションの調整値（Core の CharacterConfig。
    /// 移動、ワイヤー、攻撃と技）と、表示だけに使う値（名前、ロゴ、技の絵、ハッシュタグと技の説明）を持つ。仮値は CharacterConfig の初期値にある。
    /// シミュレーションはこの CharacterConfig を参照で持つので、プレイ中にインスペクターで変えた値は次のティックから反映される
    /// （オンラインでは、開始のときにホストの値を全員へ送る）。
    /// </summary>
    [CreateAssetMenu(menuName = "Airerage/Character Config", fileName = "CharacterConfig")]
    public sealed class CharacterConfigAsset : ScriptableObject
    {
        [SerializeField] private CharacterConfig _config = new CharacterConfig();
        [Tooltip("表示用の名前（キャラクターの選択、右上の表など）")]
        [SerializeField] private string _displayName = "STANDARD";
        [Tooltip("キャラクターのロゴ（キャラクターの選択、右上の札。docs/13 T5-8。なければ出さない）")]
        [SerializeField] private Texture2D _logo;
        [Tooltip("特殊攻撃 1 の絵（なければ技の名前で表示する）")]
        [SerializeField] private Texture2D _special1Icon;
        [Tooltip("特殊攻撃 2 の絵（なければ技の名前で表示する）")]
        [SerializeField] private Texture2D _special2Icon;
        [Tooltip("必殺技の絵（なければ技の名前で表示する）")]
        [SerializeField] private Texture2D _superIcon;
        [Tooltip("性能の種類を表すハッシュタグ（キャラクター選択。例: #高速）")]
        [SerializeField] private string[] _tags = new string[0];
        [Tooltip("特殊攻撃 1（Q）・特殊攻撃 2（E）・必殺技（R）の名前と説明（キャラクター選択）")]
        [SerializeField] private SkillText _special1Text = new SkillText();
        [SerializeField] private SkillText _special2Text = new SkillText();
        [SerializeField] private SkillText _superText = new SkillText();

        public CharacterConfig Config => _config;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;

        /// <summary>キャラクターのロゴ（なければ null）。</summary>
        public Texture2D Logo => _logo;

        /// <summary>技の絵（特殊攻撃と必殺技。なければ null）。</summary>
        public Texture2D IconOf(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.Special1: return _special1Icon;
                case AttackSlot.Special2: return _special2Icon;
                case AttackSlot.Super: return _superIcon;
                default: return null;
            }
        }

        /// <summary>性能の種類を表すハッシュタグ（キャラクター選択）。</summary>
        public string[] Tags => _tags;

        /// <summary>技の表示用の名前と説明（特殊攻撃と必殺技。それ以外は null）。</summary>
        public SkillText TextOf(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.Special1: return _special1Text;
                case AttackSlot.Special2: return _special2Text;
                case AttackSlot.Super: return _superText;
                default: return null;
            }
        }

        /// <summary>シーンのビルダー用: ハッシュタグと技の説明を設定する。</summary>
        public void SetProfile(string[] tags, SkillText special1, SkillText special2, SkillText super)
        {
            _tags = tags ?? new string[0];
            _special1Text = special1 ?? new SkillText();
            _special2Text = special2 ?? new SkillText();
            _superText = super ?? new SkillText();
        }

        /// <summary>シーンのビルダー用の設定（CharacterPresets から作るとき）。</summary>
        public void Configure(CharacterConfig config, string displayName)
        {
            _config = config ?? new CharacterConfig();
            _displayName = displayName ?? string.Empty;
        }

        /// <summary>シーンのビルダー用: ロゴを設定する。</summary>
        public void SetLogo(Texture2D logo)
        {
            _logo = logo;
        }

        private void OnValidate()
        {
            // 編集中の不正な値は、アセット名と項目名を出して知らせる（シミュレーションへの追加時には例外で止まる）。
            try
            {
                _config.Validate();
            }
            catch (InvalidTuningException e)
            {
                Debug.LogError($"{name}: {e.Message}", this);
            }
        }
    }
}
