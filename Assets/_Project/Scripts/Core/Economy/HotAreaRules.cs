using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;

namespace Airerage.Core.Economy
{
    /// <summary>
    /// ホットエリア（ユーザーの要望、2026-11-02。終盤にコインが増える COIN RUSH の代わり）。
    /// - 時間の制限があるとき、試合が始まってから MatchRules.HotAreaIntervalTicks（30 秒）ごとに 1 回出す（1 回目が 30 秒の時点）。
    ///   3 分の試合なら 0:30・1:00・1:30・2:00・2:30 の 5 回で、2:00（残り 1 分）は 100 枚のコインと同じ時刻（別の地点）。
    /// - 場所は 100 枚のコインの候補の地点（CoinSpawnKind.Jackpot）から、試合の種と回で選ぶ。前の回と同じ地点と、100 枚のコインの予告から
    ///   後に重なる回では 100 枚のコインの地点を避ける（候補が足りなければ、避けるのをやめる）。
    /// - 中心にフルーツ（触れた人のゲージが MatchRules.HotAreaFruitGauge 回復する。コインではないので台帳を通さない）、周りの 2 つの輪に
    ///   MatchRules.HotAreaCoins 個の通常のコイン（台帳の出現。寿命あり）。コインの位置がステージの範囲（撃破の線）の BoundsInset より外なら
    ///   内側まで、地形に埋まるなら埋まらない所まで、中心への線の上で寄せる（床や壁の向こう側へは抜けない。CoinPosition）。
    /// - 予告（MatchRules.HotAreaNoticeTicks。10 秒前）は表示だけ。残る長さは MatchRules.HotAreaLifetimeTicks（30 秒）。
    /// 回・時刻・場所はティック数とルールだけから決まる。フルーツを取ったかだけを MatchState.HotFruitTaken に持つ（巻き戻しに乗る）。
    /// ティックの数え方は FieldCoinRules と同じ: SpawnTick(r) からのステップで出て、状態のティックが SpawnTick(r) + 1 〜
    /// SpawnTick(r) + HotAreaLifetimeTicks の間ある。
    /// </summary>
    public static class HotAreaRules
    {
        // 場所を選ぶ乱数の、ほかの選択と分けるための数。
        private const int SpotSalt = 300;

        // 地形に埋まらないかを調べるコインの箱の半分の大きさ（m）。
        private static readonly Vector2 CoinHalf = new Vector2(0.3f, 0.3f);

        /// <summary>ホットエリアのコインを、ステージの範囲（撃破の線）からこれだけ内側に置く（m）。</summary>
        public const float BoundsInset = 1f;

        /// <summary>回 round（1 から）のホットエリアが出るティック（このティックからのステップで出る）。出ない回なら -1。</summary>
        public static int SpawnTick(MatchRules rules, int round)
        {
            if (!rules.HasTimeLimit || rules.HotAreaIntervalTicks <= 0 || round < 1)
            {
                return -1;
            }
            long offset = (long)round * rules.HotAreaIntervalTicks;
            return offset < rules.DurationTicks ? rules.CountdownTicks + (int)offset : -1;
        }

        /// <summary>1 試合に出るホットエリアの回数。</summary>
        public static int RoundCount(MatchRules rules)
        {
            if (!rules.HasTimeLimit || rules.HotAreaIntervalTicks <= 0)
            {
                return 0;
            }
            return (rules.DurationTicks - 1) / rules.HotAreaIntervalTicks;
        }

        /// <summary>回 round の場所を予告し始めるティック（出ない回なら -1）。</summary>
        public static int AnnounceTick(MatchRules rules, int round)
        {
            int spawn = SpawnTick(rules, round);
            return spawn < 0 ? -1 : Math.Max(rules.CountdownTicks, spawn - rules.HotAreaNoticeTicks);
        }

        /// <summary>状態のティック tick に出ているホットエリアの回（なければ 0）。</summary>
        public static int ActiveRound(MatchRules rules, int tick)
        {
            if (!rules.HasTimeLimit || rules.HotAreaIntervalTicks <= 0 || tick <= rules.CountdownTicks)
            {
                return 0;
            }
            // 出てから HotAreaLifetimeTicks（間隔以下）の間だけあるので、候補は直前に出た回だけ。
            int round = (tick - 1 - rules.CountdownTicks) / rules.HotAreaIntervalTicks;
            int spawn = SpawnTick(rules, round);
            return spawn >= 0 && tick > spawn && tick <= spawn + rules.HotAreaLifetimeTicks ? round : 0;
        }

