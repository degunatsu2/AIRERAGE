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
                            && view.GetComponentInChildren<PlasmaGlobeView>(true) == null)
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
                        if (view.CoreZone.Period <= 0)
                        {
                            // いつも効いているギミック（つむじ風・悪魔の手の煙）は 1 枚。
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
