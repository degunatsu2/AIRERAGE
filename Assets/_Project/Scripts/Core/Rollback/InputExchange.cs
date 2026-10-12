using System;
using System.Collections.Generic;
using Degunatsu.Core.Input;

namespace Degunatsu.Core.Rollback
{
    /// <summary>
    /// 入力の送受信の規則（docs/10_online_plan.md T2-4）。RollbackSession を包み、
    /// 自分の入力は InputNormalizer で正規化してから使う（自分のシミュレーションにも、送るのにも同じものを使う）。
    /// 相手ごとに「まだ確認されていない自分の入力」を覚えておき、毎回まとめて送り直す（欠落に備えた冗長送信）。
    /// 届いたメッセージの入力はセッションに渡し、確認応答で送らなくてよい入力を捨てる。
    /// メッセージで互いのティックを伝え合い、TimeSync で進みすぎを推定する（T2-6）。
    /// 通信はしない（BuildMessage で作ったメッセージを送り、届いたメッセージを Receive に渡すのは呼び出し側）。
    /// </summary>
    public sealed class InputExchange
    {
        /// <summary>
        /// 1つのメッセージに入れる入力の最大の数。通信路が一度に送れる大きさ（約1400バイト）に収まるようにする
        /// （16 バイト＋180 × 6 バイト = 1096 バイト）。
        /// </summary>
        public const int MaxInputsPerMessage = 180;

        private readonly RollbackSession _session;
        private readonly RollbackSettings _settings;
        private readonly TimeSync _timeSync;
        private PlayerInput _lastLocalInput;

        // まだ全員に確認されていない自分の入力。ティック → 入力。
        private readonly Dictionary<int, PlayerInput> _unacked = new Dictionary<int, PlayerInput>();
        private int _oldestUnacked;

        // 相手ごとの、自分の入力をどこまで受け取ったと確認されたか。
        private readonly int[] _ackedBy;

        public InputExchange(RollbackSession session, RollbackSettings settings)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ackedBy = new int[session.PlayerCount];
            int lastDefault = session.LocalInputTick - 1; // 入力遅延の分の最初のティックは、全員が空の入力で確定している
            for (int p = 0; p < _ackedBy.Length; p++)
            {
                _ackedBy[p] = lastDefault;
            }
            _oldestUnacked = lastDefault + 1;
            _timeSync = new TimeSync(session.PlayerCount, session.LocalPlayer, session.CurrentTick, settings);
        }

        public RollbackSession Session => _session;

        /// <summary>まだ全員に確認されていない自分の入力の数（再送の量の目安）。</summary>
        public int UnackedCount => _unacked.Count;

        /// <summary>自分が相手より何ティック先に進んでいるかの推定（TimeSync.TicksAhead）。</summary>
        public float TicksAhead => _timeSync.TicksAhead;

        /// <summary>
        /// 待つべきフレーム数（TimeSync.RecommendWait）。毎フレーム呼び、0 でなければその数のフレームだけ
        /// AdvanceFrame を呼ばずに待つ（BuildMessage と Receive は続ける）。
        /// </summary>
        public int RecommendWaitFrames()
        {
            return _timeSync.RecommendWait(_session.CurrentTick);
        }

        /// <summary>
        /// 自分の入力を正規化して、セッションを1ティック進める（RollbackSession.AdvanceFrame）。使った入力を返す。
        /// </summary>
        public PlayerInput AdvanceFrame(PlayerInput localInput)
        {
            int tick = _session.LocalInputTick;
            PlayerInput normalized = InputNormalizer.Normalize(localInput, _lastLocalInput, _settings.MoveHysteresis);
            _session.AdvanceFrame(normalized);
            _lastLocalInput = normalized;
            if (_session.PlayerCount > 1)
            {
                _unacked[tick] = normalized;
            }
            return normalized;
        }

        /// <summary>
        /// 相手 remotePlayer に送るメッセージを作る。確認されていない自分の入力（古い方から最大 MaxInputsPerMessage 個）と、
        /// 相手の入力をどこまで受け取ったかを入れる。毎フレーム、相手ごとに送る。
        /// </summary>
        public InputMessage BuildMessage(int remotePlayer)
        {
            CheckRemote(remotePlayer);
            int start = _ackedBy[remotePlayer] + 1;
            int last = Math.Min(_session.LocalInputTick - 1, start + MaxInputsPerMessage - 1);
            var inputs = new List<PlayerInput>(Math.Max(0, last - start + 1));
            for (int tick = start; tick <= last; tick++)
            {
                inputs.Add(_unacked[tick]);
            }
            int now = _session.CurrentTick;
            return new InputMessage(
                _session.LocalPlayer, now, _timeSync.AdvantageOver(remotePlayer, now), _session.ContiguousTick(remotePlayer), start, inputs);
        }

        /// <summary>届いたメッセージを処理する。同じメッセージが何度届いても、順番が前後してもよい。</summary>
        public void Receive(InputMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            CheckRemote(message.Sender);
            _timeSync.OnMessage(message.Sender, _session.CurrentTick, message.SenderTick, message.Advantage);
            // 同じ入力の再送や、確定して古くなった入力は、セッションが無視する。
            for (int i = 0; i < message.Inputs.Count; i++)
            {
                _session.AddRemoteInput(message.Sender, message.StartTick + i, message.Inputs[i]);
            }
            if (message.Ack > _ackedBy[message.Sender])
            {
                _ackedBy[message.Sender] = Math.Min(message.Ack, _session.LocalInputTick - 1);
                DiscardAcked();
            }
        }

        // 全員に確認された自分の入力は、もう送らないので捨てる。
        private void DiscardAcked()
        {
            int ackedByAll = int.MaxValue;
            for (int p = 0; p < _ackedBy.Length; p++)
            {
                if (p != _session.LocalPlayer)
                {
                    ackedByAll = Math.Min(ackedByAll, _ackedBy[p]);
                }
            }
            while (_oldestUnacked <= ackedByAll)
            {
                _unacked.Remove(_oldestUnacked);
                _oldestUnacked++;
            }
        }

        private void CheckRemote(int player)
        {
            if (player == _session.LocalPlayer || player < 0 || player >= _session.PlayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(player), player,
                    $"相手のプレイヤーの番号は 0〜{_session.PlayerCount - 1}（自分の {_session.LocalPlayer} を除く）");
            }
        }
    }
}
