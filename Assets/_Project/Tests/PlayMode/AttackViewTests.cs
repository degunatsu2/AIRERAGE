using System.Collections;
using System.Collections.Generic;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Input;
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
    /// （docs/11_combat_plan.md T3-1）。入力は InputOverride で与える。
    /// </summary>
    public sealed class AttackViewTests
    {
        [UnityTest]
        public IEnumerator AttackPhases_AreShownOnTheCharacter()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var view = Object.FindFirstObjectByType<CharacterView>();
            var renderer = view.GetComponent<MeshRenderer>();

            // 着地を待ってから、地上で1回だけ攻撃ボタンを押す。
            yield return WaitSeconds(0.5f);
            Assert.That(runner.PlayerState.Grounded, Is.True, "前提: 地面に立っている");
            runner.InputOverride = new OneAttack(30);

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

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        // 最初のティックだけ攻撃ボタンを押し、その後は何も押さない入力（合わせて ticks ティック）。
        private sealed class OneAttack : ITickInputSource
        {
            private int _left;
            private bool _pressed;

            public OneAttack(int ticks)
            {
                _left = ticks;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _pressed ? default : new SimInput(0, 0, InputButtons.Attack);
                _pressed = true;
                return _left-- > 0;
            }
        }
    }
}
