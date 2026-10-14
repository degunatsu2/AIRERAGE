using System.Collections;
using Airerage.Core.Input;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>ワイヤーが外れたときの表示（プレイテスト 2026-10-13 の #7）。</summary>
    public sealed class MissIndicatorTests
    {
        [UnityTest]
        public IEnumerator FiringIntoTheGround_ShowsMissAtTheHitPoint()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var miss = Object.FindFirstObjectByType<MissIndicatorView>();
            Assert.That(miss, Is.Not.Null, "MissIndicatorView がない");
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 0.3f)
            {
                yield return null; // 出現位置から落ちて着地するまで
            }

            // 真下（接続できない地面）へ撃つ。
            runner.InputOverride = new Repeat(SimInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(new System.Numerics.Vector2(0f, -1f)), 6);
            start = Time.realtimeSinceStartup;
            while (!miss.IsShowing && Time.realtimeSinceStartup - start < 3f)
            {
                yield return null;
            }

            Assert.That(miss.IsShowing, Is.True, "外れた表示が出ない");
            Assert.That(miss.LastLabel, Is.EqualTo("MISS"));
            Assert.That(miss.LastPoint.y, Is.LessThan(0.5f), "外れた位置が地面にない");
            Assert.That(Mathf.Abs(miss.LastPoint.x - runner.PlayerState.Position.X), Is.LessThan(0.5f), "外れた位置が真下にない");
        }

        private sealed class Repeat : ITickInputSource
        {
            private readonly SimInput _input;
            private int _left;

            public Repeat(SimInput input, int ticks)
            {
                _input = input;
                _left = ticks;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _input;
                return _left-- > 0;
            }
        }
    }
}
