using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Degunatsu.Core.Input;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Tests.Support;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Rollback
{
    /// <summary>
    /// 入力遅延と予測の上限を決めるための評価（docs/10_online_plan.md T2-9）。人の入力（ユーザーが Windows で記録した 3600 ティック）で
    /// 4人の疑似ネットワークを動かし、通信の条件と設定ごとに、巻き戻し・ワープ（巻き戻しで相手の位置が飛ぶ）・止まり（自分が進めない）を測って表にする。
    /// 時間がかかるので明示的に選んだときだけ実行する:
    ///   dotnet test tools/CoreTests/Degunatsu.Core.Tests --filter "FullyQualifiedName~RollbackEvaluationTests" --logger "console;verbosity=detailed"
    /// </summary>
    [Explicit("評価用。時間がかかるので、名前で選んだときだけ実行する")]
    public class RollbackEvaluationTests
    {
        private const string RecordingPath = "InputRecordings/archive/windows_user_20261013_typeA.txt";
        private const int Ticks = 3000;
        private const int Players = 4;
        private const int Seed = 11;

        // 往復の遅延（ms）と、片道の遅延（フレーム）・揺らぎ（フレーム）。片道の平均 = 遅延 + 揺らぎ / 2。
        private static readonly (int Rtt, int Latency, int Jitter)[] Conditions =
        {
            (0, 0, 0), (50, 1, 1), (100, 2, 2), (150, 3, 3), (200, 5, 2),
        };

        private static readonly double[] Losses = { 0.0, 0.02, 0.05 };

        [Test]
        public void InputDelaySweep()
        {
            PlayerInput[][] scripts = HumanScripts();
            var table = new StringBuilder();
            table.AppendLine("| 往復 | 欠落 | 入力遅延 | 巻き戻し/分 | 平均の長さ | 最長 | ワープ/分 | 最大のワープ | 止まり/分 | 最長の止まり |");
            table.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var condition in Conditions)
            {
                foreach (double loss in Losses)
                {
                    for (int delay = 0; delay <= 4; delay++)
                    {
                        table.AppendLine(Row(scripts, condition, loss, delay, 8, includePrediction: false));
                    }
                }
            }
            TestContext.Out.WriteLine(table.ToString());
        }

        [Test]
        public void PredictionLimitSweep()
        {
            PlayerInput[][] scripts = HumanScripts();
            var table = new StringBuilder();
            table.AppendLine("| 往復 | 欠落 | 入力遅延 | 予測の上限 | 巻き戻し/分 | 平均の長さ | 最長 | ワープ/分 | 最大のワープ | 止まり/分 | 最長の止まり |");
            table.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var condition in new[] { Conditions[3], Conditions[4] })
            {
                foreach (double loss in new[] { 0.0, 0.05 })
                {
                    foreach (int maxPrediction in new[] { 4, 6, 8, 10 })
                    {
                        table.AppendLine(Row(scripts, condition, loss, 2, maxPrediction, includePrediction: true));
                    }
                }
            }
            TestContext.Out.WriteLine(table.ToString());
        }

        [Test]
        public void SimulationCost()
        {
            // 巻き戻しの計算量の目安: 4人のワールドを1ティック進める時間（この PC の .NET。Unity の Mono ではもっと遅い）。
            PlayerInput[][] scripts = HumanScripts();
            var world = new PseudoNetwork(scripts, new RollbackSettings(), 0, 0, 0.0, Seed).Worlds[0];
            var inputs = new PlayerInput[Players];
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int t = 0; t < Ticks; t++)
            {
                for (int p = 0; p < Players; p++)
                {
                    inputs[p] = scripts[p][t];
                }
                world.Step(inputs);
            }
            watch.Stop();
            double microseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / Ticks;
            TestContext.Out.WriteLine($"4人のワールドの1ティック: {microseconds:0.0} µs（{Ticks} ティックの平均）。8ティックの巻き戻し: {microseconds * 8 / 1000.0:0.000} ms");
        }

        private static string Row((int Rtt, int Latency, int Jitter) condition, double loss)
        {
            return $"| {condition.Rtt}ms | {loss * 100:0}% ";
        }

        private static string Row(PlayerInput[][] scripts, (int Rtt, int Latency, int Jitter) condition, double loss,
            int delay, int maxPrediction, bool includePrediction)
        {
            var settings = new RollbackSettings { InputDelayTicks = delay, MaxPredictionTicks = maxPrediction };
            var network = new PseudoNetwork(scripts, settings, condition.Latency, condition.Jitter, loss, Seed);
            network.RunUntil(Ticks, maxFrames: Ticks * 4);

            // 前提: どの条件でも全員が本当の入力どおりの状態になる。
            ulong expected = network.Reference(Ticks).Checksum();
            foreach (var world in network.Worlds)
            {
                Assert.That(world.Checksum(), Is.EqualTo(expected), $"往復 {condition.Rtt}ms、欠落 {loss}、入力遅延 {delay}");
            }

            double minutes = network.Frame / 60.0 / 60.0;
            int rollbacks = 0;
            int resimulated = 0;
            int longest = 0;
            foreach (RollbackSession session in network.Sessions)
            {
                rollbacks += session.RollbackCount;
                resimulated += session.ResimulatedTicks;
                longest = Math.Max(longest, session.LongestRollback);
            }
            var row = new StringBuilder(Row(condition, loss));
            row.Append($"| {delay}F ");
            if (includePrediction)
            {
                row.Append($"| {maxPrediction}F ");
            }
            row.Append(string.Format(CultureInfo.InvariantCulture,
                "| {0:0} | {1:0.0}F | {2}F | {3:0} | {4:0.00}m | {5:0} | {6}F |",
                rollbacks / (double)Players / minutes,
                rollbacks == 0 ? 0.0 : resimulated / (double)rollbacks,
                longest,
                network.VisibleCorrectionCount / (double)Players / minutes,
                network.LargestCorrection,
                (network.StalledFrames + network.TimeSyncWaitFrames) / (double)Players / minutes,
                network.LongestFreeze));
            return row.ToString();
        }

        // 人の入力: 1つの記録を、プレイヤーごとにずらした位置から使う（同じ操作が同時に起きないように）。
        private static PlayerInput[][] HumanScripts()
        {
            List<PlayerInput> recorded = ReadRecordedInputs(Path.Combine(RepositoryRoot(), RecordingPath));
            var scripts = new PlayerInput[Players][];
            int length = Ticks * 2;
            for (int p = 0; p < Players; p++)
            {
                scripts[p] = new PlayerInput[length];
                int offset = p * recorded.Count / Players;
                for (int t = 0; t < length; t++)
                {
                    scripts[p][t] = recorded[(offset + t) % recorded.Count];
                }
            }
            return scripts;
        }

        // 記録の入力の部分だけを読む（この記録は古い状態の形式なので InputRecording.Parse では読めない）。
        private static List<PlayerInput> ReadRecordedInputs(string path)
        {
            var inputs = new List<PlayerInput>();
            bool inInputs = false;
            foreach (string line in File.ReadAllLines(path))
            {
                if (!inInputs)
                {
                    inInputs = line == "inputs";
                    continue;
                }
                if (line == "end")
                {
                    break;
                }
                string[] parts = line.Split(' ');
                var input = new PlayerInput(
                    sbyte.Parse(parts[0], CultureInfo.InvariantCulture), sbyte.Parse(parts[1], CultureInfo.InvariantCulture),
                    (InputButtons)ushort.Parse(parts[4], CultureInfo.InvariantCulture),
                    sbyte.Parse(parts[2], CultureInfo.InvariantCulture), sbyte.Parse(parts[3], CultureInfo.InvariantCulture));
                int run = int.Parse(parts[5], CultureInfo.InvariantCulture);
                for (int i = 0; i < run; i++)
                {
                    inputs.Add(input);
                }
            }
            Assert.That(inputs.Count, Is.GreaterThan(Ticks), $"記録の入力が短すぎる: {path}");
            return inputs;
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "InputRecordings")))
            {
                directory = directory.Parent;
            }
            if (directory == null)
            {
                Assert.Fail($"InputRecordings が見つからない（{TestContext.CurrentContext.TestDirectory} から上へ探した）");
            }
            return directory.FullName;
        }
    }
}
