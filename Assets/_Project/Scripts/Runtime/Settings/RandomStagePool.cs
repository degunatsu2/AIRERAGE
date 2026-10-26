using System.Collections.Generic;
using Airerage.Runtime.Flow;
using UnityEngine;

namespace Airerage.Runtime.Settings
{
    /// <summary>
    /// ステージ選択の RANDOM で選ばれるステージ（オプション。ユーザーの要望、2026-10-26）。チェックを外したステージは RANDOM で
    /// 選ばれない。外したステージのシーンの名前を PlayerPrefs に保存する（新しく加えたステージは、初めはチェックが付いている）。
    /// ネットワーク対戦では、各自の選べるステージを番号のビット（StageCatalog の順。Mask）でホストへ送り、全員がチェックを付けている
    /// ステージだけから選ぶ（OnlineSession.CommonStagePool）。
    /// </summary>
    public static class RandomStagePool
    {
        public const string PreferenceKey = "Options.RandomStageExcluded";

        /// <summary>すべてのステージを選べる（番号のビットがすべて立っている）。</summary>
        public const ulong All = ulong.MaxValue;

        private const char Separator = '\n';

        /// <summary>stage が RANDOM で選ばれるか。</summary>
        public static bool IsIncluded(StageDefinition stage)
        {
            return stage != null && !Excluded().Contains(stage.SceneName);
        }

        /// <summary>stage を RANDOM で選ばれるようにするか、外す（すぐに保存する）。</summary>
        public static void SetIncluded(StageDefinition stage, bool included)
        {
            if (stage == null)
            {
                return;
            }
            HashSet<string> excluded = Excluded();
            bool changed = included ? excluded.Remove(stage.SceneName) : excluded.Add(stage.SceneName);
            if (!changed)
            {
                return;
            }
            var names = new List<string>(excluded);
            names.Sort(System.StringComparer.Ordinal);
            PlayerPrefs.SetString(PreferenceKey, string.Join(Separator.ToString(), names));
            PlayerPrefs.Save();
        }

        /// <summary>stages の番号のうち、RANDOM で選ばれるもののビット（64 番より後ろのステージはいつも選ばれる）。</summary>
        public static ulong Mask(IReadOnlyList<StageDefinition> stages)
        {
            ulong mask = All;
            if (stages == null)
            {
                return mask;
            }
            HashSet<string> excluded = Excluded();
            for (int i = 0; i < stages.Count && i < 64; i++)
            {
                if (stages[i] != null && excluded.Contains(stages[i].SceneName))
                {
                    mask &= ~(1UL << i);
                }
            }
            return mask;
        }

        /// <summary>
        /// count 個のステージのうち、mask で選べる番号（1 つもなければ、すべての番号。全員の共通のステージがないときなど）。
        /// </summary>
        public static List<int> Indices(int count, ulong mask)
        {
            var indices = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (i >= 64 || (mask & (1UL << i)) != 0)
                {
                    indices.Add(i);
                }
            }
            if (indices.Count == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    indices.Add(i);
                }
            }
            return indices;
        }

        /// <summary>保存した設定を消す（すべてのステージを選べるように戻す。テストでも使う）。</summary>
        public static void DeleteSaved()
        {
            PlayerPrefs.DeleteKey(PreferenceKey);
            PlayerPrefs.Save();
        }

        private static HashSet<string> Excluded()
        {
            var excluded = new HashSet<string>();
            foreach (string name in PlayerPrefs.GetString(PreferenceKey, string.Empty).Split(Separator))
            {
                if (name.Length > 0)
                {
                    excluded.Add(name);
                }
            }
            return excluded;
        }
    }
}
