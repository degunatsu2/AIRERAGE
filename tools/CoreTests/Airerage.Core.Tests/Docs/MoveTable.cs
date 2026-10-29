using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;

namespace Airerage.Core.Tests.Docs
{
    /// <summary>
    /// 技の性能表（docs/17_move_data.md）を、コードの値（CharacterPresets・CombatTuning・SkillTuning）から作る（ユーザーの要望、2026-10-22）。
    /// 数値はすべてここで読み出すので、技の値を変えたら表も変わる（MoveTableTests が、ページとコードが合っているかを調べる）。
    /// 備考の文のうち、技の説明だけは MoveNotes の手書き。
    /// </summary>
    internal static class MoveTable
    {
        public const string RelativePath = "docs/17_move_data.md";

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>表に載せるキャラクター（キャラクター選択の順）。</summary>
        public static readonly (string Name, Func<CharacterConfig> Create)[] Characters =
        {
            (CharacterPresets.StandardName, CharacterPresets.Standard),
            (CharacterPresets.RiskTakerName, CharacterPresets.RiskTaker),
            (CharacterPresets.StormRunnerName, CharacterPresets.StormRunner),
            (CharacterPresets.SpaceTravelerName, CharacterPresets.SpaceTraveler),
            (CharacterPresets.PastryChefName, CharacterPresets.PastryChef),
            (CharacterPresets.SoldererName, CharacterPresets.Solderer),
            (CharacterPresets.ThiefName, CharacterPresets.Thief),
            (CharacterPresets.JuggernautName, CharacterPresets.Juggernaut),
        };

        private static readonly (AttackSlot Slot, string Label)[] Slots =
        {
            (AttackSlot.Ground, "地上技"),
            (AttackSlot.AirNeutral, "空中 N"),
            (AttackSlot.AirSide, "空中横"),
            (AttackSlot.AirUp, "空中上"),
            (AttackSlot.AirDown, "空中下"),
            (AttackSlot.Special1, "Q"),
            (AttackSlot.Special2, "E"),
            (AttackSlot.Super, "R"),
        };

        public static string Render()
        {
            var text = new StringBuilder();
            var configs = Characters.Select(c => (c.Name, Config: c.Create())).ToList();
            CharacterConfig standard = configs[0].Config;
            CombatTuning combat = standard.Combat;

            text.Append("# 17. 技の性能表（全キャラクター）\n\n");
            text.Append("> **このページは自動で作る。手で書き換えない。** 数値はコード（`CharacterPresets`・`CombatTuning`・`SkillTuning`）から読み出す。\n");
            text.Append("> 技の値を変えたら `bash tools/update_move_table.sh` で作り直す（`dotnet test` と GitHub Actions が、このページとコードの値が\n");
            text.Append("> 合っているかを調べ、合っていなければ失敗する）。作り方は `tools/CoreTests/Airerage.Core.Tests/Docs/MoveTable.cs`、\n");
            text.Append("> 技の説明の文は `MoveNotes.cs`。値はすべて【仮】。\n\n");

            text.Append("## 見方\n\n");
            text.Append("- **F** はフレーム（1/60 秒。シミュレーションの 1 ティック）。\n");
            text.Append("- **威力**: 当たったときのダメージ。コンボが終わるとそのまま同じ枚数のコインになる（威力とコインは等価）。判定が複数あれば「/」で区切る。\n");
            text.Append("- **吹き飛び**: 当たった相手の飛ぶ速さ（m/s。横は向いている方向が正、縦は上が正）。\n");
            text.Append($"- **ゲージ**: 当てた側が得るゲージ（%）。ふつうのガードをされたら {combat.GaugeGuardedPercent}%、ジャストガードされたら 0。");
            text.Append("必殺技の判定は 0。\n");
            text.Append("- **発生**: 判定が出る最初のフレーム（技を出したフレームを 1 とする）。投射物は、出るフレーム。\n");
            text.Append("- **持続**: 判定が出ている長さ（F）。**後隙**: 最後の判定が消えてから動けるまで（F）。**全体**: 技の長さ（F）。\n");
            text.Append("- **のけぞり**: 当たった相手が動けない長さ（F。ヒットストップの後から数える）。\n");
            text.Append($"- 撃破技（★）のうち「速さで強くなる」技は、威力と吹き飛びに ×（{Num(combat.KoKnockbackBase)} ＋ {Num(combat.KoKnockbackPerSpeed)} × 打つ直前の速さ）、");
            text.Append($"{Num(combat.FatalStrikeSpeed)} m/s 以上で FATAL（ヒットストップ ×{combat.FatalHitstopMultiplier}）。STANDARD だけ。\n");
            text.Append($"- 受けた側のゲージ: のけぞったら {Percent(combat.GaugeOnDamaged)}、ガードしたら {Percent(combat.GaugeOnGuard)}、");
            text.Append($"ジャストガードしたら {Percent(combat.GaugeOnJustGuard)}。コイン 1 枚を拾うと {Percent(combat.GaugePerCoin)}。\n\n");

            RenderComparison(text, configs);

            foreach ((string name, CharacterConfig config) in configs)
            {
                RenderCharacter(text, name, config);
            }
            return text.ToString();
        }

