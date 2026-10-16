using System;
using Airerage.Runtime.Flow;
using NUnit.Framework;
using UnityEditor;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// ステージの一覧（docs/15 T6-2）: ステージがあり、どのステージのシーンもビルドの設定に入っていて、起動するシーンが App である
    /// （GameFlow がシーンを名前で読み込むため）。
    /// </summary>
    public sealed class StageCatalogTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Stages/StageCatalog.asset";

        [Test]
        public void EveryStage_HasASceneInTheBuild_AndTheAppSceneStartsFirst()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, "ステージの一覧がない");
            Assert.That(catalog.Count, Is.GreaterThan(0));
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Length, Is.GreaterThan(0));
            Assert.That(scenes[0].path, Is.EqualTo("Assets/_Project/Scenes/App.unity"), "起動するシーンが App でない");
            foreach (StageDefinition stage in catalog.Stages)
            {
                Assert.That(stage, Is.Not.Null);
                Assert.That(stage.SceneName, Is.Not.Empty, $"{stage.name} にシーンがない");
                EditorBuildSettingsScene scene = Array.Find(scenes, s => s.enabled && s.path.EndsWith("/" + stage.SceneName + ".unity"));
                Assert.That(scene, Is.Not.Null, $"{stage.DisplayName} のシーン {stage.SceneName} がビルドの設定にない");
            }
            foreach (string name in new[] { "Menu", "Match_Training", "Sandbox_Online" })
            {
                Assert.That(Array.Exists(scenes, s => s.enabled && s.path.EndsWith("/" + name + ".unity")), Is.True, $"{name} がビルドの設定にない");
            }
        }
    }
}
