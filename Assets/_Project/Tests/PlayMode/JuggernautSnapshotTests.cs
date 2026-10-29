using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Airerage.Core.Input;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// JUGGERNAUT の表示と、昼のビル街の風の見た目の画像（見た目の確認用。名前で選んだときだけ動く）。戦車・回る地上技・フラックの構え
    /// （弾道の段階と予測線、全体を見せるカメラ）・地雷・メーデーの場所のカーソル、昼のビル街の谷の下向きと上向きの風を Logs/Snapshots に
    /// PNG で保存する（技の札などの HUD は IMGUI なので写らない）。仮想の画面で動かす（ThiefSnapshotTests と同じ）:
    /// xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath . -force-glcore -runTests -testPlatform PlayMode
    /// -testFilter Airerage.PlayMode.Tests.JuggernautSnapshotTests
    /// </summary>
    [Explicit("画像を撮るときだけ")]
    public sealed class JuggernautSnapshotTests
    {
        private const string Folder = "Logs/Snapshots";

        [UnityTest]
        public IEnumerator CaptureJuggernaut()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            for (int i = 0; i < 7; i++)
            {
                runner.CycleCharacter();
            }
            yield return Wait(0.8f);
            Directory.CreateDirectory(Folder);
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            ISimulationFeed feed = runner;

            Capture(camera, "Juggernaut_Standing");
            runner.InputOverride = new Script(Repeat(new SimInput(0, 0, InputButtons.Attack), 30));
            yield return Wait(0.2f);
            Capture(camera, "Juggernaut_Spin");
            yield return Wait(1f);

            // フラックの構え（照準は右上）。カメラが引くのを待って撮る。
            var aim = new SimInput(0, 0, InputButtons.None).WithAim(new System.Numerics.Vector2(1f, 0.7f));
            var stance = new List<SimInput> { new SimInput(0, 0, InputButtons.Special1) };
            stance.AddRange(Enumerable.Repeat(aim, 150));
            runner.InputOverride = new Script(stance);
            yield return Wait(1.5f);
            Capture(camera, "Juggernaut_FlakStance");
            // 上で弾道の強さのダイアルを弩へ回す（止まった後）。
            var turn = new List<SimInput> { new SimInput(0, 127, InputButtons.None).WithAim(new System.Numerics.Vector2(1f, 0.7f)) };
            turn.AddRange(Enumerable.Repeat(aim, 90));
            runner.InputOverride = new Script(turn);
            yield return Wait(0.7f);
            Capture(camera, "Juggernaut_FlakDialMax");
            runner.InputOverride = new Script(Repeat(new SimInput(127, 0, InputButtons.None), 3));
            yield return Wait(1.2f);

            // 爆発の 3 つの層（閃光と光条 → 炎 → 煙）。見た目だけを、体の右上に出して撮る。
            var explosions = Object.FindFirstObjectByType<Airerage.Runtime.Presentation.LayeredExplosionView>();
            Vector3 body = runner.PlayerView.transform.position;
            explosions.Spawn(990001, body + new Vector3(5f, 2f, 0f), 2f);
            yield return Wait(0.04f);
            Capture(camera, "Juggernaut_Blast1_Flash");
            yield return Wait(0.2f);
            Capture(camera, "Juggernaut_Blast2_Flame");
            yield return Wait(0.9f);
            Capture(camera, "Juggernaut_Blast3_Smoke");
            yield return Wait(1.6f);

            // 地雷を足もとの前に撃って張り付ける。
            var down = new SimInput(0, 0, InputButtons.Special2).WithAim(new System.Numerics.Vector2(0.5f, -1f));
            runner.InputOverride = new Script(new List<SimInput> { down, default });
            yield return Wait(0.6f);
            Capture(camera, "Juggernaut_Mine");

            // メーデー: 場所を選ぶ間のカーソル（照準に入れる場所は吊られた場所の右上）。
            CharacterState state = runner.PlayerState;
            state.Combat.Gauge = feed.GetConfig(0).Combat.SuperGaugeCost;
            runner.SetPlayerState(state);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Super), default });
            float start = Time.time;
            while (Time.time - start < 2f && feed.GetState(0).Combat.MaydayPhase != JuggernautRules.MaydayLift)
            {
                yield return null;
            }
            yield return Wait(0.35f);
            Capture(camera, "Juggernaut_MaydayLift");
            while (Time.time - start < 4f && feed.GetState(0).Combat.MaydayPhase != JuggernautRules.MaydaySelect)
            {
                yield return null;
            }
            var local = (ILocalAimSource)runner;
            JuggernautRules.TargetArea(local.Bounds, feed.GetConfig(0).Skills.Juggernaut, out System.Numerics.Vector2 center, out System.Numerics.Vector2 half);
            System.Numerics.Vector2 from = feed.GetState(0).Combat.MaydayFrom;
            SimInput pick = JuggernautRules.EncodeTarget(new SimInput(0, 0, InputButtons.None), from + new System.Numerics.Vector2(6f, 5f), center, half);
            runner.InputOverride = new Script(Enumerable.Repeat(pick, 90).ToList());
            yield return Wait(1f);
            Capture(camera, "Juggernaut_MaydaySelect");
        }

        // 昼のビル街の谷の、下向きの風と上向きの風（同じ見た目。ユーザーの要望、2026-10-29）。
        [UnityTest]
        public IEnumerator CaptureCityDayWinds()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_CityDay"));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != "Stage_CityDay")
            {
                yield return null;
            }
            yield return Wait(0.5f);
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            ISimulationFeed feed = runner;
            StageZone down = feed.Zones.First(z => z.Kind == StageZoneKind.Gust && z.Direction.Y < -0.5f && z.Period <= 0);
            Vector2 downCenter = Center(down);
            StageZone up = feed.Zones.Where(z => z.Kind == StageZoneKind.Updraft).OrderBy(z => (Center(z) - downCenter).sqrMagnitude).First();
            Vector2 between = (downCenter + Center(up)) * 0.5f;
            // 2 つの風が入るように、カメラを間に向けて引く。
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            var zoom = Object.FindFirstObjectByType<Airerage.Runtime.Presentation.CameraZoom>();
            // JUGGERNAUT の表示は、構えていない間は毎フレーム全体の表示をやめさせるので、止めておく。
            Object.FindFirstObjectByType<Airerage.Runtime.Presentation.JuggernautEffectsView>().enabled = false;
            var size = new Vector2(Mathf.Abs(Center(up).x - downCenter.x) * 0.5f + 8f, 16f);
            zoom.SetOverview(true, new Vector3(between.x, between.y, 0f),
                Airerage.Runtime.Presentation.SharedCamera.RequiredDistance(size, camera.fieldOfView, camera.aspect));
            yield return Wait(1.5f);
            Capture(camera, "CityDay_Winds");
        }

        private static Vector2 Center(in StageZone zone)
        {
            return new Vector2((zone.MinX + zone.MaxX) * 0.5f, (zone.MinY + zone.MaxY) * 0.5f);
        }

        private static List<SimInput> Repeat(SimInput input, int count)
        {
            var list = Enumerable.Repeat(input, count).ToList();
            list.Add(default);
            return list;
        }

        private static void Capture(Camera camera, string name)
        {
            var texture = new RenderTexture(1280, 720, 24);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
            camera.targetTexture = previous;
            RenderTexture.active = null;
            Object.Destroy(texture);
            Object.Destroy(image);
        }

        private static IEnumerator Wait(float seconds)
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
    }
}
