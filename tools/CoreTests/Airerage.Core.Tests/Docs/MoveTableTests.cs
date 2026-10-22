using System;
using System.IO;
using NUnit.Framework;

namespace Airerage.Core.Tests.Docs
{
    /// <summary>
    /// 技の性能表（docs/17_move_data.md）が、コードの値と合っているか（ユーザーの要望、2026-10-22: 性能を変えたら資料も変わるように）。
    /// 合っていなければ失敗する。作り直すには `bash tools/update_move_table.sh`（環境変数 AIRERAGE_UPDATE_DOCS=1 で、このテストが書き直す）。
    /// dotnet test だけで動かす（Unity のテストには入れない。tools/CoreTests の Docs）。
    /// </summary>
    public class MoveTableTests
    {
        [Test]
        public void TheDocument_MatchesTheCode()
        {
            string path = Path.Combine(RepositoryRoot(), MoveTable.RelativePath);
            string expected = MoveTable.Render();
            if (Environment.GetEnvironmentVariable("AIRERAGE_UPDATE_DOCS") == "1")
            {
                File.WriteAllText(path, expected);
                TestContext.WriteLine($"{MoveTable.RelativePath} を作り直した");
                return;
            }
            Assert.That(File.Exists(path), Is.True, $"{MoveTable.RelativePath} がない。bash tools/update_move_table.sh で作る");
            string actual = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.That(actual, Is.EqualTo(expected),
                $"{MoveTable.RelativePath} がコードの値と合っていない（技の値を変えたら bash tools/update_move_table.sh で作り直す）");
        }

        // リポジトリのいちばん上（docs と Assets があるフォルダ）。
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "docs")) && Directory.Exists(Path.Combine(directory.FullName, "Assets")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException($"リポジトリのフォルダが見つからない（{TestContext.CurrentContext.TestDirectory} から上へ探した）");
        }
    }
}
