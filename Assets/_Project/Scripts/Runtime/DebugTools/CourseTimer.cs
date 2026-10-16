using System;
using System.Collections.Generic;
using Airerage.Core.Wire;
using UnityEngine;

namespace Airerage.Runtime.DebugTools
{
    /// <summary>
    /// タイム計測コースの計測（docs/06_prototype_plan.md T1-8）。ティックごとにキャラクターの位置を受け取る。
    /// スタートの範囲を出たティックから、どれかのゴールの範囲に入ったティックまでのティック数を記録する。
    /// ベストタイムはコースとワイヤーのタイプとキャラクターごとに持つ（キャラクターは docs/13 T5-7 の比較のため）。計測中にタイプか
    /// キャラクターを変えたら、その走りは無効にする。
    /// </summary>
    public sealed class CourseTimer
    {
        public enum TimerPhase
        {
            /// <summary>スタートの範囲に戻るのを待っている。</summary>
            Waiting,

            /// <summary>スタートの範囲にいる。出ると計測を始める。</summary>
            Ready,

            /// <summary>計測中。</summary>
            Running,
        }

        public readonly struct Goal
        {
            public readonly string Course;
            public readonly Rect Area;

            public Goal(string course, Rect area)
            {
                Course = course;
                Area = area;
            }
        }

        public readonly struct Record
        {
            public readonly string Course;
            public readonly WireModelType WireType;
            public readonly int Ticks;

            /// <summary>走ったキャラクターの名前（分けなければ空）。</summary>
            public readonly string Character;

            public Record(string course, WireModelType wireType, int ticks, string character = "")
            {
                Course = course;
                WireType = wireType;
                Ticks = ticks;
                Character = character ?? string.Empty;
            }
        }

        private readonly Rect _start;
        private readonly List<Goal> _goals;
        private readonly Dictionary<(string, WireModelType, string), int> _best = new Dictionary<(string, WireModelType, string), int>();
        private WireModelType _runWireType;
        private string _runCharacter = string.Empty;

        public CourseTimer(Rect start, IEnumerable<Goal> goals)
        {
            if (goals == null)
            {
                throw new ArgumentNullException(nameof(goals));
            }
            _start = start;
            _goals = new List<Goal>(goals);
        }

        public IReadOnlyList<Goal> Goals => _goals;
        public TimerPhase Phase { get; private set; }

        /// <summary>計測中の経過ティック数。</summary>
        public int RunningTicks { get; private set; }

        /// <summary>最後に記録した走り。まだなければ null。</summary>
        public Record? LastRecord { get; private set; }

        public bool TryGetBest(string course, WireModelType wireType, out int ticks)
        {
            return TryGetBest(course, wireType, string.Empty, out ticks);
        }

        /// <summary>キャラクター character のベストタイム。</summary>
        public bool TryGetBest(string course, WireModelType wireType, string character, out int ticks)
        {
            return _best.TryGetValue((course, wireType, character ?? string.Empty), out ticks);
        }

        /// <summary>1ティック分進める。center と halfExtents はキャラクターの箱（XY 平面）。character はキャラクターの名前（分けなければ空）。</summary>
        public void Step(Vector2 center, Vector2 halfExtents, WireModelType wireType, string character = "")
        {
            character = character ?? string.Empty;
            bool inStart = Overlaps(_start, center, halfExtents);
            switch (Phase)
            {
                case TimerPhase.Waiting:
                    if (inStart)
                    {
                        Phase = TimerPhase.Ready;
                        RunningTicks = 0;
                    }
                    break;

                case TimerPhase.Ready:
                    if (!inStart)
                    {
                        Phase = TimerPhase.Running;
                        RunningTicks = 0;
                        _runWireType = wireType;
                        _runCharacter = character;
                    }
                    break;

                case TimerPhase.Running:
                    if (inStart)
                    {
                        // スタートに戻ったら、やり直し。
                        Phase = TimerPhase.Ready;
                        RunningTicks = 0;
                        break;
                    }
                    if (wireType != _runWireType || character != _runCharacter)
                    {
                        Abort();
                        break;
                    }
                    RunningTicks++;
                    foreach (Goal goal in _goals)
                    {
                        if (Overlaps(goal.Area, center, halfExtents))
                        {
                            Finish(goal.Course);
                            break;
                        }
                    }
                    break;
            }
        }

        /// <summary>計測をやめる（位置のリセットなど）。次はスタートの範囲に入ってから計測する。</summary>
        public void Abort()
        {
            Phase = TimerPhase.Waiting;
            RunningTicks = 0;
        }

        private void Finish(string course)
        {
            var record = new Record(course, _runWireType, RunningTicks, _runCharacter);
            LastRecord = record;
            var key = (course, _runWireType, _runCharacter);
            if (!_best.TryGetValue(key, out int best) || record.Ticks < best)
            {
                _best[key] = record.Ticks;
            }
            Phase = TimerPhase.Waiting;
        }

        private static bool Overlaps(Rect area, Vector2 center, Vector2 halfExtents)
        {
            return center.x + halfExtents.x > area.xMin && center.x - halfExtents.x < area.xMax
                && center.y + halfExtents.y > area.yMin && center.y - halfExtents.y < area.yMax;
        }
    }
}
