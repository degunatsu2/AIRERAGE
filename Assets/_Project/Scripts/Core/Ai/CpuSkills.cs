using System;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;

namespace Airerage.Core.Ai
{
    /// <summary>CPU が技を決めるときの、今の場面（CpuPlayer が作る）。</summary>
    public readonly struct CpuSituation
    {
        public CpuSituation(CpuGoalKind goal, Vector2 target, int opponent, float opponentDistance, bool overVoid, int bankAtHeldCoins)
        {
            Goal = goal;
            Target = target;
            Opponent = opponent;
            OpponentDistance = opponentDistance;
            OverVoid = overVoid;
            BankAtHeldCoins = bankAtHeldCoins;
        }

        /// <summary>今していること。</summary>
        public CpuGoalKind Goal { get; }

        /// <summary>今の目的地。</summary>
        public Vector2 Target { get; }

        /// <summary>いちばん近い相手（触れられる相手だけ。いなければ -1）。</summary>
        public int Opponent { get; }

        /// <summary>いちばん近い相手までの距離（m。いなければ float.MaxValue）。</summary>
        public float OpponentDistance { get; }

        /// <summary>空中で、真下に何もない。</summary>
        public bool OverVoid { get; }

        /// <summary>この枚数以上のコインを持っていたら、確定したい（CpuTuning.BankAtHeldCoins）。</summary>
        public int BankAtHeldCoins { get; }

        public bool Recovering => Goal == CpuGoalKind.Recover;
    }

    /// <summary>CPU の技の使い方（1 回分）。</summary>
    public struct CpuSkillUse
    {
        public AttackSlot Slot;

        /// <summary>ボタンを押し続けるティック（1 なら短押し）。</summary>
        public int HoldTicks;

        /// <summary>照準を向ける相手（-1 なら Aim のまま）。押している間、毎ティック相手の方へ向け直す。</summary>
        public int AimAt;

        /// <summary>照準の方向（AimAt が -1 のとき）。</summary>
        public Vector2 Aim;

        /// <summary>長押しの間に押すボタン（クリームの味を選ぶ。なければ None）と、押すティック（押し始めから数える）。</summary>
        public InputButtons Choice;
        public int ChoiceTick;

        /// <summary>落ちそうなときの技（確率を上げて使う）。</summary>
        public bool Urgent;
    }

    /// <summary>
    /// CPU の技（Q・E・R）の使い分け（docs/16 T7-4。D-70【仮】）。キャラクターごとのコードは書かず、技のデータ（効果の種類 SkillEffect、
    /// 判定と動き、長押し）から、使う場面と押し方を決める。新しいキャラクターも、技の効果が今までの種類なら、そのまま使い分ける。
    /// - 判定のある技（STANDARD の 3 つ、イナズマステップ）: 技の動きで進んだ先で判定が相手に届くなら、相手の方へ照準して出す。
    ///   長押しで遠くへ届く技は、短押しで届かないときに長押しする。
    /// - 撃つ技（ミサイル、コイントス、ボールライトニング、マカロン、突風）: 相手が射程の中にいて、間に地形がなければ、相手の方へ。
    ///   ボールライトニングは、出している間ずっと相手の方へ照準する（1 つずつ向きを変えられるため）。
    /// - 自分を強くする技（CLOAK・CASTLE・FEVER）: 遠くの目的地へ急ぐとき・相手が近いとき・コインを拾う前。
    /// - 置く技: わたがしは落ちそうなとき足元に、ミラーボールは落ちそうなとき頭上（つなぐ所）に。ワープポータルは、安全な所に 1 つ
    ///   置いておき、落ちそうなときに長押しでそこへ戻る。相手が近いときは 2 つ目を置いて、コズミックレイ（ポータルの間のビーム）を
    ///   相手が線の近くにいるときに撃つ。金庫は、コインを多く持っていて、拠点が遠く相手もいないときに長押しで預ける。
    /// - クリーム: 少し離れた相手へ、長押しの間に攻撃を押して抹茶（遅くして張り付かせる）。
    /// - インバータ: 相手が近ければオン（ワイヤーで足場につなぐと雷が伝わる）、落ちそうなとき・相手が遠いときはオフ（射程を戻す）。
    /// - JUGGERNAUT: フラックは、地上で離れた相手がいれば構え、構えの間は相手に届く弾道の照準で撃つ（届かなければ CpuPlayer が段階を変えるか、
    ///   構えをやめる）。地雷は相手の方へ撃ち、相手が爆発の範囲に入ったら CpuPlayer がもう一度 E。メーデーは落ちそうなときと、拠点が遠くて
    ///   コインを多く持っているときに使い、場所は CpuPlayer が選ぶ（拠点か、ステージの真ん中の上）。
    /// - THIEF: タレントスナッチは相手の方へ撃ち（コピーした Q は、その技の効果の種類で使い分ける）、奪ったギミックは落ちそうなら足もとへ・
    ///   相手が近ければ相手の方へ置く。ミスディレクションは相手が近いときに反対の方へ。バンクハイストは拠点の近くで相手がいないときに始め、
    ///   相手が近づくか 15 秒たったら止める（CpuPlayer）。透明の THIEF は、ほかの CPU からねらわれない。
    /// </summary>
    public static class CpuSkills
    {
        // 撃つ技で狙う相手までの距離（m）。
        private const float MissileRange = 18f;
        private const float BallRange = 12f;
        private const float TeaPartyRange = 9f;
        private const float ProjectileRange = 12f;
        private const float CreamMinRange = 2.5f;
        private const float CreamMaxRange = 10f;
        private const float SnatchRange = 20f;
        private const float PlaceRange = 9f;
        private const float MisdirectionRange = 5f;

