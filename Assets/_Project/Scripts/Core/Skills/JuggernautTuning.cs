using System;
using Airerage.Core.Combat;
using Airerage.Core.Common;

namespace Airerage.Core.Skills
{
    /// <summary>
    /// JUGGERNAUT の技の値（ユーザーの案、2026-10-29。docs/14 1.7。決まりは JuggernautRules。値は、ユーザーが決めたもののほかは【仮】）。
    /// - 空中横 CANNON SHOT: 前へ、重力を受けない砲弾を撃つ（威力 10。ユーザーの案）。最初に当たった人に当たって消える。
    /// - Q FLAK: 地上で構え、構えている間に Q を押すと、重力を受ける砲弾を照準の方向へ撃つ。キャラクター・地形・物に触れると爆発する
    ///   （威力 30・強い吹き飛び。ユーザーの案）。弾道（撃ち出す速さ）は弱・中・強・弩の 4 段階で、初めは強。構えの間に上下で選び、
    ///   その試合の間は覚えておく。3 回まで続けて撃て、1 回分は 4 秒で戻る。
    /// - E MINE TRAP: 照準の方向へ地雷を撃つ。地形かキャラクターに張り付き（ユーザーの回答、2026-10-29）、もう一度 E で爆発する（威力 30）。
    ///   クールタイム 10 秒は、爆発した（消えた）後から数える。
    /// - R MAYDAY: ジェット機に吊られて飛び去り、ステージ全体から空いた場所を選ぶと、そこへ運ばれる。その間は無敵で、選ぶ時間は 10 秒
    ///   （選ばなければ元の場所へ戻る。ユーザーの回答、2026-10-29）。
    /// </summary>
    [Serializable]
    public sealed class JuggernautTuning
    {
        /// <summary>空中横の砲弾の速さ（m/s）、当たりの半径（m）、届く距離（m）、出す位置（中心から前と上。m）。</summary>
        public float CannonSpeed = 24f;
        public float CannonRadius = 0.35f;
        public float CannonRange = 30f;
        public float CannonOffsetX = 1.6f;
        public float CannonOffsetY = 0.35f;

        /// <summary>空中横の砲弾が当たったときの攻撃（威力 10。ユーザーの案）。</summary>
        public AttackHit CannonHit = new AttackHit
        {
            Damage = 10, Gauge = 80, HitstopTicks = 10, HitstunTicks = 24, KnockbackX = 11f, KnockbackY = 6f,
        };

        /// <summary>
        /// フラックの弾道の強さ（撃ち出す速さ。m/s。弱・中・強・弩の順）と、初めの段階（強 = 2）。
        /// </summary>
        public float[] FlakSpeeds = { 14f, 20f, 26f, 33f };
        public int FlakDefaultLevel = 2;

        /// <summary>フラックの砲弾にかかる重力（m/s²）、当たりの半径（m）、爆発の半径（m）、消えるまで（ティック）。</summary>
        public float FlakGravity = 22f;
        public float FlakRadius = 0.35f;
        public float FlakExplosionRadius = 2f;
        public int FlakLifetimeTicks = 600;

        /// <summary>フラックの砲弾を出す位置（中心から上。m）と、照準の方向へ出す距離（m）。</summary>
        public float FlakMuzzleHeight = 0.6f;
        public float FlakMuzzleDistance = 1.3f;

        /// <summary>フラックの爆発の攻撃（威力 30・強い吹き飛び。ユーザーの案）。</summary>
        public AttackHit FlakHit = new AttackHit
        {
            Damage = 30, Gauge = 100, HitstopTicks = 14, HitstunTicks = 34, KnockbackX = 15f, KnockbackY = 16f,
        };

        /// <summary>地雷の速さ（m/s）、当たりの半径（m）、張り付かずに飛んでいられる長さ（ティック）、出す距離（m）。</summary>
        public float MineSpeed = 22f;
        public float MineRadius = 0.3f;
        public int MineFlightTicks = 90;
        public float MineLaunchOffset = 1.1f;

