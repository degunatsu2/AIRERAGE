using Airerage.Core.Combat;
using Airerage.Core.Movement;
using Airerage.Core.Skills;
using Airerage.Core.Wire;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// キャラクターの調整値の作り方（docs/13 T5-6、docs/14）。STANDARD は CharacterConfig の初期値そのもので、ほかのキャラクターは
    /// 違うところだけを書き換える。エディタのビルダーが、ここからキャラクターのアセット（CharacterConfigAsset）を作る。
    /// 値はすべて【仮】。
    /// </summary>
    public static class CharacterPresets
    {
        public const string StandardName = "STANDARD";
        public const string RiskTakerName = "RISK TAKER";
        public const string StormRunnerName = "STORM RUNNER";
        public const string SpaceTravelerName = "SPACE TRAVELER";
        public const string PastryChefName = "PASTRY CHEF";
        public const string SoldererName = "SOLDERER";
        public const string ThiefName = "THIEF";
        public const string JuggernautName = "JUGGERNAUT";

        /// <summary>STANDARD（基準のキャラクター。DASH・RISE・BURST）。</summary>
        public static CharacterConfig Standard()
        {
            return new CharacterConfig();
        }

        /// <summary>
        /// RISK TAKER（docs/14 1.3。ユーザーの案、2026-10-15）。コインで勝負するキャラクター。
        /// - 地上技 ALL IN: 長押しでためて、離すと大きく踏み込む一撃（ユーザーの要望、2026-10-16）。ためている間はその場から動かず、
        ///   ためた長さで威力と吹き飛びが上がる（白 +0% → 緑 +20% → 青 +50% → 紫 +100% → 金 +200% → 虹 +500%。吹き飛びは
        ///   ユーザーの要望、2026-10-16）。虹で当てると JACKPOT。
        ///   当たると斜め上へ飛び出す。外すと隙が大きい。
        /// - 空中・上 COIN TOSS: コインを投げ上げて頭上を攻撃する（投射物。Claude の案）。撃ったときの手持ちのコイン 3 枚ごとに
        ///   威力 +1%（300 枚で +100% が最大。ユーザーの要望、2026-10-16）。
        /// - 空中・下（撃破技）ROULETTE SMASH: 頭上で回るルーレットが、攻撃が出たときに止まり、その目（0.5〜2 倍）がそのまま吹き飛びと
        ///   威力の倍率になる（速さの倍率の代わり。最大で JACKPOT。ユーザーの要望、2026-10-16）。
        /// - Q LIGHT（ターンオンザライト）: 5 秒のミラーボールを自分の少し上に置く。アンカーとしてつなげて、つないだ人は 1 秒に 5%
        ///   のゲージがたまる。クールタイム 15 秒（ユーザーの要望、2026-10-16。20 秒から）。
        /// - E SAFE（セーフティボックス）: 長押しで手持ちのコインを預けて（1 秒に 30 枚）金庫を置く。5 秒ごとに 2 枚足して 1.1 倍
        ///   （上限 300 枚）。壊す（耐久 9）か 3 分でばらまく。クールタイム 30 秒（壊れてから）。
        /// - R FEVER（フィーバータイム）: 5 秒間、拾ったコインと戦闘の報酬、得るゲージが 1.2 倍（ゲージはユーザーの要望、2026-10-16）。
        ///   必要なゲージ 50%。
        /// </summary>
        public static CharacterConfig RiskTaker()
        {
            var config = new CharacterConfig();
            CombatTuning combat = config.Combat;

            combat.Ground = new AttackData
            {
                Name = "ALL IN",
                TotalTicks = 42,
                WireCancelFromTick = 8,
                HoldMaxTicks = 180,
                HoldRooted = true,
                ChargeLevels = new[]
                {
                    new ChargeLevel { FromTicks = 0, BonusPercent = 0, KnockbackPercent = 0 },
                    new ChargeLevel { FromTicks = 24, BonusPercent = 20, KnockbackPercent = 20 },
                    new ChargeLevel { FromTicks = 48, BonusPercent = 50, KnockbackPercent = 50 },
                    new ChargeLevel { FromTicks = 78, BonusPercent = 100, KnockbackPercent = 100 },
                    new ChargeLevel { FromTicks = 120, BonusPercent = 200, KnockbackPercent = 200 },
                    new ChargeLevel { FromTicks = 180, BonusPercent = 500, KnockbackPercent = 500 },
                },
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 8, ActiveTicks = 8, OffsetX = 1.8f, HalfWidth = 2f, HalfHeight = 1.6f,
                        Damage = 3, Gauge = 90, HitstopTicks = 14, HitstunTicks = 26, KnockbackX = 14f, KnockbackY = 9f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 8, Ticks = 8, VelocityX = 20f },
                    new AttackMotion { StartTick = 16, Ticks = 1, VelocityX = 9f, VelocityY = 14f, RequiresHit = true },
                },
            };

            combat.AirUp = new AttackData
            {
                Name = "COIN TOSS",
                TotalTicks = 22,
                WireCancelFromTick = 6,
                Effect = SkillEffect.Projectile,
                EffectTick = 5,
                Hits = new AttackHit[0],
            };
            config.Skills.Projectile.HeldCoinsPerBonusPercent = 3;
            config.Skills.Projectile.MaxHeldCoinBonusPercent = 100;

            combat.AirDown.Name = "ROULETTE SMASH";
            combat.AirDown.Roulette = true;
            combat.AirDown.SpeedScaled = false;
            combat.KoAttack = AttackSlot.AirDown;

            combat.Special1 = new AttackData
            {
                Name = "LIGHT",
                TotalTicks = 16,
                WireCancelFromTick = 4,
                Effect = SkillEffect.MirrorBall,
                EffectTick = 4,
                Hits = new AttackHit[0],
            };
            combat.Special1CooldownTicks = 900;

            combat.Special2 = new AttackData
            {
                Name = "SAFE",
                TotalTicks = 14,
                WireCancelFromTick = 4,
                Effect = SkillEffect.Safe,
                EffectTick = 0,
                HoldMaxTicks = 600,
                Hits = new AttackHit[0],
            };
            combat.Special2CooldownTicks = 1800;

            combat.Super = new AttackData
            {
                Name = "FEVER",
                TotalTicks = 24,
                WireCancelFromTick = 12,
                Effect = SkillEffect.CoinBoost,
                EffectTick = 0,
                Hits = new AttackHit[0],
            };
            combat.SuperGaugeCost = 50 * CombatTuning.GaugePerPercent;
            return config;
        }

        /// <summary>
        /// STORM RUNNER（docs/14 1.2。ユーザーの案、2026-10-15。C-3、C-10〜C-13 の回答、2026-10-16）。速さで戦うキャラクター。
        /// - 地上技 STORM DASH: 向いている方向へ走り続ける（SkillTuning.Run。初速 12.5 m/s、毎秒 +6、最大 28。2026-10-16 に速くした）。
        ///   別の行動をするまで終わらない。
        ///   足場が途切れたら・壁にぶつかったら浅い角度（20°）で跳ぶ。逆向きに入力すると、反転の後に初速で逆向きに走る。体の判定の
        ///   ダメージは速さに比例（初速で 2）。同じ相手には 0.5 秒に 1 回。
        /// - 空中・横 GALE SLASH: 速さを落とさずに横へ切り抜ける（動きを決めない）。
        /// - 空中・下（撃破技）DOWNBURST: 斜め下へ急降下して蹴る（打つ直前の速さで強くなる）。
        /// - 空中・ニュートラル WHIRLWIND（つむじ風）: 竜巻をまとって回る 3 段（1 段で 1 回転。表示）。
        /// - Q CLOAK（ウインドクローク）: 5 秒間、行動の速さ +20%（移動・ワイヤー・攻撃のフレーム。ヒットストップはそのまま）。効果中に
        ///   もう一度押すと、強化を解き、短く溜めてから照準の方向へ突風を撃つ（反動で少し下がる。押すだけで攻撃の判定はない。
        ///   ユーザーの要望、2026-10-16）。クールタイム 10 秒（効果が終わってから）。
        /// - E SKY ROCKET（スカイロケット）: 照準の方向へ吹く風の帯（長さ 10m、幅 3m）を 10 秒置く。全員と投射物に効く。クールタイム 15 秒。
        /// - R CASTLE（キャッスルインジエアー）: 10 秒間、速さ +50% で、ワイヤーが弾く物にも・何もない空中にもつながる。必要なゲージ 80%。
        /// </summary>
        public static CharacterConfig StormRunner()
        {
            var config = new CharacterConfig();
            CombatTuning combat = config.Combat;

            combat.Ground = new AttackData
            {
                Name = "STORM DASH",
                Run = true,
                TotalTicks = 12,
                WireCancelFromTick = 0,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 4, ActiveTicks = 1, UntilEnd = true, OffsetX = 0.4f, HalfWidth = 1.1f, HalfHeight = 1.2f,
                        Damage = 2, Gauge = 75, HitstopTicks = 6, HitstunTicks = 16, KnockbackX = 11f, KnockbackY = 9f,
                    },
                },
            };

            combat.AirSide = new AttackData
            {
                Name = "GALE SLASH",
                TotalTicks = 22,
                WireCancelFromTick = 6,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 5, ActiveTicks = 6, OffsetX = 1.8f, HalfWidth = 2.2f, HalfHeight = 1.2f,
                        Damage = 2, Gauge = 80, HitstopTicks = 12, HitstunTicks = 22, KnockbackX = 13f, KnockbackY = 5f,
                    },
                },
            };

            combat.AirDown = new AttackData
            {
                Name = "DOWNBURST",
                TotalTicks = 36,
                WireCancelFromTick = 12,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 12, ActiveTicks = 10, OffsetX = 1.2f, OffsetY = -1.5f, HalfWidth = 1.8f, HalfHeight = 1.8f,
                        Damage = 2, Gauge = 85, HitstopTicks = 20, HitstunTicks = 28, KnockbackX = 8f, KnockbackY = -12f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 12, Ticks = 10, VelocityX = 14f, VelocityY = -16f },
                },
            };
            combat.KoAttack = AttackSlot.AirDown;

            combat.AirNeutral = new AttackData
            {
                Name = "WHIRLWIND",
                SpinPerHit = true,
                TotalTicks = 26,
                WireCancelFromTick = 5,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 4, ActiveTicks = 3, OffsetX = 0f, HalfWidth = 2.4f, HalfHeight = 2.4f,
                        Damage = 1, Gauge = 50, HitstopTicks = 6, HitstunTicks = 16, KnockbackX = 2f, KnockbackY = 4f,
                    },
                    new AttackHit
                    {
                        StartTick = 10, ActiveTicks = 3, OffsetX = 0f, HalfWidth = 2.4f, HalfHeight = 2.4f,
                        Damage = 1, Gauge = 50, HitstopTicks = 6, HitstunTicks = 16, KnockbackX = 2f, KnockbackY = 4f,
                    },
                    new AttackHit
                    {
                        StartTick = 16, ActiveTicks = 4, OffsetX = 0f, HalfWidth = 2.6f, HalfHeight = 2.6f,
                        Damage = 1, Gauge = 65, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 7f, KnockbackY = 8f,
                    },
                },
            };

            combat.Special1 = new AttackData
            {
                Name = "CLOAK",
                TotalTicks = 10,
                WireCancelFromTick = 2,
                Effect = SkillEffect.SpeedBoost,
                EffectTick = 0,
                Hits = new AttackHit[0],
            };
            combat.Special1CooldownTicks = 600;

            combat.Special2 = new AttackData
            {
                Name = "SKY ROCKET",
                TotalTicks = 14,
                WireCancelFromTick = 4,
                Effect = SkillEffect.WindZone,
                EffectTick = 2,
                AimHold = true,
                Hits = new AttackHit[0],
            };
            combat.Special2CooldownTicks = 900;

            combat.Super = new AttackData
            {
                Name = "CASTLE",
                TotalTicks = 20,
                WireCancelFromTick = 10,
                Effect = SkillEffect.SkyBoost,
                EffectTick = 0,
                Hits = new AttackHit[0],
            };
            combat.SuperGaugeCost = 80 * CombatTuning.GaugePerPercent;
            return config;
        }

        /// <summary>
        /// SPACE TRAVELER（docs/14 1.1。ユーザーの案、2026-10-15。C-4〜C-9、D-62、D-63 の回答、2026-10-16）。ワープで戦うキャラクター。
        /// - 地上技 MOON HOP: 少しためた後、斜め後ろへ高く飛び上がる。飛び上がる瞬間に前へ攻撃の判定を出し、当たった相手を強く上へ
        ///   飛ばす（ユーザーの要望、2026-10-16）。その後、別の移動をするまでゆっくり落ちる（毎秒 3m。左右には動ける。C-4）。
        /// - 空中・横 BLINK KICK: 前へ 2m ほど瞬間移動してから蹴る。
        /// - 空中・下（撃破技）GRAVITY DROP: ためた後、真下へ重く落ちて叩きつける（打つ直前の速さで強くなる。D-38）。
        /// - 空中・ニュートラル STARDUST: 体の周りに星を散らす 2 段。1 段目は相手を少し引き寄せる（コンボの始めに）。
        /// - Q PORTAL（ワープポータル）: 自分のいる位置に 30 秒のポータルを置く（半径 1.2m。壊せない）。自分のものは 2 つまで（3 つ目で
        ///   いちばん古いものが消える）。片方に入るともう片方へ移る（全員と投射物。速度はそのまま。出た直後の 0.5 秒は移らない）。
        ///   置いた直後は自分が入らず、長押ししてから離すと、置いたポータルにそのまま入る（ユーザーの要望、2026-10-16）。2 回まで続けて
        ///   使え、減っている間は 10 秒ごとに 1 つ戻る（C-6）。
        /// - E MISSILE（スペースミサイル）: 長押しで狙い（狙いの表示）、離すと照準の方向へまっすぐ飛ぶミサイル（18 m/s）。触れたものか
        ///   ステージの外で爆発する（D-63）。威力は最低 2、飛んだ 1 秒ごとに +200%。壊せる物は壊す。クールタイム 6 秒。
        /// - R COSMIC RAY（コズミックレイ）: 自分のポータル 2 つの間に、壁を貫通するビーム（太さ 1.5m）を撃つ（撃つ間は止まる）。
        ///   10 ティックごとに 5 ダメージを 6 回。撃ち終わったときにビームの線が拠点に触れていれば、手持ちのコインを全部確定する（C-9）。
        ///   ポータルが 2 つないと出せない。必要なゲージ 100%（D-62）。
        /// </summary>
        public static CharacterConfig SpaceTraveler()
        {
            var config = new CharacterConfig();
            CombatTuning combat = config.Combat;

            combat.Ground = new AttackData
            {
                Name = "MOON HOP",
                TotalTicks = 26,
                WireCancelFromTick = 12,
                Effect = SkillEffect.SlowFall,
                EffectTick = 10,
                Hits = new[]
                {
                    // 飛び上がる瞬間の、前への判定。吹き飛びは強い上向き。
                    new AttackHit
                    {
                        StartTick = 9, ActiveTicks = 4, OffsetX = 1.6f, OffsetY = 0.3f, HalfWidth = 1.6f, HalfHeight = 1.75f,
                        Damage = 2, Gauge = 80, HitstopTicks = 10, HitstunTicks = 26, KnockbackX = 3f, KnockbackY = 17f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 0, Ticks = 9, VelocityX = 0f, VelocityY = 0f },
                    new AttackMotion { StartTick = 9, Ticks = 1, VelocityX = -7f, VelocityY = 16.5f },
                },
            };

            combat.AirSide = new AttackData
            {
                Name = "BLINK KICK",
                TotalTicks = 24,
                WireCancelFromTick = 7,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 6, ActiveTicks = 5, OffsetX = 1.4f, HalfWidth = 1.6f, HalfHeight = 1.3f,
                        Damage = 2, Gauge = 75, HitstopTicks = 12, HitstunTicks = 22, KnockbackX = 12f, KnockbackY = 6f,
                    },
                },
                Motions = new[]
                {
                    // 1 ティックで 2m 進む（瞬間移動。地形は通り抜けない）。その後はゆっくり。
                    new AttackMotion { StartTick = 4, Ticks = 1, VelocityX = 120f, VelocityY = 0f },
                    new AttackMotion { StartTick = 5, Ticks = 1, VelocityX = 4f, VelocityY = 0f },
                },
            };

            combat.AirDown = new AttackData
            {
                Name = "GRAVITY DROP",
                TotalTicks = 40,
                WireCancelFromTick = 16,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 16, ActiveTicks = 14, OffsetX = 0f, OffsetY = -1.2f, HalfWidth = 1.4f, HalfHeight = 1.8f,
                        Damage = 3, Gauge = 85, HitstopTicks = 20, HitstunTicks = 28, KnockbackX = 3f, KnockbackY = -14f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 0, Ticks = 16, VelocityX = 0f, VelocityY = 0f },
                    new AttackMotion { StartTick = 16, Ticks = 14, VelocityX = 0f, VelocityY = -26f },
                },
            };
            combat.KoAttack = AttackSlot.AirDown;

            combat.AirNeutral = new AttackData
            {
                Name = "STARDUST",
                TotalTicks = 22,
                WireCancelFromTick = 5,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 4, ActiveTicks = 4, OffsetX = 0f, HalfWidth = 2.4f, HalfHeight = 2.4f,
                        Damage = 1, Gauge = 50, HitstopTicks = 8, HitstunTicks = 18, KnockbackX = -3f, KnockbackY = 3f,
                    },
                    new AttackHit
                    {
                        StartTick = 12, ActiveTicks = 4, OffsetX = 0f, HalfWidth = 2.6f, HalfHeight = 2.6f,
                        Damage = 1, Gauge = 65, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 6f, KnockbackY = 7f,
                    },
                },
            };

            combat.Special1 = new AttackData
            {
                Name = "PORTAL",
                TotalTicks = 10,
                WireCancelFromTick = 2,
                Effect = SkillEffect.Portal,
                EffectTick = 0,
                HoldMaxTicks = 30,
                Hits = new AttackHit[0],
            };
            combat.Special1CooldownTicks = 600;
            combat.Special1MaxCharges = 2;

            combat.Special2 = new AttackData
            {
                Name = "MISSILE",
                TotalTicks = 16,
                WireCancelFromTick = 4,
                Effect = SkillEffect.Missile,
                EffectTick = 2,
                HoldMaxTicks = 600,
                AimHold = true,
                Hits = new AttackHit[0],
            };
            combat.Special2CooldownTicks = 360;

            combat.Super = new AttackData
            {
                Name = "COSMIC RAY",
                TotalTicks = 80,
                WireCancelFromTick = 72,
                Effect = SkillEffect.CosmicRay,
                EffectTick = 10,
                Hits = new AttackHit[0],
                Motions = new[]
                {
                    // 撃つ間は止まる（C-9）。
                    new AttackMotion { StartTick = 0, Ticks = 72, VelocityX = 0f, VelocityY = 0f },
                },
            };
            combat.SuperGaugeCost = 100 * CombatTuning.GaugePerPercent;
            return config;
        }
        /// <summary>
        /// PASTRY CHEF（仮の名前。docs/14 1.4。ユーザーの案、2026-10-21）。お菓子で足場と罠を作るキャラクター。
        /// - 地上技 SPATULA SPIN: ゴムベラを持って回り、前 → 後ろの順に攻撃し、最後に回転の勢いで前の斜め上 60° へ飛び上がって攻撃する
        ///   （技の後もかなりの慣性で飛ぶ）。
        /// - 空中 N PALETTE KNIVES: 両手のパレットナイフを横に広げて斜めに一回転。斜めに伸びた判定（AttackHit.AngleDeg）。
        /// - 空中横 HAND MIXER: ハンドミキサーを回して攻撃。細い判定の多段ヒット。
        /// - 空中下（撃破技）DIGITAL SCALE: ボウルを乗せたデジタルスケールを落とす。落ちていた時間でグラム数・威力・吹き飛びが増え、
        ///   1000g 以上で当てると FATAL（PastryRules.StepScale）。
        /// - 空中上 BOWL GUARD: ボウルを持って手を上に伸ばす。判定は少し弱いが、判定が出ている間、上から来た投射物を防ぐ（GUARD）。
        /// - Q フラッフィベイス: わたがしの足場（10 秒）。短く押すと足元に上向き、長押しすると照準の方向を向けて目の前に置く。触れた人を
        ///   弾き飛ばす。ワイヤーはつながらない。クールタイム 8 秒。
        /// - E スイートトラップ: クリームを照準の方向へ扇形に飛ばし、当たった地形に 10 秒張り付かせる。長押しの間にワイヤーでオレンジ、
        ///   攻撃で抹茶（何も押さない・短く押すとオレンジ）。オレンジは速さ +30%・壁ジャンプ 1.5 倍・ワイヤーがつながる。抹茶は速さ −30%・
        ///   壁と天井に 1 秒張り付く・壁に張り付けず、ワイヤーもつながらない。クールタイム 18 秒。
        /// - R ティーパーティ: マカロン 6 個を次々と上へ投げる（緑 60% 威力 6、黄 35% 威力 12、虹 5% 威力 30）。必要なゲージ 100%。
        /// </summary>
        public static CharacterConfig PastryChef()
        {
            var config = new CharacterConfig();
            CombatTuning combat = config.Combat;

            combat.Ground = new AttackData
            {
                Name = "SPATULA SPIN",
                TotalTicks = 42,
                WireCancelFromTick = 20,
                SpinPerHit = true,
                Hits = new[]
                {
                    // 前、後ろ、飛び上がりながら前の斜め上。
                    new AttackHit
                    {
                        StartTick = 5, ActiveTicks = 4, OffsetX = 1.6f, HalfWidth = 1.6f, HalfHeight = 1.5f,
                        Damage = 1, Gauge = 65, HitstopTicks = 9, HitstunTicks = 22, KnockbackX = 5f, KnockbackY = 9f,
                    },
                    new AttackHit
                    {
                        StartTick = 12, ActiveTicks = 4, OffsetX = -1.6f, HalfWidth = 1.6f, HalfHeight = 1.5f,
                        Damage = 1, Gauge = 65, HitstopTicks = 9, HitstunTicks = 22, KnockbackX = -6f, KnockbackY = 9f,
                    },
                    new AttackHit
                    {
                        StartTick = 19, ActiveTicks = 6, OffsetX = 1f, OffsetY = 1f, HalfWidth = 1.8f, HalfHeight = 1.8f,
                        Damage = 2, Gauge = 85, HitstopTicks = 12, HitstunTicks = 26, KnockbackX = 10f, KnockbackY = 13f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 0, Ticks = 19, VelocityX = 3f, VelocityY = 0f },
                    // 前の斜め上 60° へ 20 m/s（その後は慣性のまま飛ぶ）。
                    new AttackMotion { StartTick = 19, Ticks = 1, VelocityX = 10f, VelocityY = 17.3f },
                },
            };

            combat.AirNeutral = new AttackData
            {
                Name = "PALETTE KNIVES",
                TotalTicks = 28,
                WireCancelFromTick = 6,
                ExclusiveHits = true,
                Hits = new[]
                {
                    // 横に広げたナイフが、斜めに一回転する（斜めの細長い判定が、向きを変える）。
                    new AttackHit
                    {
                        StartTick = 6, ActiveTicks = 5, OffsetX = 0f, OffsetY = 0.2f, HalfWidth = 2.6f, HalfHeight = 0.35f, AngleDeg = 35f,
                        Damage = 2, Gauge = 70, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 7f, KnockbackY = 8f,
                    },
                    new AttackHit
                    {
                        StartTick = 11, ActiveTicks = 5, OffsetX = 0f, OffsetY = 0.2f, HalfWidth = 2.6f, HalfHeight = 0.35f, AngleDeg = -35f,
                        Damage = 2, Gauge = 70, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 8f, KnockbackY = 6f,
                    },
                },
            };

            combat.AirSide = new AttackData
            {
                Name = "HAND MIXER",
                TotalTicks = 28,
                WireCancelFromTick = 6,
                Hits = new[]
                {
                    // 細い判定の多段ヒット。最後だけ吹き飛ばす。
                    new AttackHit
                    {
                        StartTick = 5, ActiveTicks = 2, OffsetX = 1.9f, HalfWidth = 1.5f, HalfHeight = 0.35f,
                        Damage = 1, Gauge = 40, HitstopTicks = 4, HitstunTicks = 14, KnockbackX = 1f, KnockbackY = 0.5f,
                    },
                    new AttackHit
                    {
                        StartTick = 9, ActiveTicks = 2, OffsetX = 1.9f, HalfWidth = 1.5f, HalfHeight = 0.35f,
                        Damage = 1, Gauge = 40, HitstopTicks = 4, HitstunTicks = 14, KnockbackX = 1f, KnockbackY = 0.5f,
                    },
                    new AttackHit
                    {
                        StartTick = 13, ActiveTicks = 2, OffsetX = 1.9f, HalfWidth = 1.5f, HalfHeight = 0.35f,
                        Damage = 1, Gauge = 40, HitstopTicks = 4, HitstunTicks = 14, KnockbackX = 1f, KnockbackY = 0.5f,
                    },
                    new AttackHit
                    {
                        StartTick = 17, ActiveTicks = 3, OffsetX = 1.9f, HalfWidth = 1.5f, HalfHeight = 0.4f,
                        Damage = 1, Gauge = 65, HitstopTicks = 8, HitstunTicks = 22, KnockbackX = 10f, KnockbackY = 5f,
                    },
                },
                Motions = new[]
                {
                    // 回している間は、その場に少しとどまる（多段ヒットが離れないように）。
                    new AttackMotion { StartTick = 3, Ticks = 17, VelocityX = 1f, VelocityY = 0f },
                },
            };

            combat.AirDown = new AttackData
            {
                Name = "DIGITAL SCALE",
                TotalTicks = 26,
                WireCancelFromTick = 10,
                Effect = SkillEffect.DropScale,
                EffectTick = 6,
                Hits = new AttackHit[0],
                Motions = new[]
                {
                    // 落とす間、少しとどまる。
                    new AttackMotion { StartTick = 0, Ticks = 7, VelocityX = 0f, VelocityY = 1.5f },
                },
            };
            combat.KoAttack = AttackSlot.AirDown;

            combat.AirUp = new AttackData
            {
                Name = "BOWL GUARD",
                TotalTicks = 26,
                WireCancelFromTick = 6,
                BlocksProjectilesAbove = true,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 4, ActiveTicks = 16, OffsetX = 0.2f, OffsetY = 1.7f, HalfWidth = 1.5f, HalfHeight = 0.9f,
                        Damage = 1, Gauge = 65, HitstopTicks = 7, HitstunTicks = 18, KnockbackX = 2f, KnockbackY = 10f,
                    },
                },
            };

            combat.Special1 = new AttackData
            {
                Name = "FLUFFY BASE",
                TotalTicks = 12,
                WireCancelFromTick = 2,
                Effect = SkillEffect.CottonCandy,
                EffectTick = 0,
                HoldMaxTicks = 600,
                Hits = new AttackHit[0],
            };
            combat.Special1CooldownTicks = 480;

            combat.Special2 = new AttackData
            {
                Name = "SWEET TRAP",
                TotalTicks = 18,
                WireCancelFromTick = 4,
                Effect = SkillEffect.Cream,
                EffectTick = 3,
                HoldMaxTicks = 600,
                Hits = new AttackHit[0],
            };
            combat.Special2CooldownTicks = 1080;

            TeaPartyTuning party = config.Skills.TeaParty;
            combat.Super = new AttackData
            {
                Name = "TEA PARTY",
                TotalTicks = 4 + (party.Count - 1) * party.IntervalTicks + 12,
                WireCancelFromTick = 4 + (party.Count - 1) * party.IntervalTicks,
                Effect = SkillEffect.TeaParty,
                EffectTick = 4,
                Hits = new AttackHit[0],
            };
            combat.SuperGaugeCost = 100 * CombatTuning.GaugePerPercent;
            return config;
        }
        /// <summary>
        /// SOLDERER（docs/14 1.5。ユーザーの案、2026-10-21）。電気がコンセプトで、素早く動いて軽い一撃を当て続けるヒット＆アウェイ。
        /// - 地上技 THUNDER KICK: 地面を蹴って真上に高く跳ぶ。蹴るときに雷の衝撃波で前後を攻撃する。
        /// - 空中 N DISCHARGE: 大の字で全身から放電。持続は長いが弱い。
        /// - 空中横（撃破技）FINGER SPARK: 指先を斜め下に伸ばして攻撃。指先のクリティカル判定（出た瞬間だけ）が相手の体の中心に重なった
        ///   ときだけ FATAL（威力と吹き飛びが大きく増える）。速さでは FATAL にならない。持続は長い。
        /// - 空中下 STOMP: 踏みつけ。当たると反動で真上に跳ぶ。
        /// - 空中上 THUNDER FLIP: 雷をまとった両足でサマーソルト（前の下 → 上 → 後ろ）。
        /// - Q INAZUMA STEP: 短く押すと照準の方向へ短く突撃（威力 3）。3 秒以内なら続けて出せ、合わせて 3 回まで（突撃するたびに 3 秒に
        ///   戻る）。長押しすると、長押しになった瞬間に回りながら長く突撃（威力 6）し、回数に関係なく終わる。クールタイムは突撃した回数 × 6 秒（6/12/18 秒）。
        /// - E INVERTER: ON・OFF（クールタイム 0.5 秒）。ON の間はワイヤーの射程が 1/3 になり、雷を帯びる（外したワイヤーを押したままで
        ///   鞭（押している間は攻撃しても続く）、つなぐと雷の波動（3 秒に 1 回）。ElectricRules）。
        /// - R BALL LIGHTNING: 遅い電気の球を 3 つ、それぞれ放つ瞬間の照準の方向へ放つ（壁で跳ね返り、止まると 5 秒残る罠）。触れると感電。必要なゲージ 90%。
        /// </summary>
        public static CharacterConfig Solderer()
        {
            var config = new CharacterConfig();
            config.Movement.RunSpeed = 9.5f;
            config.Movement.AirAcceleration = 36f;
            CombatTuning combat = config.Combat;

            combat.Ground = new AttackData
            {
                Name = "THUNDER KICK",
                TotalTicks = 30,
                WireCancelFromTick = 6,
                Hits = new[]
                {
                    // 蹴る瞬間の、前と後ろの雷の衝撃波。
                    new AttackHit
                    {
                        StartTick = 4, ActiveTicks = 4, OffsetX = 1.6f, HalfWidth = 1.5f, HalfHeight = 1.4f,
                        Damage = 1, Gauge = 70, HitstopTicks = 8, HitstunTicks = 20, KnockbackX = 6f, KnockbackY = 10f,
                    },
                    new AttackHit
                    {
                        StartTick = 4, ActiveTicks = 4, OffsetX = -1.6f, HalfWidth = 1.5f, HalfHeight = 1.4f,
                        Damage = 1, Gauge = 70, HitstopTicks = 8, HitstunTicks = 20, KnockbackX = -6f, KnockbackY = 10f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 0, Ticks = 4, VelocityX = 0f, VelocityY = 0f },
                    new AttackMotion { StartTick = 4, Ticks = 1, VelocityX = 0f, VelocityY = 21f },
                },
            };

            combat.AirNeutral = new AttackData
            {
                Name = "DISCHARGE",
                TotalTicks = 32,
                WireCancelFromTick = 6,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 4, ActiveTicks = 20, OffsetX = 0f, HalfWidth = 2f, HalfHeight = 2f,
                        Damage = 1, Gauge = 55, HitstopTicks = 5, HitstunTicks = 16, KnockbackX = 3f, KnockbackY = 5f,
                    },
                },
            };

            combat.AirSide = new AttackData
            {
                Name = "FINGER SPARK",
                TotalTicks = 34,
                WireCancelFromTick = 7,
                ExclusiveHits = true,
                Hits = new[]
                {
                    // 指先のクリティカル判定（出た瞬間だけ。相手の体の中心に重なったときだけ当たる）。
                    new AttackHit
                    {
                        StartTick = 6, ActiveTicks = 1, OffsetX = 2.3f, OffsetY = -1.1f, HalfWidth = 0.35f, HalfHeight = 0.35f,
                        Critical = true, CriticalScale = 2.5f,
                        Damage = 3, Gauge = 85, HitstopTicks = 20, HitstunTicks = 32, KnockbackX = 12f, KnockbackY = -4f,
                    },
                    // 斜め下に伸ばした腕の通常の判定（持続は長い）。
                    new AttackHit
                    {
                        StartTick = 6, ActiveTicks = 14, OffsetX = 1.5f, OffsetY = -0.7f, HalfWidth = 1.2f, HalfHeight = 0.45f, AngleDeg = -35f,
                        Damage = 2, Gauge = 65, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 9f, KnockbackY = -2f,
                    },
                },
            };
            combat.KoAttack = AttackSlot.AirSide;

            combat.AirDown = new AttackData
            {
                Name = "STOMP",
                TotalTicks = 32,
                WireCancelFromTick = 8,
                // 当たったら反動で跳び、そこで攻撃を終える（跳んだ後に降下の動きが残らないように。ユーザーの指摘、2026-10-22）。
                EndsOnHit = true,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 6, ActiveTicks = 10, OffsetX = 0f, OffsetY = -1.4f, HalfWidth = 1.2f, HalfHeight = 0.9f,
                        Damage = 2, Gauge = 75, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 2f, KnockbackY = -12f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 4, Ticks = 12, VelocityX = 0f, VelocityY = -18f },
                    // 当たったら、反動で真上に跳ぶ（EndsOnHit で 1 回だけつける）。
                    new AttackMotion { StartTick = 6, Ticks = 1, VelocityX = 0f, VelocityY = 16f, RequiresHit = true },
                },
            };

            combat.AirUp = new AttackData
            {
                Name = "THUNDER FLIP",
                // 後隙を長く（続けて出しても少しずつ上がり続けないように。ユーザーの指摘、2026-10-22）。
                TotalTicks = 38,
                WireCancelFromTick = 6,
                ExclusiveHits = true,
                Hits = new[]
                {
                    // サマーソルト: 前の下 → 頭の上 → 後ろ。
                    new AttackHit
                    {
                        StartTick = 4, ActiveTicks = 3, OffsetX = 1.4f, OffsetY = -0.4f, HalfWidth = 1.2f, HalfHeight = 1f,
                        Damage = 1, Gauge = 70, HitstopTicks = 9, HitstunTicks = 22, KnockbackX = 3f, KnockbackY = 13f,
                    },
                    new AttackHit
                    {
                        StartTick = 7, ActiveTicks = 3, OffsetX = 0.3f, OffsetY = 1.6f, HalfWidth = 1.3f, HalfHeight = 1f,
                        Damage = 1, Gauge = 75, HitstopTicks = 9, HitstunTicks = 22, KnockbackX = 1f, KnockbackY = 14f,
                    },
                    new AttackHit
                    {
                        StartTick = 10, ActiveTicks = 3, OffsetX = -1.2f, OffsetY = 0.6f, HalfWidth = 1.2f, HalfHeight = 1f,
                        Damage = 1, Gauge = 70, HitstopTicks = 9, HitstunTicks = 22, KnockbackX = -2f, KnockbackY = 13f,
                    },
                },
                Motions = new[]
                {
                    new AttackMotion { StartTick = 2, Ticks = 1, VelocityX = 1f, VelocityY = 7f },
                },
            };

            combat.Special1 = new AttackData
            {
                Name = "INAZUMA STEP",
                TotalTicks = 11,
                ChargedTotalTicks = 21,
                WireCancelFromTick = 4,
                HoldMaxTicks = 600,
                ChargedFromTicks = 10,
                FiresWhenCharged = true,
                AimTurnsFacing = true,
                ChainMax = 3,
                ChainWindowTicks = 180,
                Hits = new[]
                {
                    new AttackHit
                    {
                        StartTick = 1, ActiveTicks = 6, OffsetX = 0f, HalfWidth = 1f, HalfHeight = 1.2f, Charge = ChargeFilter.Tapped,
                        Damage = 3, Gauge = 80, HitstopTicks = 8, HitstunTicks = 20, KnockbackX = 8f, KnockbackY = 6f,
                    },
                    new AttackHit
                    {
                        StartTick = 2, ActiveTicks = 11, OffsetX = 0f, HalfWidth = 1.4f, HalfHeight = 1.5f, Charge = ChargeFilter.Charged,
                        Damage = 6, Gauge = 115, HitstopTicks = 14, HitstunTicks = 28, KnockbackX = 13f, KnockbackY = 9f,
                    },
                },
                Motions = new[]
                {
                    // 照準の方向へ: 短く（2.4m。SPACE TRAVELER の空中横の約 2m より少し長く）、長押しなら長く（5.5m）。突撃の後の慣性は
                    // そのまま残る（ユーザーの要望で縮めた、2026-10-22。前は 3.6m・8m）。
                    new AttackMotion { StartTick = 0, Ticks = 6, VelocityX = 24f, AlongAim = true, Charge = ChargeFilter.Tapped },
                    new AttackMotion { StartTick = 0, Ticks = 11, VelocityX = 30f, AlongAim = true, Charge = ChargeFilter.Charged },
                },
            };
            combat.Special1CooldownTicks = 360;

            combat.Special2 = new AttackData
            {
                Name = "INVERTER",
                TotalTicks = 6,
                WireCancelFromTick = 1,
                Effect = SkillEffect.Inverter,
                EffectTick = 0,
                Hits = new AttackHit[0],
            };
            combat.Special2CooldownTicks = 30;

            BallLightningTuning balls = config.Skills.BallLightning;
            combat.Super = new AttackData
            {
                Name = "BALL LIGHTNING",
                TotalTicks = 2 + (balls.Count - 1) * balls.IntervalTicks + 8,
                WireCancelFromTick = 2 + (balls.Count - 1) * balls.IntervalTicks,
                Effect = SkillEffect.BallLightning,
                EffectTick = 2,
                TracksAim = true,
                Hits = new AttackHit[0],
            };
            combat.SuperGaugeCost = 90 * CombatTuning.GaugePerPercent;
            return config;
        }

        /// <summary>
        /// THIEF（docs/14 1.6。ユーザーの案、2026-10-28）。盗む・コイン重視・変則的。決まりは ThiefRules、値は ThiefTuning。
        /// - 地上技 SMOKE BOMB: 真下に煙球を投げながら、斜め後ろに飛び上がる（煙球は着弾すると、ごく狭い範囲に威力 5 と弱い吹き飛び、
        ///   狭い範囲に煙。煙の中の物は見えない）。
        /// - 空中 N ROLLING CUT: 慣性のまま前転し、体の周りを円を描くように小さな判定が回る。
        /// - 空中上（撃破技）FIVE STABS: 慣性のまま、ナイフで真上を 5 回突く。体の幅を 5 つに分けて、2・4・1・5・3 の順に短く細い判定を
        ///   頭上に出す。威力は 4 × 4（ごく弱い吹き飛び）＋ 最後が 14（中くらいの吹き飛び）。1〜4 発目を外して 5 発目だけを当てると FATAL で、
        ///   最後が威力 80・大きな吹き飛び（ユーザーの案）。
        /// - 空中横 UPRIGHT SLASH: 慣性のまま、少しためてから前を薙ぎ払う（横に狭く縦に広い弧）。
        /// - 空中下 LOW SWEEP: 慣性のまま、少しためてから下を薙ぎ払う（縦に狭く横に広い弧）。
        /// - Q TALENT SNATCH: 照準の方向へ、こげ茶色の小さな弾をとても速く撃つ（威力・吹き飛びなし）。当たった人の Q をコピーし、ステージの
        ///   ギミック・仕掛けに当てると奪う。もう一度 Q で使う・置く。クールタイム 12 秒。
        /// - E MISDIRECTION: 分身を残して透明になり、照準の方向へごく短く突進する（3 秒）。クールタイム 20 秒。
        /// - R BANK HEIST: 拠点の近くで、その場で止まって詠唱し、もう一度 R で止めると詠唱した長さでコインを得る。ゲージ 80%。
        /// </summary>
        public static CharacterConfig Thief()
        {
            var config = new CharacterConfig();
            CombatTuning combat = config.Combat;
            float column = config.Movement.HalfWidth * 2f / 5f;
            float overHead = config.Movement.HalfHeight + 0.55f;

            combat.Ground = new AttackData
            {
                Name = "SMOKE BOMB",
                TotalTicks = 30,
                WireCancelFromTick = 6,
                Effect = SkillEffect.SmokeBall,
                EffectTick = 3,
                Hits = new AttackHit[0],
                Motions = new[]
                {
                    // 投げながら、斜め後ろへ飛び上がる。
                    new AttackMotion { StartTick = 3, Ticks = 1, VelocityX = -9f, VelocityY = 15f },
                },
            };

            combat.AirNeutral = new AttackData
            {
                Name = "ROLLING CUT",
                TotalTicks = 26,
                WireCancelFromTick = 5,
                ExclusiveHits = true,
                Flip = true,
                Hits = new[]
                {
                    // 前転: 頭の上 → 前 → 足の下 → 後ろ と、体の周りを小さな判定が回る。
                    RollHit(4, 0f, 1.35f),
                    RollHit(7, 1.25f, 0.1f),
                    RollHit(10, 0f, -1.35f),
                    RollHit(13, -1.25f, 0.1f),
                },
            };

            combat.AirUp = new AttackData
            {
                Name = "FIVE STABS",
                TotalTicks = 34,
                WireCancelFromTick = 6,
                Hits = new[]
                {
                    // 体の幅を 5 つに分けた列（左から 1〜5）の、2・4・1・5・3 の順に、短く細い判定を頭上に出す。
                    Stab(5, 2, column, overHead, last: false),
                    Stab(8, 4, column, overHead, last: false),
                    Stab(11, 1, column, overHead, last: false),
                    Stab(14, 5, column, overHead, last: false),
                    Stab(17, 3, column, overHead, last: true),
                },
            };
            combat.KoAttack = AttackSlot.AirUp;

            combat.AirSide = new AttackData
            {
                Name = "UPRIGHT SLASH",
                TotalTicks = 30,
                WireCancelFromTick = 10,
                ExclusiveHits = true,
                Hits = new[]
                {
                    // 少しためてから、前を上から下へ（横に狭く縦に広い弧）。
                    Slash(10, 0.95f, 1.25f, 0.5f, 0.65f, 9f, 6f),
                    Slash(12, 1.3f, 0f, 0.5f, 0.75f, 11f, 3f),
                    Slash(14, 0.95f, -1.25f, 0.5f, 0.65f, 9f, -2f),
                },
            };

            combat.AirDown = new AttackData
            {
                Name = "LOW SWEEP",
                TotalTicks = 30,
                WireCancelFromTick = 10,
                ExclusiveHits = true,
                Hits = new[]
                {
                    // 少しためてから、下を前から後ろへ（縦に狭く横に広い弧）。
                    Slash(10, 1.25f, -1.0f, 0.7f, 0.5f, 5f, -8f),
                    Slash(12, 0f, -1.35f, 0.8f, 0.5f, 2f, -11f),
                    Slash(14, -1.25f, -1.0f, 0.7f, 0.5f, -5f, -8f),
                },
            };

            combat.Special1 = new AttackData
            {
                Name = "TALENT SNATCH",
                TotalTicks = 16,
                WireCancelFromTick = 4,
                Effect = SkillEffect.TalentSnatch,
                EffectTick = 3,
                AimHold = true,
                Hits = new AttackHit[0],
            };
            combat.Special1CooldownTicks = 720;

            combat.Special2 = new AttackData
            {
                Name = "MISDIRECTION",
                TotalTicks = 10,
                WireCancelFromTick = 6,
                Effect = SkillEffect.Misdirection,
                EffectTick = 0,
                AimTurnsFacing = true,
                AimHold = true,
                Hits = new AttackHit[0],
                Motions = new[]
                {
                    // 照準の方向へ、ごく短く（約 1.3m。その後の慣性で地上なら合わせて約 2m）。分身を残したティック（0）の次から。
                    new AttackMotion { StartTick = 1, Ticks = 5, VelocityX = 16f, AlongAim = true },
                },
            };
            combat.Special2CooldownTicks = 1200;

            combat.Super = new AttackData
            {
                Name = "BANK HEIST",
                TotalTicks = 6,
                WireCancelFromTick = 6,
                Effect = SkillEffect.BankHeist,
                EffectTick = 0,
                Hits = new AttackHit[0],
                Motions = new[]
                {
                    new AttackMotion { StartTick = 0, Ticks = 6, VelocityX = 0f, VelocityY = 0f },
                },
            };
            combat.SuperGaugeCost = 80 * CombatTuning.GaugePerPercent;
            return config;
        }

        /// <summary>
        /// JUGGERNAUT（docs/14 1.7。ユーザーの案、2026-10-29）。苔色の戦車。地上で強く、空中では鈍い（#地上戦 #鈍重）。
        /// - 体: ふつうより大きい（見た目と当たり）。地上の速さ +50%、空中とワイヤーの間の速さ -50%、ワイヤーの射程 1.5 倍。ジャンプは
        ///   発生が遅く、跳ぶ高さが半分（速さ 1/√2。ユーザーの回答、2026-10-29）。ガードのクールタイム 2 倍、押し続けると 2 秒まで延び、
        ///   地上ならガードしながらふだんの速さで歩ける（ユーザーの回答）。
        /// - 地上技（撃破技）TURRET SPIN: 回って、体に判定。回りながら左右に動ける。押し続けると回り続け、とてもゆっくり上がる（離すと
        ///   終わる）。回る砲塔の先が出始めの一瞬に当たると FATAL で、威力と吹き飛びが 2 倍（その後はふつうのヒット）。
        /// - 空中 N HULL ROLL: ゆっくり縦に回り、体に判定。空中上 SKY BURST: 頭上で爆発（上向きの判定。反動で少し下がる）。空中横 CANNON SHOT:
        ///   前へ重力を受けない砲弾（威力 10）を撃ち、反動で後ろへ下がる。空中下 GROUND BURST: 足もとで爆発（下向きの判定。反動で少し
        ///   上がる）。どれも発生が遅く、後隙が長い。
        /// - Q FLAK: 地上で構えて、構えている間に Q で重力のある砲弾を照準の方向へ撃つ（爆発。威力 15・強い吹き飛び）。3 回まで、1 回分は
        ///   4 秒で戻る。上下で弾道を弱・中・強・弩に（初めは強。試合の間覚えておく）。
        /// - E MINE TRAP: 照準の方向へ地雷を撃ち（重力で山なりに飛ぶ）、地形かキャラクターに張り付く。もう一度 E で爆発（威力 30）。クールタイム 10 秒（爆発の後から）。
        /// - R MAYDAY: ジェット機で飛び去り、ステージ全体から空いた場所を選んで運ばれる（その間は無敵。10 秒で元の場所へ）。ゲージ 100%。
        /// 技の規則は JuggernautRules、技の値は SkillTuning.Juggernaut。
        /// </summary>
        public static CharacterConfig Juggernaut()
        {
            var config = new CharacterConfig();
            MovementTuning movement = config.Movement;
            float baseRun = movement.RunSpeed;
            movement.HalfWidth = 0.75f;
            movement.HalfHeight = 0.95f;
            movement.RunSpeed = baseRun * 1.5f;
            movement.AirRunSpeed = baseRun * 0.5f;
            // 跳ぶ高さは速さの 2 乗に比例するので、高さを半分にするには速さを 1/√2 にする。
            movement.JumpSpeed *= 0.7071f;
            movement.WallJumpSpeedY *= 0.7071f;
            movement.JumpStartupTicks = 8;

            WireTuning wire = config.Wire;
            wire.MaxLength *= 1.5f;
            wire.MaxAttachedSpeed *= 0.5f;
            wire.ExitSpeedCap *= 0.5f;
            wire.Inertial.StartPullSpeed *= 0.5f;
            wire.Inertial.ReelSpeed *= 0.5f;
            wire.Inertial.ReelOutSpeed *= 0.5f;
            wire.Inertial.SwingAcceleration *= 0.5f;
            wire.Inertial.HoldSwingAcceleration *= 0.5f;
            wire.Responsive.PullSpeed *= 0.5f;
            wire.Responsive.SteerSpeed *= 0.5f;

            CombatTuning combat = config.Combat;
            combat.GuardCooldownTicks *= 2;
            // 押し続けて延ばせるのは 2 秒まで（ユーザーの要望、2026-10-29: 4 秒から下方修正）。
            combat.GuardHoldMaxTicks = 120;
            combat.GuardWalk = true;

            float side = movement.HalfWidth;
            float head = movement.HalfHeight;
            combat.Ground = new AttackData
            {
                Name = "TURRET SPIN",
                TotalTicks = 40,
                WireCancelFromTick = 20,
                Mobile = true,
                HeldSustain = true,
                SustainRiseFromTicks = 40,
                SustainMaxTicks = 300,
                SustainRiseSpeed = 1.2f,
                ExclusiveHits = true,
                RehitTicks = 20,
                Hits = new[]
                {
                    // 砲塔の先（1 周 20 ティックで前と後ろを行き来する）。出始めの一瞬（4 ティック。前を向いている間）に当たれば FATAL で 2 倍、
                    // その後はふつうのヒット（ユーザーの要望、2026-10-29）。体の判定より先に並べる。
                    new AttackHit
                    {
                        StartTick = 4, UntilEnd = true, OffsetX = side + 0.8f, OffsetY = 0.35f, HalfWidth = 0.35f, HalfHeight = 0.35f,
                        SweepTicks = 20, FatalScale = 2f, FatalTicks = 4,
                        Damage = 6, Gauge = 120, HitstopTicks = 12, HitstunTicks = 30, KnockbackX = 12f, KnockbackY = 10f,
                    },
                    // 回っている体。
                    new AttackHit
                    {
                        StartTick = 4, UntilEnd = true, OffsetX = 0f, OffsetY = 0f, HalfWidth = side + 0.2f, HalfHeight = head + 0.1f,
                        Damage = 3, Gauge = 50, HitstopTicks = 8, HitstunTicks = 20, KnockbackX = 7f, KnockbackY = 6f,
                    },
                },
            };
            combat.KoAttack = AttackSlot.Ground;

            combat.AirNeutral = new AttackData
            {
                Name = "HULL ROLL",
                TotalTicks = 52,
                WireCancelFromTick = 34,
                Flip = true,
                Hits = new[]
                {
                    // ゆっくり縦に回る体。
                    new AttackHit
                    {
                        StartTick = 14, ActiveTicks = 18, OffsetX = 0f, OffsetY = 0f, HalfWidth = side + 0.45f, HalfHeight = head + 0.3f,
                        Damage = 6, Gauge = 90, HitstopTicks = 12, HitstunTicks = 26, KnockbackX = 6f, KnockbackY = 10f,
                    },
                },
            };

            combat.AirUp = new AttackData
            {
                Name = "SKY BURST",
                TotalTicks = 54,
                WireCancelFromTick = 34,
                Hits = new[]
                {
                    // 頭上の爆発（上向き）。
                    new AttackHit
                    {
                        StartTick = 16, ActiveTicks = 4, OffsetX = 0f, OffsetY = head + 1f, HalfWidth = 1.4f, HalfHeight = 1f,
                        Damage = 9, Gauge = 110, HitstopTicks = 14, HitstunTicks = 30, KnockbackX = 3f, KnockbackY = 17f,
                    },
                },
                Motions = new[]
                {
                    // 反動で少し下がる。
                    new AttackMotion { StartTick = 16, Ticks = 1, VelocityX = 0f, VelocityY = -6f },
                },
            };

            combat.AirSide = new AttackData
            {
                Name = "CANNON SHOT",
                TotalTicks = 50,
                WireCancelFromTick = 32,
                Effect = SkillEffect.Cannon,
                EffectTick = 16,
                Hits = new AttackHit[0],
                Motions = new[]
                {
                    // 撃った反動で後ろへ下がる。
                    new AttackMotion { StartTick = 16, Ticks = 1, VelocityX = -8f, VelocityY = 2f },
                },
            };

            combat.AirDown = new AttackData
            {
                Name = "GROUND BURST",
                TotalTicks = 54,
                WireCancelFromTick = 34,
                Hits = new[]
                {
                    // 足もとの爆発（下向き）。
                    new AttackHit
                    {
                        StartTick = 16, ActiveTicks = 4, OffsetX = 0f, OffsetY = -(head + 1f), HalfWidth = 1.4f, HalfHeight = 1f,
                        Damage = 9, Gauge = 110, HitstopTicks = 14, HitstunTicks = 30, KnockbackX = 3f, KnockbackY = -16f,
                    },
                },
                Motions = new[]
                {
                    // 反動で少し上がる。
                    new AttackMotion { StartTick = 16, Ticks = 1, VelocityX = 0f, VelocityY = 8f },
                },
            };

            combat.Special1 = new AttackData
            {
                Name = "FLAK",
                TotalTicks = 20,
                WireCancelFromTick = 20,
                Effect = SkillEffect.Flak,
                EffectTick = 4,
                Hits = new AttackHit[0],
            };
            combat.Special1CooldownTicks = 240;
            combat.Special1MaxCharges = 3;

            combat.Special2 = new AttackData
            {
                Name = "MINE TRAP",
                TotalTicks = 18,
                WireCancelFromTick = 8,
                Effect = SkillEffect.Mine,
                EffectTick = 4,
                AimHold = true,
                Hits = new AttackHit[0],
            };
            combat.Special2CooldownTicks = 600;

            combat.Super = new AttackData
            {
                Name = "MAYDAY",
                TotalTicks = 21,
                WireCancelFromTick = 21,
                Effect = SkillEffect.Mayday,
                EffectTick = 20,
                Hits = new AttackHit[0],
                Motions = new[]
                {
                    // ジェット機が来るまで、その場で待つ。
                    new AttackMotion { StartTick = 0, Ticks = 21, VelocityX = 0f, VelocityY = 0f },
                },
            };
            combat.SuperGaugeCost = 100 * CombatTuning.GaugePerPercent;
            return config;
        }

        // 前転の判定 1 つ（小さめ。ユーザーの案）。
        private static AttackHit RollHit(int startTick, float offsetX, float offsetY)
        {
            return new AttackHit
            {
                StartTick = startTick, ActiveTicks = 3, OffsetX = offsetX, OffsetY = offsetY, HalfWidth = 0.6f, HalfHeight = 0.6f,
                Damage = 2, Gauge = 60, HitstopTicks = 8, HitstunTicks = 18, KnockbackX = 4f, KnockbackY = 7f,
            };
        }

        // 頭上の刺突 1 つ（列 index は左から 1〜5。向いている方向が右）。1〜4 発目は威力 4・ごく弱い吹き飛び、5 発目は威力 14・中くらいの
        // 吹き飛びで、前の 4 発をどれも当てずに当てたら FATAL（威力 80・大きな吹き飛び。ユーザーの案）。
        private static AttackHit Stab(int startTick, int index, float column, float overHead, bool last)
        {
            return new AttackHit
            {
                StartTick = startTick, ActiveTicks = 2, OffsetX = (index - 3) * column, OffsetY = overHead,
                HalfWidth = column * 0.35f, HalfHeight = 0.5f,
                Damage = last ? 14 : 4, Gauge = last ? 90 : 40, HitstopTicks = last ? 14 : 4, HitstunTicks = last ? 26 : 16,
                KnockbackX = last ? 2f : 0f, KnockbackY = last ? 14f : 3f,
                LoneFatal = last, LoneFatalDamage = last ? 80 : 0, LoneFatalKnockbackScale = last ? 2.2f : 1f,
            };
        }

        // ナイフの薙ぎ払いの判定 1 つ（弧の一部）。
        private static AttackHit Slash(int startTick, float offsetX, float offsetY, float halfWidth, float halfHeight, float knockbackX,
            float knockbackY)
        {
            return new AttackHit
            {
                StartTick = startTick, ActiveTicks = 2, OffsetX = offsetX, OffsetY = offsetY, HalfWidth = halfWidth, HalfHeight = halfHeight,
                Damage = 3, Gauge = 80, HitstopTicks = 12, HitstunTicks = 24, KnockbackX = knockbackX, KnockbackY = knockbackY,
            };
        }
    }
}
