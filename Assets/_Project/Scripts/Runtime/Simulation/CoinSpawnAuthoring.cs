using System;
using System.Collections.Generic;
using Airerage.Core.Economy;
using UnityEngine;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// シーンに置くコインの出現地点（Core の CoinSpawnPoint。docs/12_match_plan.md T4-4）。位置はこの物体の位置（Z = 0 の面）。
    /// 基本は空中に置き、取りにくい場所ほど高額にする（docs/01 3.3）。値はシーンを作るコードで決める【仮】。
    /// </summary>
    public sealed class CoinSpawnAuthoring : MonoBehaviour
    {
        [Tooltip("出すコインの枚数")]
        [SerializeField] private int _value = 1;
        [Tooltip("Regular: ふつうの地点。Jackpot: 残り 1 分に 100 枚のコインが出る候補の地点（候補の中から1つ）")]
        [SerializeField] private CoinSpawnKind _kind = CoinSpawnKind.Regular;

        public int Value => _value;
        public CoinSpawnKind Kind => _kind;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(int value, CoinSpawnKind kind = CoinSpawnKind.Regular)
        {
            _value = value;
            _kind = kind;
        }

        /// <summary>Core の出現地点にする。値が不正なら ArgumentException。</summary>
        public CoinSpawnPoint ToSpawnPoint()
        {
            Vector3 p = transform.position;
            return new CoinSpawnPoint(new System.Numerics.Vector2(p.x, p.y), _value, _kind);
        }

        /// <summary>
        /// シーンの出現地点を集める。不正なものは除き、理由を errors に追加する。並べる順は SimWorld が決める。
        /// </summary>
        public static List<CoinSpawnPoint> Collect(ICollection<string> errors)
        {
            var points = new List<CoinSpawnPoint>();
            foreach (CoinSpawnAuthoring authoring in FindObjectsByType<CoinSpawnAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                try
                {
                    points.Add(authoring.ToSpawnPoint());
                }
                catch (ArgumentException e)
                {
                    errors.Add($"コインの出現地点（{authoring.name}）を除いた: {e.Message}");
                }
            }
            return points;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _kind == CoinSpawnKind.Jackpot ? new Color(1f, 0.3f, 1f) : new Color(1f, 0.85f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, _kind == CoinSpawnKind.Jackpot ? 1f : 0.5f);
        }
    }
}
