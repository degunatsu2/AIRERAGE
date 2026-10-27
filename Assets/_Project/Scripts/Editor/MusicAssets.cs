using System.IO;
using Airerage.Runtime.Audio;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor
{
    /// <summary>
    /// 曲と効果音の音声ファイルの読み込みの設定と、曲の MusicCue（ループの位置）を作る（docs/18 6 章）。シーンのビルダーが使う。
    /// 曲のループの位置は、ファイルのタグ（LOOPSTART・LOOPLENGTH。ファイルのサンプリング周波数の単位）から読む。
    /// </summary>
    internal static class MusicAssets
    {
        public const string MusicFolder = "Assets/_Project/Audio/Music";
        public const string SeFolder = "Assets/_Project/Audio/Se";
        public const string CueFolder = "Assets/_Project/Data/Audio";
        public const string TitleMusicPath = MusicFolder + "/Title.ogg";
        public const string TitleCuePath = CueFolder + "/TitleMusic.asset";
        public const string TitleStartSePath = SeFolder + "/TitleStart.wav";
        public const string CharacterDecideSePath = SeFolder + "/CharacterDecide.wav";

        /// <summary>曲の MusicCue を作り直す（読み込みの設定、ループの位置）。ループのタグがない・曲の外にあるときはエラーを出す。</summary>
        public static MusicCue LoadOrCreateCue(string musicPath, string cuePath, float volume)
        {
            // 曲はメモリに圧縮したまま置き、鳴らしながら戻す（ループの位置へサンプルの単位で飛べるように、ストリーミングにはしない）。
            SetImport(musicPath, AudioClipLoadType.CompressedInMemory, 0.7f);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(musicPath);
            if (clip == null)
            {
                throw new FileNotFoundException($"曲のファイルがない: {musicPath}");
            }
            int loopStart = 0;
            int loopEnd = 0;
            if (OggLoopTags.TryGetLoop(OggLoopTags.Read(musicPath), out long start, out long end))
            {
                loopStart = (int)start;
                loopEnd = (int)end;
                if (end > clip.samples)
                {
                    Debug.LogError($"{musicPath}: ループの終わり（{end}）が曲の長さ（{clip.samples} サンプル、{clip.frequency}Hz）を超えている。" +
                                   "タグは、ファイルのサンプリング周波数の単位で書く（44.1kHz の値のまま 48kHz のファイルにしていないか）");
                }
            }
            else
            {
                Debug.LogWarning($"{musicPath}: ループのタグ（LOOPSTART・LOOPLENGTH）がない。曲の頭から終わりまでをくり返す");
            }

            SandboxWireSceneBuilder.EnsureFolder(CueFolder);
            var cue = AssetDatabase.LoadAssetAtPath<MusicCue>(cuePath);
            if (cue == null)
            {
                cue = ScriptableObject.CreateInstance<MusicCue>();
                AssetDatabase.CreateAsset(cue, cuePath);
            }
            cue.Configure(clip, loopStart, loopEnd, volume);
            EditorUtility.SetDirty(cue);
            AssetDatabase.SaveAssets();
            return cue;
        }

        /// <summary>効果音の音声ファイル（短いので読み込むときに戻しておく）。</summary>
        public static AudioClip LoadSe(string path)
        {
            SetImport(path, AudioClipLoadType.DecompressOnLoad, 1f);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                throw new FileNotFoundException($"効果音のファイルがない: {path}");
            }
            return clip;
        }

        // 読み込みの設定を変える（同じなら何もしない。.meta を無駄に書き換えない）。
        private static void SetImport(string path, AudioClipLoadType loadType, float quality)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer))
            {
                throw new FileNotFoundException($"音声ファイルとして読み込めない: {path}");
            }
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType == loadType && settings.compressionFormat == AudioCompressionFormat.Vorbis &&
                Mathf.Approximately(settings.quality, quality) && settings.preloadAudioData)
            {
                return;
            }
            settings.loadType = loadType;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = quality;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }
}
