using System;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;

namespace Airerage.Core.Combat
{
    /// <summary>
    /// 攻撃の強さ（命中 1 つの倍率と上乗せ）を決める、ただ 1 つの場所（D-64【提案】。ユーザーの質問、2026-10-16）。
    ///   吹き飛び = 判定の吹き飛び × 倍率
    ///   威力（ダメージ）= 判定のダメージ × 倍率 × (1 + 上乗せ ÷ 100)（四捨五入）
    /// - 倍率（掛ける。吹き飛びと威力の両方）: 技の個性になる「その瞬間の条件」だけにする。今は撃破技の速さ（D-38）とルーレット。
    ///   ルーレットの技は、速さの代わりにルーレットの目がそのまま倍率になる（ユーザーの要望、2026-10-16）。
    /// - 上乗せ（%。威力だけ。すべて足す）: ためた長さ（オールイン）、手持ちのコイン（コイントス）、走る速さ（STORM RUNNER の
    ///   地上技）。これから増える強化・弱体化も
    ///   ここに足す（同じ種類の上乗せがいくつ重なっても、足し算なので強さが読みやすい）。
    /// 計算はここだけで行い、結果は命中の記録（HitRecord の KnockbackScale と BonusPercent）に残す（デバッグ表示で内訳を見る）。
    /// </summary>
    public readonly struct AttackPower
    {
        /// <summary>倍率（吹き飛びと威力）。</summary>
        public readonly float Multiplier;

        /// <summary>威力の上乗せ（%）。</summary>
        public readonly int BonusPercent;

        /// <summary>FATAL（FatalStrikeSpeed 以上で打った撃破技。当たれば、ヒットストップが長くなる）。</summary>
        public readonly bool Fatal;

        /// <summary>JACKPOT（ルーレットの最大か、ためた技の最後の段階。当たれば JACKPOT の演出）。</summary>
        public readonly bool Jackpot;

        public AttackPower(float multiplier, int bonusPercent, bool fatal = false, bool jackpot = false)
        {
            Multiplier = multiplier;
            BonusPercent = bonusPercent;
            Fatal = fatal;
            Jackpot = jackpot;
        }

        /// <summary>何もかからない強さ（倍率 1、上乗せ 0）。</summary>
        public static AttackPower Normal => new AttackPower(1f, 0);

        /// <summary>威力にかける数（倍率 ×（1 + 上乗せ））。</summary>
        public float DamageScale => DamageScaleOf(Multiplier, BonusPercent);

        /// <summary>倍率 multiplier と上乗せ bonusPercent（%）の、威力にかける数。</summary>
        public static float DamageScaleOf(float multiplier, int bonusPercent)
        {
            return multiplier * (1f + bonusPercent / 100f);
        }

        /// <summary>ダメージ damage に強さをかけた威力（四捨五入）。</summary>
        public static int Damage(int damage, float multiplier, int bonusPercent)
        {
            return (int)MathF.Round(damage * DamageScaleOf(multiplier, bonusPercent));
        }

        /// <summary>
        /// キャラクター（番号 index）が今出している攻撃の強さ。seed は試合の種（ルーレット）。
        /// </summary>
        public static AttackPower Of(in CharacterState attacker, int index, CharacterConfig config, int seed)
        {
            CombatTuning combat = config.Combat;
            AttackData attack = combat.Get(attacker.Action.Attack);
            float multiplier = 1f;
            bool fatal = false;
            bool jackpot = false;
            if (attack.Roulette)
            {
                int roll = HitResolver.RouletteRoll(seed, index, attacker.Combat.AttackSerial);
                multiplier = AttackData.RouletteScales[roll];
                jackpot = roll == AttackData.RouletteScales.Length - 1;
            }
            else if (attacker.Action.Attack == combat.KoAttack)
            {
                multiplier = combat.KoKnockbackScale(attacker.Action.StrikeSpeed);
                fatal = attacker.Action.StrikeSpeed >= combat.FatalStrikeSpeed;
            }

            int bonus = 0;
            if (attack.Run)
            {
                // 走り続ける地上技は、走る速さに比例して強くなる（強化の倍率をかけた速さ。STORM RUNNER。C-11）。
                bonus += config.Skills.Run.BonusPercentAt(attacker.Action.RunSpeed * SpeedBoosts.ScaleOf(attacker, config));
            }
            int level = attack.ChargeLevelOf(attacker.Action.Charge);
            if (level >= 0)
            {
                bonus += attack.ChargeLevels[level].BonusPercent;
                jackpot |= attack.IsTopChargeLevel(level);
            }
            return new AttackPower(multiplier, bonus, fatal, jackpot);
        }

        public override string ToString()
        {
            return $"x{Multiplier:0.00} +{BonusPercent}%{(Fatal ? " FATAL" : string.Empty)}{(Jackpot ? " JACKPOT" : string.Empty)}";
        }
    }
}
