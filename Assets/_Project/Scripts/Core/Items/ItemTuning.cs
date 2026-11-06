using System;
using Airerage.Core.Combat;
using Airerage.Core.Common;

namespace Airerage.Core.Items
{
    /// <summary>
    /// アイテムの値（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。ユーザーが決めた数（割合・威力・クールタイム・詠唱の長さ）のほかは【仮】。
    /// 全員が同じ値を使う（SimWorld.ItemTuning）。
    /// </summary>
    [Serializable]
    public sealed class ItemTuning
    {
        // ---------------------------------------------------------------- パッシブ

        /// <summary>ブースター（Q・E のクールタイム、R のゲージの消費量）を減らす割合（%）。</summary>
        public int BoosterPercent = 20;

        /// <summary>ブースター極で減らす割合（%）と、引き換えに増やす割合（%。極の Q・E は反対の技を、極の R は Q と E を）。</summary>
        public int BoosterExPercent = 40;
        public int BoosterExPenaltyPercent = 20;
        public int UltimateExPenaltyPercent = 10;

        /// <summary>光子の盾でガードのクールタイムを減らす割合（%）。</summary>
        public int ShieldPercent = 20;

        /// <summary>マガジンマガジンで増やすスタック数（投射物の技とアイテム）。</summary>
        public int MagazineCharges = 1;

        /// <summary>グリーンバナナで設置物を大きくする倍率（見た目と判定）。</summary>
        public float BananaScale = 1.5f;

        /// <summary>ダイアモンドアレイで直接攻撃の技とアイテムの威力に足す割合（%。1.5 倍 = +50%。AttackPower の上乗せ）。</summary>
        public int DiamondDamagePercent = 50;

        /// <summary>スカイブーツで増やす空中ジャンプの回数。</summary>
        public int SkyBootsAirJumps = 1;

        /// <summary>ゴールドマグネットでコインを拾える範囲にかける数。</summary>
        public float MagnetPickupScale = 2f;

        /// <summary>金庫の鍵: 拠点で確定したときのボーナス（確定した枚数の %。切り捨て）。</summary>
        public int VaultBonusPercent = 10;

        /// <summary>鉄の長靴: 受ける吹き飛び（%。−20%）と、受ける重力（%。+30%）。ユーザーの指示で移動の −10% はなし（2026-11-05）。</summary>
        public int IronBootsKnockbackPercent = -20;
        public int IronBootsGravityPercent = 30;

        /// <summary>鋼のワイヤー: ワイヤーキャンセルのクールタイムを減らす割合（%。ユーザーの指示で −50%）。</summary>
        public int SteelWirePercent = 50;

        /// <summary>ロングフック: ワイヤーの射程を伸ばす割合（%）。</summary>
        public int LongHookPercent = 25;

        /// <summary>予備電池: 攻撃で得るゲージを増やす割合（%）。</summary>
        public int BatteryGaugePercent = 25;

        /// <summary>金の鈴: ジャストガードの受付時間にかける数。</summary>
        public float BellJustGuardScale = 1.5f;

        /// <summary>不死鳥の羽根: 復帰待ちを短くするティック（1 秒）と、撃破ドロップの枚数にかける割合（%。半分）。</summary>
        public int PhoenixRespawnTicks = 60;
        public int PhoenixKoDropPercent = 50;

        /// <summary>
        /// 逆境のお守り: 確定コインの順位が最下位の間、威力・攻撃で得るゲージ・戦闘の報酬のコインを増やす割合（%）。持っている間はいつも、
        /// 撃破ドロップが所持の全部になり、受ける威力と吹き飛びが 1.5 倍（+50%）になる（ユーザーの指示、2026-11-05）。
        /// </summary>
        public int CharmDamagePercent = 20;
        public int CharmGaugePercent = 20;
        public int CharmCoinPercent = 20;
        public int CharmTakenPercent = 50;

