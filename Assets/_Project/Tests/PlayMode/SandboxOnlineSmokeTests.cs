using System.Collections;
using System.Linq;
using Degunatsu.Core.Simulation;
using Degunatsu.Net;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Presentation;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Degunatsu.PlayMode.Tests
{
    /// <summary>
    /// Sandbox_Online シーンを再生して、ホストが1人で試合を始められ、シミュレーションと表示がつながっていることを確かめる
    /// （docs/10_online_plan.md T2-7。複数人での確認は Multiplayer Play Mode で手動で行う）。
    /// </summary>
    public sealed class SandboxOnlineSmokeTests
    {
        private const string SceneName = "Sandbox_Online";

        // 地面（上面 y = 0）に立っているときの中心の高さ: 半分の高さ 0.9m + 地面との隙間 0.01m。
        private const float StandingY = 0.91f;

        [UnityTest]
        public IEnumerator HostAlone_StartsAndSimulates()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;

            var runner = Object.FindFirstObjectByType<OnlineRunner>();
            Assert.That(runner, Is.Not.Null, "OnlineRunner がない");
            Assert.That(runner.enabled, Is.True, "OnlineRunner が無効になっている（設定の不足）");
            CharacterView[] views = Object.FindObjectsByType<CharacterView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(views.Length, Is.EqualTo(OnlineSession.MaxPlayers), "キャラクターの表示が4人分ない");
            foreach (CharacterView view in views)
            {
                Assert.That(view.gameObject.activeSelf, Is.False, "試合の前にキャラクターが表示されている");
            }

            Assert.That(runner.StartHost(0), Is.True, "待ち受けを始められない");
            Assert.That(runner.Session.State, Is.EqualTo(OnlineSession.SessionState.Lobby));
            runner.StartMatch();

            float start = Time.time;
            while (Time.time - start < 1f)
            {
                yield return null;
            }

            OnlineSession session = runner.Session;
            Assert.That(session.State, Is.EqualTo(OnlineSession.SessionState.Running));
            Assert.That(session.PlayerCount, Is.EqualTo(1));
            Assert.That(session.Session.CurrentTick, Is.GreaterThan(30), "ティックが進んでいない");

            CharacterView shown = System.Array.Find(views, v => v.gameObject.activeSelf);
            Assert.That(shown, Is.Not.Null, "自分のキャラクターが表示されていない");
            Assert.That(System.Array.FindAll(views, v => v.gameObject.activeSelf).Length, Is.EqualTo(1), "いない人のキャラクターが表示されている");
            Assert.That(shown.transform.position.y, Is.EqualTo(StandingY).Within(0.02f), "地面に立っていない");

            var followCamera = Object.FindFirstObjectByType<CinemachineCamera>();
            Assert.That(followCamera.Follow, Is.EqualTo(shown.transform), "カメラが自分のキャラクターを追従していない");

            runner.Leave();
            Assert.That(runner.Session, Is.Null);
            Assert.That(shown.gameObject.activeSelf, Is.False, "抜けた後もキャラクターが表示されている");
        }

        [UnityTest]
        public IEnumerator Stage_HasBoundsGimmicksAndFourSpawns()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            var runner = Object.FindFirstObjectByType<OnlineRunner>();

            OnlineStage stage = runner.Stage;
            Assert.That(stage.Bounds.HasValue, Is.True, "ステージの範囲（撃破）がない");
            Assert.That(stage.Spawns.Count, Is.EqualTo(OnlineSession.MaxPlayers), "出現位置が4人分ない");
            Assert.That(stage.Zones.Count(z => z.Kind == StageZoneKind.JumpPad), Is.GreaterThanOrEqualTo(1), "ジャンプ台がない");
            Assert.That(stage.Zones.Count(z => z.Kind == StageZoneKind.Updraft), Is.GreaterThanOrEqualTo(1), "上昇気流がない");
            Assert.That(stage.Zones.Count(z => z.Kind == StageZoneKind.BoostRing), Is.GreaterThanOrEqualTo(1), "加速リングがない");
            foreach (System.Numerics.Vector3 spawn in stage.Spawns)
            {
                Assert.That(stage.Bounds.Value.Contains(spawn), Is.True, $"出現位置 {spawn} がステージの外");
            }
            Assert.That(Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None).Length, Is.EqualTo(stage.Zones.Count), "ギミックの見た目がない");

            Assert.That(Object.FindFirstObjectByType<ComboHud>().enabled, Is.True, "コンボの表示が OnlineRunner を読めていない");
            Assert.That(Object.FindFirstObjectByType<CombatSoundEvents>().enabled, Is.True, "効果音が OnlineRunner を読めていない");

            Assert.That(runner.StartHost(0), Is.True);
            runner.StartMatch();
            yield return null;
            Assert.That(runner.Session.World.Bounds, Is.EqualTo(stage.Bounds), "試合のワールドにステージの範囲が渡っていない");
            Assert.That(runner.Session.World.Zones.Count, Is.EqualTo(stage.Zones.Count), "試合のワールドにギミックが渡っていない");
            runner.Leave();
        }

        [UnityTest]
        public IEnumerator JumpPad_LaunchesThePlayer_WithALightAndASound()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            var runner = Object.FindFirstObjectByType<OnlineRunner>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            runner.StartHost(0);
            runner.StartMatch();
            yield return WaitSeconds(0.3f);

            // 中央のジャンプ台の上に置く（1人なので巻き戻しは起きない）。
            StageZone pad = runner.Stage.Zones.First(z => z.Kind == StageZoneKind.JumpPad && z.MinX < 0f && z.MaxX > 0f);
            CharacterState state = runner.Session.World.GetState(0);
            state.Position = new System.Numerics.Vector3((pad.MinX + pad.MaxX) * 0.5f, pad.MinY + 1f, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            runner.Session.World.SetState(0, state);

            CharacterView view = Object.FindObjectsByType<CharacterView>(FindObjectsSortMode.None).First(v => v.gameObject.activeSelf);
            bool sawLight = false;
            float highest = 0f;
            float start = Time.time;
            while (Time.time - start < 1f)
            {
                sawLight |= view.GimmickBurst.IsPlaying;
                highest = Mathf.Max(highest, runner.Session.World.GetState(0).Position.Y);
                yield return null;
            }

            Assert.That(runner.Session.World.GetState(0).JumpPadCount, Is.GreaterThanOrEqualTo(1), "ジャンプ台で打ち上がらない");
            Assert.That(highest, Is.GreaterThan(8f), "高く上がらない");
            Assert.That(sawLight, Is.True, "ギミックの光が出ない");
            Assert.That(audio.History.Contains(SoundId.JumpPad), Is.True, "ジャンプ台の音が鳴らない");
            runner.Leave();
        }

        [UnityTest]
        public IEnumerator FallingOut_IsAKnockout_AndRespawns()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            var runner = Object.FindFirstObjectByType<OnlineRunner>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            runner.StartHost(0);
            runner.StartMatch();
            yield return WaitSeconds(0.3f);

            StageBounds bounds = runner.Stage.Bounds.Value;
            CharacterState state = runner.Session.World.GetState(0);
            state.Position = new System.Numerics.Vector3(0f, bounds.MinY + 0.5f, 0f);
            state.Velocity = new System.Numerics.Vector3(0f, -10f, 0f);
            runner.Session.World.SetState(0, state);
            yield return WaitSeconds(0.3f);

            CharacterState after = runner.Session.World.GetState(0);
            Assert.That(after.Combat.Falls, Is.EqualTo(1), "ステージの下に出ても撃破にならない");
            Assert.That(after.Position.Y, Is.GreaterThan(0f), "出現位置に戻っていない");
            Assert.That(audio.History.Contains(SoundId.Knockout), Is.True, "撃破の音が鳴らない");
            runner.Leave();
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }
    }
}
