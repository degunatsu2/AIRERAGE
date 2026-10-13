using Degunatsu.Core.Combat;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Audio
{
    /// <summary>
    /// シミュレーションの状態の変化から効果音を鳴らす（仮の音。ユーザーの要望、2026-10-13）。SimulationRunner の
    /// 1ティックごとに、操作するキャラクターとダミーの状態を前のティックと比べる。状態を読むだけで、書き換えない。
    /// 数（ガードした数、撃破された数など）は増えたときだけ鳴らす（位置のリセットで減ったときは鳴らさない）。
    /// オンライン（巻き戻しがある）では、まだ使わない（T3-8 で考える）。
    /// </summary>
    public sealed class CombatSoundEvents : MonoBehaviour
    {
        // これより速く吹き飛んだ命中は、強い命中の音にする（m/s）。
        private const float HeavyKnockbackSpeed = 20f;

        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private GameAudio _audio;

        private CharacterState[] _previous = new CharacterState[0];

        private void Awake()
        {
            if (_runner == null || _audio == null)
            {
                Debug.LogError($"{name}: SimulationRunner か GameAudio が設定されていません", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.Ticked += OnTicked;
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.Ticked -= OnTicked;
            }
        }

        private void OnTicked()
        {
            int count = 1 + _runner.DummyCount;
            if (_previous.Length != count)
            {
                _previous = new CharacterState[count];
                for (int i = 0; i < count; i++)
                {
                    _previous[i] = StateOf(i);
                }
                return;
            }
            float jumpSpeed = _runner.PlayerConfig.Movement.JumpSpeed;
            AttackSlot koAttack = _runner.PlayerConfig.Combat.KoAttack;
            for (int i = 0; i < count; i++)
            {
                CharacterState current = StateOf(i);
                Compare(_previous[i], current, jumpSpeed, koAttack);
                _previous[i] = current;
            }
        }

        private CharacterState StateOf(int index)
        {
            return index == 0 ? _runner.PlayerState : _runner.GetDummyState(index - 1);
        }

        private void Compare(in CharacterState before, in CharacterState now, float jumpSpeed, AttackSlot koAttack)
        {
            CombatState b = before.Combat;
            CombatState n = now.Combat;
            if (n.Falls > b.Falls)
            {
                _audio.Play(SoundId.Knockout);
                return;
            }
            if (n.JustGuardCount > b.JustGuardCount)
            {
                _audio.Play(SoundId.Parry);
            }
            if (n.GuardedCount > b.GuardedCount)
            {
                _audio.Play(SoundId.Guard, 1f, 0.04f);
            }
            if (n.FatalHitCount > b.FatalHitCount)
            {
                _audio.Play(SoundId.Fatal);
            }
            else if (now.Action.Kind == ActionKind.Hitstun && n.LastDamagedTick != b.LastDamagedTick && n.GuardedCount == b.GuardedCount)
            {
                bool heavy = now.Velocity.Length() >= HeavyKnockbackSpeed;
                _audio.Play(heavy ? SoundId.HeavyHit : SoundId.Hit, 1f, 0.06f);
            }

            bool attackStarted = now.Action.Kind == ActionKind.Attack && now.Action.Ticks == 0
                && !(before.Action.Kind == ActionKind.Attack && before.Action.Ticks == 0);
            if (attackStarted)
            {
                _audio.Play(now.Action.Attack == koAttack ? SoundId.Charge : SoundId.Swing, 0.8f, 0.08f);
            }
            if (now.Action.Kind == ActionKind.Guard && before.Action.Kind != ActionKind.Guard)
            {
                _audio.Play(SoundId.GuardUp, 0.8f);
            }

            bool airJump = now.AirJumpsUsed > before.AirJumpsUsed;
            bool groundJump = before.Grounded && !now.Grounded && now.Action.IsFree && now.Velocity.Y >= jumpSpeed * 0.8f;
            if (airJump || groundJump)
            {
                _audio.Play(SoundId.Jump, 0.6f, 0.05f);
            }

            if (n.WireCancelCooldownTicksLeft > b.WireCancelCooldownTicksLeft)
            {
                _audio.Play(SoundId.WireCancel);
            }
            if (now.Wire.Phase == WirePhase.Firing && before.Wire.Phase != WirePhase.Firing)
            {
                _audio.Play(SoundId.WireFire, 0.7f, 0.05f);
            }
            if (now.Wire.Phase == WirePhase.Attached && before.Wire.Phase != WirePhase.Attached)
            {
                _audio.Play(SoundId.WireAttach, 0.8f);
            }
        }
    }
}
