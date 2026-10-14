using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using Debug = UnityEngine.Debug;

namespace Degunatsu.Net
{
    /// <summary>
    /// Unity Transport の薄い包み（docs/10_online_plan.md T2-7）。通信のライブラリへの依存はここに閉じ込める。
    /// ホストは待ち受け、クライアントはホストに接続する。接続ごとに番号（peer）を振る（クライアントから見たホストは 0）。
    /// 経路は2つ: 信頼性なし（入力。失われても次のメッセージで送り直す）と、信頼性あり・順序あり・分割あり（参加や開始などの制御）。
    /// 検証用に、自分が送るパケットに遅延・揺らぎ・欠落を加えられる（SetSimulatedConditions。Unity Transport のシミュレーター。T2-8、T2-9）。
    /// </summary>
    public sealed class TransportLink : IDisposable
    {
        public enum EventKind
        {
            Connected,
            Disconnected,
            Data,
        }

        public readonly struct Event
        {
            public readonly EventKind Kind;
            public readonly int Peer;
            public readonly byte[] Data;

            public Event(EventKind kind, int peer, byte[] data)
            {
                Kind = kind;
                Peer = peer;
                Data = data;
            }
        }

        // 遅延の模擬で、送るのを遅らせて覚えておけるパケットの数（入力は相手1人につき毎秒60個）。
        private const int SimulatorMaxPackets = 512;

        // 信頼性ありの経路で、分けて送れるメッセージの大きさの上限（バイト）。既定の 4096 では、試合の開始のメッセージ
        // （調整値とルールの JSON。約 4KB）に名前が加わると足りなくなるので、余裕を持たせる。両方の側で同じ値にする。
        private const int ReliablePayloadCapacity = 16 * 1024;

        private NetworkDriver _driver;
        private readonly NetworkPipeline _unreliable;
        private readonly NetworkPipeline _reliable;
        private readonly List<NetworkConnection> _connections = new List<NetworkConnection>();
        private bool _disposed;

        private TransportLink(bool isHost)
        {
            IsHost = isHost;
            // 遅延の模擬（シミュレーター）は、どちらの経路にも入れておき、使わないときは止めておく。
            var settings = new NetworkSettings(Allocator.Temp);
            settings.WithSimulatorStageParameters(
                maxPacketCount: SimulatorMaxPackets, mode: ApplyMode.Off, randomSeed: (uint)Stopwatch.GetTimestamp());
            settings.WithFragmentationStageParameters(payloadCapacity: ReliablePayloadCapacity);
            _driver = NetworkDriver.Create(settings);
            _unreliable = _driver.CreatePipeline(typeof(SimulatorPipelineStage));
            _reliable = _driver.CreatePipeline(
                typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
        }

        public bool IsHost { get; }

        /// <summary>自分が送るパケットに加えている遅延（ミリ秒）。</summary>
        public int SimulatedDelayMs { get; private set; }

        /// <summary>自分が送るパケットに加えている遅延の揺らぎ（ミリ秒。遅延 ± この値）。</summary>
        public int SimulatedJitterMs { get; private set; }

        /// <summary>自分が送るパケットを捨てる割合（%）。</summary>
        public int SimulatedDropPercent { get; private set; }

        /// <summary>
        /// 自分が送るパケットに、遅延・揺らぎ・欠落を加える（検証用。すべて 0 なら加えない）。
        /// 往復の遅延を模擬するには、全員が同じ値にする（往復 = 自分の遅延 + 相手の遅延）。
        /// </summary>
        public void SetSimulatedConditions(int delayMs, int jitterMs, int dropPercent)
        {
            if (delayMs < 0 || jitterMs < 0 || jitterMs > delayMs || dropPercent < 0 || dropPercent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(delayMs),
                    $"遅延 {delayMs}ms、揺らぎ {jitterMs}ms、欠落 {dropPercent}%（遅延・揺らぎは 0 以上で、揺らぎは遅延以下。欠落は 0〜100）");
            }
            NetworkSettings current = _driver.CurrentSettings;
            SimulatorUtility.Parameters parameters = current.GetSimulatorStageParameters();
            bool active = delayMs > 0 || dropPercent > 0;
            parameters.Mode = active ? ApplyMode.SentPacketsOnly : ApplyMode.Off;
            parameters.PacketDelayMs = delayMs;
            parameters.PacketJitterMs = jitterMs;
            parameters.PacketDropPercentage = dropPercent;
            _driver.ModifySimulatorStageParameters(parameters);
            SimulatedDelayMs = delayMs;
            SimulatedJitterMs = jitterMs;
            SimulatedDropPercent = dropPercent;
        }