        // フラックの構えに入る相手の距離（m。近すぎると撃つ前に殴られる）。
        private const float FlakMinRange = 5f;
        private const float FlakMaxRange = 28f;

        // メーデーで拠点へ運ばれる、拠点までの距離（m）。
        private const float MaydayFarBank = 25f;

        // 自分を強くする技: 相手がこの近さなら（m）、目的地がこの遠さなら（m）使う。
        private const float BoostNearOpponent = 8f;
        private const float BoostFarTarget = 12f;

        // 置く技・切り替える技: 相手がこれより遠ければ、落ち着いている（m）。
        private const float QuietDistance = 10f;
        private const float PortalQuietDistance = 7f;
        private const float InverterOnDistance = 8f;
        private const float InverterOffDistance = 14f;

        // 判定が相手に届くかを調べるときの、相手の箱の余白（m）。
        private const float ReachMargin = 0.2f;

        /// <summary>
        /// 技 slot を今使うとよいなら true と使い方。使えるか（クールタイム・ゲージ）は呼ぶ側が ActionController.CanUse で調べる。
        /// </summary>
        public static bool Plan(SimWorld world, int index, in CharacterState self, CharacterConfig config, in CpuSituation situation, AttackSlot slot,
            out CpuSkillUse use)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            AttackData attack = config.Combat.Get(slot);
            use = new CpuSkillUse { Slot = slot, HoldTicks = 1, AimAt = -1 };
            int opponent = situation.Opponent;
            float distance = situation.OpponentDistance;
            Vector2 position = Flat(self.Position);
            Vector2 toOpponent = opponent >= 0 ? Flat(world.GetState(opponent).Position) - position : Vector2.Zero;
            switch (attack.Effect)
            {
                case SkillEffect.None:
                    return PlanStrike(world, self, config, attack, opponent, ref use);

                case SkillEffect.Projectile:
                    return AimedShot(world, self, opponent, distance, ProjectileRange, ref use);

                case SkillEffect.Missile:
                    if (!AimedShot(world, self, opponent, distance, MissileRange, ref use))
                    {
                        return false;
                    }
                    // 相手の動きを少し先読みする（ミサイルが届くまでの間）。
                    CharacterState target = world.GetState(opponent);
                    float flight = distance / Math.Max(1f, config.Skills.Missile.Speed);
                    use.AimAt = -1;
                    use.Aim = toOpponent + new Vector2(target.Velocity.X, target.Velocity.Y) * flight;
                    return true;

                case SkillEffect.BallLightning:
                    if (!AimedShot(world, self, opponent, distance, BallRange, ref use))
                    {
                        return false;
                    }
                    // 球は放つときの照準へ進むので、出している間ずっと押して相手の方へ向け続ける。
                    use.HoldTicks = attack.TotalTicks;
                    return true;

                case SkillEffect.TeaParty:
                    return opponent >= 0 && distance <= TeaPartyRange;

                case SkillEffect.Cream:
                    if (opponent < 0 || distance < CreamMinRange || distance > CreamMaxRange)
                    {
                        return false;
                    }
                    // 長押しの間に攻撃を押して抹茶を選び、相手の方へ放つ。
                    use.AimAt = opponent;
                    use.HoldTicks = 8;
                    use.Choice = InputButtons.Attack;
                    use.ChoiceTick = 3;
                    return true;

                case SkillEffect.SpeedBoost:
                    if (self.Combat.SpeedBoostTicksLeft > 0)
                    {
                        // 効果中にもう一度出すと突風（強化は終わる）。届く相手がいるときだけ。
                        SpeedBoostTuning boost = config.Skills.SpeedBoost;
                        return AimedShot(world, self, opponent, distance, boost.GustRange * 0.7f, ref use);
                    }
                    return Hurrying(self, situation) || distance <= BoostNearOpponent;

                case SkillEffect.SkyBoost:
                    use.Urgent = situation.Recovering;
                    return situation.Recovering || distance <= BoostNearOpponent;

                case SkillEffect.CoinBoost:
                    return (situation.Goal == CpuGoalKind.Coin && Vector2.Distance(position, situation.Target) <= BoostNearOpponent)
                        || distance <= BoostNearOpponent * 0.75f;

                case SkillEffect.WindZone:
                    // 風の帯を目的地の方へ置いて、乗って行く（落ちそうなとき・目的地が高いとき）。
                    Vector2 toTarget = situation.Target - position;
                    bool high = toTarget.Y > 6f && toTarget.Length() < config.Skills.WindZone.Length * 1.5f;
                    if (!situation.Recovering && !high)
                    {
                        return false;
                    }
                    use.Aim = toTarget;
                    use.Urgent = situation.Recovering;
                    return true;

                case SkillEffect.CottonCandy:
                    // 落ちそうなとき、足元に跳ねる足場を置く（短押し）。
                    use.Urgent = true;
                    return situation.Recovering && situation.OverVoid && self.Velocity.Y < 0f;

                case SkillEffect.MirrorBall:
                    // 落ちそうなとき、頭上につなげる所を置く。
                    use.Urgent = true;
                    return situation.Recovering;

                case SkillEffect.Safe:
                    return PlanSafe(world, self, config, attack, situation, ref use);

                case SkillEffect.Portal:
                    return PlanPortal(world, index, self, config, attack, situation, ref use);

                case SkillEffect.CosmicRay:
                    return opponent >= 0 && OnBeam(world, index, config, Flat(world.GetState(opponent).Position));

                case SkillEffect.TalentSnatch:
                    // 相手の Q をコピーする（相手の方へ撃つ）。
                    return AimedShot(world, self, opponent, distance, SnatchRange, ref use);

                case SkillEffect.PlaceGimmick:
                    // 奪ったギミック: 落ちそうなら足もとへ（ジャンプ台などで戻る）、相手が近ければ相手の方へ置く。
                    if (situation.Recovering)
                    {
                        use.Aim = new Vector2(0f, -1f);
                        use.Urgent = true;
                        return true;
                    }
                    use.AimAt = opponent;
                    return opponent >= 0 && distance <= PlaceRange;

                case SkillEffect.Misdirection:
                    // 相手が近いとき、分身を残して相手と反対の方へ。
                    if (opponent < 0 || distance > MisdirectionRange)
                    {
                        return false;
                    }
                    use.Aim = toOpponent.LengthSquared() > SimMath.Epsilon ? -Vector2.Normalize(toOpponent) : new Vector2(-self.Facing, 0f);
                    return true;

                case SkillEffect.Flak:
                {
                    if (opponent < 0)
                    {
                        return false;
                    }
                    if (!self.Combat.FlakStance)
                    {
                        // 構える（回数は使わない）。撃つのは構えてから（CpuPlayer.HoldStance）。
                        return distance >= FlakMinRange && distance <= FlakMaxRange;
                    }
                    if (!TrySolveFlak(world, self, config, opponent, JuggernautRules.FlakLevel(self, config.Skills.Juggernaut), out Vector2 aim))
                    {
                        return false;
                    }
                    use.Aim = aim;
                    return true;
                }

                case SkillEffect.Mine:
                {
                    // 地雷は重力で山なりに飛ぶので、届く低い弾道で相手の方へ撃つ（届かなければ撃たない）。
                    if (!AimedShot(world, self, opponent, distance, config.Skills.Juggernaut.MineRange * 0.8f, ref use))
                    {
                        return false;
                    }
                    JuggernautTuning tuning = config.Skills.Juggernaut;
                    if (!SolveBallistic(position, Flat(world.GetState(opponent).Position), tuning.MineSpeed, tuning.MineGravity, out Vector2 aim))
                    {
                        return false;
                    }
                    use.AimAt = -1;
                    use.Aim = aim;
                    return true;
                }

                case SkillEffect.Mayday:
                {
                    // 落ちそうなとき、または拠点が遠くてコインを多く持っているとき（場所は CpuPlayer が選ぶ）。
                    use.Urgent = situation.Recovering && situation.OverVoid;
                    bool farBank = situation.Goal == CpuGoalKind.Bank && Vector2.Distance(position, situation.Target) >= MaydayFarBank;
                    return use.Urgent || farBank;
                }

                case SkillEffect.BankHeist:
                    // 拠点の近くで（ActionController.CanUse が調べる）、相手が近くにいないとき。止めるのは CpuPlayer。
                    return distance > QuietDistance;

                case SkillEffect.Inverter:
                    use.Urgent = situation.Recovering;
                    if (self.Combat.InverterOn)
                    {
                        return situation.Recovering || distance > InverterOffDistance;
                    }
                    return !situation.Recovering && distance <= InverterOnDistance;

                default:
                    // まだ使い方を決めていない種類: 相手が近ければ、相手の方へ。
                    use.AimAt = opponent;
                    return opponent >= 0 && distance <= BoostNearOpponent;
            }
        }