        /// <summary>
        /// 状態のティック tick に予告している（場所を見せて、まだ出ていない）回（なければ 0）。ticksUntil は出るまで（JACKPOT の札と同じ数え方。
        /// 出るステップの状態のティックまで）。
        /// </summary>
        public static int AnnouncedRound(MatchRules rules, int tick, out int ticksUntil)
        {
            ticksUntil = 0;
            if (!rules.HasTimeLimit || rules.HotAreaIntervalTicks <= 0 || tick < rules.CountdownTicks)
            {
                return 0;
            }
            // 次に出る回（今のティックが出るティックちょうどなら、その回）。
            int round = (tick - rules.CountdownTicks) / rules.HotAreaIntervalTicks;
            if (SpawnTick(rules, round) != tick)
            {
                round++;
            }
            int spawn = SpawnTick(rules, round);
            if (spawn < 0 || tick < AnnounceTick(rules, round))
            {
                return 0;
            }
            ticksUntil = spawn + 1 - tick;
            return round;
        }

        /// <summary>状態のティック tick に、回 round のフルーツが出ているか（取られていない）。</summary>
        public static bool FruitPresent(MatchRules rules, in MatchState match, int tick, out int round)
        {
            round = ActiveRound(rules, tick);
            return round > 0 && match.HotFruitTaken != round;
        }

        /// <summary>回 round のホットエリアの地点の番号（spawns の中の番号。候補がない・出ない回なら -1）。</summary>
        public static int Spot(MatchRules rules, IReadOnlyList<CoinSpawnPoint> spawns, int round)
        {
            if (spawns == null || SpawnTick(rules, round) < 0 || CandidateCount(spawns) == 0)
            {
                return -1;
            }
            int jackpot = FieldCoinRules.JackpotSpot(rules, spawns);
            int jackpotAnnounce = FieldCoinRules.JackpotAnnounceTick(rules);
            int previous = -1;
            int spot = -1;
            for (int r = 1; r <= round; r++)
            {
                // 100 枚のコインの予告から後に残る回は、100 枚のコインの地点を避ける（別々の場所にする）。
                int spawn = SpawnTick(rules, r);
                int avoidJackpot = jackpot >= 0 && jackpotAnnounce >= 0 && spawn + rules.HotAreaLifetimeTicks > jackpotAnnounce ? jackpot : -1;
                spot = Choose(spawns, rules.Seed, r, previous, avoidJackpot);
                if (spot < 0)
                {
                    spot = Choose(spawns, rules.Seed, r, -1, avoidJackpot);
                }
                if (spot < 0)
                {
                    spot = Choose(spawns, rules.Seed, r, -1, -1);
                }
                previous = spot;
            }
            return spot;
        }

        /// <summary>
        /// ホットエリアのコイン index（0 から MatchRules.HotAreaCoins - 1）を置く、中心からの位置（地形でずらす前）。内側の輪に 2/5、
        /// 外側の輪に残りを、真上から等しい間隔で並べる（外側は内側の間に来るよう半分ずらす）。
        /// </summary>
        public static Vector2 CoinOffset(MatchRules rules, int index)
        {
            int count = Math.Max(1, rules.HotAreaCoins);
            int inner = count * 2 / 5;
            bool onInner = index < inner;
            int ringCount = onInner ? inner : count - inner;
            int slot = onInner ? index : index - inner;
            float radius = onInner ? rules.HotAreaInnerRadius : rules.HotAreaOuterRadius;
            float angle = (slot + (onInner ? 0f : 0.5f)) / ringCount * MathF.PI * 2f;
            return new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * radius;
        }

        /// <summary>
        /// 全員を進めてコインを拾った後に呼ぶ（tick は進めた後のティック）。playing（試合中）なら、出る回のフルーツとコインを出す。
        /// フルーツに触れた人がいれば（いちばん近い人。同じなら番号の小さい人）、ゲージを回復して、取った回を match に書く。
        /// </summary>
        public static void Step(CoinLedger ledger, ref MatchState match, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            IReadOnlyList<CoinSpawnPoint> spawns, MatchRules rules, ICollisionQuery collision, StageBounds? bounds, int tick, bool playing)
        {
            if (spawns == null)
            {
                return;
            }
            if (playing)
            {
                SpawnCoins(ledger, spawns, rules, collision, bounds, tick);
            }
            if (!FruitPresent(rules, match, tick, out int round))
            {
                return;
            }
            int spot = Spot(rules, spawns, round);
            if (spot < 0)
            {
                return;
            }
            int picker = FieldCoinRules.ClosestToucher(spawns[spot].Position, states, configs, rules.CoinPickupRadius);
            if (picker < 0)
            {
                return;
            }
            CharacterState state = states[picker];
            CombatTuning.AddGauge(ref state.Combat, rules.HotAreaFruitGauge, configs[picker].Skills.CoinBoost);
            states[picker] = state;
            match.HotFruitTaken = round;
        }

