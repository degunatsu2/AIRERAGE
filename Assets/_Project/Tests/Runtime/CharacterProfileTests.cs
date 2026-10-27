using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Runtime.Data;
using Airerage.Runtime.Settings;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEditor;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// キャラクター選択に出す文（docs/15 6.6。ユーザーの要望、2026-10-17）: 選べるキャラクター全員に、性能の種類のハッシュタグと、
    /// Q・E・R の技のカタカナの名前と説明がある（シーンのビルダーが CharacterProfiles から設定する）。説明の下の行は、クールタイム
    /// （スタック数が 2 以上なら、その数も）か、必殺技なら必要なゲージ。
    /// </summary>
    public sealed class CharacterProfileTests
    {
        private const string RosterPath = "Assets/_Project/Data/Characters/CharacterRoster.asset";

        [Test]
        public void EveryCharacter_HasHashtags_AndSkillNamesInKatakana_WithDescriptions()
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            Assert.That(roster, Is.Not.Null, "キャラクターの一覧がない");
            for (int i = 0; i < roster.Count; i++)
            {
                CharacterConfigAsset character = roster.Get(i);
                Assert.That(character.Tags, Is.Not.Empty, $"{character.DisplayName} にハッシュタグがない");
                foreach (string tag in character.Tags)
                {
                    Assert.That(tag, Does.StartWith("#"), $"{character.DisplayName} のハッシュタグ {tag}");
                }
                foreach (AttackSlot slot in CharacterSelectScreen.SkillSlots)
                {
                    SkillText text = character.TextOf(slot);
                    Assert.That(text, Is.Not.Null);
                    Assert.That(text.Name, Does.Match("^[゠-ヿ]+$"), $"{character.DisplayName} の {slot} の名前がカタカナでない");
                    Assert.That(text.Description, Is.Not.Empty, $"{character.DisplayName} の {slot} に説明がない");
                    Assert.That(text.Description, Does.Not.Contain(" "), $"{character.DisplayName} の {slot} の説明に半角の空白（そこで改行される）");
                    // 英語（オプションの LANGUAGE。2026-10-27）: 大文字の名前と、日本語の字のない説明。
                    Assert.That(text.NameIn(GameLanguage.English), Does.Match("^[A-Z ]+$"), $"{character.DisplayName} の {slot} の英語の名前");
                    Assert.That(text.DescriptionIn(GameLanguage.English), Does.Match("^[\\x20-\\x7E]+$"),
                        $"{character.DisplayName} の {slot} の英語の説明");
                    Assert.That(text.NameIn(GameLanguage.Japanese), Is.EqualTo(text.Name));
                }
                string[] english = character.TagsIn(GameLanguage.English);
                Assert.That(english.Length, Is.EqualTo(character.Tags.Length), $"{character.DisplayName} の英語のハッシュタグの数が違う");
                foreach (string tag in english)
                {
                    Assert.That(tag, Does.Match("^#[A-Za-z]+$"), $"{character.DisplayName} の英語のハッシュタグ {tag}");
                }
            }
        }

        [Test]
        public void CooldownLine_ShowsSeconds_Stacks_AndTheGaugeForTheSuper()
        {
            CombatTuning standard = CharacterPresets.Standard().Combat;
            Assert.That(CharacterSelectScreen.CooldownText(standard, AttackSlot.Special1, ""), Is.EqualTo("クールタイム 4 秒"));
            Assert.That(CharacterSelectScreen.CooldownText(standard, AttackSlot.Special2, ""), Is.EqualTo("クールタイム 6 秒"));
            Assert.That(CharacterSelectScreen.CooldownText(standard, AttackSlot.Super, ""), Is.EqualTo("必要ゲージ 70%"));

            CombatTuning space = CharacterPresets.SpaceTraveler().Combat;
            Assert.That(CharacterSelectScreen.CooldownText(space, AttackSlot.Special1, ""), Is.EqualTo("クールタイム 10 秒  ／  スタック 2"));

            CombatTuning odd = CharacterPresets.Standard().Combat;
            odd.Special1CooldownTicks = 90;
            odd.SuperGaugeCost = 755;
            Assert.That(CharacterSelectScreen.CooldownText(odd, AttackSlot.Special1, "効果が終わってから"),
                Is.EqualTo("クールタイム 1.5 秒（効果が終わってから）"));
            Assert.That(CharacterSelectScreen.CooldownText(odd, AttackSlot.Super, ""), Is.EqualTo("必要ゲージ 75.5%"));

            // 英語。
            Assert.That(CharacterSelectScreen.CooldownText(standard, AttackSlot.Special1, "", GameLanguage.English), Is.EqualTo("COOLDOWN 4s"));
            Assert.That(CharacterSelectScreen.CooldownText(standard, AttackSlot.Super, "", GameLanguage.English), Is.EqualTo("GAUGE 70%"));
            Assert.That(CharacterSelectScreen.CooldownText(space, AttackSlot.Special1, "", GameLanguage.English), Is.EqualTo("COOLDOWN 10s  /  STACKS 2"));
            Assert.That(CharacterSelectScreen.CooldownText(odd, AttackSlot.Special1, "after the effect ends", GameLanguage.English),
                Is.EqualTo("COOLDOWN 1.5s (after the effect ends)"));
        }
    }
}
