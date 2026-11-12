using System.Collections;
using Airerage.Runtime.DebugTools;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
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
            CharacterView view = runner != null ? runner.PlayerView : null;
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

            // 追従カメラ（Cinemachine）が、設定した距離だけ手前から、横向きに映している。
            Camera camera = Camera.main;
            var composer = Object.FindFirstObjectByType<CinemachinePositionComposer>();
            Assert.That(camera, Is.Not.Null, "MainCamera がない");
            Assert.That(composer, Is.Not.Null, "CinemachinePositionComposer がない");
            Vector3 offset = camera.transform.position - view.transform.position;
            Assert.That(-offset.z, Is.EqualTo(composer.CameraDistance).Within(0.05f), "カメラが Cinemachine の距離にない");
            Assert.That(Mathf.Abs(offset.x), Is.LessThan(2f), "カメラが横方向に追従していない");
            Assert.That(Mathf.Abs(offset.y), Is.LessThan(2f), "カメラが縦方向に追従していない");
            Assert.That(Quaternion.Angle(camera.transform.rotation, Quaternion.identity), Is.LessThan(0.01f), "カメラが回転している");

            // 出現位置はスタートの範囲の中なので、計測は「スタートを出るのを待つ」状態になっている。
            var courseTimer = Object.FindFirstObjectByType<CourseTimerHud>();
            Assert.That(courseTimer, Is.Not.Null, "CourseTimerHud がない");
            Assert.That(courseTimer.Timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Ready));

            // 照準のインジケーター: 射出できる状態なので導線（四角い点線）と照準器と結果の文字が出ていて、射程の弧はキャラクターを中心に
            // 射程の半径。太さは画面の大きさで決まる（1 ピクセルの大きさを、カメラの距離から求めている）。
            var aim = Object.FindFirstObjectByType<AimIndicatorView>();
            Assert.That(aim, Is.Not.Null, "AimIndicatorView がない");
            Assert.That(aim.Aiming, Is.True, "照準が出ていない");
            Assert.That(aim.RangeShown, Is.True, "射程の弧が出ていない");
            Assert.That(aim.DashCount, Is.GreaterThan(0), "導線の点線がない");
            Assert.That(aim.ResultLabel, Is.EqualTo("LOCK").Or.EqualTo("BLOCK").Or.EqualTo("OUT"), "結果の文字がない");
            Assert.That(aim.ResultColor.a, Is.GreaterThan(0f).And.LessThan(1f), "照準が半透明でない");
            Assert.That(aim.RangeRadius, Is.EqualTo(runner.PlayerConfig.Wire.MaxLength).Within(0.001f), "射程の半径が射程と違う");
            Assert.That(Vector3.Distance(aim.Origin, view.transform.position), Is.LessThan(1f), "射程の中心がキャラクターにない");
            float expected = 2f * composer.CameraDistance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / 1080f;
            Assert.That(aim.MetersPerPixel, Is.EqualTo(expected).Within(expected * 0.1f), "太さが画面の大きさに合っていない");
            Assert.That(aim.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0), "照準の形がない");

            // パッシブアイテムのロングフックで射程が伸びたら、射程の弧も同じだけ伸びる（2026-11-06 の指摘。もとの調整値で描いていた）。
            Airerage.Core.Simulation.CharacterState hooked = runner.PlayerState;
            hooked.Items = Airerage.Core.Items.ItemRules.StartingBag(new[] { Airerage.Core.Items.ItemId.LongHook }, 1);
            runner.SetPlayerState(hooked);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }
            float longer = runner.PlayerConfig.Wire.MaxLength * (1f + new Airerage.Core.Items.ItemTuning().LongHookPercent / 100f);
            Assert.That(aim.RangeRadius, Is.EqualTo(longer).Within(0.001f), "ロングフックの射程が照準の弧に出ない");

            // カーソルの印（2026-11-12）: 照準器（途中の物に当たった点）とマウスカーソルが離れていれば出し、同じ所なら出さない。
            Vector3 origin = view.transform.position;
            Vector3 hit = origin + new Vector3(0f, 4f, 0f);
            var ready = new Airerage.Core.Wire.WireState { Phase = Airerage.Core.Wire.WirePhase.Ready };
            var blocked = new Airerage.Core.Wire.WireShotPreview(new Airerage.Core.Wire.WireAim(new System.Numerics.Vector3(0f, 1f, 0f), false, default),
                Airerage.Core.Wire.WireShotResult.Attach, new System.Numerics.Vector3(hit.x, hit.y, 0f));
            aim.Render(origin, ready, blocked, 12f, false, true, origin + new Vector3(0f, 9f, 0f));
            Assert.That(aim.CursorShown, Is.True, "照準器がカーソルと離れているのに、カーソルの印が出ない");
            aim.Render(origin, ready, blocked, 12f, false, true, hit);
            Assert.That(aim.CursorShown, Is.False, "照準器とカーソルが同じ所なのに、カーソルの印が出る");
            aim.Render(origin, ready, blocked, 12f, false, false, default);
            Assert.That(aim.CursorShown, Is.False, "照準の指定がないのに、カーソルの印が出る");
        }
    }
}
