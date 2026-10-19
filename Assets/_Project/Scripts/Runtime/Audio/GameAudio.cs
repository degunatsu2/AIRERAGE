using System;
using System.Collections.Generic;
using Airerage.Runtime.Settings;
using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// BGM と効果音を鳴らす（仮の音。ユーザーの要望、2026-10-13）。音は ProceduralAudio がその場で合成する。
    /// 本番の音ができたら、_bgmOverride と _overrides に音声ファイルを設定すれば置き換わる（合成はしない）。
    /// 効果音は少数の AudioSource を順番に使い、重なっても切れないようにする。
    /// 音量はオプション（GameSettings の BGM と効果音。docs/15 T6-9）を掛ける。
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
        private float _bgmScale = 1f;
        private float _seScale = 1f;

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
            ApplySettings(GameSettings.Load());

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

        /// <summary>オプションの BGM と効果音の音量を当てる（docs/15 T6-9。全体の音量は AudioListener）。</summary>
        public void ApplySettings(GameSettings settings)
        {
            _bgmScale = settings.Bgm01;
            _seScale = settings.Se01;
            if (_bgm != null)
            {
                _bgm.volume = _bgmVolume * _bgmScale;
            }
        }

        /// <summary>今の BGM の音量（オプションを掛けたもの。テスト用）。</summary>
        public float BgmVolume => _bgm != null ? _bgm.volume : 0f;

        /// <summary>今の効果音の音量の倍率（オプション。テスト用）。</summary>
        public float SeScale => _seScale;

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
            source.PlayOneShot(clip, _seVolume * _seScale * volume);

            _history.Add(id);
            if (_history.Count > 64)
            {
                _history.RemoveAt(0);
            }
        }
    }
}
