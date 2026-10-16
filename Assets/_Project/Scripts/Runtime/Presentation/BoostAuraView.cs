using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 速さの強化と走る地上技の表示（STORM RUNNER。docs/14 1.2）。
    /// - ウインドクローク（CombatState.SpeedBoostTicksLeft）: 体の周りを回る黄緑の風と、残りの時間の弧。
    /// - キャッスルインジエアー（SkyBoostTicksLeft）: 体の周りを大きく回る空色の風と、残りの時間の弧（外側）。
    /// - 走り続ける地上技（AttackData.Run）: 後ろへ流れる風の線（速いほど長い）。
    /// 見た目の動きは表示の時間で決める。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class BoostAuraView : MonoBehaviour
    {
        private const int Points = 36;
        private const int StreakCount = 3;
        private const float Z = -0.65f;

        /// <summary>ウインドクロークの色（黄緑）とキャッスルインジエアーの色（空色）。</summary>
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
            public LineRenderer[] Cloak;
            public LineRenderer CloakArc;
            public LineRenderer[] Castle;
            public LineRenderer CastleArc;
            public LineRenderer[] Streaks;
            public bool ShowingCloak;
            public bool ShowingCastle;
            public bool ShowingRun;
        }

        /// <summary>キャラクター index にウインドクローク・キャッスルインジエアー・走りの表示を出しているか（テスト用）。</summary>
        public bool ShowsCloak(int index) => index >= 0 && index < _lines.Count && _lines[index].ShowingCloak;

        public bool ShowsCastle(int index) => index >= 0 && index < _lines.Count && _lines[index].ShowingCastle;

        public bool ShowsRun(int index) => index >= 0 && index < _lines.Count && _lines[index].ShowingRun;

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
            while (_lines.Count < count)
            {
                _lines.Add(Create(_lines.Count));
            }
            float time = Time.time;
            for (int i = 0; i < _lines.Count; i++)
            {
                Lines lines = _lines[i];
                if (i >= count)
                {
                    Show(lines, false, false, false);
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                bool alive = !state.Combat.IsWaitingToRespawn;
                bool cloak = alive && state.Combat.SpeedBoostTicksLeft > 0;
                bool castle = alive && state.Combat.SkyBoostTicksLeft > 0;
                bool run = alive && state.Action.Kind == ActionKind.Attack && config.Combat.Get(state.Action.Attack).Run;
                Show(lines, cloak, castle, run);
                var center = new Vector3(state.Position.X, state.Position.Y, 0f);
                if (cloak)
                {
                    DrawSwirl(lines.Cloak, center, 1.1f, 1.5f, time * 7f, CloakColor);
                    DrawArc(lines.CloakArc, center, 1.75f, state.Combat.SpeedBoostTicksLeft / (float)config.Skills.SpeedBoost.DurationTicks, CloakColor);
                }
                if (castle)
                {
                    DrawSwirl(lines.Castle, center, 1.6f, 2f, -time * 4f, CastleColor);
                    DrawArc(lines.CastleArc, center, 2.35f, state.Combat.SkyBoostTicksLeft / (float)config.Skills.SkyBoost.DurationTicks, CastleColor);
                }
                if (run)
                {
                    float speed = state.Action.RunSpeed * SpeedBoosts.ScaleOf(state, config);
                    DrawStreaks(lines.Streaks, center, state.Facing, speed / config.Skills.Run.MaxSpeed, time);
                }
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
                var start = new Color(color.r, color.g, color.b, 0.05f);
                var end = new Color(color.r, color.g, color.b, 0.75f);
                line.startColor = start;
                line.endColor = end;
            }
        }

        // 残りの時間の弧（上から時計回りに減る）。
        private static void DrawArc(LineRenderer line, Vector3 center, float radius, float fraction, Color color)
        {
            fraction = Mathf.Clamp01(fraction);
            int points = Mathf.Max(2, Mathf.CeilToInt(48 * fraction) + 1);
            line.positionCount = points;
            for (int k = 0; k < points; k++)
            {
                float a = Mathf.PI * 2f * fraction * k / (points - 1);
                line.SetPosition(k, center + new Vector3(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius, Z));
            }
            var c = new Color(color.r, color.g, color.b, 0.55f);
            line.startColor = c;
            line.endColor = c;
        }

        // 走っている後ろへ流れる線（速いほど長く、濃い）。
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
                line.startColor = new Color(1f, 1f, 1f, 0.25f + 0.5f * speed01);
                line.endColor = new Color(0.7f, 1f, 0.5f, 0f);
            }
        }

        private static void Show(Lines lines, bool cloak, bool castle, bool run)
        {
            if (lines.ShowingCloak != cloak)
            {
                SetEnabled(lines.Cloak, cloak);
                lines.CloakArc.enabled = cloak;
                lines.ShowingCloak = cloak;
            }
            if (lines.ShowingCastle != castle)
            {
                SetEnabled(lines.Castle, castle);
                lines.CastleArc.enabled = castle;
                lines.ShowingCastle = castle;
            }
            if (lines.ShowingRun != run)
            {
                SetEnabled(lines.Streaks, run);
                lines.ShowingRun = run;
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
            var lines = new Lines
            {
                Cloak = new[] { NewLine(index, "Cloak0", 0.07f), NewLine(index, "Cloak1", 0.07f) },
                CloakArc = NewLine(index, "CloakArc", 0.06f),
                Castle = new[] { NewLine(index, "Castle0", 0.1f), NewLine(index, "Castle1", 0.1f) },
                CastleArc = NewLine(index, "CastleArc", 0.08f),
                Streaks = new LineRenderer[StreakCount],
            };
            for (int s = 0; s < StreakCount; s++)
            {
                lines.Streaks[s] = NewLine(index, "Streak" + s, 0.07f);
            }
            return lines;
        }

        private LineRenderer NewLine(int index, string name, float width)
        {
            LineRenderer line = Instantiate(_lineTemplate, transform);
            line.name = $"Boost{index}{name}";
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = width;
            line.enabled = false;
            return line;
        }
    }
}
