using System.IO;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.Tests
{
    /// <summary>ゲームの字体（D-77: Orbitron ＋ M PLUS 1）が同梱され、足りない字を M PLUS 1 から借りること。</summary>
    public class GameFontsTests
    {
        private const string Folder = "Assets/_Project/Resources/Fonts/";

        [Test]
        public void AllFonts_Load_FromResources()
        {
            Assert.IsNotNull(GameFonts.Latin, "Orbitron Medium");
            Assert.IsNotNull(GameFonts.LatinBold, "Orbitron Bold");
            Assert.IsNotNull(GameFonts.LatinBlack, "Orbitron Black");
            Assert.IsNotNull(GameFonts.Japanese, "M PLUS 1 Regular");
            Assert.IsNotNull(GameFonts.JapaneseBold, "M PLUS 1 ExtraBold");
        }

        [Test]
        public void LicenseTexts_SitNextToTheFonts()
        {
            foreach (string name in new[] { "Orbitron-OFL.txt", "MPLUS1-OFL.txt" })
            {
                string text = File.ReadAllText(Folder + name);
                StringAssert.Contains("SIL OPEN FONT LICENSE Version 1.1", text, name);
                StringAssert.StartsWith("Copyright", text, name);
            }
        }

        [TestCase("Orbitron-Medium", "MPLUS1-Regular")]
        [TestCase("Orbitron-Bold", "MPLUS1-ExtraBold")]
        [TestCase("Orbitron-Black", "MPLUS1-ExtraBold")]
        public void Orbitron_FallsBackToMPlus1(string latin, string japanese)
        {
            var importer = (TrueTypeFontImporter)AssetImporter.GetAtPath(Folder + latin + ".ttf");
            Assert.IsNotNull(importer, latin);
            Assert.IsTrue(importer.includeFontData, "字体のファイルをビルドに入れる");
            // 足りない字は、字体の名前の一覧（M PLUS 1）と、その名前のどのファイルを使うか（参照）で借りる（両方がないと借りない）。
            CollectionAssert.Contains(importer.fontNames, "M PLUS 1", latin);
            var fallback = AssetDatabase.LoadAssetAtPath<Font>(Folder + japanese + ".ttf");
            CollectionAssert.AreEqual(new[] { fallback }, importer.fontReferences, $"{latin} の足りない字は {japanese} から借りる");
        }

        [Test]
        public void Orbitron_DrawsJapanese_ThroughTheFallback()
        {
            Font font = GameFonts.LatinBold;
            const string text = "A風弱中強弩はいいえ±▼";
            font.RequestCharactersInTexture(text, 32);
            foreach (char c in text)
            {
                Assert.IsTrue(font.GetCharacterInfo(c, out CharacterInfo info, 32), $"「{c}」が書けない");
                Assert.Greater(info.advance, 0, $"「{c}」の幅");
            }
        }

        [Test]
        public void BoldStyles_UseTheBoldFile_InsteadOfSyntheticBold()
        {
            var style = GameFonts.Use(new GUIStyle { fontStyle = FontStyle.BoldAndItalic });
            Assert.AreSame(GameFonts.LatinBold, style.font);
            Assert.AreEqual(FontStyle.Italic, style.fontStyle);

            var go = new GameObject("Text", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<Text>();
                text.fontStyle = FontStyle.Bold;
                GameFonts.Use(text);
                Assert.AreSame(GameFonts.LatinBold, text.font);
                Assert.AreEqual(FontStyle.Normal, text.fontStyle);
                // 言語を切り替えても太さはそのまま。
                Assert.AreSame(GameFonts.JapaneseBold, GameFonts.Switch(text.font, japanese: true));
                Assert.AreSame(GameFonts.LatinBold, GameFonts.Switch(GameFonts.JapaneseBold, japanese: false));
                Assert.AreSame(GameFonts.Japanese, GameFonts.Switch(GameFonts.Latin, japanese: true));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
