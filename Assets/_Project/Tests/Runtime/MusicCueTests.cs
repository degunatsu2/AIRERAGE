using System.Collections.Generic;
using System.IO;
using Airerage.Runtime.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// メニューの曲（docs/18 6 章）: 曲のファイルのタグ（LOOPSTART・LOOPLENGTH）からループの位置を読み、MusicCue に入っている。
    /// タイトルの曲は、ユーザーのタグが 44.1kHz の単位だったので 48kHz の値に直した（始まり 49.475 秒、終わりは曲の終わり）。
    /// キャラクター・ステージの選択の曲は、頭の無音を切り、くり返しの波形からループを作った（tools/audio/music_loop.py。44.1kHz、
    /// 始まり 8.170 秒、1 周 58.904 秒、終わりは曲の終わり）。恐怖の館の曲は、LOOPLENGTH に曲全体の長さが入っていたので、
    /// ループの長さ（曲の終わり − 始まり）に直し、その後に頭の無音（1.662 秒）を切った（ループの位置は同じ。44.1kHz、始まり 8.791 秒、
    /// 終わりは曲の終わり）。研究所の曲と競技場の曲は、くり返しの波形からループを作った（44.1kHz。研究所は始まり 8.540 秒・1 周 59.077 秒、
    /// 競技場は始まり 13.480 秒・1 周 53.161 秒、夜のビル街は始まり 15.820 秒・1 周 76.820 秒、昼のビル街は始まり 15.780 秒・1 周 61.440 秒）。
    /// </summary>
    public sealed class MusicCueTests
    {
        private const string TitleMusicPath = "Assets/_Project/Audio/Music/Title.ogg";
        private const string TitleCuePath = "Assets/_Project/Data/Audio/TitleMusic.asset";
        private const string SelectMusicPath = "Assets/_Project/Audio/Music/Select.ogg";
        private const string SelectCuePath = "Assets/_Project/Data/Audio/SelectMusic.asset";
        private const string ManorMusicPath = "Assets/_Project/Audio/Music/Manor.ogg";
        private const string ManorCuePath = "Assets/_Project/Data/Audio/ManorMusic.asset";
        private const string LabMusicPath = "Assets/_Project/Audio/Music/Lab.ogg";
        private const string LabCuePath = "Assets/_Project/Data/Audio/LabMusic.asset";

        [Test]
        public void TitleMusic_LoopTags_AreInTheFileSampleRate()
        {
            Dictionary<string, string> tags = OggLoopTags.Read(TitleMusicPath);
            Assert.That(OggLoopTags.TryGetLoop(tags, out long start, out long end), Is.True, "ループのタグがない");
            Assert.That(start, Is.EqualTo(2374798));
            Assert.That(end, Is.EqualTo(3942860), "ループの終わりが曲の終わりでない");
            Assert.That(tags["TITLE"], Is.EqualTo("TITLE"));
        }

        [Test]
        public void TitleCue_HasTheClipAndAValidLoop()
        {
            var cue = AssetDatabase.LoadAssetAtPath<MusicCue>(TitleCuePath);
            Assert.That(cue, Is.Not.Null, "タイトルの曲の MusicCue がない（FlowSceneBuilder が作る）");
            Assert.That(cue.Clip, Is.Not.Null);
            Assert.That(cue.Clip.frequency, Is.EqualTo(48000));
            Assert.That(cue.LoopStart, Is.EqualTo(2374798));
            Assert.That(cue.HasValidLoop, Is.True, $"ループの終わり {cue.LoopEnd} が曲の長さ {cue.Clip.samples} を超えている");
            Assert.That((cue.LoopEnd - cue.LoopStart) / (float)cue.Clip.frequency, Is.GreaterThan(20f), "ループが短すぎる");
        }

        [Test]
        public void SelectMusic_LoopTags_AndCue()
        {
            Dictionary<string, string> tags = OggLoopTags.Read(SelectMusicPath);
            Assert.That(OggLoopTags.TryGetLoop(tags, out long start, out long end), Is.True, "ループのタグがない");
            Assert.That(start, Is.EqualTo(360297));
            Assert.That(end - start, Is.EqualTo(2597674));
            var cue = AssetDatabase.LoadAssetAtPath<MusicCue>(SelectCuePath);
            Assert.That(cue, Is.Not.Null, "選択の曲の MusicCue がない（FlowSceneBuilder が作る）");
            Assert.That(cue.Clip, Is.Not.Null);
            Assert.That(cue.Clip.frequency, Is.EqualTo(44100));
            Assert.That(cue.Clip.samples, Is.EqualTo(end), "ループの終わりが曲の終わりでない");
            Assert.That(cue.LoopStart, Is.EqualTo(360297));
            Assert.That(cue.HasValidLoop, Is.True, $"ループの終わり {cue.LoopEnd} が曲の長さ {cue.Clip.samples} を超えている");
        }

        [Test]
        public void ManorMusic_LoopTags_AndCue()
        {
            Dictionary<string, string> tags = OggLoopTags.Read(ManorMusicPath);
            Assert.That(OggLoopTags.TryGetLoop(tags, out long start, out long end), Is.True, "ループのタグがない");
            Assert.That(start, Is.EqualTo(387693));
            Assert.That(end - start, Is.EqualTo(1509491), "頭を切ったらループの長さが変わった");
            Assert.That(end, Is.EqualTo(1897184), "ループの終わりが曲の終わりでない");
            var cue = AssetDatabase.LoadAssetAtPath<MusicCue>(ManorCuePath);
            Assert.That(cue, Is.Not.Null, "恐怖の館の曲の MusicCue がない（FlowSceneBuilder が作る）");
            Assert.That(cue.Clip, Is.Not.Null);
            Assert.That(cue.Clip.frequency, Is.EqualTo(44100));
            Assert.That(cue.Clip.samples, Is.EqualTo(end), "曲の長さがタグと合わない");
            Assert.That(cue.HasValidLoop, Is.True);
        }

        [TestCase(LabMusicPath, LabCuePath, 376614, 2605292)]
        [TestCase("Assets/_Project/Audio/Music/Stadium.ogg", "Assets/_Project/Data/Audio/StadiumMusic.asset", 594468, 2344395)]
        [TestCase("Assets/_Project/Audio/Music/CityNight.ogg", "Assets/_Project/Data/Audio/CityNightMusic.asset", 697662, 3387776)]
        [TestCase("Assets/_Project/Audio/Music/CityDay.ogg", "Assets/_Project/Data/Audio/CityDayMusic.asset", 695898, 2709504)]
        public void StageMusic_LoopTags_AndCue(string musicPath, string cuePath, long loopStart, long loopLength)
        {
            Dictionary<string, string> tags = OggLoopTags.Read(musicPath);
            Assert.That(OggLoopTags.TryGetLoop(tags, out long start, out long end), Is.True, "ループのタグがない");
            Assert.That(start, Is.EqualTo(loopStart));
            Assert.That(end - start, Is.EqualTo(loopLength));
            var cue = AssetDatabase.LoadAssetAtPath<MusicCue>(cuePath);
            Assert.That(cue, Is.Not.Null, $"{cuePath} がない（FlowSceneBuilder が作る）");
            Assert.That(cue.Clip.frequency, Is.EqualTo(44100));
            Assert.That(cue.Clip.samples, Is.EqualTo(end), "ループの終わりが曲の終わりでない");
            Assert.That(cue.HasValidLoop, Is.True);
        }

        [Test]
        public void LoopTags_AcceptLoopEndToo_AndReportMissingTags()
        {
            var tags = new Dictionary<string, string> { { "LOOPSTART", "100" }, { "LOOPEND", "900" } };
            Assert.That(OggLoopTags.TryGetLoop(tags, out long start, out long end), Is.True);
            Assert.That(start, Is.EqualTo(100));
            Assert.That(end, Is.EqualTo(900));
            Assert.That(OggLoopTags.TryGetLoop(new Dictionary<string, string>(), out _, out _), Is.False);
            Assert.Throws<InvalidDataException>(() => OggLoopTags.Read(new MemoryStream(new byte[64])));
            // Git LFS の目印のファイル（曲のファイルを取り込んでいない）は、そうと分かるエラーにする。
            byte[] pointer = System.Text.Encoding.ASCII.GetBytes(
                "version https://git-lfs.github.com/spec/v1\noid sha256:0000000000000000000000000000000000000000000000000000000000000000\nsize 100\n");
            var error = Assert.Throws<InvalidDataException>(() => OggLoopTags.Read(new MemoryStream(pointer)));
            Assert.That(error.Message, Does.Contain("git lfs pull"));
        }
    }
}
