using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// キャラクターの状態を箱で表示する（プロトタイプ用）。直前と現在のティックの状態を補間する。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        public void Render(in CharacterState previous, in CharacterState current, float alpha, Vector3 halfExtents)
        {
            transform.position = Vector3.Lerp(previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
            transform.localScale = halfExtents * 2f;
        }
    }
}
