using System.Collections;
using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// JUGGERNAUT の表示（ユーザーの案、2026-10-29。docs/14 1.7）: 一覧で選べる、戦車の衣装とロゴ、大きい体、地上技で体ごと回る、空中横の砲弾、
    /// Q の構え（弾道の段階・予測線・全体を見せるカメラ）と砲弾と爆発、E の地雷ともう一度 E の爆発、R のジェット機と場所のカーソルと N/A。
    /// Sandbox_Wire で CycleCharacter を呼ぶ。
    /// </summary>
    public sealed class JuggernautViewTests
    {
        [UnityTest]
        public IEnumerator Juggernaut_ShowsTheTank_Spin_Shells_Stance_Mines_AndMayday()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var effects = Object.FindFirstObjectByType<JuggernautEffectsView>();
            var zoom = Object.FindFirstObjectByType<CameraZoom>();
            Assert.That(effects, Is.Not.Null, "JUGGERNAUT の表示がない");
            Assert.That(effects.enabled, Is.True, "JUGGERNAUT の表示の見本が足りない");
            CharacterConfigAsset character = null;
            for (int i = 0; i < 7; i++)
            {
                character = runner.CycleCharacter();
            }
            Assert.That(character.DisplayName, Is.EqualTo(CharacterPresets.JuggernautName), "一覧に JUGGERNAUT がない");
            Assert.That(character.Costume, Is.EqualTo(CharacterCostume.Juggernaut));
            Assert.That(character.Logo, Is.Not.Null, "JUGGERNAUT のロゴがない");
            Assert.That(character.Tags, Is.EqualTo(new[] { "#地上戦", "#鈍重" }));
            yield return WaitSeconds(0.6f);
            ISimulationFeed feed = runner;
            CharacterView view = runner.PlayerView;
            CharacterConfig config = feed.GetConfig(0);
            Assert.That(view.transform.localScale.x, Is.EqualTo(config.Movement.HalfWidth * 2f).Within(0.01f), "体を大きく見せていない");
            Assert.That(view.GetComponent<Renderer>().shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),
                "戦車の衣装なのに箱そのものを見せている");

            // 地上技（TURRET SPIN）: 押し続けると、体ごと水平に回る。
            yield return WaitUntil(() => feed.GetState(0).Grounded && feed.GetState(0).Action.IsFree, 2f);
            runner.InputOverride = new Script(Repeat(new SimInput(0, 0, InputButtons.Attack), 40));
            var angles = new HashSet<int>();
            float start = Time.time;
            while (Time.time - start < 0.5f)
            {
                angles.Add(Mathf.RoundToInt(view.SpinDegrees / 30f));
                yield return null;
            }
            Assert.That(angles.Count, Is.GreaterThan(3), "回る地上技で体が回らない");
            yield return WaitUntil(() => feed.GetState(0).Action.IsFree, 2f);

            // Q（FLAK）: 構えると、斜め後ろの上に「強」、操作している人には弾道の予測線、カメラはステージ全体。
            yield return WaitUntil(() => feed.GetState(0).Grounded, 2f);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special1), default, default, default });
            yield return WaitSeconds(0.2f);
            Assert.That(feed.GetState(0).Combat.FlakStance, Is.True, "前提: Q で構える");
            Assert.That(effects.ShowsLevel(0), Is.True, "構えても弾道の段階を出さない");
            Assert.That(effects.LevelText(0), Is.EqualTo("強"), "初めの段階が強でない");
            Assert.That(effects.ShowsTrajectory(0), Is.True, "構えても弾道の予測線を出さない");
            Assert.That(zoom != null && zoom.InOverview, Is.True, "構えてもカメラがステージ全体を見せない");

            // 上で弩に。構えの間に Q で砲弾を撃ち、地面で爆発する。
            // 上へ撃つ（ダミーに当たらず、しばらく飛んでから落ちて爆発する）。
            var aim = new SimInput(0, 0, InputButtons.None).WithAim(new System.Numerics.Vector2(-0.3f, 1f));
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 127, InputButtons.None), aim, With(aim, InputButtons.Special1), aim });
            yield return WaitSeconds(0.2f);
            Assert.That(effects.LevelText(0), Is.EqualTo("弩"), "上で段階が変わらない");
            int shells = 0;
            int blasts = 0;
            start = Time.time;
            while (Time.time - start < 6f && blasts == 0)
            {
                shells = Mathf.Max(shells, effects.ShellCount);
                blasts = effects.BlastCount;
                yield return null;
            }
            Assert.That(shells, Is.GreaterThan(0), "フラックの砲弾を出さない");
            Assert.That(blasts, Is.GreaterThan(0), "砲弾が落ちても爆発を出さない");

            // 左右の移動で構えが解け、カメラが戻る。
            runner.InputOverride = new Script(Repeat(new SimInput(127, 0, InputButtons.None), 3));
            yield return WaitSeconds(0.2f);
            Assert.That(feed.GetState(0).Combat.FlakStance, Is.False, "前提: 動くと構えが解ける");
            Assert.That(effects.ShowsLevel(0), Is.False, "構えが解けても弾道の段階を出している");
            Assert.That(zoom.InOverview, Is.False, "構えが解けてもカメラが全体を見せたまま");

            // E（MINE TRAP）: 撃つと地雷を出し、もう一度 E で爆発する。
            yield return WaitUntil(() => feed.GetState(0).Action.IsFree, 2f);
            var down = new SimInput(0, 0, InputButtons.Special2).WithAim(new System.Numerics.Vector2(0.3f, -1f));
            runner.InputOverride = new Script(new List<SimInput> { down, default });
            yield return WaitSeconds(0.5f);
            Assert.That(effects.MineCount, Is.GreaterThan(0), "地雷を出さない");
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special2), default });
            blasts = 0;
            start = Time.time;
            while (Time.time - start < 0.5f && blasts == 0)
            {
                blasts = effects.BlastCount;
                yield return null;
            }
            Assert.That(blasts, Is.GreaterThan(0), "もう一度 E で地雷の爆発を出さない");

            // R（MAYDAY）: ジェット機が来て吊って上がり、場所を選ぶ間は体を隠してカーソルを出す。地面の中を選ぶと N/A。
            CharacterState state = runner.PlayerState;
            state.Combat.Gauge = config.Combat.SuperGaugeCost;
            runner.SetPlayerState(state);
            yield return WaitUntil(() => feed.GetState(0).Action.IsFree, 2f);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Super), default });
            bool jet = false;
            start = Time.time;
            while (Time.time - start < 2f && feed.GetState(0).Combat.MaydayPhase != JuggernautRules.MaydaySelect)
            {
                jet |= effects.ShowsJet(0);
                yield return null;
            }
            Assert.That(jet, Is.True, "メーデーでジェット機を出さない");
            Assert.That(feed.GetState(0).Combat.MaydayPhase, Is.EqualTo(JuggernautRules.MaydaySelect), "前提: 場所を選べる");
            // 入力が場所を選び始めるのは次のフレームの Update、表示はその LateUpdate（コルーチンは LateUpdate の前に戻る）。
            yield return null;
            yield return null;
            Assert.That(view.HiddenForAway, Is.True, "場所を選ぶ間に体を見せている");
            Assert.That(effects.ShowsCursor(0), Is.True, "場所を選ぶ間にカーソルを出さない");
            Assert.That(zoom.InOverview, Is.True, "場所を選ぶ間にカメラがステージ全体を見せない");

            var area = (ILocalAimSource)runner;
            JuggernautRules.TargetArea(area.Bounds, config.Skills.Juggernaut, out System.Numerics.Vector2 center, out System.Numerics.Vector2 half);
            System.Numerics.Vector2 at = feed.GetState(0).Combat.MaydayFrom;
            var underground = new System.Numerics.Vector2(at.X, at.Y - config.Movement.HalfHeight - 0.6f);
            SimInput pick = JuggernautRules.EncodeTarget(new SimInput(0, 0, InputButtons.None), underground, center, half);
            runner.InputOverride = new Script(new List<SimInput> { pick, With(pick, InputButtons.Attack), pick });
            yield return WaitSeconds(0.2f);
            Assert.That(effects.Denials, Is.GreaterThan(0), "地面の中を選んでも N/A を出さない");

            var sky = new System.Numerics.Vector2(at.X + 3f, at.Y + 4f);
            pick = JuggernautRules.EncodeTarget(new SimInput(0, 0, InputButtons.None), sky, center, half);
            runner.InputOverride = new Script(new List<SimInput> { pick, With(pick, InputButtons.Attack), pick });
            yield return WaitUntil(() => feed.GetState(0).Combat.MaydayPhase == 0, 3f);
            Assert.That(feed.GetState(0).Combat.MaydayPhase, Is.Zero, "空いた場所を選んでも運ばれ終わらない");
            Assert.That(feed.GetState(0).Position.X, Is.EqualTo(sky.X).Within(1f), "選んだ場所へ運ばれない");
            yield return null;
            Assert.That(view.HiddenForAway, Is.False, "降りた後も体を隠している");
            Assert.That(zoom.InOverview, Is.False, "降りた後もカメラが全体を見せたまま");
        }

        private static SimInput With(SimInput input, InputButtons buttons)
        {
            return new SimInput(input.MoveX, input.MoveY, buttons, input.AimX, input.AimY);
        }

        private static List<SimInput> Repeat(SimInput input, int count)
        {
            var list = new List<SimInput>();
            for (int i = 0; i < count; i++)
            {
                list.Add(input);
            }
            list.Add(default);
            return list;
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(System.Func<bool> done, float seconds)
        {
            float start = Time.time;
            while (!done() && Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private sealed class Script : ITickInputSource
        {
            private readonly List<SimInput> _inputs;
            private int _next;

            public Script(List<SimInput> inputs)
            {
                _inputs = inputs;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _next < _inputs.Count ? _inputs[_next] : default;
                return _next++ < _inputs.Count;
            }
        }
    }
}
