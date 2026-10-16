using System;
using Airerage.Core.Common;
using Airerage.Core.Skills;

namespace Airerage.Core.Combat
{
    /// <summary>
    /// 攻撃の判定1つ（docs/01_game_design.md 6.4）。攻撃を始めてから StartTick ティック目から ActiveTicks ティックの間だけ出る、
    /// 軸平行の箱（D-37）。1つの攻撃に複数持てる（二段技）。同じ判定は同じ相手に1回だけ当たる。
    /// 位置と吹き飛びの X は、攻撃した側の向いている方向を正とする。
    /// </summary>
    [Serializable]
    public sealed class AttackHit
    {
        /// <summary>判定が出始めるティック（攻撃を始めたティックが 0）。</summary>
        public int StartTick = 5;

        /// <summary>判定が出ている長さ（ティック）。</summary>
        public int ActiveTicks = 3;

        /// <summary>判定の箱の、キャラクターの中心からの位置（m）。</summary>
        public float OffsetX = 0.9f;
        public float OffsetY;

        /// <summary>判定の箱の半分の大きさ（m）。</summary>
        public float HalfWidth = 0.6f;
        public float HalfHeight = 0.6f;

        /// <summary>ダメージ。HP はなく（D-23）、コインへの変換（Phase 4）とコンボの表示に使う。</summary>
        public int Damage = 5;

        /// <summary>ヒットストップ（ティック）。当てた側と当たった側の両方が止まる。</summary>
        public int HitstopTicks = 6;

        /// <summary>のけぞり（ティック）。当たった側が入力を受け付けない。ヒットストップの後から数える。</summary>
        public int HitstunTicks = 20;

        /// <summary>当たった側の吹き飛びの速度（m/s）。撃破技なら、打つ直前の速さで倍率がかかる（D-38）。</summary>
        public float KnockbackX = 8f;
        public float KnockbackY = 6f;

        public bool IsActiveAt(int ticks)
        {
            return ticks >= StartTick && ticks < StartTick + ActiveTicks;
        }

        public TuningValidator Validate(string name, int totalTicks)
        {
            var v = new TuningValidator(name);
            v.NonNegative(nameof(StartTick), StartTick);
            v.Positive(nameof(ActiveTicks), ActiveTicks);
            if (StartTick + ActiveTicks > totalTicks)
            {
                v.Fail($"判定（{StartTick}〜{StartTick + ActiveTicks} ティック）が攻撃の長さ（{totalTicks}）を超えています");
            }
            v.Positive(nameof(HalfWidth), HalfWidth);
            v.Positive(nameof(HalfHeight), HalfHeight);
            v.NonNegative(nameof(Damage), Damage);
            v.NonNegative(nameof(HitstopTicks), HitstopTicks);
            v.NonNegative(nameof(HitstunTicks), HitstunTicks);
            return v;
        }
    }

    /// <summary>
    /// 攻撃中の動き1つ。攻撃を始めてから StartTick ティック目から Ticks ティックの間、速度をこの値にする（踏み込み、飛び出し、
    /// 飛び込み）。決めていない間は慣性のまま動く。X は向いている方向を正とする。
    /// </summary>
    [Serializable]
    public sealed class AttackMotion
    {
        public int StartTick;
        public int Ticks = 1;
        public float VelocityX;
        public float VelocityY;

        /// <summary>この攻撃が相手に当たっているときだけ動く（当たったら飛び出す、など。RISK TAKER のオールイン）。</summary>
        public bool RequiresHit;

        public bool IsActiveAt(int ticks)
        {
            return ticks >= StartTick && ticks < StartTick + Ticks;
        }

        public TuningValidator Validate(string name, int totalTicks)
        {
            var v = new TuningValidator(name);
            v.NonNegative(nameof(StartTick), StartTick);
            v.Positive(nameof(Ticks), Ticks);
            if (StartTick + Ticks > totalTicks)
            {
                v.Fail($"動き（{StartTick}〜{StartTick + Ticks} ティック）が攻撃の長さ（{totalTicks}）を超えています");
            }
            return v;
        }
    }

