using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// SPACE TRAVELER のキャラクターまわりの表示（docs/14 1.1）。
    /// スペースミサイルを長押しで狙っている間の矢印は、ほかの照準の方向の技と同じ SkillAimView（2026-10-29）。
    /// - ゆっくり落ちている間（CombatState.SlowFallSerial）: 足もとに散る星の光。
    /// - ワープポータルで移った瞬間（CombatState.WarpCount が増えた）: 入った所と出た所に紫の光。
    /// 見た目の動きは表示の時間で決める。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class SpaceEffectsView : MonoBehaviour
    {
        private const int StarPoints = 10;
        private const float Z = -0.6f;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("線の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private LineRenderer _lineTemplate;
        [Tooltip("ワープの光の見本（隠しておく。キャラクターごとに 2 つ複製する）")]
        [SerializeField] private BurstEffectView _burstTemplate;

        private readonly List<Effects> _effects = new List<Effects>();
        private ISimulationFeed _feed;

        private sealed class Effects
        {
            public LineRenderer Stars;
            public BurstEffectView WarpIn;
            public BurstEffectView WarpOut;
            public int WarpCount = -1;
            public Vector3 LastPosition;
            public bool Floating;
            public int Warps;
        }

        /// <summary>キャラクター index のゆっくり落ちる光を出しているか（テスト用）。</summary>
        public bool ShowsSlowFall(int index) => index >= 0 && index < _effects.Count && _effects[index].Floating;

        /// <summary>キャラクター index のワープの光を出した回数（テスト用）。</summary>
        public int WarpFlashesOf(int index) => index >= 0 && index < _effects.Count ? _effects[index].Warps : 0;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _lineTemplate == null || _burstTemplate == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、線・光の見本が設定されていません", this);
                enabled = false;
                return;
            }
            _lineTemplate.gameObject.SetActive(false);
            _burstTemplate.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            int count = _feed.CharacterCount;
            while (_effects.Count < count)
            {
                _effects.Add(Create(_effects.Count));
            }
            for (int i = 0; i < _effects.Count; i++)
            {
                Effects effects = _effects[i];
                if (i >= count)
                {
                    SetStars(effects, false);
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                var center = new Vector3(state.Position.X, state.Position.Y, 0f);
                bool alive = !state.Combat.IsWaitingToRespawn;

                bool floating = alive && state.Combat.SlowFallSerial != 0;
                SetStars(effects, floating);
                if (floating)
                {
                    DrawStars(effects.Stars, center - new Vector3(0f, config.Movement.HalfHeight, 0f));
                }

                if (effects.WarpCount >= 0 && state.Combat.WarpCount > effects.WarpCount)
                {
                    effects.WarpIn.Play(effects.LastPosition);
                    effects.WarpOut.Play(center);
                    effects.Warps++;
                }
                effects.WarpCount = state.Combat.WarpCount;
                effects.LastPosition = center;
            }
        }

        // 足もとに散る星（小さな十字が、ちらちら位置を変える）。
        private static void DrawStars(LineRenderer stars, Vector3 feet)
        {
            stars.positionCount = StarPoints;
            float time = Time.time;
            for (int k = 0; k < StarPoints; k++)
            {
                float phase = Mathf.Repeat(time * 1.5f + k * 0.37f, 1f);
                float x = Mathf.Sin(k * 2.3f + time * 0.7f) * 0.9f;
                float y = -phase * 1.2f;
                stars.SetPosition(k, feet + new Vector3(x, y, Z));
            }
            stars.startColor = new Color(1f, 0.95f, 0.7f, 0.9f);
            stars.endColor = new Color(0.7f, 0.5f, 1f, 0.2f);
        }

        private static void SetStars(Effects effects, bool show)
        {
            effects.Floating = show;
            effects.Stars.enabled = show;
        }

        private Effects Create(int index)
        {
            var effects = new Effects
            {
                Stars = NewLine(index, "Stars", 0.06f),
                WarpIn = Instantiate(_burstTemplate, transform),
                WarpOut = Instantiate(_burstTemplate, transform),
            };
            effects.WarpIn.gameObject.SetActive(true);
            effects.WarpOut.gameObject.SetActive(true);
            return effects;
        }

        private LineRenderer NewLine(int index, string name, float width)
        {
            LineRenderer line = Instantiate(_lineTemplate, transform);
            line.name = $"Space{index}{name}";
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = width;
            line.enabled = false;
            return line;
        }
    }
}