        /// <summary>
        /// スパイクの靴底: 壁に張り付ける時間を延ばすティック（2 秒）。張り付いた壁を凍らせる長さ（10 秒）と、凍らせる壁の高さ（半分。m）、
        /// 凍った壁に触れた相手を凍らせる長さ（1 秒）。氷の上で滑らない。
        /// </summary>
        public int SpikeClingTicks = 120;
        public int SpikeWallTicks = 600;
        public float SpikeWallHalfHeight = 1.6f;
        public float SpikeWallHalfWidth = 0.25f;
        public int SpikeFreezeTicks = 60;

        /// <summary>羽毛の帽子: 受ける吹き飛び（%。+20%）と、受ける重力（%。−30%。ユーザーの指示、2026-11-05）。</summary>
        public int FeatherKnockbackPercent = 20;
        public int FeatherGravityPercent = -30;

        // ---------------------------------------------------------------- 落ちているアイテム

        /// <summary>ドロップの頻度（少・中・多）ごとの、1 個落ちてくるまでのティック（30 秒に 4・8・12 個ほど）。</summary>
        public int[] DropIntervalTicks = { 450, 225, 150 };

        /// <summary>落ちているアイテムの半分の大きさ（m。拾える範囲もこの箱とキャラクターの箱の重なり）、重力（m/s²）、落ちる速さの上限（m/s）。</summary>
        public float DropHalfSize = 0.45f;
        public float DropGravity = 30f;
        public float DropMaxFallSpeed = 20f;

        /// <summary>フィールドに同時に落ちていられる数（これ以上は落ちてこない）【仮】。</summary>
        public int MaxFieldItems = 16;

        /// <summary>
        /// 捨てたアイテムが跳ねる速さ（m/s。上向き）と、向いている方向へ飛び出る速さ（m/s。その場ではなく少し前に落ちる。ユーザーの要望、
        /// 2026-11-06）、捨てた人がもう一度拾えるまで（ティック）【仮】。
        /// </summary>
        public float DiscardPopSpeed = 6f;
        public float DiscardForwardSpeed = 4f;
        public int DiscardPickupDelayTicks = 20;

        // ---------------------------------------------------------------- エネルギーショット

        /// <summary>エネルギーショット: クールタイム（8 秒）、速さ、当たりの半径、届く距離、出す距離（中心から照準の方向）。</summary>
        public int ShotCooldownTicks = 480;
        public float ShotSpeed = 26f;
        public float ShotRadius = 0.45f;
        public float ShotRange = 30f;
        public float ShotMuzzle = 1f;

        /// <summary>エネルギーショットの攻撃（威力 10。ユーザーの案）。</summary>
        public AttackHit ShotHit = new AttackHit
        {
            Damage = 10, Gauge = 60, HitstopTicks = 8, HitstunTicks = 22, KnockbackX = 10f, KnockbackY = 6f,
        };

        // ---------------------------------------------------------------- 名刀『櫓落とし』

        /// <summary>
        /// 櫓落とし: クールタイム（15 秒）、1 振りの長さ（ティック。縦・横・斜めの 3 振り）、詠唱（0.5 秒）、突進の長さ（ティック）と距離（m）、
        /// 一閃の判定の太さ（半分。m）、振りの判定の前への距離と半分の大きさ（m）。
        /// </summary>
        public int SwordCooldownTicks = 900;
        public int SwordSlashTicks = 10;
        public int SwordCastTicks = 30;
        public int SwordDashTicks = 6;
        public float SwordDashDistance = 9f;
        public float SwordFlashHalfWidth = 0.9f;
        public float SwordReach = 1.4f;
        public float SwordSlashHalfLength = 1.4f;
        public float SwordSlashHalfWidth = 0.5f;

        /// <summary>3 段の振り（威力 5）と一閃（威力 30、当たると FATAL。ユーザーの案）。</summary>
        public AttackHit SwordSlashHit = new AttackHit
        {
            Damage = 5, Gauge = 40, HitstopTicks = 6, HitstunTicks = 24, KnockbackX = 3f, KnockbackY = 4f,
        };
        public AttackHit SwordFlashHit = new AttackHit
        {
            Damage = 30, Gauge = 100, HitstopTicks = 16, HitstunTicks = 40, KnockbackX = 16f, KnockbackY = 12f,
        };

