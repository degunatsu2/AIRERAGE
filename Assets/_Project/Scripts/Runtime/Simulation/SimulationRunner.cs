using System;
using System.Collections.Generic;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Data;
using Degunatsu.Runtime.Input;
using Degunatsu.Runtime.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.Runtime.Simulation
{
    /// <summary>
    /// シミュレーションを固定ティック（60Hz）で進め、表示を更新する（docs/03_architecture.md 4.1）。
    /// Update で経過時間をためて、1/60 秒ごとに SimWorld.Step を呼ぶ。表示は直前と現在のティックの状態を補間する。
    /// プロトタイプでは、操作するキャラクター1人を動かす。
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        // 1フレームで進める最大のティック数。処理が遅れたときに追いつこうとして、さらに遅れるのを防ぐ（技術的な上限）。
        private const int MaxTicksPerFrame = 5;

        [SerializeField] private CharacterConfigAsset _character;
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private Transform _spawnPoint;
        [Tooltip("地形として読み込むコライダーの層")]
        [SerializeField] private LayerMask _terrainMask = 1;
        [Tooltip("マウスポインターでの照準に使うカメラ")]
        [SerializeField] private Camera _camera;
        [SerializeField] private CharacterView _characterView;
        [SerializeField] private WireView _wireView;
        [SerializeField] private AimIndicatorView _aimIndicator;

        private readonly SimInput[] _inputs = new SimInput[1];
        private SimWorld _world;
        private BoxTerrain _terrain;
        private InputSystemAdapter _input;
        private int _player;
        private CharacterState _previous;
        private float _accumulator;

        /// <summary>操作するキャラクターの1ティックが終わるたびに、その状態を通知する（計測などのデバッグ機能用）。</summary>
        public event Action<CharacterState> PlayerTicked;

        /// <summary>操作するキャラクターの位置をリセットしたときに通知する。</summary>
        public event Action PlayerReset;

        /// <summary>操作するキャラクターの調整値（値はシミュレーションと共有している）。</summary>
        public CharacterConfig PlayerConfig => _character.Config;

        /// <summary>操作するキャラクターの今の状態。</summary>
        public CharacterState PlayerState => _world.GetState(_player);

        /// <summary>最後のティックで使った入力。</summary>
        public SimInput LastInput { get; private set; }

        /// <summary>これまでに進めたティック数。</summary>
        public int CurrentTick => _world.Tick;

        /// <summary>マウスポインターで照準しているか。</summary>
        public bool UsingPointer => _input != null && _input.UsingPointer;

        /// <summary>シミュレーションの速さの倍率（デバッグ用。1 = 等速、0 = 一時停止）。</summary>
        public float SpeedScale { get; set; } = 1f;

        /// <summary>
        /// 設定されている間は、プレイヤーの入力の代わりにこの入力を使う（入力の再生など、デバッグ用）。
        /// 入力が尽きたら null に戻る。プレイヤーの入力は読み捨てる（再生の後に、押したボタンが残らないように）。
        /// </summary>
        public ITickInputSource InputOverride { get; set; }

        /// <summary>1ティックだけ進める（デバッグ用。一時停止中のコマ送り）。</summary>
        public void StepOnce()
        {
            if (!enabled)
            {
                return;
            }
            StepTick();
            _accumulator = 0f;
        }

        private void Awake()
        {
            if (_character == null || _inputActions == null || _spawnPoint == null)
            {
                Debug.LogError($"{name}: CharacterConfigAsset、InputActionAsset、出現位置のどれかが設定されていません", this);
                enabled = false;
                return;
            }

            var errors = new List<string>();
            BoxTerrain terrain = SceneTerrainBuilder.Build(_terrainMask, errors);
            foreach (string error in errors)
            {
                Debug.LogError(error, this);
            }

            _terrain = terrain;
            _world = new SimWorld(terrain);
            _player = _world.AddCharacter(_character.Config, _spawnPoint.position.ToNumerics());
            _previous = _world.GetState(_player);
            _input = new InputSystemAdapter(_inputActions);
        }

        private void OnEnable()
        {
            _input?.Enable();
        }

        private void OnDisable()
        {
            _input?.Disable();
        }

        private void OnDestroy()
        {
            _input?.Dispose();
        }

        private void Update()
        {
            _input.Poll(_camera, _character.Config.Wire.AimDeadzone);
            if (_input.ResetRequested)
            {
                ResetPlayer();
            }

            _accumulator += Time.deltaTime * SpeedScale;
            int ticks = 0;
            while (_accumulator >= SimTime.DeltaTime && ticks < MaxTicksPerFrame)
            {
                StepTick();
                _accumulator -= SimTime.DeltaTime;
                ticks++;
            }
            if (ticks == MaxTicksPerFrame && _accumulator >= SimTime.DeltaTime)
            {
                // 追いつけない分は捨てる（ゲームの時間が実時間より遅れる）。
                _accumulator = 0f;
            }

            // 一時停止中は補間せず、今のティックの状態を表示する（コマ送りの結果がすぐ見えるように）。
            Render(SpeedScale > 0f ? _accumulator / SimTime.DeltaTime : 1f);
        }

        private void StepTick()
        {
            _previous = _world.GetState(_player);
            SimInput input = _input.ConsumeTick(_previous.Position);
            if (InputOverride != null)
            {
                if (InputOverride.TryGetNext(out SimInput overridden))
                {
                    input = overridden;
                }
                else
                {
                    InputOverride = null;
                }
            }
            _inputs[0] = input;
            LastInput = input;
            _world.Step(_inputs);
            PlayerTicked?.Invoke(_world.GetState(_player));
        }

        private void Render(float alpha)
        {
            CharacterState current = _world.GetState(_player);
            if (_characterView != null)
            {
                _characterView.Render(_previous, current, alpha, _character.Config.Movement.HalfExtents.ToUnity());
            }
            if (_wireView != null)
            {
                _wireView.Render(_previous, current, alpha, _character.Config.Wire);
            }
            if (_aimIndicator != null)
            {
                // 表示の位置（補間した位置）から、今の入力で射出したときの結果を予測して示す。
                // 入力を差し替えている間（再生中）は、プレイヤーの今の入力ではなく、最後のティックで使った入力で示す。
                Vector3 origin = Vector3.Lerp(_previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
                System.Numerics.Vector3 simOrigin = origin.ToNumerics();
                SimInput shown = InputOverride != null ? LastInput : _input.Peek(simOrigin);
                var frame = new InputFrame(shown, current.PreviousButtons);
                WireTuning wire = _character.Config.Wire;
                WireShotPreview preview = WireTargeting.PredictShot(simOrigin, frame.Aim, current.Facing, wire, _terrain);
                _aimIndicator.Render(origin, current.Wire, preview, wire.MaxLength);
            }
        }

        /// <summary>
        /// 操作するキャラクターの状態を置き換える（入力の再生の開始など、デバッグ用）。
        /// 入力以外で状態が変わったことを PlayerReset で知らせる。
        /// </summary>
        public void SetPlayerState(CharacterState state)
        {
            _world.SetState(_player, state);
            _previous = state;
            PlayerReset?.Invoke();
        }

        private void ResetPlayer()
        {
            SetPlayerState(CharacterState.Spawn(_spawnPoint.position.ToNumerics(), 1));
        }
    }
}
