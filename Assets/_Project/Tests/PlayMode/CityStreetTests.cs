using System.Collections;
using System.IO;
using System.Linq;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// ビル街の手前の大通り（2026-11-25 の要望: 歩道と車道、歩く人影、まばらな車。docs/15 6.70）。トレーニングでビル街に入り、
    /// 車が左側通行で走り、人影が奥へも手前へも歩いていることを確かめる。CaptureCityAvenue は絵を撮る（名前で選んだときだけ）。
    /// </summary>
    public sealed class CityStreetTests
    {
        private const string Folder = "Logs/Snapshots";

        // 地上の大通りの高さ（Editor/Stages/CityStreet.Ground と同じ。遊ぶ屋上の約 110m 下）。
        private const float CityStreetHeight = -110f;

        // 使い回す車と人影の数（Editor の CityStreet.CarPool・PersonPool と同じ。テストは Editor のアセンブリを参照しないので値を写す）。
        private const int CityStreetCarPool = 32;
        private const int CityStreetPersonPool = 40;

        [UnityTest]
        public IEnumerator CityAvenue_CarsKeepLeft_AndPeopleWalkBothWays([Values("Stage_CityDay", "Stage_CityNight")] string stage)
        {
            yield return EnterTraining(stage);
            StreetTrafficView traffic = Object.FindFirstObjectByType<StreetTrafficView>();
            Assert.That(traffic, Is.Not.Null, "大通りの車と人影がない");
            Assert.That(traffic.IsNight, Is.EqualTo(stage == "Stage_CityNight"), "夜の灯りの設定が違う");
            int maxCars = 0;
            int maxPeople = 0;
            bool keptLeft = true;
            bool bothWays = false;
            float start = Time.time;
            while (Time.time - start < 3f)
            {
                maxCars = Mathf.Max(maxCars, traffic.ActiveCars);
                maxPeople = Mathf.Max(maxPeople, traffic.ActivePeople);
                keptLeft &= traffic.AllCarsKeepLeft;
                bothWays |= traffic.PeopleWalkBothWays;
                yield return null;
            }
            Assert.That(maxCars, Is.InRange(2, CityStreetCarPool), "車がまばらに走っていない");
            Assert.That(maxPeople, Is.InRange(3, CityStreetPersonPool), "歩道に人影が歩いていない");
            Assert.That(keptLeft, Is.True, "左側通行になっていない車がある");
            Assert.That(bothWays, Is.True, "奥へ・手前へのどちらかにしか歩いていない");
        }

        [UnityTest]
        [Explicit("画像を撮るときだけ")]
        public IEnumerator CaptureCityAvenue()
        {
            Directory.CreateDirectory(Folder);
            foreach (string stage in new[] { "Stage_CityDay", "Stage_CityNight" })
            {
                yield return EnterTraining(stage);
                yield return Wait(1.5f);
                Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
                string tag = stage.Replace("Stage_", "");
                Shoot(camera, camera.transform.position, camera.transform.rotation, camera.fieldOfView, $"Avenue_{tag}_0Game");
                Shoot(camera, new Vector3(0f, 12f, -70f), Quaternion.Euler(6f, 0f, 0f), 40f, $"Avenue_{tag}_1Overview");
                // 空撮のように高い所から見下ろす（雲海と、所々の雲の切れ間から遥か下の地上）。
                Shoot(camera, new Vector3(0f, 45f, -60f), Quaternion.Euler(28f, 0f, 0f), 45f, $"Avenue_{tag}_2Aerial");
                Shoot(camera, new Vector3(-40f, 8f, -45f), Quaternion.Euler(4f, 20f, 0f), 45f, $"Avenue_{tag}_3Side");
                // 雲海の下から大通りを見る（車と人影）。
                Shoot(camera, new Vector3(0f, CityStreetHeight + 14f, 150f), Quaternion.Euler(16f, 0f, 0f), 50f, $"Avenue_{tag}_4Street");
                Shoot(camera, new Vector3(-6f, CityStreetHeight + 2.5f, 160f), Quaternion.Euler(4f, 8f, 0f), 55f, $"Avenue_{tag}_5EyeLevel");
                // 遊ぶ画面の高さから地平線を見る（雲海から頭を出す遠くの超高層ビル群）。
                Shoot(camera, new Vector3(0f, 6f, -40f), Quaternion.Euler(3f, 0f, 0f), 50f, $"Avenue_{tag}_7Horizon");
                Shoot(camera, new Vector3(-60f, 10f, -40f), Quaternion.Euler(3f, -30f, 0f), 50f, $"Avenue_{tag}_8HorizonLeft");
                // 撃破の線の近くまで落ちたときの高さと、遊ぶ画面から少し見下ろしたとき（手前の雲の切れ間から遥か下の大通りがのぞくか）。
                Shoot(camera, new Vector3(0f, -12f, -35f), Quaternion.Euler(8f, 0f, 0f), 50f, $"Avenue_{tag}_9Falling");
                Shoot(camera, new Vector3(12f, 14f, -40f), Quaternion.Euler(24f, -6f, 0f), 50f, $"Avenue_{tag}_10LookDown");
                yield return Wait(4f);
                Shoot(camera, new Vector3(0f, CityStreetHeight + 14f, 150f), Quaternion.Euler(16f, 0f, 0f), 50f, $"Avenue_{tag}_6StreetLater");
            }
        }

        private static IEnumerator EnterTraining(string stage)
        {
            if (SceneManager.GetActiveScene().name != "App" && Object.FindFirstObjectByType<GameFlow>() == null)
            {
                SceneManager.LoadScene("App");
                yield return null;
            }
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.IsLoading || (flow.Current != GameFlow.FlowScreen.Menu && flow.Current != GameFlow.FlowScreen.Training))
            {
                yield return null;
            }
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == stage));
            while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != stage)
            {
                yield return null;
            }
            yield return Wait(0.5f);
        }

        private static void Shoot(Camera camera, Vector3 position, Quaternion rotation, float fov, string name)
        {
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            camera.transform.SetPositionAndRotation(position, rotation);
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
            camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
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
