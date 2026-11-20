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
    /// 実際の現象に寄せたギミックの演出（2026-11-20 の方針。溶岩の噴泉・オーブンの炎・蒸気の噴出）が、試合中に予告 → 噴き始め → 噴いている間
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
            string[] scenes = (System.Environment.GetEnvironmentVariable("AIRERAGE_PLAY_STAGES") ?? "Stage_Volcano,Stage_Kitchen").Split(',');
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
                    // 同じ見た目のギミック（間欠泉 4 本など）は、名前の数字を除いた種類ごとに perStage 個まで。
                    var rigs = new List<HazardEffectRig>();
                    var kinds = new Dictionary<string, int>();
                    foreach (HazardEffectRig rig in Object.FindObjectsByType<HazardEffectRig>(FindObjectsSortMode.InstanceID))
                    {
                        if (rig.View == null)
                        {
                            continue;
                        }
                        string kind = new string(rig.View.name.Where(c => !char.IsDigit(c)).ToArray());
                        kinds.TryGetValue(kind, out int count);
                        if (count < perStage)
                        {
                            kinds[kind] = count + 1;
                            rigs.Add(rig);
                        }
                    }
                    Assert.That(rigs, Is.Not.Empty, $"{scene}: 粒の演出のあるギミックがない");
                    foreach (HazardEffectRig rig in rigs)
                    {
                        yield return CaptureOne(camera, $"Fx_{scene}_{rig.View.name}", rig);
                    }
                }
            }
            finally
            {
                Time.timeScale = 1f;
            }
        }

        // 予告 → 噴き始め → 噴いている間 → 止んだ後を撮る。
        private static IEnumerator CaptureOne(Camera camera, string name, HazardEffectRig rig)
        {
            StageZoneView view = rig.View;
            var authoring = view.GetComponent<StageZoneAuthoring>();
            // 今噴いていたら、止むまで待ってから次の予告を待つ（予告から撮るため）。
            Time.timeScale = 4f;
            float limit = Time.realtimeSinceStartup + 60f;
            while (rig.Erupting && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            while (!rig.Warning && !rig.Erupting && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            Assert.That(rig.Warning || rig.Erupting, $"{name}: 60 秒待っても予告も噴き出しも来ない");
            Time.timeScale = 1f;
            // 予告の半ばまで待つ（粒が出そろう）。
            yield return Wait(0.5f);
            Capture(camera, view, authoring, $"{name}_1Warning");
            limit = Time.realtimeSinceStartup + 20f;
            while (!rig.Erupting && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            yield return Wait(0.15f);
            Capture(camera, view, authoring, $"{name}_2Burst");
            yield return Wait(0.6f);
            Capture(camera, view, authoring, $"{name}_3Active");
            yield return Wait(1.1f);
            Capture(camera, view, authoring, $"{name}_4Active");
            limit = Time.realtimeSinceStartup + 20f;
            while (rig.Erupting && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }
            yield return Wait(0.7f);
            Capture(camera, view, authoring, $"{name}_5After");
        }

        // ギミックの範囲（溶岩は今の柱）が収まるように、正面から撮る。
        private static void Capture(Camera camera, StageZoneView view, StageZoneAuthoring authoring, string name)
        {
            Vector3 center = view.transform.position;
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
