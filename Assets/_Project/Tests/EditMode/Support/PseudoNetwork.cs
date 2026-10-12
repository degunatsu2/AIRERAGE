using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Tests.Support
{
    /// <summary>
    /// ロールバックのセッションを、遅延・揺らぎ・欠落のある疑似ネットワークでつなぐテスト補助（docs/10_online_plan.md T2-3）。
    /// 全員が同じ速さでフレームを進める。各プレイヤーは毎フレーム、相手ごとに「まだ確認されていない自分の入力」と
    /// 「相手の入力をどこまで受け取ったか（確認応答）」を1つのメッセージで送る。乱数の種を固定し、毎回同じ結果になる。
    /// </summary>
    public sealed class PseudoNetwork
    {
        public static readonly Vector3[] Spawns =
        {
            new Vector3(-15f, 0.91f, 0f), new Vector3(-5f, 0.91f, 0f), new Vector3(4f, 0.91f, 0f), new Vector3(15f, 0.91f, 0f),
        };

        private readonly Random _random;
        private readonly int _latency;
        private readonly int _jitter;
        private readonly double _loss;
        private readonly List<Message> _inFlight = new List<Message>();

        // _acked[from][to]: to が受け取ったと確認した from の入力の最大のティック。
        private readonly int[][] _acked;

        public PseudoNetwork(int players, RollbackSettings settings, int latencyFrames, int jitterFrames, double lossRate, int seed)
        {
            _random = new Random(seed);
            _latency = latencyFrames;
            _jitter = jitterFrames;
            _loss = lossRate;
            Settings = settings;
            Scripts = new PlayerInput[players][];
            Sessions = new RollbackSession[players];
            Worlds = new SimWorld[players];
            _acked = new int[players][];
            for (int p = 0; p < players; p++)
            {
                Scripts[p] = MakeScript(seed * 31 + p, 4000);
                Worlds[p] = MakeWorld(players);
                Sessions[p] = new RollbackSession(Worlds[p], p, settings);
                _acked[p] = new int[players];
                for (int q = 0; q < players; q++)
                {
                    _acked[p][q] = settings.InputDelayTicks - 1;
                }
            }
        }

        public RollbackSettings Settings { get; }
        public PlayerInput[][] Scripts { get; }
        public RollbackSession[] Sessions { get; }
        public SimWorld[] Worlds { get; }
        public int Frame { get; private set; }

        /// <summary>入力を待って進めなかったフレームの数（全員の合計）。</summary>
        public int StalledFrames { get; private set; }

        /// <summary>同じティックについて、プレイヤーによって確定した状態のチェックサムが違った回数。</summary>
        public int ChecksumMismatches { get; private set; }

        /// <summary>確定した状態のチェックサムを比べたティックの数。</summary>
        public int ComparedTicks => _confirmed.Count;

        private readonly Dictionary<int, ulong> _confirmed = new Dictionary<int, ulong>();

        /// <summary>プレイヤー p がティック t に使う本当の入力（入力遅延の分の最初のティックは空の入力）。</summary>
        public PlayerInput TrueInput(int p, int t)
        {
            return t < Settings.InputDelayTicks ? default : Scripts[p][t];
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
                if (session.CurrentTick >= targetTick)
                {
                    session.Reconcile();
                }
                else if (session.CanAdvance)
                {
                    session.AdvanceFrame(Scripts[p][session.LocalInputTick]);
                }
                else
                {
                    StalledFrames++;
                    session.Reconcile();
                }
            }
            Send();
            CompareConfirmedChecksums();
            Frame++;
        }

        private void Send()
        {
            for (int from = 0; from < Sessions.Length; from++)
            {
                RollbackSession sender = Sessions[from];
                for (int to = 0; to < Sessions.Length; to++)
                {
                    if (to == from)
                    {
                        continue;
                    }
                    var message = new Message
                    {
                        From = from,
                        To = to,
                        Ack = sender.ContiguousTick(to),
                        DeliverFrame = Frame + _latency + (_jitter > 0 ? _random.Next(_jitter + 1) : 0),
                    };
                    // 確認されていない自分の入力をすべて送る（欠落に備えた冗長送信）。セッションは確定した古い入力を捨てるので、
                    // 送る入力は送り手が自分で持っておく（ここでは入力列から取る）。
                    for (int tick = _acked[from][to] + 1; tick <= sender.ContiguousTick(from); tick++)
                    {
                        message.Inputs.Add((tick, TrueInput(from, tick)));
                    }
                    if (_random.NextDouble() >= _loss)
                    {
                        _inFlight.Add(message);
                    }
                }
            }
        }

        private void Deliver()
        {
            for (int i = 0; i < _inFlight.Count; i++)
            {
                Message message = _inFlight[i];
                if (message.DeliverFrame > Frame)
                {
                    continue;
                }
                _inFlight.RemoveAt(i);
                i--;
                RollbackSession receiver = Sessions[message.To];
                foreach ((int tick, PlayerInput input) in message.Inputs)
                {
                    receiver.AddRemoteInput(message.From, tick, input);
                }
                // 送り主は、受け手の入力を message.Ack まで受け取っている。
                _acked[message.To][message.From] = Math.Max(_acked[message.To][message.From], message.Ack);
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

        private static SimWorld MakeWorld(int players)
        {
            var world = new SimWorld(TestArenas.Playground());
            for (int p = 0; p < players; p++)
            {
                var config = new CharacterConfig();
                config.Wire.ModelType = p % 2 == 0 ? WireModelType.Inertial : WireModelType.Responsive;
                world.AddCharacter(config, Spawns[p], p % 2 == 0 ? 1 : -1);
            }
            return world;
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

        private sealed class Message
        {
            public int From;
            public int To;
            public int Ack;
            public int DeliverFrame;
            public readonly List<(int Tick, PlayerInput Input)> Inputs = new List<(int, PlayerInput)>();
        }
    }
}
