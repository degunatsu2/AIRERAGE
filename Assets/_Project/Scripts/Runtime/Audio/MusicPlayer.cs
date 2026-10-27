using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>
    /// メニューの BGM（docs/18 6 章）。いつも読み込む App のシーンに置き、GameFlow がメニューへ向かうと曲を鳴らし、試合へ向かうと
    /// 小さくして止める（試合の BGM は試合のシーンの GameAudio が鳴らす）。メニューの中では、出している画面（MenuScreens.Music）で
    /// 曲を選ぶ: タイトル・メインメニュー・オプション・ネットワーク対戦の接続と待機所はタイトルの曲、キャラクター選択・CPU の強さ・
    /// ステージ選択は選択の曲。曲が変わるときは今の曲を小さくして止めてから、次の曲を頭から鳴らす（小さくしている途中で元の画面へ
    /// 戻ったら、止めずに大きさを戻す）。
    /// ループは 2 つの AudioSource を交互に PlayScheduled で予約して、サンプルの単位でつなぐ（前奏は 1 回だけ）。
    /// 音量はオプションの BGM の音量（GameSettings）を掛ける。
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        /// <summary>メニューで流す曲の種類（MenuScreens が今の画面から決める）。</summary>
        public enum MenuTrack
        {
            /// <summary>まだ画面を出していない（メニューのシーンを読み込んだ直後）。何も鳴らし始めない。</summary>
            None,
            Title,
            Select,
        }

        // 最初の予約までの余裕と、切り替わってから次の予約をするまでの待ち（秒）。
        private const double StartDelay = 0.1;
        private const double RescheduleAfter = 0.1;

        [Tooltip("タイトル・メインメニューの曲")]
        [SerializeField] private MusicCue _menuMusic;
        [Tooltip("キャラクター選択・CPU の強さ・ステージ選択の曲（なければタイトルの曲）")]
        [SerializeField] private MusicCue _selectMusic;
        [Range(0f, 1f)] [SerializeField] private float _volume = 0.8f;
        [SerializeField] private float _fadeOutSeconds = 0.8f;

        private AudioSource[] _sources;
        private GameFlow _flow;
        private MenuScreens _menus;
        private MusicCue _cue;
        private MusicCue _wanted;
        private int _playing;
        private double _nextSwitch;
        private bool _rescheduled;
        private float _scale = 1f;
        private float _fade = 1f;

        /// <summary>今鳴らしている曲（止めているあいだ、小さくして止める途中は null）。</summary>
        public MusicCue Current => _cue != null && _cue == _wanted ? _cue : null;

        /// <summary>タイトル・メインメニューの曲（テスト用）。</summary>
        public MusicCue MenuMusic => _menuMusic;

        /// <summary>キャラクター選択・ステージ選択の曲（テスト用）。</summary>
        public MusicCue SelectMusic => _selectMusic;

        /// <summary>曲を頭から鳴らした回数（テスト用。画面を移っても同じ曲なら続けて鳴らすことを確かめる）。</summary>
        public int PlayCount { get; private set; }

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
            _wanted = Wanted();
            // 違う曲にする（止める）ときは小さくしてから止める。元の曲に戻ったら大きさを戻す。
            bool leaving = _cue != null && _cue != _wanted;
            _fade = Mathf.MoveTowards(_fade, leaving ? 0f : 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, _fadeOutSeconds));
            if (leaving && _fade <= 0f)
            {
                Stop();
            }
            if (_cue == null && _wanted != null)
            {
                Play(_wanted);
            }
            ApplyVolume();
            KeepLooping();
        }

        // 今の画面で流したい曲（メニューの外と、メニューの画面を出す前は null）。
        private MusicCue Wanted()
        {
            if (_flow == null || _flow.Target != GameFlow.FlowScreen.Menu)
            {
                return null;
            }
            if (_menus == null)
            {
                // メニューのシーンを読み込んでいる途中は見つからない（試合から戻るたびに新しいものを探す）。
                _menus = FindFirstObjectByType<MenuScreens>();
            }
            MenuTrack track = _menus != null ? _menus.Music : MenuTrack.None;
            switch (track)
            {
                case MenuTrack.Title:
                    return _menuMusic;
                case MenuTrack.Select:
                    return _selectMusic != null ? _selectMusic : _menuMusic;
                default:
                    return null;
            }
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
            PlayCount++;
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
