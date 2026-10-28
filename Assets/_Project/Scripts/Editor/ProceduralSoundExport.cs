using System;
using System.IO;
using Airerage.Runtime.Audio;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor
{
    /// <summary>
    /// 合成している仮の効果音（ProceduralAudio）を WAV に書き出す（聞き比べと、音の大きさ・帯域を数値で確かめるため。docs/18）。
    /// 出力は Logs/ProceduralSe/（Git には入れない）。バッチモードでは -executeMethod Airerage.Editor.ProceduralSoundExport.ExportAll、
    /// 出力先は -exportDir で変えられる。
    /// </summary>
    internal static class ProceduralSoundExport
    {
        private const string DefaultFolder = "Logs/ProceduralSe";

        [MenuItem("Airerage/仮の効果音を WAV に書き出す（確認用）")]
        public static void ExportAll()
        {
            string folder = ArgumentAfter("-exportDir") ?? DefaultFolder;
            Directory.CreateDirectory(folder);
            foreach (SoundId id in (SoundId[])Enum.GetValues(typeof(SoundId)))
            {
                WriteWav(Path.Combine(folder, id + ".wav"), ProceduralAudio.Synthesize(id), ProceduralAudio.SeSampleRate);
            }
            Debug.Log($"仮の効果音を {Path.GetFullPath(folder)} に書き出した");
        }

        private static string ArgumentAfter(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        // 16 ビット、モノラルの WAV。
        private static void WriteWav(string path, float[] samples, int rate)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(36 + bytes);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(rate);
                writer.Write(rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(bytes);
                foreach (float sample in samples)
                {
                    writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(sample * 32767f), -32768, 32767));
                }
            }
        }
    }
}
