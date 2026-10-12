using System;
using System.Collections.Generic;
using System.IO;
using Degunatsu.Core.Input;

namespace Degunatsu.Core.Rollback
{
    /// <summary>
    /// プレイヤーが相手1人に送るメッセージ（docs/10_online_plan.md T2-4、docs/04_network.md 3.4）。
    /// 相手にまだ確認されていない自分の入力（StartTick から連続したティックの分）と、
    /// 相手の入力をどこまで受け取ったか（確認応答）を1つにまとめる。欠落に備えて、確認されるまで毎回送り直す。
    /// 通信路に依存しないバイト列にする（形式は FormatVersion）。
    /// </summary>
    public sealed class InputMessage
    {
        /// <summary>バイト列の形式の版。</summary>
        public const byte FormatVersion = 1;

        /// <summary>1つのメッセージに入れる入力の最大の数。</summary>
        public const int MaxInputs = 255;

        // 1つの入力のバイト数（移動 X / Y、照準 X / Y、ボタン）。
        private const int InputBytes = 6;

        private readonly List<PlayerInput> _inputs;

        public InputMessage(int sender, int ack, int startTick, IEnumerable<PlayerInput> inputs)
        {
            if (sender < 0 || sender > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(sender), sender, "送り手の番号は 0〜255");
            }
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }
            _inputs = new List<PlayerInput>(inputs);
            if (_inputs.Count > MaxInputs)
            {
                throw new ArgumentException($"入力は {MaxInputs} 個まで（{_inputs.Count} 個）", nameof(inputs));
            }
            Sender = sender;
            Ack = ack;
            StartTick = startTick;
        }

        /// <summary>送り手のプレイヤーの番号。</summary>
        public int Sender { get; }

        /// <summary>送り手が、受け手の入力をこのティックまで（連続して）受け取っている。</summary>
        public int Ack { get; }

        /// <summary>Inputs[0] のティック。Inputs[i] はティック StartTick + i の入力。</summary>
        public int StartTick { get; }

        public IReadOnlyList<PlayerInput> Inputs => _inputs;

        public byte[] ToBytes()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(FormatVersion);
                writer.Write((byte)Sender);
                writer.Write(Ack);
                writer.Write(StartTick);
                writer.Write((byte)_inputs.Count);
                foreach (PlayerInput input in _inputs)
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

        /// <summary>ToBytes の形式を読む。形式が違えば FormatException（壊れたパケットや別のゲームのパケット）。</summary>
        public static InputMessage FromBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            const int headerBytes = 1 + 1 + 4 + 4 + 1;
            if (bytes.Length < headerBytes)
            {
                throw new FormatException($"入力のメッセージが短すぎます（{bytes.Length} バイト）");
            }
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                byte version = reader.ReadByte();
                if (version != FormatVersion)
                {
                    throw new FormatException($"対応していない入力のメッセージの版です: {version}（対応している版: {FormatVersion}）");
                }
                int sender = reader.ReadByte();
                int ack = reader.ReadInt32();
                int startTick = reader.ReadInt32();
                int count = reader.ReadByte();
                if (bytes.Length != headerBytes + count * InputBytes)
                {
                    throw new FormatException($"入力のメッセージの長さが合いません（入力 {count} 個、{bytes.Length} バイト）");
                }
                var inputs = new PlayerInput[count];
                for (int i = 0; i < count; i++)
                {
                    sbyte moveX = reader.ReadSByte();
                    sbyte moveY = reader.ReadSByte();
                    sbyte aimX = reader.ReadSByte();
                    sbyte aimY = reader.ReadSByte();
                    var buttons = (InputButtons)reader.ReadUInt16();
                    inputs[i] = new PlayerInput(moveX, moveY, buttons, aimX, aimY);
                }
                return new InputMessage(sender, ack, startTick, inputs);
            }
        }
    }
}
