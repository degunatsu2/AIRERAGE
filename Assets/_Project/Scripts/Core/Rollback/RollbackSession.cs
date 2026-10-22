using System;
using System.Collections.Generic;
using Airerage.Core.Input;
using Airerage.Core.Simulation;

namespace Airerage.Core.Rollback
{
    /// <summary>
    /// 決定論的ロールバックの1人分のセッション（docs/10_online_plan.md T2-2、docs/04_network.md 3.1）。
    /// プレイヤーごとの入力を管理し、届いていない相手の入力は「直前の入力を続ける」と予測してティックを進める。
    /// 予測と違う入力が届いたら、そのティックの状態に戻して、正しい入力で今のティックまで計算し直す。
    /// 通信はしない（送受信は呼び出し側が行い、届いた入力を AddRemoteInput で渡す）。
    /// プレイヤー p はワールドのキャラクター p を操作する。
    /// </summary>
    public sealed class RollbackSession
    {
        private readonly SimWorld _world;
        private readonly RollbackSettings _settings;
        private readonly int _localPlayer;
        private readonly int _firstInputTick;

        // プレイヤーごとの、届いた（確定した）入力。ティック → 入力。
        private readonly Dictionary<int, PlayerInput>[] _inputs;

        // プレイヤーごとの、そのティックまでの入力がすべて届いている最大のティック。
        private readonly int[] _contiguous;

        // プレイヤーごとの、まだ捨てていない最も古い入力のティック。
        private readonly int[] _oldestKept;

        // ティックごとの、計算する前の状態と、計算に使った入力（予測を含む）。tick % 長さ の位置に置く。
        private readonly Frame[] _frames;

        private readonly PlayerInput[] _stepInputs;
        private int _pendingRollbackTick = int.MaxValue;

        // プレイヤーごとの、試合から抜けたティック（その後の入力は PlayerInput.Departed で確定。抜けていなければ int.MaxValue）。
        private readonly int[] _leftFrom;

        // 抜けた人の、確定とみなすティック（ConfirmedTick などの最小を求める計算で、その人が足を引っ張らないように）。
        private const int LeftContiguous = int.MaxValue - 1;

        /// <param name="world">キャラクターをプレイヤーの数だけ追加したワールド。今のティックから始める。</param>
        public RollbackSession(SimWorld world, int localPlayer, RollbackSettings settings)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            settings.Validate();
            int players = world.CharacterCount;
            if (players < 1)
            {
                throw new ArgumentException("ワールドにキャラクターがいません", nameof(world));
            }
            if (localPlayer < 0 || localPlayer >= players)
            {
                throw new ArgumentOutOfRangeException(nameof(localPlayer), localPlayer, $"プレイヤーの番号は 0〜{players - 1}");
            }

            _world = world;
            _settings = settings;
            _localPlayer = localPlayer;
            _inputs = new Dictionary<int, PlayerInput>[players];
            _contiguous = new int[players];
            _oldestKept = new int[players];
            _stepInputs = new PlayerInput[players];
            _leftFrom = new int[players];

            // 入力遅延の分の最初のティックは、誰も入力していない（全員の空の入力が確定している）。
            int start = world.Tick;
            _firstInputTick = start + settings.InputDelayTicks;
            for (int p = 0; p < players; p++)
            {
                _inputs[p] = new Dictionary<int, PlayerInput>();
                for (int tick = start; tick < _firstInputTick; tick++)
                {
                    _inputs[p][tick] = default;
                }
                _contiguous[p] = _firstInputTick - 1;
                _oldestKept[p] = start;
                _leftFrom[p] = int.MaxValue;
            }

            _frames = new Frame[settings.MaxPredictionTicks + 2];
            for (int i = 0; i < _frames.Length; i++)
            {
                _frames[i] = new Frame(players);
            }
        }

        public int PlayerCount => _inputs.Length;

