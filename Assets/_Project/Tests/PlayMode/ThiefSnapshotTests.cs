using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>
    /// THIEF の表示の画像（見た目の確認用。名前で選んだときだけ動く）。煙・透明の影と分身を Logs/Snapshots に PNG で保存する。
    /// 画面を描くので、-nographics を付けずに仮想の画面で動かす（docs/08 4.2）:
    /// xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath . -force-glcore -runTests -testPlatform PlayMode
    /// -testFilter Airerage.PlayMode.Tests.ThiefSnapshotTests
    /// </summary>
    [Explicit("画像を撮るときだけ")]
    public sealed class ThiefSnapshotTests
    {
        private const string Folder = "Logs/Snapshots";

        [UnityTest]
        public IEnumerator CaptureSmokeAndInvisibility()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            for (int i = 0; i < 6; i++)
            {
                runner.CycleCharacter();
            }
            yield return Wait(0.8f);
            Directory.CreateDirectory(Folder);
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();

            Capture(camera, "Thief_Standing");
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Attack), default });
            yield return Wait(0.45f);
            Capture(camera, "Thief_Smoke");
            yield return Wait(2f);

            runner.InputOverride = new Script(new List<SimInput> { new SimInput(-1, 0, InputButtons.Special2), default });
            yield return Wait(0.5f);
            Capture(camera, "Thief_Invisible");
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
