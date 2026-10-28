using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// 1 曲（MusicCue）を、前奏（ループの始まりまで）を 1 回だけ鳴らしてから、ループの区間をくり返して鳴らす（docs/18 6 章）。
    /// 2 つの AudioSource を交互に PlayScheduled で予約して、サンプルの単位でつなぐ。毎フレーム Update を呼ぶと、鳴り終えたほうを
    /// 次のくり返しに予約し直す。メニューの曲（MusicPlayer）と試合のステージの曲（GameAudio）が使う。
    /// 速さ（Speed。AudioSource.pitch なので、テープを速く回したように音の高さも上がる）を変えると、鳴っている途中の位置から、もう片方の
    /// AudioSource が新しい速さで引き継ぐ（docs/18 4.12。COIN RUSH で 1.2 倍）。鳴っている位置は、予約した時刻と速さから計算で求める
    /// （音の処理から位置を読まないので、つなぎ目がずれない）。次のつなぎ目が近いときは、つなぎ目から新しい速さにする。
    /// </summary>
    public sealed class LoopingMusic
    {
        /// <summary>1 つの AudioSource に予約した区間（テストと確認用）。</summary>
        public sealed class Part
        {
            /// <summary>どちらの AudioSource か（0 か 1）。</summary>
            public int Source { get; internal set; }

            /// <summary>予約した時刻（AudioSettings.dspTime）。</summary>
            public double ScheduledAt { get; internal set; }

            /// <summary>鳴り始める時刻。</summary>
            public double Start { get; internal set; }

            /// <summary>鳴り終える時刻（途中で引き継いだら、引き継いだ時刻）。</summary>
            public double End { get; internal set; }

            /// <summary>鳴り始めるサンプルの位置。</summary>
            public int From { get; internal set; }

            /// <summary>速さ（AudioSource.pitch）。</summary>
            public float Speed { get; internal set; }

            /// <summary>time に鳴っているサンプルの位置（frequency は曲のサンプリング周波数）。</summary>
            public double PositionAt(double time, int frequency) => From + (time - Start) * frequency * Speed;
        }

        // 鳴らし始めるまでの既定の余裕、切り替わってから次の予約をするまでの待ち、予約し直してよい鳴り始めるまでの最小の余裕（秒）。
        private const double StartDelay = 0.1;
        private const double RescheduleAfter = 0.1;
        private const double MinLead = 0.05;
        // 速さを変えたとき、途中から引き継ぐのは、次のつなぎ目までこれより長いとき（短いと、引き継いだ区間がすぐ終わり、次の予約が
        // 間に合わない）。それより近ければ、次のつなぎ目から新しい速さにする（秒）。
        private const double SpliceLead = 1.0;
        private const int MaxParts = 16;

        private readonly AudioSource[] _sources = new AudioSource[2];
        private readonly Part[] _parts = new Part[2];
        private readonly List<Part> _log = new List<Part>();
        private readonly Object _context;
        private readonly Func<double> _clock;
        private int _playing;
        // もう片方を、鳴っているほうの終わりに予約したか。
        private bool _queued;
        private float _volume = 1f;
        private float _speed = 1f;

        /// <summary>host に AudioSource を 2 つ足す（context はエラーの表示に使う。clock はテスト用で、なければ AudioSettings.dspTime）。</summary>
        public LoopingMusic(GameObject host, Object context, Func<double> clock = null)
        {
            _context = context;
            _clock = clock ?? (() => AudioSettings.dspTime);
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

        /// <summary>
        /// 速さ（1 で元の速さ。音の高さも同じ割合で変わる）。鳴らしている途中で変えると、すぐに（次のつなぎ目が近ければ、つなぎ目から）
        /// 新しい速さになる。止めている間に変えれば、次に鳴らすときからその速さ。
        /// </summary>
        public float Speed
        {
            get => _speed;
            set
            {
                if (!(value > 0f && value <= 3f))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "曲の速さは 0 より大きく 3 以下（AudioSource.pitch の範囲）");
                }
                _speed = value;
                if (Cue != null && Cue.Clip != null)
                {
                    ApplySpeed(_clock());
                }
            }
        }

        /// <summary>今の AudioSource の音量（曲ごとの音量を掛けたもの。テスト用）。</summary>
        public float SourceVolume => _sources[0].volume;

        /// <summary>予約した区間（古い順。最後の 16 個まで。取り消した予約は除く。テストと確認用）。</summary>
        public IReadOnlyList<Part> Parts => _log;

        /// <summary>
        /// 曲を頭から鳴らす（前奏から）。ループの位置が曲の中になければ、エラーを出して曲の頭から終わりまでをくり返す。
        /// delay は鳴らし始めるまでの余裕（秒）。予約が音の処理に届くのが予約の時刻より遅れると、前奏が遅れて始まり、最初のつなぎ目で
        /// その分が飛ぶ（長いフレームで起きた。docs/18 4.12）ので、長くなりそうなフレーム（シーンを読み込んだ直後）では長めにする。
        /// </summary>
        public void Play(MusicCue cue, double delay = StartDelay)
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
            LoopRange(cue, out int loopStart, out _);
            Part intro = Queue(0, 0, _clock() + delay);
            Queue(1, loopStart, intro.End);
            _playing = 0;
            _queued = true;
        }

        /// <summary>すぐに止める。</summary>
        public void Stop()
        {
            foreach (AudioSource source in _sources)
            {
                source.Stop();
                source.clip = null;
            }
            _parts[0] = null;
            _parts[1] = null;
            _log.Clear();
            _playing = 0;
            _queued = false;
            Cue = null;
        }

        /// <summary>
        /// ループの終わりを過ぎたら（もう片方が鳴り始めたら）、空いたほうを次の終わりに予約する。速さを変えた後で、まだ引き継げて
        /// いなければ引き継ぐ（毎フレーム呼ぶ）。
        /// </summary>
        public void Update()
        {
            if (Cue == null || Cue.Clip == null)
            {
                return;
            }
            double now = _clock();
            if (_queued && now >= _parts[1 - _playing].Start)
            {
                _playing = 1 - _playing;
                _queued = false;
            }
            Part playing = _parts[_playing];
            if (!_queued && now >= playing.Start + RescheduleAfter)
            {
                LoopRange(Cue, out int loopStart, out _);
                Queue(1 - _playing, loopStart, playing.End);
                _queued = true;
            }
            ApplySpeed(now);
        }

        // 予約した区間の速さを、今の速さ（_speed）にそろえる。もう片方の AudioSource が空いていない（鳴り終えたばかり・もうすぐ
        // 鳴り始める）ときは何もしない（次の Update でもう一度試す）。
        private void ApplySpeed(double now)
        {
            Part playing = _parts[_playing];
            Part next = _queued ? _parts[1 - _playing] : null;
            if (playing.Start > now)
            {
                // まだ鳴り始めていない（鳴らした直後）。間に合うなら、同じ時刻から今の速さで予約し直す（間に合わなければ、鳴り始めて
                // から引き継ぐ）。
                if (playing.Speed != _speed && playing.Start - now > MinLead)
                {
                    LoopRange(Cue, out int loopStart, out _);
                    Part first = Queue(_playing, playing.From, playing.Start);
                    Queue(1 - _playing, loopStart, first.End);
                }
                return;
            }
            double at = now + StartDelay;
            if (next == null || next.Start <= at)
            {
                return;
            }
            if (playing.Speed != _speed && next.Start - now > SpliceLead)
            {
                Splice(at);
            }
            else if (next.Speed != _speed)
            {
                Queue(1 - _playing, next.From, next.Start);
            }
        }

        // 鳴っているほうを at で止め、もう片方が at に鳴っているはずの位置から、今の速さで引き継ぐ。
        private void Splice(double at)
        {
            Part playing = _parts[_playing];
            int from = (int)Math.Round(playing.PositionAt(at, Cue.Clip.frequency));
            playing.End = at;
            _sources[_playing].SetScheduledEndTime(at);
            Queue(1 - _playing, from, at);
        }

        private static void LoopRange(MusicCue cue, out int loopStart, out int loopEnd)
        {
            loopStart = cue.HasValidLoop ? cue.LoopStart : 0;
            loopEnd = cue.HasValidLoop ? cue.LoopEnd : cue.Clip.samples;
        }

        // index の AudioSource を、from のサンプルから startAt に今の速さで鳴らし、ループの終わりで止める予約をする（前の予約は取り消す）。
        private Part Queue(int index, int from, double startAt)
        {
            AudioClip clip = Cue.Clip;
            LoopRange(Cue, out _, out int loopEnd);
            double now = _clock();
            var part = new Part
            {
                Source = index,
                ScheduledAt = now,
                Start = startAt,
                End = startAt + (loopEnd - from) / ((double)clip.frequency * _speed),
                From = from,
                Speed = _speed,
            };
            Part old = _parts[index];
            if (old != null && old.Start > now)
            {
                _log.Remove(old);
            }
            _parts[index] = part;
            _log.Add(part);
            if (_log.Count > MaxParts)
            {
                _log.RemoveAt(0);
            }

            AudioSource source = _sources[index];
            source.Stop();
            source.clip = clip;
            source.pitch = _speed;
            source.timeSamples = from;
            source.PlayScheduled(startAt);
            source.SetScheduledEndTime(part.End);
            return part;
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
