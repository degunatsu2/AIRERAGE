using System;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Match
{
    /// <summary>試合の段階（MatchRules.PhaseAt）。</summary>
    public enum MatchPhase : byte
    {
        /// <summary>始まる前のカウントダウン。入力を受け付けない。</summary>
        Countdown = 0,

        /// <summary>試合中。</summary>
        Playing = 1,

        /// <summary>時間切れで終わった。全員が止まる。</summary>
        Finished = 2,
    }

    /// <summary>
    /// 試合のルール（docs/12_match_plan.md T4-1、T4-2）。値はすべて【仮】（docs/01_game_design.md 1章、2章、6.8）。
    /// 試合の段階はティック数だけから決まる（状態を持たないので、巻き戻しても全員で同じになる）。
    /// オンラインでは、ホストが試合の開始のときに全員へ送る。
    /// </summary>
    [Serializable]
    public sealed class MatchRules
    {
        /// <summary>始まる前のカウントダウン（ティック）。この間は入力を無視する。</summary>
        public int CountdownTicks = 180;

        /// <summary>試合の長さ（ティック。カウントダウンの後から数える）。0 なら時間の制限はない。</summary>
        public int DurationTicks = 10800;

        /// <summary>撃破されてから復帰するまで（ティック）。この間は出現位置で待ち、動かず、攻撃も受けない。</summary>
        public int RespawnDelayTicks = 180;

        /// <summary>復帰した後の無敵（ティック）。攻撃を受けない。自分が攻撃を出すと、そこで無敵が終わる【仮】。</summary>
        public int InvincibleTicks = 90;

        /// <summary>時間の制限もカウントダウンもない（検証用のシーン、入力の記録の再生）。撃破と復帰のルールは同じ。</summary>
        public static MatchRules FreePlay()
        {
            return new MatchRules { CountdownTicks = 0, DurationTicks = 0 };
        }

        /// <summary>時間の制限があるか。</summary>
        public bool HasTimeLimit => DurationTicks > 0;

        /// <summary>ティック tick（それまでに進めたティック数）からの1ティックが、試合のどの段階か。</summary>
        public MatchPhase PhaseAt(int tick)
        {
            if (tick < CountdownTicks)
            {
                return MatchPhase.Countdown;
            }
            if (HasTimeLimit && tick >= CountdownTicks + DurationTicks)
            {
                return MatchPhase.Finished;
            }
            return MatchPhase.Playing;
        }

        /// <summary>
        /// 今の段階の残り（ティック）: カウントダウン中は始まるまで、試合中は終わるまで（時間の制限がなければ -1）、終わった後は 0。
        /// </summary>
        public int TicksLeftAt(int tick)
        {
            switch (PhaseAt(tick))
            {
                case MatchPhase.Countdown:
                    return CountdownTicks - tick;
                case MatchPhase.Playing:
                    return HasTimeLimit ? CountdownTicks + DurationTicks - tick : -1;
                default:
                    return 0;
            }
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(MatchRules));
            v.NonNegative(nameof(CountdownTicks), CountdownTicks);
            v.NonNegative(nameof(DurationTicks), DurationTicks);
            v.NonNegative(nameof(RespawnDelayTicks), RespawnDelayTicks);
            v.NonNegative(nameof(InvincibleTicks), InvincibleTicks);
            return v;
        }
    }
}
