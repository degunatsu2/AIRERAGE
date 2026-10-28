using System;
using Airerage.Core.Combat;
using Airerage.Core.Common;

namespace Airerage.Core.Skills
{
    /// <summary>
    /// THIEF の技の値（ユーザーの案、2026-10-28。docs/14 1.6。決まりは ThiefRules。値は、ユーザーが決めたもののほかは【仮】）。
    /// - 地上技 SMOKE BOMB: 真下へ煙球を投げる。煙球は着弾すると、ごく狭い範囲に威力 5・弱い吹き飛びの攻撃を当て、狭い範囲に煙を出す
    ///   （煙の中の物は見えなくなる。見た目だけ）。
    /// - Q TALENT SNATCH: 照準の方向へ、とても速い小さな弾を撃つ（威力も吹き飛びもない）。キャラクターに当たれば、その人の Q をコピーする
    ///   （相手はそのまま使える。ユーザーの回答、2026-10-28）。ステージのギミック・仕掛けに当たれば、それを奪う。もう一度 Q で、コピーした
    ///   技を使うか、奪ったギミックを照準の方向の目の前に置く（置いた瞬間に 1 回効き、その後はふつうに効く。2 つまで、3 つ目で古い方が
    ///   消える）。使うか、弾が外れて消えると、クールタイム 12 秒。
    /// - E MISDIRECTION: その場に分身を残し、自分は透明になって照準の方向へごく短く突進する。分身は直前の慣性のまま、移動とワイヤーだけを
    ///   する CPU の動き。透明と分身は 3 秒たつか、R 以外の行動をするか、もう一度 E で解ける。分身は攻撃が当たると煙になって消える
    ///   （透明は続く。ユーザーの回答、2026-10-28）。クールタイム 20 秒。
    /// - R BANK HEIST: 拠点の近くでだけ出せる。その場で止まって詠唱し、もう一度 R で止めると、詠唱した長さでコインを得る（1 秒に 1 枚、
    ///   5 秒ごとに 20 枚、10 秒ごとにさらに 20 枚）。拠点が移ったら、その時点で止めて得る。攻撃を受けてのけぞると中断し、コインは得ない
    ///   （ユーザーの回答、2026-10-28）。ゲージ 80%。
    /// </summary>
    [Serializable]
    public sealed class ThiefTuning
    {
        /// <summary>煙球を投げる速さ（真下へ。m/s）と、当たりの半径（m）と、着弾しなくても消えるまで（ティック）。</summary>
        public float SmokeBallSpeed = 26f;
        public float SmokeBallRadius = 0.3f;
        public int SmokeBallLifetimeTicks = 90;

        /// <summary>着弾したときの攻撃の範囲（半径。m。「極小範囲」）と、攻撃（ユーザーの案: 威力 5、吹き飛びは弱め）。</summary>
        public float ExplosionRadius = 0.8f;
        public AttackHit SmokeHit = new AttackHit
        {
            Damage = 5, Gauge = 70, HitstopTicks = 8, HitstunTicks = 20, KnockbackX = 4f, KnockbackY = 6f,
        };

        /// <summary>煙の半径（m。「小範囲」）と、残る長さ（ティック。5 秒）。分身が消えたときの煙は PuffRadius・PuffTicks。</summary>
        public float SmokeRadius = 2.6f;
        public int SmokeTicks = 300;
        public float PuffRadius = 1.6f;
        public int PuffTicks = 90;

        /// <summary>Q の弾の速さ（m/s。「超速」）・届く距離（m）・当たりの半径（m）・撃つ位置（体の中心から照準の方向への距離。m）。</summary>
        public float SnatchSpeed = 60f;
        public float SnatchRange = 30f;
        public float SnatchRadius = 0.3f;
        public float SnatchLaunchOffset = 0.8f;

        /// <summary>仕掛け（発射装置・ピッチャーマシン）の本体に弾が当たったとみなす半径（m）。</summary>
        public float TrapBodyRadius = 1.2f;

        /// <summary>
        /// 奪ったギミックを置く位置: 体の縁から、置く物の手前の縁までの距離（m）。大きいギミックは、縦横それぞれ PlaceMaxSize（m）までに
        /// 縮める（溶岩流などの危険は、柱 1 本の幅と高さ PlaceMaxHazardHeight まで）。仕掛けは、発射口を体の中心から TrapPlaceDistance（m）
        /// 先に置く。
        /// </summary>
        public float PlaceGap = 0.8f;
        public float PlaceMaxSize = 6f;
        public float PlaceMaxHazardHeight = 8f;
        public float TrapPlaceDistance = 2f;

