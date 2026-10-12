using System;
using System.IO;
using System.Text;

namespace Degunatsu.Net
{
    /// <summary>セッションのメッセージの種類（先頭の1バイト）。</summary>
    public enum NetKind : byte
    {
        /// <summary>ホスト → クライアント: あなたのプレイヤーの番号。</summary>
        Welcome = 1,

        /// <summary>ホスト → 全員: 試合の開始（人数と調整値）。</summary>
        Start = 2,

        /// <summary>入力のメッセージ（InputMessage）。宛先のプレイヤーの番号つき。ホストが中継する。</summary>
        Input = 3,

        /// <summary>クライアント → ホスト: 確定した状態のチェックサム（不一致の検出）。</summary>
        Checksum = 4,

        /// <summary>ホスト → クライアント: 満員か、試合中なので参加できない。</summary>
        Refused = 5,
    }

    /// <summary>
    /// セッションのメッセージのバイト列（docs/10_online_plan.md T2-7）。先頭の1バイトが種類（NetKind）。
    /// 壊れたデータは FormatException。
    /// </summary>
    public static class NetMessages
    {
        public static NetKind KindOf(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                throw new FormatException("空のメッセージ");
            }
            return (NetKind)data[0];
        }

        public static byte[] Welcome(int player)
        {
            return new[] { (byte)NetKind.Welcome, (byte)player };
        }

        public static int ReadWelcome(byte[] data)
        {
            Expect(data, NetKind.Welcome, 2);
            return data[1];
        }

        public static byte[] Refused()
        {
            return new[] { (byte)NetKind.Refused };
        }

        public static byte[] Start(int playerCount, string configJson)
        {
            byte[] config = Encoding.UTF8.GetBytes(configJson ?? string.Empty);
            var data = new byte[2 + config.Length];
            data[0] = (byte)NetKind.Start;
            data[1] = (byte)playerCount;
            Array.Copy(config, 0, data, 2, config.Length);
            return data;
        }

        public static void ReadStart(byte[] data, out int playerCount, out string configJson)
        {
            Expect(data, NetKind.Start, 2);
            playerCount = data[1];
            configJson = Encoding.UTF8.GetString(data, 2, data.Length - 2);
        }

        /// <summary>宛先 target への入力のメッセージ（InputMessage.ToBytes）を包む。</summary>
        public static byte[] Input(int target, byte[] inputMessage)
        {
            var data = new byte[2 + inputMessage.Length];
            data[0] = (byte)NetKind.Input;
            data[1] = (byte)target;
            Array.Copy(inputMessage, 0, data, 2, inputMessage.Length);
            return data;
        }

        public static int InputTarget(byte[] data)
        {
            Expect(data, NetKind.Input, 2);
            return data[1];
        }

        public static byte[] InputPayload(byte[] data)
        {
            Expect(data, NetKind.Input, 2);
            var payload = new byte[data.Length - 2];
            Array.Copy(data, 2, payload, 0, payload.Length);
            return payload;
        }

        public static byte[] Checksum(int player, int tick, ulong checksum)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)NetKind.Checksum);
                writer.Write((byte)player);
                writer.Write(tick);
                writer.Write(checksum);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void ReadChecksum(byte[] data, out int player, out int tick, out ulong checksum)
        {
            Expect(data, NetKind.Checksum, 1 + 1 + 4 + 8);
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                reader.ReadByte();
                player = reader.ReadByte();
                tick = reader.ReadInt32();
                checksum = reader.ReadUInt64();
            }
        }

        private static void Expect(byte[] data, NetKind kind, int minimumLength)
        {
            if (data == null || data.Length < minimumLength || (NetKind)data[0] != kind)
            {
                throw new FormatException($"{kind} のメッセージではありません（{data?.Length ?? 0} バイト）");
            }
        }
    }
}
