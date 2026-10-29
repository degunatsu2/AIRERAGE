using System.IO;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.DebugTools
{
    /// <summary>
    /// 画面の右下に、動いている版（Git のブランチとコミット）を小さく出す。手もとのプロジェクトが最新になっているかを、報告の
    /// コミットと見比べて確かめるため（2026-10-16。変更が反映されていなかったときの確認用）。
    /// エディタで動かしたときだけ、プロジェクトの .git から読む（配布したビルドには .git がないので出さない）。
    /// </summary>
    public sealed class VersionLabel : MonoBehaviour
    {
        private GUIStyle _style;

        /// <summary>出している文字（「branch @ 1234567」。読めなければ null）。</summary>
        public string Text { get; private set; }

        private void Awake()
        {
            Text = Application.isEditor ? Describe(Path.GetDirectoryName(Application.dataPath)) : null;
        }

        /// <summary>プロジェクトのフォルダ root の Git の、今のブランチと短いコミット（「branch @ 1234567」）。読めなければ null。</summary>
        public static string Describe(string root)
        {
            string git = Path.Combine(root, ".git");
            if (File.Exists(git))
            {
                // ワークツリー: 「gitdir: パス」の 1 行。
                string pointer = File.ReadAllText(git).Trim();
                if (!pointer.StartsWith("gitdir:"))
                {
                    return null;
                }
                git = Path.GetFullPath(Path.Combine(root, pointer.Substring("gitdir:".Length).Trim()));
            }
            string headPath = Path.Combine(git, "HEAD");
            if (!File.Exists(headPath))
            {
                return null;
            }
            string head = File.ReadAllText(headPath).Trim();
            if (!head.StartsWith("ref:"))
            {
                return Short(head);
            }
            string reference = head.Substring("ref:".Length).Trim();
            string branch = reference.StartsWith("refs/heads/") ? reference.Substring("refs/heads/".Length) : reference;
            string commit = ReadReference(git, reference);
            return commit != null ? $"{branch} @ {Short(commit)}" : branch;
        }

        // 参照のコミット（refs/heads/... のファイルか、まとめた packed-refs の行）。
        private static string ReadReference(string git, string reference)
        {
            string loose = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(loose))
            {
                return File.ReadAllText(loose).Trim();
            }
            string packed = Path.Combine(git, "packed-refs");
            if (!File.Exists(packed))
            {
                return null;
            }
            foreach (string line in File.ReadAllLines(packed))
            {
                if (line.EndsWith(" " + reference))
                {
                    return line.Substring(0, line.IndexOf(' '));
                }
            }
            return null;
        }

        private static string Short(string commit)
        {
            return commit.Length > 7 ? commit.Substring(0, 7) : commit;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(Text))
            {
                return;
            }
            _style = _style ?? GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.LowerRight, wordWrap = false });
            _style.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(Screen.width - 410f, Screen.height - 20f, 400f, 18f), Text, _style);
        }
    }
}
