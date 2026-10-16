using System.Collections;
using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Simulation;
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
    /// Sandbox_Wire で攻撃ボタンを押すと、キャラクターの色が攻撃の段階（発生 → 持続 → 硬直）で変わり、終わると戻る
    /// （docs/11_combat_plan.md T3-1）。ダミーの方へ歩いて地上技を出すと当たる（T3-2）。入力は InputOverride で与える。
    /// </summary>
    public sealed class AttackViewTests
    {
        [UnityTest]
        public IEnumerator AttackPhases_AreShownOnTheCharacter()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            CharacterView view = runner.PlayerView;
            var renderer = view.GetComponent<MeshRenderer>();

            // 着地を待ってから、地上で1回だけ攻撃ボタンを押す（ダミーのいない左へ）。
            yield return WaitSeconds(0.5f);
            Assert.That(runner.PlayerState.Grounded, Is.True, "前提: 地面に立っている");
            runner.InputOverride = new OneAttack(45);

            var seen = new List<AttackPhase>();
            var block = new MaterialPropertyBlock();
            float start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 3f)
            {
                if (view.ShownPhase.HasValue && (seen.Count == 0 || seen[seen.Count - 1] != view.ShownPhase.Value))
                {
                    seen.Add(view.ShownPhase.Value);
                    renderer.GetPropertyBlock(block);
                    Assert.That(block.isEmpty, Is.False, $"{view.ShownPhase.Value} の色が付いていない");
                }
                yield return null;
            }

            Assert.That(seen, Is.EqualTo(new[] { AttackPhase.Startup, AttackPhase.Active, AttackPhase.Recovery }));
            Assert.That(runner.PlayerState.Action.IsFree, Is.True, "攻撃が終わっていない");
            Assert.That(view.ShownPhase, Is.Null, "攻撃が終わっても色が戻らない");
            renderer.GetPropertyBlock(block);
            Assert.That(block.isEmpty, Is.True, "攻撃が終わっても色が残っている");
        }

        [UnityTest]
        public IEnumerator FacingMarker_ShowsWhichWayTheCharacterFaces()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            CharacterView view = runner.PlayerView;
            Transform visor = view.transform.Find("Facing/Visor");
            Assert.That(visor, Is.Not.Null, "向きの印（目の帯）がない");
            Assert.That(view.transform.Find("Facing/Nose"), Is.Not.Null, "向きの印（突き出た印）がない");
            yield return WaitSeconds(0.5f);
            Assert.That(view.ShowsFacingMarker, Is.True, "向きの印を見せていない");

            // 左へ歩くと、印が左に寄る。右へ歩くと右に寄る。
            foreach (sbyte x in new sbyte[] { -SimInput.AxisMax, SimInput.AxisMax })
            {
                var walk = new List<SimInput>();
                for (int t = 0; t < 10; t++)
                {
                    walk.Add(new SimInput(x, 0, InputButtons.None));
                }
                runner.InputOverride = new Script(walk);
                yield return WaitSeconds(0.3f);
                int facing = x < 0 ? -1 : 1;
                Assert.That(runner.PlayerState.Facing, Is.EqualTo(facing), "前提: 歩いた方を向いている");
                Assert.That(view.ShownFacing, Is.EqualTo(facing), "印の向きがキャラクターの向きと違う");
                Assert.That(Mathf.Sign(visor.position.x - view.transform.position.x), Is.EqualTo((float)facing), "目の帯が向いている側にない");
                Assert.That(visor.position.z, Is.LessThan(view.transform.position.z), "目の帯がカメラの側の面にない");
            }
        }

        [UnityTest]
        public IEnumerator GroundAttack_HitsTheDummy()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            Assert.That(runner.DummyCount, Is.EqualTo(1), "ダミーがいない");
            yield return WaitSeconds(0.5f);
            Assert.That(runner.GetDummyState(0).Grounded, Is.True, "前提: ダミーが地面に立っている");

            // ダミーの手前まで歩き、地上技を出す。
            var inputs = new List<SimInput>();
            for (int i = 0; i < 12; i++)
            {
                inputs.Add(SimInput.FromAxes(1f, 0f, InputButtons.None));
            }
            inputs.Add(SimInput.FromAxes(1f, 0f, InputButtons.Attack));
            for (int i = 0; i < 60; i++)
            {
                inputs.Add(default);
            }
            runner.InputOverride = new Script(inputs);

            CharacterView dummyView = null;
            foreach (CharacterView view in Object.FindObjectsByType<CharacterView>(FindObjectsSortMode.None))
            {
                dummyView = view != runner.PlayerView ? view : dummyView;
            }
            bool sawHitbox = false;
            bool sawHit = false;
            float start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 3f)
            {
                sawHitbox |= runner.PlayerView.ShowingHitbox;
                sawHit |= dummyView.ShowingHit;
                yield return null;
            }

            Assert.That(sawHitbox, Is.True, "攻撃の判定が描かれなかった");
            Assert.That(sawHit, Is.True, "ダミーにのけぞりの色が出なかった");
            CharacterState dummy = runner.GetDummyState(0);
            Assert.That(dummy.Combat.HasLastAttacker, Is.True, "ダミーに当たっていない");
            Assert.That(dummy.Combat.LastAttacker, Is.EqualTo(0), "当てたのはプレイヤー（番号 0）");
            Assert.That(dummy.Position.X, Is.GreaterThan(4.5f), "ダミーが吹き飛んでいない");
            Assert.That(runner.LastPlayerHit.HasValue, Is.True, "最後に当てた命中を覚えていない（デバッグ表示用）");
            Assert.That(runner.LastPlayerHit.Value.Attack, Is.EqualTo(AttackSlot.Ground));
        }

        [UnityTest]
        public IEnumerator KoMove_ShowsTheChargeEffect_OnlyWhileCharging()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            ChargeEffectView charge = runner.PlayerView.ChargeEffect;
            Assert.That(charge, Is.Not.Null, "チャージのエフェクトがない");
            yield return WaitSeconds(0.5f);

            // ジャンプして、上昇中に下＋攻撃（デフォルトのキャラクターの撃破技。ダミーのいない左を向く）。
            var inputs = new List<SimInput>();
            inputs.Add(SimInput.FromAxes(-1f, 0f, InputButtons.Jump));
            for (int i = 0; i < 4; i++)
            {
                inputs.Add(default);
            }
            inputs.Add(SimInput.FromAxes(0f, -1f, InputButtons.Attack));
            for (int i = 0; i < 60; i++)
            {
                inputs.Add(default);
            }
            runner.InputOverride = new Script(inputs);

            bool sawCharge = false;
            bool chargeOutsideStartup = false;
            float start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 3f)
            {
                CharacterState state = runner.PlayerState;
                bool charging = state.Action.Kind == ActionKind.Attack && state.Action.Attack == AttackSlot.AirDown
                    && runner.PlayerView.ShownPhase == AttackPhase.Startup;
                sawCharge |= charge.IsShowing;
                chargeOutsideStartup |= charge.IsShowing && !charging;
                yield return null;
            }

            Assert.That(sawCharge, Is.True, "チャージのエフェクトが出なかった");
            Assert.That(chargeOutsideStartup, Is.False, "チャージの後もエフェクトが残った");
            Assert.That(charge.IsShowing, Is.False);
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
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

        // 最初のティックだけ左に入れて攻撃ボタンを押し、その後は何も押さない入力（合わせて ticks ティック）。
        private sealed class OneAttack : ITickInputSource
        {
            private const sbyte PlayerInputMax = SimInput.AxisMax;

            private int _left;
            private bool _pressed;

            public OneAttack(int ticks)
            {
                _left = ticks;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _pressed ? default : new SimInput(-PlayerInputMax, 0, InputButtons.Attack);
                _pressed = true;
                return _left-- > 0;
            }
        }
    }
}