        // キャラクターを並べて比べる表（判定の威力の合計・発生・全体）。
        private static void RenderComparison(StringBuilder text, List<(string Name, CharacterConfig Config)> configs)
        {
            text.Append("## 比べる（キャラクター × 技）\n\n");
            text.Append("各マスは「威力の合計 ／ 発生 F ／ 全体 F」（長押しで変わる技は短押し。投射物は 1 つ分。★ は撃破技）。\n\n");
            text.Append("| 枠 |");
            foreach ((string name, _) in configs)
            {
                text.Append(' ').Append(name).Append(" |");
            }
            text.Append('\n').Append("|---|");
            foreach (var _ in configs)
            {
                text.Append("---|");
            }
            text.Append('\n');
            foreach ((AttackSlot slot, string label) in Slots)
            {
                text.Append("| ").Append(label).Append(" |");
                foreach ((_, CharacterConfig config) in configs)
                {
                    AttackData attack = config.Combat.Get(slot);
                    List<Entry> entries = EntriesOf(attack, config, 0);
                    string damage = entries.Count > 0 ? entries.Sum(e => e.Hit.Damage).ToString(Invariant) : "—";
                    string startup = entries.Count > 0 ? entries.Min(e => e.Startup).ToString(Invariant) : "—";
                    string star = slot == config.Combat.KoAttack ? "★" : string.Empty;
                    text.Append(' ').Append(star).Append(damage).Append(" ／ ").Append(startup).Append(" ／ ").Append(TotalText(attack, 0)).Append(" |");
                }
                text.Append('\n');
            }
            text.Append('\n');
        }

        private static void RenderCharacter(StringBuilder text, string name, CharacterConfig config)
        {
            CombatTuning combat = config.Combat;
            text.Append("## ").Append(name).Append("\n\n");
            text.Append($"必殺技に必要なゲージ {Percent(combat.SuperGaugeCost)}。撃破技（★）は {LabelOf(combat.KoAttack)}。");
            if (combat.GuardHoldMaxTicks > 0 || combat.GuardWalk)
            {
                text.Append($"ガードはクールタイム {Seconds(combat.GuardCooldownTicks)}");
                if (combat.GuardHoldMaxTicks > 0)
                {
                    text.Append($"、押し続けると最大 {Seconds(combat.GuardHoldMaxTicks)}");
                }
                if (combat.GuardWalk)
                {
                    text.Append("、地上ならガードしながら歩ける");
                }
                text.Append('。');
            }
            text.Append("\n\n");
            text.Append("| 枠 | 技 | 威力 | 吹き飛び（横・縦） | ゲージ | 発生 | 持続 | 後隙 | 全体 | のけぞり | 備考 |\n");
            text.Append("|---|---|---|---|---|---|---|---|---|---|---|\n");
            foreach ((AttackSlot slot, string label) in Slots)
            {
                AttackData attack = combat.Get(slot);
                var variants = new List<(int Charge, string Suffix)>();
                if (attack.ChargedFromTicks > 0)
                {
                    variants.Add((0, "（短押し）"));
                    variants.Add((attack.ChargedFromTicks, "（長押し）"));
                }
                else
                {
                    variants.Add((0, string.Empty));
                }
                foreach ((int charge, string suffix) in variants)
                {
                    RenderRow(text, name, config, slot, label + suffix, attack, charge);
                }
            }
            text.Append('\n');
        }

