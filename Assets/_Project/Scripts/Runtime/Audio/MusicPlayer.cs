using System.Collections.Generic;
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
    /// ただし、キャラクター選択からメインメニューへ戻ったときだけは、タイトルの曲を止めた所の続きから、少しずつ大きくして鳴らす
    /// （ユーザーの要望、2026-11-07）。メニューの外へ出たときや、ほかの画面（ネットワーク対戦の待機所など）で頭から鳴らしたときは、
    /// 止めた所を忘れる。
    /// ループは LoopingMusic が、サンプルの単位でつなぐ（前奏は 1 回だけ）。音量はオプションの BGM の音量（GameSettings）を掛ける。
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

        [Tooltip("タイトル・メインメニューの曲")]
        [SerializeField] private MusicCue _menuMusic;
        [Tooltip("キャラクター選択・CPU の強さ・ステージ選択の曲（なければタイトルの曲）")]
        [SerializeField] private MusicCue _selectMusic;
        [Range(0f, 1f)] [SerializeField] private float _volume = 0.8f;
        [SerializeField] private float _fadeOutSeconds = 0.8f;

        private LoopingMusic _music;
        private GameFlow _flow;
        private MenuScreens _menus;
        private MusicCue _cue;
        private MusicCue _wanted;
        // 鳴らせないことを知らせた曲（毎フレーム同じエラーを出さない）。
        private readonly HashSet<MusicCue> _reported = new HashSet<MusicCue>();
        private float _scale = 1f;
        private float _fade = 1f;
        // キャラクター選択へ移るときにタイトルの曲を止めた位置（サンプル。なければ -1）と、選択の曲の画面のうち最後に出していたのが
        // キャラクター選択か。
        private int _resumeAt = -1;
        private bool _lastSelectWasCharacter;

        /// <summary>今鳴らしている曲（止めているあいだ、小さくして止める途中は null）。</summary>
        public MusicCue Current => _cue != null && _cue == _wanted ? _cue : null;

        /// <summary>タイトル・メインメニューの曲（テスト用）。</summary>
        public MusicCue MenuMusic => _menuMusic;

        /// <summary>キャラクター選択・ステージ選択の曲（テスト用）。</summary>
        public MusicCue SelectMusic => _selectMusic;

        /// <summary>曲を頭から鳴らした回数（テスト用。画面を移っても同じ曲なら続けて鳴らすことを確かめる）。</summary>
        public int PlayCount { get; private set; }

        /// <summary>タイトルの曲を続きから鳴らした回数（テスト用）。</summary>
        public int ResumeCount { get; private set; }

        /// <summary>最後に続きから鳴らした位置（サンプル。テスト用）。</summary>
        public int LastResumeFrom { get; private set; } = -1;

        /// <summary>今鳴っている位置（サンプル。止めていれば -1。テスト用）。</summary>
        public int Position => _music != null ? _music.Position : -1;

        private void Awake()
        {
            _music = new LoopingMusic(gameObject, this);
            ApplySettings(GameSettings.Load());
        }

        private void Update()
        {
            if (_flow == null)
            {
                _flow = FindFirstObjectByType<GameFlow>();
            }
            _wanted = Wanted();
            if (_wanted == null)
            {
                // メニューの外: 止めた所は忘れる。
                _resumeAt = -1;
            }
            else if (_menus != null && _menus.Music == MenuTrack.Select)
            {
                _lastSelectWasCharacter = _menus.ShowingCharacterSelect;
            }
            // 違う曲にする（止める）ときは小さくしてから止める。元の曲に戻ったら大きさを戻す。
            bool leaving = _cue != null && _cue != _wanted;
            _fade = Mathf.MoveTowards(_fade, leaving ? 0f : 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, _fadeOutSeconds));
            if (leaving && _fade <= 0f)
            {
                // タイトルの曲から選択の曲へ移るときは、止めた位置を覚える（キャラクター選択から戻ったら続きから鳴らす）。選択の曲を
                // 止めるときは、覚えた位置をそのまま残す。
                if (_cue == _menuMusic)
                {
                    _resumeAt = _wanted != null ? _music.Position : -1;
                }
                Stop();
            }
            if (_cue == null && _wanted != null)
            {
                if (_wanted == _menuMusic && _resumeAt >= 0 && _lastSelectWasCharacter && _menus != null && _menus.ShowingMain)
                {
                    Resume(_menuMusic, _resumeAt);
                }
                else
                {
                    if (_wanted == _menuMusic)
                    {
                        _resumeAt = -1;
                    }
                    Play(_wanted);
                }
            }
            ApplyVolume();
            _music.Update();
        }

        // 曲を from のサンプルから、小さい音から大きくしながら鳴らす（続きから）。
        private void Resume(MusicCue cue, int from)
        {
            Stop();
            if (cue == null || cue.Clip == null)
            {
                return;
            }
            _cue = cue;
            _fade = 0f;
            _resumeAt = -1;
            ResumeCount++;
            LastResumeFrom = from;
            ApplyVolume();
            _music.PlayFrom(cue, from);
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
                    return Playable(_menuMusic);
                case MenuTrack.Select:
                    MusicCue select = Playable(_selectMusic);
                    return select != null ? select : Playable(_menuMusic);
                default:
                    return null;
            }
        }

        // 鳴らせる曲ならそのまま、鳴らせない（曲のファイルを読み込めていない）ならエラーを 1 回出して null。
        private MusicCue Playable(MusicCue cue)
        {
            if (cue == null || cue.IsPlayable)
            {
                return cue;
            }
            if (_reported.Add(cue))
            {
                Debug.LogError($"{name}: {cue.UnplayableMessage}", this);
            }
            return null;
        }

        /// <summary>曲を頭から鳴らす（前奏から）。</summary>
        public void Play(MusicCue cue)
        {
            Stop();
            if (cue == null || cue.Clip == null)
            {
                return;
            }
            _cue = cue;
            _fade = 1f;
            PlayCount++;
            ApplyVolume();
            _music.Play(cue);
        }

        /// <summary>すぐに止める。</summary>
        public void Stop()
        {
            _music.Stop();
            _cue = null;
            _fade = 1f;
        }

        /// <summary>オプションの BGM の音量を当てる（docs/15 T6-9）。</summary>
        public void ApplySettings(GameSettings settings)
        {
            _scale = settings.Bgm01;
            ApplyVolume();
        }

        private void ApplyVolume()
        {
            if (_music != null)
            {
                _music.Volume = _volume * _scale * _fade;
            }
        }
    }
}
