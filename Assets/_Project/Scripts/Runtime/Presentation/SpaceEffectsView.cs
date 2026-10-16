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
    /// - スペースミサイルを長押しで狙っている間（CombatState.HoldAim）: 照準の方向へ伸びる狙いの線と、先の照準（C-8。撃つ先を示す表示）。
    /// - ゆっくり落ちている間（CombatState.SlowFallSerial）: 足もとに散る星の光。
    /// - ワープポータルで移った瞬間（CombatState.WarpCount が増えた）: 入った所と出た所に紫の光。
    /// 見た目の動きは表示の時間で決める。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class SpaceEffectsView : MonoBehaviour
    {
        private const float AimLength = 14f;
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
            public LineRenderer Aim;
            public LineRenderer Reticle;
            public LineRenderer Stars;
            public BurstEffectView WarpIn;
            public BurstEffectView WarpOut;
            public int WarpCount = -1;
            public Vector3 LastPosition;
            public bool Aiming;
            public bool Floating;
            public int Warps;
        }

        /// <summary>キャラクター index のミサイルの狙いの線を出しているか（テスト用）。</summary>
        public bool ShowsAim(int index) => index >= 0 && index < _effects.Count && _effects[index].Aiming;

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
                    SetAim(effects, false);
                    SetStars(effects, false);
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                var center = new Vector3(state.Position.X, state.Position.Y, 0f);
                bool alive = !state.Combat.IsWaitingToRespawn;

                bool aiming = alive && state.Combat.Holding && config.Combat.Get(state.Combat.HoldSlot).Effect == SkillEffect.Missile;
                SetAim(effects, aiming);
                if (aiming)
                {
                    System.Numerics.Vector2 aim = SkillRules.AimDirection(state.Combat.HoldAim, state.Facing, config.Wire.AimDeadzone);
                    DrawAim(effects, center + new Vector3(0f, config.Skills.Missile.OffsetY, 0f), new Vector3(aim.X, aim.Y, 0f));
                }

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

        // 狙いの線（先ほど薄い）と、先の照準（ひし形）。
        private static void DrawAim(Effects effects, Vector3 from, Vector3 direction)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
            LineRenderer aim = effects.Aim;
            aim.positionCount = 2;
            aim.SetPosition(0, from + direction * 0.7f + new Vector3(0f, 0f, Z));
            Vector3 end = from + direction * AimLength + new Vector3(0f, 0f, Z);
            aim.SetPosition(1, end);
            aim.startColor = new Color(0.85f, 0.65f, 1f, 0.9f);
            aim.endColor = new Color(0.62f, 0.35f, 1f, 0.1f);

            LineRenderer reticle = effects.Reticle;
            float size = 0.45f + 0.1f * pulse;
            Vector3 mid = from + direction * (AimLength * 0.6f) + new Vector3(0f, 0f, Z);
            reticle.loop = true;
            reticle.positionCount = 4;
            reticle.SetPosition(0, mid + new Vector3(0f, size, 0f));
            reticle.SetPosition(1, mid + new Vector3(size, 0f, 0f));
            reticle.SetPosition(2, mid + new Vector3(0f, -size, 0f));
            reticle.SetPosition(3, mid + new Vector3(-size, 0f, 0f));
            reticle.startColor = reticle.endColor = new Color(1f, 0.9f, 1f, 0.6f + 0.3f * pulse);
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

        private static void SetAim(Effects effects, bool show)
        {
            effects.Aiming = show;
            effects.Aim.enabled = show;
            effects.Reticle.enabled = show;
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
                Aim = NewLine(index, "Aim", 0.07f),
                Reticle = NewLine(index, "Reticle", 0.05f),
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