        /// <summary>同時に置いておけるギミックの数（3 つ目を置くと、いちばん古いものが消える）。</summary>
        public int MaxPlaced = 2;

        /// <summary>奪ったギミックを置くときの技（Q。照準の方向の目の前に置く）。</summary>
        public AttackData PlaceAttack = new AttackData
        {
            Name = "PLACE",
            TotalTicks = 14,
            WireCancelFromTick = 4,
            Effect = SkillEffect.PlaceGimmick,
            EffectTick = 2,
            Hits = new AttackHit[0],
        };

        /// <summary>透明と分身が続く長さ（ティック。3 秒）。</summary>
        public int MisdirectionTicks = 180;

        /// <summary>分身の動き: 向きを変えるかを決める間（ティック）と、そのとき向きを変える割合（%）、ワイヤーにつないでいる長さの上限（ティック）。</summary>
        public int CloneTurnIntervalTicks = 60;
        public int CloneTurnPercent = 25;
        public int CloneWireHoldTicks = 45;

        /// <summary>R を出せる、拠点の中心からの距離（m。「バンクの近く」）。</summary>
        public float HeistRange = 6f;

        /// <summary>詠唱 1 秒あたりの枚数と、5 秒ごと・10 秒ごとに足す枚数（ユーザーの案: 1 枚、20 枚、20 枚）。</summary>
        public int HeistCoinsPerSecond = 1;
        public int HeistBonusPerFive = 20;
        public int HeistBonusPerTen = 20;

        /// <summary>詠唱した長さ ticks（ティック）で得るコインの枚数（3 秒 3 枚、5 秒 25 枚、10 秒 70 枚、15 秒 95 枚、20 秒 140 枚）。</summary>
        public int HeistCoins(int ticks)
        {
            int seconds = Math.Max(0, ticks) / SimTime.TicksPerSecond;
            return seconds * HeistCoinsPerSecond + seconds / 5 * HeistBonusPerFive + seconds / 10 * HeistBonusPerTen;
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(ThiefTuning));
            v.Positive(nameof(SmokeBallSpeed), SmokeBallSpeed);
            v.Positive(nameof(SmokeBallRadius), SmokeBallRadius);
            v.Positive(nameof(SmokeBallLifetimeTicks), SmokeBallLifetimeTicks);
            v.Positive(nameof(ExplosionRadius), ExplosionRadius);
            v.NotNull(nameof(SmokeHit), SmokeHit);
            v.Positive(nameof(SmokeRadius), SmokeRadius);
            v.Positive(nameof(SmokeTicks), SmokeTicks);
            v.Positive(nameof(PuffRadius), PuffRadius);
            v.Positive(nameof(PuffTicks), PuffTicks);
            v.Positive(nameof(SnatchSpeed), SnatchSpeed);
            v.Positive(nameof(SnatchRange), SnatchRange);
            v.Positive(nameof(SnatchRadius), SnatchRadius);
            v.NonNegative(nameof(SnatchLaunchOffset), SnatchLaunchOffset);
            v.Positive(nameof(TrapBodyRadius), TrapBodyRadius);
            v.NonNegative(nameof(PlaceGap), PlaceGap);
            v.Positive(nameof(PlaceMaxSize), PlaceMaxSize);
            v.Positive(nameof(PlaceMaxHazardHeight), PlaceMaxHazardHeight);
            v.Positive(nameof(TrapPlaceDistance), TrapPlaceDistance);
            v.InRange(nameof(MaxPlaced), MaxPlaced, 1f, 8f);
            v.NotNull(nameof(PlaceAttack), PlaceAttack);
            if (PlaceAttack != null)
            {
                v.Merge(PlaceAttack.Validate(nameof(PlaceAttack)));
            }
            v.Positive(nameof(MisdirectionTicks), MisdirectionTicks);
            v.Positive(nameof(CloneTurnIntervalTicks), CloneTurnIntervalTicks);
            v.InRange(nameof(CloneTurnPercent), CloneTurnPercent, 0f, 100f);
            v.Positive(nameof(CloneWireHoldTicks), CloneWireHoldTicks);
            v.Positive(nameof(HeistRange), HeistRange);
            v.NonNegative(nameof(HeistCoinsPerSecond), HeistCoinsPerSecond);
            v.NonNegative(nameof(HeistBonusPerFive), HeistBonusPerFive);
            v.NonNegative(nameof(HeistBonusPerTen), HeistBonusPerTen);
            return v;
        }
    }
}
