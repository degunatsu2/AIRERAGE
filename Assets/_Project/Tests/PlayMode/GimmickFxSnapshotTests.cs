using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 実際の現象に寄せたギミックの演出（2026-11-20 の方針。溶岩の噴泉・オーブンの炎・蒸気・落雷・つらら・つむじ風・瘴気）が、試合中に予告 → 噴き始め → 噴いている間
    /// → 止んだ後と動くところを Logs/Snapshots/Fx_*.png に保存する（名前で選んだときだけ動く。見た目の確認用）。トレーニングでステージに入り、
    /// 粒の演出（HazardEffectRig）を持つギミックごとに、そのギミックを映すカメラで撮る。待つ間は時間を速める。
    /// </summary>
    [Explicit("画像を撮るときだけ")]
    public sealed class GimmickFxSnapshotTests
    {
        private const string Folder = "Logs/Snapshots";

        [UnityTest]
        public IEnumerator CaptureHazardEffectsInAction()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            string[] scenes = (System.Environment.GetEnvironmentVariable("AIRERAGE_PLAY_STAGES") ?? "Stage_Volcano,Stage_Kitchen,Stage_Manor,Stage_SnowSea,Stage_Stadium").Split(',');
            int perStage = int.TryParse(System.Environment.GetEnvironmentVariable("AIRERAGE_FX_PER_STAGE"), out int n) ? n : 2;
            try
            {
                foreach (string scene in scenes)
                {
                    flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == scene));
                    while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != scene)
                    {
                        yield return null;
                    }
                    yield return Wait(1f);
                    Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
                    // 演出のあるギミック（粒・落雷・つらら）。同じ見た目のギミックは、名前の East・West と数字を除いた種類ごとに perStage 個まで。
                    var views = new List<StageZoneView>();
                    var kinds = new Dictionary<string, int>();
                    foreach (StageZoneView view in Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.InstanceID))
                    {
                        if (view.GetComponentInChildren<ParticleSystem>(true) == null && view.GetComponentInChildren<LightningStrikeView>(true) == null
                            && view.GetComponentInChildren<PlasmaGlobeView>(true) == null && view.GetComponentInChildren<CatapultView>(true) == null
                            && view.GetComponentInChildren<CargoNetView>(true) == null)
                        {
                            continue;
                        }
                        string kind = new string(view.name.Where(c => !char.IsDigit(c)).ToArray()).Replace("East", "").Replace("West", "");
                        kinds.TryGetValue(kind, out int count);
                        if (count < perStage)
                        {
                            kinds[kind] = count + 1;
                            views.Add(view);
                        }
                    }
                    Assert.That(views, Is.Not.Empty, $"{scene}: 演出のあるギミックがない");
                    foreach (StageZoneView view in views)
                    {
                        if (view.GetComponentInChildren<RollingBombView>(true) != null)
                        {
                            // 転がる噴石は、試合のカメラの位置から道のりの全体を撮る（CaptureRollingBomb）。
                            yield return CaptureRollingBomb(camera, $"Fx_{scene}_{view.name}", view);
                            continue;
                        }
                        if (view.CoreZone.Period <= 0 || view.CoreZone.ActiveTicks >= view.CoreZone.Period)
                        {
                            // いつも効いているギミック（つむじ風・悪魔の手の煙・プラズマ球）は 1 枚。
                            Capture(camera, view, view.GetComponent<StageZoneAuthoring>(), $"Fx_{scene}_{view.name}_Steady");
                            continue;
                        }
                        yield return CaptureOne(camera, $"Fx_{scene}_{view.name}", view);
                    }
                }
            }
            finally
            {
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// 恐怖の館の鬼火の輪と、埠頭の荷役の網（2026-11-21 の要望で作り直した）を撮る（Logs/Snapshots/Wisp_*・Net_*）。鬼火の輪: 飛び回る輪、
        /// くぐった人の周りを回る鬼火、上空へ昇って消える鬼火。荷役の網: 吊られている網、ぶら下がって下がる途中、下がりきった後、外して戻る途中。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureWispRingAndCargoNet()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Manor"));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != "Stage_Manor")
            {
                yield return null;
            }
            yield return Wait(1.5f);
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            ISimulationFeed feed = runner;
            WispRingView wisp = Object.FindFirstObjectByType<WispRingView>();
            Airerage.Core.Simulation.StageTrap trap = wisp.GetComponent<StageTrapAuthoring>().ToTrap();
            Vector3 ring = wisp.ShownCenter;
            Shoot(camera, new Vector3(ring.x, ring.y, -12f), "Wisp_1Flying");
            float limit = Time.realtimeSinceStartup + 4f;
            while (!wisp.Circling && Time.realtimeSinceStartup < limit)
            {
                System.Numerics.Vector2 next = trap.WispAt(feed.CurrentTick + 1, feed.Rules.Seed);
                Airerage.Core.Simulation.CharacterState state = runner.PlayerState;
                state.Position = new System.Numerics.Vector3(next.X, next.Y, 0f);
                state.Velocity = System.Numerics.Vector3.Zero;
                runner.SetPlayerState(state);
                yield return null;
            }
            float start = Time.time;
            while (Time.time - start < 0.5f)
            {
                yield return null;
            }
            Vector3 player = new Vector3(runner.PlayerState.Position.X, runner.PlayerState.Position.Y, 0f);
            Shoot(camera, new Vector3(player.x, player.y, -9f), "Wisp_2Circling");
            while (Time.time - start < WispRingView.CircleSeconds + 0.35f)
            {
                yield return null;
            }
            Shoot(camera, new Vector3(player.x, player.y + 4f, -14f), "Wisp_3Rising");

            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Wharf"));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != "Stage_Wharf")
            {
                yield return null;
            }
            yield return Wait(1f);
            camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            runner = Object.FindFirstObjectByType<SimulationRunner>();
            feed = runner;
            StagePlatformView net = Object.FindObjectsByType<StagePlatformView>(FindObjectsSortMode.None).Single(v => v.name == "NetWest");
            Airerage.Core.Simulation.StagePlatform platform = net.GetComponent<StagePlatformAuthoring>().ToPlatform();
            var focus = new Vector3(platform.Origin.X, platform.Origin.Y + 1f, -20f);
            Shoot(camera, focus, "Net_1Hanging");
            int index = feed.Platforms.ToList().FindIndex(p => p.SameAs(platform));
            Airerage.Core.Simulation.CharacterState hanger = runner.PlayerState;
            hanger.Position = new System.Numerics.Vector3(platform.Origin.X - 1f, platform.Origin.Y - 4f, 0f);
            hanger.Velocity = new System.Numerics.Vector3(0f, -12f, 0f);
            hanger.Wire.Phase = Airerage.Core.Wire.WirePhase.Attached;
            hanger.Wire.Anchor = new System.Numerics.Vector3(platform.Origin.X, platform.Origin.Y, 0f);
            hanger.Wire.AnchorBody = (byte)(index + 1);
            hanger.Wire.RopeLength = 4.2f;
            hanger.Wire.PhaseTicks = 0;
            runner.SetPlayerState(hanger);
            runner.InputOverride = new HoldWire();
            try
            {
                int at = feed.CurrentTick;
                while (feed.CurrentTick - at < 12)
                {
                    yield return null;
                }
                Shoot(camera, focus + new Vector3(0f, -2f, 0f), "Net_2Dropping");
                while (feed.CurrentTick - at < 70)
                {
                    yield return null;
                }
                Shoot(camera, focus + new Vector3(0f, -3f, 0f), "Net_3Lowered");
            }
            finally
            {
                runner.InputOverride = null;
            }
            int released = feed.CurrentTick;
            while (feed.CurrentTick - released < 90)
            {
                yield return null;
            }
            Shoot(camera, focus + new Vector3(0f, -2f, 0f), "Net_4Returning");
        }

        /// <summary>
        /// 宇宙ステーション（2026-11-21 の第 5 弾）: 端のワームホールの口、救出のゲートと出口の口、くぐった直後の波紋、電磁コイルの加速器を
        /// 通るところ、宇宙船の中の漂う物と水の玉を撮る（Logs/Snapshots/Space_*）。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureSpaceStationGimmicks()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_SpaceStation"));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != "Stage_SpaceStation")
            {
                yield return null;
            }
            yield return Wait(1.5f);
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            StageZoneView View(string name)
            {
                return Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None).Single(v => v.name == name);
            }
            Shoot(camera, new Vector3(-54f, 4f, -18f), "Space_1Wormhole");
            Shoot(camera, new Vector3(-55f, 4f, -9f), "Space_1WormholeClose");
            StageZoneView rescue = View("RescueGate");
            Shoot(camera, new Vector3(rescue.ShownCenter.x, rescue.ShownCenter.y + 1f, -14f), "Space_2Rescue");
            Shoot(camera, new Vector3(0f, 29f, -12f), "Space_3Exit");
            // 西の口へ飛び込み、東の口から出たところ。
            Airerage.Core.Simulation.CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(-51f, 4f, 0f);
            state.Velocity = new System.Numerics.Vector3(-14f, 2f, 0f);
            runner.SetPlayerState(state);
            float limit = Time.realtimeSinceStartup + 3f;
            while (runner.PlayerState.Combat.WarpCount == 0 && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            yield return Wait(0.12f);
            Shoot(camera, new Vector3(54f, 4f, -16f), "Space_4Warped");
            // 救出のゲートが動いて撓んでいるところ（動く向きと逆へ帆のようにふくらむ）。
            limit = Time.realtimeSinceStartup + 6f;
            WormholeView rescueMouth = rescue.GetComponentInChildren<WormholeView>();
            while (rescueMouth.Bend.magnitude < 0.6f && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            Shoot(camera, new Vector3(rescue.ShownCenter.x, rescue.ShownCenter.y + 1f, -12f), "Space_2RescueBending");
            // コイルガン: 収まってためているところ（照準を左上へ）と、撃ち出した瞬間。
            StageZoneView coil = View("CoilGunCore");
            Shoot(camera, new Vector3(coil.ShownCenter.x, coil.ShownCenter.y, -10f), "Space_5Coil");
            runner.InputOverride = new HoldAimAt(new System.Numerics.Vector2(-0.6f, 0.8f));
            state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(coil.ShownCenter.x - 2.4f, coil.ShownCenter.y, 0f);
            state.Velocity = new System.Numerics.Vector3(4f, 0f, 0f);
            runner.SetPlayerState(state);
            limit = Time.realtimeSinceStartup + 2f;
            while (runner.PlayerState.Combat.CaptureTicks < 36 && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            Shoot(camera, new Vector3(coil.ShownCenter.x, coil.ShownCenter.y + 1.5f, -12f), "Space_6CoilCharging");
            limit = Time.realtimeSinceStartup + 2f;
            while (runner.PlayerState.Combat.CaptureTicks < 54 && runner.PlayerState.Combat.CaptureTicks > 0 && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            Shoot(camera, new Vector3(coil.ShownCenter.x, coil.ShownCenter.y + 1.5f, -12f), "Space_6CoilReady");
            limit = Time.realtimeSinceStartup + 2f;
            while (runner.PlayerState.Combat.CaptureTicks > 0 && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            yield return Wait(0.05f);
            Shoot(camera, new Vector3(coil.ShownCenter.x - 1f, coil.ShownCenter.y + 2f, -12f), "Space_6CoilFiring");
            runner.InputOverride = null;
            yield return Wait(1.5f);
            // 宇宙船の中（漂う物と水の玉）と、人が通って押されたところ。
            StageZoneView cabin = View("ZeroGravityWest");
            Shoot(camera, new Vector3(cabin.ShownCenter.x, cabin.ShownCenter.y, -13f), "Space_7Cabin");
            Shoot(camera, new Vector3(cabin.ShownCenter.x - 3f, cabin.ShownCenter.y, -5f), "Space_7CabinClose");
            state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(cabin.ShownCenter.x - 6.5f, cabin.ShownCenter.y, 0f);
            state.Velocity = new System.Numerics.Vector3(8f, 0f, 0f);
            runner.SetPlayerState(state);
            yield return Wait(1.2f);
            Shoot(camera, new Vector3(cabin.ShownCenter.x, cabin.ShownCenter.y, -13f), "Space_8CabinPushed");
        }

        /// <summary>
        /// 戦いの演出（2026-11-24 の決定 D-101）: ワイヤーキャンセル・技・必殺技の回る星（S5。キャラクターより奥）と、FATAL の空気の塊の
        /// 衝撃波（F6。背景がゆがむ）を撮る（Logs/Snapshots/Fx_*）。演出はそれぞれの光を直接始めて撮る（JACKPOT の文字は画面に OnGUI で
        /// 描くので、カメラの絵には写らない）。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureCombatEffects()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Athletic"));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != "Stage_Athletic")
            {
                yield return null;
            }
            yield return Wait(2f);
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            CharacterView view = runner.PlayerView;
            System.Numerics.Vector3 at = runner.PlayerState.Position;
            var center = new Vector3(at.X, at.Y + 0.9f, at.Z);
            var shot = new Vector3(center.x, center.y + 0.5f, -10f);
            Shoot(camera, shot, "Fx_0Before");
            view.WireCancelBurst.Play(center);
            yield return Wait(0.07f);
            Shoot(camera, shot, "Fx_1WireCancelStar");
            yield return Wait(0.2f);
            Shoot(camera, shot, "Fx_1WireCancelStarLate");
            yield return Wait(0.6f);
            view.SpecialBurst.Play(center);
            yield return Wait(0.1f);
            Shoot(camera, shot, "Fx_2SpecialStar");
            yield return Wait(0.8f);
            view.SuperBurst.Play(center);
            yield return Wait(0.12f);
            Shoot(camera, shot, "Fx_2SuperStar");
            yield return Wait(1f);
            view.FatalBurst.Play(center);
            yield return Wait(0.1f);
            ShootWithShockwaves(camera, new Vector3(center.x, center.y + 1f, -14f), "Fx_3FatalShock");
            yield return Wait(0.15f);
            ShootWithShockwaves(camera, new Vector3(center.x, center.y + 1f, -14f), "Fx_3FatalShockLate");
        }

        // 画面の衝撃波の値を、撮るカメラ（位置と絵の大きさ）で入れ直してから撮る。
        private static void ShootWithShockwaves(Camera camera, Vector3 position, string name)
        {
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            camera.transform.SetPositionAndRotation(position, Quaternion.identity);
            var texture = new RenderTexture(1920, 1080, 24);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = texture;
            ScreenShockwaves.Submit(camera);
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
            camera.targetTexture = previous;
            RenderTexture.active = null;
            camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            Object.Destroy(texture);
            Object.Destroy(image);
        }

        private sealed class HoldAimAt : ITickInputSource
        {
            private readonly System.Numerics.Vector2 _aim;

            public HoldAimAt(System.Numerics.Vector2 aim)
            {
                _aim = aim;
            }

            public bool TryGetNext(out Airerage.Core.Input.PlayerInput input)
            {
                input = Airerage.Core.Input.PlayerInput.FromAxes(0f, 0f, Airerage.Core.Input.InputButtons.None).WithAim(_aim);
                return true;
            }
        }

        /// <summary>
        /// キッチンのドーナツフライヤー（2026-11-24 の要望）: 左の棚のフライヤー、弾んで転がるドーナツ、着地でつぶれたところ、
        /// 右の端から出ていくところを撮る（Logs/Snapshots/Kitchen_*）。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureKitchenDonuts()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Kitchen"));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != "Stage_Kitchen")
            {
                yield return null;
            }
            yield return Wait(1f);
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            DonutFryerView fryer = Object.FindFirstObjectByType<DonutFryerView>();
            Shoot(camera, new Vector3(-50f, 5f, -14f), "Kitchen_1Fryer");
            Shoot(camera, new Vector3(-50f, 4f, -7f), "Kitchen_1FryerClose");
            // 次のドーナツがシュートを滑ってくるところ。
            float limit = Time.realtimeSinceStartup + 8f;
            while (!fryer.OnChute && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            yield return Wait(0.25f);
            Shoot(camera, new Vector3(-52f, 3.5f, -8f), "Kitchen_1FryerChute");
            limit = Time.realtimeSinceStartup + 8f;
            while (fryer.Shown < 3 && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            Shoot(camera, new Vector3(-20f, 8f, -40f), "Kitchen_2DonutsLeft");
            Shoot(camera, new Vector3(-36f, 5f, -12f), "Kitchen_3DonutToaster");
            yield return Wait(4f);
            Shoot(camera, new Vector3(20f, 8f, -40f), "Kitchen_4DonutsRight");
            Shoot(camera, new Vector3(0f, 4f, -10f), "Kitchen_5DonutCenter");
            // 左上のつむじ風（ミキサーの粉の渦）: 見た目の高さと、飛ぶ高さ（頂点の高さ）が合っているか。
            Shoot(camera, new Vector3(-42f, 24f, -34f), "Kitchen_6MixerWhirl");
        }

        private sealed class HoldWire : ITickInputSource
        {
            public bool TryGetNext(out Airerage.Core.Input.PlayerInput input)
            {
                input = Airerage.Core.Input.PlayerInput.FromAxes(0f, 0f, Airerage.Core.Input.InputButtons.Wire).WithAim(System.Numerics.Vector2.UnitY);
                return true;
            }
        }

        /// <summary>
        /// ステージの仕掛け（2026-11-21 の第 4 弾）: 研究所の赤外線（全体・発射装置の近く・ミサイルと煙の帯）と、競技場のピッチャーマシン
        /// （全体・ボールを送り込むところ・打ち出したボール）を撮る（Logs/Snapshots/Trap_*）。赤外線は自分を光線の上に置いて撃たせる。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureTrapsInAction()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            try
            {
                foreach (string scene in new[] { "Stage_Lab", "Stage_Stadium" })
                {
                    flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == scene));
                    while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != scene)
                    {
                        yield return null;
                    }
                    yield return Wait(1f);
                    Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
                    var runner = Object.FindFirstObjectByType<SimulationRunner>();
                    foreach (StageTrapView trap in Object.FindObjectsByType<StageTrapView>(FindObjectsSortMode.InstanceID).OrderBy(t => t.transform.position.x).Take(2))
                    {
                        Vector3 at = trap.transform.position;
                        string name = $"Trap_{scene}_{trap.name}";
                        Transform launcher = trap.transform.Find("Launcher");
                        if (launcher != null)
                        {
                            Shoot(camera, new Vector3(at.x, at.y + 1f, -22f), $"{name}_Whole");
                            Shoot(camera, new Vector3(launcher.position.x + 1.5f, launcher.position.y + 0.3f, -6.5f), $"{name}_Launcher");
                            // 自分を光線の上に置いて、ミサイルを撃たせる。
                            Airerage.Core.Simulation.CharacterState player = runner.PlayerState;
                            player.Position = new System.Numerics.Vector3(at.x, at.y - 0.4f, 0f);
                            player.Velocity = System.Numerics.Vector3.Zero;
                            runner.SetPlayerState(player);
                            float limit = Time.realtimeSinceStartup + 8f;
                            while (!trap.ShowsProjectile && Time.realtimeSinceStartup < limit)
                            {
                                yield return null;
                            }
                            yield return Wait(0.35f);
                            Transform missile = trap.transform.Find("Missile");
                            Shoot(camera, new Vector3(missile.position.x - 1.5f, missile.position.y + 0.5f, -9f), $"{name}_Missile");
                            continue;
                        }
                        Shoot(camera, new Vector3(at.x, at.y - 1f, -16f), $"{name}_Whole");
                        Time.timeScale = 4f;
                        float wait = Time.realtimeSinceStartup + 40f;
                        while (!trap.Feeding && Time.realtimeSinceStartup < wait)
                        {
                            yield return null;
                        }
                        Time.timeScale = 1f;
                        yield return Wait(0.3f);
                        Shoot(camera, new Vector3(at.x - Mathf.Sign(at.x) * 0.5f, at.y + 0.5f, -10f), $"{name}_Feeding");
                        wait = Time.realtimeSinceStartup + 4f;
                        while (!trap.ShowsProjectile && Time.realtimeSinceStartup < wait)
                        {
                            yield return null;
                        }
                        yield return Wait(0.15f);
                        Shoot(camera, new Vector3(at.x + Mathf.Sign(-at.x) * 3f, at.y + 1.5f, -14f), $"{name}_Pitch");
                    }
                }
            }
            finally
            {
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// 埠頭の灯台の光のまぶしさ（2026-11-20 の指摘。試合のカメラで画面の真ん中が白い大きな光に覆われる）: 試合のカメラを灯台の明かりの
        /// 正面に置き、光の筋が 1 回りする間の画面の真ん中の明るさを記録し、いちばん明るい時を「全部」「光の筋なし」「レンズなし」で
        /// 撮り比べる（Logs/Snapshots/Glare_*）。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureLighthouseFromTheMatchCamera()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Wharf"));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != "Stage_Wharf")
            {
                yield return null;
            }
            yield return Wait(1f);
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            // 足場の「Lighthouse」（地形の箱）と同じ名前なので、光の筋を持つ方を選ぶ。
            Transform lighthouse = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).First(t => t.name == "Lighthouse" && t.Find("BeamPivot") != null);
            Transform pivot = lighthouse.Find("BeamPivot");
            Renderer lens = lighthouse.Find("Lens").GetComponent<Renderer>();
            Renderer[] beams = pivot.GetComponentsInChildren<Renderer>();
            Vector3 lampPosition = pivot.position;
            // 0.1 秒ごとに 10 秒（光の筋の 1 回り）、カメラの距離 2 通り（ふだんの距離と近づけた距離）。画面の真ん中がいちばん明るい
            // 時の絵と、そのとき光の筋・レンズをそれぞれ消した絵を残す。
            var log = new System.Text.StringBuilder();
            foreach (float distance in new[] { lampPosition.z - camera.transform.position.z, 18f })
            {
                float best = -1f;
                int bestFrame = -1;
                for (int i = 0; i < 100; i++)
                {
                    Vector3 position = new Vector3(lampPosition.x, lampPosition.y, lampPosition.z - distance);
                    float brightness = Shoot(camera, position, null);
                    log.AppendLine($"{distance:0} {i} yaw {pivot.eulerAngles.y:0} {brightness:0.0}");
                    if (brightness > best)
                    {
                        best = brightness;
                        bestFrame = i;
                        Shoot(camera, position, $"Glare_{distance:0}m_All");
                        foreach (Renderer beam in beams)
                        {
                            beam.enabled = false;
                        }
                        Shoot(camera, position, $"Glare_{distance:0}m_NoBeam");
                        foreach (Renderer beam in beams)
                        {
                            beam.enabled = true;
                        }
                        lens.enabled = false;
                        Shoot(camera, position, $"Glare_{distance:0}m_NoLens");
                        lens.enabled = true;
                    }
                    yield return Wait(0.1f);
                }
                log.AppendLine($"best {distance:0}m frame {bestFrame} brightness {best:0.0}");
            }
            File.WriteAllText($"{Folder}/Glare_log.txt", log.ToString());
        }

        // name があれば絵を保存する。画面の真ん中（横 800・縦 600）の明るさの平均（0〜255）を返す。
        private static float Shoot(Camera camera, Vector3 position, string name)
        {
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            camera.transform.SetPositionAndRotation(position, Quaternion.identity);
            var texture = new RenderTexture(1920, 1080, 24);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            if (name != null)
            {
                File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
            }
            Color[] center = image.GetPixels(560, 240, 800, 600);
            float sum = 0f;
            foreach (Color c in center)
            {
                sum += c.grayscale;
            }
            camera.targetTexture = previous;
            RenderTexture.active = null;
            camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            Object.Destroy(texture);
            Object.Destroy(image);
            return sum / center.Length * 255f;
        }

        // 予告 → 噴き始め → 噴いている間 → 止んだ後を撮る（効き始めから 0.03・0.15・0.6・1.1 秒。落雷は 0.4 秒で終わる）。
        private static IEnumerator CaptureOne(Camera camera, string name, StageZoneView view)
        {
            var authoring = view.GetComponent<StageZoneAuthoring>();
            // 今効いていたら、止むまで待ってから次の予告を待つ（予告から撮るため）。
            Time.timeScale = 4f;
            float limit = Time.realtimeSinceStartup + 60f;
            while (view.ShowsActive && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            while (!view.ShowsWarning && !view.ShowsActive && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            Assert.That(view.ShowsWarning || view.ShowsActive, $"{name}: 60 秒待っても予告も効き始めも来ない");
            Time.timeScale = 1f;
            yield return Wait(0.5f);
            Capture(camera, view, authoring, $"{name}_1Warning");
            limit = Time.realtimeSinceStartup + 20f;
            while (!view.ShowsActive && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            float start = Time.time;
            string[] tags = { "2Strike", "3Burst", "4Active", "5Active" };
            float[] at = { 0.03f, 0.15f, 0.6f, 1.1f };
            for (int i = 0; i < at.Length; i++)
            {
                while (Time.time - start < at[i])
                {
                    yield return null;
                }
                Capture(camera, view, authoring, $"{name}_{tags[i]}");
            }
            limit = Time.realtimeSinceStartup + 20f;
            while (view.ShowsActive && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            yield return Wait(0.7f);
            Capture(camera, view, authoring, $"{name}_6After");
        }

        // 転がる噴石: 予告が来たら、予告の始まりから 0.5（尾根の上の警告）・1.6（飛び出し）・2.8（飛んでいる）・4.0（斜面で跳ねて
        // 転がる）・5.0（跳ねた）・6.1（遊ぶ面を横切る）・6.6 秒（手前へ落ちる）に、試合のカメラと同じ向き（柱の前 32m）から撮る。
        private static IEnumerator CaptureRollingBomb(Camera camera, string name, StageZoneView view)
        {
            Time.timeScale = 4f;
            float limit = Time.realtimeSinceStartup + 90f;
            while (!view.ShowsWarning && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            Assert.That(view.ShowsWarning, $"{name}: 90 秒待っても予告が来ない");
            Time.timeScale = 1f;
            int warning = view.CoreZone.Timing.WarningTicks;
            // 時間はシミュレーションのティックで数える（撮影の環境ではフレームが遅く、シミュレーションが実際の時間より遅れるため）。
            float lastElapsed = 0f;
            float Elapsed()
            {
                if (view.ShowsWarning)
                {
                    lastElapsed = (warning - view.TicksUntilActive) / 60f;
                }
                else if (view.ShowsActive && view.ActiveElapsedTicks >= 0)
                {
                    lastElapsed = (warning + view.ActiveElapsedTicks) / 60f;
                }
                else
                {
                    lastElapsed += Time.deltaTime;
                }
                return lastElapsed;
            }
            float[] at = { 0.5f, 1.6f, 2.8f, 4.0f, 5.0f, 6.12f, 6.6f };
            string[] tags = { "1Sign", "2Eject", "3Flight", "4Roll", "5Hop", "6Cross", "7Away" };
            for (int i = 0; i < at.Length; i++)
            {
                while (Elapsed() < at[i])
                {
                    yield return null;
                }
                Vector2 column = view.ShownColumn;
                float x = (column.x + column.y) * 0.5f;
                Shoot(camera, new Vector3(x, view.transform.position.y + 6f, -32f), $"{name}_{tags[i]}");
            }
        }

        /// <summary>
        /// 研究所のプラズマ球の電気を帯びたワイヤー: トレーニングの自分を球の真ん中につないだ状態にし、ダミーをロープの途中に置いて撮る
        /// （Logs/Snapshots/Fx_ChargedWire_*）。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureChargedWire()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Lab"));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != "Stage_Lab")
            {
                yield return null;
            }
            yield return Wait(7f);
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var world = (Airerage.Core.Simulation.SimWorld)typeof(SimulationRunner)
                .GetField("_world", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(runner);
            Transform globe = Object.FindObjectsByType<PlasmaGlobeView>(FindObjectsSortMode.InstanceID).OrderBy(g => g.transform.position.x).First().transform;
            var anchor = new System.Numerics.Vector3(globe.position.x, globe.position.y, 0f);
            // シミュレーションを止めて（表示は続く）、置いた状態のまま撮る（ボタンを押していないのでワイヤーが外れないように）。
            ((IPauseTarget)runner).SpeedScale = 0f;
            for (int frame = 0; frame < 6; frame++)
            {
                Airerage.Core.Simulation.CharacterState player = world.GetState(0);
                player.Position = new System.Numerics.Vector3(anchor.X - 5f, anchor.Y - 4f, 0f);
                player.Velocity = System.Numerics.Vector3.Zero;
                player.Wire.Phase = Airerage.Core.Wire.WirePhase.Attached;
                player.Wire.Anchor = anchor;
                player.Wire.AnchorBody = 0;
                player.Wire.PhaseTicks = 0;
                player.Wire.RopeLength = System.Numerics.Vector3.Distance(player.Position, anchor);
                world.SetState(0, player);
                yield return null;
            }
            var wire = Object.FindObjectsByType<WireView>(FindObjectsSortMode.InstanceID).FirstOrDefault(w => w.ShowsCharged);
            Assert.That(wire, Is.Not.Null, "球につないだワイヤーが電気を帯びて見えない");
            Shoot(camera, new Vector3(anchor.X - 2.5f, anchor.Y - 1.5f, -14f), "Fx_ChargedWire_Close");
            Shoot(camera, new Vector3(anchor.X, anchor.Y - 3f, -30f), "Fx_ChargedWire_Wide");
            ((IPauseTarget)runner).SpeedScale = 1f;
        }

        // ギミックの範囲（溶岩は今の柱）が収まるように、正面から撮る。
        private static void Capture(Camera camera, StageZoneView view, StageZoneAuthoring authoring, string name)
        {
            // 動くギミック（つむじ風・悪魔の手）は今の位置。
            Vector3 center = view.ShownCenter != Vector3.zero ? view.ShownCenter : view.transform.position;
            Vector2 size = authoring.Size;
            Vector2 column = view.ShownColumn;
            if (authoring.Kind == Airerage.Core.Simulation.StageZoneKind.Hazard && column.y > column.x)
            {
                center.x = (column.x + column.y) * 0.5f;
                size.x = Mathf.Min(size.x, column.y - column.x + 8f);
            }
            float fov = 40f;
            float half = Mathf.Max(size.y * 0.5f + 3f, (size.x * 0.5f + 3f) / camera.aspect);
            float distance = Mathf.Clamp(half / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), 10f, 60f);
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            camera.transform.SetPositionAndRotation(new Vector3(center.x, center.y, -distance), Quaternion.identity);
            camera.fieldOfView = fov;
            var texture = new RenderTexture(1920, 1080, 24);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
            camera.targetTexture = previous;
            RenderTexture.active = null;
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = previousFov;
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
    }
}
