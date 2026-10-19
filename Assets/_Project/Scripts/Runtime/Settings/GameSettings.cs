using System;
using UnityEngine;

namespace Airerage.Runtime.Settings
{
    /// <summary>
    /// オプションの設定（docs/15 T6-9。D-68）: 音量（全体・BGM・効果音）、画面（フルスクリーンかウィンドウか、解像度、垂直同期）、
    /// キーの割り当て（Input System の割り当ての変更を JSON で）。保存先は PlayerPrefs（プレイヤーネームやキャラクターの選択と同じ）。
    /// 読み込みと保存だけを受け持ち、今の値をどこかに持ち続けることはしない（使う側が Load で読む）。
    /// 音量は 0〜10 の 11 段階（10 が今までの大きさ）。
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const int MaxVolume = 10;

        private const string Prefix = "Options.";
        private const string MasterKey = Prefix + "Master";
        private const string BgmKey = Prefix + "Bgm";
        private const string SeKey = Prefix + "Se";
        private const string FullscreenKey = Prefix + "Fullscreen";
        private const string WidthKey = Prefix + "Width";
        private const string HeightKey = Prefix + "Height";
        private const string VSyncKey = Prefix + "VSync";
        private const string BindingsKey = Prefix + "Bindings";

        private static readonly string[] AllKeys = { MasterKey, BgmKey, SeKey, FullscreenKey, WidthKey, HeightKey, VSyncKey, BindingsKey };

        public int MasterVolume = MaxVolume;
        public int BgmVolume = MaxVolume;
        public int SeVolume = MaxVolume;
        public bool Fullscreen = true;

        /// <summary>解像度（0 なら、決めていない。今の大きさのまま）。</summary>
        public int Width;
        public int Height;

        public bool VSync = true;

        /// <summary>キーの割り当ての変更（InputActionAsset.SaveBindingOverridesAsJson の形。空なら変更なし）。</summary>
        public string BindingOverrides = string.Empty;

        public float Master01 => Volume01(MasterVolume);
        public float Bgm01 => Volume01(BgmVolume);
        public float Se01 => Volume01(SeVolume);

        /// <summary>解像度を決めているか。</summary>
        public bool HasScreen => Width > 0 && Height > 0;

        /// <summary>画面の設定を保存したことがあるか（なければ、起動したときに画面を変えない）。</summary>
        [NonSerialized] public bool ScreenSaved;

        public static float Volume01(int volume)
        {
            return Mathf.Clamp(volume, 0, MaxVolume) / (float)MaxVolume;
        }

        /// <summary>保存した設定を読む（保存していない項目は初めの値）。</summary>
        public static GameSettings Load()
        {
            var settings = new GameSettings
            {
                MasterVolume = Mathf.Clamp(PlayerPrefs.GetInt(MasterKey, MaxVolume), 0, MaxVolume),
                BgmVolume = Mathf.Clamp(PlayerPrefs.GetInt(BgmKey, MaxVolume), 0, MaxVolume),
                SeVolume = Mathf.Clamp(PlayerPrefs.GetInt(SeKey, MaxVolume), 0, MaxVolume),
                Fullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) != 0,
                Width = Mathf.Max(0, PlayerPrefs.GetInt(WidthKey, 0)),
                Height = Mathf.Max(0, PlayerPrefs.GetInt(HeightKey, 0)),
                VSync = PlayerPrefs.GetInt(VSyncKey, 1) != 0,
                BindingOverrides = PlayerPrefs.GetString(BindingsKey, string.Empty),
                ScreenSaved = PlayerPrefs.HasKey(FullscreenKey),
            };
            return settings;
        }

        /// <summary>保存したキーの割り当ての変更だけを読む（試合の入力を作るとき）。</summary>
        public static string LoadBindingOverrides()
        {
            return PlayerPrefs.GetString(BindingsKey, string.Empty);
        }

        /// <summary>保存する（PlayerPrefs.Save まで行う）。</summary>
        public void Save()
        {
            PlayerPrefs.SetInt(MasterKey, Mathf.Clamp(MasterVolume, 0, MaxVolume));
            PlayerPrefs.SetInt(BgmKey, Mathf.Clamp(BgmVolume, 0, MaxVolume));
            PlayerPrefs.SetInt(SeKey, Mathf.Clamp(SeVolume, 0, MaxVolume));
            PlayerPrefs.SetInt(FullscreenKey, Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(WidthKey, Mathf.Max(0, Width));
            PlayerPrefs.SetInt(HeightKey, Mathf.Max(0, Height));
            PlayerPrefs.SetInt(VSyncKey, VSync ? 1 : 0);
            PlayerPrefs.SetString(BindingsKey, BindingOverrides ?? string.Empty);
            PlayerPrefs.Save();
            ScreenSaved = true;
        }

        /// <summary>保存した設定を消す（初めの値に戻す。テストでも使う）。</summary>
        public static void DeleteSaved()
        {
            foreach (string key in AllKeys)
            {
                PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
        }

        /// <summary>全体の音量を当てる（AudioListener。BGM と効果音の音量は GameAudio・UiSounds が読む）。</summary>
        public void ApplyAudio()
        {
            AudioListener.volume = Master01;
        }

        /// <summary>画面の設定を当てる（解像度を決めていなければ、フルスクリーンかどうかと垂直同期だけ）。</summary>
        public void ApplyScreen()
        {
            FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (HasScreen)
            {
                Screen.SetResolution(Width, Height, mode);
            }
            else if (Screen.fullScreenMode != mode)
            {
                Screen.fullScreenMode = mode;
            }
            QualitySettings.vSyncCount = VSync ? 1 : 0;
        }

        public GameSettings Clone()
        {
            return (GameSettings)MemberwiseClone();
        }
    }
}
