using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Airerage.Net
{
    /// <summary>セッションのメッセージの種類（先頭の1バイト）。</summary>
    public enum NetKind : byte
    {
        /// <summary>ホスト → クライアント: あなたのプレイヤーの番号。</summary>
        Welcome = 1,

        /// <summary>ホスト → 全員: 試合の開始（人数、試合の番号、調整値とルール）。再戦のときも送る。</summary>
        Start = 2,

        /// <summary>入力のメッセージ（InputMessage）。宛先のプレイヤーの番号つき。ホストが中継する。</summary>
        Input = 3,

        /// <summary>クライアント → ホスト: 確定した状態のチェックサム（不一致の検出）。</summary>
        Checksum = 4,

        /// <summary>ホスト → クライアント: 満員か、試合中なので参加できない。</summary>
        Refused = 5,

        /// <summary>クライアント → ホスト: 自分のプレイヤーネーム（ロビーで。変えるたびに送る）。</summary>
        Hello = 6,

        /// <summary>ホスト → クライアント: ロビーの顔ぶれ（番号ごとに、つながっているか、プレイヤーネーム、キャラクター）。変わるたびに送る。</summary>
        Lobby = 7,

        /// <summary>クライアント → ホスト: 自分が選んだキャラクターの番号（ロビーで。変えるたびに送る。docs/13 T5-5）。</summary>
        Pick = 8,
    }

    /// <summary>ロビーの1つの番号（待機所の表示に使う）。</summary>
    public readonly struct LobbySlot : IEquatable<LobbySlot>
    {
        public readonly bool Connected;

        /// <summary>プレイヤーネーム（入力がなければ空）。</summary>
        public readonly string Name;

        /// <summary>選んだキャラクターの番号（CharacterRoster の番号）。</summary>
        public readonly int Character;

        public LobbySlot(bool connected, string name, int character = 0)
        {
            Connected = connected;
            Name = name ?? string.Empty;
            Character = character;
        }

        public bool Equals(LobbySlot other)
        {
            return Connected == other.Connected && Name == other.Name && Character == other.Character;
        }

        public override bool Equals(object obj)
        {
            return obj is LobbySlot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Connected, Name, Character);
        }

        public override string ToString()
        {
            return Connected ? $"[{Name} #{Character}]" : "[-]";
        }
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

        /// <summary>プレイヤーネーム（UTF-8）。</summary>
        public static byte[] Hello(string name)
        {
            byte[] text = Encoding.UTF8.GetBytes(name ?? string.Empty);
            var data = new byte[1 + text.Length];
            data[0] = (byte)NetKind.Hello;
            Array.Copy(text, 0, data, 1, text.Length);
            return data;
        }

        public static string ReadHello(byte[] data)
        {
            Expect(data, NetKind.Hello, 1);
            return Encoding.UTF8.GetString(data, 1, data.Length - 1);
        }

        /// <summary>選んだキャラクターの番号（0〜255）。</summary>
        public static byte[] Pick(int character)
        {
            return new[] { (byte)NetKind.Pick, (byte)Math.Max(0, Math.Min(byte.MaxValue, character)) };
        }

        public static int ReadPick(byte[] data)
        {
            Expect(data, NetKind.Pick, 2);
            return data[1];
        }

        /// <summary>ロビーの顔ぶれ（番号の順。つながっているか、キャラクターの番号、プレイヤーネームの UTF-8）。</summary>
        public static byte[] Lobby(IReadOnlyList<LobbySlot> slots)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)NetKind.Lobby);
                writer.Write((byte)slots.Count);
                foreach (LobbySlot slot in slots)
                {
                    byte[] name = Encoding.UTF8.GetBytes(slot.Name);
                    writer.Write(slot.Connected);
                    writer.Write((byte)Math.Max(0, Math.Min(byte.MaxValue, slot.Character)));
                    writer.Write((byte)Math.Min(name.Length, byte.MaxValue));
                    writer.Write(name, 0, Math.Min(name.Length, byte.MaxValue));
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static LobbySlot[] ReadLobby(byte[] data)
        {
            Expect(data, NetKind.Lobby, 2);
            int count = data[1];
            var slots = new LobbySlot[count];
            int offset = 2;
            for (int i = 0; i < count; i++)
            {
                if (offset + 3 > data.Length || offset + 3 + data[offset + 2] > data.Length)
                {
                    throw new FormatException($"ロビーのメッセージが途中で切れています（{data.Length} バイト）");
                }
                bool connected = data[offset] != 0;
                int character = data[offset + 1];
                int length = data[offset + 2];
                slots[i] = new LobbySlot(connected, Encoding.UTF8.GetString(data, offset + 3, length), character);
                offset += 3 + length;
            }
            return slots;
        }

        /// <summary>
        /// 試合の開始（人数、試合の番号、調整値とルールの JSON を GZip で縮めたもの）。同じ人で再戦するときも送る（番号が変わる）。
        /// キャラクターが増えると調整値の JSON が大きくなるので縮める（docs/13 T5-5）。
        /// </summary>
        public static byte[] Start(int playerCount, int match, string configJson)
        {
            byte[] json = Encoding.UTF8.GetBytes(configJson ?? string.Empty);
            using (var stream = new MemoryStream())
            {
                stream.WriteByte((byte)NetKind.Start);
                stream.WriteByte((byte)playerCount);
                stream.WriteByte((byte)match);
                using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gzip.Write(json, 0, json.Length);
                }
                return stream.ToArray();
            }
        }

        public static void ReadStart(byte[] data, out int playerCount, out int match, out string configJson)
        {
            Expect(data, NetKind.Start, 3);
            playerCount = data[1];
            match = data[2];
            try
            {
                using (var input = new MemoryStream(data, 3, data.Length - 3))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    configJson = Encoding.UTF8.GetString(output.ToArray());
                }
            }
            catch (IOException e)
            {
                // 壊れたデータ（.NET では InvalidDataException、Unity の Mono では IOException。前者は後者の派生）。
                throw new FormatException($"開始のメッセージの調整値を展開できません: {e.Message}", e);
            }
        }

        /// <summary>
        /// 宛先 target への入力のメッセージ（InputMessage.ToBytes）を包む。試合の番号 match をつける
        /// （再戦の後に、前の試合の入力が遅れて届いても使わないように）。
        /// </summary>
        public static byte[] Input(int match, int target, byte[] inputMessage)
        {
            var data = new byte[3 + inputMessage.Length];
            data[0] = (byte)NetKind.Input;
            data[1] = (byte)match;
            data[2] = (byte)target;
            Array.Copy(inputMessage, 0, data, 3, inputMessage.Length);
            return data;
        }

        public static int InputMatch(byte[] data)
        {
            Expect(data, NetKind.Input, 3);
            return data[1];
        }

        public static int InputTarget(byte[] data)
        {
            Expect(data, NetKind.Input, 3);
            return data[2];
        }

        public static byte[] InputPayload(byte[] data)
        {
            Expect(data, NetKind.Input, 3);
            var payload = new byte[data.Length - 3];
            Array.Copy(data, 3, payload, 0, payload.Length);
            return payload;
        }

        /// <summary>確定した状態のチェックサム（試合の番号つき）。</summary>
        public static byte[] Checksum(int match, int player, int tick, ulong checksum)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)NetKind.Checksum);
                writer.Write((byte)match);
                writer.Write((byte)player);
                writer.Write(tick);
                writer.Write(checksum);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void ReadChecksum(byte[] data, out int match, out int player, out int tick, out ulong checksum)
        {
            Expect(data, NetKind.Checksum, 1 + 1 + 1 + 4 + 8);
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                reader.ReadByte();
                match = reader.ReadByte();
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
