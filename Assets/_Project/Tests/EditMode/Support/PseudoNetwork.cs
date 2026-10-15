using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Tests.Support
{
    /// <summary>
    /// ロールバックのセッションを、遅延・揺らぎ・欠落のある疑似ネットワークでつなぐテスト補助（docs/10_online_plan.md T2-3、T2-4）。
    /// 全員が同じ速さでフレームを進める。各プレイヤーは毎フレーム、相手ごとに InputExchange.BuildMessage で作ったメッセージを
    /// バイト列にして送り、届いたバイト列を InputExchange.Receive に渡す（製品のコードと同じ送受信の規則）。
    /// 乱数の種を固定し、毎回同じ結果になる。
    /// </summary>
    public sealed class PseudoNetwork
    {
        public static readonly Vector3[] Spawns =
        {
            new Vector3(-15f, 0.91f, 0f), new Vector3(-5f, 0.91f, 0f), new Vector3(4f, 0.91f, 0f), new Vector3(15f, 0.91f, 0f),
        };

        private readonly Random _random;
        private readonly Action<SimWorld> _setup;
        private readonly int _latency;
        private readonly int _jitter;
        private readonly double _loss;
        private readonly List<Packet> _inFlight = new List<Packet>();
        private readonly int[] _waitFrames;
        private readonly int[] _stalledByPeer;
        private readonly int[] _freezeRun;
        private readonly Vector3[] _beforeReconcile;
        private long _spreadSum;
        private int _spreadSamples;

        public PseudoNetwork(int players, RollbackSettings settings, int latencyFrames, int jitterFrames, double lossRate, int seed)
            : this(MakeScripts(players, seed), settings, latencyFrames, jitterFrames, lossRate, seed)
        {
        }

        /// <param name="scripts">プレイヤーごとの入力列（目標のティック＋入力遅延より長いこと）。</param>
        /// <param name="setup">作ったワールドの最初の状態を変える（全員のワールドと Reference のワールドに同じように。なくてもよい）。</param>
        public PseudoNetwork(PlayerInput[][] scripts, RollbackSettings settings, int latencyFrames, int jitterFrames, double lossRate, int seed,
            Action<SimWorld> setup = null)
        {
            int players = scripts.Length;
            _setup = setup;
            _random = new Random(seed);
            _latency = latencyFrames;
            _jitter = jitterFrames;
            _loss = lossRate;
            Settings = settings;
            Scripts = scripts;
            Normalized = new PlayerInput[players][];
            Sessions = new RollbackSession[players];
            Exchanges = new InputExchange[players];
            _waitFrames = new int[players];
            _stalledByPeer = new int[players];
            _freezeRun = new int[players];
            _beforeReconcile = new Vector3[players];
            Worlds = new SimWorld[players];
            for (int p = 0; p < players; p++)
            {
                Worlds[p] = MakeWorld(players);
                Sessions[p] = new RollbackSession(Worlds[p], p, settings);
                Exchanges[p] = new InputExchange(Sessions[p], settings);
                Normalized[p] = Normalize(Scripts[p], settings);
            }
        }

        public RollbackSettings Settings { get; }
        public PlayerInput[][] Scripts { get; }

        /// <summary>入力列を InputNormalizer で正規化したもの（実際にシミュレーションに使われる入力）。</summary>
        public PlayerInput[][] Normalized { get; }
        public RollbackSession[] Sessions { get; }
        public InputExchange[] Exchanges { get; }

        /// <summary>送ったメッセージの数と、その合計のバイト数（欠落したものを含む）。</summary>
        public int SentMessages { get; private set; }
        public long SentBytes { get; private set; }
        public SimWorld[] Worlds { get; }
        public int Frame { get; private set; }

        /// <summary>入力を待って進めなかったフレームの数（全員の合計）。</summary>
        public int StalledFrames { get; private set; }

        /// <summary>プレイヤー p が予測の上限で進めなかったフレームの数。</summary>
        public int StalledFramesOf(int p)
        {
            return _stalledByPeer[p];
        }

        /// <summary>時計が速いプレイヤー（-1 ならいない）。ExtraStepEvery フレームごとに、もう1ティック進もうとする。</summary>
        public int FastPeer { get; set; } = -1;

        public int ExtraStepEvery { get; set; }

        /// <summary>時刻の同期の勧め（InputExchange.RecommendWaitFrames）に従って待つか。</summary>
        public bool UseTimeSync { get; set; } = true;

        /// <summary>時刻の同期で待ったフレームの数（全員の合計）。</summary>
        public int TimeSyncWaitFrames { get; private set; }

        /// <summary>全員が進んでいる間の、最も進んだプレイヤーと最も遅れたプレイヤーのティックの差の平均。</summary>
        public double AverageTickSpread => _spreadSamples == 0 ? 0.0 : _spreadSum / (double)_spreadSamples;

        /// <summary>巻き戻しで、相手のキャラクターの今の位置が変わった回数（見ている人と見られているキャラクターの組ごと）。</summary>
        public int CorrectionCount { get; private set; }

        /// <summary>そのうち、VisibleCorrectionDistance 以上動いた（ワープに見える）回数。</summary>
        public int VisibleCorrectionCount { get; private set; }

        /// <summary>ワープに見えるとみなす位置の変化（m）。</summary>
        public float VisibleCorrectionDistance { get; set; } = 0.25f;

        /// <summary>巻き戻しで位置が変わった量の合計と最大（m）。</summary>
        public double CorrectionDistanceSum { get; private set; }
        public float LargestCorrection { get; private set; }

        /// <summary>
        /// 自分のティックが進まなかった（予測の上限で止まったか、時刻の同期で待った）フレームが続いた最長の長さ。
        /// 自分のキャラクターがその間止まって見える。
        /// </summary>
        public int LongestFreeze { get; private set; }

        /// <summary>同じティックについて、プレイヤーによって確定した状態のチェックサムが違った回数。</summary>
        public int ChecksumMismatches { get; private set; }

        /// <summary>確定した状態のチェックサムを比べたティックの数。</summary>
        public int ComparedTicks => _confirmed.Count;

        private readonly Dictionary<int, ulong> _confirmed = new Dictionary<int, ulong>();

        /// <summary>プレイヤー p がティック t に使う本当の入力（正規化した入力。入力遅延の分の最初のティックは空の入力）。</summary>
        public PlayerInput TrueInput(int p, int t)
        {
            return t < Settings.InputDelayTicks ? default : Normalized[p][t];
        }

        /// <summary>入力列を、プレイヤーが入力した順（入力遅延の後のティックから）に正規化する。</summary>
        private static PlayerInput[] Normalize(PlayerInput[] script, RollbackSettings settings)
        {
            var normalized = new PlayerInput[script.Length];
            PlayerInput previous = default;
            for (int t = settings.InputDelayTicks; t < script.Length; t++)
            {
                normalized[t] = InputNormalizer.Normalize(script[t], previous, settings.MoveHysteresis);
                previous = normalized[t];
            }
            return normalized;
        }

        /// <summary>全員がティック targetTick まで進み、そこまでの入力がすべて確定するまでフレームを進める。</summary>
        public void RunUntil(int targetTick, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (AllDone(targetTick))
                {
                    foreach (RollbackSession session in Sessions)
                    {
                        session.Reconcile();
                    }
                    CompareConfirmedChecksums();
                    return;
                }
                Step(targetTick);
            }
            throw new InvalidOperationException($"{maxFrames} フレーム以内に全員がティック {targetTick} で確定しませんでした");
        }

        /// <summary>本当の入力だけで計算したワールド（比べる基準）。</summary>
        public SimWorld Reference(int ticks)
        {
            SimWorld world = MakeWorld(Sessions.Length);
            var inputs = new PlayerInput[Sessions.Length];
            for (int t = 0; t < ticks; t++)
            {
                for (int p = 0; p < inputs.Length; p++)
                {
                    inputs[p] = TrueInput(p, t);
                }
                world.Step(inputs);
            }
            return world;
        }

        private void Step(int targetTick)
        {
            Deliver();
            for (int p = 0; p < Sessions.Length; p++)
            {
                RollbackSession session = Sessions[p];
                ReconcileAndMeasure(p);
                int tickBefore = session.CurrentTick;
                if (session.CurrentTick >= targetTick)
                {
                    session.Reconcile();
                    continue;
                }
                if (_waitFrames[p] > 0)
                {
                    _waitFrames[p]--;
                    TimeSyncWaitFrames++;
                    MeasureFreeze(p, false);
                    continue;
                }
                int steps = p == FastPeer && ExtraStepEvery > 0 && Frame % ExtraStepEvery == 0 ? 2 : 1;
                for (int i = 0; i < steps && session.CurrentTick < targetTick; i++)
                {
                    if (!session.CanAdvance)
                    {
                        StalledFrames++;
                        _stalledByPeer[p]++;
                        session.Reconcile();
                        break;
                    }
                    Exchanges[p].AdvanceFrame(Scripts[p][session.LocalInputTick]);
                }
                if (UseTimeSync)
                {
                    _waitFrames[p] += Exchanges[p].RecommendWaitFrames();
                }
                MeasureFreeze(p, session.CurrentTick > tickBefore);
            }
            Send();
            CompareConfirmedChecksums();
            MeasureSpread(targetTick);
            Frame++;
        }

        // 届いた入力で巻き戻し、相手のキャラクターの今の位置が変わった量を記録する（表示ではワープに見える）。
        private void ReconcileAndMeasure(int observer)
        {
            SimWorld world = Worlds[observer];
            int rollbacks = Sessions[observer].RollbackCount;
            for (int q = 0; q < world.CharacterCount; q++)
            {
                _beforeReconcile[q] = world.GetState(q).Position;
            }
            Sessions[observer].Reconcile();
            if (Sessions[observer].RollbackCount == rollbacks)
            {
                return;
            }
            for (int q = 0; q < world.CharacterCount; q++)
            {
                float distance = Vector3.Distance(world.GetState(q).Position, _beforeReconcile[q]);
                if (q == observer || distance == 0f)
                {
                    continue;
                }
                CorrectionCount++;
                CorrectionDistanceSum += distance;
                LargestCorrection = Math.Max(LargestCorrection, distance);
                if (distance >= VisibleCorrectionDistance)
                {
                    VisibleCorrectionCount++;
                }
            }
        }

        private void MeasureFreeze(int p, bool advanced)
        {
            _freezeRun[p] = advanced ? 0 : _freezeRun[p] + 1;
            LongestFreeze = Math.Max(LongestFreeze, _freezeRun[p]);
        }

        private void MeasureSpread(int targetTick)
        {
            int min = int.MaxValue;
            int max = int.MinValue;
            foreach (RollbackSession session in Sessions)
            {
                if (session.CurrentTick >= targetTick)
                {
                    return;
                }
                min = Math.Min(min, session.CurrentTick);
                max = Math.Max(max, session.CurrentTick);
            }
            _spreadSum += max - min;
            _spreadSamples++;
        }

        private void Send()
        {
            for (int from = 0; from < Sessions.Length; from++)
            {
                for (int to = 0; to < Sessions.Length; to++)
                {
                    if (to == from)
                    {
                        continue;
                    }
                    byte[] bytes = Exchanges[from].BuildMessage(to).ToBytes();
                    SentMessages++;
                    SentBytes += bytes.Length;
                    int deliverFrame = Frame + _latency + (_jitter > 0 ? _random.Next(_jitter + 1) : 0);
                    if (_random.NextDouble() >= _loss)
                    {
                        _inFlight.Add(new Packet(to, deliverFrame, bytes));
                    }
                }
            }
        }

        private void Deliver()
        {
            for (int i = 0; i < _inFlight.Count; i++)
            {
                Packet packet = _inFlight[i];
                if (packet.DeliverFrame > Frame)
                {
                    continue;
                }
                _inFlight.RemoveAt(i);
                i--;
                Exchanges[packet.To].Receive(InputMessage.FromBytes(packet.Bytes));
            }
        }

        private void CompareConfirmedChecksums()
        {
            foreach (RollbackSession session in Sessions)
            {
                int last = Math.Min(session.ConfirmedTick + 1, session.CurrentTick);
                for (int tick = Math.Max(0, last - Settings.MaxPredictionTicks - 1); tick <= last; tick++)
                {
                    if (!session.TryGetConfirmedChecksum(tick, out ulong checksum))
                    {
                        continue;
                    }
                    if (_confirmed.TryGetValue(tick, out ulong other))
                    {
                        if (other != checksum)
                        {
                            ChecksumMismatches++;
                        }
                    }
                    else
                    {
                        _confirmed[tick] = checksum;
                    }
                }
            }
        }

        private bool AllDone(int targetTick)
        {
            foreach (RollbackSession session in Sessions)
            {
                if (session.CurrentTick < targetTick || session.ConfirmedTick < targetTick - 1)
                {
                    return false;
                }
            }
            return true;
        }

        private SimWorld MakeWorld(int players)
        {
            var world = new SimWorld(TestArenas.Playground());
            for (int p = 0; p < players; p++)
            {
                var config = new CharacterConfig();
                config.Wire.ModelType = p % 2 == 0 ? WireModelType.Inertial : WireModelType.Responsive;
                world.AddCharacter(config, Spawns[p], p % 2 == 0 ? 1 : -1);
            }
            _setup?.Invoke(world);
            return world;
        }

        private static PlayerInput[][] MakeScripts(int players, int seed)
        {
            var scripts = new PlayerInput[players][];
            for (int p = 0; p < players; p++)
            {
                scripts[p] = MakeScript(seed * 31 + p, 4000);
            }
            return scripts;
        }

        // 人の操作に似せた入力列: ランダムな長さ（3〜40ティック）だけ同じ入力を続ける。
        private static PlayerInput[] MakeScript(int seed, int length)
        {
            var random = new Random(seed);
            var script = new PlayerInput[length];
            int t = 0;
            while (t < length)
            {
                PlayerInput input = RandomInput(random);
                int hold = 3 + random.Next(38);
                for (int i = 0; i < hold && t < length; i++, t++)
                {
                    script[t] = input;
                }
            }
            return script;
        }

        private static PlayerInput RandomInput(Random random)
        {
            float x = random.Next(3) - 1;
            float y = random.Next(3) - 1;
            switch (random.Next(5))
            {
                case 0:
                    return PlayerInput.FromAxes(x, 0f, InputButtons.None);
                case 1:
                    return PlayerInput.FromAxes(x, 0f, InputButtons.Jump);
                case 2:
                    return PlayerInput.FromAxes(x, 1f, InputButtons.Wire);
                case 3:
                    return PlayerInput.FromAxes(x, y, InputButtons.Wire)
                        .WithAim(new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble()));
                default:
                    return default;
            }
        }

        private readonly struct Packet
        {
            public readonly int To;
            public readonly int DeliverFrame;
            public readonly byte[] Bytes;

            public Packet(int to, int deliverFrame, byte[] bytes)
            {
                To = to;
                DeliverFrame = deliverFrame;
                Bytes = bytes;
            }
        }
    }
}
