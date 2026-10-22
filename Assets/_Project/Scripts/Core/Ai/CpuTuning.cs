using System;
using Airerage.Core.Common;

namespace Airerage.Core.Ai
{
    /// <summary>CPU の強さの段階（D-71【仮】。docs/16 2 章）。</summary>
    public enum CpuLevel : byte
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    /// <summary>
    /// CPU の強さの調整値（docs/16 3 章。値はすべて【仮】）。反応の遅れ・狙いのずれ・攻撃と守りと技の頻度で強さを変える。
    /// 段階ごとの値は For で作る（値はここの 1 か所で決める）。
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

        /// <summary>段階 level の値。</summary>
        public static CpuTuning For(CpuLevel level)
        {
            switch (level)
            {
                case CpuLevel.Easy:
                    return new CpuTuning
                    {
                        DecideEveryTicks = 24, ReactionTicks = 22, AttackIntervalTicks = 40, AimErrorDegrees = 20f, GuardChance = 0.05f,
                        SkillChance = 0.1f, Aggression = 0.6f, BankAtHeldCoins = 30,
                    };
                case CpuLevel.Hard:
                    return new CpuTuning
                    {
                        DecideEveryTicks = 6, ReactionTicks = 4, AttackIntervalTicks = 6, AimErrorDegrees = 3f, GuardChance = 0.6f,
                        SkillChance = 0.45f, Aggression = 1.4f, BankAtHeldCoins = 15,
                    };
                default:
                    return new CpuTuning();
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