        // ---------------------------------------------------------------- イヤーボーン

        /// <summary>イヤーボーン: クールタイム（15 秒）、詠唱（0.2 秒）、風の半径（m）、押し出す速さ（m/s。外向き）と上向きの速さ。</summary>
        public int EarCooldownTicks = 900;
        public int EarCastTicks = 12;
        public float EarRadius = 5.5f;
        public float EarPushSpeed = 24f;
        public float EarPushUp = 4f;

        // ---------------------------------------------------------------- イカロスウイング

        /// <summary>イカロスウイング: クールタイム（12 秒）、詠唱（0.2 秒）、飛べる長さ（1 秒）。点滅はしない（ユーザーの指示、2026-11-05）。</summary>
        public int WingCooldownTicks = 720;
        public int WingCastTicks = 12;
        public int WingFlightTicks = 60;

        // ---------------------------------------------------------------- 砂岩ボール

        /// <summary>
        /// 砂岩ボール: クールタイム（10 秒。ボールが消えてから数える）、消えるまで（5 秒）、半径（m）、出す距離（m）、重力（m/s²）、
        /// 地形で跳ね返る割合、地面を転がるときの減速（m/s²）、落ちる速さの上限（m/s）。
        /// </summary>
        public int BallCooldownTicks = 600;
        public int BallLifetimeTicks = 300;
        public float BallRadius = 0.6f;
        public float BallSpawnDistance = 1.4f;
        public float BallGravity = 30f;
        public float BallRestitution = 0.45f;
        public float BallRollFriction = 6f;
        public float BallMaxFallSpeed = 30f;

        /// <summary>
        /// 動いている砂岩ボールに当たった人の攻撃: 威力は速さ × DamagePerSpeed（四捨五入）、吹き飛びは速さ × KnockbackPerSpeed（ボールの向き）。
        /// MinHarmSpeed より遅いボールは当たらない。最後に打った人（打たれていなければ持ち主）には当たらない【仮】。
        /// </summary>
        public float BallMinHarmSpeed = 6f;
        public float BallDamagePerSpeed = 0.5f;
        public float BallKnockbackPerSpeed = 0.8f;
        public int BallHitstunTicks = 26;
        public int BallHitstopTicks = 8;
        public int BallGauge = 60;

        /// <summary>攻撃で打たれたときの速さ（攻撃の吹き飛び × この値）と、速さの上限（m/s）。</summary>
        public float BallStrikeScale = 1f;
        public float BallMaxSpeed = 40f;

        // ---------------------------------------------------------------- グラップリングボム

        /// <summary>
        /// グラップリングボム: クールタイム（12 秒）、投げる速さ（m/s）、重力（m/s²）、当たりの半径（m）、刺さってから爆発するまで（1.5 秒）、
        /// 刺さらずに飛べる長さ（2 秒。過ぎたら爆発）、爆発の半径（m）。
        /// </summary>
        public int BombCooldownTicks = 720;
        public float BombSpeed = 20f;
        public float BombGravity = 25f;
        public float BombRadius = 0.35f;
        public int BombFuseTicks = 90;
        public int BombFlightTicks = 120;
        public float BombBlastRadius = 2.6f;

        /// <summary>爆発の攻撃（威力 15）。</summary>
        public AttackHit BombHit = new AttackHit
        {
            Damage = 15, Gauge = 80, HitstopTicks = 10, HitstunTicks = 30, KnockbackX = 12f, KnockbackY = 10f,
        };

        // ---------------------------------------------------------------- 反射板

        /// <summary>反射板: クールタイム（15 秒。ユーザーの指示）、跳ね返す長さ（1 秒）、正面の跳ね返す範囲（前への距離と半分の大きさ。m）。</summary>
        public int ReflectorCooldownTicks = 900;
        public int ReflectorTicks = 60;
        public float ReflectorReach = 0.9f;
        public float ReflectorHalfWidth = 0.9f;
        public float ReflectorHalfHeight = 1.3f;

