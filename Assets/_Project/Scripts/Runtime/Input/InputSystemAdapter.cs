using System;
using Airerage.Core.Input;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.Runtime.Input
{
    /// <summary>
    /// Input System を読み、ティックごとの PlayerInput を作る（docs/03_architecture.md 4.5）。
    /// Input System を直接読むのはここだけで、シミュレーションは PlayerInput だけを見る。
    /// アクションは AirerageControls.inputactions の "Player" マップ（Move / Jump / Wire / Attack / Guard / Special1 / Special2 / Super /
    /// Reset / Point / Aim）。特殊攻撃 2 つと必殺技は Q・E・R（D-53）。検証用の位置のリセットは Backspace。
    ///
    /// 照準（D-08）: 最後に使った機器がキーボードかマウスなら、マウスポインターの方向に照準する（PlayerInput の照準の方向）。
    /// ゲームパッドなら、右スティックを倒しているときはその方向に照準する。倒していなければ照準の方向を指定せず、
    /// シミュレーションが左スティックの方向で照準する。
    /// ローカル対戦（docs/15 T6-5）では、人ごとに機器を限った入力のアセット（InputActionAsset.devices）を渡す。
    /// オプションで変えたキーの割り当て（BindingSettings。docs/15 T6-9）は、作るときにアセットへ当てる。
    /// メーデー（JUGGERNAUT の R）で場所を選んでいる間は、照準の代わりに選ぶ場所を入れる（UpdateTargeting。JuggernautRules.EncodeTarget）。
    /// 場所は、マウスならポインターの位置、ゲームパッドならスティックで動かす大きなカーソル（ユーザーの案、2026-10-29）。
    /// </summary>
    public sealed class InputSystemAdapter : IDisposable
    {
        private const string MapName = "Player";

        /// <summary>ゲームパッドでメーデーの場所を選ぶカーソルの速さ（m/s。スティックをいっぱいに倒したとき）【仮】。</summary>
        public const float TargetCursorSpeed = 32f;

        private readonly InputActionMap _map;
        private readonly InputAction _move;
        private readonly InputAction _jump;
        private readonly InputAction _wire;
        private readonly InputAction _attack;
        private readonly InputAction _guard;
        private readonly InputAction _special1;
        private readonly InputAction _special2;
        private readonly InputAction _super;
        private readonly InputAction _reset;
        private readonly InputAction _point;
        private readonly InputAction _aim;
        private readonly InputAction[] _deviceActions;

        // 前のティックからの間に押されたボタン。ティックの間に押して離しても取りこぼさないようにする。
        private InputButtons _pressedSinceLastTick;

        // 離すまで無視するボタン（一時停止のメニューを閉じたときに押したままのもの。SuppressHeld）。
        private InputButtons _suppressed;

        private bool _hasPointer;
        private Vector2 _pointerWorld;
        private float _aimStickDeadzone;

        // メーデーで場所を選んでいる間のカーソルと、選べる範囲。
        private System.Numerics.Vector2 _cursor;
        private System.Numerics.Vector2 _targetCenter;
        private System.Numerics.Vector2 _targetHalf;

        public InputSystemAdapter(InputActionAsset actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }
            // オプションで変えたキーの割り当てを当てる（docs/15 T6-9）。
            BindingSettings.ApplySaved(actions);
            _map = actions.FindActionMap(MapName, throwIfNotFound: true);
            _move = _map.FindAction("Move", throwIfNotFound: true);
            _jump = _map.FindAction("Jump", throwIfNotFound: true);
            _wire = _map.FindAction("Wire", throwIfNotFound: true);
            _attack = _map.FindAction("Attack", throwIfNotFound: true);
            _guard = _map.FindAction("Guard", throwIfNotFound: true);
            _special1 = _map.FindAction("Special1", throwIfNotFound: true);
            _special2 = _map.FindAction("Special2", throwIfNotFound: true);
            _super = _map.FindAction("Super", throwIfNotFound: true);
            _reset = _map.FindAction("Reset", throwIfNotFound: true);
            _point = _map.FindAction("Point", throwIfNotFound: true);
            _aim = _map.FindAction("Aim", throwIfNotFound: true);
            _deviceActions = new[] { _move, _jump, _wire, _attack, _guard, _special1, _special2, _super, _reset, _aim };
            UsingPointer = HasMouse(actions);
        }

        // 使える機器にマウスがあるか（ローカル対戦では、人ごとに機器を限った入力を使う。ゲームパッドだけの人はポインターで照準しない）。
        private static bool HasMouse(InputActionAsset actions)
        {
            if (!actions.devices.HasValue)
            {
                return Mouse.current != null;
            }
            foreach (InputDevice device in actions.devices.Value)
            {
                if (device is Mouse)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>このフレームで位置のリセットが押されたか（Poll で更新する）。</summary>
        public bool ResetRequested { get; private set; }

        /// <summary>マウスポインターで照準しているか（最後に使った機器がキーボードかマウス）。</summary>
        public bool UsingPointer { get; private set; }

        /// <summary>メーデー（JUGGERNAUT の R）で場所を選んでいるか（UpdateTargeting で決める）。</summary>
        public bool Targeting { get; private set; }

        /// <summary>メーデーで選んでいる場所（ゲームプレイ平面の m。Targeting の間だけ意味がある）。</summary>
        public System.Numerics.Vector2 TargetCursor => _cursor;

        /// <summary>
        /// 操作するキャラクターの状態 state を見て、メーデーで場所を選んでいる間は、入力の照準に場所を入れるようにする（毎フレーム、Poll の
        /// 前に呼ぶ）。選び始めたときのカーソルは、吊られた場所。bounds は選べる範囲を決めるステージの範囲。
        /// </summary>
        public void UpdateTargeting(in CharacterState state, CharacterConfig config, StageBounds? bounds)
        {
            bool targeting = config != null && state.Combat.MaydayPhase == JuggernautRules.MaydaySelect;
            if (targeting && !Targeting)
            {
                JuggernautRules.TargetArea(bounds, config.Skills.Juggernaut, out _targetCenter, out _targetHalf);
                _cursor = ClampToArea(state.Combat.MaydayFrom);
            }
            Targeting = targeting;
        }

        public void Enable()
        {
            _map.Enable();
        }

        public void Disable()
        {
            _map.Disable();
        }

        /// <summary>
        /// 毎フレーム、ティックを進める前に呼ぶ。camera はマウスポインターの位置をゲームプレイ平面（Z = 0）に写すのに使う。
        /// aimStickDeadzone より小さい右スティックの入力は無視する（WireTuning.AimDeadzone を渡す）。
        /// </summary>
        public void Poll(Camera camera, float aimStickDeadzone)
        {
            _aimStickDeadzone = aimStickDeadzone;
            if (_jump.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Jump;
            }
            if (_wire.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Wire;
            }
            if (_attack.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Attack;
            }
            if (_guard.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Guard;
            }
            if (_special1.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Special1;
            }
            if (_special2.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Special2;
            }
            if (_super.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Super;
            }
            ResetRequested = _reset.WasPressedThisFrame();
            // 離したボタンは、もう無視しない。
            _suppressed &= Held();
            _pressedSinceLastTick &= ~_suppressed;

            UpdateActiveDevice();
            _hasPointer = UsingPointer && camera != null
                && TryProjectToGameplayPlane(camera, _point.ReadValue<Vector2>(), out _pointerWorld);
            if (Targeting)
            {
                MoveCursor();
            }
        }

        // メーデーのカーソルを動かす: マウスならポインターの位置、ゲームパッドなら右スティック（倒していなければ左スティック）の向きへ。
        private void MoveCursor()
        {
            if (_hasPointer)
            {
                _cursor = ClampToArea(new System.Numerics.Vector2(_pointerWorld.x, _pointerWorld.y));
                return;
            }
            Vector2 stick = _aim.ReadValue<Vector2>();
            if (stick.magnitude < _aimStickDeadzone)
            {
                stick = _move.ReadValue<Vector2>();
            }
            if (stick.sqrMagnitude > 1f)
            {
                stick.Normalize();
            }
            float step = TargetCursorSpeed * Time.unscaledDeltaTime;
            _cursor = ClampToArea(_cursor + new System.Numerics.Vector2(stick.x, stick.y) * step);
        }

        private System.Numerics.Vector2 ClampToArea(System.Numerics.Vector2 point)
        {
            System.Numerics.Vector2 min = _targetCenter - _targetHalf;
            System.Numerics.Vector2 max = _targetCenter + _targetHalf;
            return System.Numerics.Vector2.Clamp(point, min, max);
        }

        /// <summary>
        /// 1ティック分の入力を作る。前のティックから押されたボタンは、離していても押している扱いにする。
        /// position は、照準の方向を求めるためのキャラクターの位置。
        /// </summary>
        public SimInput ConsumeTick(System.Numerics.Vector3 position)
        {
            SimInput input = Build(position, _pressedSinceLastTick);
            _pressedSinceLastTick = InputButtons.None;
            return input;
        }

        /// <summary>
        /// 今押しているボタンを、離すまで無視する（一時停止のメニューを閉じたとき。決定に使ったボタンでキャラクターが動かないように）。
        /// それまでに押したボタンの記録も捨てる。
        /// </summary>
        public void SuppressHeld()
        {
            _suppressed = Held();
            _pressedSinceLastTick = InputButtons.None;
            ResetRequested = false;
        }

        /// <summary>今の入力を、記録を消費せずに作る（照準のインジケーターの表示用）。</summary>
        public SimInput Peek(System.Numerics.Vector3 position)
        {
            return Build(position, InputButtons.None);
        }

        public void Dispose()
        {
            _map.Disable();
        }

        /// <summary>画面上の位置を、ゲームプレイ平面（Z = 0）上の点に写す。</summary>
        public static bool TryProjectToGameplayPlane(Camera camera, Vector2 screenPosition, out Vector2 world)
        {
            Ray ray = camera.ScreenPointToRay(screenPosition);
            world = default;
            if (Mathf.Abs(ray.direction.z) < 1e-6f)
            {
                return false;
            }
            float distance = -ray.origin.z / ray.direction.z;
            if (distance < 0f)
            {
                return false;
            }
            Vector3 point = ray.origin + ray.direction * distance;
            world = new Vector2(point.x, point.y);
            return true;
        }

        // 今押しているボタン。
        private InputButtons Held()
        {
            InputButtons buttons = InputButtons.None;
            if (_jump.IsPressed())
            {
                buttons |= InputButtons.Jump;
            }
            if (_wire.IsPressed())
            {
                buttons |= InputButtons.Wire;
            }
            if (_attack.IsPressed())
            {
                buttons |= InputButtons.Attack;
            }
            if (_guard.IsPressed())
            {
                buttons |= InputButtons.Guard;
            }
            if (_special1.IsPressed())
            {
                buttons |= InputButtons.Special1;
            }
            if (_special2.IsPressed())
            {
                buttons |= InputButtons.Special2;
            }
            if (_super.IsPressed())
            {
                buttons |= InputButtons.Super;
            }
            return buttons;
        }

        private SimInput Build(System.Numerics.Vector3 position, InputButtons latched)
        {
            InputButtons buttons = (latched | Held()) & ~_suppressed;

            Vector2 move = _move.ReadValue<Vector2>();
            SimInput input = SimInput.FromAxes(move.x, move.y, buttons);
            if (Targeting)
            {
                // メーデーで場所を選んでいる: 照準に場所を入れる（スティックはカーソルを動かすのに使い、移動には使わない）。
                return JuggernautRules.EncodeTarget(SimInput.FromAxes(0f, 0f, buttons), _cursor, _targetCenter, _targetHalf);
            }
            if (_hasPointer)
            {
                return input.WithAim(new System.Numerics.Vector2(_pointerWorld.x - position.X, _pointerWorld.y - position.Y));
            }
            if (!UsingPointer)
            {
                Vector2 aimStick = _aim.ReadValue<Vector2>();
                if (aimStick.magnitude >= _aimStickDeadzone && aimStick.sqrMagnitude > 0f)
                {
                    return input.WithAim(new System.Numerics.Vector2(aimStick.x, aimStick.y));
                }
            }
            return input;
        }

        private void UpdateActiveDevice()
        {
            if (_point.WasPerformedThisFrame() && _point.activeControl != null)
            {
                UsingPointer = true;
            }
            foreach (InputAction action in _deviceActions)
            {
                if (action.WasPerformedThisFrame() && action.activeControl != null)
                {
                    UsingPointer = !(action.activeControl.device is Gamepad);
                }
            }
        }
    }
}
