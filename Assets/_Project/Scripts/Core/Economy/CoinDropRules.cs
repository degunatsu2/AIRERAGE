using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Match;
using Degunatsu.Core.Simulation;

namespace Degunatsu.Core.Economy
{
    /// <summary>
    /// コインのドロップ（docs/12_match_plan.md T4-6、docs/01_game_design.md 3.3）。増減はすべて CoinLedger.Drop を通す。
    /// - 被弾ドロップ: 当たって新しいコンボの最初の命中になったら（HitRecord.StartsCombo）、所持コインの MatchRules.HitDropPercent
    ///   （少なくとも 1 枚、上限 HitDropMax）を、当たった人の位置に落とす。同じコンボの間は落とさない。ガードされたら落とさない。
    ///   同じティックに同じ人が何回当たっても、落とすのは1回。
    /// - 撃破ドロップ: 撃破されたら（自滅も。D-44）、所持コインの KoDropPercent（端数は切り上げ）を落とす。落とす場所は、
    ///   ステージの外に出た位置を、ステージの範囲の KoDropInset だけ内側に入れた位置【仮】。
    /// - 落とすコインは、20・3・1 枚のコインに分ける（大きい順。見た目で価値が分かるように。docs/12 3.5）。最大 MaxPieces 個で、
    ///   最後の1つに残りをまとめる。落とした位置の上の扇形に、決まった間隔で並べる（三角関数を使わず、決まった値の表で。PC 間で同じにする）。
    /// - 落としたコインは DropPickableDelayTicks の間だれも拾えず、DropLifetimeTicks で消える。
    /// 当たった順、撃破の順（番号の順）に落とす（結果が順番で変わらない）。
    /// </summary>
    public static class CoinDropRules
    {
        /// <summary>1回のドロップで出すコインの数の上限。</summary>
        public const int MaxPieces = 16;

        // 落とした位置からの、コインの並べ方（上の扇形。半径 1.4m、2.3m、3.2m）。
        private static readonly Vector2[] Offsets =
        {
            new Vector2(0f, 1.4f),
            new Vector2(0.7f, 1.212f),
            new Vector2(-0.7f, 1.212f),
            new Vector2(1.212f, 0.7f),
            new Vector2(-1.212f, 0.7f),
            new Vector2(0.595f, 2.222f),
            new Vector2(-0.595f, 2.222f),
            new Vector2(1.626f, 1.626f),
            new Vector2(-1.626f, 1.626f),
            new Vector2(2.222f, 0.595f),
            new Vector2(-2.222f, 0.595f),
            new Vector2(0f, 3.2f),
            new Vector2(1.6f, 2.771f),
            new Vector2(-1.6f, 2.771f),
            new Vector2(2.771f, 1.6f),
            new Vector2(-2.771f, 1.6f),
        };

        /// <summary>被弾ドロップで落とす枚数（所持 held 枚のとき）。</summary>
        public static int HitDropAmount(MatchRules rules, int held)
        {
            if (held <= 0 || rules.HitDropPercent == 0 || rules.HitDropMax == 0)
            {
                return 0;
            }
            int amount = Math.Min(rules.HitDropMax, Math.Max(1, held * rules.HitDropPercent / 100));
            return Math.Min(amount, held);
        }

        /// <summary>撃破ドロップで落とす枚数（所持 held 枚のとき。端数は切り上げ）。</summary>
        public static int KoDropAmount(MatchRules rules, int held)
        {
            if (held <= 0)
            {
                return 0;
            }
            return Math.Min(held, (held * rules.KoDropPercent + 99) / 100);
        }

        /// <summary>amount 枚を分けたときの、piece 番目のコインの枚数（20・3・1 の大きい順。最後の1つに残りをまとめる）。</summary>
        public static int PieceValue(int remaining, int piece)
        {
            if (piece == MaxPieces - 1)
            {
                return remaining;
            }
            return remaining >= 20 ? 20 : remaining >= 3 ? 3 : 1;
        }

        /// <summary>被弾ドロップ。HitResolver の後、撃破を決める前に呼ぶ（当たった人がまだステージの中にいるうちに）。</summary>
        /// <param name="tick">このティック（進めた後のティック）。</param>
        public static void DropOnHits(CoinLedger ledger, List<CharacterState> states, IReadOnlyList<HitRecord> hits, MatchRules rules, int tick)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                HitRecord hit = hits[i];
                if (hit.Result != HitResult.Hit || !hit.StartsCombo || AlreadyDropped(hits, i))
                {
                    continue;
                }
                CharacterState victim = states[hit.Victim];
                int amount = HitDropAmount(rules, victim.Coins.Held);
                Drop(ledger, states, hit.Victim, CoinEventKind.HitDrop, amount, new Vector2(victim.Position.X, victim.Position.Y), rules, tick);
            }
        }

        /// <summary>撃破ドロップ。KnockoutResolver の後に呼ぶ（撃破された人は出現位置にいるが、所持コインはまだ持っている）。</summary>
        public static void DropOnKnockouts(CoinLedger ledger, List<CharacterState> states, IReadOnlyList<KnockoutRecord> knockouts,
            StageBounds bounds, MatchRules rules, int tick)
        {
            foreach (KnockoutRecord ko in knockouts)
            {
                int amount = KoDropAmount(rules, states[ko.Victim].Coins.Held);
                Drop(ledger, states, ko.Victim, CoinEventKind.KoDrop, amount, Inside(ko.Position, bounds, rules.KoDropInset), rules, tick);
            }
        }

        /// <summary>ステージの外に出た位置を、範囲の inset だけ内側に入れた位置（範囲が狭ければ中心）。</summary>
        public static Vector2 Inside(Vector2 position, StageBounds bounds, float inset)
        {
            return new Vector2(
                ClampInto(position.X, bounds.MinX + inset, bounds.MaxX - inset),
                ClampInto(position.Y, bounds.MinY + inset, bounds.MaxY - inset));
        }

        private static float ClampInto(float value, float min, float max)
        {
            if (min > max)
            {
                return (min + max) * 0.5f;
            }
            return value < min ? min : value > max ? max : value;
        }

        // 同じティックに、同じ人への被弾ドロップがもう出たか。
        private static bool AlreadyDropped(IReadOnlyList<HitRecord> hits, int index)
        {
            for (int j = 0; j < index; j++)
            {
                if (hits[j].Victim == hits[index].Victim && hits[j].Result == HitResult.Hit && hits[j].StartsCombo)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Drop(CoinLedger ledger, List<CharacterState> states, int character, CoinEventKind kind, int amount,
            Vector2 origin, MatchRules rules, int tick)
        {
            int remaining = amount;
            for (int piece = 0; remaining > 0 && piece < MaxPieces; piece++)
            {
                int value = PieceValue(remaining, piece);
                int coinId = ledger.Drop(new CoinEventId(tick, kind, character, piece), states, character, value, origin + Offsets[piece],
                    tick + rules.DropPickableDelayTicks, tick + rules.DropLifetimeTicks);
                if (coinId < 0)
                {
                    // 台帳が拒否した（理由は ledger.LastRejection）。残りは落とさない。
                    return;
                }
                remaining -= value;
            }
        }
    }
}