        // ---------------------------------------------------------------- 折りたたみの足場

        /// <summary>折りたたみの足場: クールタイム（12 秒）、消えるまで（4 秒）、出す距離（照準の方向。m）、半分の大きさ（m）。</summary>
        public int PlatformCooldownTicks = 720;
        public int PlatformTicks = 240;
        public float PlatformDistance = 2.5f;
        public float PlatformHalfWidth = 1.5f;
        public float PlatformHalfHeight = 0.2f;

        // ---------------------------------------------------------------- 大きな磁石

        /// <summary>大きな磁石: クールタイム（20 秒）、吸い寄せる長さ（3 秒）、半径（m）、吸い寄せる速さ（m/s）。</summary>
        public int BigMagnetCooldownTicks = 1200;
        public int BigMagnetTicks = 180;
        public float BigMagnetRadius = 8f;
        public float BigMagnetPullSpeed = 14f;

        // ---------------------------------------------------------------- 帰還のリモコン

        /// <summary>帰還のリモコン: クールタイム（30 秒。ユーザーの指示。瞬間移動するか印が消えてから数える）、印が残る長さ（10 秒）。</summary>
        public int RemoteCooldownTicks = 1800;
        public int RemoteMarkTicks = 600;

        // ---------------------------------------------------------------- 砂時計

        /// <summary>砂時計: クールタイム（18 秒）、詠唱（0.3 秒）、半径（m）、遅くする長さ（1.5 秒）と割合（%。0.5 倍 = −50%）。</summary>
        public int HourglassCooldownTicks = 1080;
        public int HourglassCastTicks = 18;
        public float HourglassRadius = 5f;
        public int HourglassSlowTicks = 90;
        public int HourglassSlowPercent = -50;

        // ---------------------------------------------------------------- チェーンフック

        /// <summary>チェーンフック: クールタイム（15 秒。ユーザーの指示）、飛ぶ速さ（m/s）、届く距離（m）、当たりの半径（m）、引き寄せる時間（秒）。</summary>
        public int HookCooldownTicks = 900;
        public float HookSpeed = 40f;
        public float HookRange = 12f;
        public float HookRadius = 0.4f;
        public float HookPullSeconds = 0.25f;

        /// <summary>チェーンフックが当たった攻撃（威力 3。吹き飛びは引き寄せる速さで決める）。</summary>
        public AttackHit HookHit = new AttackHit
        {
            Damage = 3, Gauge = 30, HitstopTicks = 6, HitstunTicks = 24, KnockbackX = 0f, KnockbackY = 0f,
        };

        // ---------------------------------------------------------------- シャボンのバリア

        /// <summary>シャボンのバリア: クールタイム（30 秒。ユーザーの指示）、続く長さ（3 秒）。</summary>
        public int BarrierCooldownTicks = 1800;
        public int BarrierTicks = 180;

        // ---------------------------------------------------------------- ポケットロケット

        /// <summary>ポケットロケット: クールタイム（8 秒）、加速する長さ（0.4 秒）、速さ（m/s。ワイヤーより速い）。</summary>
        public int RocketCooldownTicks = 480;
        public int RocketTicks = 24;
        public float RocketSpeed = 34f;

        // ---------------------------------------------------------------- 携帯トランポリン

        /// <summary>携帯トランポリン: クールタイム（12 秒）、消えるまで（5 秒）、半分の大きさ（m）、跳ね上げる速さ（m/s）。</summary>
        public int TrampolineCooldownTicks = 720;
        public int TrampolineTicks = 300;
        public float TrampolineHalfWidth = 0.9f;
        public float TrampolineHalfHeight = 0.25f;
        public float TrampolineLaunchSpeed = 24f;

        // ---------------------------------------------------------------- 身代わり爆弾（ItemId.DecoyDoll。2026-11-06 に「身代わり爆弾」から改名）

