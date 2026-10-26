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
    /// - ピッチャーマシン: ボール（_projectile）を道のりの位置へ動かし、止まったら点滅させ、消えたら隠す。打ち出した瞬間に音。
    /// 表示する試合がなければ（メニューの背景）、赤外線とマシンだけを出す。
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

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Renderer beam, Transform projectile, Renderer lamp, Material lineMaterial)
        {
            _beam = beam;
            _projectile = projectile;
            _lamp = lamp;
            _lineMaterial = lineMaterial;
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
            Vector2 position = default;
            if (trap != null && _feed.Stage != null)
            {
                flying = StageTraps.TryGetMissile(trap, _feed.Stage, index, _feed.CurrentTick, out arming, out System.Numerics.Vector2 at);
                flying &= !arming;
                position = new Vector2(at.X, at.Y);
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
                // 赤外線はいつも出して、ゆっくり明滅（触れられたら速く）。
                float speed = arming ? 8f : 1.2f;
                _beam.enabled = Mathf.Repeat(Time.time * speed, 1f) < (arming ? 0.5f : 0.92f);
            }
            SetProjectile(flying, position);
            if (flying && _projectile != null)
            {
                Vector2 direction = _authoring.Direction.normalized;
                _projectile.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
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
