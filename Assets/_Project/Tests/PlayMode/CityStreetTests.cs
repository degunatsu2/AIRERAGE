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
            Assert.That(maxCars, Is.InRange(2, 14), "車がまばらに走っていない");
            Assert.That(maxPeople, Is.InRange(3, 18), "歩道に人影が歩いていない");
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
                Shoot(camera, new Vector3(0f, -10f, 30f), Quaternion.Euler(14f, 0f, 0f), 50f, $"Avenue_{tag}_2Street");
                Shoot(camera, new Vector3(-6f, -21.5f, 40f), Quaternion.Euler(4f, 8f, 0f), 55f, $"Avenue_{tag}_3EyeLevel");
                Shoot(camera, new Vector3(-24f, -17f, 140f), Quaternion.Euler(18f, 70f, 0f), 45f, $"Avenue_{tag}_4Side");
                yield return Wait(4f);
                Shoot(camera, new Vector3(0f, -10f, 30f), Quaternion.Euler(14f, 0f, 0f), 50f, $"Avenue_{tag}_5StreetLater");
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
