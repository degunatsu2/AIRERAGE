using System;
using System.Collections.Generic;
using Degunatsu.Core.Combat;
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
    /// プロトタイプでは、操作するキャラクター1人と、攻撃を当てて試すためのダミー（入力なしでその場に立つ。
    /// docs/11_combat_plan.md T3-2）を動かす。位置のリセット（R）でダミーも出現位置に戻す。
    /// シーンにステージの範囲（KnockoutBounds）があれば、外に出たキャラクターは撃破されて出現位置に戻る（T3-3）。
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        // 1フレームで進める最大のティック数。処理が遅れたときに追いつこうとして、さらに遅れるのを防ぐ（技術的な上限）。
        private const int MaxTicksPerFrame = 5;

        // ダミーに攻撃させるときの間隔（ティック。デバッグ用。ガードを試すため）。
        public const int DummyAttackIntervalTicks = 90;

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
        [Tooltip("ダミーの出現位置（攻撃を当てて試す相手。入力なし）")]
        [SerializeField] private Transform[] _dummySpawns = new Transform[0];
        [Tooltip("ダミーの表示（出現位置と同じ数・順番）")]
        [SerializeField] private CharacterView[] _dummyViews = new CharacterView[0];

        private SimInput[] _inputs = new SimInput[1];
        private CharacterState[] _previousDummies = new CharacterState[0];
        private SimWorld _world;
        private BoxTerrain _terrain;
        private InputSystemAdapter _input;
        private int _player;
        private CharacterState _previous;
        private float _accumulator;
        private bool _dummiesAttack;
        private int _dummyAttackTicks;

        /// <summary>操作するキャラクターの1ティックが終わるたびに、その状態を通知する（計測などのデバッグ機能用）。</summary>
        public event Action<CharacterState> PlayerTicked;

        /// <summary>操作するキャラクターの位置をリセットしたときに通知する。</summary>
        public event Action PlayerReset;

        /// <summary>1ティック進めるたびに、全員を進めた後に通知する（効果音など、状態の変化を見る表示用）。</summary>
        public event Action Ticked;

        /// <summary>操作するキャラクターの調整値（値はシミュレーションと共有している）。</summary>
        public CharacterConfig PlayerConfig => _character.Config;

        /// <summary>操作するキャラクターの今の状態。</summary>
        public CharacterState PlayerState => _world.GetState(_player);

        /// <summary>最後のティックで使った入力。</summary>
        public SimInput LastInput { get; private set; }

        /// <summary>操作するキャラクターの表示。</summary>
        public CharacterView PlayerView => _characterView;

        /// <summary>ダミーの数。</summary>
        public int DummyCount => _previousDummies.Length;

        /// <summary>ダミー index の今の状態。</summary>
        public CharacterState GetDummyState(int index)
        {
            return _world.GetState(_player + 1 + index);
        }

        /// <summary>これまでに進めたティック数。</summary>
        public int CurrentTick => _world.Tick;

        /// <summary>ステージの範囲があるか（外に出ると撃破になるか。T3-3）。</summary>
        public bool HasStageBounds => _world.Bounds.HasValue;

        /// <summary>操作するキャラクターが最後に当てた命中（なければ null。デバッグ表示用）。</summary>
        public HitRecord? LastPlayerHit { get; private set; }

        /// <summary>
        /// ダミーを向いている方向へ歩かせるか（デバッグ用。F7）。ダミーを足場から落として、撃破の手柄を確かめるため。
        /// 位置のリセット、入力の記録と再生の始めに止める。
        /// </summary>
        public bool DummiesWalk { get; set; }

        /// <summary>
        /// ダミーに、操作するキャラクターの方を向いて DummyAttackIntervalTicks ごとに攻撃させるか（デバッグ用。F8。ガードを試すため）。
        /// 有効にしたティックに最初の攻撃を押す。位置のリセット、入力の記録と再生の始めに止める。
        /// </summary>
        public bool DummiesAttack
        {
            get => _dummiesAttack;
            set
            {
                _dummiesAttack = value;
                _dummyAttackTicks = 0;
            }
        }

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
            StageBounds? bounds = KnockoutBounds.Find(errors);
            foreach (string error in errors)
            {
                Debug.LogError(error, this);
            }

            _terrain = terrain;
            _world = new SimWorld(terrain, bounds);
            _player = _world.AddCharacter(_character.Config, _spawnPoint.position.ToNumerics());
            _previous = _world.GetState(_player);
            if (_dummyViews.Length != _dummySpawns.Length)
            {
                Debug.LogError($"{name}: ダミーの出現位置（{_dummySpawns.Length}）と表示（{_dummyViews.Length}）の数が違います", this);
            }
            int dummies = Mathf.Min(_dummySpawns.Length, _dummyViews.Length);
            _previousDummies = new CharacterState[dummies];
            for (int i = 0; i < dummies; i++)
            {
                _world.AddCharacter(_character.Config, _dummySpawns[i].position.ToNumerics(), -1);
                _previousDummies[i] = GetDummyState(i);
            }
            _inputs = new SimInput[1 + dummies];
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
            for (int i = 0; i < _previousDummies.Length; i++)
            {
                _previousDummies[i] = GetDummyState(i);
            }
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
            bool dummyAttack = _dummiesAttack && _dummyAttackTicks++ % DummyAttackIntervalTicks == 0;
            for (int i = 0; i < _previousDummies.Length; i++)
            {
                _inputs[1 + i] = DummyInput(_previousDummies[i], dummyAttack);
            }
            _world.Step(_inputs);
            foreach (HitRecord hit in _world.LastHits)
            {
                if (hit.Attacker == _player)
                {
                    LastPlayerHit = hit;
                }
            }
            PlayerTicked?.Invoke(_world.GetState(_player));
            Ticked?.Invoke();
        }

        private void Render(float alpha)
        {
            CharacterState current = _world.GetState(_player);
            if (_characterView != null)
            {
                _characterView.Render(_previous, current, alpha, _character.Config.Movement.HalfExtents.ToUnity());
                _characterView.RenderAction(current, _character.Config.Combat);
            }
            for (int i = 0; i < _previousDummies.Length; i++)
            {
                CharacterState dummy = GetDummyState(i);
                _dummyViews[i].Render(_previousDummies[i], dummy, alpha, _character.Config.Movement.HalfExtents.ToUnity());
                _dummyViews[i].RenderAction(dummy, _character.Config.Combat);
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

        // ダミーの入力（デバッグ用）。攻撃するティックは操作するキャラクターの方へ向いて押し、それ以外は歩くか止まる。
        private SimInput DummyInput(in CharacterState dummy, bool attack)
        {
            if (attack)
            {
                float toPlayer = _previous.Position.X >= dummy.Position.X ? 1f : -1f;
                return SimInput.FromAxes(toPlayer, 0f, InputButtons.Attack);
            }
            return DummiesWalk ? SimInput.FromAxes(dummy.Facing, 0f, InputButtons.None) : default;
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

        /// <summary>
        /// ダミーを出現位置に戻す。入力の記録と再生の始めにも呼ぶ（ダミーに当てた記録を、同じ条件で再生するため。
        /// ただし、ダミーのいない照合のテスト（RecordedReplayTests）とは一致しない）。
        /// </summary>
        public void ResetDummies()
        {
            DummiesWalk = false;
            DummiesAttack = false;
            for (int i = 0; i < _previousDummies.Length; i++)
            {
                _world.SetState(_player + 1 + i, CharacterState.Spawn(_dummySpawns[i].position.ToNumerics(), -1));
                _previousDummies[i] = GetDummyState(i);
            }
        }

        private void ResetPlayer()
        {
            ResetDummies();
            SetPlayerState(CharacterState.Spawn(_spawnPoint.position.ToNumerics(), 1));
        }
    }
}