        /// <summary>
        /// フラックの砲弾を、弾道の段階 level で相手 opponent に当てる照準（低い弾道。相手の今の位置と、届くまでの動きを少し先読み）。
        /// 届かなければ false。
        /// </summary>
        public static bool TrySolveFlak(SimWorld world, in CharacterState self, CharacterConfig config, int opponent, int level, out Vector2 aim)
        {
            aim = Vector2.Zero;
            if (world == null || opponent < 0)
            {
                return false;
            }
            JuggernautTuning tuning = config.Skills.Juggernaut;
            CharacterState target = world.GetState(opponent);
            var origin = new Vector2(self.Position.X, self.Position.Y + tuning.FlakMuzzleHeight);
            float speed = tuning.FlakSpeedAt(level);
            Vector2 at = Flat(target.Position);
            float lead = Vector2.Distance(origin, at) / Math.Max(1f, speed);
            at += new Vector2(target.Velocity.X, target.Velocity.Y) * lead * 0.5f;
            return SolveBallistic(origin, at, speed, tuning.FlakGravity, out aim);
        }

        // 速さ speed・重力 gravity で origin から target に当たる、低い弾道の向き（長さ 1）。届かなければ false。
        private static bool SolveBallistic(Vector2 origin, Vector2 target, float speed, float gravity, out Vector2 aim)
        {
            aim = Vector2.Zero;
            float dx = target.X - origin.X;
            float dy = target.Y - origin.Y;
            float x = MathF.Abs(dx);
            if (x < 0.5f)
            {
                return false;
            }
            if (gravity <= SimMath.Epsilon)
            {
                aim = Vector2.Normalize(new Vector2(dx, dy));
                return true;
            }
            float v2 = speed * speed;
            float discriminant = v2 * v2 - gravity * (gravity * x * x + 2f * dy * v2);
            if (discriminant < 0f)
            {
                return false;
            }
            float angle = MathF.Atan((v2 - MathF.Sqrt(discriminant)) / (gravity * x));
            aim = new Vector2(MathF.Sign(dx) * MathF.Cos(angle), MathF.Sin(angle));
            return true;
        }

