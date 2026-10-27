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
    /// </summary>
    public sealed class MusicCueTests
    {
        private const string TitleMusicPath = "Assets/_Project/Audio/Music/Title.ogg";
        private const string TitleCuePath = "Assets/_Project/Data/Audio/TitleMusic.asset";

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
        public void LoopTags_AcceptLoopEndToo_AndReportMissingTags()
        {
            var tags = new Dictionary<string, string> { { "LOOPSTART", "100" }, { "LOOPEND", "900" } };
            Assert.That(OggLoopTags.TryGetLoop(tags, out long start, out long end), Is.True);
            Assert.That(start, Is.EqualTo(100));
            Assert.That(end, Is.EqualTo(900));
            Assert.That(OggLoopTags.TryGetLoop(new Dictionary<string, string>(), out _, out _), Is.False);
            Assert.Throws<InvalidDataException>(() => OggLoopTags.Read(new MemoryStream(new byte[64])));
        }
    }
}
