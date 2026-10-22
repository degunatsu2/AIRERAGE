using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// PASTRY CHEF の技のキャラクターまわりの表示（docs/14 1.4）と、クリームに触れている人の印。
    /// - フラッフィベイスを長押ししている間: 置く場所の枠（横長の長方形）と、表の向きの矢印（離したときと同じ計算。PastryRules）。
    ///   短く押したとき（足元に置く）とみなす間は出さず、長押しになった瞬間から照準の方向に出す。
    /// - スイートトラップを長押ししている間: クリームの飛び方の線（重力で落ちる弧。選んでいる味の色）。
    /// - クリームの上: オレンジ（速くなる）は体の周りに上へ昇る明るい山形、抹茶（遅くなる）は下へ落ちる暗い山形と、体の周りの輪。
    /// - ボウル（空中上）で上からの投射物を防いだ瞬間（CombatState.ShieldBlockCount が増えた）: 頭上に弾く光と「GUARD」。
    /// 見た目の動きは表示の時間で決める。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class PastryEffectsView : MonoBehaviour
    {
        private const float Z = -0.6f;
        private const int ArcPoints = 24;
        private const float ArcSeconds = 0.9f;
        private const int Chevrons = 3;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("線の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private LineRenderer _lineTemplate;
        [Tooltip("ボウルで防いだときの光と GUARD の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private BurstEffectView _guardTemplate;

        private readonly List<Effects> _effects = new List<Effects>();
        private ISimulationFeed _feed;

        private sealed class Effects
        {
            public LineRenderer Base;
            public LineRenderer Arrow;
            public LineRenderer[] Arcs;
            public LineRenderer Ring;
            public LineRenderer[] Marks;
            public BurstEffectView Guard;
            public int ShieldBlocks = -1;
            public int Guards;
            public bool Placing;
            public bool Aiming;
            public int Aura;
        }

        /// <summary>キャラクター index のわたがしの足場の置く場所を出しているか（テスト用）。</summary>
        public bool ShowsBasePreview(int index) => index >= 0 && index < _effects.Count && _effects[index].Placing;

        /// <summary>キャラクター index のクリームの飛び方を出しているか（テスト用）。</summary>
        public bool ShowsCreamPreview(int index) => index >= 0 && index < _effects.Count && _effects[index].Aiming;

        /// <summary>キャラクター index のクリームの印（1 は速くなる、-1 は遅くなる、0 はなし。テスト用）。</summary>
        public int AuraOf(int index) => index >= 0 && index < _effects.Count ? _effects[index].Aura : 0;

        /// <summary>キャラクター index がボウルで防いで GUARD を出した回数（テスト用）。</summary>
        public int GuardsOf(int index) => index >= 0 && index < _effects.Count ? _effects[index].Guards : 0;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _lineTemplate == null || _guardTemplate == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、線・光の見本が設定されていません", this);
                enabled = false;
                return;
            }
            _lineTemplate.gameObject.SetActive(false);
            _guardTemplate.gameObject.SetActive(false);
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
                    SetPlacing(effects, false);
                    SetAiming(effects, false);
                    SetAura(effects, 0);
                    effects.ShieldBlocks = -1;
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                bool alive = !state.Combat.IsWaitingToRespawn;
                SkillEffect holding = alive && state.Combat.Holding ? config.Combat.Get(state.Combat.HoldSlot).Effect : SkillEffect.None;

                // 置く場所は、長押しになった（短く押したときの足元ではなくなった）瞬間から、照準の方向に出す（ユーザーの要望、2026-10-21）。
                SetPlacing(effects, holding == SkillEffect.CottonCandy && state.Combat.HoldTicks >= config.Skills.CottonCandy.ShortPressTicks);
                if (effects.Placing)
                {
                    PastryRules.CandyPlacement(state, config, state.Combat.HoldTicks, state.Combat.SkillAim,
                        out System.Numerics.Vector2 center, out System.Numerics.Vector2 normal);
                    CottonCandyTuning candy = config.Skills.CottonCandy;
                    DrawBase(effects, new Vector3(center.X, center.Y, 0f), new Vector3(normal.X, normal.Y, 0f), candy.HalfLength, candy.HalfThickness);
                }

                SetAiming(effects, holding == SkillEffect.Cream);
                if (effects.Aiming)
                {
                    DrawCream(effects, state, config);
                }

                if (effects.ShieldBlocks >= 0 && state.Combat.ShieldBlockCount > effects.ShieldBlocks)
                {
                    effects.Guard.Play(new Vector3(state.Position.X, state.Position.Y + config.Movement.HalfHeight + 1f, Z));
                    effects.Guards++;
                }
                effects.ShieldBlocks = state.Combat.ShieldBlockCount;

                int aura = alive ? System.Math.Sign(state.Combat.CreamSpeedPercent) : 0;
                SetAura(effects, aura);
                if (aura != 0)
                {
                    DrawAura(effects, new Vector3(state.Position.X, state.Position.Y, 0f), config.Movement.HalfHeight, aura);
                }
            }
        }

        // 置く場所の枠と、表の向きの矢印（ピンク。脈打つ）。
        private static void DrawBase(Effects effects, Vector3 center, Vector3 normal, float halfLength, float halfThickness)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
            var tangent = new Vector3(normal.y, -normal.x, 0f);
            var z = new Vector3(0f, 0f, Z);
            LineRenderer frame = effects.Base;
            frame.loop = true;
            frame.positionCount = 4;
            frame.SetPosition(0, center + tangent * halfLength + normal * halfThickness + z);
            frame.SetPosition(1, center - tangent * halfLength + normal * halfThickness + z);
            frame.SetPosition(2, center - tangent * halfLength - normal * halfThickness + z);
            frame.SetPosition(3, center + tangent * halfLength - normal * halfThickness + z);
            frame.startColor = frame.endColor = new Color(1f, 0.6f, 0.85f, 0.55f + 0.35f * pulse);

            LineRenderer arrow = effects.Arrow;
            Vector3 tip = center + normal * (halfThickness + 0.9f);
            Vector3 root = center + normal * halfThickness;
            arrow.loop = false;
            arrow.positionCount = 5;
            arrow.SetPosition(0, root + z);
            arrow.SetPosition(1, tip + z);
            arrow.SetPosition(2, tip - normal * 0.35f + tangent * 0.3f + z);
            arrow.SetPosition(3, tip + z);
            arrow.SetPosition(4, tip - normal * 0.35f - tangent * 0.3f + z);
            arrow.startColor = arrow.endColor = new Color(1f, 0.85f, 0.95f, 0.9f);
        }

        // クリームの飛び方（重力で落ちる弧。先ほど薄い）。
        private static void DrawCream(Effects effects, in CharacterState state, CharacterConfig config)
        {
            CreamTuning cream = config.Skills.Cream;
            Color color = WorldObjectsView.CreamColor((byte)PastryRules.FlavorOf(state));
            for (int k = 0; k < effects.Arcs.Length; k++)
            {
                LineRenderer arc = effects.Arcs[k];
                bool used = k < cream.ShotCount;
                arc.enabled = used;
                if (!used)
                {
                    continue;
                }
                PastryRules.CreamLaunch(state, config, state.Combat.SkillAim, k, out System.Numerics.Vector2 origin, out System.Numerics.Vector2 velocity);
                arc.positionCount = ArcPoints;
                for (int p = 0; p < ArcPoints; p++)
                {
                    float t = ArcSeconds * p / (ArcPoints - 1);
                    arc.SetPosition(p, new Vector3(origin.X + velocity.X * t, origin.Y + velocity.Y * t - 0.5f * cream.Gravity * t * t, Z));
                }
                arc.startColor = new Color(color.r, color.g, color.b, 0.85f);
                arc.endColor = new Color(color.r, color.g, color.b, 0.05f);
            }
        }

        // クリームの上の印: 速くなる（上へ昇るオレンジの山形）・遅くなる（下へ落ちる抹茶色の山形）と、体の周りの輪。
        private static void DrawAura(Effects effects, Vector3 center, float halfHeight, int direction)
        {
            Color color = direction > 0 ? new Color(1f, 0.7f, 0.2f) : new Color(0.45f, 0.62f, 0.3f);
            LineRenderer ring = effects.Ring;
            ring.loop = true;
            ring.positionCount = 20;
            float wobble = 0.08f * Mathf.Sin(Time.time * 8f);
            for (int p = 0; p < 20; p++)
            {
                float angle = Mathf.PI * 2f * p / 20f;
                ring.SetPosition(p, center + new Vector3(Mathf.Cos(angle) * (0.75f + wobble), Mathf.Sin(angle) * (halfHeight + 0.2f + wobble), Z));
            }
            ring.startColor = ring.endColor = new Color(color.r, color.g, color.b, 0.6f);
            for (int k = 0; k < effects.Marks.Length; k++)
            {
                float phase = Mathf.Repeat(Time.time * 1.4f + k / (float)Chevrons, 1f);
                float y = direction > 0 ? Mathf.Lerp(-halfHeight, halfHeight + 0.6f, phase) : Mathf.Lerp(halfHeight + 0.4f, -halfHeight, phase);
                float x = (k - 1) * 0.55f;
                float tip = direction > 0 ? 0.22f : -0.22f;
                LineRenderer mark = effects.Marks[k];
                mark.positionCount = 3;
                mark.SetPosition(0, center + new Vector3(x - 0.2f, y - tip, Z));
                mark.SetPosition(1, center + new Vector3(x, y, Z));
                mark.SetPosition(2, center + new Vector3(x + 0.2f, y - tip, Z));
                float alpha = Mathf.Sin(phase * Mathf.PI);
                mark.startColor = mark.endColor = new Color(color.r, color.g, color.b, alpha);
            }
        }

        private static void SetPlacing(Effects effects, bool show)
        {
            effects.Placing = show;
            effects.Base.enabled = show;
            effects.Arrow.enabled = show;
        }

        private static void SetAiming(Effects effects, bool show)
        {
            // 出すときの線は DrawCream が決める（使う数だけ）。
            effects.Aiming = show;
            if (!show)
            {
                foreach (LineRenderer arc in effects.Arcs)
                {
                    arc.enabled = false;
                }
            }
        }

        private static void SetAura(Effects effects, int aura)
        {
            effects.Aura = aura;
            effects.Ring.enabled = aura != 0;
            foreach (LineRenderer mark in effects.Marks)
            {
                mark.enabled = aura != 0;
            }
        }

        private Effects Create(int index)
        {
            var effects = new Effects
            {
                Base = NewLine(index, "Base", 0.08f),
                Arrow = NewLine(index, "Arrow", 0.08f),
                Arcs = new LineRenderer[8],
                Ring = NewLine(index, "CreamRing", 0.06f),
                Marks = new LineRenderer[Chevrons],
                Guard = Instantiate(_guardTemplate, transform),
            };
            effects.Guard.name = $"Pastry{index}Guard";
            effects.Guard.gameObject.SetActive(true);
            for (int k = 0; k < effects.Arcs.Length; k++)
            {
                effects.Arcs[k] = NewLine(index, "CreamArc" + k, 0.06f);
            }
            for (int k = 0; k < Chevrons; k++)
            {
                effects.Marks[k] = NewLine(index, "CreamMark" + k, 0.09f);
            }
            return effects;
        }

        private LineRenderer NewLine(int index, string name, float width)
        {
            LineRenderer line = Instantiate(_lineTemplate, transform);
            line.name = $"Pastry{index}{name}";
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = width;
            line.enabled = false;
            return line;
        }
    }
}
