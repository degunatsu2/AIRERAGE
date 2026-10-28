using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Input;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 仮の BGM と効果音（ユーザーの要望、2026-10-13）。BGM が繰り返し鳴る設定になっていて、攻撃を出して当てると効果音を鳴らす。
    /// バッチモードには音を出す機器がないので、鳴らしたかどうかは GameAudio.History で確かめる（音そのものは耳で確かめる）。
    /// </summary>
    public sealed class AudioTests
    {
        [UnityTest]
        public IEnumerator Bgm_IsSetUpToLoop_AndF9CanStopIt()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(audio, Is.Not.Null, "GameAudio がない");
            Assert.That(audio.BgmSource.clip, Is.Not.Null);
            Assert.That(audio.BgmSource.clip.length, Is.GreaterThan(10f));
            Assert.That(audio.BgmSource.loop, Is.True);
            Assert.That(audio.BgmEnabled, Is.True, "BGM を鳴らす設定になっていない");

            audio.BgmOn = false;
            Assert.That(audio.BgmEnabled, Is.False);
        }

        [UnityTest]
        public IEnumerator AttackingTheDummy_PlaysSwingAndHit()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            float start = Time.time;
            while (Time.time - start < 0.5f)
            {
                yield return null;
            }

            var inputs = new List<SimInput>();
            for (int i = 0; i < 12; i++)
            {
                inputs.Add(SimInput.FromAxes(1f, 0f, InputButtons.None));
            }
            inputs.Add(SimInput.FromAxes(1f, 0f, InputButtons.Attack));
            for (int i = 0; i < 40; i++)
            {
                inputs.Add(default);
            }
            runner.InputOverride = new Script(inputs);
            start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 3f)
            {
                yield return null;
            }

            Assert.That(audio.History.Contains(SoundId.Swing), Is.True, "攻撃の音が鳴らない");
            Assert.That(audio.History.Contains(SoundId.Hit), Is.True, "当たった音が鳴らない");
        }

        /// <summary>
        /// ステージの曲（docs/18 4.5）: 恐怖の館の試合では、ステージの曲（ループのタグ付き）を鳴らす。メニューの曲は止まる。100 枚のコインの
        /// 予告（緊張感のある BGM）でも切り替えずに鳴らし続ける。F9（BgmOn）で止めて、また鳴らせる。
        /// </summary>
        [UnityTest]
        public IEnumerator StageMusic_PlaysInTheManor_AndKeepsPlayingInCoinRush()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            Assert.That(flow, Is.Not.Null);
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            StageDefinition manor = null;
            for (int i = 0; i < flow.Stages.Count; i++)
            {
                if (flow.Stages.Get(i).SceneName == "Stage_Manor")
                {
                    manor = flow.Stages.Get(i);
                }
            }
            Assert.That(manor, Is.Not.Null, "恐怖の館のステージがない");
            Assert.That(manor.Music, Is.Not.Null, "恐怖の館に曲が設定されていない");
            Assert.That(manor.Music.HasValidLoop, Is.True, "恐怖の館の曲のループの位置が曲の中にない");
            Assert.That(flow.Stages.Get(0).Music, Is.Null, "曲のないステージ（ATHLETIC）に曲が入っている");
            for (int i = 0; i < flow.Stages.Count; i++)
            {
                string scene = flow.Stages.Get(i).SceneName;
                if (scene == "Stage_Lab" || scene == "Stage_Stadium" || scene == "Stage_CityNight" || scene == "Stage_CityDay")
                {
                    Assert.That(flow.Stages.Get(i).Music, Is.Not.Null, $"{scene} に曲が設定されていない");
                    Assert.That(flow.Stages.Get(i).Music.HasValidLoop, Is.True, $"{scene} の曲のループの位置が曲の中にない");
                }
            }

            flow.StartTraining(manor);
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
            yield return null;
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(audio.StageMusic, Is.SameAs(manor.Music), "ステージの曲を使わない");
            Assert.That(audio.StageMusicPlaying, Is.True, "ステージの曲を鳴らさない");
            Assert.That(audio.BgmEnabled, Is.True);
            Assert.That(audio.BgmVolume, Is.EqualTo(0.8f * GameSettings.Load().Bgm01).Within(1e-3f), "ステージの曲の音量が違う");
            var music = Object.FindFirstObjectByType<MusicPlayer>();
            yield return WaitFor(() => music.Current == null, 2f, "試合でもメニューの曲が鳴っている");

            audio.SetBgmTense(true);
            Assert.That(audio.BgmTense, Is.True);
            Assert.That(audio.StageMusicPlaying, Is.True, "100 枚のコインの予告でステージの曲が止まる");
            audio.BgmOn = false;
            Assert.That(audio.StageMusicPlaying, Is.False, "F9 で止まらない");
            audio.BgmOn = true;
            Assert.That(audio.StageMusicPlaying, Is.True, "F9 でまた鳴らない");
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, float seconds, string message)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > seconds)
                {
                    Assert.Fail(message);
                }
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
