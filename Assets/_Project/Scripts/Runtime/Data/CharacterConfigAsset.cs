using Degunatsu.Core.Common;
using Degunatsu.Core.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Data
{
    /// <summary>
    /// キャラクターの調整値（移動とワイヤー）を編集するアセット（docs/03_architecture.md 4.4）。
    /// 値の本体は Core の CharacterConfig で、仮値はその初期値にある。
    /// シミュレーションはこの CharacterConfig を参照で持つので、プレイ中にインスペクターで変えた値は次のティックから反映される。
    /// </summary>
    [CreateAssetMenu(menuName = "Degunatsu/Character Config", fileName = "CharacterConfig")]
    public sealed class CharacterConfigAsset : ScriptableObject
    {
        [SerializeField] private CharacterConfig _config = new CharacterConfig();

        public CharacterConfig Config => _config;

        private void OnValidate()
        {
            // 編集中の不正な値は、アセット名と項目名を出して知らせる（シミュレーションへの追加時には例外で止まる）。
            try
            {
                _config.Validate();
            }
            catch (InvalidTuningException e)
            {
                Debug.LogError($"{name}: {e.Message}", this);
            }
        }
    }
}
