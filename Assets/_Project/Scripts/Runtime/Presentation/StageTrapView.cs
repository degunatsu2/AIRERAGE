using System;
using System.Collections.Generic;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ステージの仕掛け（StageTrapAuthoring。ユーザーの要望、2026-10-26）の見た目（表示だけ。状態を読むだけで、書き換えない）。
    /// - 赤外線とミサイル: 半透明の赤い赤外線（_beam）がゆっくり明滅し、触れられると予告の間は赤外線の上に黄色の斜線と警告の記号
    ///   （WarningOverlay）と発射装置のランプの点滅、警報。撃ったらミサイル（_projectile）をレーダーに沿って動かし、当たったら消す。
    ///   撃ってからまた触れられるようになるまで（待ち）は、赤外線を消して、発射装置の周りに残りの円形のゲージ（_reload）を出す
    ///   （ユーザーの要望、2026-10-26）。
    /// - ピッチャーマシン: ボール（_projectile）を道のりの位置へ動かし、止まったら点滅させ、消えたら隠す。打ち出した瞬間に音。
    /// 表示する試合がなければ（メニューの背景）、赤外線とマシンだけを出す。
    /// 2026-11-21 の方針（実際の現象に寄せる）: 赤外線は向かい合った投光器と受光器の間の細い 2 本の光線（_beams。本物の赤外線は目に
    /// 見えないので、ゲームの警報の光線のように細い赤い線で見せ、光線の上で塵がきらめく _motes）。ミサイルは尾に炎と、後ろに残る
    /// 白い煙の帯（_trail）。ピッチャーマシンは上下 2 つのタイヤが逆向きに回り続け（_wheels）、打ち出す少し前に、かごのボールが
    /// 送りの筒を転がってタイヤの間へ入る（_feedBall・_chute）。
    /// </summary>
    [RequireComponent(typeof(StageTrapAuthoring))]
    public sealed class StageTrapView : MonoBehaviour
    {
        private const float BlinkPerSecond = 6f;

        [Tooltip("赤外線の見た目（半透明の板）")]
        [SerializeField] private Renderer _beam;
        [Tooltip("ミサイルかボール（動かす見た目）")]
        [SerializeField] private Transform _projectile;
        [Tooltip("予告の間に点滅させるランプ")]
        [SerializeField] private Renderer _lamp;
        [Tooltip("予告の表示に使う線の材質")]
        [SerializeField] private Material _lineMaterial;
        [Tooltip("撃った後の待ちの円形のゲージ（発射装置の周り）と、その中心（発射装置）")]
        [SerializeField] private RingGaugeView _reload;
        [SerializeField] private Transform _launcher;
        [Tooltip("赤外線の光線（2 本目以降。1 本目は _beam）と、光線できらめく塵、ミサイルの煙の帯")]
        [SerializeField] private Renderer[] _beams = new Renderer[0];
        [SerializeField] private ParticleSystem _motes;
        [SerializeField] private ParticleSystem _trail;
        [Tooltip("ミサイルの尾（煙を出す所。ミサイルの子）")]
        [SerializeField] private Transform _tail;
        [Tooltip("ピッチャーマシンのタイヤ（Z の軸で回す。上と下は逆向き）と、送るボール、送りの筒の道（仕掛けからの位置）")]
        [SerializeField] private Transform[] _wheels = new Transform[0];
        [SerializeField] private Transform _feedBall;
        [SerializeField] private Vector3[] _chute = new Vector3[0];

        /// <summary>送りの筒をボールが転がる長さ（ティック）。</summary>
        public const int FeedTicks = 40;

        private StageTrapAuthoring _authoring;
        private StageTrap _trap;
        private ISimulationFeed _feed;
        private GameAudio _audio;
        private float _nextFeedSearch;
        private int _index = -1;
        private IReadOnlyList<StageTrap> _indexedIn;
        private WarningOverlay _overlay;
        private bool _wasArming;
        private bool _wasFlying;
        private int _lastAge = int.MaxValue;
        private readonly List<Rect> _areas = new List<Rect>();
        private readonly List<Vector2> _icons = new List<Vector2>();

        /// <summary>予告を見せているか（テスト用）。</summary>
        public bool ShowsWarning { get; private set; }

        /// <summary>ミサイル・ボールを見せているか（テスト用）。</summary>
        public bool ShowsProjectile => _projectile != null && _projectile.gameObject.activeSelf;

        /// <summary>ボールを点滅させているか（テスト用）。</summary>
        public bool Blinking { get; private set; }

        /// <summary>撃った後の待ちで、赤外線を消して円形のゲージを出しているか（テスト用）。</summary>
        public bool Reloading { get; private set; }

        /// <summary>赤外線を見せているか（明滅で消えている瞬間も含む。待ちの間は false。テスト用）。</summary>
        public bool ShowsBeam => _beam != null && !Reloading;

        /// <summary>送りの筒を転がっているか（テスト用）。</summary>
        public bool Feeding { get; private set; }

        /// <summary>ミサイルの煙の帯を出しているか（テスト用）。</summary>
        public bool Trailing => _trail != null && _trail.emission.enabled;

        /// <summary>赤外線の細部（2 本目以降の光線・塵・煙の帯と尾）を設定する（シーンのビルダー用）。</summary>
        public void ConfigureTripwireDetail(Renderer[] beams, ParticleSystem motes, ParticleSystem trail, Transform tail)
        {
            _beams = beams;
            _motes = motes;
            _trail = trail;
            _tail = tail;
        }

        /// <summary>ピッチャーマシンの細部（タイヤ・送るボール・送りの筒の道）を設定する（シーンのビルダー用）。</summary>
        public void ConfigurePitcherDetail(Transform[] wheels, Transform feedBall, Vector3[] chute)
        {
            _wheels = wheels;
            _feedBall = feedBall;
            _chute = chute;
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Renderer beam, Transform projectile, Renderer lamp, Material lineMaterial, RingGaugeView reload = null,
            Transform launcher = null)
        {
            _beam = beam;
            _projectile = projectile;
            _lamp = lamp;
            _lineMaterial = lineMaterial;
            _reload = reload;
            _launcher = launcher;
        }

        private void Awake()
        {
            _authoring = GetComponent<StageTrapAuthoring>();
            try
            {
                _trap = _authoring.ToTrap();
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"{name}: ステージの仕掛けの設定が不正です: {e.Message}", this);
                enabled = false;
                return;
            }
            if (_projectile != null)
            {
                _projectile.gameObject.SetActive(false);
            }
            if (_trap.Kind == StageTrapKind.Tripwire && _lineMaterial != null)
            {
                var overlay = new GameObject("Warning");
                overlay.transform.SetParent(transform, false);
                _overlay = overlay.AddComponent<WarningOverlay>();
                _overlay.Initialize(_lineMaterial, -0.7f);
            }
        }

        private void LateUpdate()
        {
            FindFeed();
            bool running = _feed != null && _feed.CharacterCount > 0 && _feed.Traps != null;
            int index = running ? IndexIn(_feed.Traps) : -1;
            if (_trap.Kind == StageTrapKind.Tripwire)
            {
                RenderTripwire(running && index >= 0 ? _feed.Traps[index] : null, index);
            }
            else
            {
                RenderPitcher(running && index >= 0 ? _feed.Traps[index] : null);
            }
        }

        private void RenderTripwire(StageTrap trap, int index)
        {
            bool arming = false;
            bool flying = false;
            bool reloading = false;
            float reloadLeft = 0f;
            Vector2 position = default;
            if (trap != null && _feed.Stage != null)
            {
                flying = StageTraps.TryGetMissile(trap, _feed.Stage, index, _feed.CurrentTick, out arming, out System.Numerics.Vector2 at);
                flying &= !arming;
                position = new Vector2(at.X, at.Y);
                if (StageTraps.TryGetReload(trap, _feed.Stage, index, _feed.CurrentTick, out int left, out int total) && total > 0)
                {
                    reloading = true;
                    reloadLeft = (float)left / total;
                }
            }
            Reloading = reloading;
            if (_reload != null)
            {
                if (reloading)
                {
                    _reload.Show(_launcher != null ? _launcher.position : transform.position, reloadLeft);
                }
                else if (_reload.IsShowing)
                {
                    _reload.Hide();
                }
            }
            if (arming && !_wasArming)
            {
                Play(SoundId.Alarm, 0.8f);
            }
            if (flying && !_wasFlying)
            {
                Play(SoundId.MissileFire, 0.9f);
            }
            if (_wasFlying && !flying && trap != null && _feed.Stage.TrapSpentOf(index))
            {
                Play(SoundId.MissileExplode, 0.9f);
            }
            _wasArming = arming;
            _wasFlying = flying;
            ShowsWarning = arming;
            if (_overlay != null)
            {
                if (arming)
                {
                    Vector2 size = _authoring.BeamSize;
                    Vector3 c = transform.position;
                    _areas.Clear();
                    _icons.Clear();
                    _areas.Add(new Rect(c.x - size.x * 0.5f, c.y - Mathf.Max(0.6f, size.y * 0.5f), size.x, Mathf.Max(1.2f, size.y)));
                    _icons.Add(new Vector2(c.x, c.y + 1.6f));
                    _overlay.Show(_areas, _icons, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f)));
                }
                else
                {
                    _overlay.Hide();
                }
            }
            if (_lamp != null)
            {
                _lamp.enabled = !arming || Mathf.Repeat(Time.time * BlinkPerSecond, 1f) < 0.5f;
            }
            if (_beam != null)
            {
                // 赤外線は待ちの間だけ消し、ほかはゆっくり明滅（触れられたら速く）。
                float speed = arming ? 8f : 1.2f;
                bool on = !reloading && Mathf.Repeat(Time.time * speed, 1f) < (arming ? 0.5f : 0.92f);
                _beam.enabled = on;
                foreach (Renderer beam in _beams)
                {
                    if (beam != null)
                    {
                        beam.enabled = on;
                    }
                }
            }
            if (_motes != null)
            {
                ParticleSystem.EmissionModule motes = _motes.emission;
                motes.enabled = !reloading;
            }
            SetProjectile(flying, position);
            if (flying && _projectile != null)
            {
                Vector2 direction = _authoring.Direction.normalized;
                _projectile.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            }
            if (_trail != null)
            {
                // 煙の帯: 飛んでいる間は尾から出し続け、当たったら出すのをやめる（出た煙は残って薄れる）。
                ParticleSystem.EmissionModule trail = _trail.emission;
                if (flying && _tail != null)
                {
                    _trail.transform.position = _tail.position;
                }
                if (trail.enabled != flying)
                {
                    trail.enabled = flying;
                }
            }
        }

        private void RenderPitcher(StageTrap trap)
        {
            bool shown = false;
            Vector2 position = default;
            Blinking = false;
            if (trap != null && trap.TryGetBall(_feed.CurrentTick, out int age, out System.Numerics.Vector2 at, out _, out bool blinking))
            {
                shown = !blinking || Mathf.Repeat(Time.time * BlinkPerSecond, 1f) < 0.5f;
                Blinking = blinking;
                position = new Vector2(at.X, at.Y);
                if (age < _lastAge)
                {
                    Play(SoundId.Pitch, 0.9f);
                }
                _lastAge = age;
            }
            else
            {
                _lastAge = int.MaxValue;
            }
            SetProjectile(shown, position);
            if (shown && _projectile != null)
            {
                // 転がる・飛ぶ向きに回す（見た目だけ）。
                _projectile.Rotate(0f, 0f, -Time.deltaTime * 720f, Space.Self);
            }
            // タイヤ: 上と下が逆向きに回り続ける（ボールを挟んで送り出す向き）。
            for (int i = 0; i < _wheels.Length; i++)
            {
                if (_wheels[i] != null)
                {
                    _wheels[i].Rotate(0f, 0f, (i % 2 == 0 ? -1f : 1f) * 1800f * Time.deltaTime, Space.Self);
                }
            }
            RenderFeed(trap);
        }

        // 送るボール: 打ち出す FeedTicks 前から、かごの出口から送りの筒を転がってタイヤの間へ入る。それまではかごの出口で待つ。
        private void RenderFeed(StageTrap trap)
        {
            Feeding = false;
            if (_feedBall == null || _chute.Length == 0)
            {
                return;
            }
            float u = 0f;
            if (trap != null && trap.Period > 0)
            {
                long shifted = (long)_feed.CurrentTick + trap.PhaseTicks;
                int age = shifted >= 0 ? (int)(shifted % trap.Period) : (int)(((shifted % trap.Period) + trap.Period) % trap.Period);
                int until = trap.Period - age;
                if (until <= FeedTicks)
                {
                    u = 1f - until / (float)FeedTicks;
                    Feeding = true;
                }
            }
            // 筒の中では重さで速くなる（転がり始めはゆっくり）。
            float along = u * u * (_chute.Length - 1);
            int segment = Mathf.Min(_chute.Length - 2, Mathf.FloorToInt(along));
            Vector3 local = _chute.Length == 1 ? _chute[0] : Vector3.Lerp(_chute[Mathf.Max(0, segment)], _chute[Mathf.Max(0, segment) + 1], along - Mathf.Max(0, segment));
            _feedBall.localPosition = local;
            _feedBall.Rotate(0f, 0f, -Time.deltaTime * (Feeding ? 900f : 0f), Space.Self);
        }

        private void SetProjectile(bool shown, Vector2 position)
        {
            if (_projectile == null)
            {
                return;
            }
            if (_projectile.gameObject.activeSelf != shown)
            {
                _projectile.gameObject.SetActive(shown);
            }
            if (shown)
            {
                _projectile.position = new Vector3(position.x, position.y, 0f);
            }
        }

        private void Play(SoundId id, float volume)
        {
            if (_feed == null || _feed.CharacterCount == 0)
            {
                return;
            }
            if (_audio == null)
            {
                _audio = FindFirstObjectByType<GameAudio>();
            }
            if (_audio != null)
            {
                _audio.Play(id, volume, 0.05f);
            }
        }

        // シミュレーションの仕掛けの一覧での、自分の番号（値が同じ仕掛けを探す。一覧が変わったら探し直す）。
        private int IndexIn(IReadOnlyList<StageTrap> traps)
        {
            if (!ReferenceEquals(traps, _indexedIn))
            {
                _indexedIn = traps;
                _index = -1;
                for (int i = 0; i < traps.Count; i++)
                {
                    if (traps[i].SameAs(_trap))
                    {
                        _index = i;
                        break;
                    }
                }
            }
            return _index;
        }

        private void FindFeed()
        {
            if (_feed is MonoBehaviour alive && alive == null)
            {
                _feed = null;
                _indexedIn = null;
            }
            if (_feed != null || Time.unscaledTime < _nextFeedSearch)
            {
                return;
            }
            _nextFeedSearch = Time.unscaledTime + 1f;
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is ISimulationFeed feed)
                {
                    _feed = feed;
                    break;
                }
            }
        }
    }
}