        /// <summary>地雷の爆発の半径（m）と攻撃（威力 30。ユーザーの案）。</summary>
        public float MineExplosionRadius = 2.4f;
        public AttackHit MineHit = new AttackHit
        {
            Damage = 30, Gauge = 100, HitstopTicks = 14, HitstunTicks = 32, KnockbackX = 13f, KnockbackY = 15f,
        };

        /// <summary>爆発（フラック・地雷）を見せている長さ（ティック。当たるのは爆発したティックだけ。表示用の印）。</summary>
        public int BlastTicks = 20;

        /// <summary>
        /// メーデー: 吊られて飛び去るまで（ティック）、場所を選べる長さ（ティック。10 秒。過ぎたら元の場所へ）、運ばれて降りるまで（ティック）。
        /// </summary>
        public int MaydayLiftTicks = 50;
        public int MaydaySelectTicks = 600;
        public int MaydayCarryTicks = 60;

        /// <summary>選べる場所: ステージの範囲（撃破の線）の内側へこの幅（m）だけ離れた所まで。範囲がないステージでは中心から ±半分の大きさ。</summary>
        public float MaydayEdgeMargin = 3f;
        public float MaydayDefaultHalfWidth = 64f;
        public float MaydayDefaultHalfHeight = 40f;

        /// <summary>弾道の段階の数。</summary>
        public int FlakLevels => FlakSpeeds == null ? 0 : FlakSpeeds.Length;

        /// <summary>段階 level（0〜）の撃ち出す速さ（m/s）。</summary>
        public float FlakSpeedAt(int level)
        {
            return FlakSpeeds[Math.Max(0, Math.Min(level, FlakSpeeds.Length - 1))];
        }

        /// <summary>地雷が張り付かずに飛ぶ距離（m）。</summary>
        public float MineRange => MineSpeed * MineFlightTicks * SimTime.DeltaTime;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(JuggernautTuning));
            v.Positive(nameof(CannonSpeed), CannonSpeed);
            v.Positive(nameof(CannonRadius), CannonRadius);
            v.Positive(nameof(CannonRange), CannonRange);
            v.NotNull(nameof(CannonHit), CannonHit);
            v.NotNull(nameof(FlakSpeeds), FlakSpeeds);
            if (FlakSpeeds != null)
            {
                if (FlakSpeeds.Length == 0)
                {
                    v.Fail("フラックの弾道の段階が 1 つもありません");
                }
                for (int i = 0; i < FlakSpeeds.Length; i++)
                {
                    v.Positive($"FlakSpeeds[{i}]", FlakSpeeds[i]);
                }
                v.InRange(nameof(FlakDefaultLevel), FlakDefaultLevel, 0f, Math.Max(0, FlakSpeeds.Length - 1));
            }
            v.NonNegative(nameof(FlakGravity), FlakGravity);
            v.Positive(nameof(FlakRadius), FlakRadius);
            v.NonNegative(nameof(FlakExplosionRadius), FlakExplosionRadius);
            v.Positive(nameof(FlakLifetimeTicks), FlakLifetimeTicks);
            v.NonNegative(nameof(FlakMuzzleDistance), FlakMuzzleDistance);
            v.NotNull(nameof(FlakHit), FlakHit);
            v.Positive(nameof(MineSpeed), MineSpeed);
            v.Positive(nameof(MineRadius), MineRadius);
            v.Positive(nameof(MineFlightTicks), MineFlightTicks);
            v.NonNegative(nameof(MineLaunchOffset), MineLaunchOffset);
            v.Positive(nameof(MineExplosionRadius), MineExplosionRadius);
            v.NotNull(nameof(MineHit), MineHit);
            v.Positive(nameof(BlastTicks), BlastTicks);
            v.Positive(nameof(MaydayLiftTicks), MaydayLiftTicks);
            v.Positive(nameof(MaydaySelectTicks), MaydaySelectTicks);
            v.Positive(nameof(MaydayCarryTicks), MaydayCarryTicks);
            v.NonNegative(nameof(MaydayEdgeMargin), MaydayEdgeMargin);
            v.Positive(nameof(MaydayDefaultHalfWidth), MaydayDefaultHalfWidth);
            v.Positive(nameof(MaydayDefaultHalfHeight), MaydayDefaultHalfHeight);
            return v;
        }
    }
}
