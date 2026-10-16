using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// ステージのシーンに置く出現位置（docs/15 T6-2）。番号の小さい順に、1 人目、2 人目…（トレーニングでは 1 人目が自分、残りがダミー）。
    /// 試合の仕組み（実行、表示）は別のシーンにあり、シーンをまたいで参照できないので、実行が読み込んだシーンから探す。
    /// </summary>
    public sealed class SpawnPointAuthoring : MonoBehaviour
    {
        [Tooltip("出現位置の番号（0 が 1 人目）")]
        [SerializeField] private int _index;

        public int Index => _index;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(int index)
        {
            _index = index;
        }

        /// <summary>読み込んでいるシーンの出現位置を、番号の順に集める（なければ空）。</summary>
        public static List<Vector3> Collect()
        {
            var points = new List<SpawnPointAuthoring>(FindObjectsByType<SpawnPointAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            points.Sort((a, b) => a._index != b._index ? a._index.CompareTo(b._index) : string.CompareOrdinal(a.name, b.name));
            var positions = new List<Vector3>(points.Count);
            foreach (SpawnPointAuthoring point in points)
            {
                positions.Add(point.transform.position);
            }
            return positions;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _index == 0 ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.6f, 0.3f);
            Gizmos.DrawWireCube(transform.position, new Vector3(0.8f, 1.8f, 0.8f));
        }
    }
}
