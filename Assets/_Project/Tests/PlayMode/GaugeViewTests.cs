using System.Collections;
using System.Collections.Generic;
using Airerage.Core.Input;
using Airerage.Core.Wire;
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
    /// 円形のゲージ（ユーザーの要望、2026-10-13）: ワイヤーキャンセルのクールタイム（キャラクターの周り、「WIRE CANCEL」）と、
    /// ワイヤーの接続を保てる残り時間（接続点の周り）。入力は InputOverride で与える。
    /// </summary>
    public sealed class GaugeViewTests
    {
        [UnityTest]
        public IEnumerator WireCancel_ShowsTheCooldownGauge_UntilTheCooldownEnds()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            RingGaugeView gauge = runner.PlayerView.WireCancelGauge;
            Assert.That(gauge, Is.Not.Null, "ワイヤーキャンセルのゲージがない");
            Assert.That(gauge.Label, Is.EqualTo("WIRE CANCEL"));
            yield return WaitSeconds(0.5f);

            // 地上技（ダミーのいない左へ）を出し、判定が出てからワイヤーでキャンセルする。
            var inputs = new List<SimInput> { SimInput.FromAxes(-1f, 0f, InputButtons.Attack) };
            for (int i = 0; i < 7; i++)
            {
                inputs.Add(default);
            }
            inputs.Add(SimInput.FromAxes(0f, 1f, InputButtons.Wire));
            runner.InputOverride = new Script(inputs);
            yield return WaitUntil(() => runner.InputOverride == null, 2f);

            Assert.That(runner.PlayerState.Combat.WireCancelCooldownTicksLeft, Is.GreaterThan(0), "前提: ワイヤーでキャンセルした");
            yield return null;
            Assert.That(gauge.IsShowing, Is.True, "ゲージが出ない");
            float first = gauge.Fraction;
            Assert.That(first, Is.GreaterThan(0.9f));

            yield return WaitSeconds(1f);
            Assert.That(gauge.Fraction, Is.LessThan(first - 0.2f), "ゲージが減らない");

            yield return WaitUntil(() => runner.PlayerState.Combat.WireCancelCooldownTicksLeft == 0, 4f);
            yield return null;
            Assert.That(gauge.IsShowing, Is.False, "クールタイムが終わってもゲージが残っている");
        }

        [UnityTest]
        public IEnumerator TwoCooldownGauges_AreSpacedApart_WithLabelsStacked()
        {
            // ワイヤーキャンセルとガードのクールタイムが同時に出ると、古いゲージ（ワイヤーキャンセル）が外へずれ、文字は新しいゲージの上に積む
            // （ユーザーの指摘、2026-10-13・2026-10-30）。
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            RingGaugeView wire = runner.PlayerView.WireCancelGauge;
            RingGaugeView guard = runner.PlayerView.GuardGauge;
            yield return WaitSeconds(0.3f);

            var state = runner.PlayerState;
            state.Combat.WireCancelCooldownTicksLeft = 150;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.2f);
            state = runner.PlayerState;
            state.Combat.GuardCooldownTicksLeft = 100;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.5f);

            Assert.That(wire.IsShowing && guard.IsShowing, Is.True, "前提: 両方のゲージが出ている");
            Assert.That(guard.Radius, Is.EqualTo(guard.BaseRadius).Within(1e-3f), "新しいゲージ（ガード）は元の半径");
            Assert.That(wire.Radius - guard.Radius, Is.GreaterThanOrEqualTo((wire.Width + guard.Width) * 0.5f + RingGaugeStack.Gap - 1e-3f),
                "古いゲージ（ワイヤーキャンセル）が外へずれていない");
            Assert.That(guard.LabelOffset, Is.EqualTo(0f), "新しいゲージの文字がいちばん下にない");
            Assert.That(wire.LabelOffset, Is.GreaterThanOrEqualTo(guard.LabelHeight), "古いゲージの文字が上に積まれていない");

            yield return WaitUntil(() => runner.PlayerState.Combat.GuardCooldownTicksLeft == 0, 3f);
            yield return WaitSeconds(0.5f);
            Assert.That(guard.IsShowing, Is.False);
            Assert.That(wire.IsShowing, Is.True, "前提: ワイヤーキャンセルのゲージだけが残っている");
            Assert.That(wire.LabelOffset, Is.EqualTo(0f), "1つだけのときは文字を元の高さに戻す");
            Assert.That(wire.Radius, Is.EqualTo(1.3f).Within(1e-3f), "1つだけのときは元の半径に戻す");
        }

        [UnityTest]
        public IEnumerator ManyGaugesAroundACharacter_NeverOverlap_OlderOnesOutside()
        {
            // 体の周りのゲージを 1 つずつ増やすと、どの 2 つも重ならず、古いゲージほど外にある（ユーザーの指摘、2026-10-30: 一部が重なっていた）。
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            CharacterView view = runner.PlayerView;
            yield return WaitSeconds(0.3f);

            // 感電の残り（WALL のゲージ）・フィーバー（FEVER）・ワイヤーキャンセル・ガードの順に出す（WALL はワイヤーキャンセルと半径が近い）。
            var state = runner.PlayerState;
            state.Combat.StunTicksLeft = 600;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.15f);
            state = runner.PlayerState;
            state.Combat.CoinBoostTicksLeft = 600;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.15f);
            state = runner.PlayerState;
            state.Combat.WireCancelCooldownTicksLeft = 150;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.15f);
            state = runner.PlayerState;
            state.Combat.GuardCooldownTicksLeft = 100;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.5f);

            var gauges = new[] { view.WallClingGauge, view.FeverGauge, view.WireCancelGauge, view.GuardGauge };
            foreach (RingGaugeView gauge in gauges)
            {
                Assert.That(gauge.IsShowing, Is.True, $"前提: {gauge.Label} が出ている");
            }
            Assert.That(view.GaugeStack.Shown, Is.EqualTo(gauges), "古い順に並んでいない");
            for (int i = 0; i + 1 < gauges.Length; i++)
            {
                RingGaugeView older = gauges[i];
                RingGaugeView newer = gauges[i + 1];
                Assert.That(older.Radius - newer.Radius, Is.GreaterThanOrEqualTo((older.Width + newer.Width) * 0.5f + RingGaugeStack.Gap - 1e-3f),
                    $"{older.Label} が {newer.Label} の外にない（重なる）");
                Assert.That(older.LabelOffset, Is.GreaterThanOrEqualTo(newer.LabelOffset + newer.LabelHeight), $"{older.Label} の文字が {newer.Label} の文字の上にない");
            }
            Assert.That(view.GuardGauge.Radius, Is.EqualTo(view.GuardGauge.BaseRadius).Within(1e-3f), "いちばん新しいゲージは元の半径");
            // 文字の起点は、いちばん外の輪の右上（どの輪にもかからない）。
            RingGaugeView outer = gauges[0];
            foreach (RingGaugeView gauge in gauges)
            {
                Assert.That(gauge.LabelAnchor.y - gauge.Center.y, Is.GreaterThanOrEqualTo(outer.Radius + outer.Width * 0.5f - 1e-3f), $"{gauge.Label} の文字が輪にかかる");
            }

            // 間のゲージ（フィーバー）が消えたら、外のゲージは内へ詰める。
            state = runner.PlayerState;
            state.Combat.CoinBoostTicksLeft = 0;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.5f);
            Assert.That(view.FeverGauge.IsShowing, Is.False);
            RingGaugeView wall = view.WallClingGauge;
            RingGaugeView wire = view.WireCancelGauge;
            Assert.That(wall.Radius - wire.Radius, Is.EqualTo((wall.Width + wire.Width) * 0.5f + RingGaugeStack.Gap).Within(0.02f), "消えた後に詰めていない");
        }

        [UnityTest]
        public IEnumerator Attach_ShowsTheRemainingTimeGauge_AroundTheAnchor()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            WireView wireView = Object.FindFirstObjectByType<WireView>();
            RingGaugeView gauge = wireView.AttachGauge;
            Assert.That(gauge, Is.Not.Null, "接続の残り時間のゲージがない");
            yield return WaitSeconds(0.5f);

            // 出現位置の左上のアンカーポイント（-2, 10）へ、マウスの照準で射出して押し続ける。
            SimInput hold = SimInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(new System.Numerics.Vector2(-2f, 9f));
            runner.InputOverride = new Repeat(hold);
            yield return WaitUntil(() => runner.PlayerState.Wire.Phase == WirePhase.Attached, 2f);
            Assert.That(runner.PlayerState.Wire.Phase, Is.EqualTo(WirePhase.Attached), "前提: 接続した");
            yield return null;

            Assert.That(gauge.IsShowing, Is.True, "ゲージが出ない");
            Vector3 anchor = new Vector3(runner.PlayerState.Wire.Anchor.X, runner.PlayerState.Wire.Anchor.Y, 0f);
            Assert.That(Vector2.Distance(gauge.Center, anchor), Is.LessThan(0.01f), "ゲージの中心が接続点にない");
            float first = gauge.Fraction;
            yield return WaitSeconds(1f);
            Assert.That(gauge.Fraction, Is.LessThan(first - 0.2f), "ゲージが減らない");

            // 接続を保てる時間（MaxAttachTicks）が過ぎると外れて、ゲージも消える。
            yield return WaitUntil(() => runner.PlayerState.Wire.Phase != WirePhase.Attached, 4f);
            runner.InputOverride = null;
            yield return null;
            Assert.That(runner.PlayerState.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.MaxDuration));
            Assert.That(gauge.IsShowing, Is.False, "外れてもゲージが残っている");
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            float start = Time.time;
            while (!condition() && Time.time - start < timeoutSeconds)
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

        private sealed class Repeat : ITickInputSource
        {
            private readonly SimInput _input;

            public Repeat(SimInput input)
            {
                _input = input;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _input;
                return true;
            }
        }
    }
}
