using System;
using Airerage.Core.Combat;
using Airerage.Core.Common;

namespace Airerage.Core.Items
{
    /// <summary>
    /// アイテムの値（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。ユーザーが決めた数（割合・威力・クールタイム・詠唱の長さ）のほかは【仮】。
    /// 全員が同じ値を使う（SimWorld.ItemTuning）。
    /// </summary>
    [Serializable]
    public sealed class ItemTuning
    {
        // ---------------------------------------------------------------- パッシブ

        /// <summary>ブースター（Q・E のクールタイム、R のゲージの消費量）を減らす割合（%）。</summary>
        public int BoosterPercent = 20;

        /// <summary>ブースター極で減らす割合（%）と、引き換えに増やす割合（%。極の Q・E は反対の技を、極の R は Q と E を）。</summary>
        public int BoosterExPercent = 40;
        public int BoosterExPenaltyPercent = 20;
        public int UltimateExPenaltyPercent = 10;

        /// <summary>光子の盾でガードのクールタイムを減らす割合（%）。</summary>
        public int ShieldPercent = 20;

        /// <summary>マガジンマガジンで増やすスタック数（投射物の技とアイテム）。</summary>
        public int MagazineCharges = 1;

        /// <summary>グリーンバナナで設置物を大きくする倍率（見た目と判定）。</summary>
        public float BananaScale = 1.5f;

        /// <summary>ダイアモンドアレイで直接攻撃の技とアイテムの威力に足す割合（%。1.5 倍 = +50%。AttackPower の上乗せ）。</summary>
        public int DiamondDamagePercent = 50;

        // ---------------------------------------------------------------- 落ちているアイテム

        /// <summary>ドロップの頻度（少・中・多）ごとの、1 個落ちてくるまでのティック（30 秒に 4・8・12 個ほど）。</summary>
        public int[] DropIntervalTicks = { 450, 225, 150 };

        /// <summary>落ちているアイテムの半分の大きさ（m。拾える範囲もこの箱とキャラクターの箱の重なり）、重力（m/s²）、落ちる速さの上限（m/s）。</summary>
        public float DropHalfSize = 0.45f;
        public float DropGravity = 30f;
        public float DropMaxFallSpeed = 20f;

        /// <summary>フィールドに同時に落ちていられる数（これ以上は落ちてこない）【仮】。</summary>
        public int MaxFieldItems = 16;

        /// <summary>捨てたアイテムが跳ねる速さ（m/s。上向き）と、捨てた人がもう一度拾えるまで（ティック）【仮】。</summary>
        public float DiscardPopSpeed = 6f;
        public int DiscardPickupDelayTicks = 20;

        // ---------------------------------------------------------------- エネルギーショット

        /// <summary>エネルギーショット: クールタイム（8 秒）、速さ、当たりの半径、届く距離、出す距離（中心から照準の方向）。</summary>
        public int ShotCooldownTicks = 480;
        public float ShotSpeed = 26f;
        public float ShotRadius = 0.45f;
        public float ShotRange = 30f;
        public float ShotMuzzle = 1f;

        /// <summary>エネルギーショットの攻撃（威力 10。ユーザーの案）。</summary>
        public AttackHit ShotHit = new AttackHit
        {
            Damage = 10, Gauge = 60, HitstopTicks = 8, HitstunTicks = 22, KnockbackX = 10f, KnockbackY = 6f,
        };

        // ---------------------------------------------------------------- 名刀『櫓落とし』

        /// <summary>
        /// 櫓落とし: クールタイム（15 秒）、1 振りの長さ（ティック。縦・横・斜めの 3 振り）、詠唱（0.5 秒）、突進の長さ（ティック）と距離（m）、
        /// 一閃の判定の太さ（半分。m）、振りの判定の前への距離と半分の大きさ（m）。
        /// </summary>
        public int SwordCooldownTicks = 900;
        public int SwordSlashTicks = 10;
        public int SwordCastTicks = 30;
        public int SwordDashTicks = 6;
        public float SwordDashDistance = 9f;
        public float SwordFlashHalfWidth = 0.9f;
        public float SwordReach = 1.4f;
        public float SwordSlashHalfLength = 1.4f;
        public float SwordSlashHalfWidth = 0.5f;

