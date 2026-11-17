using System.Collections;
using System.Text;
using Airerage.Runtime.Flow;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 字体の絵が作り直されたら、出ているすべての文字を描き直させる（FontRebuildGuard。2026-11-17 の指摘: 見出しの文字が時たま白い四角に
    /// なる）。Unity は作り直した字体を使う文字しか描き直さないので、別の字体の作り直しでも描き直すことを確かめる。
    /// </summary>
    public sealed class FontRebuildGuardTests
    {
        [UnityTest]
        public IEnumerator Rebuild_OfAnyFont_RedrawsEveryShownText()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            float start = Time.realtimeSinceStartup;
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                Assert.That(Time.realtimeSinceStartup - start, Is.LessThan(10f), "メニューにならない");
                yield return null;
            }
            var guard = Object.FindFirstObjectByType<FontRebuildGuard>();
            Assert.That(guard, Is.Not.Null, "いつも読み込むシーンに字体の守りがない");

            var root = new GameObject("TestCanvas", typeof(RectTransform));
            try
            {
                root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(root.transform, false);
                var text = textGo.AddComponent<Text>();
                text.font = GameFonts.Latin;
                text.text = "NETWORK VS";
                var counter = textGo.AddComponent<MeshCounter>();
                for (int i = 0; i < 3; i++)
                {
                    yield return null;
                }
                int before = counter.Count;
                int refreshes = guard.Refreshes;
                // 別の字体（日本語の太字）の絵を、作り直されるまで大きくする。
                bool rebuilt = false;
                System.Action<Font> onRebuilt = font => rebuilt |= font == GameFonts.JapaneseBold;
                Font.textureRebuilt += onRebuilt;
                try
                {
                    var rng = new System.Random(11);
                    for (int n = 0; n < 40 && !rebuilt; n++)
                    {
                        var chars = new StringBuilder();
                        for (int k = 0; k < 400; k++)
                        {
                            chars.Append((char)rng.Next(0x4E00, 0x9FA0));
                        }
                        GameFonts.JapaneseBold.RequestCharactersInTexture(chars.ToString(), 40 + n * 3, FontStyle.Normal);
                    }
                }
                finally
                {
                    Font.textureRebuilt -= onRebuilt;
                }
                Assert.That(rebuilt, Is.True, "前提: 日本語の太字の絵が作り直された");
                for (int i = 0; i < 4; i++)
                {
                    yield return null;
                }
                Assert.That(guard.Refreshes, Is.GreaterThan(refreshes), "字体の絵が作り直されても、守りが文字を描き直させない");
                Assert.That(counter.Count, Is.GreaterThan(before), "別の字体の絵が作り直されたのに、文字が描き直されない");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        // 文字の網が作り直された回数を数える。
        private sealed class MeshCounter : BaseMeshEffect
        {
            public int Count;

            public override void ModifyMesh(VertexHelper vh)
            {
                Count++;
            }
        }
    }
}