        public int LocalPlayer => _localPlayer;

        /// <summary>次に計算するティック（ワールドのティック数）。</summary>
        public int CurrentTick => _world.Tick;

        /// <summary>次の自分の入力を使うティック（今のティック＋入力遅延）。</summary>
        public int LocalInputTick => CurrentTick + _settings.InputDelayTicks;

        /// <summary>最初に入力を使うティック（それより前は全員の空の入力で確定している）。</summary>
        public int FirstInputTick => _firstInputTick;

        /// <summary>全員の入力が、そのティックまですべて届いている最大のティック（確定ティック）。</summary>
        public int ConfirmedTick
        {
            get
            {
                int confirmed = int.MaxValue;
                foreach (int tick in _contiguous)
                {
                    confirmed = Math.Min(confirmed, tick);
                }
                return confirmed;
            }
        }

        /// <summary>プレイヤー player の入力が、そのティックまですべて届いている最大のティック（確認応答に使う）。</summary>
        public int ContiguousTick(int player)
        {
            return _contiguous[player];
        }

        /// <summary>
        /// 今のティックを進めてよいか（予測で進めるティックが上限を超えないか）。false なら入力が届くまで待つ。
        /// AdvanceFrame で渡す自分の入力（LocalInputTick の分）は、届いているものとして数える。
        /// </summary>
        public bool CanAdvance
        {
            get
            {
                int confirmed = int.MaxValue;
                for (int p = 0; p < PlayerCount; p++)
                {
                    int tick = p == _localPlayer ? Math.Max(_contiguous[p], LocalInputTick) : _contiguous[p];
                    confirmed = Math.Min(confirmed, tick);
                }
                return CurrentTick - confirmed <= _settings.MaxPredictionTicks;
            }
        }

        /// <summary>プレイヤー player が試合から抜けたか（RemovePlayer）。</summary>
        public bool HasLeft(int player)
        {
            return _leftFrom[player] != int.MaxValue;
        }