        private static void RenderRow(StringBuilder text, string character, CharacterConfig config, AttackSlot slot, string label, AttackData attack,
            int charge)
        {
            List<Entry> entries = EntriesOf(attack, config, charge);
            string star = slot == config.Combat.KoAttack ? "★" : string.Empty;
            string moveName = string.IsNullOrEmpty(attack.Name) ? "—" : attack.Name;
            var cells = new List<string>
            {
                star + label,
                moveName,
                Join(entries, e => DamageText(e, attack)),
                Join(entries, e => $"{Num(e.Hit.KnockbackX)}・{Num(e.Hit.KnockbackY)}"),
                Join(entries, e => Percent(e.Hit.Gauge)),
                Join(entries, e => e.Startup.ToString(Invariant)),
                Join(entries, e => e.FromObject ? "—" : e.Hit.UntilEnd ? (attack.HeldSustain ? "技の間" : "走る間") : e.Hit.ActiveTicks.ToString(Invariant)),
                RecoveryText(attack, entries, charge),
                TotalText(attack, charge),
                Join(entries, e => e.Hit.HitstunTicks.ToString(Invariant)),
                string.Join("。", NotesOf(character, config, slot, attack, charge, entries)),
            };
            text.Append("| ").Append(string.Join(" | ", cells.Select(Escape))).Append(" |\n");
        }

        // 技の判定 1 つ分（技そのものの判定か、技が出す投射物などの判定）。
        private struct Entry
        {
            public AttackHit Hit;
            public string Label;
            public bool FromObject;
            public int Startup;
        }

        private static List<Entry> EntriesOf(AttackData attack, CharacterConfig config, int charge)
        {
            var entries = new List<Entry>();
            foreach (AttackHit hit in attack.Hits)
            {
                if (attack.Uses(hit.Charge, charge))
                {
                    string label = hit.Critical ? "指先" : hit.FatalScale > 0f ? "砲塔の先" : null;
                    entries.Add(new Entry { Hit = hit, Label = label, Startup = hit.StartTick + 1 });
                }
            }
            if (TryEffectHit(attack, config, out AttackHit effectHit, out string effectLabel))
            {
                entries.Add(new Entry { Hit = effectHit, Label = effectLabel, FromObject = true, Startup = attack.EffectTick + 1 });
            }
            return entries;
        }

        // 技の効果が出す物の判定（投射物・ビーム・鞭など）。
        private static bool TryEffectHit(AttackData attack, CharacterConfig config, out AttackHit hit, out string label)
        {
            SkillTuning skills = config.Skills;
            switch (attack.Effect)
            {
                case SkillEffect.Projectile:
                    hit = skills.Projectile.Hit;
                    label = "投げたコイン";
                    return true;
                case SkillEffect.Missile:
                    hit = skills.Missile.Hit;
                    label = "ミサイル";
                    return true;
                case SkillEffect.CosmicRay:
                    hit = skills.Beam.Hit;
                    label = $"ビーム 1 回あたり。{skills.Beam.Hits} 回当たる";
                    return true;
                case SkillEffect.TeaParty:
                    hit = skills.TeaParty.Hit;
                    label = $"緑のマカロン 1 個あたり。{skills.TeaParty.Count} 個投げる";
                    return true;
                case SkillEffect.BallLightning:
                    hit = skills.BallLightning.Hit;
                    label = $"電気の球 1 個あたり。{skills.BallLightning.Count} 個撃つ";
                    return true;
                case SkillEffect.DropScale:
                    hit = skills.Scale.Hit;
                    label = "スケール";
                    return true;
                case SkillEffect.Inverter:
                    hit = skills.Inverter.WhipHit;
                    label = "鞭";
                    return true;
                case SkillEffect.SmokeBall:
                    hit = skills.Thief.SmokeHit;
                    label = "煙球の着弾";
                    return true;
                case SkillEffect.Cannon:
                    hit = skills.Juggernaut.CannonHit;
                    label = "砲弾";
                    return true;
                case SkillEffect.Flak:
                    hit = skills.Juggernaut.FlakHit;
                    label = "砲弾の爆発";
                    return true;
                case SkillEffect.Mine:
                    hit = skills.Juggernaut.MineHit;
                    label = "地雷の爆発";
                    return true;
                case SkillEffect.Mayday:
                case SkillEffect.TalentSnatch:
                case SkillEffect.PlaceGimmick:
                case SkillEffect.Misdirection:
                case SkillEffect.BankHeist:
                case SkillEffect.None:
                case SkillEffect.MirrorBall:
                case SkillEffect.Safe:
                case SkillEffect.CoinBoost:
                case SkillEffect.SpeedBoost:
                case SkillEffect.WindZone:
                case SkillEffect.SkyBoost:
                case SkillEffect.Portal:
                case SkillEffect.SlowFall:
                case SkillEffect.CottonCandy:
                case SkillEffect.Cream:
                    hit = null;
                    label = null;
                    return false;
                default:
                    // 新しい効果を足したら、ここと EffectNote に書く（書き忘れたら表を作れない）。
                    throw new InvalidOperationException($"性能表に効果 {attack.Effect} の書き方がない（MoveTable.TryEffectHit・EffectNote）");
            }
        }

