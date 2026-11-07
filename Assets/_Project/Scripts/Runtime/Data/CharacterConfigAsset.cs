using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using Airerage.Runtime.Settings;
using UnityEngine;

namespace Airerage.Runtime.Data
{
    /// <summary>キャラクターの仮の見た目（衣装。表示だけ。ユーザーの要望、2026-10-20）。</summary>
    public enum CharacterCostume
    {
        /// <summary>箱のまま。</summary>
        Standard = 0,

        /// <summary>サングラス・アフロ・金色の服。</summary>
        RiskTaker = 1,

        /// <summary>ブレードシューズ・「風」のゼッケン。</summary>
        StormRunner = 2,

        /// <summary>宇宙服・宇宙帽。</summary>
        SpaceTraveler = 3,

        /// <summary>クリーム色のコック帽・コックコート。</summary>
        PastryChef = 4,

        /// <summary>黒い作業着と黄色の稲妻の線、ゴーグル、背中のバッテリー。</summary>
        Solderer = 5,

        /// <summary>黒い服とニット帽、目元の覆面、赤いスカーフ、腰の茶色い巾着袋。</summary>
        Thief = 6,

        /// <summary>苔色の戦車（履帯・車体・低い砲塔・短い砲身。箱そのものは見せない）。</summary>
        Juggernaut = 7,
    }

    /// <summary>
    /// キャラクター選択でカーソルを乗せている間のロゴの動き（D-89 の C3「小さな見せ場」。ユーザーの決定、2026-11-07）。キャラクターごとの
    /// コードは書かず、どの動きにするかをデータで持つ。
    /// </summary>
    public enum LogoMotion
    {
        /// <summary>動かない。</summary>
        None = 0,

        /// <summary>跳ねる（つぶれて伸びる）。</summary>
        Hop = 1,

        /// <summary>コインのように横に回る。</summary>
        Flip = 2,

        /// <summary>風に揺れる。</summary>
        Sway = 3,

        /// <summary>傾きながらふわりと浮く。</summary>
        Orbit = 4,

        /// <summary>ふくらんで縮む。</summary>
        Puff = 5,

        /// <summary>電気のようにちらつく。</summary>
        Zap = 6,

        /// <summary>左右をのぞく。</summary>
        Peek = 7,

        /// <summary>砲撃の反動。</summary>
        Recoil = 8,
    }

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
        [Tooltip("性能の種類を表すハッシュタグ（キャラクター選択。例: #高速）と、その英語（例: #Fast）")]
        [SerializeField] private string[] _tags = new string[0];
        [SerializeField] private string[] _tagsEn = new string[0];
        [Tooltip("特殊攻撃 1（Q）・特殊攻撃 2（E）・必殺技（R）の名前と説明（キャラクター選択）")]
        [SerializeField] private SkillText _special1Text = new SkillText();
        [SerializeField] private SkillText _special2Text = new SkillText();
        [SerializeField] private SkillText _superText = new SkillText();
        [Tooltip("仮の見た目（衣装。CostumeView）")]
        [SerializeField] private CharacterCostume _costume;
        [Tooltip("キャラクターの色（キャラクター選択の色の流れ込み・衝撃波。D-89）")]
        [SerializeField] private Color _themeColor = new Color(0.85f, 0.9f, 1f);
        [Tooltip("キャラクター選択でカーソルを乗せている間のロゴの動き（D-89 の C3）")]
        [SerializeField] private LogoMotion _logoMotion = LogoMotion.Hop;

        public CharacterConfig Config => _config;

        /// <summary>仮の見た目（衣装）。</summary>
        public CharacterCostume Costume => _costume;

        /// <summary>キャラクターの色（キャラクター選択の演出に使う）。</summary>
        public Color ThemeColor => _themeColor;

        /// <summary>キャラクター選択でカーソルを乗せている間のロゴの動き。</summary>
        public LogoMotion LogoMotion => _logoMotion;

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

        /// <summary>言語 language のハッシュタグ（英語がなければ日本語）。</summary>
        public string[] TagsIn(GameLanguage language)
        {
            return language == GameLanguage.English && _tagsEn != null && _tagsEn.Length > 0 ? _tagsEn : _tags;
        }

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

        /// <summary>シーンのビルダー用: ハッシュタグ（日本語と英語）と技の説明を設定する。</summary>
        public void SetProfile(string[] tags, string[] tagsEn, SkillText special1, SkillText special2, SkillText super)
        {
            _tags = tags ?? new string[0];
            _tagsEn = tagsEn ?? new string[0];
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

        /// <summary>シーンのビルダー用: 仮の見た目を設定する。</summary>
        public void SetCostume(CharacterCostume costume)
        {
            _costume = costume;
        }

        /// <summary>シーンのビルダー用: キャラクター選択での色とロゴの動きを設定する（D-89）。</summary>
        public void SetSelectLook(Color themeColor, LogoMotion logoMotion)
        {
            _themeColor = themeColor;
            _logoMotion = logoMotion;
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
