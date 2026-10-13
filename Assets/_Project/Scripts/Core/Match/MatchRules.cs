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
    /// 試合のルール（docs/12_match_plan.md T4-1、T4-2、T4-4、T4-7）。値はすべて【仮】（docs/01_game_design.md 1章、2章、6.8）。
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

        /// <summary>
        /// コインが拾われてから、同じ出現地点にまた出るまで（ティック。docs/12 T4-4）。試合が始まったときは全部の地点に出す。
        /// 拾われてから数える（ユーザーの要望、2026-10-13）。
        /// </summary>
        public int CoinSpawnIntervalTicks = 600;

        /// <summary>100 枚のコイン（候補の地点の1つ）が出る、試合の残り（ティック。残り 1 分。ユーザーの要望）。時間の制限がなければ出ない。</summary>
        public int JackpotTicksBeforeEnd = 3600;

        /// <summary>拠点（バンク。docs/12 T4-7）が次の地点へ移る間隔（ティック。40 秒）【仮】。試合が始まってから数える。</summary>
        public int BankMoveIntervalTicks = 2400;

        /// <summary>拠点が移る何ティック前から、次の地点を予告するか（10 秒）【仮】。表示だけに使う。</summary>
        public int BankNoticeTicks = 600;

        /// <summary>拠点の円の半径（m）【仮】。キャラクターの箱が触れていれば、所持コインを確定する。</summary>
        public float BankRadius = 2f;

        /// <summary>試合ごとの乱数の種（100 枚のコインの場所、拠点の移動先を選ぶ。ホストが試合を始めるたびに決めて全員へ送る）。</summary>
        public int Seed;

        /// <summary>コインを拾える距離（m。キャラクターの箱をこれだけ広げて、コインの中心が入れば拾う）。</summary>
        public float CoinPickupRadius = 0.7f;

        /// <summary>時間の制限もカウントダウンもない（検証用のシーン、入力の記録の再生）。撃破と復帰のルールは同じ。</summary>
        public static MatchRules FreePlay()
        {
            return new MatchRules { CountdownTicks = 0, DurationTicks = 0 };
        }

        /// <summary>同じ値の写し（ホストが種や時間の制限だけを変えて送るときに使う）。</summary>
        public MatchRules Clone()
        {
            return (MatchRules)MemberwiseClone();
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
            v.Positive(nameof(CoinSpawnIntervalTicks), CoinSpawnIntervalTicks);
            v.Positive(nameof(CoinPickupRadius), CoinPickupRadius);
            v.NonNegative(nameof(JackpotTicksBeforeEnd), JackpotTicksBeforeEnd);
            v.Positive(nameof(BankMoveIntervalTicks), BankMoveIntervalTicks);
            v.NonNegative(nameof(BankNoticeTicks), BankNoticeTicks);
            v.Positive(nameof(BankRadius), BankRadius);
            return v;
        }
    }
}
