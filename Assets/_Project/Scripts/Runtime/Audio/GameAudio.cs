using System;
using System.Collections.Generic;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// BGM と効果音を鳴らす（仮の音。ユーザーの要望、2026-10-13）。音は ProceduralAudio がその場で合成する。
    /// 本番の音ができたら、_bgmOverride と _overrides に音声ファイルを設定すれば置き換わる（合成はしない）。
    /// ステージに曲（StageDefinition.Music。docs/18 6 章）があれば、BGM はその曲（前奏を 1 回鳴らしてからループ。LoopingMusic）。
    /// その曲は 100 枚のコインの予告の後も鳴らし続ける（残り 1 分の版ができたら切り替える）。ステージは GameFlow の行き先から知る
    /// （App のシーンから始めたときだけ。検証用のシーンでは仮の BGM）。ステージの曲のファイルを読み込めていない（Git LFS のファイルを
    /// 取り込んでいない）ときは、エラーを出して仮の BGM を鳴らす（黙って無音にしない）。
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
        [Tooltip("ステージの曲の音量（メニューの曲と同じ。曲ごとの音量 MusicCue.Volume を掛ける）")]
        [Range(0f, 1f)] [SerializeField] private float _stageMusicVolume = 0.8f;
        [Range(0f, 1f)] [SerializeField] private float _seVolume = 0.7f;
        [SerializeField] private bool _playBgmOnStart = true;

        private readonly Dictionary<SoundId, AudioClip> _clips = new Dictionary<SoundId, AudioClip>();
        private readonly List<SoundId> _history = new List<SoundId>();
        private AudioSource _bgm;
        private MusicCue _stageCue;
        private LoopingMusic _stageMusic;
        private AudioClip _normalClip;
        private AudioClip _tenseClip;
        private AudioSource[] _se;
        private int _nextSe;
        private System.Random _pitchRandom = new System.Random(1);
        private float _bgmScale = 1f;
        private float _seScale = 1f;

        /// <summary>BGM の音源（仮の BGM。テスト用）。</summary>
        public AudioSource BgmSource => _bgm;

        /// <summary>ステージの曲（なければ null。仮の BGM を鳴らす）。</summary>
        public MusicCue StageMusic => _stageCue;

        /// <summary>ステージの曲を鳴らしているか（テスト用）。</summary>
        public bool StageMusicPlaying => _stageMusic != null && _stageMusic.Cue != null;

        /// <summary>これまでに鳴らした効果音（古い順。最後の 64 個まで。テストと確認用）。</summary>
        public IReadOnlyList<SoundId> History => _history;

        /// <summary>BGM を鳴らしているか（F9 で切り替える）。</summary>
        public bool BgmOn
        {
            get => _stageMusic != null ? StageMusicPlaying : _bgm != null && _bgm.isPlaying;
            set
            {
                if (_stageMusic != null)
                {
                    if (value && !StageMusicPlaying)
                    {
                        _stageMusic.Play(_stageCue);
                    }
                    else if (!value)
                    {
                        _stageMusic.Stop();
                    }
                    BgmEnabled = value;
                    return;
                }
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

        /// <summary>
        /// BGM を、ふつうのものと緊張感のあるものとで切り替える（鳴らす設定なら、頭から鳴らし直す）。ステージの曲は切り替えずに
        /// 鳴らし続ける（残り 1 分の版はまだない）。
        /// </summary>
        public void SetBgmTense(bool tense)
        {
            if (_bgm == null || tense == BgmTense)
            {
                return;
            }
            BgmTense = tense;
            if (_stageMusic != null)
            {
                return;
            }
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
            var flow = FindFirstObjectByType<GameFlow>();
            _stageCue = flow != null && flow.TargetStage != null ? flow.TargetStage.Music : null;
            if (_stageCue != null && !_stageCue.IsPlayable)
            {
                Debug.LogError($"{name}: ステージ {flow.TargetStage.DisplayName} の {_stageCue.UnplayableMessage}。代わりに仮の BGM を鳴らす", this);
                _stageCue = null;
            }
            if (_stageCue != null)
            {
                _stageMusic = new LoopingMusic(gameObject, this);
            }
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

        private void Update()
        {
            _stageMusic?.Update();
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
            if (_stageMusic != null)
            {
                _stageMusic.Volume = _stageMusicVolume * _bgmScale;
            }
        }

        /// <summary>今の BGM の音量（オプションを掛けたもの。ステージの曲なら曲ごとの音量も掛けたもの。テスト用）。</summary>
        public float BgmVolume => _stageMusic != null ? _stageMusic.Volume * _stageCue.Volume : _bgm != null ? _bgm.volume : 0f;

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
