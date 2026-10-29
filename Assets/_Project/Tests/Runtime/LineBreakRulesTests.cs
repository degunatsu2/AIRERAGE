using System;
using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Runtime.Data;
using Airerage.Runtime.Settings;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// 日本語の折り返しの決まり（禁則処理。LineBreakRules）と、キャラクター選択の技の説明が 4 行の枠に収まること（ユーザーの指摘、
    /// 2026-10-29: SOLDERER の E の説明が 4 行に収まっていない）。
    /// </summary>
    public sealed class LineBreakRulesTests
    {
        private const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
        private const string RosterPath = "Assets/_Project/Data/Characters/CharacterRoster.asset";
        private const int MaxJapaneseLines = 4;

        // 確かめる画面の大きさ（16:9 と、横の狭い 16:10）。
        private static readonly Vector2[] Screens = { new Vector2(1920f, 1080f), new Vector2(1440f, 900f) };

        [Test]
        public void FindBreak_MovesTheCharacterBeforeAPunctuationMark_ToTheNextLine()
        {
            // 「…が延び / る。」のように、行の頭が「。」になる所。
            const string text = "あいうえお。かきく";
            Assert.That(LineBreakRules.FindBreak(text, new List<int> { 0, 5 }), Is.EqualTo(4), "「お。」を次の行へ");
            // 句読点が続くときは、その前の字まで戻る。
            Assert.That(LineBreakRules.FindBreak("あいうえ」。かき", new List<int> { 0, 5 }), Is.EqualTo(3));
            // 開き括弧が行の終わりに来るときは、括弧ごと次の行へ。
            Assert.That(LineBreakRules.FindBreak("あいう「えお」か", new List<int> { 0, 4 }), Is.EqualTo(3));
            // 決まりに合っていれば直さない。改行の直後と、1 行目は見ない。
            Assert.That(LineBreakRules.FindBreak("あいうえおかきく", new List<int> { 0, 4 }), Is.EqualTo(-1));
            Assert.That(LineBreakRules.FindBreak("あいう\n。かき", new List<int> { 0, 4 }), Is.EqualTo(-1));
            // 前の行の字がなくなるほどなら直さない。
            Assert.That(LineBreakRules.FindBreak("、、、、", new List<int> { 0, 1, 2 }), Is.EqualTo(-1));
        }

        [Test]
        public void Wrap_NeverStartsALineWithAPunctuationMark()
        {
            var settings = Settings(GameFonts.Japanese, 22, 300f);
            // 句読点の直前で折り返す長さの文を、幅を変えて並べる。
            const string text = "活火山の火口の中。溶岩が噴き出し、壁の間欠泉から蒸気が吹き上がり、溶けた岩に弾き飛ばされる。";
            var generator = new TextGenerator();
            for (float width = 120f; width <= 600f; width += 7f)
            {
                settings.generationExtents = new Vector2(width, 10000f);
                string wrapped = LineBreakRules.Wrap(text, settings);
                Assert.That(wrapped.Replace("\n", string.Empty), Is.EqualTo(text), "字が変わった");
                generator.Populate(wrapped, settings);
                foreach (UILineInfo line in generator.lines)
                {
                    if (line.startCharIdx > 0 && line.startCharIdx < wrapped.Length)
                    {
                        char head = wrapped[line.startCharIdx];
                        Assert.That(LineBreakRules.NotAtLineStart.IndexOf(head), Is.LessThan(0), $"幅 {width}: 行の頭が「{head}」");
                    }
                }
            }
        }

        [Test]
        public void SkillDescriptions_FitTheBox_OnWideAndNarrowScreens()
        {
            Scene scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Additive);
            try
            {
                CharacterSelectScreen screen = null;
                foreach (CharacterSelectScreen found in UnityEngine.Object.FindObjectsByType<CharacterSelectScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (found.gameObject.scene == scene)
                    {
                        screen = found;
                    }
                }
                Assert.That(screen, Is.Not.Null, "キャラクター選択がない");
                var descriptions = (Text[])Field(screen, "_skillDescriptions");
                var cooldowns = (Text[])Field(screen, "_skillCooldowns");
                Text description = descriptions[0];
                // 説明の上から、クールタイムの行の上までの高さ（どちらも列の上からの位置で置いている）。
                float available = description.rectTransform.offsetMax.y - cooldowns[0].rectTransform.offsetMax.y;
                Assert.That(available, Is.GreaterThan(0f));

                var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
                var failures = new List<string>();
                var generator = new TextGenerator();
                foreach (Vector2 size in Screens)
                {
                    float width = WidthOf(description.rectTransform, CanvasWidth(description, size));
                    foreach (GameLanguage language in new[] { GameLanguage.Japanese, GameLanguage.English })
                    {
                        // 説明は、どちらの言語でもビルダーが付けた字体（M PLUS 1）。
                        TextGenerationSettings settings = Settings(description.font, description.fontSize, width);
                        settings.lineSpacing = description.lineSpacing;
                        for (int i = 0; i < roster.Count; i++)
                        {
                            CharacterConfigAsset character = roster.Get(i);
                            foreach (AttackSlot slot in CharacterSelectScreen.SkillSlots)
                            {
                                string wrapped = LineBreakRules.Wrap(character.TextOf(slot).DescriptionIn(language), settings);
                                generator.Populate(wrapped, settings);
                                float height = generator.GetPreferredHeight(wrapped, settings);
                                // 枠の高さに収まること（日本語は 4 行まで。英語は 1 行が低いので、高さに収まれば 5 行でもよい）。
                                bool tooMany = language == GameLanguage.Japanese && generator.lineCount > MaxJapaneseLines;
                                if (tooMany || height > available)
                                {
                                    failures.Add($"{size.x}x{size.y} {language} {character.DisplayName} {slot}: {generator.lineCount} 行・高さ {height:0} / {available:0}");
                                }
                            }
                        }
                    }
                }
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static TextGenerationSettings Settings(Font font, int fontSize, float width)
        {
            return new TextGenerationSettings
            {
                font = font,
                fontSize = fontSize,
                fontStyle = FontStyle.Normal,
                lineSpacing = 1f,
                richText = false,
                scaleFactor = 1f,
                color = Color.white,
                textAnchor = TextAnchor.UpperLeft,
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(width, 10000f),
                pivot = new Vector2(0f, 1f),
                updateBounds = true,
            };
        }

        // 画面の大きさ screen のときの Canvas の横幅（CanvasScaler の、幅と高さを混ぜて合わせる決まり）。
        private static float CanvasWidth(Component inside, Vector2 screen)
        {
            CanvasScaler scaler = inside.GetComponentInParent<CanvasScaler>(true);
            Vector2 reference = scaler.referenceResolution;
            float m = scaler.matchWidthOrHeight;
            float logScale = Mathf.Lerp(Mathf.Log(screen.x / reference.x, 2f), Mathf.Log(screen.y / reference.y, 2f), m);
            return screen.x / Mathf.Pow(2f, logScale);
        }

        // Canvas の横幅が canvasWidth のときの rect の横幅（親から順に、アンカーの割合と大きさの差を足す）。
        private static float WidthOf(RectTransform rect, float canvasWidth)
        {
            var chain = new List<RectTransform>();
            for (Transform t = rect; t != null && t.GetComponent<Canvas>() == null; t = t.parent)
            {
                chain.Add((RectTransform)t);
            }
            float width = canvasWidth;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                RectTransform r = chain[i];
                width = width * (r.anchorMax.x - r.anchorMin.x) + r.sizeDelta.x;
            }
            return width;
        }

        private static object Field(object target, string name)
        {
            var field = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name}.{name} がない");
            }
            return field.GetValue(target);
        }
    }
}