    /// <summary>
    /// 1つの攻撃のデータ（docs/01_game_design.md 6.4）。全体の長さ、判定（1〜4つ）、攻撃中の動き。
    /// 段階（表示用）は判定から決まる: 最初の判定の前が発生、判定が出ている間が持続、それ以外が硬直。
    /// </summary>
    [Serializable]
    public sealed class AttackData
    {
        /// <summary>1つの攻撃が持てる判定の数の上限（当てた相手の記録のビット数による）。</summary>
        public const int MaxHits = 4;

        /// <summary>技の名前（表示用。特殊攻撃と必殺技の表示に使う。空なら表示しない）。</summary>
        public string Name = string.Empty;

        /// <summary>技の効果（物を置く、投射物を撃つ、強化など。docs/13 T5-6）。None なら効果はない。</summary>
        public SkillEffect Effect;

        /// <summary>効果を起こすティック（攻撃を始めたティックが 0）。</summary>
        public int EffectTick;

        /// <summary>
        /// 0 より大きければ、ボタンを押している間は技を出さずにためて（動ける）、離したときに出す（RISK TAKER の E。最大のティック数）。
        /// ためた長さは ActionState.Charge に入る。
        /// </summary>
        public int HoldMaxTicks;

        /// <summary>
        /// 吹き飛びと威力の倍率を、当てたときにルーレットで決める（0.5〜2 倍のどれか。試合の種と攻撃の番号で決めるので全員で同じ。
        /// 最大なら JACKPOT。RISK TAKER のルーレットスマッシュ）。
        /// </summary>
        public bool Roulette;

        /// <summary>攻撃の全体の長さ（ティック）。終わったティックから次の行動ができる。</summary>
        public int TotalTicks = 18;

        /// <summary>
        /// ワイヤーでキャンセルできるようになるティック（攻撃を始めたティックが 0）。攻撃の終わりまでできる（D-20）。
        /// 既定は最初の判定が出るティック（持続の開始から硬直の終わりまで。発生中にできるとフェイントとして強すぎるため）。
        /// </summary>
        public int WireCancelFromTick = 5;

        public AttackHit[] Hits = { new AttackHit() };
        public AttackMotion[] Motions = new AttackMotion[0];

        /// <summary>最初の判定が出るティック（発生）。</summary>
        public int FirstHitTick
        {
            get
            {
                int first = int.MaxValue;
                foreach (AttackHit hit in Hits)
                {
                    first = Math.Min(first, hit.StartTick);
                }
                return first;
            }
        }

        /// <summary>ルーレットの倍率（Roulette。順に 0.5〜2 倍。最後が JACKPOT）。</summary>
        public static readonly float[] RouletteScales = { 0.5f, 0.8f, 1f, 1.2f, 1.5f, 2f };

        /// <summary>攻撃を始めてから ticks ティック目の段階。</summary>
        public AttackPhase PhaseAt(int ticks)
        {
            foreach (AttackHit hit in Hits)
            {
                if (hit.IsActiveAt(ticks))
                {
                    return AttackPhase.Active;
                }
            }
            return ticks < FirstHitTick ? AttackPhase.Startup : AttackPhase.Recovery;
        }

