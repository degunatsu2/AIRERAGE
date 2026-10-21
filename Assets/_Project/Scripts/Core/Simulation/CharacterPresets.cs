using Airerage.Core.Combat;
using Airerage.Core.Skills;

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
        ///   （上限 300 枚）。壊す（30 ダメージ）か 3 分でばらまく。クールタイム 30 秒（壊れてから）。
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
                        Damage = 9, HitstopTicks = 14, HitstunTicks = 26, KnockbackX = 14f, KnockbackY = 9f,
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
        ///   ダメージは速さ × 0.4（初速で 5）。同じ相手には 0.5 秒に 1 回。
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
                        Damage = 5, HitstopTicks = 6, HitstunTicks = 16, KnockbackX = 11f, KnockbackY = 9f,
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
                        Damage = 6, HitstopTicks = 12, HitstunTicks = 22, KnockbackX = 13f, KnockbackY = 5f,
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
                        Damage = 8, HitstopTicks = 20, HitstunTicks = 28, KnockbackX = 8f, KnockbackY = -12f,
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
                        Damage = 2, HitstopTicks = 6, HitstunTicks = 16, KnockbackX = 2f, KnockbackY = 4f,
                    },
                    new AttackHit
                    {
                        StartTick = 10, ActiveTicks = 3, OffsetX = 0f, HalfWidth = 2.4f, HalfHeight = 2.4f,
                        Damage = 2, HitstopTicks = 6, HitstunTicks = 16, KnockbackX = 2f, KnockbackY = 4f,
                    },
                    new AttackHit
                    {
                        StartTick = 16, ActiveTicks = 4, OffsetX = 0f, HalfWidth = 2.6f, HalfHeight = 2.6f,
                        Damage = 3, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 7f, KnockbackY = 8f,
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
        ///   ステージの外で爆発する（D-63）。威力は最低 5、1 秒ごとに +10。壊せる物は壊す。クールタイム 6 秒。
        /// - R COSMIC RAY（コズミックレイ）: 自分のポータル 2 つの間に、壁を貫通するビーム（太さ 1.5m）を撃つ（撃つ間は止まる）。
        ///   10 ティックごとに 15 ダメージを 6 回。撃ち終わったときにビームの線が拠点に触れていれば、手持ちのコインを全部確定する（C-9）。
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
                        Damage = 5, HitstopTicks = 10, HitstunTicks = 26, KnockbackX = 3f, KnockbackY = 17f,
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
                        Damage = 6, HitstopTicks = 12, HitstunTicks = 22, KnockbackX = 12f, KnockbackY = 6f,
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
                        Damage = 9, HitstopTicks = 20, HitstunTicks = 28, KnockbackX = 3f, KnockbackY = -14f,
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
                        Damage = 3, HitstopTicks = 8, HitstunTicks = 18, KnockbackX = -3f, KnockbackY = 3f,
                    },
                    new AttackHit
                    {
                        StartTick = 12, ActiveTicks = 4, OffsetX = 0f, HalfWidth = 2.6f, HalfHeight = 2.6f,
                        Damage = 4, HitstopTicks = 10, HitstunTicks = 22, KnockbackX = 6f, KnockbackY = 7f,
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
        /// PASTRY CHEF（仮の名前。docs/14 1.4。ユーザーの案、2026-10-21）。お菓子で足場と罠を作るキャラクター。通常の攻撃（地上技・空中技）は
        /// STANDARD と同じ【仮】。
        /// - Q フラッフィベイス: わたがしの足場（10 秒）。短く押すと足元に上向き、長押しすると照準の方向を向けて目の前に置く。触れた人を
        ///   弾き飛ばす。ワイヤーはつながらない。クールタイム 8 秒。
        /// - E スイートトラップ: クリームを照準の方向へ扇形に飛ばし、当たった地形に 10 秒張り付かせる。長押しの間にワイヤーでオレンジ、
        ///   攻撃で抹茶（何も押さない・短く押すとオレンジ）。オレンジは速さ +30%・壁ジャンプ 1.5 倍・ワイヤーがつながる。抹茶は速さ −30%・
        ///   壁と天井に 1 秒張り付く・壁に張り付けず、ワイヤーもつながらない。クールタイム 18 秒。
        /// - R ティーパーティ: マカロン 6 個を次々と上へ投げる（緑 60% 威力 20、黄 35% 威力 40、虹 5% 威力 100）。必要なゲージ 100%。
        /// </summary>
        public static CharacterConfig PastryChef()
        {
            var config = new CharacterConfig();
            CombatTuning combat = config.Combat;

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
    }
}
