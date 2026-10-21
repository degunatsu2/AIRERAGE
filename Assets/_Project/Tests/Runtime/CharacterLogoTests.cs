using Airerage.Core.Simulation;
using Airerage.Runtime.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// キャラクターのロゴ（仮の絵。docs/13 T5-8）: 選べるキャラクター全員にロゴがあり、絵が描かれていて、色がユーザーの案のとおり
    /// （STANDARD 白、SPACE TRAVELER 紫、STORM RUNNER 黄緑、RISK TAKER 黄色、PASTRY CHEF クリーム色）。シーンのビルダーが作る。
    /// </summary>
    public sealed class CharacterLogoTests
    {
        private const string RosterPath = "Assets/_Project/Data/Characters/CharacterRoster.asset";

        [Test]
        public void EveryCharacter_HasALogo_InItsColor()
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            Assert.That(roster, Is.Not.Null, "キャラクターの一覧がない");
            Assert.That(roster.Count, Is.EqualTo(5));
            for (int i = 0; i < roster.Count; i++)
            {
                CharacterConfigAsset character = roster.Get(i);
                Texture2D logo = character.Logo;
                Assert.That(logo, Is.Not.Null, $"{character.DisplayName} にロゴがない");
                Assert.That(logo.width, Is.EqualTo(logo.height), $"{character.DisplayName} のロゴが正方形でない");
                Assert.That(logo.isReadable, Is.True);

                // 不透明な部分の、明るい色の平均（縁取りの暗い色は除く）。
                Color[] pixels = logo.GetPixels();
                Color sum = Color.clear;
                int opaque = 0;
                foreach (Color pixel in pixels)
                {
                    if (pixel.a > 0.9f && pixel.maxColorComponent > 0.5f)
                    {
                        sum += pixel;
                        opaque++;
                    }
                }
                Assert.That(opaque, Is.GreaterThan(pixels.Length / 10), $"{character.DisplayName} のロゴがほとんど描かれていない");
                Assert.That(opaque, Is.LessThan(pixels.Length * 8 / 10), $"{character.DisplayName} のロゴが塗りつぶされている");
                Color mean = sum / opaque;
                Color.RGBToHSV(mean, out float hue, out float saturation, out float value);
                string name = character.DisplayName;
                if (name == CharacterPresets.SpaceTravelerName)
                {
                    Assert.That(hue * 360f, Is.InRange(250f, 290f), $"{name} のロゴが紫でない（{mean}）");
                }
                else if (name == CharacterPresets.StormRunnerName)
                {
                    Assert.That(hue * 360f, Is.InRange(70f, 100f), $"{name} のロゴが黄緑でない（{mean}）");
                }
                else if (name == CharacterPresets.RiskTakerName)
                {
                    Assert.That(hue * 360f, Is.InRange(40f, 60f), $"{name} のロゴが黄色でない（{mean}）");
                }
                else if (name == CharacterPresets.PastryChefName)
                {
                    Assert.That(hue * 360f, Is.InRange(30f, 55f), $"{name} のロゴがクリーム色でない（{mean}）");
                    Assert.That(saturation, Is.InRange(0.12f, 0.4f), $"{name} のロゴがクリーム色でない（{mean}）");
                }
                else
                {
                    Assert.That(name, Is.EqualTo(CharacterPresets.StandardName));
                    Assert.That(saturation, Is.LessThan(0.1f), $"{name} のロゴが白でない（{mean}）");
                }
                Assert.That(value, Is.GreaterThan(0.9f), $"{name} のロゴが暗い（{mean}）");
            }
        }
    }
}
