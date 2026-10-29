using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// STORM RUNNER の風の表示（docs/14 1.2）。風の線には濃い緑の影を付ける（WindShadow。ユーザーの要望、2026-10-16）。
    /// - ウインドクローク（CombatState.SpeedBoostTicksLeft）: 体の周りを回る黄緑の風と、残りの時間の円形のゲージ（縁つき）。
    /// - キャッスルインジエアー（SkyBoostTicksLeft）: 体の周りを大きく回る空色の風と、残りの時間の円形のゲージ（縁つき。外側）。
    /// - 走り続ける地上技（AttackData.Run）: 黄緑のオーラと、後ろへ流れる黄緑の風の線（速いほど長い）。
    /// - 風のキャラクター（STORM RUNNER）の回る技（AttackData.SpinPerHit。つむじ風）: 体を包む竜巻（キャラクターの回転は CharacterView）。
    /// - 突風の溜め（ActionState.Recast）: 体に集まってくる風の輪。
    /// 見た目の動きは表示の時間で決める。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class BoostAuraView : MonoBehaviour
    {
        private const int Points = 36;
        private const int StreakCount = 3;
        private const int TornadoLines = 3;
        private const int TornadoPoints = 40;
        private const float Z = -0.65f;
        private const float ArcWidth = 0.16f;

        /// <summary>ウインドクローク・走りの色（黄緑）とキャッスルインジエアーの色（空色）。</summary>
        public static readonly Color CloakColor = new Color(0.6f, 1f, 0.3f);
        public static readonly Color CastleColor = new Color(0.5f, 0.85f, 1f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("線の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private LineRenderer _lineTemplate;

        private readonly List<Lines> _lines = new List<Lines>();
        private ISimulationFeed _feed;

        private sealed class Lines
        {
            public readonly List<WindShadow> Winds = new List<WindShadow>();
            public LineRenderer[] Cloak;
            public SlantedRingGauge CloakGauge;
            public LineRenderer[] Castle;
            public SlantedRingGauge CastleGauge;
            public LineRenderer RunAura;
            public LineRenderer[] Streaks;
            public LineRenderer[] Tornado;
            public LineRenderer[] Gather;
            public bool ShowingCloak;
            public bool ShowingCastle;
            public bool ShowingRun;
            public bool ShowingTornado;
            public bool ShowingGather;
        }

        /// <summary>キャラクター index に、それぞれの表示を出しているか（テスト用）。</summary>
        public bool ShowsCloak(int index) => index >= 0 && index < _lines.Count && _lines[index].ShowingCloak;

        public bool ShowsCastle(int index) => index >= 0 && index < _lines.Count && _lines[index].ShowingCastle;

        public bool ShowsRun(int index) => index >= 0 && index < _lines.Count && _lines[index].ShowingRun;

        public bool ShowsTornado(int index) => index >= 0 && index < _lines.Count && _lines[index].ShowingTornado;

        public bool ShowsGustWindup(int index) => index >= 0 && index < _lines.Count && _lines[index].ShowingGather;

        /// <summary>キャラクター index のウインドクロークの残りのゲージに縁を描いているか（テスト用）。</summary>
        public bool CloakGaugeHasFrame(int index) => ShowsCloak(index) && _lines[index].CloakGauge.ShowsFrame;

        /// <summary>キャラクター index の風の線に影を付けているか（出している線がすべて影を持つ。テスト用）。</summary>
        public bool WindsHaveShadows(int index)
        {
            if (index < 0 || index >= _lines.Count)
            {
                return false;
            }
            foreach (WindShadow wind in _lines[index].Winds)
            {
                if (wind.Main.enabled && !wind.Shadow.enabled)
                {
                    return false;
                }
            }
            return true;
        }

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

        private void OnDestroy()
        {
            foreach (Lines lines in _lines)
            {
                lines.CloakGauge.Destroy();
                lines.CastleGauge.Destroy();
            }
        }

        private void LateUpdate()
        {
            int count = _feed.CharacterCount;
            while (_lines.Count < count)
            {
                _lines.Add(Create(_lines.Count));
            }
            float time = Time.time;
            for (int i = 0; i < _lines.Count; i++)
            {
                Lines lines = _lines[i];
                // 隠した後の、終わりの白い輪（ゲージを出している間は何もしない）。
                lines.CloakGauge.Advance();
                lines.CastleGauge.Advance();
                if (i >= count)
                {
                    Show(lines, false, false, false, false, false);
                    SyncShadows(lines);
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                // 透明の間（THIEF の E）も出さない。
                bool alive = !state.Combat.IsWaitingToRespawn && state.Combat.InvisibleTicksLeft <= 0;
                bool attacking = alive && state.Action.Kind == ActionKind.Attack;
                AttackData attack = attacking ? config.Combat.Get(state.Action.Attack) : null;
                bool cloak = alive && state.Combat.SpeedBoostTicksLeft > 0;
                bool castle = alive && state.Combat.SkyBoostTicksLeft > 0;
                bool run = attacking && attack.Run;
                // 竜巻は風のキャラクター（Q が行動の速さの強化＝ウインドクローク。STORM RUNNER）の回る技だけ（PASTRY CHEF のゴムベラ回転には
                // 出さない。ユーザーの指摘、2026-10-22）。
                bool windy = config.Combat.Special1.Effect == SkillEffect.SpeedBoost;
                bool tornado = attacking && windy && attack.SpinPerHit && attack.Hits.Length > 0 && state.Action.Ticks < SpinEndTick(attack) + 4;
                SpeedBoostTuning boost = config.Skills.SpeedBoost;
                bool gather = attacking && state.Action.Recast && state.Action.Ticks < boost.GustWindupTicks;
                Show(lines, cloak, castle, run, tornado, gather);
                var center = new Vector3(state.Position.X, state.Position.Y, 0f);
                if (cloak)
                {
                    DrawSwirl(lines.Cloak, center, 1.1f, 1.5f, time * 7f, CloakColor);
                    DrawGauge(lines.CloakGauge, center, 1.85f, state.Combat.SpeedBoostTicksLeft / (float)boost.DurationTicks, CloakColor);
                }
                if (castle)
                {
                    DrawSwirl(lines.Castle, center, 1.6f, 2f, -time * 4f, CastleColor);
                    DrawGauge(lines.CastleGauge, center, 2.45f, state.Combat.SkyBoostTicksLeft / (float)config.Skills.SkyBoost.DurationTicks, CastleColor);
                }
                if (run)
                {
                    float speed = state.Action.RunSpeed * SpeedBoosts.ScaleOf(state, config);
                    float speed01 = speed / config.Skills.Run.MaxSpeed;
                    DrawFlame(lines.RunAura, center, 0.95f, 1.35f, 0.12f + 0.1f * speed01, time, CloakColor);
                    DrawStreaks(lines.Streaks, center, state.Facing, speed01, time);
                }
                if (tornado)
                {
                    DrawTornado(lines.Tornado, center, time);
                }
                if (gather)
                {
                    DrawGather(lines.Gather, center, state.Action.Ticks / (float)Mathf.Max(1, boost.GustWindupTicks));
                }
                SyncShadows(lines);
            }
        }

        /// <summary>回る技の、最後の判定が終わるティック（ここで元の向きに戻る）。</summary>
        public static int SpinEndTick(AttackData attack)
        {
            AttackHit last = attack.Hits[attack.Hits.Length - 1];
            return last.StartTick + last.ActiveTicks;
        }

        /// <summary>
        /// 回る技の、攻撃を始めてから ticks ティック目の回転（度。判定 1 つにつき 360 度。最後の判定の後は 360 × 判定の数で止まる）。
        /// 判定 i の始まりから次の判定の始まりまで（最後は判定が終わるまで）に 1 回転する。
        /// </summary>
        public static float SpinAngle(AttackData attack, float ticks)
        {
            AttackHit[] hits = attack.Hits;
            if (hits.Length == 0 || ticks <= hits[0].StartTick)
            {
                return 0f;
            }
            for (int i = 0; i < hits.Length; i++)
            {
                float from = hits[i].StartTick;
                float to = i + 1 < hits.Length ? hits[i + 1].StartTick : hits[i].StartTick + hits[i].ActiveTicks;
                if (ticks < to)
                {
                    return 360f * (i + Mathf.Clamp01((ticks - from) / Mathf.Max(1f, to - from)));
                }
            }
            return 360f * hits.Length;
        }

        private static void SyncShadows(Lines lines)
        {
            foreach (WindShadow wind in lines.Winds)
            {
                wind.Sync();
            }
        }

        // 体の周りを回る風（傾いた楕円を 2 本、逆の位相で）。
        private static void DrawSwirl(LineRenderer[] swirl, Vector3 center, float rx, float ry, float phase, Color color)
        {
            for (int s = 0; s < swirl.Length; s++)
            {
                LineRenderer line = swirl[s];
                line.positionCount = Points;
                float tilt = 0.5f * (s == 0 ? 1f : -1f);
                float spin = phase + s * Mathf.PI;
                for (int k = 0; k < Points; k++)
                {
                    float a = Mathf.PI * 2f * k / (Points - 1) + spin;
                    float x = Mathf.Cos(a) * rx;
                    float y = Mathf.Sin(a) * ry * 0.45f;
                    line.SetPosition(k, center + new Vector3(x * Mathf.Cos(tilt) - y * Mathf.Sin(tilt), x * Mathf.Sin(tilt) + y * Mathf.Cos(tilt), Z));
                }
                // 回る向きに合わせて濃さを変える（流れて見えるように）。
                line.startColor = new Color(color.r, color.g, color.b, 0.1f);
                line.endColor = new Color(color.r, color.g, color.b, 0.8f);
            }
        }

        // 残りの時間の円形のゲージ（ほかの円形ゲージと同じ見た目。SlantedRingGauge。D-76）。
        private static void DrawGauge(SlantedRingGauge gauge, Vector3 center, float radius, float fraction, Color color)
        {
            gauge.Draw(center, radius, ArcWidth, fraction, new Color(color.r, color.g, color.b, 0.95f), Z);
        }

        // 揺らめくオーラ（炎のように、上の側ほど大きく揺れて伸びる）。
        private static void DrawFlame(LineRenderer line, Vector3 center, float rx, float ry, float amp, float time, Color color)
        {
            line.positionCount = Points;
            for (int k = 0; k < Points; k++)
            {
                float angle = Mathf.PI * 2f * k / Points;
                float up = 0.5f + 0.5f * Mathf.Cos(angle);
                float noise = Mathf.PerlinNoise(angle * 1.7f + time * 4f, time * 2.6f);
                float r = 1f + amp * (noise - 0.3f) * (0.6f + up);
                float lift = amp * up * up * (0.6f + 0.4f * Mathf.Sin(time * 13f + angle * 5f));
                line.SetPosition(k, center + new Vector3(Mathf.Sin(angle) * rx * r, Mathf.Cos(angle) * ry * r + lift, Z));
            }
            var c = new Color(color.r, color.g, color.b, 0.6f);
            line.startColor = c;
            line.endColor = c;
        }

        // 走っている後ろへ流れる黄緑の線（速いほど長く、濃い）。
        private static void DrawStreaks(LineRenderer[] streaks, Vector3 center, int facing, float speed01, float time)
        {
            speed01 = Mathf.Clamp01(speed01);
            for (int s = 0; s < streaks.Length; s++)
            {
                LineRenderer line = streaks[s];
                line.positionCount = 2;
                float y = (s - 1) * 0.55f + 0.08f * Mathf.Sin(time * 20f + s * 2f);
                float length = 0.8f + 2.6f * speed01;
                float start = 0.5f + 0.3f * Mathf.Repeat(time * 6f + s * 0.37f, 1f);
                line.SetPosition(0, center + new Vector3(-facing * start, y, Z));
                line.SetPosition(1, center + new Vector3(-facing * (start + length), y, Z));
                line.startColor = new Color(CloakColor.r, CloakColor.g, CloakColor.b, 0.35f + 0.55f * speed01);
                line.endColor = new Color(CloakColor.r, CloakColor.g, CloakColor.b, 0f);
            }
        }

        // 体を包む竜巻（下が細く上が太いらせん。速く回る）。
        private static void DrawTornado(LineRenderer[] tornado, Vector3 center, float time)
        {
            for (int s = 0; s < tornado.Length; s++)
            {
                LineRenderer line = tornado[s];
                line.positionCount = TornadoPoints;
                float phase = time * 14f + s * Mathf.PI * 2f / tornado.Length;
                for (int k = 0; k < TornadoPoints; k++)
                {
                    float u = k / (float)(TornadoPoints - 1);
                    float height = Mathf.Lerp(-1.3f, 2f, u);
                    float radius = Mathf.Lerp(0.5f, 1.8f, u * u);
                    float a = phase + u * Mathf.PI * 5f;
                    line.SetPosition(k, center + new Vector3(Mathf.Cos(a) * radius, height, Z + Mathf.Sin(a) * radius * 0.3f));
                }
                line.startColor = new Color(CloakColor.r, CloakColor.g, CloakColor.b, 0.35f);
                line.endColor = new Color(0.85f, 1f, 0.7f, 0.85f);
            }
        }

        // 突風の溜め: 外から体へ集まってくる風の輪（progress は溜めの進み具合 0〜1）。
        private static void DrawGather(LineRenderer[] rings, Vector3 center, float progress)
        {
            for (int r = 0; r < rings.Length; r++)
            {
                LineRenderer line = rings[r];
                float p = Mathf.Repeat(progress * 1.5f + r * 0.5f, 1f);
                float radius = Mathf.Lerp(2.4f, 0.5f, p);
                line.positionCount = Points;
                for (int k = 0; k < Points; k++)
                {
                    float a = Mathf.PI * 2f * k / Points;
                    line.SetPosition(k, center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, Z));
                }
                var c = new Color(CloakColor.r, CloakColor.g, CloakColor.b, 0.25f + 0.65f * p);
                line.startColor = c;
                line.endColor = c;
            }
        }

        private static void Show(Lines lines, bool cloak, bool castle, bool run, bool tornado, bool gather)
        {
            if (lines.ShowingCloak != cloak)
            {
                SetEnabled(lines.Cloak, cloak);
                SetGauge(lines.CloakGauge, cloak);
                lines.ShowingCloak = cloak;
            }
            if (lines.ShowingCastle != castle)
            {
                SetEnabled(lines.Castle, castle);
                SetGauge(lines.CastleGauge, castle);
                lines.ShowingCastle = castle;
            }
            if (lines.ShowingRun != run)
            {
                SetEnabled(lines.Streaks, run);
                lines.RunAura.enabled = run;
                lines.ShowingRun = run;
            }
            if (lines.ShowingTornado != tornado)
            {
                SetEnabled(lines.Tornado, tornado);
                lines.ShowingTornado = tornado;
            }
            if (lines.ShowingGather != gather)
            {
                SetEnabled(lines.Gather, gather);
                lines.ShowingGather = gather;
            }
        }

        private static void SetGauge(SlantedRingGauge gauge, bool enabled)
        {
            if (!enabled)
            {
                gauge.Hide();
            }
        }

        private static void SetEnabled(LineRenderer[] lines, bool enabled)
        {
            foreach (LineRenderer line in lines)
            {
                line.enabled = enabled;
            }
        }

        private Lines Create(int index)
        {
            var lines = new Lines();
            lines.Cloak = new[] { NewWind(lines, index, "Cloak0", 0.07f, true), NewWind(lines, index, "Cloak1", 0.07f, true) };
            lines.CloakGauge = NewGauge(index, "CloakGauge");
            lines.Castle = new[] { NewWind(lines, index, "Castle0", 0.1f, true), NewWind(lines, index, "Castle1", 0.1f, true) };
            lines.CastleGauge = NewGauge(index, "CastleGauge");
            lines.RunAura = NewWind(lines, index, "RunAura", 0.09f, true);
            lines.Streaks = new LineRenderer[StreakCount];
            for (int s = 0; s < StreakCount; s++)
            {
                lines.Streaks[s] = NewWind(lines, index, "Streak" + s, 0.08f, false);
            }
            lines.Tornado = new LineRenderer[TornadoLines];
            for (int s = 0; s < TornadoLines; s++)
            {
                lines.Tornado[s] = NewWind(lines, index, "Tornado" + s, 0.08f, false);
            }
            lines.Gather = new[] { NewWind(lines, index, "Gather0", 0.08f, true), NewWind(lines, index, "Gather1", 0.08f, true) };
            return lines;
        }

        private LineRenderer NewWind(Lines lines, int index, string name, float width, bool loop)
        {
            LineRenderer line = NewLine(index, name, width, loop);
            lines.Winds.Add(new WindShadow(line));
            return line;
        }

        private SlantedRingGauge NewGauge(int index, string name)
        {
            return new SlantedRingGauge(this, $"Boost{index}{name}", _lineTemplate.sharedMaterial, 2);
        }

        private LineRenderer NewLine(int index, string name, float width, bool loop)
        {
            LineRenderer line = Instantiate(_lineTemplate, transform);
            line.name = $"Boost{index}{name}";
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = width;
            line.enabled = false;
            return line;
        }
    }
}