        /// <summary>3 段の振り（威力 5）と一閃（威力 30、当たると FATAL。ユーザーの案）。</summary>
        public AttackHit SwordSlashHit = new AttackHit
        {
            Damage = 5, Gauge = 40, HitstopTicks = 6, HitstunTicks = 24, KnockbackX = 3f, KnockbackY = 4f,
        };
        public AttackHit SwordFlashHit = new AttackHit
        {
            Damage = 30, Gauge = 100, HitstopTicks = 16, HitstunTicks = 40, KnockbackX = 16f, KnockbackY = 12f,
        };

        // ---------------------------------------------------------------- イヤーボーン

        /// <summary>イヤーボーン: クールタイム（15 秒）、詠唱（0.2 秒）、風の半径（m）、押し出す速さ（m/s。外向き）と上向きの速さ。</summary>
        public int EarCooldownTicks = 900;
        public int EarCastTicks = 12;
        public float EarRadius = 5.5f;
        public float EarPushSpeed = 24f;
        public float EarPushUp = 4f;

        // ---------------------------------------------------------------- イカロスウイング

        /// <summary>イカロスウイング: クールタイム（12 秒）、詠唱（0.2 秒）、飛べる長さ（1 秒）、点滅し始める残り（1 秒）。</summary>
        public int WingCooldownTicks = 720;
        public int WingCastTicks = 12;
        public int WingFlightTicks = 60;
        public int WingBlinkTicks = 60;

        // ---------------------------------------------------------------- 砂岩ボール

        /// <summary>
        /// 砂岩ボール: クールタイム（10 秒。ボールが消えてから数える）、消えるまで（5 秒）、半径（m）、出す距離（m）、重力（m/s²）、
        /// 地形で跳ね返る割合、地面を転がるときの減速（m/s²）、落ちる速さの上限（m/s）。
        /// </summary>
        public int BallCooldownTicks = 600;
        public int BallLifetimeTicks = 300;
        public float BallRadius = 0.6f;
        public float BallSpawnDistance = 1.4f;
        public float BallGravity = 30f;
        public float BallRestitution = 0.45f;
        public float BallRollFriction = 6f;
        public float BallMaxFallSpeed = 30f;

        /// <summary>
        /// 動いている砂岩ボールに当たった人の攻撃: 威力は速さ × DamagePerSpeed（四捨五入）、吹き飛びは速さ × KnockbackPerSpeed（ボールの向き）。
        /// MinHarmSpeed より遅いボールは当たらない。最後に打った人（打たれていなければ持ち主）には当たらない【仮】。
        /// </summary>
        public float BallMinHarmSpeed = 6f;
        public float BallDamagePerSpeed = 0.5f;
        public float BallKnockbackPerSpeed = 0.8f;
        public int BallHitstunTicks = 26;
        public int BallHitstopTicks = 8;
        public int BallGauge = 60;

        /// <summary>攻撃で打たれたときの速さ（攻撃の吹き飛び × この値）と、速さの上限（m/s）。</summary>
        public float BallStrikeScale = 1f;
        public float BallMaxSpeed = 40f;

        // ---------------------------------------------------------------- 共通

        /// <summary>アクティブアイテム id のクールタイム（ティック。スタック 1 つが戻るまで）。パッシブは 0。</summary>
        public int CooldownOf(ItemId id)
        {
            switch (id)
            {
                case ItemId.EnergyShot:
                    return ShotCooldownTicks;
                case ItemId.Yaguraotoshi:
                    return SwordCooldownTicks;
                case ItemId.EarBone:
                    return EarCooldownTicks;
                case ItemId.IcarusWing:
                    return WingCooldownTicks;
                case ItemId.SandstoneBall:
                    return BallCooldownTicks;
                default:
                    return 0;
            }
        }

        /// <summary>詠唱の長さ（ティック。詠唱のないアイテムは 0）。</summary>
        public int CastTicksOf(ItemId id)
        {
            switch (id)
            {
                case ItemId.Yaguraotoshi:
                    return SwordCastTicks;
                case ItemId.EarBone:
                    return EarCastTicks;
                case ItemId.IcarusWing:
                    return WingCastTicks;
                default:
                    return 0;
            }
        }

