using System;
using System.Collections.Generic;
using Degunatsu.Core.Input;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Replay
{
    /// <summary>
    /// 直近の入力を記録し続ける（docs/06_prototype_plan.md T1-9b）。容量を超えたら古い入力から捨てる。
    /// 入力ごとに、その入力を処理する前の状態も持つので、いつ記録を取り出しても開始時の状態が分かる。
    /// 位置のリセットやタイプの切り替えのように、入力以外で状態が変わったときは Restart で記録をやり直す。
    /// </summary>
    public sealed class InputRecorder
    {
        private readonly CharacterState[] _before;
        private readonly PlayerInput[] _inputs;
        private int _oldest;
        private CharacterState _current;
        private bool _started;

        public InputRecorder(int capacityTicks)
        {
            if (capacityTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityTicks), capacityTicks, "容量は1ティック以上にしてください");
            }
            _before = new CharacterState[capacityTicks];
            _inputs = new PlayerInput[capacityTicks];
        }

        public int Capacity => _inputs.Length;

        /// <summary>記録しているティック数。</summary>
        public int Count { get; private set; }

        /// <summary>記録しているワイヤーのタイプ。</summary>
        public WireModelType WireModelType { get; private set; }

        /// <summary>最後に加えた入力を処理した後の状態（まだなければ Restart で指定した状態）。</summary>
        public CharacterState CurrentState => _current;

        /// <summary>記録を捨てて、state から記録し直す。</summary>
        public void Restart(in CharacterState state, WireModelType wireModelType)
        {
            _oldest = 0;
            Count = 0;
            _current = state;
            WireModelType = wireModelType;
            _started = true;
        }

        /// <summary>1ティック分の入力と、その入力を処理した後の状態を加える。</summary>
        public void Add(PlayerInput input, in CharacterState after)
        {
            if (!_started)
            {
                throw new InvalidOperationException("先に Restart で開始時の状態を指定してください");
            }
            int index;
            if (Count < Capacity)
            {
                index = (_oldest + Count) % Capacity;
                Count++;
            }
            else
            {
                // いっぱいなら、いちばん古い入力を上書きする。
                index = _oldest;
                _oldest = (_oldest + 1) % Capacity;
            }
            _before[index] = _current;
            _inputs[index] = input;
            _current = after;
        }

        /// <summary>記録している入力を取り出す。1ティックも記録していなければ InvalidOperationException。</summary>
        public InputRecording Build(string configText)
        {
            if (Count == 0)
            {
                throw new InvalidOperationException("記録している入力がありません");
            }
            var inputs = new List<PlayerInput>(Count);
            for (int i = 0; i < Count; i++)
            {
                inputs.Add(_inputs[(_oldest + i) % Capacity]);
            }
            return new InputRecording(_before[_oldest], WireModelType, inputs, StateCodec.Checksum(_current), configText);
        }
    }
}
