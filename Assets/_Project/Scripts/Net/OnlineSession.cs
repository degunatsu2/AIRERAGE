using System;
using System.Collections.Generic;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Match;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Simulation;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Degunatsu.Net
{
    /// <summary>
    /// オンラインの試合の1人分（docs/10_online_plan.md T2-7）。通信（TransportLink）と、Core のロールバック
    /// （RollbackSession / InputExchange）をつなぐ。MonoBehaviour ではないので、テストで複数を同じプロセスで動かせる。
    /// - 構成: ホスト（プレイヤー 0）にクライアントがつながる星形。クライアントの入力はホストが宛先へ中継する。
    /// - 参加: クライアントがつながると、ホストが番号を振る（Welcome）。ホストが StartMatch を呼ぶと、人数と調整値を
    ///   全員に送り（Start）、全員が同じワールドで始める。試合中の参加はできない。
    /// - 不一致の検出: 確定した状態のチェックサムを ChecksumIntervalTicks ごとにホストへ送り、ホストが自分の値と比べる。
    /// - ステージ（OnlineStage）: 地形、出現位置、ステージの範囲（撃破）、ギミック。全員が自分のシーンから作る。
    /// - 再戦: ホストが RestartMatch を呼ぶと、同じ人で、新しい試合の番号で始め直す。入力とチェックサムのメッセージには
    ///   試合の番号をつけ、今の試合のものだけを使う（前の試合のメッセージが遅れて届いても混ざらない）。
    /// </summary>
    public sealed class OnlineSession : IDisposable
    {
        public enum SessionState
        {
            Connecting,
            Lobby,
            Running,
            Ended,
        }

        public const int MaxPlayers = 4;

        /// <summary>チェックサムを比べる間隔（ティック）。</summary>
        public const int ChecksumIntervalTicks = 60;

        // 1フレームで進める最大のティック数（処理落ちのときに追いつこうとして、さらに遅れるのを防ぐ）。
        private const int MaxTicksPerFrame = 5;

        private readonly TransportLink _link;
        private readonly OnlineStage _stage;
        private readonly RollbackSettings _settings;
        private readonly CharacterConfig _hostConfig;
        private readonly List<TransportLink.Event> _events = new List<TransportLink.Event>();

        // ホスト: プレイヤーの番号 → 接続の番号（-1 ならいない）。
        private readonly int[] _playerPeer = new int[MaxPlayers];

        // ホスト: ティック → プレイヤーごとのチェックサム（全員の分を比べたら消す）。
        private readonly Dictionary<int, ChecksumRow> _checksums = new Dictionary<int, ChecksumRow>();

        private CharacterState[] _previous = new CharacterState[0];
        private NVector3[] _corrections = new NVector3[0];
        private NVector3[] _beforeReconcile = new NVector3[0];
        private float _accumulator;
        private int _waitFrames;
        private int _nextChecksumTick = ChecksumIntervalTicks;

        private OnlineSession(TransportLink link, OnlineStage stage, RollbackSettings settings, CharacterConfig hostConfig)
        {
            _link = link;
            _stage = stage;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _hostConfig = hostConfig;
            for (int p = 0; p < MaxPlayers; p++)
            {
                _playerPeer[p] = -1;
            }
        }

        public bool IsHost => _link.IsHost;
        public SessionState State { get; private set; }

        /// <summary>自分のプレイヤーの番号（クライアントは Welcome が届くまで -1）。</summary>
        public int LocalPlayer { get; private set; } = -1;

        /// <summary>試合の人数（始まるまで 0）。</summary>
        public int PlayerCount { get; private set; }

        /// <summary>今の試合の番号（始まるたびに 1 増える。0〜255 を繰り返す。始まるまで 0）。</summary>
        public int MatchNumber { get; private set; }

        /// <summary>ロビーにいるプレイヤーの数（ホストのみ。自分を含む）。</summary>
        public int LobbyPlayerCount
        {
            get
            {
                int count = 0;
                foreach (int peer in _playerPeer)
                {
                    count += peer >= 0 ? 1 : 0;
                }
                return IsHost ? count + 1 : count;
            }
        }

        public ushort Port => _link.LocalPort;
        public SimWorld World { get; private set; }
        public RollbackSession Session { get; private set; }
        public InputExchange Exchange { get; private set; }

        /// <summary>今の状態の説明（画面に表示する）。</summary>
        public string Status { get; private set; } = string.Empty;

        /// <summary>見つかった不一致（ホストのみ）。</summary>
        public List<string> Desyncs { get; } = new List<string>();

        /// <summary>比べたチェックサムの数（ホストのみ）。</summary>
        public int ComparedChecksums { get; private set; }

        public int StalledFrames { get; private set; }
        public int TimeSyncWaitFrames { get; private set; }

        /// <summary>巻き戻しで、表示中のキャラクターの位置が変わった回数（キャラクターごとに数える）。</summary>
        public int CorrectionCount { get; private set; }

        /// <summary>巻き戻しで位置が変わった量の最大（m）。</summary>
        public float LargestCorrection { get; private set; }

        /// <summary>通信路（遅延の模擬の設定に使う）。</summary>
        public TransportLink Link => _link;

        /// <summary>これより先のティックへは進めない（テストとデバッグ用）。</summary>
        public int TickLimit { get; set; } = int.MaxValue;

        /// <summary>表示の補間に使う、直前のティックの状態と、次のティックまでの割合（0〜1）。</summary>
        public IReadOnlyList<CharacterState> PreviousStates => _previous;
        public float Alpha => _accumulator / SimTime.DeltaTime;

        /// <summary>ホストとして port で待ち受ける（0 なら空いているポート）。地形と出現位置だけのステージ（撃破とギミックはない）。</summary>
        public static OnlineSession Host(ushort port, ICollisionQuery terrain, NVector3 spawnBase, CharacterConfig config, RollbackSettings settings)
        {
            return Host(port, new OnlineStage(terrain, new[] { spawnBase }), config, settings);
        }

        /// <summary>ホストとして port で待ち受ける（0 なら空いているポート）。config は全員が使う調整値。</summary>
        public static OnlineSession Host(ushort port, OnlineStage stage, CharacterConfig config, RollbackSettings settings)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            config.Validate();
            var session = new OnlineSession(TransportLink.Host(port), stage, settings, config)
            {
                LocalPlayer = 0,
                State = SessionState.Lobby,
            };
            session.Status = $"Hosting on port {session.Port}";
            return session;
        }

        /// <summary>address:port のホストに参加する。地形と出現位置だけのステージ（撃破とギミックはない）。</summary>
        public static OnlineSession Join(string address, ushort port, ICollisionQuery terrain, NVector3 spawnBase, RollbackSettings settings)
        {
            return Join(address, port, new OnlineStage(terrain, new[] { spawnBase }), settings);
        }

        /// <summary>address:port のホストに参加する。stage はホストと同じものにする（同じシーンから作る）。</summary>
        public static OnlineSession Join(string address, ushort port, OnlineStage stage, RollbackSettings settings)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            var session = new OnlineSession(TransportLink.Connect(address, port), stage, settings, null)
            {
                State = SessionState.Connecting,
            };
            session.Status = $"Connecting to {address}:{port}";
            return session;
        }

        /// <summary>
        /// 前に呼んでから、巻き戻しでプレイヤー player の位置が変わった量（m）を返し、0 に戻す。
        /// 表示の補正（CorrectionSmoother）に使う。補間に使う直前のティックの位置も同じだけずらしてあるので、
        /// 表示の位置はちょうどこの量だけ飛ぶ。
        /// </summary>
        public NVector3 ConsumeCorrection(int player)
        {
            NVector3 correction = _corrections[player];
            _corrections[player] = NVector3.Zero;
            return correction;
        }

        /// <summary>出現位置が1つのときの出現位置（人数に合わせて左右に並べる）。</summary>
        public static NVector3 SpawnPosition(NVector3 spawnBase, int player, int playerCount)
        {
            return spawnBase + new NVector3((player - (playerCount - 1) * 0.5f) * OnlineStage.SpawnSpacing, 0f, 0f);
        }

        /// <summary>
        /// 試合を始める（ホストのみ、ロビーで）。今いる全員で始める。rules は試合のルール（null なら時間の制限なし。
        /// MatchRules.FreePlay()）で、調整値といっしょに全員へ送る。不正なら InvalidTuningException。
        /// </summary>
        public void StartMatch(MatchRules rules = null)
        {
            if (!IsHost || State != SessionState.Lobby)
            {
                throw new InvalidOperationException("試合を始められるのは、ロビーにいるホストだけです");
            }
            SendStartAndBegin(LobbyPlayerCount, rules);
        }

        /// <summary>
        /// 同じ人で試合を始め直す（ホストのみ、試合中か試合が終わった後。結果の画面の「Restart」）。rules は StartMatch と同じ。
        /// 試合の番号が変わり、全員が新しいワールドで始める。
        /// </summary>
        public void RestartMatch(MatchRules rules = null)
        {
            if (!IsHost || State != SessionState.Running)
            {
                throw new InvalidOperationException("試合を始め直せるのは、試合中のホストだけです");
            }
            SendStartAndBegin(PlayerCount, rules);
        }

        private void SendStartAndBegin(int count, MatchRules rules)
        {
            rules = rules ?? MatchRules.FreePlay();
            rules.Validate().ThrowIfInvalid();
            int match = (MatchNumber + 1) & 0xFF;
            // ホストも送ったものと同じ JSON から作った値を使う（全員が同じ値になり、試合中にアセットを編集しても影響しない）。
            string json = JsonUtility.ToJson(new StartSettings { Character = _hostConfig, Match = rules });
            byte[] start = NetMessages.Start(count, match, json);
            for (int p = 1; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] >= 0)
                {
                    _link.Send(_playerPeer[p], start, reliable: true);
                }
            }
            Begin(count, match, SettingsFromJson(json));
        }

        /// <summary>
        /// 毎フレーム呼ぶ。送受信を進め、試合中なら経過時間の分だけティックを進める。
        /// localInput は、自分の入力を使うティック（RollbackSession.LocalInputTick）を受け取り、そのティックの入力を返す。
        /// </summary>
        public void Update(float deltaTime, Func<int, PlayerInput> localInput)
        {
            PumpEvents();
            if (State == SessionState.Running)
            {
                Advance(deltaTime, localInput);
                SendInputs();
                ReportChecksum();
            }
            _link.Update(_events);
            HandleEvents();
        }

        public void Dispose()
        {
            _link.Dispose();
        }

        private void Advance(float deltaTime, Func<int, PlayerInput> localInput)
        {
            // 届いた入力で予測が外れていたら、ここで巻き戻す（この後の AdvanceFrame の中では巻き戻しは起きない）。
            ReconcileAndMeasure();
            _accumulator += deltaTime;
            int ticks = 0;
            while (_accumulator >= SimTime.DeltaTime && ticks < MaxTicksPerFrame)
            {
                _accumulator -= SimTime.DeltaTime;
                ticks++;
                if (Session.CurrentTick >= TickLimit)
                {
                    break;
                }
                if (_waitFrames > 0)
                {
                    // 時刻の同期: 相手より進みすぎているので、このティックは待つ。
                    _waitFrames--;
                    TimeSyncWaitFrames++;
                    continue;
                }
                if (!Session.CanAdvance)
                {
                    // 予測の上限: 相手の入力が届くまで待つ（待った分の時間は持ち越さない）。
                    StalledFrames++;
                    _accumulator = 0f;
                    break;
                }
                for (int p = 0; p < PlayerCount; p++)
                {
                    _previous[p] = World.GetState(p);
                }
                Exchange.AdvanceFrame(localInput(Session.LocalInputTick));
                _waitFrames += Exchange.RecommendWaitFrames();
            }
            if (ticks == MaxTicksPerFrame && _accumulator >= SimTime.DeltaTime)
            {
                _accumulator = 0f;
            }
        }

        // 巻き戻して計算し直し、各キャラクターの今の位置が変わった量を記録する。
        private void ReconcileAndMeasure()
        {
            int rollbacks = Session.RollbackCount;
            for (int p = 0; p < PlayerCount; p++)
            {
                _beforeReconcile[p] = World.GetState(p).Position;
            }
            Session.Reconcile();
            if (Session.RollbackCount == rollbacks)
            {
                return;
            }
            for (int p = 0; p < PlayerCount; p++)
            {
                NVector3 change = World.GetState(p).Position - _beforeReconcile[p];
                if (change == NVector3.Zero)
                {
                    continue;
                }
                _corrections[p] += change;
                // 補間に使う直前のティックの位置も同じだけずらす（表示がちょうど change だけ飛ぶようにする）。
                _previous[p].Position += change;
                CorrectionCount++;
                LargestCorrection = Math.Max(LargestCorrection, change.Length());
            }
        }

        private void SendInputs()
        {
            for (int p = 0; p < PlayerCount; p++)
            {
                if (p == LocalPlayer)
                {
                    continue;
                }
                byte[] data = NetMessages.Input(MatchNumber, p, Exchange.BuildMessage(p).ToBytes());
                if (IsHost)
                {
                    _link.Send(_playerPeer[p], data, reliable: false);
                }
                else
                {
                    _link.Send(0, data, reliable: false);
                }
            }
        }

        // 確定した状態のチェックサムを、間隔ごとにホストへ送る（ホストは自分の値として記録する）。
        private void ReportChecksum()
        {
            // 古くなって状態が残っていないティックは飛ばす。
            int oldest = Session.CurrentTick - _settings.MaxPredictionTicks - 1;
            while (_nextChecksumTick < oldest)
            {
                _nextChecksumTick += ChecksumIntervalTicks;
            }
            if (!Session.TryGetConfirmedChecksum(_nextChecksumTick, out ulong checksum))
            {
                return;
            }
            if (IsHost)
            {
                RecordChecksum(LocalPlayer, _nextChecksumTick, checksum);
            }
            else
            {
                _link.Send(0, NetMessages.Checksum(MatchNumber, LocalPlayer, _nextChecksumTick, checksum), reliable: true);
            }
            _nextChecksumTick += ChecksumIntervalTicks;
        }

        private void RecordChecksum(int player, int tick, ulong checksum)
        {
            if (!_checksums.TryGetValue(tick, out ChecksumRow row))
            {
                row = new ChecksumRow();
                _checksums[tick] = row;
            }
            row.Values[player] = checksum;
            ulong? host = row.Values[0];
            if (host == null)
            {
                return;
            }
            for (int p = 1; p < PlayerCount; p++)
            {
                if (row.Values[p] == null)
                {
                    continue;
                }
                ComparedChecksums++;
                row.Compared++;
                if (row.Values[p] != host)
                {
                    string message = $"ティック {tick}: プレイヤー {p} の状態がホストと違う（{row.Values[p]:x16} / {host:x16}）";
                    Desyncs.Add(message);
                    Debug.LogError($"OnlineSession: 不一致を検出しました。{message}");
                }
                row.Values[p] = null;
            }
            if (row.Compared >= PlayerCount - 1)
            {
                _checksums.Remove(tick);
            }
        }

        private void Begin(int playerCount, int match, StartSettings settings)
        {
            PlayerCount = playerCount;
            MatchNumber = match;
            // 前の試合（再戦の前）の進み具合を残さない。
            _accumulator = 0f;
            _waitFrames = 0;
            _nextChecksumTick = ChecksumIntervalTicks;
            _checksums.Clear();
            World = new SimWorld(_stage.Terrain, _stage.Bounds, _stage.Zones, settings.Match);
            for (int p = 0; p < playerCount; p++)
            {
                World.AddCharacter(settings.Character, _stage.SpawnPosition(p, playerCount), _stage.SpawnFacing(p, playerCount));
            }
            Session = new RollbackSession(World, LocalPlayer, _settings);
            Exchange = new InputExchange(Session, _settings);
            _previous = new CharacterState[playerCount];
            _corrections = new NVector3[playerCount];
            _beforeReconcile = new NVector3[playerCount];
            for (int p = 0; p < playerCount; p++)
            {
                _previous[p] = World.GetState(p);
            }
            State = SessionState.Running;
            Status = $"Running: player {LocalPlayer + 1} of {playerCount}";
        }

        private void PumpEvents()
        {
            _link.Update(_events);
            HandleEvents();
        }

        private void HandleEvents()
        {
            foreach (TransportLink.Event e in _events)
            {
                try
                {
                    if (IsHost)
                    {
                        HandleHostEvent(e);
                    }
                    else
                    {
                        HandleClientEvent(e);
                    }
                }
                catch (FormatException ex)
                {
                    // 壊れたメッセージは捨てる（原因を追えるようにログを残す）。
                    Debug.LogWarning($"OnlineSession: 読めないメッセージを捨てました（相手 {e.Peer}）: {ex.Message}");
                }
            }
            _events.Clear();
        }

        private void HandleHostEvent(TransportLink.Event e)
        {
            switch (e.Kind)
            {
                case TransportLink.EventKind.Connected:
                    int player = FreePlayer();
                    if (State != SessionState.Lobby || player < 0)
                    {
                        _link.Send(e.Peer, NetMessages.Refused(), reliable: true);
                        _link.Disconnect(e.Peer);
                        return;
                    }
                    _playerPeer[player] = e.Peer;
                    _link.Send(e.Peer, NetMessages.Welcome(player), reliable: true);
                    Status = $"Hosting on port {Port}: {LobbyPlayerCount} player(s)";
                    break;

                case TransportLink.EventKind.Disconnected:
                    for (int p = 1; p < MaxPlayers; p++)
                    {
                        if (_playerPeer[p] == e.Peer)
                        {
                            _playerPeer[p] = -1;
                            if (State == SessionState.Running)
                            {
                                // 試合中に抜けた人がいたら、試合を終える（残りの人も止まったままにしないよう、接続を切る）。
                                State = SessionState.Ended;
                                Status = $"Player {p + 1} disconnected";
                                DisconnectAll();
                            }
                        }
                    }
                    break;

                case TransportLink.EventKind.Data:
                    HandleHostData(e);
                    break;
            }
        }

        private void HandleHostData(TransportLink.Event e)
        {
            switch (NetMessages.KindOf(e.Data))
            {
                case NetKind.Input:
                    if (State != SessionState.Running || NetMessages.InputMatch(e.Data) != MatchNumber)
                    {
                        // 試合の前か、前の試合のメッセージ（再戦の前に送られて遅れて届いた）。
                        return;
                    }
                    int target = NetMessages.InputTarget(e.Data);
                    if (target == LocalPlayer)
                    {
                        Exchange.Receive(Degunatsu.Core.Rollback.InputMessage.FromBytes(NetMessages.InputPayload(e.Data)));
                    }
                    else if (target > 0 && target < PlayerCount && _playerPeer[target] >= 0)
                    {
                        _link.Send(_playerPeer[target], e.Data, reliable: false);
                    }
                    break;

                case NetKind.Checksum:
                    NetMessages.ReadChecksum(e.Data, out int checksumMatch, out int player, out int tick, out ulong checksum);
                    if (State == SessionState.Running && checksumMatch == MatchNumber && player > 0 && player < PlayerCount)
                    {
                        RecordChecksum(player, tick, checksum);
                    }
                    break;
            }
        }

        private void HandleClientEvent(TransportLink.Event e)
        {
            switch (e.Kind)
            {
                case TransportLink.EventKind.Connected:
                    Status = "Connected. Waiting for the host";
                    break;

                case TransportLink.EventKind.Disconnected:
                    State = SessionState.Ended;
                    Status = "Disconnected from the host";
                    break;

                case TransportLink.EventKind.Data:
                    switch (NetMessages.KindOf(e.Data))
                    {
                        case NetKind.Welcome:
                            LocalPlayer = NetMessages.ReadWelcome(e.Data);
                            State = SessionState.Lobby;
                            Status = $"Joined as player {LocalPlayer + 1}. Waiting for the host to start";
                            break;
                        case NetKind.Refused:
                            State = SessionState.Ended;
                            Status = "The host refused (full or already playing)";
                            break;
                        case NetKind.Start:
                            NetMessages.ReadStart(e.Data, out int count, out int match, out string json);
                            Begin(count, match, SettingsFromJson(json));
                            break;
                        case NetKind.Input:
                            if (State == SessionState.Running && NetMessages.InputMatch(e.Data) == MatchNumber
                                && NetMessages.InputTarget(e.Data) == LocalPlayer)
                            {
                                Exchange.Receive(Degunatsu.Core.Rollback.InputMessage.FromBytes(NetMessages.InputPayload(e.Data)));
                            }
                            break;
                    }
                    break;
            }
        }

        private void DisconnectAll()
        {
            for (int p = 1; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] >= 0)
                {
                    _link.Disconnect(_playerPeer[p]);
                    _playerPeer[p] = -1;
                }
            }
        }

        private static StartSettings SettingsFromJson(string json)
        {
            var settings = new StartSettings { Character = new CharacterConfig(), Match = new MatchRules() };
            JsonUtility.FromJsonOverwrite(json, settings);
            settings.Character.Validate();
            settings.Match.Validate().ThrowIfInvalid();
            return settings;
        }

        /// <summary>試合の開始のときに、ホストが全員へ送る値（調整値と試合のルール）。</summary>
        [Serializable]
        private sealed class StartSettings
        {
            public CharacterConfig Character;
            public MatchRules Match;
        }

        private int FreePlayer()
        {
            for (int p = 1; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] < 0)
                {
                    return p;
                }
            }
            return -1;
        }

        private sealed class ChecksumRow
        {
            public readonly ulong?[] Values = new ulong?[MaxPlayers];
            public int Compared;
        }
    }
}
