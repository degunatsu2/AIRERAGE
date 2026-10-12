using System;
using System.Collections.Generic;
using Degunatsu.Core.Common;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
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
        [SerializeField] private CharacterView _characterView;
        [SerializeField] private WireView _wireView;

        private readonly SimInput[] _inputs = new SimInput[1];
        private SimWorld _world;
        private InputSystemAdapter _input;
        private int _player;
        private CharacterState _previous;
        private float _accumulator;

        /// <summary>操作するキャラクターの1ティックが終わるたびに、その状態を通知する（計測などのデバッグ機能用）。</summary>
        public event Action<CharacterState> PlayerTicked;

        /// <summary>操作するキャラクターの位置をリセットしたときに通知する。</summary>
        public event Action PlayerReset;

        /// <summary>操作するキャラクターの調整値（読み取り用。値はシミュレーションと共有している）。</summary>
        public CharacterConfig PlayerConfig => _character.Config;

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
            _input.Poll();
            if (_input.ResetRequested)
            {
                ResetPlayer();
            }

            _accumulator += Time.deltaTime;
            int ticks = 0;
            while (_accumulator >= SimTime.DeltaTime && ticks < MaxTicksPerFrame)
            {
                _previous = _world.GetState(_player);
                _inputs[0] = _input.ConsumeTick();
                _world.Step(_inputs);
                _accumulator -= SimTime.DeltaTime;
                ticks++;
                PlayerTicked?.Invoke(_world.GetState(_player));
            }
            if (ticks == MaxTicksPerFrame && _accumulator >= SimTime.DeltaTime)
            {
                // 追いつけない分は捨てる（ゲームの時間が実時間より遅れる）。
                _accumulator = 0f;
            }

            Render(_accumulator / SimTime.DeltaTime);
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
                _wireView.Render(_previous, current, alpha);
            }
        }

        private void ResetPlayer()
        {
            CharacterState state = CharacterState.Spawn(_spawnPoint.position.ToNumerics(), 1);
            _world.SetState(_player, state);
            _previous = state;
            PlayerReset?.Invoke();
        }
    }
}
