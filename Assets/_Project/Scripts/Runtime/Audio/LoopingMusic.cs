using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// 1 曲（MusicCue）を、前奏（ループの始まりまで）を 1 回だけ鳴らしてから、ループの区間をくり返して鳴らす（docs/18 6 章）。
    /// 2 つの AudioSource を交互に PlayScheduled で予約して、サンプルの単位でつなぐ。毎フレーム Update を呼ぶと、鳴り終えたほうを
    /// 次のくり返しに予約し直す。メニューの曲（MusicPlayer）と試合のステージの曲（GameAudio）が使う。
    /// </summary>
    public sealed class LoopingMusic
    {
        // 最初の予約までの余裕と、切り替わってから次の予約をするまでの待ち（秒）。
        private const double StartDelay = 0.1;
        private const double RescheduleAfter = 0.1;

        private readonly AudioSource[] _sources = new AudioSource[2];
        private readonly Object _context;
        private int _playing;
        private double _nextSwitch;
        private bool _rescheduled;
        private float _volume = 1f;

        /// <summary>host に AudioSource を 2 つ足す（context はエラーの表示に使う）。</summary>
        public LoopingMusic(GameObject host, Object context)
        {
            _context = context;
            for (int i = 0; i < _sources.Length; i++)
            {
                _sources[i] = host.AddComponent<AudioSource>();
                _sources[i].playOnAwake = false;
                _sources[i].loop = false;
            }
        }

        /// <summary>鳴らしている曲（止めていれば null）。</summary>
        public MusicCue Cue { get; private set; }

        /// <summary>音量（曲ごとの音量 MusicCue.Volume は、ここに掛けて当てる）。</summary>
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                ApplyVolume();
            }
        }

        /// <summary>今の AudioSource の音量（曲ごとの音量を掛けたもの。テスト用）。</summary>
        public float SourceVolume => _sources[0].volume;

        /// <summary>曲を頭から鳴らす（前奏から）。ループの位置が曲の中になければ、エラーを出して曲の頭から終わりまでをくり返す。</summary>
        public void Play(MusicCue cue)
        {
            Stop();
            if (cue == null || cue.Clip == null)
            {
                return;
            }
            if (!cue.HasValidLoop)
            {
                Debug.LogError($"{cue.name} のループの位置が曲の中にない（始まり {cue.LoopStart}、終わり {cue.LoopEnd}、" +
                               $"曲の長さ {cue.Clip.samples} サンプル）。曲の頭から終わりまでをくり返す", _context);
            }
            Cue = cue;
            ApplyVolume();
            AudioClip clip = cue.Clip;
            LoopRange(cue, out int loopStart, out int loopEnd);
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
            Cue = null;
        }

        /// <summary>ループの終わりを過ぎたら（もう片方が鳴り始めたら）、空いたほうを次の終わりに予約する（毎フレーム呼ぶ）。</summary>
        public void Update()
        {
            if (Cue == null || Cue.Clip == null)
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
                AudioClip clip = Cue.Clip;
                LoopRange(Cue, out int loopStart, out int loopEnd);
                double length = (double)(loopEnd - loopStart) / clip.frequency;
                double next = _nextSwitch + length;
                Schedule(1 - _playing, clip, loopStart, next, next + length);
                _nextSwitch = next;
                _rescheduled = true;
            }
        }

        private static void LoopRange(MusicCue cue, out int loopStart, out int loopEnd)
        {
            loopStart = cue.HasValidLoop ? cue.LoopStart : 0;
            loopEnd = cue.HasValidLoop ? cue.LoopEnd : cue.Clip.samples;
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
            float volume = _volume * (Cue != null ? Cue.Volume : 1f);
            foreach (AudioSource source in _sources)
            {
                source.volume = volume;
            }
        }
    }
}
