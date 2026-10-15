using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Airerage.Net
{
    /// <summary>
    /// オンラインの試合の1人分（docs/10_online_plan.md T2-7）。通信（TransportLink）と、Core のロールバック
    /// （RollbackSession / InputExchange）をつなぐ。MonoBehaviour ではないので、テストで複数を同じプロセスで動かせる。
    /// - 構成: ホスト（プレイヤー 0）にクライアントがつながる星形。クライアントの入力はホストが宛先へ中継する。
    /// - 参加: クライアントがつながると、ホストが番号を振る（Welcome）。ホストが StartMatch を呼ぶと、人数と調整値を
    ///   全員に送り（Start）、全員が同じワールドで始める。試合中の参加はできない。
    /// - 不一致の検出: 確定した状態のチェックサムを ChecksumIntervalTicks ごとにホストへ送り、ホストが自分の値と比べる。
    /// - ステージ（OnlineStage）: 地形、出現位置、ステージの範囲（撃破）、ギミック。全員が自分のシーンから作る。
    /// - プレイヤーネーム: 各自が LocalName を決める（ロビーで）。クライアントは変えるたびにホストへ送り（Hello）、ホストが試合の
    ///   開始のときに全員の名前を開始のメッセージに入れて送る（PlayerNames）。名前はシミュレーションに関係しない（表示だけ）。
    /// - 開始のメッセージは、クライアントが新しい試合の入力を送ってくる（その試合を始めた）まで、ホストが StartResendSeconds ごとに
    ///   送り直す（送れなかった、届かなかったときに、ホストだけが始まって止まったままにならないように。配布したビルドでの再戦で
    ///   起きた）。クライアントは、今の試合と同じ番号の開始のメッセージは無視する。
    /// - 待機所（ロビー）: ホストは、番号ごとにつながっているかとプレイヤーネーム（Roster）を、変わるたびに全員へ送る（Lobby）。
    ///   クライアントも同じ顔ぶれを表示できる。ロビーで抜けた人の番号は空き、試合を始めるときに詰める（番号が変わる人には
    ///   Welcome を送り直してから Start を送る。確実に届ける経路は順番どおりに届く）。
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

        /// <summary>プレイヤーネームの最大の文字数。</summary>
        public const int MaxNameLength = 12;

        /// <summary>チェックサムを比べる間隔（ティック）。</summary>
        public const int ChecksumIntervalTicks = 60;

        // 1フレームで進める最大のティック数（処理落ちのときに追いつこうとして、さらに遅れるのを防ぐ）。
        private const int MaxTicksPerFrame = 5;

        /// <summary>まだ新しい試合を始めていないクライアントへ、開始のメッセージを送り直す間隔（秒）。</summary>
        public const float StartResendSeconds = 0.5f;

        private readonly TransportLink _link;
        private readonly OnlineStage _stage;
        private readonly RollbackSettings _settings;
        // ホスト: 選べるキャラクターの調整値（番号の順。CharacterRoster と同じ順）。
        private readonly IReadOnlyList<CharacterConfig> _characters;
        private readonly List<TransportLink.Event> _events = new List<TransportLink.Event>();

        // ホスト: プレイヤーの番号 → 接続の番号（-1 ならいない）。
        private readonly int[] _playerPeer = new int[MaxPlayers];

        // ホスト: プレイヤーの番号 → ロビーで届いたプレイヤーネーム（空ならまだない）。
        private readonly string[] _lobbyNames = new string[MaxPlayers];

        // ホスト: プレイヤーの番号 → ロビーで選んだキャラクターの番号（docs/13 T5-5）。
        private readonly int[] _lobbyCharacters = new int[MaxPlayers];

        // ホスト: プレイヤーの番号 → そのクライアントから最後に届いた入力の試合の番号（まだなら -1）。
        private readonly int[] _peerMatch = new int[MaxPlayers];

        // ホスト: 送り直すかもしれない開始のメッセージ（全員が始めたら null）と、次に送り直すまでの秒数。
        private byte[] _startMessage;
        private float _startResendTimer;
        private string _localName = string.Empty;
        private int _localCharacter;

        // クライアント: ホストから届いたロビーの顔ぶれ（まだ届いていなければ、全員いない）。
        private LobbySlot[] _clientRoster = EmptyRoster();

        // ホスト: ティック → プレイヤーごとのチェックサム（全員の分を比べたら消す）。
        private readonly Dictionary<int, ChecksumRow> _checksums = new Dictionary<int, ChecksumRow>();

        private CharacterState[] _previous = new CharacterState[0];
        private NVector3[] _corrections = new NVector3[0];
        private NVector3[] _beforeReconcile = new NVector3[0];
        private float _accumulator;
        private int _waitFrames;
        private int _nextChecksumTick = ChecksumIntervalTicks;

        private OnlineSession(TransportLink link, OnlineStage stage, RollbackSettings settings, IReadOnlyList<CharacterConfig> characters)
        {
            _link = link;
            _stage = stage;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _characters = characters;
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

        /// <summary>試合のプレイヤーネーム（番号の順。入力がなかった人は空。始まるまで空の配列）。</summary>
        public IReadOnlyList<string> PlayerNames { get; private set; } = Array.Empty<string>();

        /// <summary>試合で各プレイヤーが使うキャラクターの番号（番号の順。CharacterRoster の番号。始まるまで空の配列。表示に使う）。</summary>
        public IReadOnlyList<int> PlayerCharacters { get; private set; } = Array.Empty<int>();

        /// <summary>
        /// 自分が選んだキャラクターの番号（CharacterRoster の番号。docs/13 T5-5）。クライアントは、ロビーにいればすぐにホストへ送る。
        /// 試合中に変えたら、次の試合（再戦）から使われる。ホストは、選べる数を超えた番号を 0 にする。
        /// </summary>
        public int LocalCharacter
        {
            get => _localCharacter;
            set
            {
                int character = Math.Max(0, value);
                if (character == _localCharacter)
                {
                    return;
                }
                _localCharacter = character;
                if (IsHost)
                {
                    _lobbyCharacters[0] = ClampCharacter(character);
                    BroadcastLobby();
                }
                else if (LocalPlayer >= 0)
                {
                    _link.Send(0, NetMessages.Pick(character), reliable: true);
                }
            }
        }

        // ホスト: 選べるキャラクターの番号に収める（範囲の外なら 0）。
        private int ClampCharacter(int character)
        {
            return _characters != null && character >= 0 && character < _characters.Count ? character : 0;
        }
        /// <summary>
        /// 自分のプレイヤーネーム（SanitizeName で整える）。クライアントは、ロビーにいればすぐにホストへ送る。試合中に変えたら、
        /// 次の試合（再戦）から使われる。
        /// </summary>
        public string LocalName
        {
            get => _localName;
            set
            {
                string name = SanitizeName(value);
                if (name == _localName)
                {
                    return;
                }
                _localName = name;
                if (IsHost)
                {
                    _lobbyNames[0] = name;
                    BroadcastLobby();
                }
                else if (LocalPlayer >= 0)
                {
                    _link.Send(0, NetMessages.Hello(name), reliable: true);
                }
            }
        }

        /// <summary>名前を整える: 制御文字を除き、前後の空白を取り、MaxNameLength 文字までにする（null は空）。</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (!char.IsControl(c))
                {
                    builder.Append(c);
                }
            }
            string trimmed = builder.ToString().Trim();
            return trimmed.Length > MaxNameLength ? trimmed.Substring(0, MaxNameLength).TrimEnd() : trimmed;
        }

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

        /// <summary>
        /// ロビーの顔ぶれ（番号の順に MaxPlayers 個。つながっているかとプレイヤーネーム）。ホストは今の値、クライアントはホストから
        /// 最後に届いた値（待機所の表示に使う）。
        /// </summary>
        public IReadOnlyList<LobbySlot> Roster
        {
            get
            {
                if (!IsHost)
                {
                    return _clientRoster;
                }
                var slots = new LobbySlot[MaxPlayers];
                slots[0] = new LobbySlot(true, _localName, _lobbyCharacters[0]);
                for (int p = 1; p < MaxPlayers; p++)
                {
                    bool connected = _playerPeer[p] >= 0;
                    slots[p] = new LobbySlot(connected, connected ? _lobbyNames[p] : string.Empty, connected ? _lobbyCharacters[p] : 0);
                }
                return slots;
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

        /// <summary>開始のメッセージを送り直した回数（ホストのみ。調査とテスト用）。</summary>
        public int StartResends { get; private set; }

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

        /// <summary>ホストとして port で待ち受ける（0 なら空いているポート）。config は全員が使う調整値（キャラクターは 1 体だけ）。</summary>
        public static OnlineSession Host(ushort port, OnlineStage stage, CharacterConfig config, RollbackSettings settings)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            return Host(port, stage, new[] { config }, settings);
        }

        /// <summary>
        /// ホストとして port で待ち受ける（0 なら空いているポート）。characters は選べるキャラクターの調整値（番号の順。CharacterRoster と
        /// 同じ順。1 体以上）。開始のときに、各プレイヤーが選んだキャラクターの調整値を全員へ送る（docs/13 T5-5）。
        /// </summary>
        public static OnlineSession Host(ushort port, OnlineStage stage, IReadOnlyList<CharacterConfig> characters, RollbackSettings settings)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            if (characters == null || characters.Count == 0)
            {
                throw new ArgumentException("選べるキャラクターが 1 体もありません", nameof(characters));
            }
            foreach (CharacterConfig config in characters)
            {
                if (config == null)
                {
                    throw new ArgumentNullException(nameof(characters), "キャラクターの調整値が null です");
                }
                config.Validate();
            }
            var session = new OnlineSession(TransportLink.Host(port), stage, settings, characters)
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
            CloseUpPlayers();
            SendStartAndBegin(LobbyPlayerCount, rules);
        }

        // ロビーで抜けた人の空いた番号を詰める（番号が 0 から人数 - 1 までになるように）。番号が変わる人には Welcome を送り直す。
        private void CloseUpPlayers()
        {
            int next = 1;
            for (int p = 1; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] < 0)
                {
                    continue;
                }
                if (p != next)
                {
                    _playerPeer[next] = _playerPeer[p];
                    _lobbyNames[next] = _lobbyNames[p];
                    _lobbyCharacters[next] = _lobbyCharacters[p];
                    _playerPeer[p] = -1;
                    _lobbyNames[p] = string.Empty;
                    _lobbyCharacters[p] = 0;
                    _link.Send(_playerPeer[next], NetMessages.Welcome(next), reliable: true);
                }
                next++;
            }
        }

        // ホスト: ロビーの顔ぶれを全員へ送る（ロビーにいる間だけ）。
        private void BroadcastLobby()
        {
            if (!IsHost || State != SessionState.Lobby)
            {
                return;
            }
            byte[] message = NetMessages.Lobby(Roster);
            for (int p = 1; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] >= 0)
                {
                    _link.Send(_playerPeer[p], message, reliable: true);
                }
            }
        }

        private static LobbySlot[] EmptyRoster()
        {
            var slots = new LobbySlot[MaxPlayers];
            for (int p = 0; p < MaxPlayers; p++)
            {
                slots[p] = new LobbySlot(false, string.Empty, 0);
            }
            return slots;
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
            var names = new string[count];
            for (int p = 0; p < count; p++)
            {
                names[p] = SanitizeName(p == 0 ? _localName : _lobbyNames[p]);
            }
            // 使うキャラクターの調整値だけを、重ねずに送る（同じキャラクターを選んだ人は同じ調整値を指す）。
            var picks = new int[count];
            var ids = new int[count];
            var used = new List<int>();
            for (int p = 0; p < count; p++)
            {
                ids[p] = ClampCharacter(_lobbyCharacters[p]);
                int index = used.IndexOf(ids[p]);
                if (index < 0)
                {
                    index = used.Count;
                    used.Add(ids[p]);
                }
                picks[p] = index;
            }
            var configs = new CharacterConfig[used.Count];
            for (int i = 0; i < used.Count; i++)
            {
                configs[i] = _characters[used[i]];
            }
            string json = JsonUtility.ToJson(new StartSettings
            {
                Characters = configs, Picks = picks, CharacterIds = ids, Match = rules, Names = names,
            });
            byte[] start = NetMessages.Start(count, match, json);
            for (int p = 1; p < MaxPlayers; p++)
            {
                _peerMatch[p] = -1;
                if (_playerPeer[p] >= 0 && !_link.Send(_playerPeer[p], start, reliable: true))
                {
                    Debug.LogError($"OnlineSession: 試合の開始のメッセージをプレイヤー {p + 1} に送れませんでした（{start.Length} バイト）。送り直します");
                }
            }
            _startMessage = start;
            _startResendTimer = StartResendSeconds;
            Begin(count, match, SettingsFromJson(json, count));
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
                ResendStartIfNeeded(deltaTime);
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

        // ホスト: 新しい試合の入力をまだ送ってこない（始めていない）クライアントへ、開始のメッセージを送り直す。
        private void ResendStartIfNeeded(float deltaTime)
        {
            if (!IsHost || _startMessage == null)
            {
                return;
            }
            bool waiting = false;
            for (int p = 1; p < PlayerCount; p++)
            {
                waiting |= _playerPeer[p] >= 0 && _peerMatch[p] != MatchNumber;
            }
            if (!waiting)
            {
                _startMessage = null;
                return;
            }
            _startResendTimer -= deltaTime;
            if (_startResendTimer > 0f)
            {
                return;
            }
            _startResendTimer = StartResendSeconds;
            for (int p = 1; p < PlayerCount; p++)
            {
                if (_playerPeer[p] >= 0 && _peerMatch[p] != MatchNumber)
                {
                    _link.Send(_playerPeer[p], _startMessage, reliable: true);
                    StartResends++;
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
            var names = new string[playerCount];
            for (int p = 0; p < playerCount; p++)
            {
                names[p] = settings.Names != null && p < settings.Names.Length ? SanitizeName(settings.Names[p]) : string.Empty;
            }
            PlayerNames = names;
            PlayerCharacters = (int[])settings.CharacterIds.Clone();
            MatchNumber = match;
            // 前の試合（再戦の前）の進み具合を残さない。
            _accumulator = 0f;
            _waitFrames = 0;
            _nextChecksumTick = ChecksumIntervalTicks;
            _checksums.Clear();
            World = new SimWorld(_stage.Terrain, _stage.Bounds, _stage.Zones, settings.Match, _stage.CoinSpawns, _stage.BankSpots);
            for (int p = 0; p < playerCount; p++)
            {
                World.AddCharacter(settings.Characters[settings.Picks[p]], _stage.SpawnPosition(p, playerCount), _stage.SpawnFacing(p, playerCount));
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
                catch (InvalidTuningException ex)
                {
                    // 調整値やルールが不正な開始のメッセージ（違うビルドどうしなど）。捨てて、同じメッセージで毎フレーム止まらないようにする。
                    Debug.LogError($"OnlineSession: 試合の開始のメッセージの値が不正なので捨てました（相手 {e.Peer}）: {ex.Message}");
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
                    _lobbyNames[player] = string.Empty;
                    _lobbyCharacters[player] = 0;
                    _link.Send(e.Peer, NetMessages.Welcome(player), reliable: true);
                    Status = $"Hosting on port {Port}: {LobbyPlayerCount} player(s)";
                    BroadcastLobby();
                    break;

                case TransportLink.EventKind.Disconnected:
                    for (int p = 1; p < MaxPlayers; p++)
                    {
                        if (_playerPeer[p] == e.Peer)
                        {
                            _playerPeer[p] = -1;
                            _lobbyNames[p] = string.Empty;
                            _lobbyCharacters[p] = 0;
                            BroadcastLobby();
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
                    int inputSender = PlayerOfPeer(e.Peer);
                    if (inputSender > 0)
                    {
                        // そのクライアントが今どの試合にいるか（開始のメッセージを送り直すかどうかに使う）。
                        _peerMatch[inputSender] = NetMessages.InputMatch(e.Data);
                    }
                    if (State != SessionState.Running || NetMessages.InputMatch(e.Data) != MatchNumber)
                    {
                        // 試合の前か、前の試合のメッセージ（再戦の前に送られて遅れて届いた）。
                        return;
                    }
                    int target = NetMessages.InputTarget(e.Data);
                    if (target == LocalPlayer)
                    {
                        Exchange.Receive(Airerage.Core.Rollback.InputMessage.FromBytes(NetMessages.InputPayload(e.Data)));
                    }
                    else if (target > 0 && target < PlayerCount && _playerPeer[target] >= 0)
                    {
                        _link.Send(_playerPeer[target], e.Data, reliable: false);
                    }
                    break;

                case NetKind.Hello:
                    int sender = PlayerOfPeer(e.Peer);
                    if (sender > 0)
                    {
                        _lobbyNames[sender] = SanitizeName(NetMessages.ReadHello(e.Data));
                        BroadcastLobby();
                    }
                    break;

                case NetKind.Pick:
                    int picker = PlayerOfPeer(e.Peer);
                    if (picker > 0)
                    {
                        _lobbyCharacters[picker] = ClampCharacter(NetMessages.ReadPick(e.Data));
                        BroadcastLobby();
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
                            if (_localName.Length > 0)
                            {
                                _link.Send(0, NetMessages.Hello(_localName), reliable: true);
                            }
                            if (_localCharacter != 0)
                            {
                                _link.Send(0, NetMessages.Pick(_localCharacter), reliable: true);
                            }
                            Status = $"Joined as player {LocalPlayer + 1}. Waiting for the host to start";
                            break;
                        case NetKind.Lobby:
                            LobbySlot[] roster = NetMessages.ReadLobby(e.Data);
                            _clientRoster = EmptyRoster();
                            for (int p = 0; p < MaxPlayers && p < roster.Length; p++)
                            {
                                _clientRoster[p] = new LobbySlot(roster[p].Connected, SanitizeName(roster[p].Name), roster[p].Character);
                            }
                            break;
                        case NetKind.Refused:
                            State = SessionState.Ended;
                            Status = "The host refused (full or already playing)";
                            break;
                        case NetKind.Start:
                            NetMessages.ReadStart(e.Data, out int count, out int match, out string json);
                            if (State == SessionState.Running && match == MatchNumber)
                            {
                                // 送り直された、今の試合の開始のメッセージ（もう始めている）。
                                break;
                            }
                            Begin(count, match, SettingsFromJson(json, count));
                            break;
                        case NetKind.Input:
                            if (State == SessionState.Running && NetMessages.InputMatch(e.Data) == MatchNumber
                                && NetMessages.InputTarget(e.Data) == LocalPlayer)
                            {
                                Exchange.Receive(Airerage.Core.Rollback.InputMessage.FromBytes(NetMessages.InputPayload(e.Data)));
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

        private static StartSettings SettingsFromJson(string json, int playerCount)
        {
            var settings = new StartSettings { Match = new MatchRules() };
            JsonUtility.FromJsonOverwrite(json, settings);
            if (settings.Characters == null || settings.Characters.Length == 0)
            {
                throw new FormatException("開始のメッセージにキャラクターの調整値がありません");
            }
            foreach (CharacterConfig config in settings.Characters)
            {
                if (config == null)
                {
                    throw new FormatException("開始のメッセージのキャラクターの調整値が空です");
                }
                config.Validate();
            }
            if (settings.Picks == null || settings.Picks.Length < playerCount || settings.CharacterIds == null || settings.CharacterIds.Length < playerCount)
            {
                throw new FormatException($"開始のメッセージのキャラクターの選択が人数（{playerCount}）より少ない");
            }
            for (int p = 0; p < playerCount; p++)
            {
                if (settings.Picks[p] < 0 || settings.Picks[p] >= settings.Characters.Length)
                {
                    throw new FormatException($"開始のメッセージのプレイヤー {p + 1} のキャラクターが範囲の外（{settings.Picks[p]}）");
                }
            }
            settings.Match.Validate().ThrowIfInvalid();
            return settings;
        }

        /// <summary>
        /// 試合の開始のときに、ホストが全員へ送る値（使うキャラクターの調整値、各プレイヤーのキャラクター、試合のルール、プレイヤーネーム）。
        /// </summary>
        [Serializable]
        private sealed class StartSettings
        {
            /// <summary>使うキャラクターの調整値（重ねない）。</summary>
            public CharacterConfig[] Characters;

            /// <summary>プレイヤーごとの、Characters の番号。</summary>
            public int[] Picks;

            /// <summary>プレイヤーごとの、キャラクターの番号（CharacterRoster の番号。表示に使う）。</summary>
            public int[] CharacterIds;

            public MatchRules Match;
            public string[] Names;
        }

        // 接続の番号 → プレイヤーの番号（いなければ -1）。
        private int PlayerOfPeer(int peer)
        {
            for (int p = 1; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] == peer)
                {
                    return p;
                }
            }
            return -1;
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
