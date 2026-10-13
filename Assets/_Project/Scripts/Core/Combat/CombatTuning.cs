using System;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Combat
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
            if (Hits != null)
            {
                if (Hits.Length < 1 || Hits.Length > MaxHits)
                {
                    v.Fail($"判定は 1〜{MaxHits} つ（{Hits.Length} つ）");
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
    /// </summary>
    [Serializable]
    public sealed class CombatTuning
    {
        /// <summary>
        /// 地上技（デフォルトのキャラクター）: 前へ大きく踏み込みながら1段目、斜め上へ飛び出しながら2段目の二段技。
        /// 最後に地面を離れて空中に移る（キャラクターを作るときの方針。D-35）。1段目は踏み込みに追い越されないように前へ飛ばし、
        /// 2段目は飛び出しとほぼ同じ速度で飛ばす（空中技につなげやすいように。相手との距離 0.4〜2.6m で2段とも当たる）。
        /// </summary>
        public AttackData Ground = new AttackData
        {
            TotalTicks = 28,
            WireCancelFromTick = 5,
            Hits = new[]
            {
                new AttackHit
                {
                    StartTick = 5, ActiveTicks = 8, OffsetX = 0.8f, HalfWidth = 0.7f, HalfHeight = 0.7f,
                    Damage = 4, HitstopTicks = 5, HitstunTicks = 20, KnockbackX = 13f, KnockbackY = 5f,
                },
                new AttackHit
                {
                    StartTick = 13, ActiveTicks = 5, OffsetX = 0.6f, OffsetY = 0.9f, HalfWidth = 0.8f, HalfHeight = 0.8f,
                    Damage = 4, HitstopTicks = 6, HitstunTicks = 24, KnockbackX = 10.5f, KnockbackY = 13.5f,
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
                    StartTick = 5, ActiveTicks = 4, OffsetX = 0f, HalfWidth = 1.1f, HalfHeight = 1.1f,
                    Damage = 4, HitstopTicks = 5, HitstunTicks = 20, KnockbackX = 5f, KnockbackY = 7f,
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
                    StartTick = 7, ActiveTicks = 4, OffsetX = 1.1f, OffsetY = 0.1f, HalfWidth = 0.8f, HalfHeight = 0.5f,
                    Damage = 6, HitstopTicks = 7, HitstunTicks = 24, KnockbackX = 14f, KnockbackY = 4f,
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
                    StartTick = 6, ActiveTicks = 4, OffsetX = 0.2f, OffsetY = 1.1f, HalfWidth = 0.7f, HalfHeight = 0.7f,
                    Damage = 5, HitstopTicks = 6, HitstunTicks = 22, KnockbackX = 2f, KnockbackY = 14f,
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
                    StartTick = 16, ActiveTicks = 12, OffsetX = 0.4f, OffsetY = -0.8f, HalfWidth = 0.7f, HalfHeight = 0.8f,
                    Damage = 8, HitstopTicks = 10, HitstunTicks = 28, KnockbackX = 6f, KnockbackY = -12f,
                },
            },
            Motions = new[]
            {
                new AttackMotion { StartTick = 16, Ticks = 12, VelocityX = 9f, VelocityY = -15f },
            },
        };

        /// <summary>撃破技にする攻撃（キャラクターごとに1つ。D-38）。</summary>
        public AttackSlot KoAttack = AttackSlot.AirDown;

        /// <summary>撃破技の吹き飛びの倍率 = KoKnockbackBase + KoKnockbackPerSpeed × 打つ直前の速さ（m/s）。【仮】</summary>
        public float KoKnockbackBase = 0.5f;
        public float KoKnockbackPerSpeed = 0.1f;

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

        public AttackData Get(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.Ground: return Ground;
                case AttackSlot.AirNeutral: return AirNeutral;
                case AttackSlot.AirSide: return AirSide;
                case AttackSlot.AirUp: return AirUp;
                case AttackSlot.AirDown: return AirDown;
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "不明な攻撃の種類");
            }
        }

        /// <summary>撃破技の吹き飛びの倍率（打つ直前の速さ speed から）。</summary>
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
            v.NonNegative(nameof(KoKnockbackBase), KoKnockbackBase);
            v.NonNegative(nameof(KoKnockbackPerSpeed), KoKnockbackPerSpeed);
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
            return v;
        }
    }
}
