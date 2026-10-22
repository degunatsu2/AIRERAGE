using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Unity.Networking.Transport.Relay;
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
    /// - ロビーの段階（docs/15 T6-7）: 待機所（Waiting）→ 2 人以上が全員準備完了（LocalReady）でキャラクター選択 → 全員が決定で
    ///   ステージ選択（ホストが StageCursor を動かして StartMatch(rules, stage)）。段階・準備完了・ステージの番号はロビーのメッセージで
    ///   全員へ送る。段階が変わると全員の準備完了を外す。待機所の段階でだけ参加できる。2 人を下回ったら待機所へ戻る。
    ///   準備完了を使わなければ段階は待機所のまま（検証用のシーン Sandbox_Online は、ホストが待機所から StartMatch する）。
    /// - ステージを選ぶ流れ（GameFlow から）では、セッションを作るときにステージがない。開始のメッセージにステージの番号を入れ、
    ///   全員がそのステージのシーンを読み込んでから BeginPending で始める（それまでは Loading）。今と同じステージなら（再戦）すぐ始める。
    /// - 試合の後にホストが ReturnToLobby を呼ぶと、全員がロビー（キャラクター選択など）へ戻る。
    /// - 試合中にクライアントが抜けたら（docs/15 T6-10）: 残りが 2 人以上なら、ホストがその人の入力で自分が見た（中継した）最後の次の
    ///   ティックを抜けたティックに決め、まだだれかに届いていないかもしれない入力といっしょに、抜けた知らせ（Left）を確実な経路で全員へ
    ///   送る。全員がそのティックからその人の入力を「抜けた」（PlayerInput.Departed）で確定するので、結果はそろう。抜けた人のキャラクターは
    ///   見えず何にも触れない（CombatState.HasLeft）。1 人だけになったら試合を終える。ホストが抜けたら試合は終わる。
    ///   再戦では番号を詰めて、今いる人で始め直す。
    /// </summary>
    public sealed class OnlineSession : IDisposable
    {
        public enum SessionState
        {
            Connecting,
            Lobby,

            /// <summary>試合の開始のメッセージを受けて、ステージを読み込んでいる（BeginPending で Running）。</summary>
            Loading,
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
        private OnlineStage _stage;
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

        // ホスト: プレイヤーの番号 → 準備完了か（docs/15 T6-7）。
        private readonly bool[] _lobbyReady = new bool[MaxPlayers];
        private LobbyPhase _phase = LobbyPhase.Waiting;
        private int _stageCursor;

        // クライアント: ホストから届いたロビーの段階・ステージの番号と、自分の準備完了。
        private LobbyPhase _clientPhase = LobbyPhase.Waiting;
        private int _clientStage;
        private bool _localReady;

        // ステージを読み込んでから始める試合（なければ null）。
        private PendingStart _pending;

        // ホスト: 送り直すかもしれない開始のメッセージ（全員が始めたら null）と、次に送り直すまでの秒数。
        private byte[] _startMessage;
        private float _startResendTimer;
        private string _localName = string.Empty;
        private int _localCharacter;

        // クライアント: ホストから届いたロビーの顔ぶれ（まだ届いていなければ、全員いない）。
        private LobbySlot[] _clientRoster = EmptyRoster();

        // ホスト: ティック → プレイヤーごとのチェックサム（全員の分を比べたら消す）。
        private readonly Dictionary<int, ChecksumRow> _checksums = new Dictionary<int, ChecksumRow>();

        // ホスト: 試合中にクライアントから届いた入力（自分宛ても、中継したものも。プレイヤーごとにティック → 入力）と、途切れずにそろった
        // 最後のティックと、まだ残している最も古いティック。抜けた人の入力を、だれかに届いたかもしれない分まで知るため（docs/15 T6-10）。
        // ホストはすべての入力を中継するので、ここにない入力はだれにも届いていない。
        private readonly Dictionary<int, PlayerInput>[] _seenInputs = new Dictionary<int, PlayerInput>[MaxPlayers];
        private readonly int[] _seenContiguous = new int[MaxPlayers];
        private readonly int[] _seenOldest = new int[MaxPlayers];

        // ホスト: [宛先, 送り手] → 送り手が宛先の入力をどこまで受け取ったか（中継したメッセージの確認応答）。
        private readonly int[,] _seenAck = new int[MaxPlayers, MaxPlayers];

        // 試合から抜けた人（番号ごと。試合の始めに戻す）。
        private readonly bool[] _departed = new bool[MaxPlayers];

        // クライアント: ステージを読み込んでいる間に届いた、抜けた知らせ（始めてから当てる）。
        private readonly List<byte[]> _pendingLeaves = new List<byte[]>();

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
                _seenInputs[p] = new Dictionary<int, PlayerInput>();
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

        /// <summary>ロビーの段階（ホストは今の値、クライアントはホストから最後に届いた値）。</summary>
        public LobbyPhase Phase => IsHost ? _phase : _clientPhase;

        /// <summary>
        /// ステージ選択でホストが選んでいるステージの番号（StageCatalog の番号）。ホストが変えると全員へ送る。クライアントはホストから
        /// 最後に届いた値。
        /// </summary>
        public int StageCursor
        {
            get => IsHost ? _stageCursor : _clientStage;
            set
            {
                if (!IsHost)
                {
                    throw new InvalidOperationException("ステージを選べるのはホストだけです");
                }
                int stage = Math.Max(0, Math.Min(byte.MaxValue, value));
                if (stage == _stageCursor)
                {
                    return;
                }
                _stageCursor = stage;
                BroadcastLobby();
            }
        }

        /// <summary>
        /// 自分が準備完了か（待機所の READY、キャラクター選択の決定）。クライアントはホストへ送る。段階が変わると外れる。
        /// </summary>
        public bool LocalReady
        {
            get => IsHost ? _lobbyReady[0] : _localReady;
            set
            {
                if (IsHost)
                {
                    if (_lobbyReady[0] == value)
                    {
                        return;
                    }
                    _lobbyReady[0] = value;
                    CheckLobbyProgress();
                    BroadcastLobby();
                }
                else
                {
                    _localReady = value;
                    if (LocalPlayer >= 0)
                    {
                        _link.Send(0, NetMessages.Ready(value), reliable: true);
                    }
                }
            }
        }

        /// <summary>今のステージの番号（StageCatalog の番号。ステージを選ぶ流れでなければ -1）。</summary>
        public int StageIndex { get; private set; } = -1;

        /// <summary>ステージを読み込んでから始める試合があるか（Loading の間）と、そのステージの番号。</summary>
        public bool HasPendingStart => _pending != null;
        public int PendingStage => _pending != null ? _pending.Settings.Stage : -1;

        /// <summary>今の試合の番号（始まるたびに 1 増える。0〜255 を繰り返す。始まるまで 0）。</summary>
        public int MatchNumber { get; private set; }

        /// <summary>ロビーにいるプレイヤーの数（ホストのみ。自分を含む）。</summary>
        /// <summary>プレイヤー player が試合から抜けたか（docs/15 T6-10。抜けた人のキャラクターは外れ、試合は続く）。</summary>
        public bool HasDeparted(int player)
        {
            return player >= 0 && player < MaxPlayers && _departed[player];
        }

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
                slots[0] = new LobbySlot(true, _localName, _lobbyCharacters[0], _lobbyReady[0]);
                for (int p = 1; p < MaxPlayers; p++)
                {
                    bool connected = _playerPeer[p] >= 0;
                    slots[p] = new LobbySlot(connected, connected ? _lobbyNames[p] : string.Empty, connected ? _lobbyCharacters[p] : 0,
                        connected && _lobbyReady[p]);
                }
                return slots;
            }
        }

        public ushort Port => _link.LocalPort;

        /// <summary>Relay を通してつないでいるときの参加コード（直接つないでいるときは空。docs/15 T6-10）。</summary>
        public string JoinCode { get; private set; } = string.Empty;

        /// <summary>Relay を通してつないでいるか。</summary>
        public bool UsesRelay => _link.UsesRelay;

        // ホストの状態の文の頭（参加コードか、待ち受けているポート）。
        private string HostingLabel => JoinCode.Length > 0 ? $"Hosting online (code {JoinCode})" : $"Hosting on port {Port}";
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
            return HostWith(port, stage, characters, settings);
        }

        private static OnlineSession HostWith(ushort port, OnlineStage stage, IReadOnlyList<CharacterConfig> characters, RollbackSettings settings)
        {
            OnlineSession session = HostOver(() => TransportLink.Host(port), stage, characters, settings);
            session.Status = $"Hosting on port {session.Port}";
            return session;
        }

        /// <summary>
        /// ホストとして Relay の割り当て allocation で待ち受ける（参加コード。docs/15 T6-10）。ステージは試合を始めるときに選ぶ。
        /// </summary>
        public static OnlineSession HostRelay(RelayHostAllocation allocation, IReadOnlyList<CharacterConfig> characters, RollbackSettings settings)
        {
            OnlineSession session = HostOver(() => TransportLink.HostRelay(allocation.ServerData), null, characters, settings);
            session.JoinCode = allocation.JoinCode;
            session.Status = $"Hosting online. Join code: {allocation.JoinCode}";
            return session;
        }

        /// <summary>参加コード joinCode から加わった Relay の割り当て relay で、ホストに参加する（docs/15 T6-10）。</summary>
        public static OnlineSession JoinRelay(RelayServerData relay, string joinCode, RollbackSettings settings)
        {
            var session = new OnlineSession(TransportLink.ConnectRelay(relay), null, settings, null)
            {
                State = SessionState.Connecting,
                JoinCode = joinCode,
            };
            session.Status = $"Connecting with code {joinCode}";
            return session;
        }

        // 選べるキャラクターを確かめてから、link を開いてホストの接続を作る。
        private static OnlineSession HostOver(Func<TransportLink> link, OnlineStage stage, IReadOnlyList<CharacterConfig> characters,
            RollbackSettings settings)
        {
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
            return new OnlineSession(link(), stage, settings, characters)
            {
                LocalPlayer = 0,
                State = SessionState.Lobby,
            };
        }

        /// <summary>
        /// ホストとして待ち受ける（ステージを選ぶ流れ。docs/15 T6-7）。ステージは試合を始めるときに選び、読み込んでから BeginPending で
        /// 渡す。characters は選べるキャラクターの調整値（番号の順）。
        /// </summary>
        public static OnlineSession Host(ushort port, IReadOnlyList<CharacterConfig> characters, RollbackSettings settings)
        {
            return HostWith(port, null, characters, settings);
        }

        /// <summary>address:port のホストに参加する（ステージを選ぶ流れ。ステージは開始のメッセージの番号で読み込む）。</summary>
        public static OnlineSession Join(string address, ushort port, RollbackSettings settings)
        {
            var session = new OnlineSession(TransportLink.Connect(address, port), null, settings, null)
            {
                State = SessionState.Connecting,
            };
            session.Status = $"Connecting to {address}:{port}";
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
        /// stage はステージの番号（StageCatalog の番号。ステージを選ぶ流れ。-1 なら作ったときのステージ）。今と違うステージなら、
        /// 全員が読み込んで BeginPending を呼ぶまで Loading。
        /// </summary>
        public void StartMatch(MatchRules rules = null, int stage = -1)
        {
            if (!IsHost || State != SessionState.Lobby)
            {
                throw new InvalidOperationException("試合を始められるのは、ロビーにいるホストだけです");
            }
            CloseUpPlayers();
            SendStartAndBegin(LobbyPlayerCount, rules, stage);
        }

        /// <summary>ホスト: ステージ選択からキャラクター選択へ戻す（全員の決定を外す）。</summary>
        public void BackToCharacterSelect()
        {
            if (!IsHost || State != SessionState.Lobby)
            {
                throw new InvalidOperationException("ステージ選択から戻せるのは、ロビーにいるホストだけです");
            }
            SetPhase(LobbyPhase.CharacterSelect);
            BroadcastLobby();
        }

        /// <summary>
        /// ホスト: 試合の後に、全員でロビーの phase の段階（キャラクター選択など）へ戻る（試合の結果の「キャラクター選択へ」。T6-8）。
        /// 試合のワールドを捨て、全員の準備完了を外す。
        /// </summary>
        public void ReturnToLobby(LobbyPhase phase)
        {
            if (!IsHost || (State != SessionState.Running && State != SessionState.Loading && State != SessionState.Lobby))
            {
                throw new InvalidOperationException("ロビーへ戻せるのは、試合中のホストだけです");
            }
            EnterLobby();
            _startMessage = null;
            SetPhase(phase);
            BroadcastLobby();
        }

        // ロビーへ戻る（試合のワールドを捨てる）。
        private void EnterLobby()
        {
            State = SessionState.Lobby;
            _pending = null;
            World = null;
            Session = null;
            Exchange = null;
            PlayerCount = 0;
            _previous = new CharacterState[0];
            Status = IsHost ? $"{HostingLabel}: {LobbyPlayerCount} player(s)" : $"Joined as player {LocalPlayer + 1}";
        }

        // ホスト: ロビーの段階を変える（全員の準備完了を外す）。
        private void SetPhase(LobbyPhase phase)
        {
            _phase = phase;
            for (int p = 0; p < MaxPlayers; p++)
            {
                _lobbyReady[p] = false;
            }
        }

        // ホスト: 2 人以上が全員準備完了なら次の段階へ（ステージ選択はホストが選ぶので進めない）。2 人を下回ったら待機所へ戻す。
        private void CheckLobbyProgress()
        {
            if (!IsHost || State != SessionState.Lobby)
            {
                return;
            }
            int players = LobbyPlayerCount;
            if (_phase != LobbyPhase.Waiting && players < 2)
            {
                SetPhase(LobbyPhase.Waiting);
                return;
            }
            if (players < 2 || _phase == LobbyPhase.StageSelect)
            {
                return;
            }
            for (int p = 0; p < MaxPlayers; p++)
            {
                bool present = p == 0 || _playerPeer[p] >= 0;
                if (present && !_lobbyReady[p])
                {
                    return;
                }
            }
            SetPhase(_phase == LobbyPhase.Waiting ? LobbyPhase.CharacterSelect : LobbyPhase.StageSelect);
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
                    _lobbyReady[next] = _lobbyReady[p];
                    _playerPeer[p] = -1;
                    _lobbyNames[p] = string.Empty;
                    _lobbyCharacters[p] = 0;
                    _lobbyReady[p] = false;
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
            byte[] message = NetMessages.Lobby(Roster, _phase, _stageCursor);
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
            // 試合中に抜けた人がいれば、番号を詰めて、今いる人で始め直す。
            CloseUpPlayers();
            SendStartAndBegin(LobbyPlayerCount, rules, StageIndex);
        }

        private void SendStartAndBegin(int count, MatchRules rules, int stage)
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
                Characters = configs, Picks = picks, CharacterIds = ids, Match = rules, Names = names, Stage = stage,
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
            BeginOrWait(count, match, SettingsFromJson(json, count));
        }

        // 今のステージで始められるなら始め、違うステージなら読み込むまで待つ（Loading。BeginPending で始める）。
        private void BeginOrWait(int count, int match, StartSettings settings)
        {
            if (_stage != null && (settings.Stage < 0 || settings.Stage == StageIndex))
            {
                _pending = null;
                Begin(count, match, settings);
                return;
            }
            _pending = new PendingStart { Count = count, Match = match, Settings = settings };
            State = SessionState.Loading;
            Status = $"Loading stage {settings.Stage}";
        }

        /// <summary>
        /// 読み込んだステージ stage（番号 stageIndex）で、待っていた試合を始める（ステージを選ぶ流れ。試合の仕組みのシーンが読み込まれたら
        /// 呼ぶ）。待っている試合がなければ InvalidOperationException。
        /// </summary>
        public void BeginPending(OnlineStage stage, int stageIndex)
        {
            if (_pending == null)
            {
                throw new InvalidOperationException("ステージを待っている試合がありません");
            }
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
            StageIndex = stageIndex;
            PendingStart pending = _pending;
            _pending = null;
            Begin(pending.Count, pending.Match, pending.Settings);
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
                // インバータ（SOLDERER）の間は、ワイヤーを押している間の照準も送る（鞭の向き）。
                bool keepWireAim = LocalPlayer >= 0 && LocalPlayer < PlayerCount && World.GetState(LocalPlayer).Combat.InverterOn;
                Exchange.AdvanceFrame(localInput(Session.LocalInputTick), keepWireAim);
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
                if (p == LocalPlayer || _departed[p])
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
            int remotes = 0;
            for (int p = 1; p < PlayerCount; p++)
            {
                remotes += _departed[p] ? 0 : 1;
            }
            if (row.Compared >= remotes)
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
            for (int p = 0; p < MaxPlayers; p++)
            {
                _departed[p] = false;
                _seenInputs[p].Clear();
                _seenContiguous[p] = Session.FirstInputTick - 1;
                _seenOldest[p] = Session.FirstInputTick;
                for (int q = 0; q < MaxPlayers; q++)
                {
                    _seenAck[p, q] = Session.FirstInputTick - 1;
                }
            }
            _previous = new CharacterState[playerCount];
            _corrections = new NVector3[playerCount];
            _beforeReconcile = new NVector3[playerCount];
            for (int p = 0; p < playerCount; p++)
            {
                _previous[p] = World.GetState(p);
            }
            State = SessionState.Running;
            Status = $"Running: player {LocalPlayer + 1} of {playerCount}";
            // ステージを読み込んでいる間に届いた、この試合の抜けた知らせ。
            foreach (byte[] leave in _pendingLeaves)
            {
                NetMessages.ReadLeft(leave, out int leftMatch, out int player, out int fromTick, out int startTick, out PlayerInput[] inputs);
                if (leftMatch == match)
                {
                    ApplyDeparture(player, fromTick, startTick, inputs);
                }
            }
            _pendingLeaves.Clear();
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
                    // 参加できるのは、ロビーの待機所の段階だけ（キャラクター選択の途中からは入れない）。
                    if (State != SessionState.Lobby || _phase != LobbyPhase.Waiting || player < 0)
                    {
                        _link.Send(e.Peer, NetMessages.Refused(), reliable: true);
                        _link.Disconnect(e.Peer);
                        return;
                    }
                    _playerPeer[player] = e.Peer;
                    _lobbyNames[player] = string.Empty;
                    _lobbyCharacters[player] = 0;
                    _lobbyReady[player] = false;
                    _link.Send(e.Peer, NetMessages.Welcome(player), reliable: true);
                    Status = $"{HostingLabel}: {LobbyPlayerCount} player(s)";
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
                            _lobbyReady[p] = false;
                            CheckLobbyProgress();
                            BroadcastLobby();
                            // 試合中に抜けた人は外して、残りの 2 人以上で続ける（docs/15 T6-10）。
                            bool goesOn = State == SessionState.Running && p < PlayerCount && LobbyPlayerCount >= 2 && RemoveDeparted(p);
                            if (!goesOn && (State == SessionState.Running || State == SessionState.Loading))
                            {
                                // 1 人だけになったか、外せなかったら、試合を終える（残りの人も止まったままにしないよう、接続を切る）。
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
                    if (inputSender <= 0 || inputSender >= PlayerCount || _departed[inputSender] || target < 0 || target >= PlayerCount)
                    {
                        return;
                    }
                    Airerage.Core.Rollback.InputMessage message = Airerage.Core.Rollback.InputMessage.FromBytes(NetMessages.InputPayload(e.Data));
                    RecordSeen(inputSender, target, message);
                    if (target == LocalPlayer)
                    {
                        Exchange.Receive(message);
                    }
                    else if (target > 0 && _playerPeer[target] >= 0)
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

                case NetKind.Ready:
                    int readier = PlayerOfPeer(e.Peer);
                    if (readier > 0 && State == SessionState.Lobby)
                    {
                        _lobbyReady[readier] = NetMessages.ReadReady(e.Data);
                        CheckLobbyProgress();
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
                            LobbySlot[] roster = NetMessages.ReadLobby(e.Data, out LobbyPhase phase, out int stage);
                            _clientRoster = EmptyRoster();
                            for (int p = 0; p < MaxPlayers && p < roster.Length; p++)
                            {
                                _clientRoster[p] = new LobbySlot(roster[p].Connected, SanitizeName(roster[p].Name), roster[p].Character,
                                    roster[p].Ready);
                            }
                            _clientPhase = phase;
                            _clientStage = stage;
                            if (LocalPlayer >= 0 && LocalPlayer < MaxPlayers)
                            {
                                // 段階が変わるとホストが準備完了を外す（自分の値もそろえる）。
                                _localReady = _clientRoster[LocalPlayer].Ready;
                            }
                            if (State == SessionState.Running || State == SessionState.Loading)
                            {
                                // ホストが試合の後にロビーへ戻した（ロビーのメッセージはロビーでしか送られない）。
                                EnterLobby();
                            }
                            break;
                        case NetKind.Refused:
                            State = SessionState.Ended;
                            Status = "The host refused (full or already playing)";
                            break;
                        case NetKind.Start:
                            NetMessages.ReadStart(e.Data, out int count, out int match, out string json);
                            if ((State == SessionState.Running && match == MatchNumber)
                                || (State == SessionState.Loading && _pending != null && match == _pending.Match))
                            {
                                // 送り直された、今の試合の開始のメッセージ（もう始めているか、ステージを読み込んでいる）。
                                break;
                            }
                            BeginOrWait(count, match, SettingsFromJson(json, count));
                            break;
                        case NetKind.Input:
                            if (State == SessionState.Running && NetMessages.InputMatch(e.Data) == MatchNumber
                                && NetMessages.InputTarget(e.Data) == LocalPlayer)
                            {
                                Exchange.Receive(Airerage.Core.Rollback.InputMessage.FromBytes(NetMessages.InputPayload(e.Data)));
                            }
                            break;
                        case NetKind.Left:
                            NetMessages.ReadLeft(e.Data, out int leftMatch, out int leaver, out int fromTick, out int startTick,
                                out PlayerInput[] leftInputs);
                            if (State == SessionState.Running && leftMatch == MatchNumber)
                            {
                                ApplyDeparture(leaver, fromTick, startTick, leftInputs);
                            }
                            else if (State == SessionState.Loading && _pending != null && leftMatch == _pending.Match)
                            {
                                _pendingLeaves.Add(e.Data);
                            }
                            break;
                    }
                    break;
            }
        }

        // ホスト: クライアント sender から宛先 target へのメッセージの入力と確認応答を記録する（抜けた人の入力を知るため）。
        // 全員に届いたことが確認された古い入力は捨てる。
        private void RecordSeen(int sender, int target, Airerage.Core.Rollback.InputMessage message)
        {
            Dictionary<int, PlayerInput> seen = _seenInputs[sender];
            for (int i = 0; i < message.Inputs.Count; i++)
            {
                int tick = message.StartTick + i;
                if (tick > _seenContiguous[sender] && !seen.ContainsKey(tick))
                {
                    seen[tick] = message.Inputs[i];
                }
            }
            while (seen.ContainsKey(_seenContiguous[sender] + 1))
            {
                _seenContiguous[sender]++;
            }
            _seenAck[target, sender] = Math.Max(_seenAck[target, sender], message.Ack);
            int keepFrom = OldestNeeded(sender);
            while (_seenOldest[sender] < keepFrom)
            {
                seen.Remove(_seenOldest[sender]);
                _seenOldest[sender]++;
            }
        }

        // ホスト: プレイヤー player の入力のうち、まだだれかに届いていないかもしれない最も古いティック（自分と、今いるほかの人の確認応答の最小の次）。
        private int OldestNeeded(int player)
        {
            int oldest = Session.ContiguousTick(player) + 1;
            for (int r = 1; r < PlayerCount; r++)
            {
                if (r != player && !_departed[r] && _playerPeer[r] >= 0)
                {
                    oldest = Math.Min(oldest, _seenAck[player, r] + 1);
                }
            }
            return Math.Max(oldest, Session.FirstInputTick);
        }

        // ホスト: 試合中に抜けたプレイヤー player を外す。抜けたティックは、その人の入力でホストが見た（中継した）最後の次。そこまでの入力の
        // うち、まだだれかに届いていないかもしれない分を、抜けた知らせに入れて全員へ確実に送る。外せなければ false（試合を終える）。
        private bool RemoveDeparted(int player)
        {
            int fromTick = Math.Max(_seenContiguous[player] + 1, Session.FirstInputTick);
            int start = OldestNeeded(player);
            var inputs = new List<PlayerInput>();
            for (int tick = start; tick < fromTick; tick++)
            {
                if (!_seenInputs[player].TryGetValue(tick, out PlayerInput input))
                {
                    Debug.LogError($"OnlineSession: 抜けたプレイヤー {player + 1} のティック {tick} の入力が残っていないので、試合を終えます");
                    return false;
                }
                inputs.Add(input);
            }
            if (inputs.Count > NetMessages.MaxLeftInputs)
            {
                Debug.LogError($"OnlineSession: 抜けたプレイヤー {player + 1} の送る入力が多すぎる（{inputs.Count} 個）ので、試合を終えます");
                return false;
            }
            byte[] message = NetMessages.Left(MatchNumber, player, fromTick, start, inputs);
            ApplyDeparture(player, fromTick, start, inputs);
            for (int r = 1; r < MaxPlayers; r++)
            {
                if (_playerPeer[r] >= 0)
                {
                    _link.Send(_playerPeer[r], message, reliable: true);
                }
            }
            return true;
        }

        // 抜けた人を外す（ホストもクライアントも同じ。InputExchange.RemovePlayer）。
        private void ApplyDeparture(int player, int fromTick, int startTick, IReadOnlyList<PlayerInput> inputs)
        {
            if (player <= 0 || player >= PlayerCount || player == LocalPlayer || _departed[player])
            {
                Debug.LogWarning($"OnlineSession: 抜けた知らせのプレイヤーの番号がおかしいので無視しました（{player}）");
                return;
            }
            Exchange.RemovePlayer(player, fromTick, startTick, inputs);
            _departed[player] = true;
            Status = $"Player {player + 1} left. The match goes on";
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

            /// <summary>ステージの番号（StageCatalog の番号。ステージを選ぶ流れでなければ -1）。</summary>
            public int Stage = -1;
        }

        // ステージを読み込んでから始める試合。
        private sealed class PendingStart
        {
            public int Count;
            public int Match;
            public StartSettings Settings;
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
