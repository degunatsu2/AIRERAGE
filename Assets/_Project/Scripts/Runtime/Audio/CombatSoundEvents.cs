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

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [SerializeField] private GameAudio _audio;

        private CharacterState[] _previous = new CharacterState[0];
        private ISimulationFeed _feed;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _audio == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か GameAudio が設定されていません", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_feed != null)
            {
                _feed.Ticked += OnTicked;
            }
        }

        private void OnDisable()
        {
            if (_feed != null)
            {
                _feed.Ticked -= OnTicked;
            }
        }

        private void OnTicked()
        {
            int count = _feed.CharacterCount;
            if (_previous.Length != count)
            {
                // 人数が変わった（オンラインの試合の始まりと終わり）。比べる元だけを取り直す。
                _previous = new CharacterState[count];
                for (int i = 0; i < count; i++)
                {
                    _previous[i] = _feed.GetState(i);
                }
                return;
            }
            for (int i = 0; i < count; i++)
            {
                CharacterState current = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                Compare(_previous[i], current, config.Movement.JumpSpeed, config.Combat.KoAttack);
                _previous[i] = current;
            }
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
            if (now.Coins.Banked > before.Coins.Banked)
            {
                _audio.Play(SoundId.Bank);
            }
            else if (now.Coins.Held > before.Coins.Held)
            {
                _audio.Play(SoundId.CoinPickup, 0.8f, 0.04f);
            }
            if (n.JustGuardCount > b.JustGuardCount)
            {
                _audio.Play(SoundId.Parry);
            }
            if (n.GuardedCount > b.GuardedCount)
            {
                _audio.Play(SoundId.Guard, 1f, 0.04f);
            }
            if (n.BounceCount > b.BounceCount)
            {
                _audio.Play(SoundId.Bounce, 1f, 0.06f);
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

            // 攻撃の始まり: 攻撃でなかったか、別の攻撃だったか、同じ攻撃の初めに戻った（攻撃→攻撃のキャンセル）。
            bool attackStarted = now.Action.Kind == ActionKind.Attack
                && (before.Action.Kind != ActionKind.Attack || now.Action.Attack != before.Action.Attack || now.Action.Ticks < before.Action.Ticks);
            if (attackStarted)
            {
                _audio.Play(now.Action.Attack == koAttack ? SoundId.Charge : SoundId.Swing, 0.8f, 0.08f);
            }
            if (now.Action.Kind == ActionKind.Guard && before.Action.Kind != ActionKind.Guard)
            {
                _audio.Play(SoundId.GuardUp, 0.8f);
            }

            bool pad = now.JumpPadCount > before.JumpPadCount;
            if (pad)
            {
                _audio.Play(SoundId.JumpPad, 0.9f, 0.05f);
            }
            if (now.BoostRingCount > before.BoostRingCount)
            {
                _audio.Play(SoundId.Boost, 0.9f, 0.05f);
            }
            bool airJump = now.AirJumpsUsed > before.AirJumpsUsed;
            bool groundJump = before.Grounded && !now.Grounded && now.Action.IsFree && now.Velocity.Y >= jumpSpeed * 0.8f;
            if (!pad && (airJump || groundJump))
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
