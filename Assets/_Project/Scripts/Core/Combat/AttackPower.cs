using System;
using System.Text;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;

namespace Airerage.Core.Combat
{
    /// <summary>
    /// 攻撃の強さの元（倍率・上乗せの出どころ。D-64）。新しい元を足すときは、ここに名前を足し、AttackPower.Of（か投射物を作るところ）で
    /// 倍率か上乗せのどちらかとして足す。倍率に入れてよいのは、技の個性になる「その瞬間の条件」だけ（むやみに増やさない）。
    /// </summary>
    [Flags]
    public enum PowerSource : ushort
    {
        None = 0,

        /// <summary>倍率: 撃破技を打つ直前の速さ（D-38）。</summary>
        KoSpeed = 1 << 0,

        /// <summary>倍率: ルーレットの目（RISK TAKER のルーレットスマッシュ。速さの倍率の代わり）。</summary>
        Roulette = 1 << 1,

        /// <summary>上乗せ（威力と吹き飛び）: ためた長さ（RISK TAKER のオールイン。ChargeLevel）。</summary>
        Charge = 1 << 2,

        /// <summary>上乗せ（威力）: 撃ったときの手持ちのコイン（RISK TAKER のコイントス）。</summary>
        HeldCoins = 1 << 3,

        /// <summary>上乗せ（威力）: 走る速さ（STORM RUNNER の地上技）。</summary>
        RunSpeed = 1 << 4,

        /// <summary>上乗せ（威力）: ミサイルが飛んでいた時間（SPACE TRAVELER のスペースミサイル）。</summary>
        FlightTime = 1 << 5,
    }