        /// <summary>
        /// 身代わり爆弾（ユーザーの指定、2026-11-06）: クールタイム（15 秒。爆発するか消えてから数える）、爆発するまでに受けられる攻撃の回数
        /// （3 回）、爆発するまでの長さ（5 秒。同じキーをもう一度押すとすぐに爆発する）、置く距離（照準の方向。m）、大きさ（キャラクターの箱に
        /// かける数）【仮】。
        /// </summary>
        public int DecoyCooldownTicks = 900;
        public int DecoyHits = 3;
        public int DecoyTicks = 300;
        public float DecoyDistance = 1.8f;
        public float DecoySizeScale = 1.3f;

        /// <summary>一度でも攻撃を受けた人形の動き: 攻撃の吹き飛びにかける数、重力（m/s²）、落ちる速さの上限（m/s）、地面での減速（m/s²）【仮】。</summary>
        public float DecoyKnockScale = 0.8f;
        public float DecoyGravity = 30f;
        public float DecoyMaxFallSpeed = 30f;
        public float DecoyGroundFriction = 12f;

        /// <summary>
        /// 人形の爆発（記録した威力の合計 recorded に応じる）: 半径は Base + PerDamage × recorded（Max まで。m）、威力は recorded の DamagePercent %
        /// （DamageMax まで）、吹き飛びは DecoyBlastHit の値に 0.6 + recorded / KnockbackDivisor をかける（KnockbackScaleMax まで）。
        /// 記録が 0 なら、見た目だけで当たらない【仮】。
        /// </summary>
        public float DecoyBlastRadiusBase = 1.6f;
        public float DecoyBlastRadiusPerDamage = 0.1f;
        public float DecoyBlastRadiusMax = 6f;
        public int DecoyBlastDamagePercent = 100;
        public int DecoyBlastDamageMax = 60;
        public float DecoyBlastKnockbackDivisor = 40f;
        public float DecoyBlastKnockbackScaleMax = 2f;
        public AttackHit DecoyBlastHit = new AttackHit
        {
            Damage = 0, Gauge = 60, HitstopTicks = 10, HitstunTicks = 30, KnockbackX = 10f, KnockbackY = 9f,
        };

        // ---------------------------------------------------------------- 瓶詰めの雷雲

        /// <summary>
        /// 瓶詰めの雷雲: クールタイム（16 秒）、出す距離（照準の方向。m）、続く長さ（3 秒）、雷の回数（1 秒ごと）、雷の届く幅（半分）と
        /// 下への長さ（m）。
        /// </summary>
        public int CloudCooldownTicks = 960;
        public float CloudDistance = 7f;
        public int CloudTicks = 180;
        public int CloudStrikes = 3;
        public float CloudHalfWidth = 1.6f;
        public float CloudDepth = 10f;

        /// <summary>雷の攻撃（威力 2 で感電）。</summary>
        public AttackHit CloudHit = new AttackHit
        {
            Damage = 2, Gauge = 20, HitstopTicks = 4, HitstunTicks = 12, KnockbackX = 1f, KnockbackY = 2f, StunTicks = 30,
        };

        // ---------------------------------------------------------------- 確定の印鑑

        /// <summary>確定の印鑑: クールタイム（30 秒）、詠唱（2 秒。当てられると止まる）、確定する割合（所持の %）。</summary>
        public int SealCooldownTicks = 1800;
        public int SealCastTicks = 120;
        public int SealPercent = 50;

        // ---------------------------------------------------------------- 落ちているアイテム（続き）

        /// <summary>着地してから消えるまで（30 秒。ユーザーの指示）と、拾う範囲を広げる量（m。箱の重なりの余裕）、地上で重なったときに押しのける速さ（m/s）。</summary>
        public int DropLifetimeTicks = 1800;
        public float DropPickupMargin = 0.45f;
        public float DropSpreadSpeed = 3f;

        // ---------------------------------------------------------------- 共通

