using Airerage.Core.Wire;
using Airerage.Runtime.DebugTools;
using NUnit.Framework;
using UnityEngine;

namespace Airerage.Runtime.Tests
{
    public sealed class CourseTimerTests
    {
        private static readonly Vector2 Half = new Vector2(0.4f, 0.9f);
        private static readonly Vector2 InStart = new Vector2(0f, 1f);
        private static readonly Vector2 Outside = new Vector2(5f, 1f);
        private static readonly Vector2 InGoal = new Vector2(20f, 1f);

        private static CourseTimer CreateTimer()
        {
            return new CourseTimer(
                Rect.MinMaxRect(-2f, 0f, 2f, 2.5f),
                new[] { new CourseTimer.Goal("Right", Rect.MinMaxRect(19f, 0f, 21f, 3f)) });
        }

        [Test]
        public void StartsWaiting_BecomesReadyInStart_RunsAfterLeaving()
        {
            CourseTimer timer = CreateTimer();
            Assert.That(timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Waiting));

            timer.Step(InStart, Half, WireModelType.Inertial);
            Assert.That(timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Ready));

            timer.Step(Outside, Half, WireModelType.Inertial);
            Assert.That(timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Running));
            Assert.That(timer.RunningTicks, Is.EqualTo(0));
        }

        [Test]
        public void ReachingGoal_RecordsTicksSinceLeavingStart()
        {
            CourseTimer timer = CreateTimer();
            timer.Step(InStart, Half, WireModelType.Inertial);
            timer.Step(Outside, Half, WireModelType.Inertial); // ここから計測（0 ティック）
            for (int i = 0; i < 9; i++)
            {
                timer.Step(Outside, Half, WireModelType.Inertial);
            }
            timer.Step(InGoal, Half, WireModelType.Inertial);  // 10 ティック目にゴール

            Assert.That(timer.LastRecord.HasValue, Is.True);
            Assert.That(timer.LastRecord.Value.Course, Is.EqualTo("Right"));
            Assert.That(timer.LastRecord.Value.Ticks, Is.EqualTo(10));
            Assert.That(timer.LastRecord.Value.WireType, Is.EqualTo(WireModelType.Inertial));
            Assert.That(timer.TryGetBest("Right", WireModelType.Inertial, out int best), Is.True);
            Assert.That(best, Is.EqualTo(10));
            Assert.That(timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Waiting));
        }

        [Test]
        public void TouchingGoalEdge_CountsAsReached()
        {
            CourseTimer timer = CreateTimer();
            timer.Step(InStart, Half, WireModelType.Inertial);
            timer.Step(Outside, Half, WireModelType.Inertial);
            // キャラクターの箱の右端（18.4 + 0.4 = 18.8）はゴール（19〜）に届いていない
            timer.Step(new Vector2(18.4f, 1f), Half, WireModelType.Inertial);
            Assert.That(timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Running));
            // 箱の右端が 19.1 になり、重なった
            timer.Step(new Vector2(18.7f, 1f), Half, WireModelType.Inertial);
            Assert.That(timer.LastRecord.HasValue, Is.True);
        }

        [Test]
        public void Best_KeepsFastest_PerWireType()
        {
            CourseTimer timer = CreateTimer();
            Run(timer, WireModelType.Inertial, 20);
            Run(timer, WireModelType.Inertial, 12);
            Run(timer, WireModelType.Inertial, 30);
            Run(timer, WireModelType.Responsive, 25);

            Assert.That(timer.TryGetBest("Right", WireModelType.Inertial, out int a), Is.True);
            Assert.That(a, Is.EqualTo(12));
            Assert.That(timer.TryGetBest("Right", WireModelType.Responsive, out int b), Is.True);
            Assert.That(b, Is.EqualTo(25));
            Assert.That(timer.LastRecord.Value.Ticks, Is.EqualTo(25));
        }

        [Test]
        public void ReturningToStart_RestartsRun()
        {
            CourseTimer timer = CreateTimer();
            timer.Step(InStart, Half, WireModelType.Inertial);
            timer.Step(Outside, Half, WireModelType.Inertial);
            timer.Step(Outside, Half, WireModelType.Inertial);

            timer.Step(InStart, Half, WireModelType.Inertial);

            Assert.That(timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Ready));
            Assert.That(timer.RunningTicks, Is.EqualTo(0));
        }

        [Test]
        public void ChangingWireTypeDuringRun_AbortsRun()
        {
            CourseTimer timer = CreateTimer();
            timer.Step(InStart, Half, WireModelType.Inertial);
            timer.Step(Outside, Half, WireModelType.Inertial);

            timer.Step(Outside, Half, WireModelType.Responsive);
            timer.Step(InGoal, Half, WireModelType.Responsive);

            Assert.That(timer.LastRecord.HasValue, Is.False);
            Assert.That(timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Waiting));
        }

        [Test]
        public void Abort_StopsRunUntilBackInStart()
        {
            CourseTimer timer = CreateTimer();
            timer.Step(InStart, Half, WireModelType.Inertial);
            timer.Step(Outside, Half, WireModelType.Inertial);

            timer.Abort();
            timer.Step(InGoal, Half, WireModelType.Inertial);

            Assert.That(timer.LastRecord.HasValue, Is.False);
            Assert.That(timer.Phase, Is.EqualTo(CourseTimer.TimerPhase.Waiting));
        }

        /// <summary>スタートから出て、ticks ティック目にゴールに入る。</summary>
        private static void Run(CourseTimer timer, WireModelType type, int ticks)
        {
            timer.Step(InStart, Half, type);
            timer.Step(Outside, Half, type);
            for (int i = 0; i < ticks - 1; i++)
            {
                timer.Step(Outside, Half, type);
            }
            timer.Step(InGoal, Half, type);
        }
    }
}
