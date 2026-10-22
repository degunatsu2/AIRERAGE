using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 電気のキャラクターまわりの表示（SOLDERER。ユーザーの案、2026-10-21）。
    /// - 感電している間（CombatState.StunTicksLeft）: 体を走る稲妻（だれでも。残りは CharacterView の円形のゲージ）。
    /// - インバータがオンの間（CombatState.InverterOn）: 体のまわりにときどき弾ける小さな火花。
    /// - 電気のキャラクター（E がインバータ）の攻撃: 出ている判定の縁を走る稲妻（全身の発電、雷の衝撃波、指先、ストンピング、サマーソルト）。
    /// - イナズマステップ（連続で出せる特殊攻撃 1）: 突撃の跡。長押しで出したものは太い黄色、短く押したものは水色。
    /// - イナズマステップを長押ししている間: 長押しになった瞬間から、体のまわりに強い雷のオーラ（離すと強い突撃になる合図）。
    ///   長押しで出した突撃の間は、そのオーラが体のまわりを回る。
    /// 稲妻のゆらぎは表示の時間で決める。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class ElectricEffectsView : MonoBehaviour
    {
        private const float Z = -0.65f;
        private const int Bolts = 3;
        private const int BoltPoints = 9;
        private const int TrailPoints = 12;
        private const int AuraPoints = 28;
        private const float FlickerSeconds = 0.05f;

        public static readonly Color Spark = new Color(0.45f, 0.92f, 1f);
        public static readonly Color StrongSpark = new Color(1f, 0.95f, 0.45f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("線の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private LineRenderer _lineTemplate;

        private readonly List<Effects> _effects = new List<Effects>();
        private readonly AttackHit[] _activeHits = new AttackHit[AttackData.MaxHits];
        private ISimulationFeed _feed;

        private sealed class Effects
        {
            public LineRenderer[] Bolts;
            public LineRenderer Inverter;
            public LineRenderer[] AttackSparks;
            public int SparkCount;
            public LineRenderer Trail;
            public LineRenderer Aura;
            public readonly Vector3[] TrailPositions = new Vector3[TrailPoints];
            public int TrailCount;
            public bool Stunned;
            public bool InverterOn;
            public bool Sparking;
            public bool Dashing;
            public bool Charging;
        }

        /// <summary>キャラクター index に感電の稲妻を出しているか（テスト用）。</summary>
        public bool ShowsStun(int index) => index >= 0 && index < _effects.Count && _effects[index].Stunned;

        /// <summary>キャラクター index にインバータの火花を出しているか（テスト用）。</summary>
        public bool ShowsInverter(int index) => index >= 0 && index < _effects.Count && _effects[index].InverterOn;

        /// <summary>キャラクター index の攻撃の判定に稲妻を出しているか（テスト用）。</summary>
        public bool ShowsAttackSpark(int index) => index >= 0 && index < _effects.Count && _effects[index].Sparking;

        /// <summary>キャラクター index の、稲妻を出している判定の数（前後に同時に出る判定は 2。テスト用）。</summary>
        public int AttackSparkCount(int index) => index >= 0 && index < _effects.Count ? _effects[index].SparkCount : 0;

        /// <summary>キャラクター index のイナズマステップの跡を出しているか（テスト用）。</summary>
        public bool ShowsDash(int index) => index >= 0 && index < _effects.Count && _effects[index].Dashing;

        /// <summary>キャラクター index のイナズマステップの長押しのオーラを出しているか（テスト用）。</summary>
        public bool ShowsCharge(int index) => index >= 0 && index < _effects.Count && _effects[index].Charging;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _lineTemplate == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、線の見本が設定されていません", this);
                enabled = false;
                return;
            }
            _lineTemplate.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            int count = _feed.CharacterCount;
            while (_effects.Count < count)
            {
                _effects.Add(Create(_effects.Count));
            }
            // 稲妻の形は FlickerSeconds ごとに変える（毎フレーム変えるとちらつきすぎる）。
            int seed = Mathf.FloorToInt(Time.time / FlickerSeconds);
            for (int i = 0; i < _effects.Count; i++)
            {
                Effects effects = _effects[i];
                if (i >= count)
                {
                    Hide(effects);
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                bool alive = !state.Combat.IsWaitingToRespawn;
                var center = new Vector3(state.Position.X, state.Position.Y, 0f);
                float halfHeight = config.Movement.HalfHeight;
                float halfWidth = config.Movement.HalfWidth;
                var random = new System.Random(seed * 31 + i * 7919);

                SetStun(effects, alive && state.Combat.StunTicksLeft > 0);
                if (effects.Stunned)
                {
                    DrawStun(effects, center, halfWidth, halfHeight, random);
                }

                SetLine(effects.Inverter, effects.InverterOn = alive && state.Combat.InverterOn);
                if (effects.InverterOn)
                {
                    DrawInverter(effects.Inverter, center, halfWidth, halfHeight, random);
                }

                // 出ている判定ごとに稲妻を走らせる（THUNDER KICK は前と後ろに同時に出る。2026-10-22）。
                bool electric = config.Combat.Get(AttackSlot.Special2).Effect == SkillEffect.Inverter;
                int sparks = electric && alive ? ActiveHits(state, config.Combat, _activeHits) : 0;
                effects.SparkCount = sparks;
                effects.Sparking = sparks > 0;
                for (int k = 0; k < effects.AttackSparks.Length; k++)
                {
                    SetLine(effects.AttackSparks[k], k < sparks);
                    if (k < sparks)
                    {
                        DrawAttackSpark(effects.AttackSparks[k], state, _activeHits[k], random);
                    }
                }

                AttackData step = config.Combat.Get(AttackSlot.Special1);
                bool dashing = alive && step.ChainMax > 1 && state.Action.Kind == ActionKind.Attack && state.Action.Attack == AttackSlot.Special1;
                UpdateTrail(effects, dashing, center, step.IsCharged(state.Action.Charge));

                bool charging = alive && step.ChainMax > 1 && state.Combat.Holding && state.Combat.HoldSlot == AttackSlot.Special1
                    && step.IsCharged(state.Combat.HoldTicks);
                // 長押しで出した突撃の間は、同じオーラが体のまわりを回る（長押しになった瞬間に出る技では、ためている間のオーラは
                // 出る間がなく、このオーラだけになる。2026-10-22）。
                bool spinning = dashing && step.IsCharged(state.Action.Charge);
                effects.Charging = charging || spinning;
                SetLine(effects.Aura, charging || spinning);
                if (charging || spinning)
                {
                    DrawAura(effects.Aura, center, halfWidth, halfHeight, spinning ? Time.time * 25f : 0f, random);
                }
            }
        }

        // 今出ている判定（長押しかどうかで使う判定を選ぶ）を into に入れ、その数を返す。
        private static int ActiveHits(in CharacterState state, CombatTuning combat, AttackHit[] into)
        {
            if (state.Action.Kind != ActionKind.Attack)
            {
                return 0;
            }
            AttackData attack = combat.Get(state.Action.Attack);
            int count = 0;
            foreach (AttackHit candidate in attack.Hits)
            {
                if (count < into.Length && candidate.IsActiveAt(state.Action.Ticks) && attack.Uses(candidate.Charge, state.Action.Charge))
                {
                    into[count++] = candidate;
                }
            }
            return count;
        }

        // 感電: 体を上から下へ走る 3 本の稲妻。
        private static void DrawStun(Effects effects, Vector3 center, float halfWidth, float halfHeight, System.Random random)
        {
            for (int k = 0; k < effects.Bolts.Length; k++)
            {
                float x = (k - 1) * halfWidth * 0.8f;
                Vector3 from = center + new Vector3(x + Jitter(random, 0.2f), halfHeight + 0.25f, 0f);
                Vector3 to = center + new Vector3(x + Jitter(random, 0.2f), -halfHeight - 0.15f, 0f);
                Zigzag(effects.Bolts[k], from, to, 0.28f, random);
                Color color = k == 1 ? StrongSpark : Spark;
                effects.Bolts[k].startColor = new Color(1f, 1f, 1f, 0.95f);
                effects.Bolts[k].endColor = new Color(color.r, color.g, color.b, 0.8f);
            }
        }

        // インバータ: 体の縁のどこかで、短く弾ける火花（数回に 1 回は消える）。
        private static void DrawInverter(LineRenderer line, Vector3 center, float halfWidth, float halfHeight, System.Random random)
        {
            if (random.NextDouble() < 0.3)
            {
                line.positionCount = 0;
                return;
            }
            float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
            var edge = new Vector3(Mathf.Cos(angle) * (halfWidth + 0.15f), Mathf.Sin(angle) * (halfHeight + 0.1f), 0f);
            var outward = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Zigzag(line, center + edge, center + edge + outward * 0.55f + new Vector3(Jitter(random, 0.25f), Jitter(random, 0.25f), 0f), 0.12f, random);
            line.startColor = new Color(1f, 1f, 1f, 0.9f);
            line.endColor = new Color(Spark.r, Spark.g, Spark.b, 0.3f);
        }

        // 攻撃の判定の縁を走る稲妻（傾いた判定は傾けて）。
        private static void DrawAttackSpark(LineRenderer line, in CharacterState state, AttackHit hit, System.Random random)
        {
            HitResolver.Hitbox(state, hit, out System.Numerics.Vector2 center, out System.Numerics.Vector2 half);
            float angle = HitResolver.HitAngle(state, hit);
            const int perSide = 4;
            line.loop = true;
            line.positionCount = perSide * 4;
            for (int side = 0; side < 4; side++)
            {
                System.Numerics.Vector2 a = HitShapes.Corner(center, half, angle, side);
                System.Numerics.Vector2 b = HitShapes.Corner(center, half, angle, (side + 1) % 4);
                for (int p = 0; p < perSide; p++)
                {
                    float t = p / (float)perSide;
                    System.Numerics.Vector2 point = a + (b - a) * t;
                    line.SetPosition(side * perSide + p, new Vector3(point.X + Jitter(random, 0.12f), point.Y + Jitter(random, 0.12f), Z));
                }
            }
            line.startColor = line.endColor = new Color(Spark.r, Spark.g, Spark.b, 0.9f);
        }

        // 長押しのオーラ: 体のまわりの、ぎざぎざに揺れる輪（黄色）。turn（ラジアン）だけ回して描く。
        private static void DrawAura(LineRenderer line, Vector3 center, float halfWidth, float halfHeight, float turn, System.Random random)
        {
            line.loop = true;
            line.positionCount = AuraPoints;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f);
            for (int p = 0; p < AuraPoints; p++)
            {
                float angle = Mathf.PI * 2f * p / AuraPoints + turn;
                float spike = (p % 2 == 0 ? 0.3f : 0f) + Jitter(random, 0.12f);
                float rx = halfWidth + 0.45f + spike;
                float ry = halfHeight + 0.35f + spike;
                line.SetPosition(p, center + new Vector3(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry, Z));
            }
            line.startColor = line.endColor = new Color(StrongSpark.r, StrongSpark.g, StrongSpark.b, 0.6f + 0.35f * pulse);
        }

        // 突撃の跡（通った位置を残し、だんだん細く薄く）。
        private static void UpdateTrail(Effects effects, bool dashing, Vector3 center, bool charged)
        {
            effects.Dashing = dashing;
            LineRenderer trail = effects.Trail;
            if (!dashing)
            {
                effects.TrailCount = 0;
                trail.enabled = false;
                return;
            }
            for (int p = Mathf.Min(effects.TrailCount, TrailPoints - 1); p > 0; p--)
            {
                effects.TrailPositions[p] = effects.TrailPositions[p - 1];
            }
            effects.TrailPositions[0] = center + new Vector3(0f, 0f, Z);
            effects.TrailCount = Mathf.Min(effects.TrailCount + 1, TrailPoints);
            trail.enabled = effects.TrailCount >= 2;
            trail.positionCount = effects.TrailCount;
            for (int p = 0; p < effects.TrailCount; p++)
            {
                trail.SetPosition(p, effects.TrailPositions[p]);
            }
            Color color = charged ? StrongSpark : Spark;
            trail.widthMultiplier = charged ? 0.9f : 0.5f;
            trail.startColor = new Color(1f, 1f, 1f, 0.85f);
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
        }

        // from から to への稲妻（途中の点を横へずらす）。
        private static void Zigzag(LineRenderer line, Vector3 from, Vector3 to, float amplitude, System.Random random)
        {
            Vector3 along = to - from;
            Vector3 side = new Vector3(-along.y, along.x, 0f).normalized;
            line.loop = false;
            line.positionCount = BoltPoints;
            for (int p = 0; p < BoltPoints; p++)
            {
                float t = p / (float)(BoltPoints - 1);
                float offset = p == 0 || p == BoltPoints - 1 ? 0f : Jitter(random, amplitude);
                Vector3 point = from + along * t + side * offset;
                line.SetPosition(p, new Vector3(point.x, point.y, Z));
            }
        }

        private static float Jitter(System.Random random, float amount)
        {
            return (float)(random.NextDouble() * 2.0 - 1.0) * amount;
        }

        private static void SetStun(Effects effects, bool show)
        {
            effects.Stunned = show;
            foreach (LineRenderer bolt in effects.Bolts)
            {
                bolt.enabled = show;
            }
        }

        private static void SetLine(LineRenderer line, bool show)
        {
            line.enabled = show;
        }

        private static void Hide(Effects effects)
        {
            SetStun(effects, false);
            SetLine(effects.Inverter, effects.InverterOn = false);
            effects.Sparking = false;
            effects.SparkCount = 0;
            foreach (LineRenderer spark in effects.AttackSparks)
            {
                SetLine(spark, false);
            }
            effects.Charging = false;
            SetLine(effects.Aura, false);
            effects.Dashing = false;
            effects.TrailCount = 0;
            effects.Trail.enabled = false;
        }

        private Effects Create(int index)
        {
            var effects = new Effects
            {
                Bolts = new LineRenderer[Bolts],
                Inverter = NewLine(index, "Inverter", 0.06f),
                AttackSparks = new LineRenderer[AttackData.MaxHits],
                Trail = NewLine(index, "Trail", 0.5f),
                Aura = NewLine(index, "Aura", 0.08f),
            };
            for (int k = 0; k < Bolts; k++)
            {
                effects.Bolts[k] = NewLine(index, "StunBolt" + k, 0.08f);
            }
            for (int k = 0; k < effects.AttackSparks.Length; k++)
            {
                effects.AttackSparks[k] = NewLine(index, "AttackSpark" + k, 0.07f);
            }
            return effects;
        }

        private LineRenderer NewLine(int index, string name, float width)
        {
            LineRenderer line = Instantiate(_lineTemplate, transform);
            line.name = $"Electric{index}{name}";
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = width;
            line.enabled = false;
            return line;
        }
    }
}