        private static string DamageText(Entry entry, AttackData attack)
        {
            string text = entry.Hit.Damage.ToString(Invariant);
            if (entry.Hit.Critical)
            {
                text += $"（{entry.Label} ×{Num(entry.Hit.CriticalScale)}）";
            }
            else if (entry.Label != null)
            {
                text += $"（{entry.Label}）";
            }
            if (attack.Run && !entry.FromObject)
            {
                text += "（初速で。速さに比例）";
            }
            return text;
        }

        private static string RecoveryText(AttackData attack, List<Entry> entries, int charge)
        {
            List<Entry> own = entries.Where(e => !e.FromObject).ToList();
            if (attack.Run || own.Count == 0 || own.Any(e => e.Hit.UntilEnd))
            {
                return "—";
            }
            int end = own.Max(e => e.Hit.StartTick + e.Hit.ActiveTicks);
            return (attack.TotalTicksFor(charge) - end).ToString(Invariant);
        }

        private static string TotalText(AttackData attack, int charge)
        {
            if (attack.HeldSustain)
            {
                return $"{attack.TotalTicksFor(charge)}〜{attack.SustainMaxTicks}";
            }
            return attack.Run ? "走る間" : attack.TotalTicksFor(charge).ToString(Invariant);
        }

        private static IEnumerable<string> NotesOf(string character, CharacterConfig config, AttackSlot slot, AttackData attack, int charge,
            List<Entry> entries)
        {
            CombatTuning combat = config.Combat;
            string note = MoveNotes.Of(character, slot, attack);
            if (!string.IsNullOrEmpty(note))
            {
                yield return note;
            }
            if (attack.SpeedScaled)
            {
                yield return "打つ直前の速さで強くなる（FATAL あり）";
            }
            if (attack.Roulette)
            {
                yield return $"ルーレットの目 ×{Num(AttackData.RouletteScales.First())}〜×{Num(AttackData.RouletteScales.Last())}が威力と吹き飛びの倍率（最大で JACKPOT）";
            }
            if (attack.ChargeLevels.Length > 1)
            {
                IEnumerable<string> levels = attack.ChargeLevels.Select(l => $"{l.FromTicks}F +{l.BonusPercent}%/+{l.KnockbackPercent}%");
                yield return $"長押しでため（最大 {attack.HoldMaxTicks}F。威力/吹き飛びの上乗せ: {string.Join("、", levels)}。最後で JACKPOT）";
            }
            else if (attack.AimHold)
            {
                yield return "長押しの間は照準の方向に矢印を出して狙い、離すと出る";
            }
            else if (attack.HoldMaxTicks > 0 && attack.ChargedFromTicks == 0)
            {
                yield return $"長押しできる（最大 {attack.HoldMaxTicks}F）";
            }
            if (attack.ChargedFromTicks > 0 && charge > 0)
            {
                yield return attack.FiresWhenCharged
                    ? $"{attack.ChargedFromTicks}F 押し続けた瞬間に出る（離さなくてよい）"
                    : $"{attack.ChargedFromTicks}F 以上押してから離す";
            }
            if (attack.AimTurnsFacing)
            {
                yield return "照準の方向へ";
            }
            if (attack.ChainMax > 1 && charge == 0)
            {
                yield return $"続けて {attack.ChainMax} 回まで（次は {Seconds(attack.ChainWindowTicks)}以内）。長押しで出すと終わる";
            }
            float distance = MotionDistance(attack, charge);
            if (distance > 0.05f)
            {
                yield return $"動き {Num(distance)}m";
            }
            if (attack.Motions.Any(m => m.RequiresHit))
            {
                yield return "当たると反動で動く";
            }
            if (attack.EndsOnHit)
            {
                yield return "当たったら技が終わる";
            }
            if (attack.ExclusiveHits)
            {
                yield return "1 人には判定 1 つだけ当たる";
            }
            if (attack.RehitTicks > 0)
            {
                yield return $"同じ相手に {attack.RehitTicks}F に 1 回当たる";
            }
            if (attack.Mobile)
            {
                yield return "攻撃中も左右に動ける";
            }
            if (attack.HeldSustain)
            {
                yield return $"押し続けると最大 {attack.SustainMaxTicks}F 続き、{attack.SustainRiseFromTicks}F からは毎秒 {Num(attack.SustainRiseSpeed)}m で上がる（離すと終わる）";
            }
            AttackHit sweet = entries.Select(e => e.Hit).FirstOrDefault(h => h.FatalScale > 0f);
            if (sweet != null)
            {
                string sweep = sweet.SweepTicks > 0 ? $"（{sweet.SweepTicks}F で 1 周して前と後ろを行き来する）" : string.Empty;
                yield return $"砲塔の先{sweep}が当たると FATAL（威力と吹き飛び ×{Num(sweet.FatalScale)}）";
            }
            AttackHit lone = entries.Select(e => e.Hit).FirstOrDefault(h => h.LoneFatal);
            if (lone != null)
            {
                yield return $"前の判定をどれも当てずに最後の判定だけを当てると FATAL（威力 {lone.LoneFatalDamage}、吹き飛び ×{Num(lone.LoneFatalKnockbackScale)}）";
            }
            if (entries.Any(e => e.Hit.Critical))
            {
                yield return $"指先は相手の中心から {Num(combat.CriticalCenterRadius)}m 以内に重なったときだけ当たり、FATAL";
            }
            AttackHit stunning = entries.Select(e => e.Hit).FirstOrDefault(h => h.StunTicks > 0);
            if (stunning != null)
            {
                yield return $"当たると、先に {stunning.StunTicks}F 感電してから吹き飛ぶ";
            }
            if (attack.BlocksProjectilesAbove)
            {
                yield return "判定の間、上から来た投射物を防ぐ";
            }
            if (attack.Run)
            {
                RunTuning run = config.Skills.Run;
                yield return $"走り続ける（初速 {Num(run.InitialSpeed)} m/s、毎秒 +{Num(run.Acceleration)}、最大 {Num(run.MaxSpeed)}。同じ相手に {run.RehitTicks}F に 1 回）";
            }
            if (attack.TracksAim)
            {
                yield return "出している間、照準を追う";
            }
            string effect = EffectNote(attack.Effect, config);
            if (!string.IsNullOrEmpty(effect))
            {
                yield return effect;
            }
            if (attack.WireCancelFromTick < attack.TotalTicksFor(charge))
            {
                yield return $"ワイヤーでキャンセル {attack.WireCancelFromTick + 1}F〜";
            }
            switch (slot)
            {
                case AttackSlot.Special1:
                case AttackSlot.Special2:
                    int charges = combat.MaxChargesOf(slot);
                    yield return charges > 1
                        ? $"{charges} 回まで続けて使え、{Seconds(combat.CooldownTicksOf(slot))}ごとに 1 回戻る"
                        : attack.ChainMax > 1
                            ? $"クールタイム {Seconds(combat.CooldownTicksOf(slot))} × 使った回数"
                            : $"クールタイム {Seconds(combat.CooldownTicksOf(slot))}";
                    break;
                case AttackSlot.Super:
                    yield return $"ゲージ {Percent(combat.SuperGaugeCost)}";
                    break;
            }
        }

