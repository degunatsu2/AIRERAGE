using System.Collections;
using System.Collections.Generic;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Input;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.PlayMode.Tests
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
