using System;
using System.Collections.Generic;
using Degunatsu.Core.Wire;
using UnityEngine;

namespace Degunatsu.Runtime.DebugTools
{
    /// <summary>
    /// タイム計測コースの計測（docs/06_prototype_plan.md T1-8）。ティックごとにキャラクターの位置を受け取る。
    /// スタートの範囲を出たティックから、どれかのゴールの範囲に入ったティックまでのティック数を記録する。
    /// ベストタイムはコースとワイヤーのタイプごとに持つ。計測中にタイプを変えたら、その走りは無効にする。
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

            public Record(string course, WireModelType wireType, int ticks)
            {
                Course = course;
                WireType = wireType;
                Ticks = ticks;
            }
        }

        private readonly Rect _start;
        private readonly List<Goal> _goals;
        private readonly Dictionary<(string, WireModelType), int> _best = new Dictionary<(string, WireModelType), int>();
        private WireModelType _runWireType;

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
            return _best.TryGetValue((course, wireType), out ticks);
        }

        /// <summary>1ティック分進める。center と halfExtents はキャラクターの箱（XY 平面）。</summary>
        public void Step(Vector2 center, Vector2 halfExtents, WireModelType wireType)
        {
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
                    if (wireType != _runWireType)
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
            var record = new Record(course, _runWireType, RunningTicks);
            LastRecord = record;
            var key = (course, _runWireType);
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
