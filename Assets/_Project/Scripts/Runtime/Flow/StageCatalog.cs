using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Flow
{
    /// <summary>選べるステージの一覧（docs/15 T6-2。ステージ選択に並ぶ順）。最初のステージはメニューの背景にも使う。</summary>
    [CreateAssetMenu(menuName = "Airerage/Stage Catalog", fileName = "StageCatalog")]
    public sealed class StageCatalog : ScriptableObject
    {
        [SerializeField] private StageDefinition[] _stages = new StageDefinition[0];

        public int Count => _stages.Length;

        public IReadOnlyList<StageDefinition> Stages => _stages;

        /// <summary>番号 index のステージ（範囲の外なら最初のステージ。一覧が空なら null）。</summary>
        public StageDefinition Get(int index)
        {
            if (_stages.Length == 0)
            {
                return null;
            }
            return _stages[index >= 0 && index < _stages.Length ? index : 0];
        }

        /// <summary>ステージの番号（一覧になければ -1。ネットワーク対戦では番号で送る）。</summary>
        public int IndexOf(StageDefinition stage)
        {
            return System.Array.IndexOf(_stages, stage);
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageDefinition[] stages)
        {
            _stages = stages ?? new StageDefinition[0];
        }
    }
}
