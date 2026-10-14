using Degunatsu.Core.Economy;
using Degunatsu.Core.Match;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 試合の終盤の雰囲気（ユーザーの要望、2026-10-14）: 100 枚のコインの予告（残り 1 分）から試合の終わりまで、BGM を緊張感の
    /// あるものに変え、背景の空を少し赤くする。予告の瞬間に警報を鳴らす。表示だけで、状態は書き換えない。
    /// 予告かどうかはティック数とルールだけから決まる（FieldCoinRules.JackpotAnnounceTick）ので、巻き戻しても変わらない。
    /// 再戦でティックが戻れば、ふつうに戻る。
    /// 空は、シーンの空のマテリアル（手続き型の空の _SkyTint）を写したものを使う（元のアセットを書き換えない）。
    /// </summary>
    public sealed class MatchAtmosphere : MonoBehaviour
    {
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("BGM の切り替えと警報（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("赤くした空の色")]
        [SerializeField] private Color _tenseSkyTint = new Color(0.85f, 0.3f, 0.25f);
        [Tooltip("どれだけ赤くするか（0〜1）")]
        [Range(0f, 1f)] [SerializeField] private float _tenseAmount = 0.65f;
        [Tooltip("空の色が変わりきるまでの秒数")]
        [SerializeField] private float _transitionSeconds = 2.5f;
        [Tooltip("予告の瞬間から、これだけ遅れて表示を始めたときは警報を鳴らさない（ティック。途中から見始めたとき）")]
        [SerializeField] private int _alarmWindowTicks = 30;

        private ISimulationFeed _feed;
        private Material _originalSky;
        private Material _sky;
        private Color _normalTint;

        /// <summary>今、終盤の雰囲気か（テスト用）。</summary>
        public bool Tense { get; private set; }

        /// <summary>空の赤さ（0〜1。テスト用）。</summary>
        public float SkyBlend { get; private set; }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
                return;
            }
            _originalSky = RenderSettings.skybox;
            if (_originalSky != null && _originalSky.HasProperty(SkyTintId))
            {
                _sky = new Material(_originalSky) { name = _originalSky.name + " (Atmosphere)" };
                _normalTint = _sky.GetColor(SkyTintId);
                RenderSettings.skybox = _sky;
            }
        }

        private void OnDestroy()
        {
            if (_sky != null)
            {
                if (RenderSettings.skybox == _sky)
                {
                    RenderSettings.skybox = _originalSky;
                }
                Destroy(_sky);
            }
        }

        private void Update()
        {
            MatchRules rules = _feed.Rules;
            int tick = _feed.CurrentTick;
            int announce = rules != null ? FieldCoinRules.JackpotAnnounceTick(rules) : -1;
            bool tense = announce >= 0 && tick >= announce;
            if (tense != Tense)
            {
                Tense = tense;
                if (_audio != null)
                {
                    _audio.SetBgmTense(tense);
                    if (tense && tick - announce <= _alarmWindowTicks)
                    {
                        _audio.Play(SoundId.JackpotAlarm);
                    }
                }
            }

            float target = tense ? 1f : 0f;
            SkyBlend = Mathf.MoveTowards(SkyBlend, target, Time.deltaTime / Mathf.Max(0.05f, _transitionSeconds));
            if (_sky != null)
            {
                _sky.SetColor(SkyTintId, Color.Lerp(_normalTint, _tenseSkyTint, SkyBlend * _tenseAmount));
            }
        }
    }
}
