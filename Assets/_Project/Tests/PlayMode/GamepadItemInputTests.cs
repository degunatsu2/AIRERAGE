using System.Collections;
using System.Reflection;
using Airerage.Core.Input;
using Airerage.Core.Items;
using Airerage.Runtime.Input;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Settings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// ゲームパッドのアイテムの操作（ユーザーの指定、2026-11-05。docs/20_item_plan.md）: X（ITEM MENU）を押しながら十字キーの左・上・右で
    /// アイテムの枠を押した瞬間に使い（1 ティックだけ押した入力）、X を押していない十字キーは枠にならない。X を押している間の A はガードでは
    /// なく捨てる（X + A + 十字キー）。X を押していなければ A はガード。
    /// </summary>
    public sealed class GamepadItemInputTests
    {
        private InputSettings.EditorInputBehaviorInPlayMode _savedBehavior;
        private InputSettings.BackgroundBehavior _savedBackground;

        [SetUp]
        public void SetUp()
        {
            GameSettings.DeleteSaved();
            _savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            _savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        [TearDown]
        public void TearDown()
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedBehavior;
            InputSystem.settings.backgroundBehavior = _savedBackground;
        }

        [UnityTest]
        public IEnumerator ItemMenu_DpadPressesUseTheSlot_AndAButtonDropsInsteadOfGuarding()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var zoom = Object.FindFirstObjectByType<CameraZoom>();
            var source = (InputActionAsset)typeof(CameraZoom).GetField("_inputActions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(zoom);
            Assert.That(source, Is.Not.Null, "前提: 入力のアセットがある");
            InputActionAsset asset = Object.Instantiate(source);
            var gamepad = InputSystem.AddDevice<Gamepad>();
            asset.devices = new[] { (InputDevice)gamepad };
            var input = new InputSystemAdapter(asset);
            input.Enable();
            try
            {
                InputButtons item1 = ItemRules.SlotButton(0);
                InputButtons item2 = ItemRules.SlotButton(1);

                // X を押していない十字キーの左は、枠にならない。
                yield return Hold(gamepad, new GamepadState().WithButton(GamepadButton.DpadLeft));
                SimInput tick = Tick(input);
                Assert.That(tick.IsHeld(item1), Is.False, "X を押していない十字キーでアイテムを使った");
                yield return Hold(gamepad, new GamepadState());
                Tick(input);

                // X を押しながら十字キーの左: そのティックだけ押した入力（次のティックで離した入力になり、使う）。
                yield return Hold(gamepad, new GamepadState().WithButton(GamepadButton.West));
                Tick(input);
                Assert.That(input.ItemMenuHeld, Is.True, "X で ITEM MENU にならない");
                yield return Hold(gamepad, new GamepadState().WithButton(GamepadButton.West).WithButton(GamepadButton.DpadLeft));
                tick = Tick(input);
                Assert.That(tick.IsHeld(item1), Is.True, "X + 左で 1 つ目の枠を押した入力にならない");
                Assert.That(input.ItemMenuSlot, Is.EqualTo(0));
                Assert.That(input.ItemMenuDrop, Is.False);
                Assert.That(input.ItemMenuDropHeld, Is.False, "A を押していないのにゴミ箱の印を出す入力になった");
                yield return null;
                tick = Tick(input);
                Assert.That(tick.IsHeld(item1), Is.False, "十字キーを押し続けると枠を押し続けた入力になる（押した瞬間に使わない）");

                // X を押している間の A は、ガードではなく捨てるキー。X + A + 上で 2 つ目の枠を捨てる入力。
                yield return Hold(gamepad, new GamepadState().WithButton(GamepadButton.West).WithButton(GamepadButton.South));
                tick = Tick(input);
                Assert.That(tick.IsHeld(InputButtons.Guard), Is.False, "X を押しているのに A でガードした");
                Assert.That(tick.IsHeld(InputButtons.ItemDrop), Is.True, "X + A が捨てるキーにならない");
                Assert.That(input.ItemMenuDropHeld, Is.True, "X + A でバッグの左にゴミ箱の印を出す入力にならない");
                yield return Hold(gamepad, new GamepadState().WithButton(GamepadButton.West).WithButton(GamepadButton.South).WithButton(GamepadButton.DpadUp));
                tick = Tick(input);
                Assert.That(tick.IsHeld(item2) && tick.IsHeld(InputButtons.ItemDrop), Is.True, "X + A + 上で 2 つ目の枠を捨てる入力にならない");
                Assert.That(input.ItemMenuDrop, Is.True);

                // X を離せば、A はガード（捨てるキーではない）。
                yield return Hold(gamepad, new GamepadState().WithButton(GamepadButton.South));
                tick = Tick(input);
                Assert.That(tick.IsHeld(InputButtons.Guard), Is.True, "X を離しても A でガードしない");
                Assert.That(tick.IsHeld(InputButtons.ItemDrop), Is.False, "X を離しても A が捨てるキーのまま");
                Assert.That(input.ItemMenuHeld, Is.False);
                Assert.That(input.ItemMenuDropHeld, Is.False, "X を離してもゴミ箱の印を出す入力のまま");
            }
            finally
            {
                input.Dispose();
                InputSystem.RemoveDevice(gamepad);
                Object.Destroy(asset);
            }
        }

        private static IEnumerator Hold(Gamepad gamepad, GamepadState state)
        {
            InputSystem.QueueStateEvent(gamepad, state);
            yield return null;
        }

        private static SimInput Tick(InputSystemAdapter input)
        {
            input.Poll(null, 0.2f);
            return input.ConsumeTick(System.Numerics.Vector3.Zero);
        }
    }
}