        private static void SpawnCoins(CoinLedger ledger, IReadOnlyList<CoinSpawnPoint> spawns, MatchRules rules, ICollisionQuery collision,
            StageBounds? bounds, int tick)
        {
            // tick は進めた後のティックなので、このステップは tick - 1 から。
            int round = (tick - 1 - rules.CountdownTicks) / Math.Max(1, rules.HotAreaIntervalTicks);
            if (tick - 1 < rules.CountdownTicks || SpawnTick(rules, round) != tick - 1)
            {
                return;
            }
            int spot = Spot(rules, spawns, round);
            if (spot < 0)
            {
                return;
            }
            Vector2 center = spawns[spot].Position;
            for (int i = 0; i < rules.HotAreaCoins; i++)
            {
                Vector2 position = CoinPosition(rules, collision, bounds, center, i);
                int coinId = ledger.Spawn(new CoinEventId(tick, CoinEventKind.HotArea, round, i), position, rules.HotAreaCoinValue, tick,
                    tick + rules.HotAreaLifetimeTicks);
                if (coinId < 0)
                {
                    // 台帳が拒否した（理由は ledger.LastRejection）。残りは出さない。
                    return;
                }
            }
        }

        /// <summary>
        /// 中心 center のホットエリアの、コイン index を置く位置: 輪の上の位置（CoinOffset）を、ステージの範囲の BoundsInset より内側に、
        /// 地形に埋まらない所に、中心への線の上で寄せる。collision と bounds は、なければ調べない。
        /// </summary>
        public static Vector2 CoinPosition(MatchRules rules, ICollisionQuery collision, StageBounds? bounds, Vector2 center, int index)
        {
            Vector2 offset = CoinOffset(rules, index);
            if (bounds.HasValue)
            {
                offset *= InsideFraction(bounds.Value, center, offset);
            }
            Vector2 position = center + offset;
            return collision != null ? PullOutOfTerrain(collision, center, position) : position;
        }

        // center + offset × t が範囲の BoundsInset より内側になる、いちばん大きい t（0〜1）。
        private static float InsideFraction(StageBounds bounds, Vector2 center, Vector2 offset)
        {
            float t = 1f;
            t = MathF.Min(t, Limit(center.X, offset.X, bounds.MinX + BoundsInset, bounds.MaxX - BoundsInset));
            t = MathF.Min(t, Limit(center.Y, offset.Y, bounds.MinY + BoundsInset, bounds.MaxY - BoundsInset));
            return MathF.Max(0f, t);
        }

        private static float Limit(float start, float delta, float min, float max)
        {
            if (delta > 0f && start + delta > max)
            {
                return (max - start) / delta;
            }
            if (delta < 0f && start + delta < min)
            {
                return (min - start) / delta;
            }
            return 1f;
        }

        // position が地形に埋まるなら、中心 center への線の上で、埋まらない所のうち position にいちばん近い所へ寄せる（床や壁の向こう側へは
        // 抜けない。中心も埋まっていれば、そのまま）。
        private static Vector2 PullOutOfTerrain(ICollisionQuery collision, Vector2 center, Vector2 position)
        {
            if (!PlacementRules.Overlaps(collision, position, CoinHalf) || PlacementRules.Overlaps(collision, center, CoinHalf))
            {
                return position;
            }
            const int Steps = 20;
            for (int k = 1; k <= Steps; k++)
            {
                float free = 1f - k / (float)Steps;
                if (PlacementRules.Overlaps(collision, Vector2.Lerp(center, position, free), CoinHalf))
                {
                    continue;
                }
                // free（埋まらない）と、その 1 つ外（埋まる）の間で境目まで詰める。
                float blocked = free + 1f / Steps;
                for (int i = 0; i < 8; i++)
                {
                    float middle = (free + blocked) * 0.5f;
                    if (PlacementRules.Overlaps(collision, Vector2.Lerp(center, position, middle), CoinHalf))
                    {
                        blocked = middle;
                    }
                    else
                    {
                        free = middle;
                    }
                }
                return Vector2.Lerp(center, position, free);
            }
            return center;
        }

        private static int CandidateCount(IReadOnlyList<CoinSpawnPoint> spawns)
        {
            int count = 0;
            for (int i = 0; i < spawns.Count; i++)
            {
                count += spawns[i].Kind == CoinSpawnKind.Jackpot ? 1 : 0;
            }
            return count;
        }

        // 候補の地点のうち、avoidA と avoidB を除いた中から、種と回で 1 つ選ぶ（残らなければ -1）。
        private static int Choose(IReadOnlyList<CoinSpawnPoint> spawns, int seed, int round, int avoidA, int avoidB)
        {
            int allowed = 0;
            for (int i = 0; i < spawns.Count; i++)
            {
                allowed += Allowed(spawns, i, avoidA, avoidB) ? 1 : 0;
            }
            if (allowed == 0)
            {
                return -1;
            }
            int chosen = StableHash.Range(seed, SpotSalt, round, allowed);
            for (int i = 0; i < spawns.Count; i++)
            {
                if (Allowed(spawns, i, avoidA, avoidB) && chosen-- == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool Allowed(IReadOnlyList<CoinSpawnPoint> spawns, int index, int avoidA, int avoidB)
        {
            return spawns[index].Kind == CoinSpawnKind.Jackpot && index != avoidA && index != avoidB;
        }
    }
}
