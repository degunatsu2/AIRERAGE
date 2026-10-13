using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Degunatsu.Core.Common;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Replay
{
    /// <summary>
    /// 入力の記録（docs/06_prototype_plan.md T1-9b）。開始時の状態と、ティックごとの入力と、
    /// 最後の入力を処理した後の状態のチェックサムを持つ。シミュレーションは決定的なので、
    /// 同じ地形と調整値で再生すれば同じ状態になる（チェックサムで確かめられる。PC 間の比較にも使う）。
    /// キャラクター1人分。テキストにして保存する（同じ入力が続く部分はまとめる）。
    /// </summary>
    public sealed class InputRecording
    {
        /// <summary>テキストの形式の版。</summary>
        public const int FormatVersion = 1;

        private const string Magic = "degunatsu-input-recording";

        private readonly List<PlayerInput> _inputs;

        public InputRecording(
            CharacterState startState, WireModelType wireModelType, IEnumerable<PlayerInput> inputs,
            ulong finalChecksum, string configText)
        {
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }
            configText = configText ?? string.Empty;
            if (configText.IndexOf('\n') >= 0 || configText.IndexOf('\r') >= 0)
            {
                throw new ArgumentException("調整値のテキストは1行にしてください", nameof(configText));
            }
            StartState = startState;
            WireModelType = wireModelType;
            _inputs = new List<PlayerInput>(inputs);
            FinalChecksum = finalChecksum;
            ConfigText = configText;
        }

        public CharacterState StartState { get; }

        /// <summary>記録したときのワイヤーのタイプ。</summary>
        public WireModelType WireModelType { get; }

        public IReadOnlyList<PlayerInput> Inputs => _inputs;

        /// <summary>最後の入力を処理した後の状態のチェックサム（StateCodec.Checksum）。</summary>
        public ulong FinalChecksum { get; }

        /// <summary>記録したときの調整値（Runtime が JSON で入れる。照合と調査用）。空でもよい。</summary>
        public string ConfigText { get; }

        /// <summary>
        /// 地形 collision と調整値 config で記録を再生し、最後の状態を返す。
        /// config のワイヤーのタイプが記録と違えば ArgumentException（違うタイプで再生しても同じ動きにならないため）。
        /// </summary>
        public CharacterState Simulate(ICollisionQuery collision, CharacterConfig config, Action<CharacterState> onTick = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (config.Wire.ModelType != WireModelType)
            {
                throw new ArgumentException(
                    $"ワイヤーのタイプが記録（{WireModelType}）と違います（{config.Wire.ModelType}）", nameof(config));
            }
            var world = new SimWorld(collision);
            world.AddCharacter(config, StartState.Position, StartState.Facing);
            world.SetState(0, StartState);
            var inputs = new PlayerInput[1];
            foreach (PlayerInput input in _inputs)
            {
                inputs[0] = input;
                world.Step(inputs);
                onTick?.Invoke(world.GetState(0));
            }
            return world.GetState(0);
        }

        public string ToText()
        {
            var text = new StringBuilder();
            text.Append(Magic).Append(' ').Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AppendLine(text, "state-format", StateCodec.FormatVersion.ToString(CultureInfo.InvariantCulture));
            AppendLine(text, "tick-rate", SimTime.TicksPerSecond.ToString(CultureInfo.InvariantCulture));
            AppendLine(text, "wire-model", WireModelType.ToString());
            AppendLine(text, "ticks", _inputs.Count.ToString(CultureInfo.InvariantCulture));
            AppendLine(text, "final-checksum", FinalChecksum.ToString("x16", CultureInfo.InvariantCulture));
            AppendLine(text, "start", Convert.ToBase64String(StateCodec.ToBytes(StartState)));
            AppendLine(text, "config", ConfigText);
            text.Append("inputs\n");
            // 1行に「移動 X、移動 Y、照準 X、照準 Y、ボタン、続くティック数」。
            int i = 0;
            while (i < _inputs.Count)
            {
                PlayerInput input = _inputs[i];
                int run = 1;
                while (i + run < _inputs.Count && _inputs[i + run].Equals(input))
                {
                    run++;
                }
                text.Append(input.MoveX.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(input.MoveY.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(input.AimX.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(input.AimY.ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(((ushort)input.Buttons).ToString(CultureInfo.InvariantCulture)).Append(' ')
                    .Append(run.ToString(CultureInfo.InvariantCulture)).Append('\n');
                i += run;
            }
            text.Append("end\n");
            return text.ToString();
        }

        /// <summary>ToText の形式を読む。形式が違えば、行番号付きの FormatException。</summary>
        public static InputRecording Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            var reader = new LineReader(text);

            string[] header = reader.Next().Split(' ');
            if (header.Length != 2 || header[0] != Magic)
            {
                throw reader.Error($"入力の記録ではありません（1行目は「{Magic} 版」）");
            }
            if (ParseInt(reader, header[1]) != FormatVersion)
            {
                throw reader.Error($"対応していない形式の版です: {header[1]}（対応している版: {FormatVersion}）");
            }
            if (ParseInt(reader, reader.Value("state-format")) != StateCodec.FormatVersion)
            {
                throw reader.Error($"対応していない状態の形式です（対応している版: {StateCodec.FormatVersion}）");
            }
            if (ParseInt(reader, reader.Value("tick-rate")) != SimTime.TicksPerSecond)
            {
                throw reader.Error($"ティックレートが違います（このビルドは {SimTime.TicksPerSecond}）");
            }
            string modelName = reader.Value("wire-model");
            if (!Enum.TryParse(modelName, out WireModelType modelType) || !Enum.IsDefined(typeof(WireModelType), modelType))
            {
                throw reader.Error($"不明なワイヤーのタイプです: {modelName}");
            }
            int ticks = ParseInt(reader, reader.Value("ticks"));
            string checksumText = reader.Value("final-checksum");
            if (!ulong.TryParse(checksumText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong checksum))
            {
                throw reader.Error($"チェックサムが16進数ではありません: {checksumText}");
            }
            CharacterState start;
            try
            {
                start = StateCodec.FromBytes(Convert.FromBase64String(reader.Value("start")));
            }
            catch (FormatException e)
            {
                throw reader.Error($"開始時の状態を読めません: {e.Message}");
            }
            string config = reader.Value("config");
            if (reader.Next() != "inputs")
            {
                throw reader.Error("「inputs」の行がありません");
            }

            var inputs = new List<PlayerInput>(ticks);
            while (true)
            {
                string line = reader.Next();
                if (line == "end")
                {
                    break;
                }
                string[] parts = line.Split(' ');
                if (parts.Length != 6)
                {
                    throw reader.Error("入力の行は「移動X 移動Y 照準X 照準Y ボタン ティック数」の6つの数です");
                }
                var input = new PlayerInput(
                    ParseSByte(reader, parts[0]), ParseSByte(reader, parts[1]), (InputButtons)ParseUShort(reader, parts[4]),
                    ParseSByte(reader, parts[2]), ParseSByte(reader, parts[3]));
                int run = ParseInt(reader, parts[5]);
                if (run < 1)
                {
                    throw reader.Error($"ティック数は1以上です: {run}");
                }
                for (int i = 0; i < run; i++)
                {
                    inputs.Add(input);
                }
            }
            if (inputs.Count != ticks)
            {
                throw reader.Error($"入力のティック数（{inputs.Count}）が ticks（{ticks}）と一致しません");
            }
            return new InputRecording(start, modelType, inputs, checksum, config);
        }

        private static void AppendLine(StringBuilder text, string key, string value)
        {
            text.Append(key).Append(' ').Append(value).Append('\n');
        }

        private static int ParseInt(LineReader reader, string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                throw reader.Error($"整数ではありません: {value}");
            }
            return result;
        }

        private static sbyte ParseSByte(LineReader reader, string value)
        {
            if (!sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte result))
            {
                throw reader.Error($"-128〜127 の整数ではありません: {value}");
            }
            return result;
        }

        private static ushort ParseUShort(LineReader reader, string value)
        {
            if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort result))
            {
                throw reader.Error($"0〜65535 の整数ではありません: {value}");
            }
            return result;
        }

        /// <summary>1行ずつ読む（改行は LF でも CRLF でもよい）。エラーに行番号を付ける。</summary>
        private sealed class LineReader
        {
            private readonly string[] _lines;
            private int _index;

            public LineReader(string text)
            {
                _lines = text.Replace("\r\n", "\n").Split('\n');
            }

            public string Next()
            {
                if (_index >= _lines.Length)
                {
                    _index++;
                    throw Error("記録が途中で終わっています");
                }
                return _lines[_index++].TrimEnd();
            }

            /// <summary>「key 値」の行を読み、値を返す（値は空でもよい）。</summary>
            public string Value(string key)
            {
                string line = Next();
                if (line == key)
                {
                    return string.Empty;
                }
                if (!line.StartsWith(key + " ", StringComparison.Ordinal))
                {
                    throw Error($"「{key}」の行がありません");
                }
                return line.Substring(key.Length + 1);
            }

            public FormatException Error(string message)
            {
                return new FormatException($"入力の記録の {_index} 行目: {message}");
            }
        }
    }
}
