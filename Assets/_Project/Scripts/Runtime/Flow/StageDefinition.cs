using Airerage.Runtime.Audio;
using Airerage.Runtime.Settings;
using UnityEngine;

namespace Airerage.Runtime.Flow
{
    /// <summary>
    /// ステージ 1 つ（docs/15 T6-2）。ステージのシーン（地形、ギミック、出現位置、コインの出現地点、拠点の候補、撃破の範囲、光）の名前と、
    /// 選択の画面に出す名前と説明（英語と日本語。オプションの LANGUAGE で切り替える。2026-10-27）、試合で鳴らす曲（docs/18 6 章。
    /// なければ仮の BGM）。試合の仕組み（実行、表示）は別のシーンにあり、ステージのシーンに重ねて読み込む（D-66）。
    /// </summary>
    [CreateAssetMenu(menuName = "Airerage/Stage", fileName = "Stage")]
    public sealed class StageDefinition : ScriptableObject
    {
        [Tooltip("選択の画面に出す名前")]
        [SerializeField] private string _displayName = "STAGE";
        [Tooltip("ステージのシーンの名前（ビルドの設定のシーンの一覧に入れる）")]
        [SerializeField] private string _sceneName = string.Empty;
        [Tooltip("選択の画面に出す説明（英語）")]
        [SerializeField] private string _description = string.Empty;
        [Tooltip("選択の画面に出す説明（日本語）")]
        [SerializeField] private string _descriptionJa = string.Empty;
        [Tooltip("選択の画面の色")]
        [SerializeField] private Color _color = new Color(0.35f, 0.85f, 1f);
        [Tooltip("試合で鳴らす曲（なければ仮の BGM を合成して鳴らす）")]
        [SerializeField] private MusicCue _music;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public string SceneName => _sceneName;
        public string Description => _description;
        public string DescriptionJa => _descriptionJa;
        public Color Color => _color;
        public MusicCue Music => _music;

        /// <summary>言語 language の説明（日本語の説明がなければ英語）。</summary>
        public string DescriptionIn(GameLanguage language)
        {
            return language == GameLanguage.Japanese && !string.IsNullOrEmpty(_descriptionJa) ? _descriptionJa : _description;
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(string displayName, string sceneName, string description, string descriptionJa, Color color)
        {
            _displayName = displayName;
            _sceneName = sceneName;
            _description = description;
            _descriptionJa = descriptionJa;
            _color = color;
        }

        /// <summary>試合で鳴らす曲を設定する（シーンのビルダー用。null なら仮の BGM）。</summary>
        public void SetMusic(MusicCue music)
        {
            _music = music;
        }
    }
}
