using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// 1 曲の BGM（docs/18 6 章）: 曲のファイルと、ループの始まり・終わり（サンプル）。前奏（始まりまで）は 1 回だけ鳴らし、
    /// 終わりまで来たら始まりへ戻る。ループの位置は曲のファイルのタグ（LOOPSTART・LOOPLENGTH）から、ビルダーが入れる。
    /// </summary>
    [CreateAssetMenu(menuName = "Airerage/Music Cue", fileName = "MusicCue")]
    public sealed class MusicCue : ScriptableObject
    {
        [SerializeField] private AudioClip _clip;
        [Tooltip("ループの始まり（サンプル）")]
        [SerializeField] private int _loopStart;
        [Tooltip("ループの終わり（サンプル。0 なら曲の終わり）")]
        [SerializeField] private int _loopEnd;
        [Tooltip("曲ごとの音量（曲の大きさの違いをそろえる）")]
        [Range(0f, 1f)] [SerializeField] private float _volume = 1f;

        public AudioClip Clip => _clip;

        public int LoopStart => _loopStart;

        /// <summary>ループの終わり（サンプル。設定がなければ曲の終わり）。</summary>
        public int LoopEnd => _loopEnd > 0 || _clip == null ? _loopEnd : _clip.samples;

        public float Volume => _volume;

        /// <summary>
        /// 鳴らせるか（曲のファイルを読み込めているか）。曲のファイルは Git LFS なので、手もとで取り込んでいない（中身が LFS の目印の
        /// 小さなファイルのまま）と、Unity が音として読み込めず、曲がないことになる。
        /// </summary>
        public bool IsPlayable => _clip != null && _clip.loadState != AudioDataLoadState.Failed;

        /// <summary>鳴らせないときのエラーの文。</summary>
        public string UnplayableMessage =>
            $"{name} の曲を鳴らせない（曲のファイルを読み込めていない。曲のファイルは Git LFS なので、手もとで git lfs pull をしてから " +
            "Unity を開き直す。Unity のコンソールに曲のファイルの読み込みのエラーが出ていないかも確かめる）";

        /// <summary>ループの位置が曲の中に収まっているか。</summary>
        public bool HasValidLoop => _clip != null && _loopStart >= 0 && LoopEnd > _loopStart && LoopEnd <= _clip.samples;

        /// <summary>ビルダー用の設定。</summary>
        public void Configure(AudioClip clip, int loopStart, int loopEnd, float volume)
        {
            _clip = clip;
            _loopStart = loopStart;
            _loopEnd = loopEnd;
            _volume = volume;
        }
    }
}