        public TuningValidator Validate(string name)
        {
            var v = new TuningValidator(name);
            v.Positive(nameof(TotalTicks), TotalTicks);
            v.InRange(nameof(WireCancelFromTick), WireCancelFromTick, 0f, TotalTicks);
            v.NotNull(nameof(Hits), Hits);
            v.NotNull(nameof(Motions), Motions);
            v.InRange(nameof(EffectTick), EffectTick, 0f, TotalTicks - 1);
            v.NonNegative(nameof(HoldMaxTicks), HoldMaxTicks);
            if (Hits != null)
            {
                // 効果のある技（物を置くなど）は、判定がなくてもよい。
                int minHits = Effect != SkillEffect.None ? 0 : 1;
                if (Hits.Length < minHits || Hits.Length > MaxHits)
                {
                    v.Fail($"判定は {minHits}〜{MaxHits} つ（{Hits.Length} つ）");
                }
                for (int i = 0; i < Hits.Length; i++)
                {
                    v.NotNull($"Hits[{i}]", Hits[i]);
                    if (Hits[i] != null)
                    {
                        v.Merge(Hits[i].Validate($"{name}.Hits[{i}]", TotalTicks));
                    }
                }
            }
            if (Motions != null)
            {
                for (int i = 0; i < Motions.Length; i++)
                {
                    v.NotNull($"Motions[{i}]", Motions[i]);
                    if (Motions[i] != null)
                    {
                        v.Merge(Motions[i].Validate($"{name}.Motions[{i}]", TotalTicks));
                    }
                }
            }
            return v;
        }
    }

