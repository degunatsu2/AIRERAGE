using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 恐怖の館の鬼火の輪（ステージの仕掛け StageTrapKind.WispRing。ユーザーの要望、2026-11-21。表示だけ。状態を読むだけで、書き換えない）。
    /// 鬼火（ウィル・オ・ウィスプ）は、沼や墓地の地面から数メートルの所に浮かぶ小さな青白い火の玉で、煙を出さず、ゆらゆら漂い、人が近づくと
    /// 離れていくと伝えられる（記録と伝承のまとめ）。
    /// - ある間: 青白い鬼火が輪の形に連なって回り（Airerage/Wisp の板）、輪は決まった 6 地点の間を速く飛び回る（道のりは Core の
    ///   StageTrap.WispAt。表示はティックの間をなめらかにつなぐ）。飛んだ跡に鬼火の粒が残る。
    /// - くぐられたら: 鬼火がくぐった人の周りをぐるぐる回り（コインを渡す。台帳は Core）、続けて上空へ昇って消える。
    /// - また現れるとき: 上空から降りてきて、輪の形に戻る。
    /// 表示する試合がなければ（メニューの背景）、起動からの時間で飛び回る。
    /// </summary>
    [RequireComponent(typeof(StageTrapAuthoring))]
    public sealed class WispRingView : MonoBehaviour
    {
        [Tooltip("輪（鬼火の親。この物体の子で、ワールドの位置へ動かす）")]
        [SerializeField] private Transform _ring;
        [Tooltip("輪に連なる鬼火（Airerage/Wisp の板）")]
        [SerializeField] private Transform[] _wisps = new Transform[0];
        [Tooltip("輪の半径（横・縦。m）")]
        [SerializeField] private Vector2 _radii = new Vector2(1.4f, 1.7f);
        [Tooltip("くぐられたときに散る粒と、飛んだ跡に残る粒")]
        [SerializeField] private ParticleSystem _burst;
        [SerializeField] private ParticleSystem _trail;

        /// <summary>輪の形で回る速さ（ラジアン/秒）。</summary>
        public const float OrbitSpeed = 1.4f;

        /// <summary>くぐった人の周りを回る長さと、上空へ昇って消える長さ（秒）。</summary>
        public const float CircleSeconds = 1.6f;
        public const float RiseSeconds = 0.9f;

        /// <summary>また現れるときに、上空から降りてくる長さ（秒）と高さ（m）。</summary>
        public const float ArriveSeconds = 0.7f;
        public const float ArriveHeight = 14f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;
        private StageTrapAuthoring _authoring;
        private StageTrap _trap;
        private ISimulationFeed _feed;
        private GameAudio _audio;
        private float _nextFeedSearch;
        private int _index = -1;
        private IReadOnlyList<StageTrap> _indexedIn;
        private float[] _baseSize = new float[0];
        private int _lastTick = int.MinValue;
        private float _lastTickTime;
        private int _seenTaken;
        private float _arriveStart = -100f;

        /// <summary>今、輪の形で飛び回っているか（くぐられていない。テスト用）。</summary>
        public bool ShowsRing { get; private set; }

        /// <summary>くぐった人の周りを回っているか（テスト用）。</summary>
        public bool Circling { get; private set; }

        /// <summary>見た目の輪の中心（テスト用）。</summary>
        public Vector3 ShownCenter => _ring != null ? _ring.position : transform.position;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Transform ring, Transform[] wisps, Vector2 radii, ParticleSystem burst, ParticleSystem trail)
        {
            _ring = ring;
            _wisps = wisps;
            _radii = radii;
            _burst = burst;
            _trail = trail;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _authoring = GetComponent<StageTrapAuthoring>();
            try
            {
                _trap = _authoring.ToTrap();
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"{name}: 鬼火の輪の設定が不正です: {e.Message}", this);
                enabled = false;
                return;
            }
            _baseSize = new float[_wisps.Length];
            for (int i = 0; i < _wisps.Length; i++)
            {
                _baseSize[i] = _wisps[i] != null ? _wisps[i].localScale.x : 1f;
            }
        }

        private void LateUpdate()
        {
            if (_ring == null || _trap == null)
            {
                return;
            }
            FindFeed();
            bool running = _feed != null && _feed.CharacterCount > 0 && _feed.Traps != null && _feed.Stage != null;
            int tick = running ? _feed.CurrentTick : Mathf.FloorToInt(Time.time * SimTime.TicksPerSecond);
            int seed = running ? _feed.Rules.Seed : 0;
            if (tick != _lastTick)
            {
                _lastTick = tick;
                _lastTickTime = Time.time;
            }
            int taken = 0;
            int owner = -1;
            if (running)
            {
                int index = IndexIn(_feed.Traps);
                if (index >= 0)
                {
                    taken = _feed.Stage.TrapTickOf(index);
                    owner = _feed.Stage.TrapOwnerOf(index);
                }
            }
            if (taken != _seenTaken)
            {
                if (taken > 0)
                {
                    OnTaken(owner);
                }
                else
                {
                    _arriveStart = Time.time;
                }
                _seenTaken = taken;
            }
            if (taken == 0)
            {
                RenderRing(tick, seed);
            }
            else
            {
                RenderGift(tick - taken, owner);
            }
        }

        // 輪の形で飛び回る（ティックの間はなめらかにつなぐ）。また現れた直後は、上空から降りてくる。
        private void RenderRing(int tick, int seed)
        {
            ShowsRing = true;
            Circling = false;
            float frac = Mathf.Clamp01((Time.time - _lastTickTime) * SimTime.TicksPerSecond);
            System.Numerics.Vector2 a = _trap.WispAt(tick, seed);
            System.Numerics.Vector2 b = _trap.WispAt(tick + 1, seed);
            var center = new Vector3(Mathf.Lerp(a.X, b.X, frac), Mathf.Lerp(a.Y, b.Y, frac), 0f);
            float arrive = Mathf.Clamp01((Time.time - _arriveStart) / ArriveSeconds);
            float ease = 1f - (1f - arrive) * (1f - arrive) * (1f - arrive);
            center.y += ArriveHeight * (1f - ease);
            _ring.position = center;
            SetTrail(true);
            int count = _wisps.Length;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / Mathf.Max(1, count) + Time.time * OrbitSpeed;
                // 輪の線から少し外れて漂い、奥行きも少し揺れる。降りてくる間は縦に細長く連なる。
                float drift = 0.12f * Mathf.Sin(Time.time * 1.3f + i * 2.3f);
                float rx = (_radii.x + drift) * Mathf.Lerp(0.3f, 1f, ease);
                float ry = (_radii.y + drift) * Mathf.Lerp(2.2f, 1f, ease);
                Place(i, new Vector3(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry + 0.1f * Mathf.Sin(Time.time * 2.1f + i),
                    0.25f * Mathf.Sin(angle * 2f + Time.time)), 1f, 0f);
            }
        }

        // くぐった人の周りを回り、続けて上空へ昇って消える。その後は、また現れるまで隠す。
        private void RenderGift(int age, int owner)
        {
            ShowsRing = false;
            SetTrail(false);
            float seconds = age / (float)SimTime.TicksPerSecond;
            bool hasOwner = owner >= 0 && _feed != null && owner < _feed.CharacterCount;
            if (!hasOwner || seconds >= CircleSeconds + RiseSeconds)
            {
                Circling = false;
                SetWispsShown(false);
                return;
            }
            SetWispsShown(true);
            Circling = seconds < CircleSeconds;
            System.Numerics.Vector3 p = _feed.GetState(owner).Position;
            _ring.position = new Vector3(p.X, p.Y, 0f);
            float rise = Mathf.Max(0f, seconds - CircleSeconds) / RiseSeconds;
            int count = _wisps.Length;
            for (int i = 0; i < count; i++)
            {
                // 速く回りながら、少しずつ体に寄って上下にらせんを描く。昇るときは回る輪がほどけて上へ流れる。
                float angle = i * Mathf.PI * 2f / Mathf.Max(1, count) + seconds * 7f;
                float radius = Mathf.Lerp(1.7f, 1.1f, Mathf.Clamp01(seconds / CircleSeconds));
                float height = 0.9f * Mathf.Sin(angle * 0.5f + seconds * 4f) + rise * (10f + i * 1.3f);
                var local = new Vector3(Mathf.Cos(angle) * radius * (1f - rise * 0.6f), height, Mathf.Sin(angle) * radius * 0.5f);
                Place(i, local, 1f - rise, 1.2f * (1f - rise));
            }
        }

        private void Place(int i, Vector3 local, float scale, float flare)
        {
            Transform wisp = _wisps[i];
            if (wisp == null)
            {
                return;
            }
            wisp.localPosition = local;
            float flicker = Mathf.PerlinNoise(Time.time * 2.4f, i * 1.37f);
            float size = _baseSize.Length > i ? _baseSize[i] : 1f;
            wisp.localScale = Vector3.one * size * Mathf.Max(0.01f, scale) * (0.75f + 0.5f * flicker + 0.4f * flare);
            var renderer = wisp.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.GetPropertyBlock(_block);
                float glow = 0.65f + 0.6f * flicker + 1.4f * flare;
                _block.SetColor(ColorId, new Color(0.9f * glow, 1.7f * glow, 1.4f * glow, 1f));
                renderer.SetPropertyBlock(_block);
            }
        }

        private void SetWispsShown(bool shown)
        {
            foreach (Transform wisp in _wisps)
            {
                if (wisp != null && wisp.gameObject.activeSelf != shown)
                {
                    wisp.gameObject.SetActive(shown);
                }
            }
        }

        private void SetTrail(bool on)
        {
            SetWispsShown(true);
            if (_trail == null)
            {
                return;
            }
            ParticleSystem.EmissionModule emission = _trail.emission;
            if (emission.enabled != on)
            {
                emission.enabled = on;
            }
        }

        // くぐられた瞬間: くぐった人の所で鬼火の粒が散り、コインの音。
        private void OnTaken(int owner)
        {
            if (owner < 0 || _feed == null || owner >= _feed.CharacterCount)
            {
                return;
            }
            System.Numerics.Vector3 p = _feed.GetState(owner).Position;
            if (_burst != null)
            {
                _burst.transform.position = new Vector3(p.X, p.Y, -0.2f);
                _burst.Emit(24);
            }
            if (_audio == null)
            {
                _audio = FindFirstObjectByType<GameAudio>();
            }
            if (_audio != null)
            {
                _audio.Play(SoundId.CoinPickup, 1f, 0.05f);
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
                _audio = null;
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
