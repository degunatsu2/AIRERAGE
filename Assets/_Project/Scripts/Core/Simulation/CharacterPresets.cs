using Airerage.Core.Combat;
using Airerage.Core.Skills;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// キャラクターの調整値の作り方（docs/13 T5-6、docs/14）。STANDARD は CharacterConfig の初期値そのもので、ほかのキャラクターは
    /// 違うところだけを書き換える。エディタのビルダーが、ここからキャラクターのアセット（CharacterConfigAsset）を作る。
    /// 値はすべて【仮】。
    /// </summary>
    public static class CharacterPresets
    {
        public const string StandardName = "STANDARD";
        public const string RiskTakerName = "RISK TAKER";

        /// <summary>STANDARD（基準のキャラクター。DASH・RISE・BURST）。</summary>
        public static CharacterConfig Standard()
        {
            return new CharacterConfig();
        }

        /// <summary>
        /// RISK TAKER（docs/14 1.3。ユーザーの案、2026-10-15）。コインで勝負するキャラクター。
        /// - 地上技 ALL IN: 大きく踏み込む一撃。当たると斜め上へ飛び出す。外すと隙が大きい（ハイリスク・ハイリターン。Claude の案）。
        /// - 空中・上 COIN TOSS: コインを投げ上げて頭上を攻撃する（投射物。Claude の案）。
        /// - 空中・下（撃破技）ROULETTE SMASH: 吹き飛びと威力の倍率がルーレット（0.5〜2 倍。最大で JACKPOT。Claude の案）。
        /// - Q LIGHT（ターンオンザライト）: 5 秒のミラーボールを自分の少し上に置く。アンカーとしてつなげて、つないだ人は 1 秒に 5%
        ///   のゲージがたまる。クールタイム 20 秒。
        /// - E SAFE（セーフティボックス）: 長押しで手持ちのコインを預けて（1 秒に 30 枚）金庫を置く。5 秒ごとに 2 枚足して 1.1 倍
        ///   （上限 300 枚）。壊す（30 ダメージ）か 3 分でばらまく。クールタイム 30 秒（壊れてから）。
        /// - R FEVER（フィーバータイム）: 5 秒間、拾ったコインと戦闘の報酬が 1.2 倍。必要なゲージ 50%。
        /// </summary>
        public static CharacterConfig RiskTaker()
        {
            var config = new CharacterConfig();
            CombatTuning combat = config.Combat;

            combat.Ground = new AttackData
            {
                Name = "ALL IN",
                TotalTicks = 42,
                WireCancelFromTick = 8,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 8, ActiveTicks = 8, OffsetX = 1.8f, HalfWidth = 2f, HalfHeight = 1.6f,
                        Damage = 9, HitstopTicks = 14, HitstunTicks = 26, KnockbackX = 14f, KnockbackY = 9f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 8, Ticks = 8, VelocityX = 20f },
                    new AttackMotion { StartTick = 16, Ticks = 1, VelocityX = 9f, VelocityY = 14f, RequiresHit = true },
                },
            };

            combat.AirUp = new AttackData
            {
                Name = "COIN TOSS",
                TotalTicks = 22,
                WireCancelFromTick = 6,
                Effect = SkillEffect.Projectile,
                EffectTick = 5,
                Hits = new AttackHit[0],
            };

            combat.AirDown.Name = "ROULETTE SMASH";
            combat.AirDown.Roulette = true;
            combat.KoAttack = AttackSlot.AirDown;

            combat.Special1 = new AttackData
            {
                Name = "LIGHT",
                TotalTicks = 16,
                WireCancelFromTick = 4,
                Effect = SkillEffect.MirrorBall,
                EffectTick = 4,
                Hits = new AttackHit[0],
            };
            combat.Special1CooldownTicks = 1200;

            combat.Special2 = new AttackData
            {
                Name = "SAFE",
                TotalTicks = 14,
                WireCancelFromTick = 4,
                Effect = SkillEffect.Safe,
                EffectTick = 0,
                HoldMaxTicks = 600,
                Hits = new AttackHit[0],
            };
            combat.Special2CooldownTicks = 1800;

            combat.Super = new AttackData
            {
                Name = "FEVER",
                TotalTicks = 24,
                WireCancelFromTick = 12,
                Effect = SkillEffect.CoinBoost,
                EffectTick = 0,
                Hits = new AttackHit[0],
            };
            combat.SuperGaugeCost = 50 * CombatTuning.GaugePerPercent;
            return config;
        }
    }
}
