using System;
using System.Collections.Generic;
using Airerage.Core.Match;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
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

        /// <summary>今の接続（つながっていなければ null）。</summary>
        public OnlineSession Session => _session;

        /// <summary>接続があり、切れていないか。</summary>
        public bool IsConnected => _session != null && _session.State != OnlineSession.SessionState.Ended;

        /// <summary>最後に起きたこと（接続できなかった理由など。画面に出す）。</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>時間の制限ありで始めるか（ホストが待機所で選ぶ）。</summary>
        public bool Timed { get; set; } = true;

        public CharacterRoster Roster => _roster;

        public RollbackSettings Rollback => _rollback;

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

        /// <summary>接続を閉じる（ロビーから抜ける、試合をやめる）。</summary>
        public void Leave()
        {
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
