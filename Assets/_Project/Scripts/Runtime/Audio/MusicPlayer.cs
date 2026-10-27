using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// メニューの BGM（docs/18 6 章）。いつも読み込む App のシーンに置き、GameFlow がメニューへ向かうと曲を鳴らし、試合へ向かうと
    /// 小さくして止める（試合の BGM は試合のシーンの GameAudio が鳴らす）。今はタイトルの曲を、タイトルからステージ選択まで鳴らす。
    /// ループは 2 つの AudioSource を交互に PlayScheduled で予約して、サンプルの単位でつなぐ（前奏は 1 回だけ）。
    /// 音量はオプションの BGM の音量（GameSettings）を掛ける。
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        // 最初の予約までの余裕と、切り替わってから次の予約をするまでの待ち（秒）。
        private const double StartDelay = 0.1;
        private const double RescheduleAfter = 0.1;

        [Tooltip("メニュー（タイトル・メインメニュー・選択の画面）の曲")]
        [SerializeField] private MusicCue _menuMusic;
        [Range(0f, 1f)] [SerializeField] private float _volume = 0.8f;
        [SerializeField] private float _fadeOutSeconds = 0.8f;

        private AudioSource[] _sources;
        private GameFlow _flow;
        private MusicCue _cue;
        private int _playing;
        private double _nextSwitch;
        private bool _rescheduled;
        private float _scale = 1f;
        private float _fade = 1f;
        private bool _fadingOut;

        /// <summary>今鳴らしている曲（止めているあいだ、小さくして止める途中は null）。</summary>
        public MusicCue Current => _fadingOut ? null : _cue;

        /// <summary>メニューの曲（テスト用）。</summary>
        public MusicCue MenuMusic => _menuMusic;

        private void Awake()
        {
            _sources = new AudioSource[2];
            for (int i = 0; i < _sources.Length; i++)
            {
                _sources[i] = gameObject.AddComponent<AudioSource>();
                _sources[i].playOnAwake = false;
                _sources[i].loop = false;
            }
            ApplySettings(GameSettings.Load());
        }

        private void Update()
        {
            if (_flow == null)
            {
                _flow = FindFirstObjectByType<GameFlow>();
            }
            MusicCue wanted = _flow != null && _flow.Target == GameFlow.FlowScreen.Menu ? _menuMusic : null;
            if (wanted != Current)
            {
                if (wanted == null)
                {
                    _fadingOut = _cue != null;
                }
                else
                {
                    Play(wanted);
                }
            }
            if (_fadingOut)
            {
                _fade -= Time.unscaledDeltaTime / Mathf.Max(0.01f, _fadeOutSeconds);
                if (_fade <= 0f)
                {
                    Stop();
                }
                ApplyVolume();
                return;
            }
            KeepLooping();
        }

        /// <summary>曲を頭から鳴らす（前奏から）。</summary>
        public void Play(MusicCue cue)
        {
            Stop();
            if (cue == null || cue.Clip == null)
            {
                return;
            }
            if (!cue.HasValidLoop)
            {
                Debug.LogError($"{name}: {cue.name} のループの位置が曲の中にない（始まり {cue.LoopStart}、終わり {cue.LoopEnd}、" +
                               $"曲の長さ {cue.Clip.samples} サンプル）。曲の頭から終わりまでをくり返す", this);
            }
            _cue = cue;
            _fade = 1f;
            _fadingOut = false;
            ApplyVolume();
            AudioClip clip = cue.Clip;
            int loopStart = cue.HasValidLoop ? cue.LoopStart : 0;
            int loopEnd = cue.HasValidLoop ? cue.LoopEnd : clip.samples;
            double start = AudioSettings.dspTime + StartDelay;
            double firstEnd = start + (double)loopEnd / clip.frequency;
            Schedule(0, clip, 0, start, firstEnd);
            Schedule(1, clip, loopStart, firstEnd, firstEnd + (double)(loopEnd - loopStart) / clip.frequency);
            _playing = 0;
            _nextSwitch = firstEnd;
            _rescheduled = true;
        }

        /// <summary>すぐに止める。</summary>
        public void Stop()
        {
            foreach (AudioSource source in _sources)
            {
                source.Stop();
                source.clip = null;
            }
            _cue = null;
            _fadingOut = false;
            _fade = 1f;
        }

        /// <summary>オプションの BGM の音量を当てる（docs/15 T6-9）。</summary>
        public void ApplySettings(GameSettings settings)
        {
            _scale = settings.Bgm01;
            ApplyVolume();
        }

        // ループの終わりを過ぎたら（もう片方が鳴り始めたら）、空いたほうを次の終わりに予約する。
        private void KeepLooping()
        {
            if (_cue == null || _cue.Clip == null)
            {
                return;
            }
            double now = AudioSettings.dspTime;
            if (_rescheduled && now >= _nextSwitch)
            {
                _playing = 1 - _playing;
                _rescheduled = false;
            }
            if (!_rescheduled && now >= _nextSwitch + RescheduleAfter)
            {
                AudioClip clip = _cue.Clip;
                int loopStart = _cue.HasValidLoop ? _cue.LoopStart : 0;
                int loopEnd = _cue.HasValidLoop ? _cue.LoopEnd : clip.samples;
                double length = (double)(loopEnd - loopStart) / clip.frequency;
                double next = _nextSwitch + length;
                Schedule(1 - _playing, clip, loopStart, next, next + length);
                _nextSwitch = next;
                _rescheduled = true;
            }
        }

        private void Schedule(int index, AudioClip clip, int fromSample, double startAt, double endAt)
        {
            AudioSource source = _sources[index];
            source.clip = clip;
            source.timeSamples = fromSample;
            source.PlayScheduled(startAt);
            source.SetScheduledEndTime(endAt);
        }

        private void ApplyVolume()
        {
            if (_sources == null)
            {
                return;
            }
            float volume = _volume * _scale * _fade * (_cue != null ? _cue.Volume : 1f);
            foreach (AudioSource source in _sources)
            {
                source.volume = volume;
            }
        }
    }
}
