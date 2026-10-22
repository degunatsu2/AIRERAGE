using Airerage.Core.Ai;
using UnityEngine.InputSystem;

namespace Airerage.Runtime.Flow
{
    /// <summary>
    /// ローカル対戦（docs/15 T6-5。D-67）の 1 人分: 操作する機器（キーボード＋マウス、またはゲームパッド 1 つ）と、選んだキャラクター
    /// （CharacterRoster の番号）。参加の画面で作り、GameFlow を通して試合の仕組みへ渡す。番号（P1〜P4）は一覧の順。
    /// </summary>
    public sealed class LocalPlayerSetup
    {
        public LocalPlayerSetup(InputDevice[] devices, string deviceName, int character)
        {
            Devices = devices ?? new InputDevice[0];
            DeviceName = deviceName ?? string.Empty;
            Character = character;
        }

        /// <summary>操作する機器（空なら操作しない。シーンを直接再生したときの 2 人目など）。</summary>
        public InputDevice[] Devices { get; }

        /// <summary>機器の表示用の名前（KEYBOARD、GAMEPAD 1 など）。</summary>
        public string DeviceName { get; }

        /// <summary>キャラクター（CharacterRoster の番号）。</summary>
        public int Character { get; set; }

        /// <summary>CPU が操作するか（シングルモード。docs/16 T7-2）。</summary>
        public bool IsCpu { get; private set; }

        /// <summary>CPU の強さ（IsCpu のときだけ）。</summary>
        public CpuLevel CpuLevel { get; private set; }

        /// <summary>CPU が操作する人（機器なし）。</summary>
        public static LocalPlayerSetup Cpu(int character, CpuLevel level)
        {
            return new LocalPlayerSetup(new InputDevice[0], "CPU", character) { IsCpu = true, CpuLevel = level };
        }
    }
}