        // 技の効果の説明（効果の値はすべて調整値から）。
        private static string EffectNote(SkillEffect effect, CharacterConfig config)
        {
            SkillTuning s = config.Skills;
            switch (effect)
            {
                case SkillEffect.None:
                    return null;
                case SkillEffect.MirrorBall:
                    return $"ミラーボールを {Num(s.MirrorBall.OffsetY)}m 上に {Seconds(s.MirrorBall.DurationTicks)}置く（つなげる。つないだ人はゲージ " +
                        $"{Percent(s.MirrorBall.GaugePerInterval)} / {s.MirrorBall.GaugeIntervalTicks}F）";
                case SkillEffect.Safe:
                    return $"長押しで所持コインを預けて金庫を置く（毎秒 {s.Safe.DepositPerSecond} 枚。{Seconds(s.Safe.ChargeIntervalTicks)}ごとに +{s.Safe.ChargeCoins} 枚・" +
                        $"×{Num(s.Safe.ChargeRatePercent / 100f)}、上限 {s.Safe.MaxCoins} 枚。耐久 {s.Safe.Durability}、{Seconds(s.Safe.DurationTicks)}でばらまく）";
                case SkillEffect.CoinBoost:
                    return $"{Seconds(s.CoinBoost.DurationTicks)}、拾ったコインと戦闘の報酬 +{s.CoinBoost.BonusPercent}%、得るゲージ +{s.CoinBoost.GaugeBonusPercent}%";
                case SkillEffect.Projectile:
                    return $"コインを投げ上げる（所持コイン {s.Projectile.HeldCoinsPerBonusPercent} 枚ごとに威力 +1%、最大 +{s.Projectile.MaxHeldCoinBonusPercent}%）";
                case SkillEffect.SpeedBoost:
                    return $"{Seconds(s.SpeedBoost.DurationTicks)}、行動の速さ +{s.SpeedBoost.SpeedPercent}%。効果中にもう一度押すと突風（{Num(s.SpeedBoost.GustRange)}m、" +
                        $"溜め {s.SpeedBoost.GustWindupTicks}F・硬直 {s.SpeedBoost.GustRecoveryTicks}F）";
                case SkillEffect.WindZone:
                    return $"照準の方向へ風の帯（長さ {Num(s.WindZone.Length)}m・幅 {Num(s.WindZone.HalfWidth * 2f)}m）を {Seconds(s.WindZone.DurationTicks)}";
                case SkillEffect.SkyBoost:
                    return $"{Seconds(s.SkyBoost.DurationTicks)}、速さ +{s.SkyBoost.SpeedPercent}%、ワイヤーが弾く物・何もない空中にもつながる";
                case SkillEffect.Portal:
                    return $"ワープポータルを置く（{Seconds(s.Portal.DurationTicks)}、{s.Portal.MaxPerOwner} つまで。{s.Portal.EnterHoldTicks}F 長押しで置いて入る）";
                case SkillEffect.Missile:
                    return $"長押しで狙い、照準の方向へ {Num(s.Missile.Speed)} m/s。飛んだ 1 秒ごとに威力 +{s.Missile.BonusPercentPerSecond}%";
                case SkillEffect.CosmicRay:
                    return $"自分のポータル 2 つの間にビーム（太さ {Num(s.Beam.HalfWidth * 2f)}m、{s.Beam.IntervalTicks}F ごとに {s.Beam.Hits} 回）。ポータルが 2 つないと出せない";
                case SkillEffect.SlowFall:
                    return $"その後、別の移動をするまでゆっくり落ちる（毎秒 {Num(s.SlowFall.MaxFallSpeed)}m まで）";
                case SkillEffect.CottonCandy:
                    return $"わたがしの足場（{Seconds(s.CottonCandy.DurationTicks)}。触れた人を弾く）。短押しで足元、{s.CottonCandy.ShortPressTicks}F 以上の長押しで照準の方向";
                case SkillEffect.Cream:
                    return $"クリーム {s.Cream.ShotCount} 個を扇 {Num(s.Cream.SpreadDeg)}° に（{Seconds(s.Cream.PatchTicks)}張り付く）。長押しの間にワイヤーでオレンジ（速さ " +
                        $"+{s.Cream.OrangeSpeedPercent}%）、攻撃で抹茶（速さ {s.Cream.MatchaSpeedPercent}%・{s.Cream.MatchaStickTicks}F 張り付く）";
                case SkillEffect.TeaParty:
                    TeaPartyTuning t = s.TeaParty;
                    return $"マカロン {t.Count} 個を {t.IntervalTicks}F ごとに投げ上げる（緑 {t.GreenPercent}%・黄 {t.YellowPercent}%・虹 {100 - t.GreenPercent - t.YellowPercent}%。" +
                        $"黄は威力と吹き飛び ×{Num(1f + t.BonusPercentOf(MacaronColor.Yellow) / 100f)}、虹は ×{Num(1f + t.BonusPercentOf(MacaronColor.Rainbow) / 100f)}）";
                case SkillEffect.DropScale:
                    ScaleTuning scale = s.Scale;
                    return $"スケールを真下へ落とす（{scale.BaseGrams}g から毎秒 +{scale.GramsPerSecond}g。100g ごとに威力と吹き飛び +{scale.BonusPercentPer100Grams}%、" +
                        $"最大 +{scale.MaxBonusPercent}%。{scale.FatalGrams}g 以上で FATAL。幅 {Num(scale.HalfWidth * 2f)}m）";
                case SkillEffect.Inverter:
                    InverterTuning inv = s.Inverter;
                    return $"押すたびにオン・オフ。オンの間はワイヤーの射程 ×{Num(inv.RangeScale)}。外したワイヤーを押し続けると鞭（{inv.WhipRehitTicks}F ごと。攻撃しても続く）。" +
                        $"足場につなぐと雷の波動（感電 {inv.StunTicks}F。{Seconds(inv.WaveCooldownTicks)}に 1 回）";
                case SkillEffect.BallLightning:
                    BallLightningTuning ball = s.BallLightning;
                    return $"電気の球 {ball.Count} 個を {ball.IntervalTicks}F ごとに、放つ瞬間の照準の方向へ（{Num(ball.Speed)} m/s、半径 {Num(ball.Radius)}m。" +
                        $"{Num(ball.TravelDistance)}m で止まり {Seconds(ball.TrapTicks)}残る。当たったら消える）";
                case SkillEffect.SmokeBall:
                    ThiefTuning smoke = s.Thief;
                    return $"真下へ煙球を投げる（{Num(smoke.SmokeBallSpeed)} m/s）。着弾すると半径 {Num(smoke.ExplosionRadius)}m を攻撃し、半径 {Num(smoke.SmokeRadius)}m の煙を " +
                        $"{Seconds(smoke.SmokeTicks)}（中の物が見えない。見た目だけ）";
                case SkillEffect.TalentSnatch:
                    ThiefTuning snatch = s.Thief;
                    return $"照準の方向へ弾（{Num(snatch.SnatchSpeed)} m/s、{Num(snatch.SnatchRange)}m。威力・吹き飛びなし）。当たった人の Q をコピー（相手はそのまま使える）、" +
                        $"ステージのギミック・仕掛けに当てると奪う。もう一度 Q で使う・目の前に置く（{snatch.MaxPlaced} つまで）。使うか外れると、クールタイム";
                case SkillEffect.PlaceGimmick:
                    return "奪ったギミック・仕掛けを照準の方向の目の前に置く";
                case SkillEffect.Misdirection:
                    return $"分身を残して {Seconds(s.Thief.MisdirectionTicks)}透明になる（R 以外の行動・もう一度 E で解ける。分身は攻撃が当たると煙になって消える）";
                case SkillEffect.Cannon:
                    JuggernautTuning cannon = s.Juggernaut;
                    return $"前へ重力を受けない砲弾（{Num(cannon.CannonSpeed)} m/s、{Num(cannon.CannonRange)}m。最初に当たった人に当たって消える）";
                case SkillEffect.Flak:
                {
                    JuggernautTuning flak = s.Juggernaut;
                    string[] names = { "弱", "中", "強", "弩" };
                    IEnumerable<string> levels = flak.FlakSpeeds.Select((v, i) => $"{(i < names.Length ? names[i] : (i + 1).ToString(Invariant))} {Num(v)}");
                    string first = flak.FlakDefaultLevel < names.Length ? names[flak.FlakDefaultLevel] : (flak.FlakDefaultLevel + 1).ToString(Invariant);
                    return $"地上で Q を押すと構え（回数は使わない）、構えの間に Q で照準の方向へ重力のある砲弾を撃つ（{string.Join("・", levels)} m/s。上下で選び、" +
                        $"初めは{first}。試合の間覚えておく）。触れると半径 {Num(flak.FlakExplosionRadius)}m の爆発。Q 以外の行動・左右の移動・空中・回数切れで構えが解ける";
                }
                case SkillEffect.Mine:
                    JuggernautTuning mine = s.Juggernaut;
                    return $"照準の方向へ地雷（{Num(mine.MineSpeed)} m/s、{Num(mine.MineRange)}m）。地形・キャラクターに張り付いて一緒に動き、もう一度 E で半径 " +
                        $"{Num(mine.MineExplosionRadius)}m の爆発。クールタイムは爆発の後から";
                case SkillEffect.Mayday:
                    JuggernautTuning mayday = s.Juggernaut;
                    return $"ジェット機で飛び去り、ステージ全体から空いた場所を選んで運ばれる（その間は無敵。{Seconds(mayday.MaydaySelectTicks)}選ばなければ元の場所へ）";
                case SkillEffect.BankHeist:
                    ThiefTuning heist = s.Thief;
                    return $"拠点の中心から {Num(heist.HeistRange)}m 以内で、その場で詠唱。もう一度 R で止めると、1 秒 {heist.HeistCoinsPerSecond} 枚 ＋ 5 秒ごとに " +
                        $"{heist.HeistBonusPerFive} 枚 ＋ 10 秒ごとに {heist.HeistBonusPerTen} 枚（10 秒で {heist.HeistCoins(600)} 枚）。拠点が移ると止まって得る。のけぞると得ない";
                default:
                    throw new InvalidOperationException($"性能表に効果 {effect} の書き方がない（MoveTable.EffectNote）");
            }
        }