        /// <summary>アクティブアイテム id のクールタイム（ティック。スタック 1 つが戻るまで）。パッシブは 0。</summary>
        public int CooldownOf(ItemId id)
        {
            switch (id)
            {
                case ItemId.EnergyShot:
                    return ShotCooldownTicks;
                case ItemId.Yaguraotoshi:
                    return SwordCooldownTicks;
                case ItemId.EarBone:
                    return EarCooldownTicks;
                case ItemId.IcarusWing:
                    return WingCooldownTicks;
                case ItemId.SandstoneBall:
                    return BallCooldownTicks;
                case ItemId.GrappleBomb:
                    return BombCooldownTicks;
                case ItemId.Reflector:
                    return ReflectorCooldownTicks;
                case ItemId.FoldingPlatform:
                    return PlatformCooldownTicks;
                case ItemId.BigMagnet:
                    return BigMagnetCooldownTicks;
                case ItemId.ReturnRemote:
                    return RemoteCooldownTicks;
                case ItemId.Hourglass:
                    return HourglassCooldownTicks;
                case ItemId.ChainHook:
                    return HookCooldownTicks;
                case ItemId.BubbleBarrier:
                    return BarrierCooldownTicks;
                case ItemId.PocketRocket:
                    return RocketCooldownTicks;
                case ItemId.PocketTrampoline:
                    return TrampolineCooldownTicks;
                case ItemId.DecoyDoll:
                    return DecoyCooldownTicks;
                case ItemId.BottledThunder:
                    return CloudCooldownTicks;
                case ItemId.BankSeal:
                    return SealCooldownTicks;
                default:
                    return 0;
            }
        }

        /// <summary>詠唱の長さ（ティック。詠唱のないアイテムは 0）。</summary>
        public int CastTicksOf(ItemId id)
        {
            switch (id)
            {
                case ItemId.Yaguraotoshi:
                    return SwordCastTicks;
                case ItemId.EarBone:
                    return EarCastTicks;
                case ItemId.IcarusWing:
                    return WingCastTicks;
                case ItemId.Hourglass:
                    return HourglassCastTicks;
                case ItemId.BankSeal:
                    return SealCastTicks;
                default:
                    return 0;
            }
        }

        /// <summary>ドロップの頻度 rate（0〜2）の、1 個落ちてくるまでのティック。</summary>
        public int DropIntervalOf(int rate)
        {
            return DropIntervalTicks[Math.Max(0, Math.Min(rate, DropIntervalTicks.Length - 1))];
        }

