using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        /// 予告（緊張感のある BGM）からは、切り替えずに 1.2 倍の速さで鳴らし続ける（docs/18 4.12）。F9（BgmOn）で止めて、また鳴らせる
        /// （速さはそのまま）。再戦などで緊張感のある BGM をやめると、元の速さに戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator StageMusic_PlaysInTheManor_AndSpeedsUpInCoinRush()
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
                if (scene == "Stage_Lab" || scene == "Stage_Stadium" || scene == "Stage_CityNight" || scene == "Stage_CityDay"
                    || scene == "Stage_Volcano")
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

            Assert.That(audio.StageMusicSpeed, Is.EqualTo(1f));
            audio.SetBgmTense(true);
            Assert.That(audio.BgmTense, Is.True);
            Assert.That(audio.StageMusicPlaying, Is.True, "100 枚のコインの予告でステージの曲が止まる");
            Assert.That(audio.StageMusicSpeed, Is.EqualTo(1.2f), "100 枚のコインの予告でステージの曲が 1.2 倍の速さにならない");
            yield return WaitFor(() => audio.StageMusicPlayer.Parts.Last().Speed == 1.2f, 2f, "曲の予約が 1.2 倍の速さにならない");
            audio.BgmOn = false;
            Assert.That(audio.StageMusicPlaying, Is.False, "F9 で止まらない");
            audio.BgmOn = true;
            Assert.That(audio.StageMusicPlaying, Is.True, "F9 でまた鳴らない");
            Assert.That(audio.StageMusicPlayer.Parts.All(part => part.Speed == 1.2f), Is.True, "F9 でまた鳴らすと元の速さに戻る");
            audio.SetBgmTense(false);
            Assert.That(audio.StageMusicSpeed, Is.EqualTo(1f), "緊張感のある BGM をやめても元の速さに戻らない");
            yield return WaitFor(() => audio.StageMusicPlayer.Parts.Last().Speed == 1f, 2f, "曲の予約が元の速さに戻らない");
        }

        /// <summary>
        /// 曲の速さを変える（docs/18 4.12。COIN RUSH で 1.2 倍）: 鳴らした直後・鳴っている途中・つなぎ目の近く・つなぎ目の直前のどれで変えても、
        /// 予約した区間がすき間も重なりもなくつながり、鳴る位置が飛ばない（途中で引き継いだら同じ位置から、ループの終わりからはループの
        /// 始まりへ）。新しい速さになるまでの遅れも確かめる。時計は偽物（音の出る機器がなくても確かめられる）。
        /// </summary>
        [Test]
        public void LoopingMusic_ChangingSpeed_KeepsTheLoopSeamless()
        {
            const int rate = 44100;
            const int loopStart = 2 * rate;
            const int loopEnd = 8 * rate;
            var host = new GameObject("LoopingMusicTest");
            AudioClip clip = AudioClip.Create("Loop", 10 * rate, 1, rate, false);
            var cue = ScriptableObject.CreateInstance<MusicCue>();
            cue.name = "LoopTest";
            cue.Configure(clip, loopStart, loopEnd, 0f);
            double now = 1.0;
            var music = new LoopingMusic(host, host, () => now);
            void Advance(double seconds)
            {
                double until = now + seconds;
                while (now < until)
                {
                    now += 1.0 / 60.0;
                    music.Update();
                    AssertSeamless(music, loopStart, loopEnd, rate);
                }
            }
            double NextSeam() => music.Parts.Max(part => part.Start);
            try
            {
                // 鳴らした直後（まだ何も鳴っていない）: 頭から予約し直す。
                music.Play(cue);
                music.Speed = 1.2f;
                Assert.That(music.Parts.Count, Is.EqualTo(2));
                Assert.That(music.Parts.All(part => part.Speed == 1.2f), Is.True, "鳴らした直後に速さを変えても、予約が新しい速さにならない");
                Assert.That(music.Parts[0].From, Is.EqualTo(0), "前奏から鳴らさない");
                AssertSeamless(music, loopStart, loopEnd, rate);

                // 鳴っている途中（次のつなぎ目まで 1 秒より長い）: 0.1 秒後に、その位置から新しい速さで引き継ぐ。
                Advance(3.0);
                double changed = now;
                music.Speed = 1f;
                Assert.That(SpeedAt(music, changed + 0.05), Is.EqualTo(1.2f));
                Assert.That(SpeedAt(music, changed + 0.11), Is.EqualTo(1f), "鳴っている途中で速さを変えても、すぐに変わらない");
                LoopingMusic.Part spliced = music.Parts.Last();
                Assert.That(spliced.From, Is.GreaterThan(0).And.LessThan(loopEnd), "途中の位置から引き継がない");
                AssertSeamless(music, loopStart, loopEnd, rate);

                // つなぎ目の 0.5 秒前: つなぎ目から新しい速さにする（引き継いだほうが鳴り始め、次のくり返しを予約してから）。
                Advance(0.5);
                double seam = NextSeam();
                Assert.That(seam, Is.EqualTo(spliced.End), "引き継いだ区間の後を予約していない");
                Advance(seam - 0.5 - now);
                music.Speed = 1.2f;
                Assert.That(SpeedAt(music, seam - 0.01), Is.EqualTo(1f));
                Assert.That(SpeedAt(music, seam + 0.01), Is.EqualTo(1.2f), "つなぎ目の近くで速さを変えても、つなぎ目から変わらない");
                AssertSeamless(music, loopStart, loopEnd, rate);

                // つなぎ目の直前（もう片方が鳴り始める寸前）: つなぎ目を過ぎて、空いたほうを予約してから引き継ぐ。
                Advance(1.0);
                seam = NextSeam();
                Advance(seam - 0.05 - now);
                music.Speed = 1f;
                Advance(0.5);
                Assert.That(SpeedAt(music, seam + 0.05), Is.EqualTo(1.2f));
                Assert.That(SpeedAt(music, seam + 0.3), Is.EqualTo(1f), "つなぎ目の直前で速さを変えると、つなぎ目の後もしばらく変わらない");

                // その後も何周もつながる。
                Advance(20.0);
                Assert.That(music.Parts.Last().Speed, Is.EqualTo(1f));

                // 止めている間に変えた速さは、次に鳴らすときから。
                music.Stop();
                Assert.That(music.Parts.Count, Is.EqualTo(0));
                music.Speed = 1.2f;
                Assert.That(music.Parts.Count, Is.EqualTo(0));
                music.Play(cue);
                Assert.That(music.Parts.All(part => part.Speed == 1.2f), Is.True);
                Assert.Throws<System.ArgumentOutOfRangeException>(() => music.Speed = 0f);
            }
            finally
            {
                music.Stop();
                Object.Destroy(host);
                Object.Destroy(cue);
                Object.Destroy(clip);
            }
        }

        // 予約した区間が、すき間も重なりもなく、鳴る位置が飛ばずにつながっているか（ループの終わりからは、ループの始まりへ）。
        private static void AssertSeamless(LoopingMusic music, int loopStart, int loopEnd, int rate)
        {
            List<LoopingMusic.Part> parts = music.Parts.OrderBy(part => part.Start).ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                LoopingMusic.Part part = parts[i];
                Assert.That(part.Start - part.ScheduledAt, Is.GreaterThanOrEqualTo(0.05), "鳴り始める直前（か過ぎてから）に予約している");
                Assert.That(part.End, Is.GreaterThan(part.Start), "長さのない区間を予約している");
                Assert.That(part.PositionAt(part.End, rate), Is.LessThanOrEqualTo(loopEnd + 0.5), "ループの終わりを越えて鳴らす");
                if (i == 0)
                {
                    continue;
                }
                LoopingMusic.Part before = parts[i - 1];
                Assert.That(part.Start, Is.EqualTo(before.End).Within(1e-9), "前の区間との間にすき間か重なりがある");
                Assert.That(part.Source, Is.Not.EqualTo(before.Source), "同じ AudioSource を続けて使っている");
                double end = before.PositionAt(before.End, rate);
                if (System.Math.Abs(end - loopEnd) < 0.5)
                {
                    Assert.That(part.From, Is.EqualTo(loopStart), "ループの終わりから、ループの始まりへ戻らない");
                }
                else
                {
                    Assert.That((double)part.From, Is.EqualTo(end).Within(0.5), "途中で引き継いだ位置が飛んでいる");
                }
            }
        }

        // time に鳴っている区間の速さ。
        private static float SpeedAt(LoopingMusic music, double time)
        {
            foreach (LoopingMusic.Part part in music.Parts)
            {
                if (part.Start <= time && time < part.End)
                {
                    return part.Speed;
                }
            }
            Assert.Fail($"{time:F3} 秒に鳴っている区間がない");
            return 0f;
        }

        /// <summary>
        /// ステージの曲のファイルを読み込めていない（Git LFS のファイルを取り込んでいない）ときは、黙って無音にせず、エラーを出して仮の BGM を
        /// 鳴らす（2026-10-28。昼のビル街で BGM が鳴らなかった件）。
        /// </summary>
        [UnityTest]
        public IEnumerator StageMusic_WithoutItsFile_ReportsIt_AndPlaysTheTemporaryBgm()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var broken = ScriptableObject.CreateInstance<MusicCue>();
            broken.name = "BrokenMusic";
            broken.Configure(null, 0, 0, 1f);
            var stage = ScriptableObject.CreateInstance<StageDefinition>();
            stage.Configure("BROKEN", "Stage_CityDay", string.Empty, string.Empty, Color.white);
            stage.SetMusic(broken);
            try
            {
                Assert.That(broken.IsPlayable, Is.False);
                LogAssert.Expect(LogType.Error, new Regex("BrokenMusic の曲を鳴らせない.*git lfs pull"));
                flow.StartTraining(stage);
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
                yield return null;
                var audio = Object.FindFirstObjectByType<GameAudio>();
                Assert.That(audio.StageMusic, Is.Null, "鳴らせないステージの曲を使っている");
                Assert.That(audio.StageMusicPlaying, Is.False);
                Assert.That(audio.BgmEnabled, Is.True, "BGM を鳴らす設定になっていない");
                Assert.That(audio.BgmSource.clip, Is.Not.Null, "仮の BGM がない（無音になる）");
                Assert.That(audio.BgmVolume, Is.EqualTo(0.3f * GameSettings.Load().Bgm01).Within(1e-3f), "仮の BGM の音量でない");
            }
            finally
            {
                Object.Destroy(stage);
                Object.Destroy(broken);
            }
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