        /// <summary>待ち受けているポート（ホスト）。0 を指定して作ったときは、実際に割り当てられたポート。</summary>
        public ushort LocalPort => _driver.GetLocalEndpoint().Port;

        /// <summary>つながっている相手の数。</summary>
        public int ConnectedPeerCount
        {
            get
            {
                int count = 0;
                foreach (NetworkConnection connection in _connections)
                {
                    if (connection.IsCreated && _driver.GetConnectionState(connection) == NetworkConnection.State.Connected)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>port で待ち受ける（0 なら空いているポート）。失敗したら InvalidOperationException。</summary>
        public static TransportLink Host(ushort port)
        {
            var link = new TransportLink(true);
            if (link._driver.Bind(NetworkEndpoint.AnyIpv4.WithPort(port)) != 0)
            {
                link.Dispose();
                throw new InvalidOperationException($"ポート {port} で待ち受けられません（ほかのプログラムが使っている可能性）");
            }
            if (link._driver.Listen() != 0)
            {
                link.Dispose();
                throw new InvalidOperationException($"ポート {port} で待ち受けを始められません");
            }
            return link;
        }

        /// <summary>address:port のホストに接続する（接続できたかは Update の Connected で分かる）。</summary>
        public static TransportLink Connect(string address, ushort port)
        {
            if (!NetworkEndpoint.TryParse(address, port, out NetworkEndpoint endpoint))
            {
                throw new ArgumentException($"アドレスを読めません: {address}", nameof(address));
            }
            var link = new TransportLink(false);
            link._connections.Add(link._driver.Connect(endpoint));
            return link;
        }

        /// <summary>送受信を進め、起きたことを events に加える。毎フレーム呼ぶ。</summary>
        public void Update(List<Event> events)
        {
            _driver.ScheduleUpdate().Complete();

            if (IsHost)
            {
                NetworkConnection accepted;
                while ((accepted = _driver.Accept()) != default)
                {
                    _connections.Add(accepted);
                    events.Add(new Event(EventKind.Connected, _connections.Count - 1, null));
                }
            }

            for (int peer = 0; peer < _connections.Count; peer++)
            {
                NetworkConnection connection = _connections[peer];
                if (!connection.IsCreated)
                {
                    continue;
                }
                NetworkEvent.Type type;
                while ((type = _driver.PopEventForConnection(connection, out DataStreamReader reader)) != NetworkEvent.Type.Empty)
                {
                    switch (type)
                    {
                        case NetworkEvent.Type.Connect:
                            // ホストでは Accept で知らせているので、二重に知らせない。
                            if (!IsHost)
                            {
                                events.Add(new Event(EventKind.Connected, peer, null));
                            }
                            break;
                        case NetworkEvent.Type.Data:
                            var data = new byte[reader.Length];
                            reader.ReadBytes(new Span<byte>(data));
                            events.Add(new Event(EventKind.Data, peer, data));
                            break;
                        case NetworkEvent.Type.Disconnect:
                            _connections[peer] = default;
                            events.Add(new Event(EventKind.Disconnected, peer, null));
                            break;
                    }
                    if (!_connections[peer].IsCreated)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>相手 peer に送る。reliable なら信頼性あり・順序ありの経路で送る。送れなければ警告を出して false。</summary>
        public bool Send(int peer, byte[] data, bool reliable)
        {
            if (peer < 0 || peer >= _connections.Count || !_connections[peer].IsCreated)
            {
                return false;
            }
            NetworkPipeline pipeline = reliable ? _reliable : _unreliable;
            int status = _driver.BeginSend(pipeline, _connections[peer], out DataStreamWriter writer);
            if (status != 0)
            {
                Debug.LogWarning($"TransportLink: 送信を始められません（相手 {peer}、状態 {status}）");
                return false;
            }
            if (!writer.WriteBytes(new Span<byte>(data)))
            {
                _driver.AbortSend(writer);
                Debug.LogWarning($"TransportLink: メッセージが大きすぎて送れません（{data.Length} バイト）");
                return false;
            }
            int sent = _driver.EndSend(writer);
            if (sent < 0)
            {
                Debug.LogWarning($"TransportLink: 送信に失敗しました（相手 {peer}、状態 {sent}）");
                return false;
            }
            return true;
        }

        /// <summary>相手 peer との接続を切る。</summary>
        public void Disconnect(int peer)
        {
            if (peer >= 0 && peer < _connections.Count && _connections[peer].IsCreated)
            {
                _driver.Disconnect(_connections[peer]);
                _connections[peer] = default;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_driver.IsCreated)
            {
                for (int peer = 0; peer < _connections.Count; peer++)
                {
                    Disconnect(peer);
                }
                _driver.ScheduleUpdate().Complete();
                _driver.Dispose();
            }
        }
    }
}