    /// <summary>
    /// 戦闘の調整値（攻撃のセット）。初期値はすべて【仮】（docs/11_combat_plan.md）。
    /// 地上技1つと、空中のニュートラル／横／上／下の4つ（D-35）。そのうち1つが撃破技（D-38）。
    /// ほかに特殊攻撃 2 つ（それぞれのクールタイム）と必殺技（ゲージを消費）を持つ（D-53、D-55。docs/13 T5-2、T5-3）。
    /// 友達との対戦の結果（ワイヤーで動きながらだと当てにくい。ユーザーの要望、2026-10-13）で、判定の箱を縦横とも約 2.5 倍に
    /// （体の近くも含むように、キャラクターの側の端はおおむね残して前へ広げた）、ヒットストップを 2 倍にした（最初の攻撃から
    /// コンボへ行きやすいように）。
    /// </summary>
    [Serializable]
    public sealed class CombatTuning
    {
        /// <summary>
        /// 地上技（デフォルトのキャラクター）: 前へ大きく踏み込みながら1段目、斜め上へ飛び出しながら2段目の二段技。
        /// 最後に地面を離れて空中に移る（キャラクターを作るときの方針。D-35）。1段目は踏み込みに追い越されないように前へ飛ばし、
        /// 2段目は飛び出しとほぼ同じ速度で飛ばす（空中技につなげやすいように。相手との距離 0〜6m で2段とも当たる）。
        /// </summary>
        public AttackData Ground = new AttackData
        {
            TotalTicks = 28,
            WireCancelFromTick = 5,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 5, ActiveTicks = 8, OffsetX = 1.6f, HalfWidth = 1.75f, HalfHeight = 1.75f,
                    Damage = 4, HitstopTicks = 10, HitstunTicks = 20, KnockbackX = 13f, KnockbackY = 5f,
                },
                new AttackHit
                {
                    StartTick = 13, ActiveTicks = 5, OffsetX = 1.6f, OffsetY = 1.5f, HalfWidth = 2f, HalfHeight = 2f,
                    Damage = 4, HitstopTicks = 12, HitstunTicks = 24, KnockbackX = 10.5f, KnockbackY = 13.5f,
                },
            },
            Motions = new[]
            {
                new AttackMotion { StartTick = 5, Ticks = 8, VelocityX = 18f },
                new AttackMotion { StartTick = 13, Ticks = 1, VelocityX = 10.5f, VelocityY = 12.5f },
            },
        };

        /// <summary>空中・ニュートラル: 体の周りを広く。軽く浮かせる（コンボの始動）。</summary>
        public AttackData AirNeutral = new AttackData
        {
            TotalTicks = 19,
            WireCancelFromTick = 5,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 5, ActiveTicks = 4, OffsetX = 0f, HalfWidth = 2.75f, HalfHeight = 2.75f,
                    Damage = 4, HitstopTicks = 10, HitstunTicks = 20, KnockbackX = 5f, KnockbackY = 7f,
                },
            },
        };

        /// <summary>空中・横: 前へ長く。横へ大きく飛ばす（横からステージの外へ）。</summary>
        public AttackData AirSide = new AttackData
        {
            TotalTicks = 25,
            WireCancelFromTick = 7,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 7, ActiveTicks = 4, OffsetX = 2f, OffsetY = 0.1f, HalfWidth = 2f, HalfHeight = 1.25f,
                    Damage = 6, HitstopTicks = 14, HitstunTicks = 24, KnockbackX = 14f, KnockbackY = 4f,
                },
            },
        };

        /// <summary>空中・上: 頭上。真上へ打ち上げる（追撃の始動）。</summary>
        public AttackData AirUp = new AttackData
        {
            TotalTicks = 24,
            WireCancelFromTick = 6,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 6, ActiveTicks = 4, OffsetX = 0.2f, OffsetY = 1.75f, HalfWidth = 1.75f, HalfHeight = 1.75f,
                    Damage = 5, HitstopTicks = 12, HitstunTicks = 22, KnockbackX = 2f, KnockbackY = 14f,
                },
            },
        };

        /// <summary>
        /// 空中・下（デフォルトのキャラクターの撃破技。D-38）: 発生（チャージ）が遅く、その間は慣性のまま。チャージの後、
        /// 斜め下へ飛び込みながら攻撃し、相手を斜め下へたたき落とす。
        /// </summary>
        public AttackData AirDown = new AttackData
        {
            TotalTicks = 40,
            WireCancelFromTick = 16,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 16, ActiveTicks = 12, OffsetX = 1f, OffsetY = -2f, HalfWidth = 1.75f, HalfHeight = 2f,
                    Damage = 8, HitstopTicks = 20, HitstunTicks = 28, KnockbackX = 6f, KnockbackY = -12f,
                },
            },
            Motions = new[]
            {
                new AttackMotion { StartTick = 16, Ticks = 12, VelocityX = 9f, VelocityY = -15f },
            },
        };

        /// <summary>
        /// 特殊攻撃 1（デフォルトのキャラクター。docs/13 T5-2 の仮の技）: 少しためてから、向いている方向へまっすぐ突進して当てる
        /// （突進の間は高さを保つ。横への移動技にもなる）。
        /// </summary>
        public AttackData Special1 = new AttackData
        {
            Name = "DASH",
            TotalTicks = 30,
            WireCancelFromTick = 6,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 6, ActiveTicks = 12, OffsetX = 1f, HalfWidth = 1.5f, HalfHeight = 1.5f,
                    Damage = 6, HitstopTicks = 12, HitstunTicks = 24, KnockbackX = 12f, KnockbackY = 8f,
                },
            },
            Motions = new[]
            {
                new AttackMotion { StartTick = 0, Ticks = 6, VelocityX = 0f, VelocityY = 0f },
                new AttackMotion { StartTick = 6, Ticks = 12, VelocityX = 22f, VelocityY = 0f },
            },
        };

        /// <summary>特殊攻撃 1 をもう一度使えるまで（ティック。使い始めたときから数える。D-55）。【仮】4 秒。</summary>
        public int Special1CooldownTicks = 240;

        /// <summary>
        /// 特殊攻撃 2（デフォルトのキャラクター。docs/13 T5-2 の仮の技）: すぐに斜め上へ飛び上がりながら、頭上を打ち上げる
        /// （上への移動技にもなる。ステージの外から戻るのにも使える）。
        /// </summary>
        public AttackData Special2 = new AttackData
        {
            Name = "RISE",
            TotalTicks = 30,
            WireCancelFromTick = 4,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 4, ActiveTicks = 8, OffsetX = 0.4f, OffsetY = 1.2f, HalfWidth = 1.5f, HalfHeight = 2f,
                    Damage = 5, HitstopTicks = 12, HitstunTicks = 26, KnockbackX = 3f, KnockbackY = 15f,
                },
            },
            Motions = new[]
            {
                new AttackMotion { StartTick = 4, Ticks = 8, VelocityX = 3f, VelocityY = 14f },
            },
        };

        /// <summary>特殊攻撃 2 をもう一度使えるまで（ティック）。【仮】6 秒。</summary>
        public int Special2CooldownTicks = 360;

        /// <summary>
        /// 必殺技（デフォルトのキャラクター。docs/13 T5-3 の仮の技）: その場で止まってためた後、体の周りを大きく吹き飛ばす。
        /// ダメージは 60（ユーザーの要望、2026-10-15。15 では低かった）。
        /// </summary>
        public AttackData Super = new AttackData
        {
            Name = "BURST",
            TotalTicks = 54,
            WireCancelFromTick = 18,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 18, ActiveTicks = 8, OffsetX = 0f, HalfWidth = 4.5f, HalfHeight = 4.5f,
                    Damage = 60, HitstopTicks = 24, HitstunTicks = 40, KnockbackX = 16f, KnockbackY = 12f,
                },
            },
            Motions = new[]
            {
                new AttackMotion { StartTick = 0, Ticks = 26, VelocityX = 0f, VelocityY = 0f },
            },
        };

        /// <summary>
        /// 必殺技に使うゲージ（0.1% 単位）。出したときに減る。キャラクターごとに変える（ユーザーの要望、2026-10-15。最大の 100% 以下）。
        /// デフォルトのキャラクター（STANDARD）の BURST は 70%（ユーザーの要望、2026-10-16）。
        /// </summary>
        public int SuperGaugeCost = 700;

        /// <summary>撃破技にする攻撃（キャラクターごとに1つ。D-38）。</summary>
        public AttackSlot KoAttack = AttackSlot.AirDown;

        /// <summary>
        /// 撃破技の倍率 = KoKnockbackBase + KoKnockbackPerSpeed × 打つ直前の速さ（m/s）。吹き飛びと威力（ダメージ）の両方にかける
        /// （D-38。速さの係数は、うまく速さを乗せて当てたときに爽快に飛ぶよう 0.1 から 3 倍の 0.3 にした。ユーザーの要望、2026-10-13）。【仮】
        /// </summary>
        public float KoKnockbackBase = 0.5f;
        public float KoKnockbackPerSpeed = 0.3f;

        /// <summary>
        /// 撃破技をこの速さ（m/s）以上で打って当てると「FATAL」: ヒットストップが FatalHitstopMultiplier 倍になり、派手な演出を出す
        /// （ユーザーの要望、2026-10-13。倍率は 3 倍で長すぎたので 2 倍にした。撃破技のヒットストップ 20 ティックの 2 倍で 40 ティック）。【仮】
        /// </summary>
        public float FatalStrikeSpeed = 14f;
        public int FatalHitstopMultiplier = 2;

        /// <summary>
        /// のけぞり中に床・壁・天井へぶつかったとき、ぶつかった向きの速さがこれ以上（m/s）なら跳ね返る（ユーザーの要望、2026-10-13）。【仮】
        /// </summary>
        public float BounceMinSpeed = 10f;

        /// <summary>跳ね返るときの速さの倍率（0〜1）。【仮】</summary>
        public float BounceRestitution = 0.6f;

        /// <summary>
        /// 攻撃をワイヤーでキャンセルした後、もう一度ワイヤーでキャンセルできるまでのティック数（P-1。永久コンボを止める）。
        /// 通常のワイヤーの射出には影響しない。【仮】
        /// </summary>
        public int WireCancelCooldownTicks = 180;

        /// <summary>
        /// 攻撃ボタンの先行入力（ティック）。押してからこの間に攻撃を始められる・キャンセルできるようになれば、そのときに出る。【仮】
        /// 地上技の1段目が当たった瞬間に押しても、空中に出たとき（2段目の始め）に空中の攻撃へキャンセルできる長さにする。
        /// </summary>
        public int AttackBufferTicks = 10;

        /// <summary>ガードの持続（ティック。D-21: 0.5 秒）。</summary>
        public int GuardTicks = 30;

        /// <summary>ガードを始めてから、ジャストガードになる長さ（ティック。D-21: 0.1 秒）。</summary>
        public int JustGuardTicks = 6;

        /// <summary>ガードが終わってから、次のガードができるまで（ティック。D-21: 2 秒）。ジャストガードに成功したら 0 に戻る（D-39）。</summary>
        public int GuardCooldownTicks = 120;

        /// <summary>ふつうのガードで攻撃を受けたとき、ガードが少なくともこの後まで続く（ガード硬直。ティック）。【仮】</summary>
        public int GuardStunTicks = 12;

        /// <summary>ふつうのガードで受けたときの吹き飛びの倍率（攻撃の吹き飛びにかける。ノックバック）。【仮】</summary>
        public float GuardKnockbackScale = 0.4f;

        /// <summary>ジャストガードされた攻撃側の隙（のけぞりの長さ。ティック）。【仮】</summary>
        public int ParriedStunTicks = 30;

        /// <summary>
        /// ジャストガードされた攻撃側が弾かれる速度（m/s。X は守った側から離れる向き、Y は上）。少しだけ斜め上に押し戻す
        /// （ユーザーの要望、2026-10-13）。【仮】
        /// </summary>
        public float ParriedPushbackX = 4f;
        public float ParriedPushbackY = 6f;

        /// <summary>ゲージ 1% あたりの値（ゲージは 0.1% 単位の整数。％で小数第 1 位まで表示する。ユーザーの要望、2026-10-15）。</summary>
        public const int GaugePerPercent = 10;

        /// <summary>
        /// ゲージの最大（0.1% 単位で 100%。docs/01_game_design.md 6.7）。どのキャラクターも同じ（ユーザーの指示、2026-10-15）。
        /// 使い道は必殺技だけ（D-55、D-57）。
        /// </summary>
        public const int MaxGauge = 100 * GaugePerPercent;

        /// <summary>攻撃が当たったときに、当てた側が得るゲージ（大。8%）。必殺技が当たったときは得ない【仮】。【仮】</summary>
        public int GaugeOnHit = 80;

        /// <summary>攻撃がふつうのガードをされたときに、攻撃した側が得るゲージ（3%）。【仮】</summary>
        public int GaugeOnGuarded = 30;

        /// <summary>攻撃を受けた（のけぞった）ときに得るゲージ（小。4%）。【仮】</summary>
        public int GaugeOnDamaged = 40;

        /// <summary>攻撃をふつうのガードで受けたときに得るゲージ（2%）。【仮】</summary>
        public int GaugeOnGuard = 20;

        /// <summary>ジャストガードに成功したときに得るゲージ（大。20%。D-21）。【仮】</summary>
        public int GaugeOnJustGuard = 200;

        /// <summary>
        /// コインを拾ったときに、コインの価値 1 枚あたり得るゲージ（0.2%。価値の高いコインはその枚数分。ユーザーの要望、2026-10-15）。
        /// 拾ったとき（フィールドのコインとドロップ）だけで、戦闘の報酬や拠点での確定では得ない。
        /// </summary>
        public int GaugePerCoin = 2;

        public AttackData Get(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.Ground: return Ground;
                case AttackSlot.AirNeutral: return AirNeutral;
                case AttackSlot.AirSide: return AirSide;
                case AttackSlot.AirUp: return AirUp;
                case AttackSlot.AirDown: return AirDown;
                case AttackSlot.Special1: return Special1;
                case AttackSlot.Special2: return Special2;
                case AttackSlot.Super: return Super;
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "不明な攻撃の種類");
            }
        }

        /// <summary>特殊攻撃か必殺技か（地上でも空中でも同じ技で、使う条件がある）。</summary>
        public static bool IsSpecial(AttackSlot slot)
        {
            return slot == AttackSlot.Special1 || slot == AttackSlot.Special2 || slot == AttackSlot.Super;
        }

        /// <summary>特殊攻撃のクールタイム（ティック。それ以外は 0）。</summary>
        public int CooldownTicksOf(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.Special1: return Special1CooldownTicks;
                case AttackSlot.Special2: return Special2CooldownTicks;
                default: return 0;
            }
        }

        /// <summary>ゲージを amount だけ増やす（最大 MaxGauge）。</summary>
        public static void AddGauge(ref CombatState combat, int amount)
        {
            combat.Gauge = Math.Min(MaxGauge, combat.Gauge + amount);
        }

        /// <summary>撃破技の倍率（吹き飛びと威力。打つ直前の速さ speed から）。</summary>
        public float KoKnockbackScale(float speed)
        {
            return KoKnockbackBase + KoKnockbackPerSpeed * speed;
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(CombatTuning));
            foreach (AttackSlot slot in (AttackSlot[])Enum.GetValues(typeof(AttackSlot)))
            {
                AttackData attack = Get(slot);
                v.NotNull(slot.ToString(), attack);
                if (attack != null)
                {
                    v.Merge(attack.Validate($"{nameof(CombatTuning)}.{slot}"));
                }
            }
            if (!Enum.IsDefined(typeof(AttackSlot), KoAttack))
            {
                v.Fail($"{nameof(KoAttack)} が攻撃の種類ではありません（{KoAttack}）");
            }
            v.NonNegative(nameof(Special1CooldownTicks), Special1CooldownTicks);
            v.NonNegative(nameof(Special2CooldownTicks), Special2CooldownTicks);
            v.Positive(nameof(SuperGaugeCost), SuperGaugeCost);
            v.AtMost(nameof(SuperGaugeCost), SuperGaugeCost, nameof(MaxGauge), MaxGauge);
            v.NonNegative(nameof(KoKnockbackBase), KoKnockbackBase);
            v.NonNegative(nameof(KoKnockbackPerSpeed), KoKnockbackPerSpeed);
            v.NonNegative(nameof(FatalStrikeSpeed), FatalStrikeSpeed);
            v.Positive(nameof(FatalHitstopMultiplier), FatalHitstopMultiplier);
            v.NonNegative(nameof(BounceMinSpeed), BounceMinSpeed);
            v.InRange(nameof(BounceRestitution), BounceRestitution, 0f, 1f);
            v.NonNegative(nameof(WireCancelCooldownTicks), WireCancelCooldownTicks);
            v.NonNegative(nameof(AttackBufferTicks), AttackBufferTicks);
            v.Positive(nameof(GuardTicks), GuardTicks);
            v.NonNegative(nameof(JustGuardTicks), JustGuardTicks);
            v.AtMost(nameof(JustGuardTicks), JustGuardTicks, nameof(GuardTicks), GuardTicks);
            v.NonNegative(nameof(GuardCooldownTicks), GuardCooldownTicks);
            v.NonNegative(nameof(GuardStunTicks), GuardStunTicks);
            v.InRange(nameof(GuardKnockbackScale), GuardKnockbackScale, 0f, 1f);
            v.NonNegative(nameof(ParriedStunTicks), ParriedStunTicks);
            v.NonNegative(nameof(ParriedPushbackX), ParriedPushbackX);
            v.NonNegative(nameof(GaugeOnHit), GaugeOnHit);
            v.NonNegative(nameof(GaugeOnGuarded), GaugeOnGuarded);
            v.NonNegative(nameof(GaugeOnDamaged), GaugeOnDamaged);
            v.NonNegative(nameof(GaugeOnGuard), GaugeOnGuard);
            v.NonNegative(nameof(GaugeOnJustGuard), GaugeOnJustGuard);
            v.NonNegative(nameof(GaugePerCoin), GaugePerCoin);
            return v;
        }
    }
}
