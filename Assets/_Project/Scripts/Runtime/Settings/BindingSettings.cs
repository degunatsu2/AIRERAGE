using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Airerage.Runtime.Settings
{
    /// <summary>
    /// キーの割り当ての変更（docs/15 T6-9。D-68）。変えられるのは "Player" の組のボタンの操作（ジャンプ・攻撃・ワイヤー・ガード・
    /// 特殊攻撃 2 つ・必殺技・アイテムの 3 枠・アイテムを捨てる。docs/20_item_plan.md）の、キーボードとマウスの 1 つ（キーか、マウスの左・
    /// 右・サイド・真ん中のボタン。ユーザーの要望、2026-10-20・2026-11-05）とゲームパッドのボタン【仮】。移動（WASD・左スティック）、照準
    /// （マウスの位置・右スティック）、マウスのホイール（ズーム）、一時停止（Esc・Start）は変えない。
    /// 変更は Input System の割り当ての上書き（元のアセットは変えない）で、JSON にして GameSettings に保存する。試合の入力
    /// （InputSystemAdapter）を作るときに ApplySaved で当てる。
    /// ゲームパッドのアイテムの操作（2026-11-05）: ITEM MENU（X）を押している間だけ、アイテムの 3 枠（十字キーの左・上・右）と捨てる（A）が
    /// 効く。この 4 つ（MenuLayer）は ITEM MENU と一緒に押す組なので、ゲームパッドの列では「X+左」のように出し、同じボタンの入れ替えは
    /// この 4 つの中でだけ行う（ガードの A と捨てるの A は、同じでよい）。十字キーを選べるのも、この 4 つだけ。
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
        public static readonly string[] Actions =
        {
            "Jump", "Attack", "Wire", "Guard", "Special1", "Special2", "Super", "Item1", "Item2", "Item3", "ItemDrop", "ItemMenu",
        };
        public static readonly string[] Labels =
        {
            "JUMP", "ATTACK", "WIRE", "GUARD", "SKILL 1", "SKILL 2", "SUPER", "ITEM 1", "ITEM 2", "ITEM 3", "DROP ITEM", "ITEM MENU",
        };

        /// <summary>ゲームパッドでは ITEM MENU を押しながら使う操作（docs/20_item_plan.md）。</summary>
        public static readonly string[] MenuLayer = { "Item1", "Item2", "Item3", "ItemDrop" };

        /// <summary>ゲームパッドで、ITEM MENU と一緒に押す操作か。</summary>
        public static bool InMenuLayer(string actionName)
        {
            return Array.IndexOf(MenuLayer, actionName) >= 0;
        }

        // キーボードの列で使えるマウスのボタンと、画面に出す名前。
        private static readonly string[] MouseButtons =
        {
            "<Mouse>/leftButton", "<Mouse>/rightButton", "<Mouse>/backButton", "<Mouse>/forwardButton", "<Mouse>/middleButton",
        };
        private static readonly Dictionary<string, string> MouseNames = new Dictionary<string, string>
        {
            { "leftButton", "LEFT CLICK" }, { "rightButton", "RIGHT CLICK" }, { "backButton", "MOUSE 4" }, { "forwardButton", "MOUSE 5" },
            { "middleButton", "MIDDLE CLICK" },
        };

        // ゲームパッドのボタンの、画面に出す名前（Xbox の配置の名前）。
        private static readonly Dictionary<string, string> GamepadNames = new Dictionary<string, string>
        {
            { "buttonSouth", "A" }, { "buttonEast", "B" }, { "buttonWest", "X" }, { "buttonNorth", "Y" },
            { "leftShoulder", "LB" }, { "rightShoulder", "RB" }, { "leftTrigger", "LT" }, { "rightTrigger", "RT" },
            { "leftStickPress", "L3" }, { "rightStickPress", "R3" }, { "start", "START" }, { "select", "SELECT" },
            { "dpad/left", "LEFT" }, { "dpad/up", "UP" }, { "dpad/right", "RIGHT" }, { "dpad/down", "DOWN" },
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

        /// <summary>
        /// 操作 action の、列 column の変えられる割り当ての番号（なければ -1）。キーボードの列は、キーかマウスのボタン
        /// （左・右・サイド）の最初の割り当て。
        /// </summary>
        public static int BindingIndex(InputAction action, Column column)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (!binding.isComposite && !binding.isPartOfComposite && binding.path != null && InColumn(binding.path, column))
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool InColumn(string path, Column column)
        {
            if (column == Column.Gamepad)
            {
                return path.StartsWith("<Gamepad>/", StringComparison.Ordinal);
            }
            return path.StartsWith("<Keyboard>/", StringComparison.Ordinal) || Array.IndexOf(MouseButtons, path) >= 0;
        }

        /// <summary>
        /// 今の割り当て（上書きがあればそれ）の、画面に出す名前。ゲームパッドの、ITEM MENU と一緒に押す操作は「X+LEFT」の形
        /// （捨てるは「X+A+D-PAD」）。
        /// </summary>
        public static string DisplayName(InputAction action, int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            {
                return "-";
            }
            string path = action.bindings[bindingIndex].effectivePath;
            string name = Readable(path);
            if (InMenuLayer(action.name) && InColumn(path ?? string.Empty, Column.Gamepad))
            {
                InputAction menu = action.actionMap?.FindAction("ItemMenu", throwIfNotFound: false);
                string menuName = menu != null ? DisplayName(menu, BindingIndex(menu, Column.Gamepad)) : "X";
                return action.name == "ItemDrop" ? $"{menuName}+{name}+D-PAD" : $"{menuName}+{name}";
            }
            return name;
        }

        /// <summary>
        /// 操作 actionName（"Item1" など）の、列 column の今の割り当ての名前（asset に当てた変更で。アイテムの選択の画面に出すキー）。
        /// </summary>
        public static string KeyName(InputActionAsset asset, string actionName, Column column)
        {
            InputAction action = asset != null ? asset.FindActionMap(MapName, throwIfNotFound: false)?.FindAction(actionName, throwIfNotFound: false) : null;
            return action == null ? "-" : DisplayName(action, BindingIndex(action, column));
        }

        /// <summary>割り当ての道（"&lt;Keyboard&gt;/space" など）の、画面に出す名前。</summary>
        public static string Readable(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "-";
            }
            const string mouse = "<Mouse>/";
            if (path.StartsWith(mouse, StringComparison.Ordinal) && MouseNames.TryGetValue(path.Substring(mouse.Length), out string mouseName))
            {
                return mouseName;
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
        /// 操作 action の割り当て bindingIndex を、次に押したキー・ボタンに変える（列 column の機器だけ。キーボードの列はキーと
        /// マウスの左・右・サイドのボタン。キーボードは Esc、
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
                operation.WithControlsHavingToMatchPath("<Keyboard>");
                foreach (string button in MouseButtons)
                {
                    operation.WithControlsHavingToMatchPath(button);
                }
                operation.WithControlsExcluding("<Keyboard>/anyKey")
                    .WithCancelingThrough("<Keyboard>/escape");
            }
            else
            {
                operation.WithControlsHavingToMatchPath("<Gamepad>")
                    .WithControlsExcluding("<Gamepad>/leftStick")
                    .WithControlsExcluding("<Gamepad>/rightStick")
                    .WithControlsExcluding("<Gamepad>/select")
                    .WithCancelingThrough("<Gamepad>/start");
                // 十字キーはカメラのズームに使うので、ITEM MENU と一緒に押す操作だけが選べる。
                if (!InMenuLayer(action.name))
                {
                    operation.WithControlsExcluding("<Gamepad>/dpad");
                }
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
                // ゲームパッドでは、ITEM MENU と一緒に押す操作とそれ以外は、同じボタンでもぶつからない。
                if (column == Column.Gamepad && InMenuLayer(name) != InMenuLayer(changed.name))
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
