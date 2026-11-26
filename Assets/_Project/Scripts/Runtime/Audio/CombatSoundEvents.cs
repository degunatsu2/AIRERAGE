using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Audio
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
                Compare(_previous[i], current, config.Movement.JumpSpeed, config.Combat, WispGifted(i));
                _previous[i] = current;
            }
        }

        // キャラクター index が、このあいだに恐怖の館の鬼火の輪をくぐってコインをもらったか（鬼火の専用の音は WispRingView が鳴らすので、
        // コインの音は鳴らさない。2026-11-26 の要望）。
        private bool WispGifted(int index)
        {
            IReadOnlyList<StageTrap> traps = _feed.Traps;
            StageState stage = _feed.Stage;
            if (traps == null || stage == null)
            {
                return false;
            }
            int tick = _feed.CurrentTick;
            for (int t = 0; t < traps.Count && t < stage.TrapCount; t++)
            {
                if (traps[t].Kind == StageTrapKind.WispRing && stage.TrapOwnerOf(t) == index && tick - stage.TrapTickOf(t) <= WispGiftWindowTicks)
                {
                    return true;
                }
            }
            return false;
        }

        // 鬼火の輪からもらったとみなすティック数（1 回の通知で何ティックか進むことがあるので、少し余裕を持つ）。
        private const int WispGiftWindowTicks = 6;

        private void Compare(in CharacterState before, in CharacterState now, float jumpSpeed, CombatTuning combat, bool wispGift = false)
        {
            CombatState b = before.Combat;
            CombatState n = now.Combat;
            if (n.Falls > b.Falls)
            {
                _audio.Play(SoundId.Knockout);
                return;
            }
            if (n.HeistPayoutCount > b.HeistPayoutCount)
            {
                // バンクハイストでコインを得た（THIEF の R）。
                _audio.Play(SoundId.Heist);
            }
            else if (now.Coins.Banked > before.Coins.Banked)
            {
                _audio.Play(SoundId.Bank);
            }
            else if (now.Coins.Held > before.Coins.Held && !wispGift)
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
            if (now.Wire.Phase == WirePhase.Retracting && before.Wire.Phase != WirePhase.Retracting
                && now.Wire.LastMissReason == WireMissReason.HitNonAttachable && !now.Wire.Whip)
            {
                // ワイヤーが接続できない物に弾かれた（BLOCK!）。
                _audio.Play(SoundId.Guard, 0.6f, 0.08f);
            }
            if (n.BounceCount > b.BounceCount)
            {
                _audio.Play(SoundId.Bounce, 1f, 0.06f);
            }
            if (n.WarpCount > b.WarpCount)
            {
                // ワープポータルで移った（SPACE TRAVELER）。
                _audio.Play(SoundId.Warp, 0.9f, 0.04f);
            }
            if (n.HazardHitCount > b.HazardHitCount)
            {
                // 溶岩流・つららに当たった（ステージの危険）。
                _audio.Play(SoundId.HeavyHit, 1f, 0.06f);
            }
            if (n.SplashCount > b.SplashCount)
            {
                // 水に飛び込んだ。
                _audio.Play(SoundId.Splash, 0.85f, 0.06f);
            }
            if (n.DrownTicks > 0 && b.DrownTicks == 0)
            {
                // 溺れた。
                _audio.Play(SoundId.Drown, 0.9f, 0.03f);
            }
            bool coil = StageZones.WasCoil(now, _feed.ActiveZones);
            if (n.CaptureCount > b.CaptureCount)
            {
                // 悪魔の手に握られた・コイルガンに収まった（ためる音。2026-11-24）。
                _audio.Play(coil ? SoundId.ChargeUp : SoundId.Grab, 1f, 0.04f);
            }
            if (n.CaptureTicks == 0 && b.CaptureTicks > 0)
            {
                // 時空に吸い込まれて、牢屋に現れた・コイルガンから撃ち出された。
                _audio.Play(StageZones.WasCoil(before, _feed.ActiveZones) ? SoundId.Cannon : SoundId.Vortex, 0.8f, 0.04f);
            }
            if (n.FreezeCount > b.FreezeCount)
            {
                _audio.Play(SoundId.Freeze, 0.9f, 0.04f);
            }
            if (n.StunCount > b.StunCount)
            {
                // 雷で感電した（SOLDERER）。
                _audio.Play(SoundId.Zap, 0.9f, 0.05f);
            }
            if (n.StealCount > b.StealCount)
            {
                // タレントスナッチで技・ギミックを奪った（THIEF の Q）。
                _audio.Play(SoundId.Steal, 0.9f, 0.03f);
            }
            if (n.ShieldBlockCount > b.ShieldBlockCount)
            {
                // ボウルで上からの投射物を防いだ（PASTRY CHEF）。
                _audio.Play(SoundId.ShieldBlock, 0.9f, 0.04f);
            }
            if (n.FatalHitCount > b.FatalHitCount)
            {
                _audio.Play(SoundId.Fatal);
            }
            if (n.JackpotHitCount > b.JackpotHitCount)
            {
                // ルーレットの最大（JACKPOT）を受けた（RISK TAKER）。
                _audio.Play(SoundId.JackpotGet, 0.8f);
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
                switch (now.Action.Attack)
                {
                    case AttackSlot.Super:
                        // バンクハイスト（THIEF の R）は、出したときに鳴らさない（こっそり始める。ユーザーの案）。
                        if (combat.Super.Effect != Airerage.Core.Skills.SkillEffect.BankHeist)
                        {
                            _audio.Play(SoundId.SuperStart);
                        }
                        break;
                    case AttackSlot.Special1:
                    case AttackSlot.Special2:
                        _audio.Play(SoundId.SpecialMove, 0.85f, 0.05f);
                        break;
                    default:
                        // ルーレットの技は、チャージの代わりにルーレットが回る音（ユーザーの要望、2026-10-16）。
                        bool roulette = combat.Get(now.Action.Attack).Roulette;
                        _audio.Play(roulette ? SoundId.RouletteSpin : now.Action.Attack == combat.KoAttack ? SoundId.Charge : SoundId.Swing,
                            0.8f, roulette ? 0f : 0.08f);
                        break;
                }
            }
            if (now.Action.Kind == ActionKind.Guard && before.Action.Kind != ActionKind.Guard)
            {
                _audio.Play(SoundId.GuardUp, 0.8f);
            }

            // コイルガンから撃ち出されたときは、撃ち出す音（Cannon）だけにする。
            bool pad = now.JumpPadCount > before.JumpPadCount && !(before.Combat.CaptureTicks > 0 && StageZones.WasCoil(before, _feed.ActiveZones));
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
            bool wallJump = now.WallJumpCount > before.WallJumpCount;
            if (!pad && (airJump || groundJump || wallJump))
            {
                _audio.Play(SoundId.Jump, wallJump ? 0.75f : 0.6f, 0.05f);
            }

            if (n.WireCancelCooldownTicksLeft > b.WireCancelCooldownTicksLeft)
            {
                _audio.Play(SoundId.WireCancel);
            }
            if (now.Wire.Phase == WirePhase.Firing && before.Wire.Phase != WirePhase.Firing)
            {
                _audio.Play(SoundId.WireFire, 0.55f, 0.05f);
            }
            if (now.Wire.Phase == WirePhase.Attached && before.Wire.Phase != WirePhase.Attached)
            {
                _audio.Play(SoundId.WireAttach, 0.8f);
            }
        }
    }
}