        // 判定のある技: 技の動きで進んだ先で、判定が相手に届くか（短押しで届かなければ長押し）。
        private static bool PlanStrike(SimWorld world, in CharacterState self, CharacterConfig config, AttackData attack, int opponent,
            ref CpuSkillUse use)
        {
            if (opponent < 0 || attack.Hits == null || attack.Hits.Length == 0)
            {
                return false;
            }
            CharacterState target = world.GetState(opponent);
            Vector3 targetHalf = world.GetConfig(opponent).Movement.HalfExtents;
            Vector2 toOpponent = Flat(target.Position) - Flat(self.Position);
            Vector2 aim = toOpponent.LengthSquared() > SimMath.Epsilon ? Vector2.Normalize(toOpponent) : new Vector2(self.Facing, 0f);
            int facing = attack.AimTurnsFacing && MathF.Abs(aim.X) > 0.1f ? (aim.X > 0f ? 1 : -1) : (toOpponent.X >= 0f ? 1 : -1);
            float scale = SpeedBoosts.ScaleOf(self, config);
            use.AimAt = opponent;
            if (ReachesAfterMotion(self, attack, 0, facing, aim, scale, target.Position, targetHalf))
            {
                return true;
            }
            if (attack.HoldMaxTicks > 0 && attack.ChargedFromTicks > 0
                && ReachesAfterMotion(self, attack, attack.ChargedFromTicks, facing, aim, scale, target.Position, targetHalf))
            {
                // 押し始めのティックはためた長さに数えないので、1 ティック多く押す。
                use.HoldTicks = Math.Min(attack.HoldMaxTicks, attack.ChargedFromTicks + 2);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 攻撃 attack をためた長さ charge で、向き facing・照準 aim で出したとき、どれかの判定が、出ている間の真ん中のティックに
        /// 相手の箱へ届くか。攻撃の動き（AttackMotion）で進む分を足す（重力と減速は考えない目安）。
        /// </summary>
        public static bool ReachesAfterMotion(in CharacterState self, AttackData attack, int charge, int facing, Vector2 aim, float scale,
            Vector3 target, Vector3 targetHalf)
        {
            CharacterState probe = self;
            probe.Facing = facing;
            probe.Action.Aim = aim;
            var targetCenter = new Vector2(target.X, target.Y);
            var half = new Vector2(targetHalf.X + ReachMargin, targetHalf.Y + ReachMargin);
            foreach (AttackHit hit in attack.Hits)
            {
                if (hit.Critical || !attack.Uses(hit.Charge, charge))
                {
                    continue;
                }
                float at = hit.StartTick + hit.ActiveTicks * 0.5f;
                Vector2 moved = Displacement(attack, charge, facing, aim, scale, at);
                probe.Position = self.Position + new Vector3(moved.X, moved.Y, 0f);
                HitResolver.Hitbox(probe, hit, out Vector2 center, out Vector2 boxHalf);
                if (HitShapes.Overlaps(center, boxHalf, HitResolver.HitAngle(probe, hit), targetCenter, half))
                {
                    return true;
                }
            }
            return false;
        }

        // 攻撃の始めから untilTick までに、攻撃の動きで進む距離（m）。当たったときだけの動きは数えない。
        private static Vector2 Displacement(AttackData attack, int charge, int facing, Vector2 aim, float scale, float untilTick)
        {
            Vector2 moved = Vector2.Zero;
            if (attack.Motions == null)
            {
                return moved;
            }
            foreach (AttackMotion motion in attack.Motions)
            {
                if (motion.RequiresHit || !attack.Uses(motion.Charge, charge))
                {
                    continue;
                }
                float ticks = Math.Min(untilTick, motion.StartTick + motion.Ticks) - motion.StartTick;
                if (ticks <= 0f)
                {
                    continue;
                }
                Vector2 velocity = motion.AlongAim ? aim * motion.VelocityX : new Vector2(motion.VelocityX * facing, motion.VelocityY);
                moved += velocity * scale * (ticks / SimTime.TicksPerSecond);
            }
            return moved;
        }

        // 撃つ技: 相手が range の中にいて、間に地形がなければ、相手の方へ照準する。
        private static bool AimedShot(SimWorld world, in CharacterState self, int opponent, float distance, float range, ref CpuSkillUse use)
        {
            if (opponent < 0 || distance > range)
            {
                return false;
            }
            Vector3 from = self.Position;
            Vector3 to = world.GetState(opponent).Position;
            Vector3 delta = to - from;
            float length = delta.Length();
            if (length > SimMath.Epsilon && world.Collision.Raycast(from, delta / length, length, out _))
            {
                return false;
            }
            use.AimAt = opponent;
            return true;
        }

        // 急いでいる（目的地が遠い、または落ちそう）。
        private static bool Hurrying(in CharacterState self, in CpuSituation situation)
        {
            if (situation.Recovering)
            {
                return true;
            }
            bool going = situation.Goal == CpuGoalKind.Coin || situation.Goal == CpuGoalKind.Bank || situation.Goal == CpuGoalKind.Fight;
            return going && Vector2.Distance(Flat(self.Position), situation.Target) > BoostFarTarget;
        }

        // 金庫: コインを多く持っていて、拠点が遠く、相手も近くにいない。地上で、持っている分を預けるまで長押しする。
        private static bool PlanSafe(SimWorld world, in CharacterState self, CharacterConfig config, AttackData attack, in CpuSituation situation,
            ref CpuSkillUse use)
        {
            int held = self.Coins.Held;
            if (held < situation.BankAtHeldCoins || situation.OpponentDistance <= QuietDistance || !self.Grounded || situation.Recovering)
            {
                return false;
            }
            if (situation.Goal == CpuGoalKind.Bank && Vector2.Distance(Flat(self.Position), situation.Target) < 15f)
            {
                return false;
            }
            SafeTuning safe = config.Skills.Safe;
            int ticks = held * SimTime.TicksPerSecond / Math.Max(1, safe.DepositPerSecond) + 2;
            use.HoldTicks = Math.Clamp(ticks, 1, Math.Max(1, attack.HoldMaxTicks));
            return true;
        }

        // ワープポータル: 落ちそうなとき、安全な所のポータルへ長押しで戻る。落ち着いていて 1 つもなければ、地上に戻る所として置く。
        // 相手が近く、コズミックレイを撃てるゲージがあれば、2 つ目を置く。
        private static bool PlanPortal(SimWorld world, int index, in CharacterState self, CharacterConfig config, AttackData attack,
            in CpuSituation situation, ref CpuSkillUse use)
        {
            PortalTuning tuning = config.Skills.Portal;
            CountPortals(world, index, out int count, out Vector2 oldest, out Vector2 newest);
            if (situation.Recovering)
            {
                if (count == 0 || !world.Bounds.HasValue)
                {
                    return false;
                }
                // 置くと、数が上限ならいちばん古いものが消えるので、残る方へ移る。
                Vector2 partner = count >= tuning.MaxPerOwner ? newest : oldest;
                if (!Safe(world.Bounds.Value, partner) || Vector2.Distance(partner, Flat(self.Position)) < 4f)
                {
                    return false;
                }
                use.HoldTicks = Math.Min(attack.HoldMaxTicks, tuning.EnterHoldTicks + 2);
                use.Urgent = true;
                return true;
            }
            if (count == 0)
            {
                return self.Grounded && situation.OpponentDistance > PortalQuietDistance;
            }
            bool canRay = config.Combat.Super.Effect == SkillEffect.CosmicRay && self.Combat.Gauge >= config.Combat.SuperGaugeCost;
            return count == 1 && canRay && situation.OpponentDistance <= BoostNearOpponent && Vector2.Distance(oldest, Flat(self.Position)) > 4f;
        }

        // 相手 point が、自分の 2 つのポータルの間のビームの近くにいるか。
        private static bool OnBeam(SimWorld world, int index, CharacterConfig config, Vector2 point)
        {
            CountPortals(world, index, out int count, out Vector2 oldest, out Vector2 newest);
            return count >= 2 && SpaceRules.DistanceToSegment(point, oldest, newest) <= config.Skills.Beam.HalfWidth + 0.5f;
        }

        private static void CountPortals(SimWorld world, int index, out int count, out Vector2 oldest, out Vector2 newest)
        {
            count = 0;
            oldest = Vector2.Zero;
            newest = Vector2.Zero;
            WorldObjects objects = world.Objects;
            for (int o = 0; o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Kind != WorldObjectKind.Portal || item.Owner != index || item.Ended)
                {
                    continue;
                }
                if (count == 0)
                {
                    oldest = item.Position;
                }
                newest = item.Position;
                count++;
            }
        }

        // 撃破の範囲の端から十分に内側か。
        private static bool Safe(StageBounds bounds, Vector2 point)
        {
            const float margin = 8f;
            return point.X > bounds.MinX + margin && point.X < bounds.MaxX - margin && point.Y > bounds.MinY + margin;
        }

        private static Vector2 Flat(Vector3 value)
        {
            return new Vector2(value.X, value.Y);
        }
    }
}
