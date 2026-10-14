using System;
using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// BGM と効果音を鳴らす（仮の音。ユーザーの要望、2026-10-13）。音は ProceduralAudio がその場で合成する。
    /// 本番の音ができたら、_bgmOverride と _overrides に音声ファイルを設定すれば置き換わる（合成はしない）。
    /// 効果音は少数の AudioSource を順番に使い、重なっても切れないようにする。
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private const int SePoolSize = 8;

        [Serializable]
        public sealed class ClipOverride
        {
            public SoundId Id;
            public AudioClip Clip;
        }

        [Tooltip("BGM（空なら合成した仮の BGM）")]
        [SerializeField] private AudioClip _bgmOverride;
        [Tooltip("緊張感のある BGM（100 枚のコインの予告から。なければ合成する）")]
        [SerializeField] private AudioClip _tenseBgmOverride;
        [Tooltip("効果音の差し替え（設定がない効果音は合成する）")]
        [SerializeField] private ClipOverride[] _overrides = new ClipOverride[0];
        [Range(0f, 1f)] [SerializeField] private float _bgmVolume = 0.3f;
        [Range(0f, 1f)] [SerializeField] private float _seVolume = 0.7f;
        [SerializeField] private bool _playBgmOnStart = true;

        private readonly Dictionary<SoundId, AudioClip> _clips = new Dictionary<SoundId, AudioClip>();
        private readonly List<SoundId> _history = new List<SoundId>();
        private AudioSource _bgm;
        private AudioClip _normalClip;
        private AudioClip _tenseClip;
        private AudioSource[] _se;
        private int _nextSe;
        private System.Random _pitchRandom = new System.Random(1);

        /// <summary>BGM の音源（テスト用）。</summary>
        public AudioSource BgmSource => _bgm;

        /// <summary>これまでに鳴らした効果音（古い順。最後の 64 個まで。テストと確認用）。</summary>
        public IReadOnlyList<SoundId> History => _history;

        /// <summary>BGM を鳴らしているか（F9 で切り替える）。</summary>
        public bool BgmOn
        {
            get => _bgm != null && _bgm.isPlaying;
            set
            {
                if (_bgm == null)
                {
                    return;
                }
                if (value && !_bgm.isPlaying)
                {
                    _bgm.Play();
                }
                else if (!value && _bgm.isPlaying)
                {
                    _bgm.Stop();
                }
                BgmEnabled = value;
            }
        }

        /// <summary>BGM を鳴らす設定か（音の出る機器がない環境では isPlaying が false のままのことがあるので、設定を別に持つ）。</summary>
        public bool BgmEnabled { get; private set; }

        /// <summary>緊張感のある BGM にしているか（MatchAtmosphere が切り替える）。</summary>
        public bool BgmTense { get; private set; }

        /// <summary>BGM を、ふつうのものと緊張感のあるものとで切り替える（鳴らす設定なら、頭から鳴らし直す）。</summary>
        public void SetBgmTense(bool tense)
        {
            if (_bgm == null || tense == BgmTense)
            {
                return;
            }
            BgmTense = tense;
            _bgm.clip = tense ? _tenseClip : _normalClip;
            if (BgmEnabled)
            {
                _bgm.Play();
            }
        }

        private void Awake()
        {
            foreach (ClipOverride entry in _overrides)
            {
                if (entry != null && entry.Clip != null)
                {
                    _clips[entry.Id] = entry.Clip;
                }
            }
            foreach (SoundId id in (SoundId[])Enum.GetValues(typeof(SoundId)))
            {
                if (!_clips.ContainsKey(id))
                {
                    _clips[id] = ProceduralAudio.CreateClip(id);
                }
            }

            _bgm = gameObject.AddComponent<AudioSource>();
            _normalClip = _bgmOverride != null ? _bgmOverride : ProceduralAudio.CreateBgm();
            _tenseClip = _tenseBgmOverride != null ? _tenseBgmOverride : ProceduralAudio.CreateTenseBgm();
            _bgm.clip = _normalClip;
            _bgm.loop = true;
            _bgm.playOnAwake = false;
            _bgm.volume = _bgmVolume;

            _se = new AudioSource[SePoolSize];
            for (int i = 0; i < _se.Length; i++)
            {
                _se[i] = gameObject.AddComponent<AudioSource>();
                _se[i].playOnAwake = false;
            }
        }

        private void Start()
        {
            BgmOn = _playBgmOnStart;
        }

        /// <summary>効果音を鳴らす。pitchJitter は音の高さの揺らぎ（0.05 なら ±5%。同じ音の連続を単調にしない）。</summary>
        public void Play(SoundId id, float volume = 1f, float pitchJitter = 0f)
        {
            if (_se == null || !_clips.TryGetValue(id, out AudioClip clip))
            {
                return;
            }
            AudioSource source = _se[_nextSe];
            _nextSe = (_nextSe + 1) % _se.Length;
            source.pitch = 1f + pitchJitter * (float)(_pitchRandom.NextDouble() * 2.0 - 1.0);
            source.PlayOneShot(clip, _seVolume * volume);

            _history.Add(id);
            if (_history.Count > 64)
            {
                _history.RemoveAt(0);
            }
        }
    }
}
