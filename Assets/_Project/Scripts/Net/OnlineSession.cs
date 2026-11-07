using System;
using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Unity.Networking.Transport.Relay;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;
using Airerage.Runtime.Settings;

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

        /// <summary>1 つのセッションに入れる人数（チーム対戦は 8 人まで。docs/19 T9-4）。</summary>
        public const int MaxPlayers = 8;

        /// <summary>個人戦（チームなし）で始められる人数の上限（今までと同じ 4 人。docs/19）。</summary>
        public const int MaxFreeForAllPlayers = 4;

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

        // ホスト: プレイヤーの番号 → RANDOM で選んでよいステージの番号のビット（NetKind.StagePool。届くまではすべて）。
        private readonly ulong[] _lobbyStagePools = new ulong[MaxPlayers];
        private ulong _localStagePool = ulong.MaxValue;

        // ホスト: プレイヤーの番号 → そのクライアントから最後に届いた入力の試合の番号（まだなら -1）。
        private readonly int[] _peerMatch = new int[MaxPlayers];

        // ホスト: プレイヤーの番号 → 準備完了か（docs/15 T6-7）。
        private readonly bool[] _lobbyReady = new bool[MaxPlayers];

        // ホスト: チーム対戦か（待機所でホストが決める）と、プレイヤーの番号 → チームの番号（1〜4。チーム対戦でなければ 0。docs/19 T9-5）。
        private bool _teamMode;
        private readonly byte[] _lobbyTeams = new byte[MaxPlayers];

        // クライアント: ホストから届いた、チーム対戦か。
        private bool _clientTeamMode;

        // ホスト: アイテムのルール（待機所でホストが決める）と、プレイヤーの番号 → プリアイテムで持って始めるアイテム（NetKind.Items）・
        // アイテムを選ぶ画面を開いているか。クライアント: ホストから届いたアイテムのルール。自分の値（docs/20_item_plan.md）。
        private LobbyItems _items = LobbyItems.Default;
        private LobbyItems _clientItems = LobbyItems.Default;
        private readonly Airerage.Core.Items.ItemId[][] _lobbyStartItems = new Airerage.Core.Items.ItemId[MaxPlayers][];
        private readonly bool[] _lobbyItemSelecting = new bool[MaxPlayers];
        private Airerage.Core.Items.ItemId[] _localStartItems = new Airerage.Core.Items.ItemId[0];
        private bool _localItemSelecting;

        // ホスト: プレイヤーの番号 → 観戦の待機所にいるか（試合に参加せず見るだけ。docs/19。ホスト自身も観戦にできる。2026-11-03）。
        private readonly bool[] _lobbySpectators = new bool[MaxPlayers];

        // ホストの番号（ロビーの番号。初めは 0。ホストが観戦するときは、参加する人の後ろへ詰める）と、クライアントが知っているホストの番号。
        private int _hostSlot;
        private int _clientHostSlot;

        // 最初に入力を使うティック（試合の初め。ホストが観戦していると巻き戻しのセッションがないので、ここに覚える）。
        private int _firstInputTick;

        // ホストが観戦: 自分のワールドへ渡した最後のティック（中継した全員の入力が届いたティックまで、順に渡す）と、抜けた人の抜けたティック。
        private int _hostFedTick;
        private readonly int[] _departFrom = new int[MaxPlayers];

        // ホスト: 途中から入って、今の選択・試合が終わるのを待っている人（2026-11-03）と、入ったことをもう知らせたか。
        private readonly bool[] _lobbyLate = new bool[MaxPlayers];
        private readonly bool[] _lateAnnounced = new bool[MaxPlayers];

        // クライアント: ホストから届いた、試合中か（途中から入った人の待機所の表示に使う）。
        private bool _clientMatchRunning;

        // 途中から入った人の知らせ（名前と、知らせを受けた時刻。画面の左下に少しの間だけ出す）と、Update で進める時計（秒）。
        private readonly List<JoinNotice> _joinNotices = new List<JoinNotice>();
        private float _clock;
        // キャラクター選択で全員が決めた時刻（ホスト。全員そろった帯を見せてからステージ選択へ進む。D-89 の C17。まだなら -1）。
        private float _allReadySince = -1f;

        /// <summary>キャラクター選択で全員が決めてから、ステージ選択へ進むまでの秒数（全員そろった帯を見せる。D-89 の C17）。</summary>
        public const float AllReadyHoldSeconds = 1f;

        // 観戦の人: 確定した入力で進めるワールド（試合を見ている間だけ）。
        private SpectatorSession _spectator;

        // ホスト: 観戦の人へ送る、確定したティックの全員の入力（試合の初めから。観戦の人がいる試合だけ記録する）と、その最初のティック、
        // 観戦の人ごとの次に送るティック（まだ見始めていなければ -1）。
        private readonly List<PlayerInput[]> _confirmedLog = new List<PlayerInput[]>();
        private int _confirmedLogStart;
        private readonly int[] _spectatorNext = new int[MaxPlayers];
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
                _lobbyStagePools[p] = ulong.MaxValue;
                _spectatorNext[p] = -1;
            }
        }

        public bool IsHost => _link.IsHost;

        /// <summary>
        /// ホストの番号（ロビーの番号。初めは 0。ホストが観戦の待機所にいると、キャラクター選択に進むときに参加する人の後ろへ移る。
        /// 2026-11-03）。待機所の「(HOST)」の印に使う。
        /// </summary>
        public int HostSlot => IsHost ? _hostSlot : _clientHostSlot;
        public SessionState State { get; private set; }

        /// <summary>自分のプレイヤーの番号（クライアントは Welcome が届くまで -1）。</summary>
        public int LocalPlayer { get; private set; } = -1;

        /// <summary>試合の人数（始まるまで 0）。</summary>
        public int PlayerCount { get; private set; }

        /// <summary>試合のプレイヤーネーム（番号の順。入力がなかった人は空。始まるまで空の配列）。</summary>
        public IReadOnlyList<string> PlayerNames { get; private set; } = Array.Empty<string>();

        /// <summary>試合で各プレイヤーが使うキャラクターの番号（番号の順。CharacterRoster の番号。始まるまで空の配列。表示に使う）。</summary>
        public IReadOnlyList<int> PlayerCharacters { get; private set; } = Array.Empty<int>();

        /// <summary>試合の各プレイヤーのチーム（番号の順。1〜4。個人戦なら 0。始まるまで空の配列。docs/19）。</summary>
        public IReadOnlyList<byte> PlayerTeams { get; private set; } = Array.Empty<byte>();

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
                    _lobbyCharacters[_hostSlot] = ClampCharacter(character);
                    BroadcastLobby();
                }
                else if (LocalPlayer >= 0)
                {
                    _link.Send(0, NetMessages.Pick(character), reliable: true);
                }
            }
        }

        /// <summary>
        /// 自分が RANDOM で選んでよいステージ（StageCatalog の番号のビット。オプションの RANDOM STAGES）。クライアントは、ロビーにいれば
        /// すぐにホストへ送る。
        /// </summary>
        public ulong LocalStagePool
        {
            get => _localStagePool;
            set
            {
                if (value == _localStagePool)
                {
                    return;
                }
                _localStagePool = value;
                if (!IsHost && LocalPlayer >= 0)
                {
                    _link.Send(0, NetMessages.StagePool(value), reliable: true);
                }
            }
        }

        /// <summary>ホスト: ロビーにいる全員が RANDOM で選んでよいステージ（全員のビットの共通部分。0 なら共通のステージがない）。</summary>
        public ulong CommonStagePool
        {
            get
            {
                ulong mask = _localStagePool;
                for (int p = 0; p < MaxPlayers; p++)
                {
                    if (_playerPeer[p] >= 0)
                    {
                        mask &= _lobbyStagePools[p];
                    }
                }
                return mask;
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
                    _lobbyNames[_hostSlot] = name;
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
            get => IsHost ? _lobbyReady[_hostSlot] : _localReady;
            set
            {
                if (IsHost)
                {
                    if (_lobbyReady[_hostSlot] == value)
                    {
                        return;
                    }
                    _lobbyReady[_hostSlot] = value;
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

        /// <summary>
        /// アイテムのルール（モード・初期アイテム数・ドロップ頻度・ドロップするアイテム。docs/20_item_plan.md）。待機所でホストが決める
        /// （クライアントはホストから最後に届いた値）。変えたら全員へ送る。
        /// </summary>
        public LobbyItems Items
        {
            get => IsHost ? _items : _clientItems;
            set
            {
                if (!IsHost)
                {
                    throw new InvalidOperationException("アイテムのルールを決められるのはホストだけです");
                }
                if (value.Equals(_items))
                {
                    return;
                }
                _items = value;
                if (State == SessionState.Lobby)
                {
                    BroadcastLobby();
                }
            }
        }

        /// <summary>自分がプリアイテムで持って始めるアイテム（最大 3 つ。Z・X・C の順）。変えたらホストへ送る。</summary>
        public IReadOnlyList<Airerage.Core.Items.ItemId> LocalStartItems
        {
            get => _localStartItems;
            set
            {
                _localStartItems = CleanItems(value);
                SendLocalItems();
            }
        }

        /// <summary>自分がアイテムを選ぶ画面を開いているか（待機所の「アイテム選択中……」）。変えたらホストへ送る。</summary>
        public bool LocalItemSelecting
        {
            get => _localItemSelecting;
            set
            {
                if (value == _localItemSelecting)
                {
                    return;
                }
                _localItemSelecting = value;
                SendLocalItems();
            }
        }

        // 自分のアイテムの選び方を、ホストへ送る（ホストは全員へ待機所の様子を送り直す）。
        private void SendLocalItems()
        {
            if (IsHost)
            {
                if (State == SessionState.Lobby)
                {
                    BroadcastLobby();
                }
            }
            else if (LocalPlayer >= 0)
            {
                _link.Send(0, NetMessages.Items(_localItemSelecting, _localStartItems), reliable: true);
            }
        }

        // アイテムの並びの、ないアイテムと重なりを除いて 3 つまでにする。
        private static Airerage.Core.Items.ItemId[] CleanItems(IReadOnlyList<Airerage.Core.Items.ItemId> items)
        {
            var clean = new List<Airerage.Core.Items.ItemId>();
            if (items != null && items.Count > 0 && items[0] == Airerage.Core.Items.ItemId.Random)
            {
                // ランダムセレクト（試合を始めるとき、全員が試合の種から同じアイテムを選ぶ。SimWorld.SetItems）。
                return new[] { Airerage.Core.Items.ItemId.Random };
            }
            for (int i = 0; items != null && i < items.Count && clean.Count < Airerage.Core.Items.ItemCatalog.BagSlots; i++)
            {
                if (Airerage.Core.Items.ItemCatalog.IsValid(items[i]) && !clean.Contains(items[i]))
                {
                    clean.Add(items[i]);
                }
            }
            return clean.ToArray();
        }

        // プレイヤー p がプリアイテムで持って始めるアイテム（ホスト）。
        private Airerage.Core.Items.ItemId[] StartItemsOf(int p)
        {
            return p == _hostSlot ? _localStartItems : _lobbyStartItems[p] ?? new Airerage.Core.Items.ItemId[0];
        }

        /// <summary>
        /// チーム対戦か（docs/19。待機所でホストが決める。クライアントはホストから最後に届いた値）。ホストが変えると全員の準備完了を外し、
        /// チーム対戦にするときは、チームのない人を人数の少ないチームへ順に入れる。待機所でだけ変えられる。
        /// </summary>
        public bool TeamMode
        {
            get => IsHost ? _teamMode : _clientTeamMode;
            set
            {
                if (!IsHost)
                {
                    throw new InvalidOperationException("チーム対戦にするかを決められるのはホストだけです");
                }
                if (State != SessionState.Lobby || _phase != LobbyPhase.Waiting || value == _teamMode)
                {
                    return;
                }
                _teamMode = value;
                for (int p = 0; p < MaxPlayers; p++)
                {
                    _lobbyTeams[p] = 0;
                }
                if (value)
                {
                    for (int p = 0; p < MaxPlayers; p++)
                    {
                        if (IsPresent(p) && !_lobbySpectators[p])
                        {
                            _lobbyTeams[p] = SmallestTeam();
                        }
                    }
                }
                SetPhase(LobbyPhase.Waiting);
                BroadcastLobby();
            }
        }

        /// <summary>自分のチーム（1〜4。チーム対戦でなければ 0）。クライアントはホストへ送る（待機所でだけ変えられる）。</summary>
        public int LocalTeam
        {
            get
            {
                IReadOnlyList<LobbySlot> roster = Roster;
                return LocalPlayer >= 0 && LocalPlayer < roster.Count ? roster[LocalPlayer].Team : 0;
            }
            set
            {
                if (IsHost)
                {
                    SetTeamOf(_hostSlot, value);
                }
                else if (LocalPlayer >= 0 && TeamMode && Phase == LobbyPhase.Waiting)
                {
                    _link.Send(0, NetMessages.Team(value), reliable: true);
                }
            }
        }

        /// <summary>
        /// 自分が観戦の待機所にいるか（docs/19。ユーザーの要望、2026-11-02）。観戦の人は試合に参加せず、見るだけ。クライアントはホストへ送る
        /// （待機所でだけ変えられる）。ホストは観戦にできない（いつも参加する）。
        /// </summary>
        public bool LocalSpectator
        {
            get
            {
                IReadOnlyList<LobbySlot> roster = Roster;
                return LocalPlayer >= 0 && LocalPlayer < roster.Count && roster[LocalPlayer].Spectator;
            }
            set
            {
                if (IsHost)
                {
                    // ホストも観戦にできる（大会で、開催者が参加者どうしの試合を見る。2026-11-03 のユーザーの要望）。観戦していても、
                    // 全員の入力の中継とずれの判定はホストが受け持つ。
                    SetSpectatorOf(_hostSlot, value);
                    return;
                }
                if (LocalPlayer >= 0 && Phase == LobbyPhase.Waiting)
                {
                    _link.Send(0, NetMessages.Spectate(value), reliable: true);
                }
            }
        }

        /// <summary>
        /// ホスト: プレイヤー player を観戦の待機所へ入れる（spectate）か、出して参加させる（待機所でだけ。ホスト自身も観戦にできる）。
        /// 観戦に入ると準備完了とチームが外れ、参加に戻るとチーム対戦なら人数の少ないチームに入る。
        /// </summary>
        public void SetSpectatorOf(int player, bool spectate)
        {
            if (!IsHost)
            {
                throw new InvalidOperationException("ほかの人を観戦にできるのはホストだけです");
            }
            if (State != SessionState.Lobby || _phase != LobbyPhase.Waiting || player < 0 || !IsPresent(player)
                || _lobbySpectators[player] == spectate)
            {
                return;
            }
            _lobbySpectators[player] = spectate;
            _lobbyReady[player] = false;
            _lobbyTeams[player] = 0;
            if (!spectate && _teamMode)
            {
                _lobbyTeams[player] = SmallestTeam();
            }
            CheckLobbyProgress();
            BroadcastLobby();
        }

        /// <summary>試合に参加する人の数（ロビーで。観戦の人を除く。ホストを含む）。</summary>
        public int LobbyParticipantCount
        {
            get
            {
                int count = 0;
                for (int p = 0; p < MaxPlayers; p++)
                {
                    count += IsPresent(p) && !SpectatorAt(p) && !LateAt(p) ? 1 : 0;
                }
                return count;
            }
        }

        // 番号 p の人が途中から入って待っているか（ホストは自分の表、クライアントは届いた顔ぶれ）。
        private bool LateAt(int p)
        {
            return IsHost ? _lobbyLate[p] : p < _clientRoster.Length && _clientRoster[p].Late;
        }

        // 番号 p の人が観戦の待機所にいるか（ホストは自分の表、クライアントは届いた顔ぶれ）。
        private bool SpectatorAt(int p)
        {
            return IsHost ? _lobbySpectators[p] : p < _clientRoster.Length && _clientRoster[p].Spectator;
        }

        /// <summary>
        /// 自分が途中から入って、今の選択・試合が終わるのを待っているか（2026-11-03。キャラクター選択・ステージ選択・試合の途中に入った人）。
        /// 待機所で待ち、次の試合から参加する。
        /// </summary>
        public bool LocalLate
        {
            get
            {
                IReadOnlyList<LobbySlot> roster = Roster;
                return LocalPlayer >= 0 && LocalPlayer < roster.Count && roster[LocalPlayer].Late;
            }
        }

        /// <summary>
        /// 途中から部屋に入って、今の試合が終わるのを待っている人がいるか（結果の画面で REMATCH・CHARACTER SELECT を押せなくし、BACK ROOM で
        /// 待機所へ戻ってもらう。2026-11-03 のユーザーの要望）。
        /// </summary>
        public bool HasLateJoiners
        {
            get
            {
                IReadOnlyList<LobbySlot> roster = Roster;
                for (int p = 0; p < roster.Count; p++)
                {
                    if (roster[p].Connected && roster[p].Late)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>ホストが試合中か（ステージの読み込みを含む。ホストは今の値、クライアントはホストから最後に届いた値か、自分の状態）。</summary>
        public bool MatchRunning => IsHost || State != SessionState.Lobby
            ? State == SessionState.Running || State == SessionState.Loading
            : _clientMatchRunning;

        /// <summary>途中から人が入った知らせ（名前と、受けてからの秒数 Age）。JoinNotice.Seconds を過ぎたものは Update で消える。</summary>
        public IReadOnlyList<JoinNotice> JoinNotices => _joinNotices;

        /// <summary>途中から入った人の知らせ 1 つ。</summary>
        public readonly struct JoinNotice
        {
            /// <summary>知らせを出しておく秒数。</summary>
            public const float Seconds = 5f;

            public readonly string Name;

            /// <summary>知らせを受けた時刻（セッションの時計の秒）。</summary>
            public readonly float Received;

            /// <summary>受けてからの秒数（Update のたびに増える）。</summary>
            public readonly float Age;

            public JoinNotice(string name, float received, float age)
            {
                Name = name;
                Received = received;
                Age = age;
            }
        }

        private void AddJoinNotice(string name)
        {
            _joinNotices.Add(new JoinNotice(name, _clock, 0f));
        }

        private void AgeJoinNotices()
        {
            for (int i = _joinNotices.Count - 1; i >= 0; i--)
            {
                JoinNotice notice = _joinNotices[i];
                float age = _clock - notice.Received;
                if (age > JoinNotice.Seconds)
                {
                    _joinNotices.RemoveAt(i);
                    continue;
                }
                _joinNotices[i] = new JoinNotice(notice.Name, notice.Received, age);
            }
        }

        /// <summary>試合を観戦しているか（試合に参加せず、確定した入力で見ている。docs/19）。</summary>
        public bool IsSpectating => _spectator != null && World != null;

        /// <summary>観戦で、すぐに進められる届いた入力のティックの数（遅れの目安。観戦していなければ 0）。</summary>
        public int SpectatorBufferedTicks => _spectator != null ? _spectator.BufferedTicks : 0;

        /// <summary>ホスト: プレイヤー player をチーム team（1〜4）へ移す（D-78。待機所で、チーム対戦のときだけ）。</summary>
        public void SetTeamOf(int player, int team)
        {
            if (!IsHost)
            {
                throw new InvalidOperationException("ほかの人のチームを変えられるのはホストだけです");
            }
            if (State != SessionState.Lobby || _phase != LobbyPhase.Waiting || !_teamMode || !IsPresent(player)
                || team < 1 || team > TeamRules.MaxTeams || (_lobbyTeams[player] == team && !_lobbySpectators[player]))
            {
                return;
            }
            // 観戦の待機所からチームを選んだら、そのチームで参加する。
            if (_lobbySpectators[player])
            {
                _lobbySpectators[player] = false;
                _lobbyReady[player] = false;
            }
            _lobbyTeams[player] = (byte)team;
            CheckLobbyProgress();
            BroadcastLobby();
        }

        /// <summary>
        /// ホスト: 均等に分けるボタン（D-78。ユーザーの決定、2026-11-02）。今いる人を、人数配分 sizes（チームごとの人数。合計が人数と同じ。
        /// TeamRules.SizePatterns から選ぶ）のとおりに、種 seed でランダムにチームへ分ける。配分が合わなければ ArgumentException。
        /// </summary>
        public void SplitTeams(IReadOnlyList<int> sizes, int seed)
        {
            if (!IsHost)
            {
                throw new InvalidOperationException("チームを分けられるのはホストだけです");
            }
            if (State != SessionState.Lobby || _phase != LobbyPhase.Waiting || !_teamMode)
            {
                return;
            }
            var present = new List<int>();
            for (int p = 0; p < MaxPlayers; p++)
            {
                if (IsPresent(p) && !_lobbySpectators[p] && !_lobbyLate[p])
                {
                    present.Add(p);
                }
            }
            byte[] teams = TeamRules.Distribute(present.Count, sizes, seed);
            for (int i = 0; i < present.Count; i++)
            {
                _lobbyTeams[present[i]] = teams[i];
            }
            BroadcastLobby();
        }

        /// <summary>
        /// 待機所から進めない理由（なければ null。参加する人が 2 人に足りないときは null）: 個人戦で MaxFreeForAllPlayers 人より多い、
        /// チーム対戦でチームが 2 つに満たない（全員が同じチーム。D-78）。観戦の人は数えない。
        /// </summary>
        public string WaitingProblem => WaitingProblemOf(Roster, TeamMode);

        /// <summary>WaitingProblem の決まり（顔ぶれ roster と、チーム対戦か）。</summary>
        public static string WaitingProblemOf(IReadOnlyList<LobbySlot> roster, bool teamMode)
        {
            int players = 0;
            uint teams = 0;
            bool missing = false;
            foreach (LobbySlot slot in roster)
            {
                if (!slot.Plays)
                {
                    continue;
                }
                players++;
                if (slot.Team == 0)
                {
                    missing = true;
                }
                else
                {
                    teams |= 1u << slot.Team;
                }
            }
            if (players < 2)
            {
                return null;
            }
            if (!teamMode && players > MaxFreeForAllPlayers)
            {
                return $"FREE FOR ALL IS UP TO {MaxFreeForAllPlayers} PLAYERS. SWITCH TO TEAM MODE";
            }
            int teamCount = 0;
            for (uint bits = teams; bits != 0; bits &= bits - 1)
            {
                teamCount++;
            }
            if (teamMode && (missing || teamCount < 2))
            {
                return "MAKE AT LEAST 2 TEAMS";
            }
            return null;
        }

        // ホスト: 番号 p の人がロビーにいるか（HostSlot はホスト自身）。
        private bool IsPresent(int p)
        {
            return p == _hostSlot || (p >= 0 && p < MaxPlayers && _playerPeer[p] >= 0);
        }

        // ホスト: 人数のいちばん少ないチーム（同じなら番号の小さいほう。新しく入った人や、チーム対戦にしたとき）。
        private byte SmallestTeam()
        {
            var counts = new int[TeamRules.MaxTeams + 1];
            for (int p = 0; p < MaxPlayers; p++)
            {
                if (IsPresent(p) && !_lobbySpectators[p] && _lobbyTeams[p] != 0)
                {
                    counts[_lobbyTeams[p]]++;
                }
            }
            // 自動では 2 チームに分ける（3・4 チーム目は、各自かホストが選ぶか、均等に分けるボタンで使う）。
            return counts[2] < counts[1] ? (byte)2 : (byte)1;
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
                for (int p = 0; p < MaxPlayers; p++)
                {
                    bool connected = IsPresent(p);
                    bool spectator = connected && _lobbySpectators[p];
                    string name = p == _hostSlot ? _localName : connected ? _lobbyNames[p] : string.Empty;
                    slots[p] = new LobbySlot(connected, name, connected ? _lobbyCharacters[p] : 0,
                        connected && _lobbyReady[p], connected && _teamMode && !spectator ? _lobbyTeams[p] : (byte)0, spectator,
                        connected && _lobbyLate[p], connected && (p == _hostSlot ? _localItemSelecting : _lobbyItemSelecting[p]));
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
        // ホストの待機所の状態の文（待ち受けと人数）。
        private string PlayersLabel()
        {
            return Loc.T($"{HostingLabel}: {LobbyPlayerCount} player(s)", $"{HostingLabel}: {LobbyPlayerCount} 人");
        }

        private string HostingLabel => JoinCode.Length > 0
            ? Loc.T($"Hosting online (code {JoinCode})", $"オンラインで待ち受け中（参加コード {JoinCode}）")
            : Loc.T($"Hosting on port {Port}", $"ポート {Port} で待ち受け中");
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
            session.Status = Loc.T($"Hosting on port {session.Port}", $"ポート {session.Port} で待ち受け中");
            return session;
        }

        /// <summary>
        /// ホストとして Relay の割り当て allocation で待ち受ける（参加コード。docs/15 T6-10）。ステージは試合を始めるときに選ぶ。
        /// </summary>
        public static OnlineSession HostRelay(RelayHostAllocation allocation, IReadOnlyList<CharacterConfig> characters, RollbackSettings settings)
        {
            OnlineSession session = HostOver(() => TransportLink.HostRelay(allocation.ServerData), null, characters, settings);
            session.JoinCode = allocation.JoinCode;
            session.Status = Loc.T($"Hosting online. Join code: {allocation.JoinCode}", $"オンラインで待ち受け中。参加コード: {allocation.JoinCode}");
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
            session.Status = Loc.T($"Connecting with code {joinCode}", $"参加コード {joinCode} で接続中……");
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
            session.Status = Loc.T($"Connecting to {address}:{port}", $"{address}:{port} に接続中……");
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
            session.Status = Loc.T($"Connecting to {address}:{port}", $"{address}:{port} に接続中……");
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
            string problem = WaitingProblem;
            if (problem != null)
            {
                // 個人戦で 5 人以上、チーム対戦でチームが 1 つだけ（待機所の画面はこのときは進めない）。
                throw new InvalidOperationException($"この顔ぶれでは始められません: {problem}");
            }
            CloseUpPlayers();
            SendStartAndBegin(LobbyParticipantCount, rules, stage);
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
            // 途中から入って待っていた人も、ここから参加する。
            ClearLate();
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
            _spectator = null;
            _confirmedLog.Clear();
            PlayerCount = 0;
            _previous = new CharacterState[0];
            Status = IsHost ? PlayersLabel() : Loc.T($"Joined as player {LocalPlayer + 1}", $"プレイヤー {LocalPlayer + 1} として参加中");
        }

        // ホスト: ロビーの段階を変える（全員の準備完了を外す）。
        private void SetPhase(LobbyPhase phase)
        {
            if (phase == LobbyPhase.Waiting)
            {
                // 待機所へ戻ったら、途中から入って待っていた人も、ここから参加を選べる（観戦の列のまま）。
                ClearLate();
            }
            _phase = phase;
            _allReadySince = -1f;
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
                _allReadySince = -1f;
                return;
            }
            int players = LobbyParticipantCount;
            // 参加する人が 2 人を下回ったか、抜けた人がいてチームが 1 つだけになったら、待機所へ戻す。
            if (_phase != LobbyPhase.Waiting && (players < 2 || WaitingProblem != null))
            {
                SetPhase(LobbyPhase.Waiting);
                return;
            }
            if (players < 2 || _phase == LobbyPhase.StageSelect || (_phase == LobbyPhase.Waiting && WaitingProblem != null))
            {
                _allReadySince = -1f;
                return;
            }
            for (int p = 0; p < MaxPlayers; p++)
            {
                // 観戦の人は準備完了を待たない。
                if (IsPresent(p) && !_lobbySpectators[p] && !_lobbyLate[p] && !_lobbyReady[p])
                {
                    _allReadySince = -1f;
                    return;
                }
            }
            if (_phase == LobbyPhase.CharacterSelect)
            {
                // 全員が決めたら、全員そろった帯を見せてから進む（Update が時間を見て、もう一度ここへ来る）。
                if (_allReadySince < 0f)
                {
                    _allReadySince = _clock;
                    return;
                }
                if (_clock - _allReadySince < AllReadyHoldSeconds)
                {
                    return;
                }
            }
            if (_phase == LobbyPhase.Waiting)
            {
                // キャラクター選択の前に、参加する人を前の番号に詰める（キャラクター選択の 1P〜の枠と試合の番号をそろえる。観戦の人は後ろ）。
                CloseUpPlayers();
            }
            SetPhase(_phase == LobbyPhase.Waiting ? LobbyPhase.CharacterSelect : LobbyPhase.StageSelect);
        }

        // ロビーで抜けた人の空いた番号を詰める（番号が 0 から人数 - 1 までになるように）。参加する人を前に、観戦の人を後ろに並べる
        // （試合の番号 = ロビーの番号。観戦の人の番号は試合の人数以上）。番号が変わる人には Welcome を送り直す。
        private void CloseUpPlayers()
        {
            // 並べる順: 参加する人（ホストも、参加するなら）→ 観戦の人。同じ組の中は今の番号の順。
            var order = new List<int>();
            for (int group = 0; group < 3; group++)
            {
                for (int p = 0; p < MaxPlayers; p++)
                {
                    if (IsPresent(p) && SeatGroup(p) == group)
                    {
                        order.Add(p);
                    }
                }
            }
            var peers = (int[])_playerPeer.Clone();
            var names = (string[])_lobbyNames.Clone();
            var characters = (int[])_lobbyCharacters.Clone();
            var pools = (ulong[])_lobbyStagePools.Clone();
            var ready = (bool[])_lobbyReady.Clone();
            var teams = (byte[])_lobbyTeams.Clone();
            var spectators = (bool[])_lobbySpectators.Clone();
            var late = (bool[])_lobbyLate.Clone();
            var announced = (bool[])_lateAnnounced.Clone();
            var startItems = (Airerage.Core.Items.ItemId[][])_lobbyStartItems.Clone();
            var selecting = (bool[])_lobbyItemSelecting.Clone();
            int hostFrom = _hostSlot;
            for (int next = 0; next < MaxPlayers; next++)
            {
                bool filled = next < order.Count;
                int from = filled ? order[next] : -1;
                _playerPeer[next] = filled ? peers[from] : -1;
                _lobbyNames[next] = filled ? names[from] : string.Empty;
                _lobbyCharacters[next] = filled ? characters[from] : 0;
                _lobbyStagePools[next] = filled ? pools[from] : ulong.MaxValue;
                _lobbyReady[next] = filled && ready[from];
                _lobbyTeams[next] = filled ? teams[from] : (byte)0;
                _lobbySpectators[next] = filled && spectators[from];
                _lobbyLate[next] = filled && late[from];
                _lateAnnounced[next] = filled && announced[from];
                _lobbyStartItems[next] = filled ? startItems[from] : null;
                _lobbyItemSelecting[next] = filled && selecting[from];
                if (filled && from == hostFrom)
                {
                    _hostSlot = next;
                }
                else if (filled && from != next)
                {
                    _link.Send(_playerPeer[next], NetMessages.Welcome(next), reliable: true);
                }
            }
            LocalPlayer = _hostSlot;
        }

        // ホスト: 途中から入って待っていた人を、次の試合に参加する人にする（チーム対戦なら、チームがなければ人数の少ないチームへ）。
        private void ClearLate()
        {
            for (int p = 0; p < MaxPlayers; p++)
            {
                if (!_lobbyLate[p])
                {
                    continue;
                }
                _lobbyLate[p] = false;
                _lobbyReady[p] = false;
                if (_teamMode && _lobbyTeams[p] == 0 && !_lobbySpectators[p])
                {
                    _lobbyTeams[p] = SmallestTeam();
                }
            }
        }

        // 番号を詰めるときの組（0: 参加する人、1: 観戦の人、2: 途中から入って待っている人）。
        private int SeatGroup(int p)
        {
            return _lobbyLate[p] ? 2 : _lobbySpectators[p] ? 1 : 0;
        }

        // ホスト: ロビーの顔ぶれを全員へ送る（ロビーにいる間だけ）。
        private void BroadcastLobby()
        {
            if (!IsHost)
            {
                return;
            }
            // 試合中（ステージの読み込みを含む）は、途中から入って待っている人にだけ送る（試合中の人はロビーのメッセージで試合を抜けるので）。
            bool matchRunning = State == SessionState.Running || State == SessionState.Loading;
            if (State != SessionState.Lobby && !matchRunning)
            {
                return;
            }
            byte[] message = NetMessages.Lobby(Roster, _phase, _stageCursor, _teamMode, _hostSlot, matchRunning, _items);
            for (int p = 0; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] >= 0 && (!matchRunning || _lobbyLate[p]))
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
            // 試合中に抜けた人がいれば、番号を詰めて、今いる人で始め直す。途中から入って待っていた人も、再戦から参加する。
            var joining = new List<int>();
            for (int p = 0; p < MaxPlayers; p++)
            {
                if (_lobbyLate[p] && _playerPeer[p] >= 0)
                {
                    joining.Add(_playerPeer[p]);
                }
            }
            ClearLate();
            CloseUpPlayers();
            if (WaitingProblem != null)
            {
                // 抜けた人がいて、チームが 1 つだけになった（チーム対戦）: 再戦はできないので、全員で待機所へ戻ってチームを分け直す。
                ReturnToLobby(LobbyPhase.Waiting);
                return;
            }
            // 待っていた人へ、待つ印を外した待機所の様子を、開始のメッセージより先に送る（試合中の人へはロビーのメッセージを送らない。
            // 受けると試合を抜けるので）。
            byte[] lobby = NetMessages.Lobby(Roster, _phase, _stageCursor, _teamMode, _hostSlot, matchRunning: false, items: _items);
            foreach (int peer in joining)
            {
                _link.Send(peer, lobby, reliable: true);
            }
            SendStartAndBegin(LobbyParticipantCount, rules, StageIndex);
        }

        private void SendStartAndBegin(int count, MatchRules rules, int stage)
        {
            rules = (rules ?? MatchRules.FreePlay()).Clone();
            // アイテムのルールは待機所でホストが決めたもの（docs/20_item_plan.md）。
            _items.ApplyTo(rules);
            rules.Validate().ThrowIfInvalid();
            int match = (MatchNumber + 1) & 0xFF;
            // ホストも送ったものと同じ JSON から作った値を使う（全員が同じ値になり、試合中にアセットを編集しても影響しない）。
            var names = new string[count];
            for (int p = 0; p < count; p++)
            {
                names[p] = SanitizeName(p == _hostSlot ? _localName : _lobbyNames[p]);
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
            // チーム対戦なら人ごとのチームの番号（docs/19 T9-5。全員が試合の初めに SimWorld.SetTeam で同じ値を入れる）。
            var teams = new int[count];
            for (int p = 0; p < count; p++)
            {
                teams[p] = _teamMode ? _lobbyTeams[p] : 0;
            }
            // プリアイテムで持って始めるアイテム（人ごとに 3 つ。なしは 0）。
            var items = new int[count * Airerage.Core.Items.ItemCatalog.BagSlots];
            for (int p = 0; p < count; p++)
            {
                Airerage.Core.Items.ItemId[] own = StartItemsOf(p);
                for (int i = 0; i < own.Length && i < Airerage.Core.Items.ItemCatalog.BagSlots; i++)
                {
                    items[p * Airerage.Core.Items.ItemCatalog.BagSlots + i] = (int)own[i];
                }
            }
            string json = JsonUtility.ToJson(new StartSettings
            {
                Characters = configs, Picks = picks, CharacterIds = ids, Match = rules, Names = names, Stage = stage, Teams = teams, Items = items,
            });
            byte[] start = NetMessages.Start(count, match, json);
            for (int p = 0; p < MaxPlayers; p++)
            {
                _peerMatch[p] = -1;
                if (_playerPeer[p] >= 0 && !_lobbyLate[p] && !_link.Send(_playerPeer[p], start, reliable: true))
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
            Status = Loc.T($"Loading stage {settings.Stage}", $"ステージ {settings.Stage} を読み込み中……");
            // 待っている人の待機所の表示を「対戦中」にする（BroadcastLobby は試合中は待っている人にだけ送る）。
            BroadcastLobby();
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
            _clock += Math.Max(0f, deltaTime);
            PumpEvents();
            AgeJoinNotices();
            if (State == SessionState.Running && _spectator != null)
            {
                if (IsHost)
                {
                    FeedHostSpectator();
                }
                AdvanceSpectator(deltaTime);
                if (IsHost)
                {
                    SendSpectatorInputs();
                    ResendStartIfNeeded(deltaTime);
                }
            }
            else if (State == SessionState.Running)
            {
                Advance(deltaTime, localInput);
                SendInputs();
                ReportChecksum();
                SendSpectatorInputs();
                ResendStartIfNeeded(deltaTime);
            }
            _link.Update(_events);
            HandleEvents();
            if (_allReadySince >= 0f && State == SessionState.Lobby)
            {
                CheckLobbyProgress();
            }
        }

        public void Dispose()
        {
            _link.Dispose();
        }

        /// <summary>観戦で、届いた入力がこれより多くたまったら、少し速く進めて追いつく（ティック。見ている試合の遅れを小さく保つ）。</summary>
        public const int SpectatorCatchUpTicks = 20;

        // 観戦の人: 経過時間の分だけ、届いた確定した入力でワールドを進める（入力が届いていなければ待つ。たまりすぎたら速く進める）。
        private void AdvanceSpectator(float deltaTime)
        {
            _accumulator += deltaTime;
            int buffered = _spectator.BufferedTicks;
            int extra = buffered > SpectatorCatchUpTicks ? 1 : 0;
            int ticks = 0;
            while ((_accumulator >= SimTime.DeltaTime || extra > 0) && ticks < MaxTicksPerFrame && World.Tick < TickLimit)
            {
                if (!_spectator.CanStep)
                {
                    // まだ届いていない: 待った分の時間は持ち越さない（届いたときに一度に飛ばないように）。
                    StalledFrames++;
                    _accumulator = Math.Min(_accumulator, SimTime.DeltaTime);
                    break;
                }
                if (_accumulator >= SimTime.DeltaTime)
                {
                    _accumulator -= SimTime.DeltaTime;
                }
                else
                {
                    extra--;
                }
                for (int p = 0; p < PlayerCount; p++)
                {
                    _previous[p] = World.GetState(p);
                }
                _spectator.Step();
                ticks++;
                if (IsHost && World.Tick == _nextChecksumTick)
                {
                    // 観戦しているホストも、自分のワールドの確定した状態でずれを判定する（参加している人のチェックサムと比べる）。
                    RecordHostChecksum(World.Tick, World.Checksum());
                    _nextChecksumTick += ChecksumIntervalTicks;
                }
            }
        }

        // ホストが観戦: 中継した全員の入力が届いたティックまで、自分のワールドへ順に渡し、ほかの観戦の人へ送る分にも記録する。抜けた人は
        // 抜けたティックから PlayerInput.Departed。試合の初めの入力遅延の分は、全員の空の入力。
        private void FeedHostSpectator()
        {
            int through = int.MaxValue;
            for (int p = 0; p < PlayerCount; p++)
            {
                int contiguous = _departFrom[p] != int.MaxValue ? int.MaxValue : _seenContiguous[p];
                through = Math.Min(through, contiguous);
            }
            if (through == int.MaxValue)
            {
                return;
            }
            bool log = SpectatorPeers() > 0;
            while (_hostFedTick < through)
            {
                int tick = _hostFedTick + 1;
                var inputs = new PlayerInput[PlayerCount];
                for (int p = 0; p < PlayerCount; p++)
                {
                    if (tick >= _departFrom[p])
                    {
                        inputs[p] = PlayerInput.Departed;
                    }
                    else if (tick < _firstInputTick)
                    {
                        inputs[p] = default;
                    }
                    else if (!_seenInputs[p].TryGetValue(tick, out inputs[p]))
                    {
                        // 起きないはず（全員の分がそろったティックまでしか進めず、渡すまでは捨てない。OldestNeeded）。
                        Debug.LogError($"OnlineSession: 観戦しているホストに、プレイヤー {p + 1} のティック {tick} の入力がありません");
                        return;
                    }
                }
                _spectator.AddInputs(tick, inputs);
                if (log)
                {
                    _confirmedLog.Add(inputs);
                }
                _hostFedTick = tick;
            }
        }

        // ホスト: 観戦の人へ、まだ送っていない確定した入力を送る（1 つのメッセージに MaxSpectateTicks まで。送れなければ次のフレームに）。
        private void SendSpectatorInputs()
        {
            if (!IsHost)
            {
                return;
            }
            for (int p = PlayerCount; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] < 0 || _spectatorNext[p] < 0)
                {
                    continue;
                }
                int end = _confirmedLogStart + _confirmedLog.Count;
                while (_spectatorNext[p] < end)
                {
                    int count = Math.Min(NetMessages.MaxSpectateTicks, end - _spectatorNext[p]);
                    List<PlayerInput[]> chunk = _confirmedLog.GetRange(_spectatorNext[p] - _confirmedLogStart, count);
                    byte[] message = NetMessages.SpectateInputs(MatchNumber, _spectatorNext[p], PlayerCount, chunk);
                    if (!_link.Send(_playerPeer[p], message, reliable: true))
                    {
                        break;
                    }
                    _spectatorNext[p] += count;
                }
            }
        }

        // ホスト: 観戦の待機所にいて、つながっている人の数。
        private int SpectatorPeers()
        {
            int count = 0;
            for (int p = 0; p < MaxPlayers; p++)
            {
                count += _playerPeer[p] >= 0 && _lobbySpectators[p] ? 1 : 0;
            }
            return count;
        }

        // ホスト: 試合に残っている参加者の数（自分を含む。抜けた人と観戦の人は数えない）。
        private int RemainingPlayers()
        {
            int count = 0;
            for (int p = 0; p < PlayerCount; p++)
            {
                count += (p == _hostSlot || _playerPeer[p] >= 0) && !_departed[p] ? 1 : 0;
            }
            return count;
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
                // インバータ（SOLDERER）の間は、ワイヤーを押している間の照準も送る（鞭の向き）。出している間ずっと照準を追う技
                // （ボールライトニング）の間は、いつも照準を送る。
                bool local = LocalPlayer >= 0 && LocalPlayer < PlayerCount;
                bool keepWireAim = local && World.GetState(LocalPlayer).Combat.InverterOn;
                bool keepAim = local && ActionController.TracksAimNow(World.GetState(LocalPlayer), World.GetConfig(LocalPlayer).Combat);
                Exchange.AdvanceFrame(localInput(Session.LocalInputTick), keepWireAim, keepAim);
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
            // 観戦の人にも送る（観戦の人は Watching で見始めたことを知らせる）。
            bool waiting = false;
            for (int p = 0; p < MaxPlayers; p++)
            {
                waiting |= _playerPeer[p] >= 0 && !_lobbyLate[p] && _peerMatch[p] != MatchNumber;
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
            for (int p = 0; p < MaxPlayers; p++)
            {
                if (_playerPeer[p] >= 0 && !_lobbyLate[p] && _peerMatch[p] != MatchNumber)
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
                RecordHostChecksum(_nextChecksumTick, checksum);
            }
            else
            {
                _link.Send(0, NetMessages.Checksum(MatchNumber, LocalPlayer, _nextChecksumTick, checksum), reliable: true);
            }
            _nextChecksumTick += ChecksumIntervalTicks;
        }

        // ホスト: クライアント player が報告した確定した状態のチェックサム。
        private void RecordChecksum(int player, int tick, ulong checksum)
        {
            ChecksumRow row = RowAt(tick);
            row.Values[player] = checksum;
            CompareRow(tick, row);
        }

        // ホスト: 自分のワールド（参加していれば巻き戻しのセッション、観戦していれば観戦のワールド）の確定した状態のチェックサム。
        private void RecordHostChecksum(int tick, ulong checksum)
        {
            ChecksumRow row = RowAt(tick);
            row.Host = checksum;
            CompareRow(tick, row);
        }

        private ChecksumRow RowAt(int tick)
        {
            if (!_checksums.TryGetValue(tick, out ChecksumRow row))
            {
                row = new ChecksumRow();
                _checksums[tick] = row;
            }
            return row;
        }

        // ホストの値が届いていれば、届いているクライアントの値と比べる（ずれの判定。ホストが観戦していても同じ）。全員の分を比べたら消す。
        private void CompareRow(int tick, ChecksumRow row)
        {
            ulong? host = row.Host;
            if (host == null)
            {
                return;
            }
            int remotes = 0;
            for (int p = 0; p < PlayerCount; p++)
            {
                if (p == LocalPlayer || _departed[p])
                {
                    continue;
                }
                remotes++;
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
            World = new SimWorld(_stage.Terrain, _stage.Bounds, _stage.Zones, settings.Match, _stage.CoinSpawns, _stage.BankSpots, _stage.Platforms,
                _stage.Traps);
            for (int p = 0; p < playerCount; p++)
            {
                World.AddCharacter(settings.Characters[settings.Picks[p]], _stage.SpawnPosition(p, playerCount), _stage.SpawnFacing(p, playerCount));
            }
            PlayerTeams = TeamsOf(settings, playerCount);
            for (int p = 0; p < playerCount; p++)
            {
                World.SetTeam(p, PlayerTeams[p]);
                // プリアイテム: 開始の設定のアイテムを持って始める（全員が同じ値を入れる。docs/20_item_plan.md）。
                World.SetItems(p, ItemsOf(settings, p));
            }
            _confirmedLog.Clear();
            _confirmedLogStart = World.Tick;
            _firstInputTick = World.Tick + _settings.InputDelayTicks;
            _hostFedTick = World.Tick - 1;
            if (LocalPlayer >= playerCount)
            {
                // 観戦（docs/19）: 試合に参加せず、確定した入力でワールドを進める。クライアントは見始めたことをホストへ知らせ、ホストから
                // 届く入力で進める。観戦しているホストは、中継した全員の入力から自分で確定したティックを決めて進める（FeedHostSpectator）。
                Session = null;
                Exchange = null;
                _spectator = new SpectatorSession(World);
                if (!IsHost)
                {
                    _link.Send(0, NetMessages.Watching(match), reliable: true);
                }
            }
            else
            {
                _spectator = null;
                Session = new RollbackSession(World, LocalPlayer, _settings);
                Exchange = new InputExchange(Session, _settings);
                if (IsHost && SpectatorPeers() > 0)
                {
                    // 観戦の人がいる: 確定したティックの全員の入力を記録して送る。
                    Session.TickConfirmed += (tick, inputs) => _confirmedLog.Add((PlayerInput[])inputs.Clone());
                }
            }
            int firstInputTick = World.Tick + _settings.InputDelayTicks;
            for (int p = 0; p < MaxPlayers; p++)
            {
                _departed[p] = false;
                _departFrom[p] = int.MaxValue;
                // 観戦の人が、ホストより先にステージを読み込んで見始めていたら（Watching が先に届いた）、すぐに送り始める。
                _spectatorNext[p] = IsHost && p >= playerCount && _playerPeer[p] >= 0 && _peerMatch[p] == match ? World.Tick : -1;
                _seenInputs[p].Clear();
                _seenContiguous[p] = firstInputTick - 1;
                _seenOldest[p] = firstInputTick;
                for (int q = 0; q < MaxPlayers; q++)
                {
                    _seenAck[p, q] = firstInputTick - 1;
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
            // 途中から入って待っている人の待機所の表示を、すぐに「対戦中」にする（ホストだけが送る）。
            BroadcastLobby();
            Status = _spectator != null
                ? Loc.T($"Spectating {playerCount} players", $"{playerCount} 人の試合を観戦中")
                : Loc.T($"Running: player {LocalPlayer + 1} of {playerCount}", $"対戦中: {playerCount} 人のうちプレイヤー {LocalPlayer + 1}");
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
                    // 待機所の段階なら、そのまま参加する。キャラクター選択・ステージ選択・試合の途中なら、待機所で今の試合が終わるのを待つ
                    // （2026-11-03 のユーザーの要望。次の試合から参加する）。満員なら断る。
                    bool late = State != SessionState.Lobby || _phase != LobbyPhase.Waiting;
                    if (player < 0 || State == SessionState.Ended || State == SessionState.Connecting)
                    {
                        _link.Send(e.Peer, NetMessages.Refused(), reliable: true);
                        _link.Disconnect(e.Peer);
                        return;
                    }
                    _playerPeer[player] = e.Peer;
                    _lobbyNames[player] = string.Empty;
                    _lobbyCharacters[player] = 0;
                    _lobbyStagePools[player] = ulong.MaxValue;
                    _lobbyReady[player] = false;
                    _lobbyTeams[player] = 0;
                    _lobbySpectators[player] = false;
                    _spectatorNext[player] = -1;
                    _lobbyLate[player] = late;
                    _lateAnnounced[player] = false;
                    _lobbyStartItems[player] = null;
                    _lobbyItemSelecting[player] = false;
                    // 途中から入った人は、観戦の列に入れる（2026-11-03 のユーザーの要望。待機所に戻ってから、見出しを選んで参加する）。
                    _lobbySpectators[player] = late;
                    if (_teamMode && !late)
                    {
                        _lobbyTeams[player] = SmallestTeam();
                    }
                    _link.Send(e.Peer, NetMessages.Welcome(player), reliable: true);
                    Status = PlayersLabel();
                    BroadcastLobby();
                    break;

                case TransportLink.EventKind.Disconnected:
                    for (int p = 0; p < MaxPlayers; p++)
                    {
                        if (_playerPeer[p] == e.Peer)
                        {
                            _playerPeer[p] = -1;
                            _lobbyNames[p] = string.Empty;
                            _lobbyCharacters[p] = 0;
                            _lobbyStagePools[p] = ulong.MaxValue;
                            _lobbyReady[p] = false;
                            _lobbyTeams[p] = 0;
                            _lobbyStartItems[p] = null;
                            _lobbyItemSelecting[p] = false;
                            bool wasSpectator = _lobbySpectators[p] || _lobbyLate[p];
                            _lobbySpectators[p] = false;
                            _lobbyLate[p] = false;
                            _spectatorNext[p] = -1;
                            CheckLobbyProgress();
                            BroadcastLobby();
                            if ((State == SessionState.Running || State == SessionState.Loading) && (wasSpectator || p >= PlayerCount))
                            {
                                // 観戦の人が抜けても、試合はそのまま。
                                continue;
                            }
                            // 試合中に抜けた人は外して、残りの 2 人以上で続ける（docs/15 T6-10）。試合が終わった後（結果の画面の EXIT）に
                            // 抜けたときは、残りが 1 人でも部屋を閉じない（ホストは BACK ROOM で待機所へ戻って人を待てる。2026-11-03）。
                            bool finished = World != null && World.Phase == Airerage.Core.Match.MatchPhase.Finished;
                            bool goesOn = State == SessionState.Running && p < PlayerCount && (RemainingPlayers() >= 2 || finished)
                                && RemoveDeparted(p);
                            if (!goesOn && (State == SessionState.Running || State == SessionState.Loading))
                            {
                                // 1 人だけになったか、外せなかったら、試合を終える（残りの人も止まったままにしないよう、接続を切る）。
                                State = SessionState.Ended;
                                Status = Loc.T($"Player {p + 1} disconnected", $"プレイヤー {p + 1} の接続が切れました");
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
                    if (inputSender >= 0)
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
                    if (inputSender < 0 || inputSender >= PlayerCount || _departed[inputSender] || target < 0 || target >= PlayerCount)
                    {
                        return;
                    }
                    Airerage.Core.Rollback.InputMessage message = Airerage.Core.Rollback.InputMessage.FromBytes(NetMessages.InputPayload(e.Data));
                    RecordSeen(inputSender, target, message);
                    if (target == LocalPlayer)
                    {
                        Exchange.Receive(message);
                    }
                    else if (_playerPeer[target] >= 0)
                    {
                        _link.Send(_playerPeer[target], e.Data, reliable: false);
                    }
                    break;

                case NetKind.Hello:
                    int sender = PlayerOfPeer(e.Peer);
                    if (sender >= 0)
                    {
                        _lobbyNames[sender] = SanitizeName(NetMessages.ReadHello(e.Data));
                        BroadcastLobby();
                        if (_lobbyLate[sender] && !_lateAnnounced[sender])
                        {
                            // 途中から入った人を、試合・選択の途中の全員へ知らせる（画面の左下に「名前 JOIN」）。
                            _lateAnnounced[sender] = true;
                            string joined = _lobbyNames[sender].Length > 0 ? _lobbyNames[sender] : $"P{sender + 1}";
                            byte[] notice = NetMessages.Joined(joined);
                            for (int q = 0; q < MaxPlayers; q++)
                            {
                                if (_playerPeer[q] >= 0 && !_lobbyLate[q])
                                {
                                    _link.Send(_playerPeer[q], notice, reliable: true);
                                }
                            }
                            AddJoinNotice(joined);
                        }
                    }
                    break;

                case NetKind.StagePool:
                    int pooler = PlayerOfPeer(e.Peer);
                    if (pooler >= 0)
                    {
                        _lobbyStagePools[pooler] = NetMessages.ReadStagePool(e.Data);
                    }
                    break;

                case NetKind.Items:
                    int chooser = PlayerOfPeer(e.Peer);
                    if (chooser >= 0)
                    {
                        _lobbyStartItems[chooser] = CleanItems(NetMessages.ReadItems(e.Data, out bool choosing));
                        _lobbyItemSelecting[chooser] = choosing;
                        if (State == SessionState.Lobby)
                        {
                            BroadcastLobby();
                        }
                    }
                    break;

                case NetKind.Pick:
                    int picker = PlayerOfPeer(e.Peer);
                    if (picker >= 0)
                    {
                        _lobbyCharacters[picker] = ClampCharacter(NetMessages.ReadPick(e.Data));
                        BroadcastLobby();
                    }
                    break;

                case NetKind.Spectate:
                    int seat = PlayerOfPeer(e.Peer);
                    if (seat >= 0)
                    {
                        SetSpectatorOf(seat, NetMessages.ReadSpectate(e.Data));
                    }
                    break;

                case NetKind.Watching:
                    int watcher = PlayerOfPeer(e.Peer);
                    int watchedMatch = NetMessages.ReadWatching(e.Data);
                    if (watcher >= 0)
                    {
                        _peerMatch[watcher] = watchedMatch;
                        if (State == SessionState.Running && watchedMatch == MatchNumber && watcher >= PlayerCount)
                        {
                            // 見始めた: 試合の初めからの確定した入力を送る。
                            _spectatorNext[watcher] = _confirmedLogStart;
                        }
                    }
                    break;

                case NetKind.Team:
                    int mover = PlayerOfPeer(e.Peer);
                    if (mover >= 0)
                    {
                        SetTeamOf(mover, NetMessages.ReadTeam(e.Data));
                    }
                    break;

                case NetKind.Ready:
                    int readier = PlayerOfPeer(e.Peer);
                    if (readier >= 0 && State == SessionState.Lobby)
                    {
                        _lobbyReady[readier] = NetMessages.ReadReady(e.Data);
                        CheckLobbyProgress();
                        BroadcastLobby();
                    }
                    break;

                case NetKind.Checksum:
                    NetMessages.ReadChecksum(e.Data, out int checksumMatch, out int player, out int tick, out ulong checksum);
                    if (State == SessionState.Running && checksumMatch == MatchNumber && player >= 0 && player < PlayerCount && player != LocalPlayer
                        && player == PlayerOfPeer(e.Peer))
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
                    Status = Loc.T("Connected. Waiting for the host", "接続しました。ホストを待っています");
                    break;

                case TransportLink.EventKind.Disconnected:
                    State = SessionState.Ended;
                    Status = Loc.T("Disconnected from the host", "ホストとの接続が切れました");
                    break;

                case TransportLink.EventKind.Data:
                    switch (NetMessages.KindOf(e.Data))
                    {
                        case NetKind.Welcome:
                            LocalPlayer = NetMessages.ReadWelcome(e.Data);
                            State = SessionState.Lobby;
                            // 名前がなくても送る（途中から入ったとき、ホストがこれを受けて全員に知らせる）。
                            _link.Send(0, NetMessages.Hello(_localName), reliable: true);
                            if (_localCharacter != 0)
                            {
                                _link.Send(0, NetMessages.Pick(_localCharacter), reliable: true);
                            }
                            if (_localStagePool != ulong.MaxValue)
                            {
                                _link.Send(0, NetMessages.StagePool(_localStagePool), reliable: true);
                            }
                            if (_localStartItems.Length > 0 || _localItemSelecting)
                            {
                                _link.Send(0, NetMessages.Items(_localItemSelecting, _localStartItems), reliable: true);
                            }
                            Status = Loc.T($"Joined as player {LocalPlayer + 1}. Waiting for the host to start",
                                $"プレイヤー {LocalPlayer + 1} として参加しました。ホストの開始を待っています");
                            break;
                        case NetKind.Lobby:
                            LobbySlot[] roster = NetMessages.ReadLobby(e.Data, out LobbyPhase phase, out int stage, out bool teamMode, out int hostSlot,
                                out bool matchRunning, out LobbyItems lobbyItems);
                            _clientItems = lobbyItems;
                            _clientHostSlot = hostSlot;
                            _clientMatchRunning = matchRunning;
                            _clientRoster = EmptyRoster();
                            for (int p = 0; p < MaxPlayers && p < roster.Length; p++)
                            {
                                _clientRoster[p] = new LobbySlot(roster[p].Connected, SanitizeName(roster[p].Name), roster[p].Character,
                                    roster[p].Ready, roster[p].Team, roster[p].Spectator, roster[p].Late, roster[p].ItemSelecting);
                            }
                            _clientTeamMode = teamMode;
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
                            Status = Loc.T("The host refused (the room is full)", "ホストに断られました（満員）");
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
                        case NetKind.Joined:
                            AddJoinNotice(SanitizeName(NetMessages.ReadJoined(e.Data)));
                            break;
                        case NetKind.SpectateInputs:
                            NetMessages.ReadSpectateInputs(e.Data, out int spectateMatch, out int spectateStart, out PlayerInput[][] ticks);
                            if (_spectator != null && State == SessionState.Running && spectateMatch == MatchNumber)
                            {
                                for (int i = 0; i < ticks.Length; i++)
                                {
                                    if (ticks[i].Length != PlayerCount)
                                    {
                                        throw new FormatException($"観戦の入力の人数（{ticks[i].Length}）が試合の人数（{PlayerCount}）と違う");
                                    }
                                    _spectator.AddInputs(spectateStart + i, ticks[i]);
                                }
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
            // 参加していれば自分の巻き戻しのセッションがまだ受け取っていない分、観戦していれば自分のワールドへまだ渡していない分も残す。
            int oldest = Session != null ? Session.ContiguousTick(player) + 1 : _hostFedTick + 1;
            for (int r = 0; r < PlayerCount; r++)
            {
                if (r != player && !_departed[r] && _playerPeer[r] >= 0)
                {
                    oldest = Math.Min(oldest, _seenAck[player, r] + 1);
                }
            }
            if (_spectator != null)
            {
                oldest = Math.Min(oldest, _hostFedTick + 1);
            }
            return Math.Max(oldest, _firstInputTick);
        }

        // ホスト: 試合中に抜けたプレイヤー player を外す。抜けたティックは、その人の入力でホストが見た（中継した）最後の次。そこまでの入力の
        // うち、まだだれかに届いていないかもしれない分を、抜けた知らせに入れて全員へ確実に送る。外せなければ false（試合を終える）。
        private bool RemoveDeparted(int player)
        {
            int fromTick = Math.Max(_seenContiguous[player] + 1, _firstInputTick);
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
            for (int r = 0; r < MaxPlayers; r++)
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
            if (player < 0 || player >= PlayerCount || player == LocalPlayer || player == HostSlot || _departed[player])
            {
                Debug.LogWarning($"OnlineSession: 抜けた知らせのプレイヤーの番号がおかしいので無視しました（{player}）");
                return;
            }
            if (_spectator == null)
            {
                // 観戦の人は、抜けた人の入力も確定した入力（PlayerInput.Departed）で届くので、印をつけるだけ（観戦しているホストは、
                // 抜けたティックから自分のワールドへ「抜けた」入力を渡す。_departFrom）。
                Exchange.RemovePlayer(player, fromTick, startTick, inputs);
            }
            _departFrom[player] = fromTick;
            _departed[player] = true;
            Status = Loc.T($"Player {player + 1} left. The match goes on", $"プレイヤー {player + 1} が抜けました。試合は続きます");
        }

        private void DisconnectAll()
        {
            for (int p = 0; p < MaxPlayers; p++)
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
            // チームの番号とアイテムも、受け取ったときに確かめる（試合を始めるときに失敗しないように）。
            TeamsOf(settings, playerCount);
            for (int p = 0; p < playerCount; p++)
            {
                ItemsOf(settings, p);
            }
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

            /// <summary>プレイヤーごとのチームの番号（1〜4。個人戦なら 0 か、なし）。</summary>
            public int[] Teams;

            /// <summary>プレイヤーごとの、プリアイテムで持って始めるアイテム（人ごとに 3 つ。ItemId。なしは 0。なければアイテムなし）。</summary>
            public int[] Items;
        }

        // 開始の設定の、プレイヤー p のアイテム（範囲の外なら FormatException。違うビルドどうしなど）。
        private static Airerage.Core.Items.ItemId[] ItemsOf(StartSettings settings, int p)
        {
            var items = new List<Airerage.Core.Items.ItemId>();
            int slots = Airerage.Core.Items.ItemCatalog.BagSlots;
            for (int i = 0; settings.Items != null && i < slots && p * slots + i < settings.Items.Length; i++)
            {
                int value = settings.Items[p * slots + i];
                if (value == 0)
                {
                    continue;
                }
                bool random = i == 0 && value == (int)Airerage.Core.Items.ItemId.Random;
                if (!random && !Airerage.Core.Items.ItemCatalog.IsValid((Airerage.Core.Items.ItemId)value))
                {
                    throw new FormatException($"開始の設定のアイテムが不正です（プレイヤー {p + 1}: {value}）");
                }
                items.Add((Airerage.Core.Items.ItemId)value);
            }
            return items.ToArray();
        }

        // 開始の設定のチームの番号（なければ、すべて 0。範囲の外なら FormatException。違うビルドどうしなど）。
        private static byte[] TeamsOf(StartSettings settings, int count)
        {
            var teams = new byte[count];
            if (settings.Teams == null)
            {
                return teams;
            }
            for (int p = 0; p < count && p < settings.Teams.Length; p++)
            {
                int team = settings.Teams[p];
                if (team < 0 || team > TeamRules.MaxTeams)
                {
                    throw new FormatException($"開始の設定のチームの番号が不正です（プレイヤー {p + 1}: {team}）");
                }
                teams[p] = (byte)team;
            }
            return teams;
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
            for (int p = 0; p < MaxPlayers; p++)
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
            // 試合中は、試合の人の番号（抜けた人の番号も）より後ろを先に探す（試合の番号と重ならないように）。
            int from = State == SessionState.Running || State == SessionState.Loading ? PlayerCount : 0;
            for (int i = 0; i < MaxPlayers; i++)
            {
                int p = (from + i) % MaxPlayers;
                if (p != _hostSlot && _playerPeer[p] < 0)
                {
                    return p;
                }
            }
            return -1;
        }

        private sealed class ChecksumRow
        {
            public readonly ulong?[] Values = new ulong?[MaxPlayers];
            public ulong? Host;
            public int Compared;
        }
    }
}
