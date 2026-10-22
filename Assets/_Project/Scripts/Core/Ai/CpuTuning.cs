using System;
using Airerage.Core.Common;

namespace Airerage.Core.Ai
{
    /// <summary>
    /// CPU の強さの段階（D-71。ユーザーの決定、2026-10-22: 4 段階。10 点満点の手ごわさで EASY 1・NORMAL 3・HARD 6・HELL 10 くらい）。
    /// </summary>
    public enum CpuLevel : byte
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Hell = 3,
    }

    /// <summary>
    /// CPU の強さの調整値（docs/16 3 章。値はすべて【仮】）。反応の遅れ・狙いのずれ・攻撃と守りと技の頻度で強さを変える。
    /// HELL だけは、当たったら次の攻撃でキャンセルしてつなぎ（CancelOnHit）、相手の攻撃が出る直前にガードしてジャストガードを狙う
    /// （TimedGuard）。段階ごとの値は For で作る（値はここの 1 か所で決める）。
    /// </summary>
    [Serializable]
    public sealed class CpuTuning
    {
        /// <summary>何をするか（目標）を選び直す間隔（ティック）。長いほど状況の変化に気づくのが遅い。</summary>
        public int DecideEveryTicks = 12;

        /// <summary>相手が攻撃の届く所に入ってから、攻撃を出すまでの遅れ（ティック）。</summary>
        public int ReactionTicks = 10;

        /// <summary>攻撃を出してから、次の攻撃を考えるまでの間（ティック。攻撃の長さとは別）。</summary>
        public int AttackIntervalTicks = 20;

        /// <summary>ワイヤーを撃つ方向のずれ（度。左右に一様）。</summary>
        public float AimErrorDegrees = 10f;

        /// <summary>相手の攻撃が来そうなときにガードする確率（0〜1）。</summary>
        public float GuardChance = 0.3f;

        /// <summary>相手が近く、技を使えるときに使う確率（判断ごと。0〜1）。</summary>
        public float SkillChance = 0.25f;

        /// <summary>相手を攻めることの欲しさ（コイン・拠点と比べる重み）。</summary>
        public float Aggression = 1f;

        /// <summary>この枚数以上のコインを持っていたら、拠点へ確定しに行く。</summary>
        public int BankAtHeldCoins = 20;

        /// <summary>攻撃が当たったら、終わるのを待たずに次の攻撃でキャンセルしてつなぐ（D-17 の 1 回。攻撃の間 AttackIntervalTicks も待たない）。</summary>
        public bool CancelOnHit;

        /// <summary>ガードを、相手の攻撃の判定が出る直前まで待ってから押す（ジャストガードを狙う）。false なら相手が攻撃を出したらすぐ押す。</summary>
        public bool TimedGuard;

        /// <summary>段階 level の値。</summary>
        public static CpuTuning For(CpuLevel level)
        {
            switch (level)
            {
                case CpuLevel.Easy:
                    // 1 点: 初めての人でも勝てる。のんびり判断し、攻撃はまばらで、ガードしない。
                    return new CpuTuning
                    {
                        DecideEveryTicks = 36, ReactionTicks = 30, AttackIntervalTicks = 60, AimErrorDegrees = 25f, GuardChance = 0f,
                        SkillChance = 0.05f, Aggression = 0.4f, BankAtHeldCoins = 40,
                    };
                case CpuLevel.Hard:
                    // 6 点: 慣れた人と互角くらい。
                    return new CpuTuning
                    {
                        DecideEveryTicks = 8, ReactionTicks = 6, AttackIntervalTicks = 12, AimErrorDegrees = 5f, GuardChance = 0.4f,
                        SkillChance = 0.35f, Aggression = 1.2f, BankAtHeldCoins = 18,
                    };
                case CpuLevel.Hell:
                    // 10 点: ほとんど待たずに判断・反応し、当たればつなぎ、ジャストガードを狙う。
                    return new CpuTuning
                    {
                        DecideEveryTicks = 3, ReactionTicks = 1, AttackIntervalTicks = 0, AimErrorDegrees = 1f, GuardChance = 0.85f,
                        SkillChance = 0.6f, Aggression = 1.6f, BankAtHeldCoins = 12, CancelOnHit = true, TimedGuard = true,
                    };
                default:
                    // 3 点: 少し慣れれば勝てる。
                    return new CpuTuning
                    {
                        DecideEveryTicks = 18, ReactionTicks = 16, AttackIntervalTicks = 30, AimErrorDegrees = 13f, GuardChance = 0.15f,
                        SkillChance = 0.18f, Aggression = 0.8f, BankAtHeldCoins = 25,
                    };
            }
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(CpuTuning));
            v.Positive(nameof(DecideEveryTicks), DecideEveryTicks);
            v.NonNegative(nameof(ReactionTicks), ReactionTicks);
            v.NonNegative(nameof(AttackIntervalTicks), AttackIntervalTicks);
            v.InRange(nameof(AimErrorDegrees), AimErrorDegrees, 0f, 90f);
            v.InRange(nameof(GuardChance), GuardChance, 0f, 1f);
            v.InRange(nameof(SkillChance), SkillChance, 0f, 1f);
            v.NonNegative(nameof(Aggression), Aggression);
            v.Positive(nameof(BankAtHeldCoins), BankAtHeldCoins);
            return v;
        }
    }
}
