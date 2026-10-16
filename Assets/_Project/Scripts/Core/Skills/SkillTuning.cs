using System;
using Airerage.Core.Combat;
using Airerage.Core.Common;

namespace Airerage.Core.Skills
{
    /// <summary>
    /// 技の効果の種類（docs/13 T5-6、docs/14）。攻撃のデータ（AttackData.Effect）が、出してから EffectTick のティックに起こす。
    /// 効果ごとの値は、キャラクターの SkillTuning にある（キャラクターごとに 1 つずつ）。
    /// </summary>
    public enum SkillEffect : byte
    {
        None,

        /// <summary>ミラーボールを置く（RISK TAKER の Q）。</summary>
        MirrorBall,

        /// <summary>セーフティボックスを置く（RISK TAKER の E。長押しした長さだけ手持ちのコインを預ける）。</summary>
        Safe,

        /// <summary>コインの効率を上げる（RISK TAKER の R。フィーバータイム）。</summary>
        CoinBoost,

        /// <summary>投射物を撃つ（RISK TAKER のコイントス）。</summary>
        Projectile,

        /// <summary>
        /// 行動の速さを上げる（STORM RUNNER の Q。ウインドクローク）。効果中にもう一度出すと、強化を解いて突風を撃つ。
        /// クールタイムは効果が終わってから数える。
        /// </summary>
        SpeedBoost,

        /// <summary>照準の向きへ吹く風の帯を置く（STORM RUNNER の E。スカイロケット）。</summary>
        WindZone,

        /// <summary>速さとワイヤーを強くする（STORM RUNNER の R。キャッスルインジエアー）。</summary>
        SkyBoost,
    }

    /// <summary>
    /// 走り続ける地上技（AttackData.Run。STORM RUNNER。docs/14 1.2、C-10、C-11。値は【仮】）。向いている方向へ走り、だんだん加速する。
    /// 足場が途切れたら・壁にぶつかったら浅い角度で跳ぶ（壁なら反対側へ）。着地したらそのまま走る。逆向きに入力すると、反転の後に
    /// 速さを初めに戻して逆向きに走る。体の判定のダメージは速さに比例する（初速で判定のダメージ。速さ × 0.4）。
    /// </summary>
    [Serializable]
    public sealed class RunTuning
    {
        /// <summary>初速（m/s）。</summary>
        public float InitialSpeed = 10f;

        /// <summary>加速（m/s²。地上を走っている間だけ）。</summary>
        public float Acceleration = 6f;

        /// <summary>最大の速さ（m/s）。</summary>
        public float MaxSpeed = 24f;

        /// <summary>足場が途切れた・壁にぶつかったときに跳ぶ角度（度。水平から上へ）。</summary>
        public float JumpAngleDeg = 20f;

        /// <summary>逆向きに入力したときの反転の長さ（ティック。この間に止まる）。</summary>
        public int TurnTicks = 8;

        /// <summary>同じ相手にもう一度当たれるまで（ティック。C-11: 0.5 秒）。</summary>
        public int RehitTicks = 30;

