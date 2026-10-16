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

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(CoinBoostTuning));
            v.Positive(nameof(DurationTicks), DurationTicks);
            v.NonNegative(nameof(BonusPercent), BonusPercent);
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

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(SkillTuning));
            v.NotNull(nameof(MirrorBall), MirrorBall);
            v.NotNull(nameof(Safe), Safe);
            v.NotNull(nameof(CoinBoost), CoinBoost);
            v.NotNull(nameof(Projectile), Projectile);
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
