using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using NVector3 = System.Numerics.Vector3;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// 背景とオブジェクトのクオリティアップ（D-97。2026-11-17 のユーザーの決定。ATHLETIC で試作）: 見た目だけを変え、遊び（地形の当たり）は
    /// 変えないこと、決めた案（V1・V3・V7・V8・V10〜V15・V17〜V19・V21・V22）の部品がそろっていること。見え方そのものは確かめられない。
    /// </summary>
    public sealed class StageLookTests
    {
        private const string AthleticPath = "Assets/_Project/Scenes/Stages/Stage_Athletic.unity";

        private readonly List<Scene> _opened = new List<Scene>();

        [TearDown]
        public void TearDown()
        {
            foreach (Scene scene in _opened)
            {
                if (scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            _opened.Clear();
        }

        private Scene Open(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            _opened.Add(scene);
            Physics.SyncTransforms();
            return scene;
        }

        private static T[] InScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        }

        [Test]
        public void BlockMesh_KeepsTheBoxSize_AndFacesOutward()
        {
            var mesh = new Mesh();
            try
            {
                var size = new Vector3(16f, 1.5f, 4f);
                StageBlockMesh.Fill(mesh, size, 0.22f, 1.3f, true);
                Assert.That(mesh.bounds.size.x, Is.EqualTo(size.x).Within(1e-4f));
                Assert.That(mesh.bounds.size.y, Is.EqualTo(size.y).Within(1e-4f));
                Assert.That(mesh.bounds.size.z, Is.EqualTo(size.z).Within(1e-4f));
                Assert.That(mesh.bounds.center.magnitude, Is.LessThan(1e-4f), "箱の真ん中が中心でない");
                // 6 つの面・12 本の辺の面取り（各 2 つの三角形）・8 つの角（三角形）。
                Assert.That(mesh.triangles.Length / 3, Is.EqualTo(6 * 2 + 12 * 2 + 8));
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                int[] triangles = mesh.triangles;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]];
                    Vector3 b = vertices[triangles[t + 1]];
                    Vector3 c = vertices[triangles[t + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    Assert.That(Vector3.Dot(face, (a + b + c) / 3f), Is.GreaterThan(0f), $"三角形 {t / 3} が内側を向いている（見えなくなる）");
                    Assert.That(Vector3.Dot(face.normalized, normals[triangles[t]]), Is.GreaterThan(0.99f), $"三角形 {t / 3} の向きと法線が合わない");
                }
                var block = new List<Vector4>();
                mesh.GetUVs(1, block);
                Assert.That(block[0], Is.EqualTo(new Vector4(8f, 0.75f, 1.3f, 1f)), "シェーダーに箱の半分の大きさ・位相・刺さる足場かを渡していない");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BlockMesh_ThinBlocks_GetANarrowerBevel()
        {
            var mesh = new Mesh();
            try
            {
                StageBlockMesh.Fill(mesh, new Vector3(2.5f, 0.5f, 4f), 0.22f, 0f, false);
                // 正面（-Z を向いた面）の平らな所は、縁から面取りの幅だけ内側。薄い足場（0.5m）では 0.25 × 0.45 に細くなる。
                float expected = 0.25f * StageBlockMesh.MaxBevelRatio;
                float frontTop = mesh.vertices.Where((v, i) => mesh.normals[i] == Vector3.back).Max(v => v.y);
                Assert.That(0.25f - frontTop, Is.EqualTo(expected).Within(1e-4f), "薄い足場の面取りが太すぎる");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Impacts_ComeFromHardLandings_AndWireAttachments()
        {
            var before = new CharacterState { Position = new NVector3(0f, 5f, 0f), Velocity = new NVector3(0f, -12f, 0f) };
            CharacterState landed = before;
            landed.Grounded = true;
            landed.Position = new NVector3(2f, 1f, 0f);
            Assert.That(SurfaceImpactFeed.Detect(before, landed, 0.9f, 6f, out Vector3 point), Is.EqualTo(SurfaceImpactFeed.ImpactKind.Landing));
            Assert.That((point - new Vector3(2f, 0.1f, 0f)).magnitude, Is.LessThan(1e-4f), "足もとに輪を出さない");

            CharacterState gentle = before;
            gentle.Velocity = new NVector3(0f, -3f, 0f);
            Assert.That(SurfaceImpactFeed.Detect(gentle, landed, 0.9f, 6f, out _), Is.EqualTo(SurfaceImpactFeed.ImpactKind.None),
                "そっと着地しても輪を出す（段を下りるたびに光る）");

            CharacterState firing = before;
            firing.Wire.Phase = WirePhase.Firing;
            CharacterState attached = firing;
            attached.Wire.Phase = WirePhase.Attached;
            attached.Wire.Anchor = new NVector3(-4f, 8f, 0f);
            Assert.That(SurfaceImpactFeed.Detect(firing, attached, 0.9f, 6f, out point), Is.EqualTo(SurfaceImpactFeed.ImpactKind.WireAttach));
            Assert.That(point, Is.EqualTo(new Vector3(-4f, 8f, 0f)), "ワイヤーがつながった所に輪を出さない");
            Assert.That(SurfaceImpactFeed.Detect(attached, attached, 0.9f, 6f, out _), Is.EqualTo(SurfaceImpactFeed.ImpactKind.None),
                "つながっている間ずっと輪を出す");
        }

        [Test]
        public void Athletic_TerrainKeepsItsBoxes_AndGetsBeveledVisuals()
        {
            Scene scene = Open(AthleticPath);
            BoxCollider[] terrain = InScene<BoxCollider>(scene);
            Assert.That(terrain.Length, Is.EqualTo(15), "地形の箱の数が変わった（遊びが変わる）");
            StageBlock[] blocks = scene.GetRootGameObjects().Single(g => g.name == "TerrainVisuals").GetComponentsInChildren<StageBlock>();
            Assert.That(blocks.Length, Is.EqualTo(terrain.Length), "地形の箱ごとに面取りした見た目がない");
            foreach (BoxCollider box in terrain)
            {
                Assert.That(box.GetComponent<MeshRenderer>(), Is.Null, $"{box.name}: 元の箱の見た目が残っている（面取りした見た目と重なる）");
                StageBlock block = blocks.SingleOrDefault(b => b.name == box.name);
                Assert.That(block, Is.Not.Null, $"{box.name} の見た目がない");
                Assert.That((block.transform.position - box.bounds.center).magnitude, Is.LessThan(1e-3f), $"{box.name} の見た目が当たりとずれている");
                Assert.That((block.Size - box.bounds.size).magnitude, Is.LessThan(1e-3f), $"{box.name} の見た目の大きさが当たりと違う");
                bool attachable = box.GetComponent<WireAttachable>() != null;
                Assert.That(block.Attachable, Is.EqualTo(attachable), $"{box.name}: 刺さる足場かが見た目と違う");
                Assert.That(block.GetComponent<MeshRenderer>().sharedMaterial.shader.name, Is.EqualTo("Airerage/StageSurface"));
                // V21: 小さな浮いた刺さらない足場（飛び石）だけ、見た目が呼吸する。
                bool small = box.bounds.size.x <= 4f && box.bounds.size.y <= 1f;
                Assert.That(block.BreathAmplitude > 0f, Is.EqualTo(small && !attachable), $"{box.name}: 呼吸させるかが違う");
                Assert.That(block.BreathAmplitude, Is.LessThanOrEqualTo(0.06f), "呼吸が大きすぎる（当たりとずれて見える）");
            }
        }

        [Test]
        public void Athletic_Decoration_HasNoColliders()
        {
            // 飾り（遠景・格子・膜・足場の見た目）に当たりがあると、地形として読み込まれて遊びが変わる。
            Scene scene = Open(AthleticPath);
            foreach (Collider collider in InScene<Collider>(scene))
            {
                Assert.That(collider.transform.parent != null && collider.transform.parent.name == "Terrain", Is.True,
                    $"地形の外に当たりがある: {collider.name}");
            }
        }

        [Test]
        public void Athletic_HasTheSkyFogVolumeBackdropGridAndMembranes()
        {
            Scene scene = Open(AthleticPath);
            SceneManager.SetActiveScene(scene);
            Assert.That(RenderSettings.skybox.shader.name, Is.EqualTo("Airerage/GradientSky"), "グラデーションの空でない（V15）");
            Assert.That(RenderSettings.fog, Is.True, "霧がない（V15）");
            Assert.That(RenderSettings.fogStartDistance, Is.GreaterThan(50f), "霧が遊ぶ所（カメラから約 30m）にかかる");

            Volume volume = InScene<Volume>(scene).Single();
            Assert.That(volume.isGlobal, Is.True);
            Assert.That(volume.sharedProfile.TryGet(out Bloom bloom), Is.True, "光のにじみがない（V13）");
            Assert.That(bloom.intensity.value, Is.GreaterThan(0f));
            Assert.That(bloom.threshold.value, Is.GreaterThanOrEqualTo(1f), "光る物以外までにじむ");

            Transform backdrop = scene.GetRootGameObjects().Single(g => g.name == "Backdrop").transform;
            foreach (string layer in new[] { "Near", "Mid", "Far", "Airships" })
            {
                Assert.That(backdrop.Find(layer).childCount, Is.GreaterThan(0), $"遠景の {layer} がない（V14）");
            }
            foreach (Renderer renderer in backdrop.GetComponentsInChildren<Renderer>())
            {
                Assert.That(renderer.bounds.min.z, Is.GreaterThan(60f), $"遠景が遊ぶ所の近くにある: {renderer.name}");
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off), $"遠景が影を落とす: {renderer.name}");
            }
            // 世界観は現代日本: 浮かぶ島は置かず、運営の撮影の飛行船は 1 隻だけ（2026-11-17 の要望）。
            Assert.That(backdrop.GetComponentsInChildren<Transform>().Any(t => t.name == "Island" || t.name == "Ring"), Is.False, "浮かぶ島が残っている");
            Assert.That(backdrop.GetComponentsInChildren<BackdropMotion>().Length, Is.EqualTo(1), "横切る飛行船が 1 隻でない（V17）");
            Assert.That(backdrop.Find("Airships").childCount, Is.EqualTo(1));

            GameObject grid = scene.GetRootGameObjects().Single(g => g.name == "LightGrid");
            Assert.That(grid.transform.position.y, Is.LessThan(-22f - 20f), "光の格子が撃破の線の近くにある（V18）");

            Transform bounds = scene.GetRootGameObjects().Single(g => g.name == "KnockoutBounds").transform;
            Assert.That(bounds.Find("Frame"), Is.Null, "前の赤い枠の線が残っている");
            Transform membranes = bounds.Find("Membranes");
            Assert.That(membranes.childCount, Is.EqualTo(4), "撃破の線の膜が 4 辺にない（V19）");
            foreach (Renderer renderer in membranes.GetComponentsInChildren<Renderer>())
            {
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Airerage/KillMembrane"));
            }
        }

        [Test]
        public void Athletic_ImpactRings_AreSubtle()
        {
            // 着地とワイヤーの光の輪はかなり控えめに（2026-11-17 の要望）: にじまない明るさ・短い時間・細い輪。
            Scene scene = Open(AthleticPath);
            foreach (StageBlock block in InScene<StageBlock>(scene))
            {
                Material material = block.GetComponent<MeshRenderer>().sharedMaterial;
                Color impact = material.GetColor("_ImpactColor");
                Assert.That(Mathf.Max(impact.r, Mathf.Max(impact.g, impact.b)), Is.LessThan(1f), $"{block.name}: 光の輪がにじむほど明るい");
                Assert.That(material.GetFloat("_ImpactSeconds"), Is.LessThanOrEqualTo(0.5f));
                Assert.That(material.GetFloat("_ImpactSpeed") * material.GetFloat("_ImpactSeconds"), Is.LessThan(3.5f), "光の輪が広がりすぎる");
            }
        }

        [Test]
        public void Athletic_GimmicksAnchorsAndRims_MatchThePlatforms()
        {
            // ギミックも足場と同じ質感に（2026-11-17 の要望）。
            Scene scene = Open(AthleticPath);
            foreach (StageZoneView view in InScene<StageZoneView>(scene))
            {
                Assert.That(view.LineMaterial.shader.name, Is.EqualTo("Airerage/GlowLine"), $"{view.name}: 線が光る線でない");
                // 予告の斜線と記号は今までの材質（光る線の材質で描くと光の塊になった。2026-11-17 の指摘）。
                Assert.That(view.OverlayMaterial.shader.name, Is.EqualTo("Sprites/Default"), $"{view.name}: 予告の板が光る線の材質");
                var zone = view.GetComponent<StageZoneAuthoring>();
                Transform[] parts = view.GetComponentsInChildren<Transform>(true);
                switch (zone.Kind)
                {
                    case StageZoneKind.JumpPad:
                        Assert.That(parts.Any(t => t.name == "PadBase" && t.GetComponent<StageBlock>() != null), Is.True, $"{view.name}: 面取りした台がない");
                        Assert.That(parts.Single(t => t.name == "PadGlow").GetComponent<Renderer>().sharedMaterial.shader.name, Is.EqualTo("Airerage/Emissive"));
                        Assert.That(parts.Any(t => t.name == "Pad"), Is.False, "前の平らな台が残っている");
                        break;
                    case StageZoneKind.Updraft:
                        Assert.That(parts.Single(t => t.name == "Column").GetComponent<Renderer>().sharedMaterial.shader.name, Is.EqualTo("Airerage/WindColumn"),
                            $"{view.name}: 風が光の柱でない");
                        break;
                    case StageZoneKind.BoostRing:
                        Assert.That(parts.Count(t => t.name == "Piece" && t.GetComponent<StageBlock>() != null), Is.GreaterThanOrEqualTo(12), $"{view.name}: 枠がない");
                        break;
                }
                foreach (Collider collider in view.GetComponentsInChildren<Collider>(true))
                {
                    Assert.Fail($"{view.name}: ギミックの見た目に当たりがある: {collider.name}");
                }
            }
            foreach (WireAnchorPoint anchor in InScene<WireAnchorPoint>(scene))
            {
                Assert.That(anchor.GetComponent<StageBlock>(), Is.Not.Null, $"{anchor.name}: アンカーが面取りした菱形でない");
                Assert.That(anchor.transform.Find("Core"), Is.Not.Null, $"{anchor.name}: 光る芯がない");
            }
            AttachableGlow glow = InScene<AttachableGlow>(scene).Single();
            Assert.That(glow.GetComponentsInChildren<Renderer>(true).Any(r => r.name == "Surface"), Is.False,
                "刺さる足場の正面に薄いオレンジの板が残っている（暗い面が赤茶色に濁る）");
            var rims = new SerializedObject(glow).FindProperty("_rims");
            for (int i = 0; i < rims.arraySize; i++)
            {
                var rim = (Renderer)rims.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.That(rim.sharedMaterial.shader.name, Is.EqualTo("Airerage/Emissive"), "オレンジの縁がにじまない");
                if (rim.name == "TopRim")
                {
                    Assert.That(rim.transform.localScale.z, Is.LessThan(0.2f), "上の面をオレンジの板が覆っている（陰影が見えない）");
                }
            }
        }

        private static IEnumerable<string> DressedStages()
        {
            return new[]
            {
                "Stage_Athletic", "Stage_CityDay", "Stage_CityNight", "Stage_Volcano", "Stage_SnowSea", "Stage_SpaceStation", "Stage_Lab",
                "Stage_Kitchen", "Stage_Manor", "Stage_Stadium", "Stage_Wharf",
            };
        }

        [TestCaseSource(nameof(DressedStages))]
        public void EveryStage_IsDressed_WithoutChangingTheTerrain(string stage)
        {
            // ほかのステージにも当てる（2026-11-17 の要望）。地形の当たりはそのまま、見た目だけ。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            SceneManager.SetActiveScene(scene);
            Assert.That(RenderSettings.skybox.shader.name, Is.EqualTo("Airerage/GradientSky"), "グラデーションの空でない");
            BoxCollider[] terrain = scene.GetRootGameObjects().Single(g => g.name == "Terrain").GetComponentsInChildren<BoxCollider>();
            StageBlock[] blocks = scene.GetRootGameObjects().Single(g => g.name == "TerrainVisuals").GetComponentsInChildren<StageBlock>();
            Assert.That(blocks.Length, Is.EqualTo(terrain.Length), "地形の箱ごとに面取りした見た目がない");
            foreach (BoxCollider box in terrain)
            {
                Assert.That(box.GetComponent<MeshRenderer>(), Is.Null, $"{box.name}: 元の箱の見た目が残っている");
            }
            Assert.That(InScene<Volume>(scene).Single().sharedProfile.TryGet(out Bloom _), Is.True, "仕上げの画面処理がない");
            Transform bounds = scene.GetRootGameObjects().Single(g => g.name == "KnockoutBounds").transform;
            Assert.That(bounds.Find("Membranes").childCount, Is.EqualTo(4), "撃破の線の膜がない");
            Assert.That(bounds.Find("Frame"), Is.Null, "前の赤い枠の線が残っている");
            foreach (StageZoneView view in InScene<StageZoneView>(scene))
            {
                Assert.That(view.LineMaterial.shader.name, Is.EqualTo("Airerage/GlowLine"), $"{view.name}: 線が光る線でない");
                Assert.That(view.OverlayMaterial.shader.name, Is.EqualTo("Sprites/Default"), $"{view.name}: 予告の板が光る線の材質（光の塊になる）");
                Transform column = view.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Column");
                Renderer columnRenderer = column != null ? column.GetComponent<Renderer>() : null;
                if (columnRenderer != null && columnRenderer.sharedMaterial.shader.name == "Airerage/WindColumn")
                {
                    // 風は気流の筋と粒だけ（2026-11-17 の要望）。前の矢印の形の筋を重ねない。
                    Assert.That(view.WindLines, Is.False, $"{view.name}: 風に前の筋が重なる");
                }
            }
            foreach (Collider collider in InScene<Collider>(scene))
            {
                Assert.That(collider.transform.parent != null && collider.transform.parent.name == "Terrain", Is.True,
                    $"地形の外に当たりがある: {collider.name}");
            }
            // 奥の背景の飾りは影を受けず落とさない（影はカメラからの距離の球の中だけに出るので、奥の壁ではプレイヤーの周りの円の
            // 中だけ影が出ていた。2026-11-17 の指摘: キッチンの奥の壁のタイルの線が、プレイヤーの周りだけ太い）。
            // ATHLETIC は配置のデータ（StageLayout）から作らないので、飾り（Decor）がない。
            GameObject decor = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Decor");
            Assert.That(decor != null || stage == "Stage_Athletic", Is.True, "飾りがない");
            foreach (Renderer renderer in decor != null ? decor.GetComponentsInChildren<Renderer>(true) : new Renderer[0])
            {
                if (renderer.bounds.min.z >= BackgroundShadowDepth)
                {
                    Assert.That(renderer.receiveShadows, Is.False, $"{renderer.name}: 奥の飾りが影を受ける（円の中だけ影が出る）");
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off), $"{renderer.name}: 奥の飾りが影を落とす");
                }
            }
        }

        // StageDressing.BackgroundShadowDepth と同じ（m）。
        private const float BackgroundShadowDepth = 1f;

        [Test]
        public void CityNight_IsDressedForTheNight()
        {
            Scene scene = Open("Assets/_Project/Scenes/Stages/Stage_CityNight.unity");
            SceneManager.SetActiveScene(scene);
            Assert.That(RenderSettings.skybox.shader.name, Is.EqualTo("Airerage/GradientSky"));
            Assert.That(RenderSettings.skybox.GetFloat("_Stars"), Is.GreaterThan(0f), "夜空に星がない");
            BoxCollider[] terrain = InScene<BoxCollider>(scene);
            StageBlock[] blocks = scene.GetRootGameObjects().Single(g => g.name == "TerrainVisuals").GetComponentsInChildren<StageBlock>();
            Assert.That(blocks.Length, Is.EqualTo(terrain.Length), "ビルの箱ごとに面取りした見た目がない");
            Assert.That(scene.GetRootGameObjects().Any(g => g.name == "LightGrid"), Is.False, "下に道路のある街に光の格子がある");
            Assert.That(InScene<Volume>(scene).Single().sharedProfile.TryGet(out Bloom _), Is.True);
            Transform bounds = scene.GetRootGameObjects().Single(g => g.name == "KnockoutBounds").transform;
            Assert.That(bounds.Find("Membranes").childCount, Is.EqualTo(4));
        }

        [Test]
        public void MatchScenes_UsePostProcessing_TheImpactFeed_AndTheBankPillars()
        {
            foreach (string path in new[] { "Assets/_Project/Scenes/Modes/Match_Training.unity", "Assets/_Project/Scenes/Modes/Match_Local.unity",
                         "Assets/_Project/Scenes/Modes/Match_Online.unity" })
            {
                Scene scene = Open(path);
                Camera camera = InScene<Camera>(scene).Single();
                Assert.That(camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing, Is.True, $"{path}: 仕上げの画面処理がオフ（V13）");
                SurfaceImpactFeed feed = InScene<SurfaceImpactFeed>(scene).Single();
                Assert.That(new SerializedObject(feed).FindProperty("_source").objectReferenceValue, Is.Not.Null, $"{path}: 光の輪の元がない（V22）");
                BankView bank = InScene<BankView>(scene).Single();
                var serialized = new SerializedObject(bank);
                foreach (string field in new[] { "_pillar", "_nextPillar" })
                {
                    var pillar = (Renderer)serialized.FindProperty(field).objectReferenceValue;
                    Assert.That(pillar, Is.Not.Null, $"{path}: 拠点の光の柱がない（V12）");
                    Assert.That(pillar.sharedMaterial.shader.name, Is.EqualTo("Airerage/LightPillar"));
                }
            }
        }

        [Test]
        public void DefaultPostProcessing_IsNeutral_SoStagesWithoutAVolumeLookTheSame()
        {
            // URP の設定のアセットの既定の Volume（テンプレートの Bloom・モーションブラーなど）は外した。ステージの Volume だけが効く。
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.volumeProfile, Is.Null, "既定の Volume が残っている（モーションブラーなどが全ステージに効く）");
        }
    }
}