        /// <summary>ドロップの頻度 rate（0〜2）の、1 個落ちてくるまでのティック。</summary>
        public int DropIntervalOf(int rate)
        {
            return DropIntervalTicks[Math.Max(0, Math.Min(rate, DropIntervalTicks.Length - 1))];
        }

        /// <summary>櫓落としの 3 振りが終わるまで（ティック）。</summary>
        public int SwordSlashesTicks => SwordSlashTicks * 3;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(ItemTuning));
            v.InRange(nameof(BoosterPercent), BoosterPercent, 0f, 99f);
            v.InRange(nameof(BoosterExPercent), BoosterExPercent, 0f, 99f);
            v.NonNegative(nameof(BoosterExPenaltyPercent), BoosterExPenaltyPercent);
            v.NonNegative(nameof(UltimateExPenaltyPercent), UltimateExPenaltyPercent);
            v.InRange(nameof(ShieldPercent), ShieldPercent, 0f, 99f);
            v.NonNegative(nameof(MagazineCharges), MagazineCharges);
            v.Positive(nameof(BananaScale), BananaScale);
            v.NonNegative(nameof(DiamondDamagePercent), DiamondDamagePercent);
            v.NotNull(nameof(DropIntervalTicks), DropIntervalTicks);
            if (DropIntervalTicks != null)
            {
                if (DropIntervalTicks.Length == 0)
                {
                    v.Fail("ドロップの頻度が 1 つもありません");
                }
                for (int i = 0; i < DropIntervalTicks.Length; i++)
                {
                    v.Positive($"DropIntervalTicks[{i}]", DropIntervalTicks[i]);
                }
            }
            v.Positive(nameof(DropHalfSize), DropHalfSize);
            v.NonNegative(nameof(DropGravity), DropGravity);
            v.Positive(nameof(DropMaxFallSpeed), DropMaxFallSpeed);
            v.Positive(nameof(MaxFieldItems), MaxFieldItems);
            v.NonNegative(nameof(DiscardPickupDelayTicks), DiscardPickupDelayTicks);
            v.Positive(nameof(ShotCooldownTicks), ShotCooldownTicks);
            v.Positive(nameof(ShotSpeed), ShotSpeed);
            v.Positive(nameof(ShotRadius), ShotRadius);
            v.Positive(nameof(ShotRange), ShotRange);
            v.NotNull(nameof(ShotHit), ShotHit);
            v.Positive(nameof(SwordCooldownTicks), SwordCooldownTicks);
            v.Positive(nameof(SwordSlashTicks), SwordSlashTicks);
            v.NonNegative(nameof(SwordCastTicks), SwordCastTicks);
            v.Positive(nameof(SwordDashTicks), SwordDashTicks);
            v.NonNegative(nameof(SwordDashDistance), SwordDashDistance);
            v.Positive(nameof(SwordFlashHalfWidth), SwordFlashHalfWidth);
            v.NotNull(nameof(SwordSlashHit), SwordSlashHit);
            v.NotNull(nameof(SwordFlashHit), SwordFlashHit);
            v.Positive(nameof(EarCooldownTicks), EarCooldownTicks);
            v.NonNegative(nameof(EarCastTicks), EarCastTicks);
            v.Positive(nameof(EarRadius), EarRadius);
            v.Positive(nameof(WingCooldownTicks), WingCooldownTicks);
            v.NonNegative(nameof(WingCastTicks), WingCastTicks);
            v.Positive(nameof(WingFlightTicks), WingFlightTicks);
            v.Positive(nameof(BallCooldownTicks), BallCooldownTicks);
            v.Positive(nameof(BallLifetimeTicks), BallLifetimeTicks);
            v.Positive(nameof(BallRadius), BallRadius);
            v.InRange(nameof(BallRestitution), BallRestitution, 0f, 1f);
            v.Positive(nameof(BallMaxSpeed), BallMaxSpeed);
            return v;
        }
    }
}
