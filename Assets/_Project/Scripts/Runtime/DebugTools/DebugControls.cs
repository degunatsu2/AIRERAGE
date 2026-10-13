using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Degunatsu.Runtime.DebugTools
{
    /// <summary>
    /// デバッグ用の操作（docs/06_prototype_plan.md 3章）。DegunatsuControls.inputactions の "Debug" マップを読む。
    /// - ToggleWireType（Tab / Select）: ワイヤーのタイプ A / B を切り替える（調整値のアセットの値を変える）
    /// - CycleSpeed（F1 / 十字キー下）: シミュレーションの速さを 1 → 1/2 → 1/4 → 一時停止 → 1 の順に変える
    /// - StepTick（F2 / 十字キー右）: 一時停止中に1ティックだけ進める
    /// - ToggleDummyWalk（F7）: ダミーを向いている方向へ歩かせる／止める（足場から落として撃破の手柄を確かめる）
    /// 同じマップの SaveRecording（F5）と PlayRecording（F6）は ReplayControls が読む。
    /// </summary>
    public sealed class DebugControls : MonoBehaviour
    {
        private const string MapName = "Debug";

        // 速さの切り替えの順番（0 = 一時停止）。
        private static readonly float[] SpeedSteps = { 1f, 0.5f, 0.25f, 0f };

        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private InputActionAsset _inputActions;

        private InputActionMap _map;
        private InputAction _toggleWireType;
        private InputAction _cycleSpeed;
        private InputAction _stepTick;
        private InputAction _toggleDummyWalk;
        private int _speedIndex;

        private void Awake()
        {
            if (_runner == null || _inputActions == null)
            {
                Debug.LogError($"{name}: SimulationRunner か InputActionAsset が設定されていません", this);
                enabled = false;
                return;
            }
            _map = _inputActions.FindActionMap(MapName, throwIfNotFound: true);
            _toggleWireType = _map.FindAction("ToggleWireType", throwIfNotFound: true);
            _cycleSpeed = _map.FindAction("CycleSpeed", throwIfNotFound: true);
            _stepTick = _map.FindAction("StepTick", throwIfNotFound: true);
            _toggleDummyWalk = _map.FindAction("ToggleDummyWalk", throwIfNotFound: true);
        }

        private void OnEnable()
        {
            _map?.Enable();
        }

        private void OnDisable()
        {
            _map?.Disable();
        }

        private void Update()
        {
            if (_toggleWireType.WasPressedThisFrame())
            {
                ToggleWireType();
            }
            if (_cycleSpeed.WasPressedThisFrame())
            {
                CycleSpeed();
            }
            if (_stepTick.WasPressedThisFrame() && _runner.SpeedScale <= 0f)
            {
                _runner.StepOnce();
            }
            if (_toggleDummyWalk.WasPressedThisFrame())
            {
                _runner.DummiesWalk = !_runner.DummiesWalk;
            }
        }

        /// <summary>ワイヤーのタイプを次のタイプに切り替える（WireTypeLabels の順番）。</summary>
        public void ToggleWireType()
        {
            WireTuning wire = _runner.PlayerConfig.Wire;
            wire.ModelType = WireTypeLabels.Next(wire.ModelType);
        }

        /// <summary>シミュレーションの速さを次の段階にする。</summary>
        public void CycleSpeed()
        {
            _speedIndex = (_speedIndex + 1) % SpeedSteps.Length;
            _runner.SpeedScale = SpeedSteps[_speedIndex];
        }
    }
}
