using System;
using Airerage.Core.Common;

namespace Airerage.Core.Match
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

        /// <summary>アディショナルタイム（時間切れのとき、逆転できる人がいれば。docs/12 4.2、T4-8）。試合中と同じく動ける。</summary>
        Overtime = 3,
    }

    /// <summary>
    /// 試合のルール（docs/12_match_plan.md T4-1、T4-2、T4-4、T4-6、T4-7）。値はすべて【仮】（docs/01_game_design.md 1章、2章、6.8）。
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

        /// <summary>アディショナルタイムの長さ（ティック。5 秒。20 秒から短くした。ユーザーの決定、2026-10-14）。</summary>
        public int OvertimeTicks = 300;

        /// <summary>コインの出現が増える終盤（試合の残り。ティック。30 秒）【仮】。アディショナルタイムも含む。</summary>
        public int LateGameTicks = 1800;

        /// <summary>終盤に、コインが拾われてから同じ地点にまた出るまで（ティック。5 秒。ふだんの半分）【仮】。</summary>
        public int LateGameSpawnIntervalTicks = 300;

        /// <summary>撃破されてから復帰するまで（ティック）。この間は出現位置で待ち、動かず、攻撃も受けない。</summary>
        public int RespawnDelayTicks = 180;

        /// <summary>復帰した後の無敵（ティック）。攻撃を受けない。自分が攻撃を出すと、そこで無敵が終わる【仮】。</summary>
        public int InvincibleTicks = 90;

        /// <summary>
        /// コインが拾われてから、同じ出現地点にまた出るまで（ティック。docs/12 T4-4）。試合が始まったときは全部の地点に出す。
        /// 拾われてから数える（ユーザーの要望、2026-10-13）。
        /// </summary>
        public int CoinSpawnIntervalTicks = 600;

        /// <summary>
        /// 100 枚のコイン（候補の地点の1つ）を予告する、試合の残り（ティック。残り 1 分。ユーザーの要望）。時間の制限がなければ出ない。
        /// 予告してから JackpotNoticeTicks の後に出る。
        /// </summary>
        public int JackpotTicksBeforeEnd = 3600;

        /// <summary>100 枚のコインを予告してから、出るまで（ティック。10 秒。ユーザーの要望、2026-10-14）。</summary>
        public int JackpotNoticeTicks = 600;

        /// <summary>被弾ドロップ（docs/12 T4-6）: コンボの最初の被弾で落とす、所持コインの割合（%）【仮】。少なくとも 1 枚。</summary>
        public int HitDropPercent = 10;

        /// <summary>被弾ドロップの上限（枚）【仮】。</summary>
        public int HitDropMax = 10;

        /// <summary>撃破ドロップ: 撃破されたときに落とす、所持コインの割合（%。端数は切り上げ）【仮】。自滅も同じ（D-44）。</summary>
        public int KoDropPercent = 50;

        /// <summary>撃破ドロップを落とす位置: ステージの外に出た位置を、ステージの範囲のこれだけ内側（m）に入れる【仮】。</summary>
        public float KoDropInset = 4f;

        /// <summary>ドロップしたコインを、だれも拾えない時間（ティック。1 秒。戦っている2人が攻撃の途中に拾わないように 0.5 秒から延ばした）【仮】。</summary>
        public int DropPickableDelayTicks = 60;

        /// <summary>ドロップしたコインの寿命（ティック。10 秒）【仮】。過ぎると消える。</summary>
        public int DropLifetimeTicks = 600;

        /// <summary>
        /// ダメージ報酬（docs/12 T4-5。ユーザーの決定、2026-10-14）: コンボが終わったときに、そのコンボのダメージのこの割合（%。端数は
        /// 切り捨て、少なくとも 1 枚）を、当てた人の所持コインに加える【仮】。0 なら払わない。同じ相手からの上限と減衰はない。
        /// </summary>
        public int DamageRewardPercent = 30;

        /// <summary>撃破報酬（T4-5）: 撃破した人の所持コインに加える、基本の枚数【仮】。自滅（手柄なし）では払わない。</summary>
        public int KoRewardBase = 20;

        /// <summary>撃破報酬の上乗せ: 撃破した相手が、撃破した人より 1 つ上の順位（確定コイン）にいるごとに加える枚数【仮】。</summary>
        public int KoRewardPerRank = 10;

        /// <summary>撃破報酬の上乗せの上限（枚）【仮】。4 人なら、最下位が首位を撃破すると 20 + 30 = 50 枚。同じ相手への減りはない。</summary>
        public int KoRewardMaxBonus = 30;

        /// <summary>拠点（バンク。docs/12 T4-7）が次の地点へ移る間隔（ティック。40 秒）【仮】。試合が始まってから数える。</summary>
        public int BankMoveIntervalTicks = 2400;

        /// <summary>拠点が移る何ティック前から、次の地点を予告するか（10 秒）【仮】。表示だけに使う。</summary>
        public int BankNoticeTicks = 600;

        /// <summary>拠点の円の半径（m）【仮】。キャラクターの箱が触れていれば、所持コインを確定する（2m から少し小さくした。ユーザーの要望、2026-10-14）。</summary>
        public float BankRadius = 1.7f;

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

        /// <summary>
        /// ティック tick（それまでに進めたティック数）からの1ティックが、試合のどの段階か（アディショナルタイムを考えない、時間だけの
        /// 段階。アディショナルタイムを含めた段階は MatchFlow.PhaseAt）。
        /// </summary>
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
            v.NonNegative(nameof(JackpotNoticeTicks), JackpotNoticeTicks);
            v.InRange(nameof(HitDropPercent), HitDropPercent, 0f, 100f);
            v.NonNegative(nameof(HitDropMax), HitDropMax);
            v.InRange(nameof(KoDropPercent), KoDropPercent, 0f, 100f);
            v.NonNegative(nameof(DropPickableDelayTicks), DropPickableDelayTicks);
            v.Positive(nameof(DropLifetimeTicks), DropLifetimeTicks);
            v.NonNegative(nameof(OvertimeTicks), OvertimeTicks);
            v.NonNegative(nameof(LateGameTicks), LateGameTicks);
            v.Positive(nameof(LateGameSpawnIntervalTicks), LateGameSpawnIntervalTicks);
            v.NonNegative(nameof(KoDropInset), KoDropInset);
            v.InRange(nameof(DamageRewardPercent), DamageRewardPercent, 0f, 1000f);
            v.NonNegative(nameof(KoRewardBase), KoRewardBase);
            v.NonNegative(nameof(KoRewardPerRank), KoRewardPerRank);
            v.NonNegative(nameof(KoRewardMaxBonus), KoRewardMaxBonus);
            v.Positive(nameof(BankMoveIntervalTicks), BankMoveIntervalTicks);
            v.NonNegative(nameof(BankNoticeTicks), BankNoticeTicks);
            v.Positive(nameof(BankRadius), BankRadius);
            return v;
        }
    }
}
