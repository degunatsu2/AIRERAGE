using System.Collections.Generic;
using Airerage.Runtime.Settings;
using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// メニューの効果音（仮の音。ユーザーの要望、2026-10-16）: カーソルの移動（UiMove）、決定（UiConfirm）、一時停止を開く（UiPause）。
    /// いつも読み込んでおく App のシーンに 1 つ置く（決定した直後に画面のシーンを外しても、音が途切れないように）。検証用のシーンを
    /// 直接再生したときは、Find が今のシーンに作る。音は ProceduralAudio が合成する（差し替えの音声ファイルがあればそれを使う）。
    /// 音量はオプションの効果音の音量（GameSettings。docs/15 T6-9）を掛ける。
    /// </summary>
    public sealed class UiSounds : MonoBehaviour
    {
        private const int HistoryLimit = 32;

        [Range(0f, 1f)] [SerializeField] private float _volume = 0.6f;
        [Tooltip("差し替えの音（なければ合成した仮の音）")]
        [SerializeField] private AudioClip _moveOverride;
        [SerializeField] private AudioClip _confirmOverride;
        [SerializeField] private AudioClip _pauseOverride;

        private readonly Dictionary<SoundId, AudioClip> _clips = new Dictionary<SoundId, AudioClip>();
        private readonly List<SoundId> _history = new List<SoundId>();
        private AudioSource _source;
        private float _scale = 1f;

        /// <summary>これまでに鳴らした音（古い順。最後の 32 個まで。テスト用）。</summary>
        public IReadOnlyList<SoundId> History => _history;

        /// <summary>読み込んでいるシーンのメニューの効果音（なければ今のシーンに作る）。</summary>
        public static UiSounds Find()
        {
            var sounds = FindFirstObjectByType<UiSounds>();
            return sounds != null ? sounds : new GameObject("UiSounds").AddComponent<UiSounds>();
        }

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _clips[SoundId.UiMove] = _moveOverride != null ? _moveOverride : ProceduralAudio.CreateClip(SoundId.UiMove);
            _clips[SoundId.UiConfirm] = _confirmOverride != null ? _confirmOverride : ProceduralAudio.CreateClip(SoundId.UiConfirm);
            _clips[SoundId.UiPause] = _pauseOverride != null ? _pauseOverride : ProceduralAudio.CreateClip(SoundId.UiPause);
            ApplySettings(GameSettings.Load());
        }

        /// <summary>オプションの効果音の音量を当てる（docs/15 T6-9）。</summary>
        public void ApplySettings(GameSettings settings)
        {
            _scale = settings.Se01;
        }

        /// <summary>メニューの効果音を鳴らす（UiMove、UiConfirm、UiPause のどれか）。</summary>
        public void Play(SoundId id)
        {
            if (!_clips.TryGetValue(id, out AudioClip clip))
            {
                Debug.LogError($"{name}: メニューの効果音ではない（{id}）", this);
                return;
            }
            _source.PlayOneShot(clip, _volume * _scale);
            _history.Add(id);
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(0);
            }
        }
    }
}