        /// <summary>
        /// プレイヤー player が試合から抜けた（ネットワーク対戦で切れた。docs/15 T6-10）。fromTick からのその人の入力は、すべて
        /// PlayerInput.Departed で確定とする（全員が同じ fromTick を使えば、結果はそろう）。fromTick より前のその人の入力は、すべて
        /// 届いている必要がある（先に AddRemoteInput で渡す。足りなければ InvalidOperationException）。fromTick 以降を予測で計算して
        /// いれば、次の AdvanceFrame か Reconcile で巻き戻す。
        /// </summary>
        public void RemovePlayer(int player, int fromTick)
        {
            if (player == _localPlayer || player < 0 || player >= PlayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(player), player, $"抜けた人の番号は 0〜{PlayerCount - 1}（自分の {_localPlayer} を除く）");
            }
            if (HasLeft(player))
            {
                if (_leftFrom[player] != fromTick)
                {
                    throw new InvalidOperationException(
                        $"プレイヤー {player} はティック {_leftFrom[player]} で抜けています（今回の {fromTick} と違う）");
                }
                return;
            }
            if (fromTick < _firstInputTick)
            {
                throw new ArgumentOutOfRangeException(nameof(fromTick), fromTick, $"抜けるティックは {_firstInputTick} から");
            }
            if (_contiguous[player] < fromTick - 1)
            {
                throw new InvalidOperationException(
                    $"プレイヤー {player} のティック {_contiguous[player] + 1}〜{fromTick - 1} の入力が届いていないので、ティック {fromTick} で抜けたことにできません");
            }
            // 抜けた後のティックに届いていた入力は使わない（ホストが見た入力より後のものは、だれにも届いていないはず）。
            var later = new List<int>();
            foreach (int tick in _inputs[player].Keys)
            {
                if (tick >= fromTick)
                {
                    later.Add(tick);
                }
            }
            foreach (int tick in later)
            {
                _inputs[player].Remove(tick);
            }
            _leftFrom[player] = fromTick;
            _contiguous[player] = LeftContiguous;
            if (fromTick < CurrentTick)
            {
                _pendingRollbackTick = Math.Min(_pendingRollbackTick, fromTick);
            }
        }

        /// <summary>巻き戻した回数。</summary>
        public int RollbackCount { get; private set; }

        /// <summary>巻き戻して計算し直したティックの合計。</summary>
        public int ResimulatedTicks { get; private set; }

        /// <summary>1回の巻き戻しで計算し直した最大のティック数。</summary>
        public int LongestRollback { get; private set; }

        /// <summary>
        /// 自分の入力を渡して、1ティック進める。先に、届いた入力で予測が外れていたら巻き戻して計算し直す。
        /// 入力は LocalInputTick のティックに使う。CanAdvance が false のときは InvalidOperationException。
        /// </summary>
        public void AdvanceFrame(PlayerInput localInput)
        {
            if (!CanAdvance)
            {
                throw new InvalidOperationException(
                    $"予測の上限（{_settings.MaxPredictionTicks} ティック）に達しています。相手の入力が届くまで待ってください（今のティック {CurrentTick}、確定ティック {ConfirmedTick}）");
            }
            StoreInput(_localPlayer, LocalInputTick, localInput);
            Reconcile();
            SimulateTick();
            DiscardOldInputs();
        }

        /// <summary>
        /// 相手の入力を渡す。同じ入力が何度届いてもよく、順番が前後してもよい。
        /// すでに予測で計算したティックの入力が予測と違えば、次の AdvanceFrame か Reconcile で巻き戻す。
        /// 同じティックに前と違う入力が届いたら InvalidOperationException（通信の誤りか不正）。
        /// </summary>
        public void AddRemoteInput(int player, int tick, PlayerInput input)
        {
            if (player == _localPlayer)
            {
                throw new ArgumentException("自分の入力は AdvanceFrame で渡してください", nameof(player));
            }
            if (player < 0 || player >= PlayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(player), player, $"プレイヤーの番号は 0〜{PlayerCount - 1}");
            }
            if (tick < _firstInputTick)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), tick, $"入力のあるティックは {_firstInputTick} から");
            }
            if (tick <= _contiguous[player] && !_inputs[player].ContainsKey(tick))
            {
                // すでに確定し、古くなって捨てた入力の再送（抜けた人の、遅れて届いた入力も）。
                return;
            }
            if (!StoreInput(player, tick, input))
            {
                return;
            }
            if (tick < CurrentTick)
            {
                Frame frame = FrameAt(tick);
                if (!frame.Inputs[player].Equals(input))
                {
                    _pendingRollbackTick = Math.Min(_pendingRollbackTick, tick);
                }
            }
        }

        /// <summary>届いた入力で予測が外れていたら、そのティックまで巻き戻し、今のティックまで計算し直す。</summary>
        public void Reconcile()
        {
            if (_pendingRollbackTick == int.MaxValue)
            {
                return;
            }
            int from = _pendingRollbackTick;
            _pendingRollbackTick = int.MaxValue;
            int target = CurrentTick;
            if (from >= target)
            {
                return;
            }

            _world.LoadFrom(FrameAt(from).State);
            int length = target - from;
            RollbackCount++;
            ResimulatedTicks += length;
            LongestRollback = Math.Max(LongestRollback, length);
            while (CurrentTick < target)
            {
                SimulateTick();
            }
        }

        /// <summary>届いている入力を返す（送信や確認用）。古くなって捨てた入力は返せない。</summary>
        public bool TryGetInput(int player, int tick, out PlayerInput input)
        {
            return _inputs[player].TryGetValue(tick, out input);
        }

        /// <summary>
        /// ティック tick の状態のチェックサムを、それが確定していれば返す（不一致の検出用）。
        /// 確定している = そのティックより前の全員の入力が届き、巻き戻しも済んでいる。
        /// </summary>
        public bool TryGetConfirmedChecksum(int tick, out ulong checksum)
        {
            checksum = 0;
            if (tick > ConfirmedTick + 1 || tick > CurrentTick || _pendingRollbackTick < tick)
            {
                return false;
            }
            if (tick == CurrentTick)
            {
                checksum = _world.Checksum();
                return true;
            }
            Frame frame = _frames[Index(tick)];
            if (frame.Tick != tick)
            {
                return false;
            }
            checksum = frame.State.Checksum();
            return true;
        }

        private void SimulateTick()
        {
            int tick = CurrentTick;
            Frame frame = _frames[Index(tick)];
            _world.SaveTo(frame.State);
            frame.Tick = tick;
            for (int p = 0; p < PlayerCount; p++)
            {
                frame.Inputs[p] = InputFor(p, tick);
                _stepInputs[p] = frame.Inputs[p];
            }
            _world.Step(_stepInputs);
        }

        // 届いていればその入力、届いていなければ予測（届いている中で最も新しい、それより前のティックの入力を続ける。
        // まだ何も届いていなければ空の入力）。
        private PlayerInput InputFor(int player, int tick)
        {
            if (tick >= _leftFrom[player])
            {
                return PlayerInput.Departed;
            }
            Dictionary<int, PlayerInput> inputs = _inputs[player];
            if (inputs.TryGetValue(tick, out PlayerInput input))
            {
                return input;
            }
            for (int t = tick - 1; t >= _contiguous[player]; t--)
            {
                if (inputs.TryGetValue(t, out input))
                {
                    return input;
                }
            }
            if (_contiguous[player] < _firstInputTick)
            {
                return default;
            }
            throw new InvalidOperationException($"プレイヤー {player} のティック {tick} の入力を予測できません（内部の誤り）");
        }

        // 新しく加えたら true。同じ入力がすでにあれば false。違う入力があれば InvalidOperationException。
        private bool StoreInput(int player, int tick, PlayerInput input)
        {
            Dictionary<int, PlayerInput> inputs = _inputs[player];
            if (inputs.TryGetValue(tick, out PlayerInput existing))
            {
                if (!existing.Equals(input))
                {
                    throw new InvalidOperationException(
                        $"プレイヤー {player} のティック {tick} に、前と違う入力が届きました（前: {existing}、今回: {input}）");
                }
                return false;
            }
            inputs[tick] = input;
            while (inputs.ContainsKey(_contiguous[player] + 1))
            {
                _contiguous[player]++;
            }
            return true;
        }

        // 確定ティックより古く、計算も済んだティックの入力は、予測にも巻き戻しにも使わないので捨てる。
        // 相手の入力が先のティックまで届いていると、確定ティックが今のティックより先になる（遅れて始めた側）ので、今のティックでも制限する。
        private void DiscardOldInputs()
        {
            int discardBefore = Math.Min(ConfirmedTick, CurrentTick);
            for (int p = 0; p < PlayerCount; p++)
            {
                while (_oldestKept[p] < discardBefore)
                {
                    _inputs[p].Remove(_oldestKept[p]);
                    _oldestKept[p]++;
                }
            }
        }

        private Frame FrameAt(int tick)
        {
            Frame frame = _frames[Index(tick)];
            if (frame.Tick != tick)
            {
                throw new InvalidOperationException($"ティック {tick} の状態が残っていません（内部の誤り。予測の上限を超えて進んだ可能性）");
            }
            return frame;
        }

        private int Index(int tick)
        {
            return tick % _frames.Length;
        }

        private sealed class Frame
        {
            public Frame(int players)
            {
                Inputs = new PlayerInput[players];
            }

            public readonly WorldSnapshot State = new WorldSnapshot();
            public readonly PlayerInput[] Inputs;
            public int Tick = -1;
        }
    }
}
