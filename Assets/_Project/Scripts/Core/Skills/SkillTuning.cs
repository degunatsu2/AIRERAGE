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

        /// <summary>
        /// ワープポータルを置く（SPACE TRAVELER の Q）。長押ししてから離すと、置いたポータルにそのまま入る（もう 1 つのポータルへ移る）。
        /// </summary>
        Portal,

        /// <summary>照準の方向へまっすぐ飛ぶミサイルを撃つ（SPACE TRAVELER の E。長押しで狙い、離して撃つ）。</summary>
        Missile,

        /// <summary>自分のワープポータル 2 つの間にビームを撃つ（SPACE TRAVELER の R。コズミックレイ。ポータルが 2 つないと出せない）。</summary>
        CosmicRay,

        /// <summary>ゆっくり落ちる（SPACE TRAVELER の地上技の後。別の移動をするまで）。</summary>
        SlowFall,
    }

    /// <summary>
    /// 走り続ける地上技（AttackData.Run。STORM RUNNER。docs/14 1.2、C-10、C-11。値は【仮】）。向いている方向へ走り、だんだん加速する。
    /// 足場が途切れたら・壁にぶつかったら浅い角度で跳ぶ（壁なら反対側へ）。着地したらそのまま走る。逆向きに入力すると、反転の後に
    /// 速さを初めに戻して逆向きに走る。体の判定のダメージは速さに比例する（初速で判定のダメージ。速さ × 0.4 になるように、
    /// 判定のダメージは初速 × 0.4 にする）。
    /// </summary>
    [Serializable]
    public sealed class RunTuning
    {
        /// <summary>初速（m/s。ユーザーの要望で 10 から上げた、2026-10-16）。</summary>
        public float InitialSpeed = 12.5f;

        /// <summary>加速（m/s²。地上を走っている間だけ）。</summary>
        public float Acceleration = 6f;

        /// <summary>最大の速さ（m/s。ユーザーの要望で 24 から上げた、2026-10-16）。</summary>
        public float MaxSpeed = 28f;

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

        /// <summary>
        /// 突風を撃つ前の溜め（ティック。この間はほとんど止まる）と、撃った後の硬直（ティック）、撃ったときの反動（m/s。撃つ向きと
        /// 逆へ。ユーザーの要望、2026-10-16）。
        /// </summary>
        public int GustWindupTicks = 10;
        public int GustRecoveryTicks = 14;
        public float GustRecoilSpeed = 9f;

        /// <summary>溜めの間、速度にかける数（ティックごと。ほとんど止まる）。</summary>
        public float GustWindupBrake = 0.8f;

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
            v.NonNegative(nameof(GustWindupTicks), GustWindupTicks);
            v.Positive(nameof(GustRecoveryTicks), GustRecoveryTicks);
            v.NonNegative(nameof(GustRecoilSpeed), GustRecoilSpeed);
            v.InRange(nameof(GustWindupBrake), GustWindupBrake, 0f, 1f);
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

    /// <summary>
    /// ワープポータル（SPACE TRAVELER の Q。docs/14 1.1、C-5〜C-7。値は【仮】）。自分のいる位置に置く。壊せない。自分のものは
    /// MaxPerOwner まで（増えたらいちばん古いものが消える）。同じ持ち主のポータル 2 つがつながり、片方に入るともう片方へ移る（速度は
    /// そのまま）。全員と投射物が使える。入るのは外から入ったときだけ（置いたとき・出たときにいたポータルは、いったん出てから入り直す）。
    /// 出た直後の WarpCooldownTicks は、どのポータルでも移らない。
    /// </summary>
    [Serializable]
    public sealed class PortalTuning
    {
        /// <summary>残る長さ（ティック。30 秒）。</summary>
        public int DurationTicks = 1800;

        /// <summary>半径（m）。</summary>
        public float Radius = 1.2f;

        /// <summary>持ち主ごとのポータルの数の上限。</summary>
        public int MaxPerOwner = 2;

        /// <summary>移った直後に、また移れるようになるまで（ティック。0.5 秒）。</summary>
        public int WarpCooldownTicks = 30;

        /// <summary>この長さ（ティック）以上長押ししてから離すと、置いたポータルにそのまま入る（ユーザーの要望、2026-10-16）。</summary>
        public int EnterHoldTicks = 12;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(PortalTuning));
            v.Positive(nameof(DurationTicks), DurationTicks);
            v.Positive(nameof(Radius), Radius);
            v.InRange(nameof(MaxPerOwner), MaxPerOwner, 2f, 8f);
            v.NonNegative(nameof(WarpCooldownTicks), WarpCooldownTicks);
            v.NonNegative(nameof(EnterHoldTicks), EnterHoldTicks);
            return v;
        }
    }

    /// <summary>
    /// スペースミサイル（SPACE TRAVELER の E。docs/14 1.1、C-8、D-63。値は【仮】）。照準の方向へまっすぐ飛び続け、キャラクター（撃った本人を
    /// 除く）・地形・壊せる物に触れるか、ステージの範囲の外に出ると爆発する（爆発の範囲の相手に当たる。壊せる物は壊す）。
    /// 威力は飛んでいた時間で上がる（最低 5、1 秒ごとに +10。AttackPower の上乗せ）。
    /// </summary>
    [Serializable]
    public sealed class MissileTuning
    {
        /// <summary>速さ（m/s）。</summary>
        public float Speed = 18f;

        /// <summary>当たりの半径（m）と、爆発の半径（m）。</summary>
        public float Radius = 0.4f;
        public float ExplosionRadius = 1.6f;

        /// <summary>出す位置の高さ（キャラクターの中心から。m）。</summary>
        public float OffsetY = 0.2f;

        /// <summary>1 秒ごとの威力の上乗せ（%。判定のダメージ 5 に 200% で、1 秒ごとに +10）。</summary>
        public int BonusPercentPerSecond = 200;

        /// <summary>消えるまでの上限（ティック。ステージの範囲がないときのため）。</summary>
        public int LifetimeTicks = 1200;

        /// <summary>当たったときの判定のデータ（位置・大きさ・出る時間は使わない）。</summary>
        public AttackHit Hit = new AttackHit
        {
            Damage = 5, HitstopTicks = 10, HitstunTicks = 24, KnockbackX = 9f, KnockbackY = 7f,
        };

        /// <summary>飛んでいた ticks ティックの威力の上乗せ（%）。</summary>
        public int BonusPercentAt(int ticks)
        {
            return Math.Max(0, ticks) * BonusPercentPerSecond / SimTime.TicksPerSecond;
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(MissileTuning));
            v.Positive(nameof(Speed), Speed);
            v.Positive(nameof(Radius), Radius);
            v.NonNegative(nameof(ExplosionRadius), ExplosionRadius);
            v.NonNegative(nameof(BonusPercentPerSecond), BonusPercentPerSecond);
            v.Positive(nameof(LifetimeTicks), LifetimeTicks);
            v.NotNull(nameof(Hit), Hit);
            return v;
        }
    }

    /// <summary>
    /// コズミックレイ（SPACE TRAVELER の R。docs/14 1.1、C-9、D-62。値は【仮】）。自分のワープポータル 2 つ（古い方から新しい方）の間に、
    /// 壁を貫通するビーム（太さ 2 × HalfWidth）を撃つ。IntervalTicks ごとに、ビームに触れている相手（撃った本人を除く）と壊せる物に当たる
    /// （Hits 回）。撃ち終わったときにビームの線が拠点に触れていれば、手持ちのコインを全部確定する。
    /// </summary>
    [Serializable]
    public sealed class BeamTuning
    {
        public float HalfWidth = 0.75f;
        public int IntervalTicks = 10;
        public int Hits = 6;

        /// <summary>1 回の判定のデータ（吹き飛びの X はビームから離れる向き、Y は上）。</summary>
        public AttackHit Hit = new AttackHit
        {
            Damage = 15, HitstopTicks = 6, HitstunTicks = 20, KnockbackX = 3f, KnockbackY = 4f,
        };

        /// <summary>ビームが残る長さ（ティック。最後の判定まで）。</summary>
        public int DurationTicks => IntervalTicks * Hits;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(BeamTuning));
            v.Positive(nameof(HalfWidth), HalfWidth);
            v.Positive(nameof(IntervalTicks), IntervalTicks);
            v.Positive(nameof(Hits), Hits);
            v.NotNull(nameof(Hit), Hit);
            return v;
        }
    }

    /// <summary>ゆっくり落ちる（SPACE TRAVELER の地上技の後。C-4。値は【仮】）。</summary>
    [Serializable]
    public sealed class SlowFallTuning
    {
        /// <summary>落ちる速さの上限（m/s）。</summary>
        public float MaxFallSpeed = 3f;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(SlowFallTuning));
            v.Positive(nameof(MaxFallSpeed), MaxFallSpeed);
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
        public PortalTuning Portal = new PortalTuning();
        public MissileTuning Missile = new MissileTuning();
        public BeamTuning Beam = new BeamTuning();
        public SlowFallTuning SlowFall = new SlowFallTuning();

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
            v.NotNull(nameof(Portal), Portal);
            v.NotNull(nameof(Missile), Missile);
            v.NotNull(nameof(Beam), Beam);
            v.NotNull(nameof(SlowFall), SlowFall);
            if (Portal != null)
            {
                v.Merge(Portal.Validate());
            }
            if (Missile != null)
            {
                v.Merge(Missile.Validate());
            }
            if (Beam != null)
            {
                v.Merge(Beam.Validate());
            }
            if (SlowFall != null)
            {
                v.Merge(SlowFall.Validate());
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