        /// <summary>櫓落としの 3 振りが終わるまで（ティック）。</summary>
        public int SwordSlashesTicks => SwordSlashTicks * 3;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(ItemTuning));
            v.InRange(nameof(BoosterPercent), BoosterPercent, 0f, 99f);
            v.InRange(nameof(BoosterExPercent), BoosterExPercent, 0f, 99f);
            v.NonNegative(nameof(BoosterExPenaltyPercent), BoosterExPenaltyPercent);
            v.NonNegative(nameof(UltimateExPenaltyPercent), UltimateExPenaltyPercent);
            v.InRange(nameof(ShieldPercent), ShieldPercent, 0f, 99f);
            v.NonNegative(nameof(MagazineCharges), MagazineCharges);
            v.Positive(nameof(BananaScale), BananaScale);
            v.NonNegative(nameof(DiamondDamagePercent), DiamondDamagePercent);
            v.NotNull(nameof(DropIntervalTicks), DropIntervalTicks);
            if (DropIntervalTicks != null)
            {
                if (DropIntervalTicks.Length == 0)
                {
                    v.Fail("ドロップの頻度が 1 つもありません");
                }
                for (int i = 0; i < DropIntervalTicks.Length; i++)
                {
                    v.Positive($"DropIntervalTicks[{i}]", DropIntervalTicks[i]);
                }
            }
            v.Positive(nameof(DropHalfSize), DropHalfSize);
            v.NonNegative(nameof(DropGravity), DropGravity);
            v.Positive(nameof(DropMaxFallSpeed), DropMaxFallSpeed);
            v.Positive(nameof(MaxFieldItems), MaxFieldItems);
            v.NonNegative(nameof(DiscardPickupDelayTicks), DiscardPickupDelayTicks);
            v.NonNegative(nameof(DiscardForwardSpeed), DiscardForwardSpeed);
            v.Positive(nameof(ShotCooldownTicks), ShotCooldownTicks);
            v.Positive(nameof(ShotSpeed), ShotSpeed);
            v.Positive(nameof(ShotRadius), ShotRadius);
            v.Positive(nameof(ShotRange), ShotRange);
            v.NotNull(nameof(ShotHit), ShotHit);
            v.Positive(nameof(SwordCooldownTicks), SwordCooldownTicks);
            v.Positive(nameof(SwordSlashTicks), SwordSlashTicks);
            v.NonNegative(nameof(SwordCastTicks), SwordCastTicks);
            v.Positive(nameof(SwordDashTicks), SwordDashTicks);
            v.NonNegative(nameof(SwordDashDistance), SwordDashDistance);
            v.Positive(nameof(SwordFlashHalfWidth), SwordFlashHalfWidth);
            v.NotNull(nameof(SwordSlashHit), SwordSlashHit);
            v.NotNull(nameof(SwordFlashHit), SwordFlashHit);
            v.Positive(nameof(EarCooldownTicks), EarCooldownTicks);
            v.NonNegative(nameof(EarCastTicks), EarCastTicks);
            v.Positive(nameof(EarRadius), EarRadius);
            v.Positive(nameof(WingCooldownTicks), WingCooldownTicks);
            v.NonNegative(nameof(WingCastTicks), WingCastTicks);
            v.Positive(nameof(WingFlightTicks), WingFlightTicks);
            v.Positive(nameof(BallCooldownTicks), BallCooldownTicks);
            v.Positive(nameof(BallLifetimeTicks), BallLifetimeTicks);
            v.Positive(nameof(BallRadius), BallRadius);
            v.InRange(nameof(BallRestitution), BallRestitution, 0f, 1f);
            v.Positive(nameof(BallMaxSpeed), BallMaxSpeed);
            v.NotNull(nameof(BombHit), BombHit);
            v.NotNull(nameof(HookHit), HookHit);
            v.NotNull(nameof(CloudHit), CloudHit);
            v.Positive(nameof(DropLifetimeTicks), DropLifetimeTicks);
            v.Positive(nameof(BombFuseTicks), BombFuseTicks);
            v.Positive(nameof(ReflectorTicks), ReflectorTicks);
            v.Positive(nameof(PlatformTicks), PlatformTicks);
            v.Positive(nameof(BigMagnetTicks), BigMagnetTicks);
            v.Positive(nameof(RemoteMarkTicks), RemoteMarkTicks);
            v.Positive(nameof(BarrierTicks), BarrierTicks);
            v.Positive(nameof(RocketTicks), RocketTicks);
            v.Positive(nameof(TrampolineTicks), TrampolineTicks);
            v.Positive(nameof(DecoyHits), DecoyHits);
            v.Positive(nameof(DecoyTicks), DecoyTicks);
            v.NonNegative(nameof(DecoyDistance), DecoyDistance);
            v.Positive(nameof(DecoySizeScale), DecoySizeScale);
            v.NonNegative(nameof(DecoyGravity), DecoyGravity);
            v.Positive(nameof(DecoyMaxFallSpeed), DecoyMaxFallSpeed);
            v.Positive(nameof(DecoyBlastRadiusBase), DecoyBlastRadiusBase);
            v.Positive(nameof(DecoyBlastRadiusMax), DecoyBlastRadiusMax);
            v.Positive(nameof(DecoyBlastKnockbackDivisor), DecoyBlastKnockbackDivisor);
            v.NotNull(nameof(DecoyBlastHit), DecoyBlastHit);
            v.Positive(nameof(CloudStrikes), CloudStrikes);
            v.InRange(nameof(SealPercent), SealPercent, 0f, 100f);
            for (int i = 1; i <= ItemCatalog.Count; i++)
            {
                var id = (ItemId)i;
                if (ItemCatalog.IsActive(id) && CooldownOf(id) <= 0)
                {
                    v.Fail($"アクティブアイテム {id} のクールタイムがありません");
                }
            }
            return v;
        }
    }
}
