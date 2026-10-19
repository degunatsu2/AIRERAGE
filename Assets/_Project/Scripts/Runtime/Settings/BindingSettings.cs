using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Airerage.Runtime.Settings
{
    /// <summary>
    /// キーの割り当ての変更（docs/15 T6-9。D-68）。変えられるのは "Player" の組のボタンの操作（ジャンプ・攻撃・ワイヤー・ガード・
    /// 特殊攻撃 2 つ・必殺技）の、キーボードの 1 つ目のキーとゲームパッドのボタン【仮】。移動（WASD・左スティック）、照準（マウス・
    /// 右スティック）、マウスのボタン、一時停止（Esc・Start）は変えない。
    /// 変更は Input System の割り当ての上書き（元のアセットは変えない）で、JSON にして GameSettings に保存する。試合の入力
    /// （InputSystemAdapter）を作るときに ApplySaved で当てる。
    /// </summary>
    public static class BindingSettings
    {
        public enum Column
        {
            Keyboard,
            Gamepad,
        }

        public const string MapName = "Player";

        /// <summary>変えられる操作（アクションの名前）と、オプションの画面に出す名前（同じ順）。</summary>
        public static readonly string[] Actions = { "Jump", "Attack", "Wire", "Guard", "Special1", "Special2", "Super" };
        public static readonly string[] Labels = { "JUMP", "ATTACK", "WIRE", "GUARD", "SKILL 1", "SKILL 2", "SUPER" };

        // ゲームパッドのボタンの、画面に出す名前（Xbox の配置の名前）。
        private static readonly Dictionary<string, string> GamepadNames = new Dictionary<string, string>
        {
            { "buttonSouth", "A" }, { "buttonEast", "B" }, { "buttonWest", "X" }, { "buttonNorth", "Y" },
            { "leftShoulder", "LB" }, { "rightShoulder", "RB" }, { "leftTrigger", "LT" }, { "rightTrigger", "RT" },
            { "leftStickPress", "L3" }, { "rightStickPress", "R3" }, { "start", "START" }, { "select", "SELECT" },
        };

        /// <summary>割り当ての変更を当てる（前の変更は消す。json が空なら元の割り当てに戻す）。</summary>
        public static void Apply(InputActionAsset asset, string json)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }
            asset.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(json))
            {
                asset.LoadBindingOverridesFromJson(json);
            }
        }

        /// <summary>保存した割り当ての変更を当てる。</summary>
        public static void ApplySaved(InputActionAsset asset)
        {
            Apply(asset, GameSettings.LoadBindingOverrides());
        }

        /// <summary>操作 action の、列 column の変えられる割り当ての番号（なければ -1）。</summary>
        public static int BindingIndex(InputAction action, Column column)
        {
            string prefix = column == Column.Keyboard ? "<Keyboard>/" : "<Gamepad>/";
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (!binding.isComposite && !binding.isPartOfComposite && binding.path != null && binding.path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>今の割り当て（上書きがあればそれ）の、画面に出す名前。</summary>
        public static string DisplayName(InputAction action, int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            {
                return "-";
            }
            return Readable(action.bindings[bindingIndex].effectivePath);
        }

        /// <summary>割り当ての道（"&lt;Keyboard&gt;/space" など）の、画面に出す名前。</summary>
        public static string Readable(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "-";
            }
            const string gamepad = "<Gamepad>/";
            if (path.StartsWith(gamepad, StringComparison.Ordinal))
            {
                string control = path.Substring(gamepad.Length);
                if (GamepadNames.TryGetValue(control, out string name))
                {
                    return name;
                }
                return control.Replace("dpad/", "D-PAD ").ToUpperInvariant();
            }
            return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant();
        }

        /// <summary>
        /// 操作 action の割り当て bindingIndex を、次に押したキー・ボタンに変える（列 column の機器だけ。キーボードは Esc、
        /// ゲームパッドは Start でやめる）。押したキーはほかへ伝えない（メニューの決定などにならない）。終わったら finished（変えたら true）。
        /// 操作の組は止めておく（変えている間は使えない）。
        /// </summary>
        public static InputActionRebindingExtensions.RebindingOperation StartRebind(InputAction action, int bindingIndex, Column column,
            Action<bool> finished)
        {
            InputActionRebindingExtensions.RebindingOperation operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithExpectedControlType("Button")
                .WithMatchingEventsBeingSuppressed()
                .OnMatchWaitForAnother(0.05f);
            if (column == Column.Keyboard)
            {
                operation.WithControlsHavingToMatchPath("<Keyboard>")
                    .WithControlsExcluding("<Keyboard>/anyKey")
                    .WithCancelingThrough("<Keyboard>/escape");
            }
            else
            {
                operation.WithControlsHavingToMatchPath("<Gamepad>")
                    .WithControlsExcluding("<Gamepad>/leftStick")
                    .WithControlsExcluding("<Gamepad>/rightStick")
                    .WithControlsExcluding("<Gamepad>/dpad")
                    .WithControlsExcluding("<Gamepad>/select")
                    .WithCancelingThrough("<Gamepad>/start");
            }
            operation.OnComplete(op =>
                {
                    op.Dispose();
                    finished?.Invoke(true);
                })
                .OnCancel(op =>
                {
                    op.Dispose();
                    finished?.Invoke(false);
                });
            return operation.Start();
        }

        /// <summary>
        /// 操作 changed の割り当てを変えた後、同じ列のほかの変えられる操作が同じキーを使っていたら、その操作に changed の前のキーを
        /// 渡す（入れ替える。1 つのキーで 2 つの操作をしないように）。入れ替えた操作の名前を返す（なければ null）。
        /// </summary>
        public static string SwapDuplicate(InputActionMap map, InputAction changed, Column column, string previousPath)
        {
            int index = BindingIndex(changed, column);
            if (index < 0)
            {
                return null;
            }
            string path = changed.bindings[index].effectivePath;
            foreach (string name in Actions)
            {
                InputAction other = map.FindAction(name, throwIfNotFound: true);
                if (other == changed)
                {
                    continue;
                }
                int otherIndex = BindingIndex(other, column);
                if (otherIndex >= 0 && string.Equals(other.bindings[otherIndex].effectivePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    other.ApplyBindingOverride(otherIndex, previousPath);
                    return name;
                }
            }
            return null;
        }
    }
}
