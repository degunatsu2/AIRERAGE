using Degunatsu.Core.Combat;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// キャラクターの状態を箱で表示する（プロトタイプ用）。直前と現在のティックの状態を補間する。
    /// 攻撃中は、攻撃の段階（発生・持続・硬直）で箱の色を変える（命中の判定と見た目ができるまでの仮の表示。docs/11_combat_plan.md T3-1）。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Color _startupColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color _activeColor = new Color(0.95f, 0.15f, 0.15f);
        [SerializeField] private Color _recoveryColor = new Color(0.45f, 0.5f, 0.8f);

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;

        /// <summary>今表示している攻撃の段階（攻撃中でなければ null）。</summary>
        public AttackPhase? ShownPhase { get; private set; }

        public void Render(in CharacterState previous, in CharacterState current, float alpha, Vector3 halfExtents)
        {
            transform.position = Vector3.Lerp(previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
            transform.localScale = halfExtents * 2f;
        }

        /// <summary>アクション状態（攻撃の段階）を色で示す。</summary>
        public void RenderAction(in CharacterState current, CombatTuning combat)
        {
            AttackPhase? phase = ActionController.CurrentPhase(current, combat);
            if (phase == ShownPhase)
            {
                return;
            }
            ShownPhase = phase;
            if (_renderer == null && !TryGetComponent(out _renderer))
            {
                return;
            }
            if (phase == null)
            {
                _renderer.SetPropertyBlock(null);
                return;
            }
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }
            _block.SetColor(BaseColorId, phase == AttackPhase.Startup ? _startupColor : phase == AttackPhase.Active ? _activeColor : _recoveryColor);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
