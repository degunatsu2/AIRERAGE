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

        /// <summary>
        /// クライアント → ホスト: 自分が入りたいチーム（1〜4。待機所で。チーム対戦のときだけ。docs/19 T9-5）。ホストはだれのチームでも
        /// 変えられる（D-78）。
        /// </summary>
        Team = 12,

        /// <summary>
        /// クライアント → ホスト: 観戦の待機所に入るか（1）、出て試合に参加するか（0）。待機所で。観戦の人は試合に参加せず、見るだけ
        /// （docs/19。ユーザーの要望、2026-11-02）。ホストは観戦にできない。
        /// </summary>
        Spectate = 13,

        /// <summary>観戦の人 → ホスト: 試合（番号）を見始めた（ホストは確定した入力を送り始める。開始のメッセージの送り直しも止める）。</summary>
        Watching = 14,

        /// <summary>ホスト → 観戦の人: 確定したティックの全員の入力（試合の番号、最初のティック、人数、ティックごとの全員の入力）。</summary>
        SpectateInputs = 15,

        /// <summary>
        /// ホスト → 全員: キャラクター選択・ステージ選択・試合の途中に、人が部屋に入った（プレイヤーネーム）。入った人は試合が終わるまで
        /// 待機所で待つ。試合中の人の画面の左下に「名前 JOIN」を出す（2026-11-03 のユーザーの要望）。
        /// </summary>
        Joined = 16,

        /// <summary>
        /// クライアント → ホスト: アイテムを選ぶ画面を開いているか（待機所の「アイテム選択中……」）と、プリアイテムで持って始めるアイテム
        /// （最大 3 つ。ItemId。ユーザーの要望、2026-11-05。docs/20_item_plan.md）。変えるたびに送る。
        /// </summary>
        Items = 17,
    }

    /// <summary>
    /// 待機所でホストが決めるアイテムのルール（モード・初期アイテム数・ドロップ頻度・ドロップするアイテム。docs/20_item_plan.md）。
    /// ロビーのメッセージで全員へ送り、試合の開始のときに MatchRules に入れる。
    /// </summary>
    public readonly struct LobbyItems : IEquatable<LobbyItems>
    {
        public readonly Airerage.Core.Items.ItemMode Mode;
        public readonly int StartCount;
        public readonly int DropRate;
        public readonly uint Pool;

        public LobbyItems(Airerage.Core.Items.ItemMode mode, int startCount, int dropRate, uint pool)
        {
            Mode = mode >= Airerage.Core.Items.ItemMode.Default && mode <= Airerage.Core.Items.ItemMode.PreItem ? mode : Airerage.Core.Items.ItemMode.Default;
            StartCount = Math.Max(1, Math.Min(Airerage.Core.Items.ItemCatalog.BagSlots, startCount));
            DropRate = Math.Max(0, Math.Min(2, dropRate));
            uint valid = pool & Airerage.Core.Items.ItemCatalog.AllMask;
            Pool = valid == 0 ? Airerage.Core.Items.ItemCatalog.AllMask : valid;
        }

        /// <summary>初めの値（アイテムなし、3 個、中、すべてのアイテム）。</summary>
        public static LobbyItems Default => new LobbyItems(Airerage.Core.Items.ItemMode.Default, Airerage.Core.Items.ItemCatalog.BagSlots, 1,
            Airerage.Core.Items.ItemCatalog.AllMask);

        public LobbyItems WithMode(Airerage.Core.Items.ItemMode mode) => new LobbyItems(mode, StartCount, DropRate, Pool);
        public LobbyItems WithStartCount(int count) => new LobbyItems(Mode, count, DropRate, Pool);
        public LobbyItems WithDropRate(int rate) => new LobbyItems(Mode, StartCount, rate, Pool);
        public LobbyItems WithPool(uint pool) => new LobbyItems(Mode, StartCount, DropRate, pool);

        /// <summary>試合のルールに入れる。</summary>
        public void ApplyTo(Airerage.Core.Match.MatchRules rules)
        {
            rules.ItemMode = Mode;
            rules.StartItemCount = StartCount;
            rules.ItemDropRate = DropRate;
            rules.ItemDropPool = (int)Pool;
        }

        public bool Equals(LobbyItems other)
        {
            return Mode == other.Mode && StartCount == other.StartCount && DropRate == other.DropRate && Pool == other.Pool;
        }

        public override bool Equals(object obj)
        {
            return obj is LobbyItems other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)Mode, StartCount, DropRate, Pool);
        }
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

        /// <summary>チームの番号（1〜4。チーム対戦でなければ 0。docs/19 T9-5）。</summary>
        public readonly byte Team;

        /// <summary>観戦の待機所にいるか（試合に参加せず、見るだけ。docs/19）。</summary>
        public readonly bool Spectator;

        /// <summary>
        /// 途中から入って、今の選択・試合が終わるのを待っているか（2026-11-03）。次の試合（再戦・キャラクター選択へ戻る・待機所へ戻る）から
        /// 参加する。
        /// </summary>
        public readonly bool Late;

        /// <summary>アイテムを選ぶ画面を開いているか（待機所に入ったまま。状態は「アイテム選択中……」。docs/20_item_plan.md）。</summary>
        public readonly bool ItemSelecting;

        public LobbySlot(bool connected, string name, int character = 0, bool ready = false, byte team = 0, bool spectator = false,
            bool late = false, bool itemSelecting = false)
        {
            ItemSelecting = itemSelecting;
            Connected = connected;
            Name = name ?? string.Empty;
            Character = character;
            Ready = ready;
            Team = team;
            Spectator = spectator;
            Late = late;
        }

        /// <summary>今の試合に参加する人か（つながっていて、観戦でも途中から入って待っている人でもない）。</summary>
        public bool Plays => Connected && !Spectator && !Late;

        public bool Equals(LobbySlot other)
        {
            return Connected == other.Connected && Name == other.Name && Character == other.Character && Ready == other.Ready
                && Team == other.Team && Spectator == other.Spectator && Late == other.Late && ItemSelecting == other.ItemSelecting;
        }

        public override bool Equals(object obj)
        {
            return obj is LobbySlot other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Connected, Name, Character, Ready, Team, Spectator, Late, ItemSelecting);
        }

        public override string ToString()
        {
            return Connected
                ? $"[{Name} #{Character}{(Team != 0 ? $" team {Team}" : string.Empty)}{(Spectator ? " spectator" : string.Empty)}{(Late ? " late" : string.Empty)}{(Ready ? " ready" : string.Empty)}]"
                : "[-]";
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
        /// ロビーの顔ぶれと段階（段階、ホストが選んでいるステージの番号、チーム対戦か、ホストの番号、番号の順に つながっているか・
        /// キャラクターの番号・準備完了か・チームの番号・観戦か・途中から入ったか・アイテムを選んでいるか・プレイヤーネームの UTF-8、
        /// 最後にアイテムのルール（モード・初期アイテム数・ドロップ頻度・ドロップするアイテムのビット 4 バイト））。
        /// </summary>
        public static byte[] Lobby(IReadOnlyList<LobbySlot> slots, LobbyPhase phase = LobbyPhase.Waiting, int stage = 0, bool teamMode = false,
            int hostSlot = 0, bool matchRunning = false, LobbyItems? items = null)
        {
            LobbyItems rules = items ?? LobbyItems.Default;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)NetKind.Lobby);
                writer.Write((byte)slots.Count);
                writer.Write((byte)phase);
                writer.Write((byte)Math.Max(0, Math.Min(byte.MaxValue, stage)));
                writer.Write(teamMode);
                writer.Write((byte)Math.Max(0, Math.Min(byte.MaxValue, hostSlot)));
                writer.Write(matchRunning);
                foreach (LobbySlot slot in slots)
                {
                    byte[] name = Encoding.UTF8.GetBytes(slot.Name);
                    writer.Write(slot.Connected);
                    writer.Write((byte)Math.Max(0, Math.Min(byte.MaxValue, slot.Character)));
                    writer.Write(slot.Ready);
                    writer.Write(slot.Team);
                    writer.Write(slot.Spectator);
                    writer.Write(slot.Late);
                    writer.Write(slot.ItemSelecting);
                    writer.Write((byte)Math.Min(name.Length, byte.MaxValue));
                    writer.Write(name, 0, Math.Min(name.Length, byte.MaxValue));
                }
                writer.Write((byte)rules.Mode);
                writer.Write((byte)rules.StartCount);
                writer.Write((byte)rules.DropRate);
                writer.Write(rules.Pool);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static LobbySlot[] ReadLobby(byte[] data)
        {
            return ReadLobby(data, out _, out _, out _, out _);
        }

        public static LobbySlot[] ReadLobby(byte[] data, out LobbyPhase phase, out int stage)
        {
            return ReadLobby(data, out phase, out stage, out _, out _);
        }

        public static LobbySlot[] ReadLobby(byte[] data, out LobbyPhase phase, out int stage, out bool teamMode)
        {
            return ReadLobby(data, out phase, out stage, out teamMode, out _);
        }

        public static LobbySlot[] ReadLobby(byte[] data, out LobbyPhase phase, out int stage, out bool teamMode, out int hostSlot)
        {
            return ReadLobby(data, out phase, out stage, out teamMode, out hostSlot, out _);
        }

        public static LobbySlot[] ReadLobby(byte[] data, out LobbyPhase phase, out int stage, out bool teamMode, out int hostSlot,
            out bool matchRunning)
        {
            return ReadLobby(data, out phase, out stage, out teamMode, out hostSlot, out matchRunning, out _);
        }

        public static LobbySlot[] ReadLobby(byte[] data, out LobbyPhase phase, out int stage, out bool teamMode, out int hostSlot,
            out bool matchRunning, out LobbyItems items)
        {
            const int header = 7;
            const int slotHeader = 8;
            Expect(data, NetKind.Lobby, header);
            int count = data[1];
            if (data[2] > (byte)LobbyPhase.StageSelect)
            {
                throw new FormatException($"ロビーの段階が不正です（{data[2]}）");
            }
            phase = (LobbyPhase)data[2];
            stage = data[3];
            teamMode = data[4] != 0;
            hostSlot = data[5];
            matchRunning = data[6] != 0;
            var slots = new LobbySlot[count];
            int offset = header;
            for (int i = 0; i < count; i++)
            {
                if (offset + slotHeader > data.Length || offset + slotHeader + data[offset + slotHeader - 1] > data.Length)
                {
                    throw new FormatException($"ロビーのメッセージが途中で切れています（{data.Length} バイト）");
                }
                bool connected = data[offset] != 0;
                int character = data[offset + 1];
                bool ready = data[offset + 2] != 0;
                byte team = data[offset + 3];
                if (team > Airerage.Core.Match.TeamRules.MaxTeams)
                {
                    throw new FormatException($"チームの番号が不正です（{team}）");
                }
                bool spectator = data[offset + 4] != 0;
                bool late = data[offset + 5] != 0;
                bool itemSelecting = data[offset + 6] != 0;
                int length = data[offset + 7];
                slots[i] = new LobbySlot(connected, Encoding.UTF8.GetString(data, offset + slotHeader, length), character, ready, team, spectator,
                    late, itemSelecting);
                offset += slotHeader + length;
            }
            const int itemBytes = 7;
            if (offset + itemBytes > data.Length)
            {
                throw new FormatException($"ロビーのメッセージのアイテムのルールが途中で切れています（{data.Length} バイト）");
            }
            if (data[offset] > (byte)Airerage.Core.Items.ItemMode.PreItem)
            {
                throw new FormatException($"アイテムのモードが不正です（{data[offset]}）");
            }
            items = new LobbyItems((Airerage.Core.Items.ItemMode)data[offset], data[offset + 1], data[offset + 2], (uint)(data[offset + 3] | data[offset + 4] << 8 | data[offset + 5] << 16 | data[offset + 6] << 24));
            return slots;
        }

        /// <summary>アイテムを選ぶ画面を開いているかと、プリアイテムで持って始めるアイテム（最大 3 つ。クライアント → ホスト）。</summary>
        public static byte[] Items(bool selecting, IReadOnlyList<Airerage.Core.Items.ItemId> items)
        {
            var data = new byte[2 + Airerage.Core.Items.ItemCatalog.BagSlots];
            data[0] = (byte)NetKind.Items;
            data[1] = selecting ? (byte)1 : (byte)0;
            for (int i = 0; items != null && i < items.Count && i < Airerage.Core.Items.ItemCatalog.BagSlots; i++)
            {
                data[2 + i] = Airerage.Core.Items.ItemCatalog.IsValid(items[i]) ? (byte)items[i] : (byte)0;
            }
            return data;
        }

        public static Airerage.Core.Items.ItemId[] ReadItems(byte[] data, out bool selecting)
        {
            Expect(data, NetKind.Items, 2 + Airerage.Core.Items.ItemCatalog.BagSlots);
            selecting = data[1] != 0;
            var items = new Airerage.Core.Items.ItemId[Airerage.Core.Items.ItemCatalog.BagSlots];
            for (int i = 0; i < items.Length; i++)
            {
                var id = (Airerage.Core.Items.ItemId)data[2 + i];
                items[i] = Airerage.Core.Items.ItemCatalog.IsValid(id) ? id : Airerage.Core.Items.ItemId.None;
            }
            return items;
        }

        /// <summary>入りたいチーム（1〜4。クライアント → ホスト）。</summary>
        public static byte[] Team(int team)
        {
            return new[] { (byte)NetKind.Team, (byte)Math.Max(0, Math.Min(byte.MaxValue, team)) };
        }

        public static int ReadTeam(byte[] data)
        {
            Expect(data, NetKind.Team, 2);
            return data[1];
        }

        /// <summary>途中から人が入った（ホスト → 全員。プレイヤーネームの UTF-8）。</summary>
        public static byte[] Joined(string name)
        {
            byte[] text = Encoding.UTF8.GetBytes(name ?? string.Empty);
            var data = new byte[1 + text.Length];
            data[0] = (byte)NetKind.Joined;
            Array.Copy(text, 0, data, 1, text.Length);
            return data;
        }

        public static string ReadJoined(byte[] data)
        {
            Expect(data, NetKind.Joined, 1);
            return Encoding.UTF8.GetString(data, 1, data.Length - 1);
        }

        /// <summary>観戦の待機所に入るか（クライアント → ホスト）。</summary>
        public static byte[] Spectate(bool spectate)
        {
            return new[] { (byte)NetKind.Spectate, (byte)(spectate ? 1 : 0) };
        }

        public static bool ReadSpectate(byte[] data)
        {
            Expect(data, NetKind.Spectate, 2);
            return data[1] != 0;
        }

        /// <summary>試合 match を見始めた（観戦の人 → ホスト）。</summary>
        public static byte[] Watching(int match)
        {
            return new[] { (byte)NetKind.Watching, (byte)match };
        }

        public static int ReadWatching(byte[] data)
        {
            Expect(data, NetKind.Watching, 2);
            return data[1];
        }

        /// <summary>1 つの観戦の入力のメッセージに入れるティックの最大の数（8 人で約 6 KB。確実に届ける経路の大きさ 16 KB に収める）。</summary>
        public const int MaxSpectateTicks = 120;

        /// <summary>
        /// 確定したティックの全員の入力（ホスト → 観戦の人）。ticks[i] はティック startTick + i の、プレイヤーの番号の順の入力（人数 players）。
        /// </summary>
        public static byte[] SpectateInputs(int match, int startTick, int players, IReadOnlyList<PlayerInput[]> ticks)
        {
            if (ticks.Count > MaxSpectateTicks)
            {
                throw new ArgumentException($"ティックは {MaxSpectateTicks} 個まで（{ticks.Count} 個）", nameof(ticks));
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)NetKind.SpectateInputs);
                writer.Write((byte)match);
                writer.Write(startTick);
                writer.Write((byte)players);
                writer.Write((ushort)ticks.Count);
                foreach (PlayerInput[] inputs in ticks)
                {
                    if (inputs.Length != players)
                    {
                        throw new ArgumentException($"入力の数（{inputs.Length}）が人数（{players}）と違う", nameof(ticks));
                    }
                    foreach (PlayerInput input in inputs)
                    {
                        writer.Write(input.MoveX);
                        writer.Write(input.MoveY);
                        writer.Write(input.AimX);
                        writer.Write(input.AimY);
                        writer.Write((ushort)input.Buttons);
                    }
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void ReadSpectateInputs(byte[] data, out int match, out int startTick, out PlayerInput[][] ticks)
        {
            const int header = 1 + 1 + 4 + 1 + 2;
            Expect(data, NetKind.SpectateInputs, header);
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                reader.ReadByte();
                match = reader.ReadByte();
                startTick = reader.ReadInt32();
                int players = reader.ReadByte();
                int count = reader.ReadUInt16();
                if (players < 1 || data.Length != header + count * players * 6)
                {
                    throw new FormatException($"観戦の入力のメッセージの長さが合いません（{count} ティック、{players} 人、{data.Length} バイト）");
                }
                ticks = new PlayerInput[count][];
                for (int i = 0; i < count; i++)
                {
                    ticks[i] = new PlayerInput[players];
                    for (int p = 0; p < players; p++)
                    {
                        sbyte moveX = reader.ReadSByte();
                        sbyte moveY = reader.ReadSByte();
                        sbyte aimX = reader.ReadSByte();
                        sbyte aimY = reader.ReadSByte();
                        var buttons = (InputButtons)reader.ReadUInt16();
                        ticks[i][p] = new PlayerInput(moveX, moveY, buttons, aimX, aimY);
                    }
                }
            }
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
