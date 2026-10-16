using System.IO;
using Airerage.Runtime.DebugTools;
using NUnit.Framework;

namespace Airerage.Runtime.Tests
{
    /// <summary>版の表示（VersionLabel）: Git のブランチとコミットを、ばらの参照・まとめた参照・ブランチのない状態から読む。</summary>
    public sealed class VersionLabelTests
    {
        private const string Commit = "43a141bcfd86f7e9baa153df11ae81b78c46334b";

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "VersionLabelTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, ".git", "refs", "heads", "claude"));
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        [Test]
        public void ReadsTheBranchAndTheShortCommit_FromALooseReference()
        {
            File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "ref: refs/heads/claude/work\n");
            File.WriteAllText(Path.Combine(_root, ".git", "refs", "heads", "claude", "work"), Commit + "\n");
            Assert.That(VersionLabel.Describe(_root), Is.EqualTo("claude/work @ 43a141b"));
        }

        [Test]
        public void ReadsThePackedReference_WhenThereIsNoLooseOne()
        {
            File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "ref: refs/heads/claude/work\n");
            File.WriteAllText(Path.Combine(_root, ".git", "packed-refs"),
                "# pack-refs with: peeled fully-peeled sorted\n" + Commit + " refs/heads/claude/work\n");
            Assert.That(VersionLabel.Describe(_root), Is.EqualTo("claude/work @ 43a141b"));
        }

        [Test]
        public void ReadsADetachedHead_AndNothingWithoutGit()
        {
            File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), Commit + "\n");
            Assert.That(VersionLabel.Describe(_root), Is.EqualTo("43a141b"));
            Assert.That(VersionLabel.Describe(Path.Combine(_root, "missing")), Is.Null);
        }
    }
}
