using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Airerage.Core.Match;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
using Unity.Networking.Transport.Relay;
using Unity.Services.Core;
using UnityEngine;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.Net
{
    /// <summary>
    /// ネットワーク対戦の接続（docs/15 T6-7）。いつも読み込んでおく App のシーンに置き、画面のシーン（メニュー、試合）が変わっても
    /// 接続（OnlineSession）を保つ。
    /// - メニュー（OnlineMenu）がホスト・参加・抜けるを頼み、ロビー（待機所、キャラクター選択、ステージ選択）の間はここが送受信を進める。
    /// - 開始のメッセージが届いたら（ステージを読み込む待ち）、GameFlow にそのステージと試合の仕組みのシーンを読み込ませる。試合の仕組み
    ///   （OnlineRunner）が BeginPending で始め、試合中は試合の仕組みが自分の入力で送受信を進める（Pump）。
    /// - 試合の後にホストがロビーへ戻したら（ReturnToLobby）、メニューへ戻す（メニューが今の段階の画面を開く）。
    /// つなぎ方は 2 つ: 直接（IP アドレスとポート。LAN）と、参加コード（Unity の Relay。docs/15 T6-10）。参加コードでは、サービスに
    /// 問い合わせている間（数秒）は Busy で、終わったら接続を作る。問い合わせの間に抜けたら、届いた結果は捨てる。
    /// </summary>
    public sealed class OnlineConnection : MonoBehaviour
    {
        /// <summary>プレイヤーネーム・アドレス・ポートを覚えておくキー（PlayerPrefs）。</summary>
        public const string PlayerNameKey = "Airerage.PlayerName";
        public const string AddressKey = "Airerage.Address";
        public const string PortKey = "Airerage.Port";

        public const ushort DefaultPort = 7777;

        [Tooltip("選べるキャラクターの一覧（全員が同じビルドのこと）")]
        [SerializeField] private CharacterRoster _roster;
        [Tooltip("ロールバックの設定（OnlineRunner と同じ値。D-34）")]
        [SerializeField] private RollbackSettings _rollback = new RollbackSettings();
        [Tooltip("試合のルール（すべて【仮】。docs/12）。待機所で時間の制限を外すと、制限だけをなくす")]
        [SerializeField] private MatchRules _match = new MatchRules();

        private OnlineSession _session;
        private GameFlow _flow;
        private int _pumpedFrame = -1;
        private int _request;

        /// <summary>今の接続（つながっていなければ null）。</summary>
        public OnlineSession Session => _session;

        /// <summary>接続があり、切れていないか。</summary>
        public bool IsConnected => _session != null && _session.State != OnlineSession.SessionState.Ended;

        /// <summary>最後に起きたこと（接続できなかった理由など。画面に出す）。</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>時間の制限ありで始めるか（ホストが待機所で選ぶ）。</summary>
        public bool Timed { get; set; } = true;

        /// <summary>参加コードのサービス（Relay）に問い合わせているか（その間はボタンを押せない）。</summary>
        public bool Busy { get; private set; }

        /// <summary>参加コードのサービス（テストでは偽物に差し替える）。</summary>
        public IRelayBackend RelayBackend { get; set; } = new UnityRelayBackend();

        public CharacterRoster Roster => _roster;

        public RollbackSettings Rollback => _rollback;

        /// <summary>次の試合のルール（値はシーンの設定。テストで短くするのに使う）。</summary>
        public MatchRules MatchRules => _match;

        private void Awake()
        {
            if (_roster == null || _roster.Count == 0)
            {
                Debug.LogError($"{name}: キャラクターの一覧が設定されていません", this);
                enabled = false;
                return;
            }
            _rollback.Validate();
            _match.Validate().ThrowIfInvalid();
            // 同じ PC で複数のプレイヤーを動かすとき（Multiplayer Play Mode）、操作していないウィンドウも止めない。
            Application.runInBackground = true;
        }

        private void Start()
        {
            _flow = FindFirstObjectByType<GameFlow>();
        }

        private void OnDestroy()
        {
            _session?.Dispose();
            _session = null;
        }

        /// <summary>ホストとして port で待ち受ける。失敗したら false（理由は Message）。</summary>
        public bool Host(ushort port, string playerName, int character)
        {
            var configs = new List<CharacterConfig>();
            for (int i = 0; i < _roster.Count; i++)
            {
                configs.Add(_roster.Get(i).Config);
            }
            return Open(() => OnlineSession.Host(port, configs, _rollback), playerName, character);
        }

        /// <summary>address:port のホストに参加する。失敗したら false（理由は Message）。</summary>
        public bool Join(string address, ushort port, string playerName, int character)
        {
            return Open(() => OnlineSession.Join(address, port, _rollback), playerName, character);
        }

        /// <summary>
        /// 参加コードでホストになる（Relay の割り当てと参加コードを作り、それで待ち受ける。docs/15 T6-10）。サービスの問い合わせが
        /// 終わるまで待つ。失敗したら false（理由は Message）。
        /// </summary>
        public async Task<bool> HostOnline(string playerName, int character)
        {
            var configs = new List<CharacterConfig>();
            for (int i = 0; i < _roster.Count; i++)
            {
                configs.Add(_roster.Get(i).Config);
            }
            int request = BeginRequest("Creating an online room...");
            RelayHostAllocation allocation;
            try
            {
                allocation = await RelayBackend.CreateAsync(OnlineSession.MaxPlayers - 1);
            }
            catch (Exception e) when (IsServiceError(e))
            {
                return FailRequest(request, $"Could not create an online room: {e.Message}", e);
            }
            finally
            {
                if (request == _request)
                {
                    Busy = false;
                }
            }
            if (request != _request)
            {
                return false;
            }
            return Open(() => OnlineSession.HostRelay(allocation, configs, _rollback), playerName, character);
        }

        /// <summary>参加コード code のホストに参加する（docs/15 T6-10）。失敗したら false（理由は Message）。</summary>
        public async Task<bool> JoinOnline(string code, string playerName, int character)
        {
            string joinCode = JoinCodes.Normalize(code);
            if (joinCode.Length == 0)
            {
                Message = "ENTER THE JOIN CODE FROM THE HOST";
                return false;
            }
            int request = BeginRequest($"Looking for the room {joinCode}...");
            RelayServerData relay;
            try
            {
                relay = await RelayBackend.JoinAsync(joinCode);
            }
            catch (Exception e) when (IsServiceError(e))
            {
                return FailRequest(request, $"Could not join {joinCode}: {e.Message}", e);
            }
            finally
            {
                if (request == _request)
                {
                    Busy = false;
                }
            }
            if (request != _request)
            {
                return false;
            }
            return Open(() => OnlineSession.JoinRelay(relay, joinCode, _rollback), playerName, character);
        }

        // サービスの問い合わせで起こりうる失敗（サービスの拒否・通信の失敗、サービスを初期化できない、割り当てが使えない）。
        // ほかの例外は問い合わせの外へそのまま出す。
        private static bool IsServiceError(Exception e)
        {
            return e is RequestFailedException || e is ServicesInitializationException || e is ArgumentException;
        }

        // サービスへの問い合わせを始める（前の問い合わせの結果は捨てる）。
        private int BeginRequest(string message)
        {
            Leave();
            Busy = true;
            Message = message;
            return _request;
        }

        private bool FailRequest(int request, string message, Exception error)
        {
            if (request == _request)
            {
                Message = message;
            }
            Debug.LogWarning($"{name}: {message}\n{error}", this);
            return false;
        }

        /// <summary>接続を閉じる（ロビーから抜ける、試合をやめる）。問い合わせ中なら、その結果は捨てる。</summary>
        public void Leave()
        {
            _request++;
            Busy = false;
            _session?.Dispose();
            _session = null;
        }

        /// <summary>ホスト: 選んだステージで試合を始める（全員がそのステージを読み込んでから始まる）。</summary>
        public void StartMatch(int stage)
        {
            _session.StartMatch(RulesForNextMatch(), stage);
        }

        /// <summary>ホスト: 同じ人・同じステージで始め直す（結果の Restart）。</summary>
        public void Restart()
        {
            _session.RestartMatch(RulesForNextMatch());
        }

        // 次の試合のルール: 待機所の選択（時間の制限なしなら制限だけをなくす）と、試合ごとの乱数の種（100 枚のコインの場所など）。
        private MatchRules RulesForNextMatch()
        {
            MatchRules rules = _match.Clone();
            if (!Timed)
            {
                rules.DurationTicks = 0;
            }
            rules.Seed = Environment.TickCount;
            return rules;
        }

        /// <summary>
        /// 送受信を進める（試合中は OnlineRunner が自分の入力を渡して毎フレーム呼ぶ。呼ばれなかったフレームは、ここが入力なしで進める）。
        /// </summary>
        public void Pump(float deltaTime, Func<int, SimInput> localInput)
        {
            _pumpedFrame = Time.frameCount;
            _session?.Update(deltaTime, localInput ?? NoInput);
        }

        private static SimInput NoInput(int tick)
        {
            return default;
        }

        private void LateUpdate()
        {
            if (_session == null)
            {
                return;
            }
            if (_pumpedFrame != Time.frameCount)
            {
                Pump(Time.deltaTime, null);
            }
            if (_flow == null || _flow.IsLoading)
            {
                return;
            }
            if (_session.HasPendingStart && _flow.Current != GameFlow.FlowScreen.OnlineMatch)
            {
                // 開始のメッセージのステージと、試合の仕組みを読み込む（読み込んだ OnlineRunner が BeginPending で始める）。
                _flow.StartOnlineMatch(_flow.Stages.Get(_session.PendingStage));
            }
            else if (_session.State == OnlineSession.SessionState.Lobby && _flow.Current == GameFlow.FlowScreen.OnlineMatch)
            {
                // ホストが試合の後にロビーへ戻した（メニューが今の段階の画面を開く）。
                _flow.ShowMenu();
            }
        }

        private bool Open(Func<OnlineSession> open, string playerName, int character)
        {
            Leave();
            try
            {
                _session = open();
                _session.LocalName = playerName;
                _session.LocalCharacter = Mathf.Clamp(character, 0, _roster.Count - 1);
                // RANDOM で選んでよいステージ（オプションの RANDOM STAGES。ホストは全員の共通のステージから選ぶ）。
                _session.LocalStagePool = RandomStagePool.Mask(_flow != null && _flow.Stages != null ? _flow.Stages.Stages : null);
                Message = string.Empty;
                return true;
            }
            catch (InvalidOperationException e)
            {
                Message = e.Message;
                Debug.LogError($"{name}: 接続を始められません: {e.Message}", this);
            }
            catch (ArgumentException e)
            {
                Message = e.Message;
                Debug.LogError($"{name}: 接続を始められません: {e.Message}", this);
            }
            return false;
        }
    }
}
