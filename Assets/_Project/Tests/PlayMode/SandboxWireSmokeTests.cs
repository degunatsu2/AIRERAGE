using System.Collections;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Degunatsu.PlayMode.Tests
{
    /// <summary>
    /// Sandbox_Wire シーンを再生して、シミュレーションと表示がつながっていることを確かめる（入力は与えない）。
    /// エラーのログが出るとテストは失敗する（Unity Test Framework の既定の動作）。
    /// </summary>
    public sealed class SandboxWireSmokeTests
    {
        private const string SceneName = "Sandbox_Wire";

        // 地面（上面 y = 0）に立っているときの中心の高さ: 半分の高さ 0.9m + 地面との隙間 0.01m。
        private const float StandingY = 0.91f;

        [UnityTest]
        public IEnumerator Play_CharacterFallsAndStandsOnGround()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var view = Object.FindFirstObjectByType<CharacterView>();
            Assert.That(runner, Is.Not.Null, "SimulationRunner がない");
            Assert.That(runner.enabled, Is.True, "SimulationRunner が無効になっている（設定の不足）");
            Assert.That(view, Is.Not.Null, "CharacterView がない");

            float start = Time.time;
            while (Time.time - start < 1f)
            {
                yield return null;
            }

            Assert.That(view.transform.position.y, Is.EqualTo(StandingY).Within(0.02f), "地面に立っていない");
            Assert.That(view.transform.localScale, Is.EqualTo(new Vector3(0.8f, 1.8f, 0.8f)), "箱の大きさがキャラクターの当たり判定と違う");

            // 追従カメラ（Cinemachine）が、キャラクターの手前から横向きに映している。
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "MainCamera がない");
            Vector3 offset = camera.transform.position - view.transform.position;
            Assert.That(offset.z, Is.LessThan(-1f), "カメラがキャラクターの手前にない");
            Assert.That(Mathf.Abs(offset.x), Is.LessThan(2f), "カメラが横方向に追従していない");
            Assert.That(Mathf.Abs(offset.y), Is.LessThan(2f), "カメラが縦方向に追従していない");
            Assert.That(Quaternion.Angle(camera.transform.rotation, Quaternion.identity), Is.LessThan(0.01f), "カメラが回転している");
        }
    }
}