    /// <summary>
    /// 攻撃の強さ（命中 1 つの倍率と上乗せ）を決める、ただ 1 つの場所（D-64。ユーザーの決定、2026-10-16: 推奨どおり (b)）。
    ///   吹き飛び = 判定の吹き飛び × 倍率 × (1 + 吹き飛びの上乗せ ÷ 100)
    ///   威力（ダメージ）= 判定のダメージ × 倍率 × (1 + 威力の上乗せ ÷ 100)（四捨五入）
    /// - 倍率（×。掛ける。吹き飛びと威力の両方）: 技の個性になる「その瞬間の条件」だけ（PowerSource の KoSpeed、Roulette）。
    /// - 上乗せ（%。すべて足す）: 状態や準備で上がるもの（ため、手持ちのコイン、走る速さ）。これから増える強化・弱体化もここに足す
    ///   （同じ種類がいくつ重なっても、足し算なので強さが読みやすい）。威力の上乗せと吹き飛びの上乗せは別に足す（ためは両方）。
    /// 結果と内訳（Sources）は命中の記録（HitRecord）に残し、デバッグ表示で見られるようにする。
    /// </summary>
    public readonly struct AttackPower
    {
        /// <summary>倍率（吹き飛びと威力）。</summary>
        public readonly float Multiplier;

        /// <summary>威力の上乗せ（%）。</summary>
        public readonly int BonusPercent;

        /// <summary>吹き飛びの上乗せ（%）。</summary>
        public readonly int KnockbackBonusPercent;

        /// <summary>FATAL（FatalStrikeSpeed 以上で打った撃破技。当たれば、ヒットストップが長くなる）。</summary>
        public readonly bool Fatal;

        /// <summary>JACKPOT（ルーレットの最大か、ためた技の最後の段階。当たれば JACKPOT の演出）。</summary>
        public readonly bool Jackpot;

        /// <summary>かかった強さの元（内訳。表示用）。</summary>
        public readonly PowerSource Sources;

        public AttackPower(float multiplier, int bonusPercent, int knockbackBonusPercent = 0, bool fatal = false, bool jackpot = false,
            PowerSource sources = PowerSource.None)
        {
            Multiplier = multiplier;
            BonusPercent = bonusPercent;
            KnockbackBonusPercent = knockbackBonusPercent;
            Fatal = fatal;
            Jackpot = jackpot;
            Sources = sources;
        }

        /// <summary>何もかからない強さ（倍率 1、上乗せ 0）。</summary>
        public static AttackPower Normal => new AttackPower(1f, 0);

        /// <summary>威力にかける数（倍率 ×（1 + 威力の上乗せ））。</summary>
        public float DamageScale => ScaleOf(Multiplier, BonusPercent);

        /// <summary>吹き飛びにかける数（倍率 ×（1 + 吹き飛びの上乗せ））。</summary>
        public float KnockbackScale => ScaleOf(Multiplier, KnockbackBonusPercent);

        /// <summary>倍率 multiplier と上乗せ bonusPercent（%）の、かける数。</summary>
        public static float ScaleOf(float multiplier, int bonusPercent)
        {
            return multiplier * (1f + bonusPercent / 100f);
        }

        /// <summary>ダメージ damage に強さをかけた威力（四捨五入）。</summary>
        public static int Damage(int damage, float multiplier, int bonusPercent)
        {
            return (int)MathF.Round(damage * ScaleOf(multiplier, bonusPercent));
        }

        /// <summary>投射物の強さ（撃ったときに決めた威力の上乗せだけ。手持ちのコインなど）。</summary>
        public static AttackPower OfProjectile(int bonusPercent, PowerSource source)
        {
            return new AttackPower(1f, bonusPercent, 0, sources: bonusPercent != 0 ? source : PowerSource.None);
        }

        /// <summary>
        /// キャラクター（番号 index）が今出している攻撃の強さ。seed は試合の種（ルーレット）。
        /// </summary>
        public static AttackPower Of(in CharacterState attacker, int index, CharacterConfig config, int seed)
        {
            CombatTuning combat = config.Combat;
            AttackData attack = combat.Get(attacker.Action.Attack);
            float multiplier = 1f;
            int damageBonus = 0;
            int knockbackBonus = 0;
            bool fatal = false;
            bool jackpot = false;
            PowerSource sources = PowerSource.None;

            // 倍率。
            if (attack.Roulette)
            {
                int roll = HitResolver.RouletteRoll(seed, index, attacker.Combat.AttackSerial);
                multiplier *= AttackData.RouletteScales[roll];
                jackpot = roll == AttackData.RouletteScales.Length - 1;
                sources |= PowerSource.Roulette;
            }
            else if (attacker.Action.Attack == combat.KoAttack)
            {
                multiplier *= combat.KoKnockbackScale(attacker.Action.StrikeSpeed);
                fatal = attacker.Action.StrikeSpeed >= combat.FatalStrikeSpeed;
                sources |= PowerSource.KoSpeed;
            }

            // 上乗せ。
            if (attack.Run)
            {
                // 走る速さに比例して強くなる（強化の倍率をかけた速さ。STORM RUNNER。C-11）。
                damageBonus += config.Skills.Run.BonusPercentAt(attacker.Action.RunSpeed * SpeedBoosts.ScaleOf(attacker, config));
                sources |= PowerSource.RunSpeed;
            }
            int level = attack.ChargeLevelOf(attacker.Action.Charge);
            if (level >= 0)
            {
                // ためた長さ（威力と吹き飛びの両方。ユーザーの要望、2026-10-16）。
                damageBonus += attack.ChargeLevels[level].BonusPercent;
                knockbackBonus += attack.ChargeLevels[level].KnockbackPercent;
                jackpot |= attack.IsTopChargeLevel(level);
                sources |= PowerSource.Charge;
            }
            return new AttackPower(multiplier, damageBonus, knockbackBonus, fatal, jackpot, sources);
        }

        /// <summary>内訳の文字（「x1.20 +50% KB+50% [Roulette, Charge]」の形。デバッグ表示用）。</summary>
        public static string Describe(float multiplier, int bonusPercent, int knockbackBonusPercent, PowerSource sources)
        {
            var text = new StringBuilder();
            text.Append('x').Append(multiplier.ToString("0.00"));
            if (bonusPercent != 0)
            {
                text.Append(" +").Append(bonusPercent).Append('%');
            }
            if (knockbackBonusPercent != 0)
            {
                text.Append(" KB+").Append(knockbackBonusPercent).Append('%');
            }
            if (sources != PowerSource.None)
            {
                text.Append(" [").Append(sources).Append(']');
            }
            return text.ToString();
        }

        public override string ToString()
        {
            return Describe(Multiplier, BonusPercent, KnockbackBonusPercent, Sources)
                + (Fatal ? " FATAL" : string.Empty) + (Jackpot ? " JACKPOT" : string.Empty);
        }
    }
}
