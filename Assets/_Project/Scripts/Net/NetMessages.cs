using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Airerage.Core.Input;

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

        /// <summary>クライアント → ホスト: 準備完了か（待機所の READY、キャラクター選択の決定。docs/15 T6-7）。</summary>
        Ready = 9,

        /// <summary>
        /// ホスト → クライアント: 試合中にプレイヤーが抜けた（試合の番号、抜けた人、抜けたティック、その前までのその人の入力。docs/15 T6-10）。
        /// </summary>
        Left = 10,

        /// <summary>
        /// クライアント → ホスト: RANDOM で選んでよいステージ（StageCatalog の番号のビット。オプションの RANDOM STAGES。ロビーに入ったときに
        /// 送る。ユーザーの要望、2026-10-26）。ホストは全員が選んでよいステージだけから RANDOM で選ぶ。
        /// </summary>
        StagePool = 11,
    }

    /// <summary>
    /// ロビーの段階（docs/15 T6-7）: 待機所 → 全員が準備完了でキャラクター選択 → 全員が決定でステージ選択（ホストが選ぶ）→ 試合。
    /// 段階はホストが決めて、ロビーのメッセージで全員へ送る。
    /// </summary>
    public enum LobbyPhase : byte
    {
        Waiting = 0,
        CharacterSelect = 1,
        StageSelect = 2,
    }

    /// <summary>ロビーの1つの番号（待機所の表示に使う）。</summary>
    public readonly struct LobbySlot : IEquatable<LobbySlot>
    {
        public readonly bool Connected;

        /// <summary>プレイヤーネーム（入力がなければ空）。</summary>
        public readonly string Name;

        /// <summary>選んだキャラクターの番号（CharacterRoster の番号）。</summary>
        public readonly int Character;

        /// <summary>準備完了か（待機所の READY、キャラクター選択の決定）。</summary>
        public readonly bool Ready;

        public LobbySlot(bool connected, string name, int character = 0, bool ready = false)
        {
            Connected = connected;
            Name = name ?? string.Empty;
            Character = character;
            Ready = ready;
        }

        public bool Equals(LobbySlot other)
        {
            return Connected == other.Connected && Name == other.Name && Character == other.Character && Ready == other.Ready;
        }

        public override bool Equals(object obj)
        {
            return obj is LobbySlot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Connected, Name, Character, Ready);
        }

        public override string ToString()
        {
            return Connected ? $"[{Name} #{Character}{(Ready ? " ready" : string.Empty)}]" : "[-]";
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

        /// <summary>RANDOM で選んでよいステージの番号のビット。</summary>
        public static byte[] StagePool(ulong mask)
        {
            var data = new byte[9];
            data[0] = (byte)NetKind.StagePool;
            for (int i = 0; i < 8; i++)
            {
                data[1 + i] = (byte)(mask >> (8 * i));
            }
            return data;
        }

        public static ulong ReadStagePool(byte[] data)
        {
            Expect(data, NetKind.StagePool, 9);
            ulong mask = 0;
            for (int i = 0; i < 8; i++)
            {
                mask |= (ulong)data[1 + i] << (8 * i);
            }
            return mask;
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

        /// <summary>
        /// ロビーの顔ぶれと段階（段階、ホストが選んでいるステージの番号、番号の順に つながっているか・キャラクターの番号・準備完了か・
        /// プレイヤーネームの UTF-8）。
        /// </summary>
        public static byte[] Lobby(IReadOnlyList<LobbySlot> slots, LobbyPhase phase = LobbyPhase.Waiting, int stage = 0)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)NetKind.Lobby);
                writer.Write((byte)slots.Count);
                writer.Write((byte)phase);
                writer.Write((byte)Math.Max(0, Math.Min(byte.MaxValue, stage)));
                foreach (LobbySlot slot in slots)
                {
                    byte[] name = Encoding.UTF8.GetBytes(slot.Name);
                    writer.Write(slot.Connected);
                    writer.Write((byte)Math.Max(0, Math.Min(byte.MaxValue, slot.Character)));
                    writer.Write(slot.Ready);
                    writer.Write((byte)Math.Min(name.Length, byte.MaxValue));
                    writer.Write(name, 0, Math.Min(name.Length, byte.MaxValue));
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static LobbySlot[] ReadLobby(byte[] data)
        {
            return ReadLobby(data, out _, out _);
        }

        public static LobbySlot[] ReadLobby(byte[] data, out LobbyPhase phase, out int stage)
        {
            Expect(data, NetKind.Lobby, 4);
            int count = data[1];
            if (data[2] > (byte)LobbyPhase.StageSelect)
            {
                throw new FormatException($"ロビーの段階が不正です（{data[2]}）");
            }
            phase = (LobbyPhase)data[2];
            stage = data[3];
            var slots = new LobbySlot[count];
            int offset = 4;
            for (int i = 0; i < count; i++)
            {
                if (offset + 4 > data.Length || offset + 4 + data[offset + 3] > data.Length)
                {
                    throw new FormatException($"ロビーのメッセージが途中で切れています（{data.Length} バイト）");
                }
                bool connected = data[offset] != 0;
                int character = data[offset + 1];
                bool ready = data[offset + 2] != 0;
                int length = data[offset + 3];
                slots[i] = new LobbySlot(connected, Encoding.UTF8.GetString(data, offset + 4, length), character, ready);
                offset += 4 + length;
            }
            return slots;
        }

        /// <summary>準備完了か（クライアント → ホスト）。</summary>
        public static byte[] Ready(bool ready)
        {
            return new[] { (byte)NetKind.Ready, (byte)(ready ? 1 : 0) };
        }

        public static bool ReadReady(byte[] data)
        {
            Expect(data, NetKind.Ready, 2);
            return data[1] != 0;
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

        /// <summary>1 つの抜けた知らせに入れられる入力の最大の数（確実に届ける経路の大きさ 16 KB に収める）。</summary>
        public const int MaxLeftInputs = 2400;

        /// <summary>
        /// プレイヤー player が抜けた（試合の番号 match）。fromTick からその人の入力は抜けた人の入力。inputs はティック startTick からの、
        /// fromTick より前のその人の入力（受け手にまだ届いていないかもしれない分）。
        /// </summary>
        public static byte[] Left(int match, int player, int fromTick, int startTick, IReadOnlyList<PlayerInput> inputs)
        {
            if (inputs.Count > MaxLeftInputs)
            {
                throw new ArgumentException($"入力は {MaxLeftInputs} 個まで（{inputs.Count} 個）", nameof(inputs));
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)NetKind.Left);
                writer.Write((byte)match);
                writer.Write((byte)player);
                writer.Write(fromTick);
                writer.Write(startTick);
                writer.Write((ushort)inputs.Count);
                foreach (PlayerInput input in inputs)
                {
                    writer.Write(input.MoveX);
                    writer.Write(input.MoveY);
                    writer.Write(input.AimX);
                    writer.Write(input.AimY);
                    writer.Write((ushort)input.Buttons);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void ReadLeft(byte[] data, out int match, out int player, out int fromTick, out int startTick,
            out PlayerInput[] inputs)
        {
            const int header = 1 + 1 + 1 + 4 + 4 + 2;
            Expect(data, NetKind.Left, header);
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                reader.ReadByte();
                match = reader.ReadByte();
                player = reader.ReadByte();
                fromTick = reader.ReadInt32();
                startTick = reader.ReadInt32();
                int count = reader.ReadUInt16();
                if (data.Length != header + count * 6)
                {
                    throw new FormatException($"抜けた知らせの長さが合いません（入力 {count} 個、{data.Length} バイト）");
                }
                inputs = new PlayerInput[count];
                for (int i = 0; i < count; i++)
                {
                    sbyte moveX = reader.ReadSByte();
                    sbyte moveY = reader.ReadSByte();
                    sbyte aimX = reader.ReadSByte();
                    sbyte aimY = reader.ReadSByte();
                    var buttons = (InputButtons)reader.ReadUInt16();
                    inputs[i] = new PlayerInput(moveX, moveY, buttons, aimX, aimY);
                }
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
