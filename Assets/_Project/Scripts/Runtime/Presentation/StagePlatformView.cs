using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 動く足場と扉の見た目（StagePlatformAuthoring。ユーザーの要望、2026-10-26）。見た目の子（_visual）を、シミュレーションの足場の
    /// 位置へ動かす（足場の物体そのものは動かさない。道のりの元の位置なので）。道のりはティック数だけで決まり、扉は開き具合
    /// （ISimulationFeed.Stage）で決まる。ない間（ロールケーキがステージの外へ去った後など）は隠す。表示する試合がなければ
    /// （メニューの背景）、起動からの時間で動かす（扉は閉じたまま）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class StagePlatformView : MonoBehaviour
    {
        [Tooltip("動かす見た目（この物体の子）")]
        [SerializeField] private Transform _visual;
        [Tooltip("ボタンのランプ（ワイヤーをつないでいる間は _lampOn、ほかは _lampOff。ボタンだけ）")]
        [SerializeField] private Renderer _lamp;
        [SerializeField] private Material _lampOff;
        [SerializeField] private Material _lampOn;

        private StagePlatformAuthoring _authoring;
        private StagePlatform _platform;
        private ISimulationFeed _feed;
        private float _nextFeedSearch;
        private int _index = -1;
        private IReadOnlyList<StagePlatform> _indexedIn;

        /// <summary>見た目が出ているか（テスト用）。</summary>
        public bool Shown => _visual != null && _visual.gameObject.activeSelf;

        /// <summary>見た目の今の中心（テスト用）。</summary>
        public Vector3 ShownCenter => _visual != null ? _visual.position : transform.position;

        /// <summary>ボタンのランプを点けているか（テスト用）。</summary>
        public bool LampOn { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Transform visual)
        {
            _visual = visual;
        }

        /// <summary>ボタンのランプを決める（シーンのビルダー用）。</summary>
        public void ConfigureLamp(Renderer lamp, Material off, Material on)
        {
            _lamp = lamp;
            _lampOff = off;
            _lampOn = on;
        }

        private void Awake()
        {
            _authoring = GetComponent<StagePlatformAuthoring>();
            if (_authoring == null || _visual == null)
            {
                Debug.LogError($"{name}: 動く足場（StagePlatformAuthoring）か、動かす見た目がありません", this);
                enabled = false;
                return;
            }
            try
            {
                _platform = _authoring.ToPlatform();
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"{name}: 動く足場の設定が不正です: {e.Message}", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            FindFeed();
            bool running = _feed != null && _feed.CharacterCount > 0 && _feed.Platforms != null;
            int tick = running ? _feed.CurrentTick : Mathf.FloorToInt(Time.time * SimTime.TicksPerSecond);
            int open = 0;
            if (running && _platform.IsDoor)
            {
                int index = IndexIn(_feed.Platforms);
                open = index >= 0 && _feed.Stage != null ? _feed.Stage.OpenOf(index) : 0;
            }
            else if (!running && _platform.IsDoor && _platform.OpenPeriod > 0 && _platform.ScheduledOpen(tick))
            {
                // メニューの背景: 周期で開く扉（牢屋）は、周期どおりに開いて見せる。
                open = StagePlatform.DoorScale;
            }
            if (_platform.IsButton && _lamp != null)
            {
                bool pressed = running && IsPressed(IndexIn(_feed.Platforms));
                if (pressed != LampOn || _lamp.sharedMaterial == null)
                {
                    LampOn = pressed;
                    _lamp.sharedMaterial = pressed ? _lampOn : _lampOff;
                }
            }
            bool active = _platform.TryGetCenter(tick, open, out System.Numerics.Vector2 center);
            if (_visual.gameObject.activeSelf != active)
            {
                _visual.gameObject.SetActive(active);
            }
            if (active)
            {
                _visual.position = new Vector3(center.X, center.Y, transform.position.z);
            }
        }

        // ボタン index に、だれかがワイヤーをつないでいるか。
        private bool IsPressed(int index)
        {
            if (index < 0)
            {
                return false;
            }
            for (int i = 0; i < _feed.CharacterCount; i++)
            {
                CharacterState state = _feed.GetState(i);
                if (!state.Combat.HasLeft && state.Wire.Phase == Airerage.Core.Wire.WirePhase.Attached && state.Wire.AnchorBody == index + 1)
                {
                    return true;
                }
            }
            return false;
        }

        // シミュレーションの足場の一覧での、自分の番号（値が同じ足場を探す。一覧が変わったら探し直す）。
        private int IndexIn(IReadOnlyList<StagePlatform> platforms)
        {
            if (!ReferenceEquals(platforms, _indexedIn))
            {
                _indexedIn = platforms;
                _index = -1;
                for (int i = 0; i < platforms.Count; i++)
                {
                    if (platforms[i].SameAs(_platform))
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