        /// <summary>速さ speed のときの、威力の上乗せ（%。初速で 0、速さに比例してダメージが増える）。</summary>
        public int BonusPercentAt(float speed)
        {
            return InitialSpeed > 0f ? Math.Max(0, (int)MathF.Round((speed / InitialSpeed - 1f) * 100f)) : 0;
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(RunTuning));
            v.Positive(nameof(InitialSpeed), InitialSpeed);
            v.NonNegative(nameof(Acceleration), Acceleration);
            v.AtMost(nameof(InitialSpeed), InitialSpeed, nameof(MaxSpeed), MaxSpeed);
            v.InRange(nameof(JumpAngleDeg), JumpAngleDeg, 0f, 80f);
            v.Positive(nameof(TurnTicks), TurnTicks);
            v.Positive(nameof(RehitTicks), RehitTicks);
            return v;
        }
    }

    /// <summary>
    /// 行動の速さの強化（STORM RUNNER の Q。ウインドクローク。docs/14 1.2、C-3、C-12。値は【仮】）と、解いたときの突風。
    /// 強化の間は、移動・ワイヤー・攻撃のフレームが SpeedPercent % 速くなる（ヒットストップはそのまま。SpeedBoosts）。
    /// </summary>
    [Serializable]
    public sealed class SpeedBoostTuning
    {
        /// <summary>続く長さ（ティック。5 秒）。</summary>
        public int DurationTicks = 300;

        /// <summary>速さの上がる割合（%）。</summary>
        public int SpeedPercent = 20;

        /// <summary>突風の速さ（m/s）、届く距離（m）、幅の半分（m）、押す強さ（m/s。触れた相手と投射物の速度に、風の向きへ足す）。</summary>
        public float GustSpeed = 30f;
        public float GustRange = 15f;
        public float GustHalfWidth = 1f;
        public float GustPush = 12f;

        /// <summary>突風が消えるまで（ティック。届く距離 ÷ 速さ）。</summary>
        public int GustTicks => GustSpeed > 0f ? Math.Max(1, (int)MathF.Ceiling(GustRange / GustSpeed * SimTime.TicksPerSecond)) : 1;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(SpeedBoostTuning));
            v.Positive(nameof(DurationTicks), DurationTicks);
            v.InRange(nameof(SpeedPercent), SpeedPercent, 0f, 100f);
            v.Positive(nameof(GustSpeed), GustSpeed);
            v.Positive(nameof(GustRange), GustRange);
            v.Positive(nameof(GustHalfWidth), GustHalfWidth);
            v.NonNegative(nameof(GustPush), GustPush);
            return v;
        }
    }

    /// <summary>風の帯（STORM RUNNER の E。スカイロケット。docs/14 1.2、C-13。値は【仮】）。全員と投射物に効く。</summary>
    [Serializable]
    public sealed class WindZoneTuning
    {
        /// <summary>残る長さ（ティック。10 秒）。</summary>
        public int DurationTicks = 600;

        /// <summary>長さ（m。置いた点から照準の向きへ）と、幅の半分（m。幅 3m）。</summary>
        public float Length = 10f;
        public float HalfWidth = 1.5f;

        /// <summary>中にいる物を、向きへ加速する強さ（m/s²）と、向きの速さの上限（m/s）。</summary>
        public float Acceleration = 70f;
        public float SpeedLimit = 20f;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(WindZoneTuning));
            v.Positive(nameof(DurationTicks), DurationTicks);
            v.Positive(nameof(Length), Length);
            v.Positive(nameof(HalfWidth), HalfWidth);
            v.NonNegative(nameof(Acceleration), Acceleration);
            v.Positive(nameof(SpeedLimit), SpeedLimit);
            return v;
        }
    }

    /// <summary>
    /// 空の強化（STORM RUNNER の R。キャッスルインジエアー。docs/14 1.2。値は【仮】）。強化の間は、速さが SpeedPercent % 上がり
    /// （ウインドクロークと重なったら足す）、ワイヤーが弾く物にもつながり、最大射程まで何もなければ空中につながる。
    /// </summary>
    [Serializable]
    public sealed class SkyBoostTuning
    {
        /// <summary>続く長さ（ティック。10 秒）。</summary>
        public int DurationTicks = 600;

        /// <summary>速さの上がる割合（%）。</summary>
        public int SpeedPercent = 50;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(SkyBoostTuning));
            v.Positive(nameof(DurationTicks), DurationTicks);
            v.InRange(nameof(SpeedPercent), SpeedPercent, 0f, 100f);
            return v;
        }
    }

    /// <summary>ミラーボール（docs/14 1.3。値はすべて【仮】）。</summary>
    [Serializable]
    public sealed class MirrorBallTuning
    {
        /// <summary>残る長さ（ティック。5 秒）。</summary>
        public int DurationTicks = 300;

        /// <summary>置く位置（キャラクターの中心からの高さ。m。「自分の少し上」）。</summary>
        public float OffsetY = 2.2f;

        /// <summary>つないだ人のゲージがたまる間隔（ティック）と、1 回の量（0.1% 単位）。12 ティックごとに 1% で、1 秒に 5%。</summary>
        public int GaugeIntervalTicks = 12;
        public int GaugePerInterval = 10;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(MirrorBallTuning));
            v.Positive(nameof(DurationTicks), DurationTicks);
            v.Positive(nameof(GaugeIntervalTicks), GaugeIntervalTicks);
            v.NonNegative(nameof(GaugePerInterval), GaugePerInterval);
            return v;
        }
    }

    /// <summary>セーフティボックス（docs/14 1.3、3.2 の C-2・C-15・C-16。値は【仮】）。</summary>
    [Serializable]
    public sealed class SafeTuning
    {
        /// <summary>残る長さ（ティック。3 分）。過ぎたら、その場にばらまく。</summary>
        public int DurationTicks = 10800;

        /// <summary>チャージの間隔（ティック。5 秒）。間隔ごとに ChargeCoins 枚を足し、その後 ChargeRatePercent % にする。</summary>
        public int ChargeIntervalTicks = 300;
        public int ChargeCoins = 2;
        public int ChargeRatePercent = 110;

        /// <summary>チャージの上限（枚。ユーザーの決定、2026-10-16）。</summary>
        public int MaxCoins = 300;

        /// <summary>この枚数で金色に光る（表示用。ユーザーの決定）。</summary>
        public int GoldCoins = 150;

        /// <summary>壊れるまでのダメージ（C-2）。</summary>
        public int Durability = 30;

        /// <summary>長押しで預ける速さ（枚/秒）。</summary>
        public int DepositPerSecond = 30;

        /// <summary>箱の半分の大きさ（m）。</summary>
        public float HalfWidth = 0.7f;
        public float HalfHeight = 0.7f;

        /// <summary>長押しした ticks ティックで預ける枚数（手持ちと上限は別に考える）。</summary>
        public int DepositFor(int ticks)
        {
            return ticks * DepositPerSecond / SimTime.TicksPerSecond;
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(SafeTuning));
            v.Positive(nameof(DurationTicks), DurationTicks);
            v.Positive(nameof(ChargeIntervalTicks), ChargeIntervalTicks);
            v.NonNegative(nameof(ChargeCoins), ChargeCoins);
            v.InRange(nameof(ChargeRatePercent), ChargeRatePercent, 100f, 1000f);
            v.Positive(nameof(MaxCoins), MaxCoins);
            v.Positive(nameof(Durability), Durability);
            v.NonNegative(nameof(DepositPerSecond), DepositPerSecond);
            v.Positive(nameof(HalfWidth), HalfWidth);
            v.Positive(nameof(HalfHeight), HalfHeight);
            return v;
        }
    }

    /// <summary>コインの効率を上げる（フィーバータイム。C-17。値は【仮】）。</summary>
    [Serializable]
    public sealed class CoinBoostTuning
    {
        /// <summary>続く長さ（ティック。5 秒）。</summary>
        public int DurationTicks = 300;

        /// <summary>拾ったコインと戦闘の報酬に足す割合（%）。端数はためて 1 枚になったら加える。</summary>
        public int BonusPercent = 20;

        /// <summary>得るゲージに足す割合（%。ユーザーの要望、2026-10-16）。端数はためて加える（CombatState.GaugeBonusRemainder）。</summary>
        public int GaugeBonusPercent = 20;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(CoinBoostTuning));
            v.Positive(nameof(DurationTicks), DurationTicks);
            v.NonNegative(nameof(BonusPercent), BonusPercent);
            v.NonNegative(nameof(GaugeBonusPercent), GaugeBonusPercent);
            return v;
        }
    }

    /// <summary>投射物（コイントスなど。値は【仮】）。当たるか地形に触れると消える。撃った本人には当たらない（C-1）。</summary>
    [Serializable]
    public sealed class ProjectileTuning
    {
        /// <summary>出す位置（キャラクターの中心から。X は向いている方向が正。m）。</summary>
        public float OffsetX = 0.3f;
        public float OffsetY = 1f;

        /// <summary>初速（m/s。X は向いている方向が正）。</summary>
        public float VelocityX = 2f;
        public float VelocityY = 16f;

        /// <summary>重力（m/s²。0 ならまっすぐ飛ぶ）。</summary>
        public float Gravity = 30f;

        /// <summary>消えるまで（ティック）。</summary>
        public int LifetimeTicks = 40;

        /// <summary>当たりの半径（m）。</summary>
        public float Radius = 0.5f;

        /// <summary>当たったときの判定のデータ（位置・大きさ・出る時間は使わない）。</summary>
        public AttackHit Hit = new AttackHit
        {
            Damage = 5, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 2f, KnockbackY = 13f,
        };

        /// <summary>
        /// 撃ったときの手持ちのコイン（拠点で確定していない分）この枚数ごとに、威力に 1% を上乗せする（0 なら上乗せしない）。
        /// 上乗せは MaxHeldCoinBonusPercent まで（RISK TAKER のコイントス: 3 枚ごとに 1%、300 枚で +100%。ユーザーの要望、2026-10-16）。
        /// </summary>
        public int HeldCoinsPerBonusPercent;
        public int MaxHeldCoinBonusPercent;

        /// <summary>手持ちのコイン held 枚で撃ったときの、威力の上乗せ（%）。</summary>
        public int HeldCoinBonusPercent(int held)
        {
            return HeldCoinsPerBonusPercent > 0 ? Math.Min(Math.Max(held, 0) / HeldCoinsPerBonusPercent, MaxHeldCoinBonusPercent) : 0;
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(ProjectileTuning));
            v.NonNegative(nameof(Gravity), Gravity);
            v.Positive(nameof(LifetimeTicks), LifetimeTicks);
            v.Positive(nameof(Radius), Radius);
            v.NotNull(nameof(Hit), Hit);
            if (Hit != null)
            {
                v.NonNegative("Hit.Damage", Hit.Damage);
                v.NonNegative("Hit.HitstopTicks", Hit.HitstopTicks);
                v.NonNegative("Hit.HitstunTicks", Hit.HitstunTicks);
            }
            v.NonNegative(nameof(HeldCoinsPerBonusPercent), HeldCoinsPerBonusPercent);
            v.NonNegative(nameof(MaxHeldCoinBonusPercent), MaxHeldCoinBonusPercent);
            return v;
        }
    }

    /// <summary>キャラクターの技の効果の値（効果の種類ごとに 1 つ。使わない効果の値はそのまま）。</summary>
    [Serializable]
    public sealed class SkillTuning
    {
        public MirrorBallTuning MirrorBall = new MirrorBallTuning();
        public SafeTuning Safe = new SafeTuning();
        public CoinBoostTuning CoinBoost = new CoinBoostTuning();
        public ProjectileTuning Projectile = new ProjectileTuning();
        public RunTuning Run = new RunTuning();
        public SpeedBoostTuning SpeedBoost = new SpeedBoostTuning();
        public WindZoneTuning WindZone = new WindZoneTuning();
        public SkyBoostTuning SkyBoost = new SkyBoostTuning();

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(SkillTuning));
            v.NotNull(nameof(MirrorBall), MirrorBall);
            v.NotNull(nameof(Safe), Safe);
            v.NotNull(nameof(CoinBoost), CoinBoost);
            v.NotNull(nameof(Projectile), Projectile);
            v.NotNull(nameof(Run), Run);
            v.NotNull(nameof(SpeedBoost), SpeedBoost);
            v.NotNull(nameof(WindZone), WindZone);
            v.NotNull(nameof(SkyBoost), SkyBoost);
            if (Run != null)
            {
                v.Merge(Run.Validate());
            }
            if (SpeedBoost != null)
            {
                v.Merge(SpeedBoost.Validate());
            }
            if (WindZone != null)
            {
                v.Merge(WindZone.Validate());
            }
            if (SkyBoost != null)
            {
                v.Merge(SkyBoost.Validate());
            }
            if (MirrorBall != null)
            {
                v.Merge(MirrorBall.Validate());
            }
            if (Safe != null)
            {
                v.Merge(Safe.Validate());
            }
            if (CoinBoost != null)
            {
                v.Merge(CoinBoost.Validate());
            }
            if (Projectile != null)
            {
                v.Merge(Projectile.Validate());
            }
            return v;
        }
    }
}