        // 攻撃の動き（当たったときだけの動きを除く）で進む距離（m）。
        private static float MotionDistance(AttackData attack, int charge)
        {
            float distance = 0f;
            foreach (AttackMotion motion in attack.Motions)
            {
                if (motion.RequiresHit || !attack.Uses(motion.Charge, charge))
                {
                    continue;
                }
                float speed = motion.AlongAim ? Math.Abs(motion.VelocityX) : (float)Math.Sqrt(motion.VelocityX * motion.VelocityX + motion.VelocityY * motion.VelocityY);
                distance += speed * motion.Ticks / SimTime.TicksPerSecond;
            }
            return distance;
        }

        private static string LabelOf(AttackSlot slot)
        {
            return Slots.First(s => s.Slot == slot).Label;
        }

        private static string Join(List<Entry> entries, Func<Entry, string> cell)
        {
            return entries.Count == 0 ? "—" : string.Join(" / ", entries.Select(cell));
        }

        private static string Num(float value)
        {
            return value.ToString("0.##", Invariant);
        }

        // ゲージの値（0.1% 単位）を「8%」「7.5%」の形に。
        private static string Percent(int gauge)
        {
            return (gauge / (float)CombatTuning.GaugePerPercent).ToString("0.#", Invariant) + "%";
        }

        private static string Seconds(int ticks)
        {
            return (ticks / (float)SimTime.TicksPerSecond).ToString("0.##", Invariant) + " 秒";
        }

        private static string Escape(string cell)
        {
            return cell.Replace("|", "\\|");
        }
    }
}
