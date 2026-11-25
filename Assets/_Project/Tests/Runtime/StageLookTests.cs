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
                if (renderer.name == "Streets")
                {
                    // はるか下の西新宿の道路の地面は、ステージの真下から奥へ敷く（2026-11-18 の要望: 奥の背景の根元までを地面に）。
                    Assert.That(renderer.bounds.max.y, Is.LessThan(-100f), "下の道路の地面が遊ぶ所の近くにある");
                }
                else
                {
                    Assert.That(renderer.bounds.min.z, Is.GreaterThan(60f), $"遠景が遊ぶ所の近くにある: {renderer.name}");
                }
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
        public void EveryStage_HasASkyThatFitsTheStage(string stage)
        {
            // 2026-11-18 の要望: 最奥の空をステージに合ったリアルな空模様に。昼は雲、夜は月と星、宇宙は星雲と地球、溶岩は灰の雲と火の粉。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            SceneManager.SetActiveScene(scene);
            Material sky = RenderSettings.skybox;
            Assert.That(sky.shader.name, Is.EqualTo("Airerage/GradientSky"));
            // 月と惑星は空に描く（飾りの平たい球を重ねると 2 つ見える）。
            Assert.That(InScene<Renderer>(scene).Any(r => r.name == "Moon" || r.name == "Planet"), Is.False, "飾りの月か惑星が空の物と重なる");
            bool night = stage == "Stage_CityNight" || stage == "Stage_Manor";
            bool space = stage == "Stage_SpaceStation";
            if (space)
            {
                Assert.That(sky.GetFloat("_Planet"), Is.EqualTo(1f), "宇宙に地球がない");
                Assert.That(sky.GetFloat("_Nebula"), Is.GreaterThan(0f), "宇宙に星雲がない");
                Assert.That(sky.GetFloat("_Stars"), Is.GreaterThan(0.5f), "宇宙に星が少ない");
                Assert.That(sky.GetFloat("_CloudCover"), Is.EqualTo(0f), "宇宙に雲がある");
                return;
            }
            Assert.That(sky.GetFloat("_CloudCover"), Is.GreaterThan(0.3f), "雲がない");
            Assert.That(sky.GetFloat("_Planet"), Is.EqualTo(0f));
            Assert.That(sky.GetFloat("_Nebula"), Is.EqualTo(0f));
            if (night)
            {
                Assert.That(sky.GetColor("_MoonColor").maxColorComponent, Is.GreaterThan(0.5f), "夜に月がない");
                Assert.That(sky.GetFloat("_Stars"), Is.GreaterThan(0f), "夜に星がない");
                Vector4 sun = sky.GetVector("_SunDirection");
                Vector4 moon = sky.GetVector("_MoonDirection");
                Assert.That(Vector3.Dot(((Vector3)sun).normalized, ((Vector3)moon).normalized), Is.GreaterThan(0.99f), "夜の雲が月の側から照らされない");
                if (stage == "Stage_Manor")
                {
                    Color red = sky.GetColor("_MoonColor");
                    Assert.That(red.r, Is.GreaterThan(red.g * 2f), "恐怖の館の満月が赤くない");
                }
            }
            else
            {
                Assert.That(sky.GetColor("_MoonColor").maxColorComponent, Is.EqualTo(0f), "昼に月がある");
            }
            Assert.That(sky.GetFloat("_Embers"), stage == "Stage_Volcano" ? Is.GreaterThan(0f) : Is.EqualTo(0f), "火の粉は溶岩のステージだけ");
            if (stage == "Stage_Volcano" || stage == "Stage_CityNight")
            {
                Assert.That(sky.GetColor("_CloudUnder").maxColorComponent, Is.GreaterThan(0.2f), "雲の底を下から照らす光がない");
            }
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
            // 自前の模型で見せる地形（名前 + "Model" の飾り。キッチンのトースター）は、面取りした見た目を作らない。
            int models = terrain.Count(b => InScene<Transform>(scene).Any(t => t.name == b.name + "Model"));
            Assert.That(blocks.Length, Is.EqualTo(terrain.Length - models), "地形の箱ごとに面取りした見た目がない");
            foreach (BoxCollider box in terrain)
            {
                Assert.That(box.GetComponent<MeshRenderer>(), Is.Null, $"{box.name}: 元の箱の見た目が残っている");
            }
            Assert.That(InScene<Volume>(scene).Single().sharedProfile.TryGet(out Bloom _), Is.True, "仕上げの画面処理がない");
            Transform bounds = scene.GetRootGameObjects().Single(g => g.name == "KnockoutBounds").transform;
            Assert.That(bounds.Find("Membranes").childCount, Is.EqualTo(4), "撃破の線の膜がない");
            Assert.That(bounds.Find("Frame"), Is.Null, "前の赤い枠の線が残っている");
            // 光の格子（認識阻害の結界。2026-11-18 の要望で全ステージに）。撃破の線の下にある。
            GameObject grid = scene.GetRootGameObjects().SingleOrDefault(g => g.name == "LightGrid");
            Assert.That(grid, Is.Not.Null, "光の格子がない");
            Assert.That(grid.GetComponent<Renderer>().sharedMaterial.shader.name, Is.EqualTo("Airerage/StageGrid"));
            Assert.That(grid.transform.position.y, Is.LessThan(bounds.GetComponent<KnockoutBounds>().Min.y), "光の格子が撃破の線より上にある");
            // 目を凝らすと見える程度に薄く（2026-11-18 の要望）。
            Color line = grid.GetComponent<Renderer>().sharedMaterial.GetColor("_LineColor");
            Assert.That(Mathf.Max(line.r, Mathf.Max(line.g, line.b)), Is.LessThan(0.35f), "光の格子が明るすぎる");
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

        [TestCase("Stage_CityDay")]
        [TestCase("Stage_CityNight")]
        public void CityAvenue_HasRoadSidewalksHalfTheLampsAndTraffic(string stage)
        {
            // 手前の大通り（2026-11-25 の要望: 電灯を半分にして置き直し、アセットで歩道と車道、人影と車。docs/15 6.70）。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            GameObject[] roots = scene.GetRootGameObjects();
            Transform[] all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform avenue = all.Single(t => t.name == "Avenue" && t.Find("Roadway") != null);
            Material road = avenue.Find("Roadway").GetComponent<Renderer>().sharedMaterial;
            Texture asphalt = road.HasProperty("_BaseMap") ? road.GetTexture("_BaseMap") : road.GetTexture("_MainTex");
            Assert.That(asphalt != null && asphalt.name.Contains("asphalt"), Is.True, "車道に舗装（アスファルト）の写真の絵がない");
            Assert.That(all.Count(t => t.name == "Sidewalk"), Is.EqualTo(2), "両側の歩道がない");
            Assert.That(all.Count(t => t.name == "Curb"), Is.EqualTo(2), "縁石がない");
            Assert.That(avenue.Find("Markings").GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(400), "道路の標示がない");
            // 道路照明は前の半分（20 → 10）、左右交互（千鳥）。前の Kenney の街灯は置かない。
            Transform[] lamps = all.Where(t => t.name == "AvenueLamp").ToArray();
            Assert.That(lamps.Length, Is.EqualTo(10), "道路照明が前の半分の数でない");
            Assert.That(lamps.Count(t => t.position.x < 0f), Is.EqualTo(5), "道路照明が左右交互でない");
            Assert.That(all.Any(t => t.name == "Prop_light-curved"), Is.False, "前の街灯が残っている");
            Assert.That(all.Count(t => t.name.StartsWith("Prop_CommonTree")), Is.GreaterThan(20), "街路樹がない");
            StreetTrafficView traffic = all.Select(t => t.GetComponent<StreetTrafficView>()).Single(v => v != null);
            Assert.That(traffic.IsNight, Is.EqualTo(stage == "Stage_CityNight"));
            // 大通りの上に建物を置かない（歩道の外の端 13 m まで）。
            foreach (Transform block in all.Where(t => t.name == "Block"))
            {
                Bounds bounds = block.GetComponent<Renderer>().bounds;
                Assert.That(bounds.min.x > 13f || bounds.max.x < -13f, Is.True, $"{block.name}: 大通りの上に建物がある（x {bounds.min.x:0.0}〜{bounds.max.x:0.0}）");
            }
        }

        [TestCase("Stage_Athletic")]
        [TestCase("Stage_CityDay")]
        [TestCase("Stage_CityNight")]
        public void CityBackdrops_ShowTheRealTokyoSkyline(string stage)
        {
            // 無料の背景のアセット: PLATEAU の西新宿の高層ビル群と周りの街を遠景の真ん中の奥に（2026-11-17 のユーザーの決定）。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            Transform tokyo = scene.GetRootGameObjects().Single(g => g.name == "Backdrop").transform.Find("Tokyo");
            Assert.That(tokyo, Is.Not.Null, "実在の街がない");
            foreach (string name in new[] { "Shinjuku", "Surroundings" })
            {
                MeshFilter mesh = tokyo.Find(name).GetComponent<MeshFilter>();
                Assert.That(mesh.sharedMesh, Is.Not.Null, $"{name}: 形がない");
                Assert.That(mesh.sharedMesh.triangles.Length / 3, Is.GreaterThan(10000), $"{name}: 建物が少なすぎる");
                Assert.That(mesh.GetComponent<Renderer>().sharedMaterial.shader.name, Is.EqualTo("Airerage/StageBackdrop"), $"{name}: 遠景の材質でない");
                Assert.That(mesh.GetComponent<Collider>(), Is.Null, $"{name}: 当たりがある");
            }
            Assert.That(tokyo.Find("Beacons").childCount, Is.GreaterThan(5), "高いビルに航空障害灯がない");
            // 西新宿は外壁の写真つき（ほぼシルエットに見えないように。2026-11-18 の要望）。
            Material shinjuku = tokyo.Find("Shinjuku").GetComponent<Renderer>().sharedMaterial;
            if (stage != "Stage_Athletic")
            {
                // ビル街は、道路の地図を貼った地面の上に建物が立つ（根元を切らない。2026-11-18 の要望）。
                Transform streets = tokyo.Find("Streets");
                Assert.That(streets, Is.Not.Null, "道路の地面がない");
                Assert.That(streets.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex"), Is.Not.Null, "道路の地図がない");
                float bottom = tokyo.Find("Shinjuku").GetComponent<Renderer>().bounds.min.y;
                Assert.That(bottom, Is.EqualTo(streets.position.y).Within(2f), "建物の根元が道路の地面にない");
            }
            if (stage == "Stage_CityNight")
            {
                // 夜の明かりは写真の窓に合わせた絵（窓と同期。ネオン風に光らせる）。作った窓の帯は重ねない。
                Assert.That(shinjuku.GetTexture("_NightTex"), Is.Not.Null, "夜の窓の明かりの絵がない");
                Assert.That(shinjuku.GetFloat("_NightStrength"), Is.GreaterThan(1f), "夜の明かりが光らない");
                Assert.That(shinjuku.GetFloat("_BandFill"), Is.EqualTo(0f), "写真の窓と合わない窓の帯を重ねている");
                // 夜の道路は光らせない（大通りとのつなぎ目が目立った。2026-11-18 の指摘）。街明かりのもやは奥と手前の 2 枚。
                Assert.That(tokyo.Find("Streets").GetComponent<Renderer>().sharedMaterial.GetFloat("_NightStrength"), Is.EqualTo(0f), "夜の道路が光る");
                Transform glow = scene.GetRootGameObjects().Single(g => g.name == "Backdrop").transform.Find("CityGlow");
                Assert.That(glow, Is.Not.Null, "夜の街明かりのもやがない");
                Assert.That(glow.childCount, Is.EqualTo(2));
            }
            Assert.That(shinjuku.GetTexture("_MainTex"), Is.Not.Null, "西新宿に外壁の写真がない");
            Assert.That(shinjuku.GetTexture("_MainTex").width, Is.EqualTo(4096), "外壁の写真が縮んで読み込まれている");
            Assert.That(shinjuku.GetFloat("_TexBlend"), Is.EqualTo(1f));
            Assert.That(shinjuku.GetFloat("_LightShade"), Is.GreaterThan(0f), "面に光の陰影がない");
            Assert.That(tokyo.Find("Shinjuku").GetComponent<MeshFilter>().sharedMesh.uv.Length, Is.GreaterThan(0), "形に UV がない");
        }

        [TestCase("Stage_CityDay")]
        [TestCase("Stage_CityNight")]
        public void CityBackdrops_KeepTheVistaToTokyoOpen(string stage)
        {
            // 2026-11-17 の回答（ビル街の昼夜は背景が前と変わって見えない）: 谷の奥のビル群が西新宿を隠していた。真ん中の奥のビル群を
            // 置かず・低くして、大通りの先の高層ビル群の上半分は見えるようにする（いつもの目の高さから見て）。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            Renderer shinjuku = scene.GetRootGameObjects().Single(g => g.name == "Backdrop").transform.Find("Tokyo/Shinjuku").GetComponent<Renderer>();
            var eye = new Vector3(0f, 6f, -30f);
            float towerTop = Elevation(eye, shinjuku.bounds.max.y, shinjuku.bounds.center.z);
            float towerHalf = Elevation(eye, (shinjuku.bounds.min.y + shinjuku.bounds.max.y) * 0.5f, shinjuku.bounds.center.z);
            Assert.That(towerTop, Is.GreaterThan(5f).And.LessThan(20f), "高層ビルが低すぎる・画面の上からはみ出す");
            Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
            int checkedCount = 0;
            foreach (Renderer renderer in decor.GetComponentsInChildren<Renderer>(true))
            {
                // 箱のビルと、Kenney のビルのモデル（2026-11-18。Prop_building-*）。
                bool building = renderer.name == "Skyline" || renderer.name == "Block"
                    || renderer.GetComponentsInParent<Transform>(true).Any(t => t.name.StartsWith("Prop_building"));
                if (!building || Mathf.Abs(renderer.bounds.center.x) > 40f)
                {
                    continue;
                }
                checkedCount++;
                Assert.That(Elevation(eye, renderer.bounds.max.y, renderer.bounds.min.z), Is.LessThan(Mathf.Max(towerHalf, 0f)),
                    $"{renderer.name} ({renderer.bounds.center}): 真ん中の奥のビルが西新宿を隠す");
            }
            Assert.That(checkedCount, Is.GreaterThan(3), "大通りの見通しの手前の低い街並みがない");
        }

        // 目から見上げる角度（度）。
        private static float Elevation(Vector3 eye, float y, float z)
        {
            return Mathf.Atan2(y - eye.y, z - eye.z) * Mathf.Rad2Deg;
        }

        [Test]
        public void Kitchen_Windows_HaveFramesAndALock_AndLookOutOnTheCity()
        {
            // 2026-11-18 の要望: キッチンの奥の水色の所（窓）に窓枠と鍵を付けて、外にビル街の街並みが見えるように。
            Scene scene = Open("Assets/_Project/Scenes/Stages/Stage_Kitchen.unity");
            Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
            Renderer[] parts = decor.GetComponentsInChildren<Renderer>(true);
            Assert.That(parts.Any(r => r.name == "WindowWest" || r.name == "WindowEast"), Is.False, "前の水色の板が残っている");
            Renderer city = scene.GetRootGameObjects().Single(g => g.name == "WindowCity").transform.Find("Shinjuku").GetComponent<Renderer>();
            Assert.That(city.sharedMaterial.GetTexture("_MainTex"), Is.Not.Null, "窓の外の街に外壁の写真がない");
            Bounds wholeCity = scene.GetRootGameObjects().Single(g => g.name == "WindowCity").GetComponentsInChildren<Renderer>()
                .Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            Assert.That(wholeCity.min.z, Is.GreaterThan(17f), "窓の外の街が壁より手前に出ている");
            foreach (string side in new[] { "West", "East" })
            {
                foreach (string part in new[] { "FrameTop", "FrameBottom", "FrameLeft", "FrameRight", "SashOuterGlass", "SashInnerGlass", "LockBase",
                    "LockCrescent", "LockKnob", "LockKeeper", "Sill" })
                {
                    Assert.That(parts.Any(r => r.name == $"Window{side}{part}"), Is.True, $"窓（{side}）に {part} がない");
                }
                Renderer glass = parts.First(r => r.name == $"Window{side}SashInnerGlass");
                Vector3 center = glass.bounds.center;
                // 壁は窓の所でくりぬかれている（窓の真ん中を壁の箱が覆わない）。
                Assert.That(parts.Where(r => r.name.StartsWith("BackWall")).Any(r => r.bounds.Contains(new Vector3(center.x, center.y, 16f))), Is.False,
                    $"窓（{side}）の所が壁でふさがっている");
                // いつもの高さのカメラから窓の真ん中を通る視線が、外の街（西新宿）に当たる。高いカメラからは足もとの道路が見える。
                foreach (float eyeY in new[] { 12f, 20f, 26f })
                {
                    var eye = new Vector3(0f, eyeY, -30f);
                    var ray = new Ray(eye, (center - eye).normalized);
                    Assert.That(city.bounds.IntersectRay(ray), Is.True, $"高さ {eyeY} のカメラから窓（{side}）の外に街が見えない");
                }
            }
            Assert.That(parts.Where(r => r.name.StartsWith("BackWall")).Select(r => r.bounds.max.x).Max(), Is.GreaterThan(150f),
                "奥の壁が狭い（全体を映すカメラで壁の外の街が見える）");
        }

        [Test]
        public void Lab_HasTheSciFiInterior_WithEveryMaterialMapped()
        {
            // 2026-11-18 の要望（背景を無料のアセットで精巧に）: 研究所の奥は Quaternius の SF の壁の板・柱・小物。材質は OBJ の材質の
            // 名前ごとに選ぶ（名前が分からず代わりの材質になった面がない）。
            Scene scene = Open("Assets/_Project/Scenes/Stages/Stage_Lab.unity");
            Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
            Transform[] props = decor.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Prop_")).ToArray();
            Assert.That(props.Count(t => t.name.StartsWith("Prop_Wall") || t.name.StartsWith("Prop_Top") || t.name.StartsWith("Prop_ShortWall")),
                Is.GreaterThanOrEqualTo(96), "奥の壁の板が少ない");
            Assert.That(props.Count(t => t.name == "Prop_Column_Large_Straight"), Is.GreaterThan(20), "柱が少ない");
            // 2026-11-19 の要望（区画ごとにちぐはぐ）: 同じ高さの段の板は同じ種類にそろえる（真ん中から 1 つ外側の丸い窓の板だけは別）。
            bool IsPanel(Transform t) => t.name.StartsWith("Prop_Wall") || t.name.StartsWith("Prop_Top") || t.name.StartsWith("Prop_ShortWall");
            foreach (var row in props.Where(t => IsPanel(t) && t.name != "Prop_WallAstra_Straight_Flat_Window_Plain")
                         .GroupBy(t => Mathf.RoundToInt(t.GetComponentInChildren<Renderer>().bounds.min.y)))
            {
                Assert.That(row.Select(t => t.name).Distinct().Count(), Is.EqualTo(1), $"高さ {row.Key} の段の板がそろっていない: " + string.Join(", ", row.Select(t => t.name).Distinct()));
            }
            // 2026-11-20 の要望（3 回目）: 上から 青い天井・黒・なし・窓・なし・黒・暗い板とトラス・黒。
            // 窓のない区画（x 30。真ん中の柱の隣の区画は窓の段の板が窓と半分の幅の板になる）で調べる。
            string[] fromTop = props.Where(t => IsPanel(t) && Mathf.Abs(t.GetComponentInChildren<Renderer>().bounds.center.x - 30f) < 1f)
                .OrderByDescending(t => t.GetComponentInChildren<Renderer>().bounds.min.y).Select(t => t.name.Substring("Prop_".Length)).ToArray();
            Assert.That(fromTop, Is.EqualTo(new[]
            {
                "TopPlastic_Straight_Plain", "WallAstra_Straight_Flat", "WallAstra_Straight_Flat_Plain", "WallWindow_Straight",
                "WallAstra_Straight_Flat_Plain", "WallAstra_Straight_Flat", "ShortWall_Triangles_Straight", "ShortWall_DarkMetal2_Straight",
                "WallAstra_Straight_Flat",
            }), "段の並び（上から）");
            Assert.That(props.Where(t => t.name == "Prop_WallAstra_Straight_Flat_Window_Plain").Select(t => Mathf.RoundToInt(t.GetComponentInChildren<Renderer>().bounds.center.x))
                .OrderBy(x => x).ToArray(), Is.EqualTo(new[] { -12, 12 }), "丸い窓は真ん中の柱と隣の柱の間のちょうど真ん中（2 枚の板の境目をまたぐ）");
            // 真ん中の柱の両側の 2 区画（x −24〜24）は、すべての段で板を半分ずらして並べる（窓の段と上下の段の模様の境目をそろえる。
            // 2026-11-20 の要望）: どの段も板の真ん中が −21・−12・0・12・21、幅が 6・12・12・12・6（隙間も重なりもない）。
            Bounds Whole(Transform t)
            {
                Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                return bounds;
            }
            foreach (var row in props.Where(IsPanel).Select(Whole).Where(b => Mathf.Abs(b.center.x) < 24f).GroupBy(b => Mathf.RoundToInt(b.min.y)))
            {
                Bounds[] panels = row.OrderBy(b => b.center.x).ToArray();
                Assert.That(panels.Select(b => Mathf.RoundToInt(b.center.x)).ToArray(), Is.EqualTo(new[] { -21, -12, 0, 12, 21 }),
                    $"高さ {row.Key} の段の真ん中の範囲の板の並び");
                Assert.That(panels.Select(b => Mathf.RoundToInt(b.size.x)).ToArray(), Is.EqualTo(new[] { 6, 12, 12, 12, 6 }),
                    $"高さ {row.Key} の段の真ん中の範囲の板の幅");
            }
            Assert.That(props.Any(t => t.name == "Prop_WallAstra_Straight"), Is.False, "マークの板が残っている");
            // 黒いライン（帯）を塗った板には、帯の材質が残っていない。
            foreach (Transform plainPanel in props.Where(t => t.name.EndsWith("_Plain")))
            {
                string[] names = plainPanel.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Select(m => m.name).ToArray();
                Assert.That(names.Any(n => n.EndsWith("MI_Trim_03_Dark") || n.EndsWith("MI_Trim_02")), Is.False, $"{plainPanel.name} に帯の材質が残っている");
            }
            Assert.That(props.Count(t => t.name == "Prop_Prop_Computer"), Is.EqualTo(2), "操作盤は左右 1 台ずつ");
            // 柱は区画の境目 2 つに 1 つ（24m ごと。端と真ん中に立つ）。
            foreach (Transform column in props.Where(t => t.name == "Prop_Column_Large_Straight"))
            {
                float x = column.GetComponentInChildren<Renderer>().bounds.center.x;
                Assert.That(Mathf.Abs(x / 24f - Mathf.Round(x / 24f)), Is.LessThan(0.03f), $"柱が 24m ごとの境目にない（x {x}）");
            }
            foreach (Transform prop in props)
            {
                foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, $"{prop.name} に材質がない");
                        Assert.That(material.shader.name, Is.EqualTo("Airerage/PropLit"), $"{prop.name} の材質 {material.name}（代わりの材質になった）");
                    }
                    Assert.That(renderer.bounds.min.z, Is.GreaterThan(2f), $"{prop.name} が遊ぶ面に出ている");
                }
                Assert.That(prop.GetComponentsInChildren<Collider>(true), Is.Empty, $"{prop.name} に当たりがある");
            }
        }

        [Test]
        public void Stadium_HasAModeledGrandstand()
        {
            // 2026-11-18 の要望（背景を精巧に）: 奥のスタンドは段々の座席・通路・観客・屋根を 1 つの網にまとめたもの、両端に照明塔。
            Scene scene = Open("Assets/_Project/Scenes/Stages/Stage_Stadium.unity");
            Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
            Transform stand = decor.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Grandstand");
            Mesh mesh = stand.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.GreaterThan(30000), "観客と座席が少ない");
            Assert.That(AssetDatabase.GetAssetPath(mesh), Does.StartWith("Assets/_Project/Meshes/Stages/"), "網がアセットとして保存されていない");
            Assert.That(stand.GetComponent<Renderer>().sharedMaterials.Length, Is.EqualTo(mesh.subMeshCount));
            Assert.That(stand.GetComponent<Renderer>().bounds.min.z, Is.GreaterThan(25f), "遊ぶ面に近すぎる");
            Assert.That(decor.GetComponentsInChildren<Transform>(true).Count(t => t.name == "LightTowerLamp"), Is.EqualTo(30), "照明塔の灯り");
            Assert.That(decor.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Fan" || t.name == "BackStand"), Is.False, "前の箱のスタンドが残っている");
        }

        [TestCase("Stage_Stadium", "Prop_flagRed", "Prop_tentLong", "Prop_barrierRed")]
        [TestCase("Stage_Wharf", "Prop_ship-cargo-a", "Prop_water-tower", "Prop_building-")]
        [TestCase("Stage_SnowSea", "Prop_tree-snow-a", "Prop_rocks-large", "Prop_boat-sail-a")]
        [TestCase("Stage_Volcano", "Prop_DeadTree_1", "Prop_DeadTree_2", "Prop_rock_")]
        [TestCase("Stage_Kitchen", "Prop_kitchenFridgeLarge", "Prop_kitchenMicrowave", "Prop_pottedPlant")]
        [TestCase("Stage_CityNight", "Prop_CommonTree_1", "Prop_traffic-light", "Prop_building-")]
        public void NewAssetPacks_ArePlacedBehindThePlayPlane(string stage, string a, string b, string c)
        {
            // 2026-11-19 にユーザーが追加したアセット（Kenney の Racing・Industrial・Watercraft・Holiday・Nature・Furniture・Roads、
            // Quaternius の Nature）。材質はすべて背景の小物のシェーダー、当たりはなく、遊ぶ面より奥。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
            Transform[] props = decor.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Prop_")).ToArray();
            foreach (string name in new[] { a, b })
            {
                Assert.That(props.Any(t => t.name == name), Is.True, $"{stage} に {name} がない");
            }
            Assert.That(props.Any(t => t.name.StartsWith(c)), Is.True, $"{stage} に {c} がない");
            foreach (Transform prop in props)
            {
                foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(renderer.sharedMaterials.All(m => m != null && m.shader.name == "Airerage/PropLit"), Is.True, $"{stage} の {prop.name} の材質");
                    Assert.That(renderer.bounds.min.z, Is.GreaterThan(0f), $"{stage} の {prop.name} が遊ぶ面より手前に出ている");
                }
                Assert.That(prop.GetComponentsInChildren<Collider>(true), Is.Empty, $"{stage} の {prop.name} に当たりがある");
            }
        }

        [TestCase("Stage_Volcano", "CraterRock")]
        [TestCase("Stage_SnowSea", "SnowMountain")]
        [TestCase("Stage_Stadium", "GrassGround")]
        [TestCase("Stage_Wharf", "ConcreteGround")]
        [TestCase("Stage_Manor", "DirtGround")]
        public void GroundMaterials_UseThePhotoTextures(string stage, string material)
        {
            // 2026-11-19 の要望（地面の写真の絵。Poly Haven の CC0）: 地面と岩山の材質に、写真の絵と法線の絵が当たっている。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            Material found = InScene<Renderer>(scene).SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.name == material);
            Assert.That(found, Is.Not.Null, $"{stage} に {material} がない");
            Assert.That(found.shader.name, Is.EqualTo("Airerage/TerrainRock"));
            Assert.That(found.GetTexture("_RockTex"), Is.Not.Null, "写真の絵がない");
            Assert.That(found.GetTexture("_RockNormal"), Is.Not.Null, "法線の絵がない");
            Assert.That(AssetDatabase.GetAssetPath(found.GetTexture("_RockTex")), Does.StartWith("Assets/_Project/ThirdParty/PolyHaven/"));
            Assert.That(found.GetFloat("_TexStrength"), Is.GreaterThan(0f));
        }

        [TestCase("Stage_Volcano", "CraterRidge")]
        [TestCase("Stage_Volcano", "FarVolcanoes")]
        [TestCase("Stage_SnowSea", "FarMountains")]
        [TestCase("Stage_Wharf", "FarMountains")]
        public void BackgroundTerrain_IsSculpted(string stage, string name)
        {
            // 2026-11-18 の要望（地面のモデリングもしっかり）: 背景の山は箱ではなく、起伏のある高さの網（保存したメッシュ）を、斜面と高さで
            // 岩・雪・灰・溶岩の光に塗る材質で描く。遊ぶ面より奥。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
            Transform terrain = decor.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
            Mesh mesh = terrain.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.GreaterThan(2000), "網が粗い");
            Assert.That(AssetDatabase.GetAssetPath(mesh), Does.StartWith("Assets/_Project/Meshes/Stages/"), "網がアセットとして保存されていない");
            Renderer renderer = terrain.GetComponent<Renderer>();
            Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Airerage/TerrainRock"));
            Assert.That(renderer.bounds.min.z, Is.GreaterThan(10f), "遊ぶ面に近すぎる");
            Assert.That(renderer.bounds.size.y, Is.GreaterThan(30f), "起伏が小さい");
        }

        [Test]
        public void Stages_UseTheReceivedAssetProps()
        {
            // 2026-11-18 のユーザーの指示: push したアセット（KayKit・Kenney。CC0）を背景に組み込む。小物は色の絵の材質で、当たりはない。
            foreach (string stage in new[] { "Stage_Kitchen", "Stage_Manor", "Stage_SpaceStation", "Stage_CityDay", "Stage_CityNight" })
            {
                Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
                Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
                Transform[] props = decor.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Prop_")).ToArray();
                Assert.That(props.Length, Is.GreaterThan(8), $"{stage} に背景の小物が少ない");
                foreach (Transform prop in props)
                {
                    Renderer renderer = prop.GetComponentInChildren<Renderer>();
                    Assert.That(renderer, Is.Not.Null, $"{stage} の {prop.name} に見た目がない");
                    // 色の絵のパックは絵がある（.mtl の色から作る材質〈Mtl*。Furniture など〉は絵がなくてよい）。
                    if (!renderer.sharedMaterial.name.StartsWith("Mtl"))
                    {
                        Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null, $"{stage} の {prop.name} に色の絵がない");
                    }
                    // キーワードに頼らない背景の小物のシェーダー（バッチモードで作った URP の Lit では発光・法線の絵が効かなかった）。
                    Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Airerage/PropLit"), $"{stage} の {prop.name} の材質");
                    Assert.That(prop.GetComponentsInChildren<Collider>(true), Is.Empty, $"{stage} の {prop.name} に当たりがある");
                    // 遊ぶ面（z 0 のまわり）に出ない。
                    Assert.That(renderer.bounds.min.z, Is.GreaterThan(0f), $"{stage} の {prop.name} が遊ぶ面より手前に出ている");
                }
                if (stage == "Stage_CityNight")
                {
                    // 夜のビルは窓の色の所だけが光る（発光の絵）。
                    Assert.That(props.Any(x =>
                    {
                        Material m = x.GetComponentInChildren<Renderer>().sharedMaterial;
                        return m.GetTexture("_EmissionMap") != null && m.GetColor("_EmissionColor").maxColorComponent > 1f;
                    }), Is.True, "夜のビルの窓が光らない");
                }
            }
        }

        [Test]
        public void Manor_HasDriftingFogLayers_MoonRays_AndWisps()
        {
            // 2026-11-18 の要望: 恐怖の館の手前の霧をリアルにし、怪しい空気感と雰囲気を出す。
            Scene scene = Open("Assets/_Project/Scenes/Stages/Stage_Manor.unity");
            Transform atmosphere = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform.Find("Atmosphere");
            Assert.That(atmosphere, Is.Not.Null, "霧の層がない");
            Renderer[] fog = atmosphere.GetComponentsInChildren<Renderer>().Where(r => r.sharedMaterial.shader.name == "Airerage/FogSheet").ToArray();
            Assert.That(fog.Length, Is.GreaterThanOrEqualTo(6), "霧の板が少ない");
            Assert.That(fog.Any(r => r.bounds.center.z < 0f), Is.True, "手前の地霧がない");
            Assert.That(fog.Any(r => r.bounds.center.z > 40f), Is.True, "奥の森の霧がない");
            Assert.That(fog.Where(r => r.bounds.center.z < 0f).All(r => r.sharedMaterial.GetFloat("_Density") < 0.5f), Is.True,
                "手前の霧が濃すぎる（遊ぶ所を隠す）");
            Assert.That(atmosphere.GetComponentsInChildren<ParticleSystem>().Length, Is.GreaterThanOrEqualTo(2), "鬼火がない");
            Assert.That(atmosphere.GetComponentsInChildren<Renderer>().Count(r => r.name == "MoonRay"), Is.EqualTo(3), "月の光の筋がない");
            Assert.That(InScene<Renderer>(scene).Any(r => r.name == "Fog" || r.name == "ForestTree"), Is.False, "前の平たい霧の板か箱の森が残っている");
        }

        [TestCaseSource(nameof(DressedStages))]
        public void OutdoorStages_AreSkyAllAround_AndTheFogMatchesTheHorizon(string stage)
        {
            // 2026-11-18 の要望: 屋内のステージ（キッチン・研究所）以外は最後面を全部空にし、空と地面の境目に地平線の処理（霧と遠景の
            // もやの色を空の地平線の色にそろえ、遠くの地面・海・建物の根元を空に溶かす）。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            SceneManager.SetActiveScene(scene);
            Assert.That(InScene<Renderer>(scene).Any(r => r.name == "FarGround"), Is.False, "奥の地面の板が残っている");
            bool indoor = stage == "Stage_Kitchen" || stage == "Stage_Lab";
            if (indoor || !RenderSettings.fog)
            {
                return;
            }
            Material sky = RenderSettings.skybox;
            Color horizon = sky.GetColor("_HorizonColor");
            Color fog = RenderSettings.fogColor;
            Assert.That(Mathf.Abs(fog.r - horizon.r) + Mathf.Abs(fog.g - horizon.g) + Mathf.Abs(fog.b - horizon.b), Is.LessThan(0.02f),
                $"霧の色 {fog} が空の地平線の色 {horizon} と違う（遠くの地面と空の境目に線が出る）");
            // 地平線より下も空（地面の色でなく、もやから空の色へ）。
            Color below = sky.GetColor("_BelowColor");
            Color skyColor = sky.GetColor("_SkyColor");
            Color expected = Color.Lerp(horizon, skyColor, 0.3f);
            Assert.That(Mathf.Abs(below.r - expected.r) + Mathf.Abs(below.g - expected.g) + Mathf.Abs(below.b - expected.b), Is.LessThan(0.02f),
                "地平線より下が空の色でない");
        }

        [Test]
        public void Manor_HasTheManorHouse_TheTomatoFarm_AndTatteredScarecrows()
        {
            // 2026-11-18 の要望: 中央を精巧な館に（こげ茶・紫のアクセント・紫の光の窓）、右の黒い球と赤い点を消してトマト農園と
            // ボロボロのカカシ、左の門を地面の上に。
            Scene scene = Open("Assets/_Project/Scenes/Stages/Stage_Manor.unity");
            Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
            Transform house = decor.Find("ManorHouse");
            Assert.That(house, Is.Not.Null, "館がない");
            Renderer[] parts = house.GetComponentsInChildren<Renderer>();
            Assert.That(parts.Length, Is.GreaterThan(80), "館が作り込まれていない");
            Renderer[] glass = parts.Where(r => r.name.Contains("Glass") || r.name.Contains("Rose") && !r.name.Contains("Frame") && !r.name.Contains("Spoke")).ToArray();
            Assert.That(glass.Length, Is.GreaterThanOrEqualTo(10), "光る窓が少ない");
            Color pane = glass[0].sharedMaterial.GetColor("_BaseColor");
            Assert.That(pane.b > pane.g * 2f && pane.r > pane.g * 2f && pane.maxColorComponent > 1f, Is.True, $"窓の光が紫に光っていない（{pane}）");
            Color wall = parts.First(r => r.name == "ManorHall").sharedMaterial.GetColor("_BaseColor");
            Assert.That(wall.r > wall.b && wall.r < 0.4f, Is.True, $"館の壁がこげ茶でない（{wall}）");
            Assert.That(parts.Count(r => r.sharedMaterial.name.StartsWith("ManorAccent")), Is.GreaterThan(5), "紫のアクセントが少ない");
            // 足場（z ±2）より奥。例外はバルコニーの足場の手前の縁のバラスターの手すり（2026-11-19。低く細い）。
            Renderer[] protruding = parts.Where(r => r.bounds.min.z < 1.9f && !r.name.StartsWith("ManorBalcony")).ToArray();
            Assert.That(protruding, Is.Empty, "館が遊ぶ面に出ている: " + string.Join(", ", protruding.Select(r => r.name)));
            // 洋館のパーツ（2026-11-19 の要望）: 尖塔・塔屋・マンサード屋根・ドーマー・ペディメント・ベイウィンドウ・ポーチの柱・
            // バルコニー・バラスター。
            foreach (string part in new[] { "ManorSpireShape", "ManorTower", "ManorMansard", "ManorDormer", "ManorPediment", "ManorBayFront",
                "ManorPorchColumn", "ManorBalconyDeck", "ManorBalconyBaluster" })
            {
                Assert.That(parts.Any(r => r.name == part), Is.True, $"{part} がない");
            }
            Assert.That(parts.Count(r => r.name.EndsWith("Baluster")), Is.GreaterThan(40), "バラスターが少ない");
            // 館の階の高さが足場に合う: バルコニーの足場（y 10.6）= ポーチの屋根、左右の屋根の足場（y 20.8）= マンサード屋根の屋上。
            Renderer deck = parts.Single(r => r.name == "ManorBalconyDeck");
            Assert.That(deck.bounds.max.y, Is.EqualTo(10.6f).Within(0.05f), "ポーチの屋根がバルコニーの足場の高さでない");
            Renderer roofDeck = parts.Single(r => r.name == "ManorRoofDeck");
            Assert.That(roofDeck.bounds.max.y, Is.EqualTo(20.8f).Within(0.05f), "屋上が屋根の足場の高さでない");
            Assert.That(InScene<Renderer>(scene).Any(r => r.name == "ManorFacade" || r.name == "TomatoPlant" || r.name == "TomatoFruit"), Is.False,
                "前の館の箱かトマトの球が残っている");
            Assert.That(decor.GetComponentsInChildren<Transform>().Count(t => t.name == "Prop_food_ingredient_tomato"), Is.GreaterThan(30), "トマトの実が少ない");
            Transform[] scarecrows = decor.GetComponentsInChildren<Transform>().Where(t => t.name == "Scarecrow").ToArray();
            Assert.That(scarecrows.Length, Is.EqualTo(2), "カカシが 2 体でない");
            Assert.That(scarecrows.All(t => t.position.z > 4f && t.GetComponentsInChildren<Renderer>().Count(r => r.name == "ScarecrowRag") >= 7), Is.True,
                "カカシが奥にいない・ぼろぼろでない");
            // 門は墓場の地面（x -48〜-24）の上。
            Renderer gate = decor.GetComponentsInChildren<Transform>().Single(t => t.name == "Prop_arch_gate").GetComponentInChildren<Renderer>();
            Assert.That(gate.bounds.max.x, Is.LessThan(-24f), "門が墓場の地面の外へはみ出して宙に浮く");
        }

        [Test]
        public void Stadium_HasNoSimpleClouds()
        {
            // 2026-11-18 の要望: 陸上競技場の簡易な雲を消す（雲は空に描く）。
            Scene scene = Open("Assets/_Project/Scenes/Stages/Stage_Stadium.unity");
            Assert.That(InScene<Renderer>(scene).Any(r => r.name == "Cloud"), Is.False);
        }

        [Test]
        public void CityNight_WetRoads_ReflectTheNeon()
        {
            // 2026-11-18 の要望: 夜のビル街の地面にもネオンの照り返しを描いて、奥の街の光と地続きに。
            Scene scene = Open("Assets/_Project/Scenes/Stages/Stage_CityNight.unity");
            Renderer avenue = InScene<Renderer>(scene).Single(r => r.name == "Avenue");
            Renderer streets = InScene<Renderer>(scene).Single(r => r.name == "Streets");
            foreach (Renderer road in new[] { avenue, streets })
            {
                Assert.That(road.sharedMaterial.shader.name, Is.EqualTo("Airerage/StageBackdrop"), $"{road.name} が照り返しのシェーダーでない");
                Assert.That(road.sharedMaterial.GetFloat("_WetReflect"), Is.GreaterThan(0.5f), $"{road.name} にネオンの照り返しがない");
            }
            Assert.That(avenue.sharedMaterial.GetFloat("_WetReflect"), Is.EqualTo(streets.sharedMaterial.GetFloat("_WetReflect")).Within(0.01f),
                "大通りと西新宿の道路の照り返しの強さが違う（つなぎ目が出る）");
        }

        [TestCase("Stage_SnowSea")]
        [TestCase("Stage_Wharf")]
        [TestCase("Stage_Kitchen")]
        public void Water_IsDrawnWithTheWaterShader(string stage)
        {
            // 2026-11-18 の要望: 水の表現をリアルに（揺れる水面と泡・深さの色・光の網と筋・空を映す水面・しぶき）。
            Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
            SceneManager.SetActiveScene(scene);
            StageZoneView[] water = InScene<StageZoneView>(scene).Where(v => v.GetComponent<StageZoneAuthoring>().Kind == StageZoneKind.Water).ToArray();
            Assert.That(water.Length, Is.GreaterThan(0));
            foreach (StageZoneView view in water)
            {
                Assert.That(view.ShaderWater, Is.True, $"{view.name} がしぶきを出さない");
                Renderer front = view.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "Front");
                Assert.That(front.sharedMaterial.shader.name, Is.EqualTo("Airerage/Water"));
                Assert.That(front.sharedMaterial.GetFloat("_Mode"), Is.EqualTo(0f), "手前の面が水の断面でない");
                float top = view.transform.position.y + view.GetComponent<StageZoneAuthoring>().Size.y * 0.5f;
                Assert.That(front.sharedMaterial.GetFloat("_SurfaceY"), Is.EqualTo(top).Within(0.01f), "水の断面の水面の高さがずれている");
            }
            if (stage != "Stage_Kitchen")
            {
                Renderer sea = InScene<Renderer>(scene).Single(r => r.name == "Sea");
                Assert.That(sea.sharedMaterial.shader.name, Is.EqualTo("Airerage/Water"), "海の箱が水のシェーダーでない");
                Assert.That(sea.sharedMaterial.renderQueue, Is.EqualTo(2000), "海の箱は不透明で描く");
                // 海は霧が地平線の色になりきる所まで続く（水平線で空に溶ける）。
                Assert.That(sea.bounds.max.z + 30f, Is.GreaterThan(RenderSettings.fogEndDistance), "海が霧に溶ける前に途切れる");
            }
        }

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
            // 光の格子（認識阻害の結界）は全ステージに（2026-11-18 の要望）。下の道路より上、撃破の線より下。
            GameObject grid = scene.GetRootGameObjects().Single(g => g.name == "LightGrid");
            Assert.That(grid.transform.position.y, Is.GreaterThan(-24f).And.LessThan(-22f));
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
        public void NaturalGimmicks_HaveTheirLooks_AndFitTheirStages()
        {
            // 2026-11-20 の方針（ギミックを実際の現象に寄せ、判定を見た目に合わせる）の第 2 弾と、同じ日の指摘。
            Scene lab = Open("Assets/_Project/Scenes/Stages/Stage_Lab.unity");
            Transform labDecor = lab.GetRootGameObjects().Single(g => g.name == "Decor").transform;
            Transform window = labDecor.GetComponentsInChildren<Transform>(true).First(t => t.name == "Prop_WallAstra_Straight_Flat_Window_Plain");
            float windowFront = window.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.z);
            float windowBottom = window.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y);
            float flatFront = labDecor.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Prop_WallAstra_Straight_Flat_Plain")
                .Select(t => t.GetComponentsInChildren<Renderer>()).Where(rs => Mathf.Abs(rs.Min(r => r.bounds.min.y) - windowBottom) < 0.5f)
                .Select(rs => rs.Min(r => r.bounds.min.z)).First();
            Assert.That(windowFront, Is.EqualTo(flatFront).Within(0.02f), "丸い窓の板の表が、同じ段の板の表とそろっていない（手前へ浮く）");

            Scene manor = Open("Assets/_Project/Scenes/Stages/Stage_Manor.unity");
            Bounds cave = InScene<Renderer>(manor).Single(r => r.name == "CaveWall").bounds;
            Assert.That(cave.min.x, Is.GreaterThanOrEqualTo(-48.05f), "洞窟の岩壁が左へはみ出している");
            Assert.That(cave.max.x, Is.LessThanOrEqualTo(-23.95f), "洞窟の岩壁が右へはみ出している");
            Assert.That(InScene<LightningStrikeView>(manor).Count(), Is.EqualTo(2), "落雷の見た目");
            Assert.That(InScene<Transform>(manor).Any(t => t.name == "Mist"), Is.False, "悪魔の手の紫の四角い板が残っている");
            Assert.That(InScene<ParticleSystem>(manor).Count(p => p.name == "MiasmaSmoke"), Is.EqualTo(2), "悪魔の手の瘴気の煙");

            Scene snow = Open("Assets/_Project/Scenes/Stages/Stage_SnowSea.unity");
            IcicleFallView icicles = InScene<IcicleFallView>(snow).Single();
            Assert.That(new SerializedObject(icicles).FindProperty("_hanging").arraySize, Is.GreaterThanOrEqualTo(15), "天井のつらら");
            StageZone icicleZone = InScene<StageZoneAuthoring>(snow).Single(z => z.name == "Icicles").ToZone();
            Assert.That(icicleZone.FallLength, Is.EqualTo(1.5f), "つららの判定は落ちてくるつららの所だけ");

            Scene wharf = Open("Assets/_Project/Scenes/Stages/Stage_Wharf.unity");
            Assert.That(InScene<Light>(wharf).Any(l => l.name == "LampLight"), Is.False, "灯台の点の光は外した（まぶしさ）");

            Scene stadium = Open("Assets/_Project/Scenes/Stages/Stage_Stadium.unity");
            Assert.That(InScene<ParticleSystem>(stadium).Any(p => p.name == "DustCore"), Is.True, "つむじ風の塵");
            Scene kitchen = Open("Assets/_Project/Scenes/Stages/Stage_Kitchen.unity");
            Assert.That(InScene<HazardEffectRig>(kitchen).Any(r => r.name == "SteamFx"), Is.True, "やかんの湯気");
            Assert.That(InScene<ParticleSystem>(kitchen).Any(p => p.name == "DustCore"), Is.True, "ミキサーの粉の渦");

            // 第 3 弾: 吹雪（雪は地形にぶつかると消える）・プラズマ球・噴石・噴気孔。
            snow = Open("Assets/_Project/Scenes/Stages/Stage_SnowSea.unity");
            ParticleSystem blizzard = InScene<ParticleSystem>(snow).Single(p => p.name == "Snow");
            Assert.That(blizzard.collision.enabled, Is.True, "吹雪の雪が地形を通り抜ける（陰の判定と合わない）");
            Assert.That(InScene<Transform>(snow).Any(t => t.name == "Storm" && t.GetComponent<Renderer>() != null), Is.False, "吹雪の白い板が残っている");
            lab = Open("Assets/_Project/Scenes/Stages/Stage_Lab.unity");
            Assert.That(InScene<PlasmaGlobeView>(lab).Count(), Is.EqualTo(2), "プラズマ球");
            Scene volcano = Open("Assets/_Project/Scenes/Stages/Stage_Volcano.unity");
            Assert.That(InScene<Transform>(volcano).Count(t => t.name == "Fumarole"), Is.EqualTo(2), "噴気孔");

            // 2026-11-20 の要望（見た目と効果を合わせる。汎用のギミックは見た目に合わせて効果と配置も変える）。
            // プラズマ球: 制御甲板の両端の柱の上に載り、触れると当たる電気の危険で、真ん中がアンカーポイント。
            StageZone[] globes = InScene<StageZoneAuthoring>(lab).Where(z => z.name.StartsWith("Plasma")).Select(z => z.ToZone()).ToArray();
            Assert.That(globes.Length, Is.EqualTo(2));
            foreach (StageZone globe in globes)
            {
                Assert.That(globe.Kind, Is.EqualTo(StageZoneKind.Hazard), "プラズマ球が触れると当たる危険でない");
                Assert.That(globe.Electric, Is.True, "プラズマ球につないだワイヤーが電気を帯びない");
                Assert.That(globe.ActiveTicks, Is.EqualTo(globe.Period), "プラズマ球がいつも効いていない");
                Assert.That(Mathf.Abs(globe.Center.X), Is.EqualTo(16.3f).Within(0.01f), "柱の上にない");
                Assert.That(globe.MinY, Is.GreaterThanOrEqualTo(26.5f), "柱に埋まっている");
            }
            Transform globeView = InScene<PlasmaGlobeView>(lab).First().transform;
            Assert.That(globeView.gameObject.activeInHierarchy, Is.True, "プラズマ球がいつも見えていない");
            // 噴石: 浮かぶ跳ね返す物はなくし、奥の山から転がってくる危険 1 つ（15〜25 秒ごと）。
            StageZone[] volcanoZones = InScene<StageZoneAuthoring>(volcano).Select(z => z.ToZone()).ToArray();
            Assert.That(volcanoZones.Any(z => z.Kind == StageZoneKind.Bumper), Is.False, "浮かぶ噴石が残っている");
            StageZone rolling = InScene<StageZoneAuthoring>(volcano).Single(z => z.name == "RollingBomb").ToZone();
            Assert.That(rolling.Period, Is.EqualTo(15 * 60));
            Assert.That(rolling.Timing.JitterTicks, Is.EqualTo(10 * 60), "15〜25 秒のばらつき");
            RollingBombView bomb = InScene<RollingBombView>(volcano).Single();
            Assert.That(bomb.HeightAt(0f, 40f), Is.GreaterThan(-15f), "奥の火口の壁の高さが焼かれていない");
            Assert.That(InScene<Renderer>(volcano).Any(r => r.sharedMaterial != null && r.sharedMaterial.shader.name == "Airerage/MagmaRock"), Is.True,
                "噴石（ひび割れて奥が光る岩）");
            // 噴気孔: ジャンプ台の効果でなく、湯気の柱の中のとても強い風。
            foreach (StageZone vent in InScene<StageZoneAuthoring>(volcano).Where(z => z.name.StartsWith("Vent")).Select(z => z.ToZone()))
            {
                Assert.That(vent.Kind, Is.EqualTo(StageZoneKind.Gust), "噴気孔が風でない");
                Assert.That(vent.Limit, Is.GreaterThanOrEqualTo(28f), "噴気孔の風が弱い");
                Assert.That(vent.MaxY - vent.MinY, Is.GreaterThan(8f), "湯気の柱の高さまで吹かない");
            }
            // やかんの湯気: 注ぎ口の先から（地面から生えない）。
            kitchen = Open("Assets/_Project/Scenes/Stages/Stage_Kitchen.unity");
            StageZone steam = InScene<StageZoneAuthoring>(kitchen).Single(z => z.name == "KettleSteam").ToZone();
            Transform spout = InScene<Transform>(kitchen).Single(t => t.name == "Spout");
            float spoutTop = spout.GetComponent<Renderer>().bounds.max.y;
            Assert.That(steam.MinY, Is.EqualTo(spoutTop).Within(0.4f), "湯気の範囲が注ぎ口の先から始まらない");
            ParticleSystem kettleSteam = InScene<ParticleSystem>(kitchen).First(p => p.name == "Steam" && p.transform.parent.name == "SteamFx"
                && p.transform.parent.parent.name == "KettleSteam");
            Assert.That(kettleSteam.transform.position.y, Is.EqualTo(spoutTop).Within(0.4f), "湯気が注ぎ口から出ない");
        }

        [Test]
        public void FourthGimmickBatch_HasItsLooks_AndMatchesTheHitboxes()
        {
            // 2026-11-21 の方針（実際の現象に寄せる。汎用のギミックは見た目に合わせて効果と配置も変える）の第 4 弾。
            // 気送管: 丸いガラスの管・送風機・昇る筒（管の奥側を昇り、キャラクターを隠さない）。前の四角いガラスの箱はない。
            Scene lab = Open("Assets/_Project/Scenes/Stages/Stage_Lab.unity");
            PneumaticTubeView[] tubes = InScene<PneumaticTubeView>(lab).ToArray();
            Assert.That(tubes.Length, Is.EqualTo(2), "気送管");
            foreach (PneumaticTubeView tube in tubes)
            {
                Transform[] carriers = tube.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Carrier")).ToArray();
                Assert.That(carriers.Length, Is.EqualTo(3), "昇る筒");
                Assert.That(carriers.All(c => c.localPosition.z > 0.5f), Is.True, "筒が遊ぶ面の手前を昇る（キャラクターを隠す）");
                Assert.That(tube.transform.Find("Fan"), Is.Not.Null, "送風機の羽根");
            }
            Assert.That(InScene<Transform>(lab).Any(t => t.name == "TubeGlass"), Is.False, "前の四角いガラスの箱が残っている");
            // 赤外線: 上下 2 本の細い光線と、両端の投光器・受光器、ミサイルの煙の帯。
            foreach (StageTrapView trap in InScene<StageTrapView>(lab))
            {
                Assert.That(trap.transform.Find("Beam").GetComponent<LineRenderer>(), Is.Not.Null, "赤外線の光線が線でない");
                Assert.That(trap.transform.Find("Beam2"), Is.Not.Null, "赤外線の 2 本目の光線");
                Assert.That(trap.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Sensor"), Is.EqualTo(2), "投光器と受光器");
                Assert.That(trap.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.name == "MissileSmoke"), Is.True, "ミサイルの煙の帯");
                Assert.That(trap.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Fin"), Is.EqualTo(4), "ミサイルの尾翼");
            }

            // 投石機: 周期のある発射台（予告の間に引き絞る）で、かごは範囲の所にあり、腕は左上へ振り上がる。
            Scene manor = Open("Assets/_Project/Scenes/Stages/Stage_Manor.unity");
            StageZone catapult = InScene<StageZoneAuthoring>(manor).Single(z => z.name == "PumpkinLauncher").ToZone();
            Assert.That(catapult.Kind, Is.EqualTo(StageZoneKind.Launcher));
            Assert.That(catapult.Period, Is.GreaterThan(0), "投石機が周期のある発射台でない（いつも効く）");
            Assert.That(catapult.Timing.WarningTicks, Is.GreaterThan(0), "引き絞る予告がない");
            CatapultView catapultView = InScene<CatapultView>(manor).Single();
            Transform bucket = catapultView.GetComponentsInChildren<Transform>(true).First(t => t.name == "BucketBase");
            Assert.That(bucket.position.x, Is.InRange(catapult.MinX, catapult.MaxX), "かごが発射台の範囲にない");
            Assert.That(bucket.position.y, Is.LessThan(catapult.MaxY + 0.6f), "かごが地面から浮いている");
            Assert.That(CatapultView.AngleAt(CatapultView.SwingSeconds, -16f, 100f), Is.EqualTo(100f).Within(0.01f), "腕が横木まで振り上がらない");
            Assert.That(catapultView.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("Prop_pumpkin")), Is.True, "かごのかぼちゃ");
            // 鬼火の輪（2026-11-21 の要望で作り直した）: 加速リングはなくし、1 つの仕掛けが 6 地点を飛び回る（くぐった人に 30 枚）。
            Assert.That(InScene<StageZoneAuthoring>(manor).Any(z => z.Kind == StageZoneKind.BoostRing), Is.False, "前の加速リングの鬼火の輪が残っている");
            StageTrapAuthoring wispAuthoring = InScene<StageTrapAuthoring>(manor).Single(t => t.Kind == StageTrapKind.WispRing);
            StageTrap wisp = wispAuthoring.ToTrap();
            Assert.That(wisp.Waypoints.Count, Is.EqualTo(6), "鬼火の輪の地点が 6 つでない");
            Assert.That(wisp.Reward, Is.EqualTo(30), "くぐったときのコインが 30 枚でない");
            Assert.That(wisp.RespawnMinTicks, Is.EqualTo(20 * 60));
            Assert.That(wisp.RespawnMaxTicks, Is.EqualTo(30 * 60));
            WispRingView wispView = wispAuthoring.GetComponent<WispRingView>();
            Assert.That(wispView, Is.Not.Null, "鬼火の輪の見た目");
            Assert.That(wispView.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Wisp") && t.GetComponent<Renderer>() != null),
                Is.GreaterThanOrEqualTo(8), "輪に連なる鬼火");
            // 6 地点はどれも地形に埋まらない（輪の大きさの箱が、動かない地形と重ならない）。
            foreach (System.Numerics.Vector2 point in wisp.Waypoints)
            {
                foreach (BoxCollider box in InScene<BoxCollider>(manor).Where(b => !b.isTrigger))
                {
                    Bounds b = box.bounds;
                    bool overlaps = point.X + wisp.HalfSize.X > b.min.x && point.X - wisp.HalfSize.X < b.max.x
                        && point.Y + wisp.HalfSize.Y > b.min.y && point.Y - wisp.HalfSize.Y < b.max.y;
                    Assert.That(overlaps, Is.False, $"鬼火の輪の地点 {point} が {box.name} に埋まる");
                }
            }

            // 荷役の網（2026-11-21 の要望で作り直した）: クレーンの梁から吊った、ワイヤーで下がる動く足場。跳ね返す物の板は残っていない。
            Scene wharf = Open("Assets/_Project/Scenes/Stages/Stage_Wharf.unity");
            Assert.That(InScene<StageZoneAuthoring>(wharf).Any(z => z.Kind == StageZoneKind.Bumper), Is.False, "前の跳ね返す物の網が残っている");
            StagePlatformAuthoring[] nets = InScene<StagePlatformAuthoring>(wharf).Where(p => p.Kind == StagePlatformKind.Hoist).ToArray();
            Assert.That(nets.Length, Is.EqualTo(2), "荷役の網が 2 つない");
            foreach (StagePlatformAuthoring authoring in nets)
            {
                StagePlatform platform = authoring.ToPlatform();
                Assert.That(platform.HasAnchor, Is.True, "網の真ん中にワイヤーがつながらない");
                Assert.That(platform.IsSolid, Is.False, "網が当たる箱になっている");
                Assert.That(platform.BouncePower, Is.EqualTo(16f), "網が跳ね返さない");
                Assert.That(platform.OpenHeight, Is.EqualTo(4f), "網の下がる長さ");
                CargoNetView net = authoring.GetComponentInChildren<CargoNetView>(true);
                Assert.That(net, Is.Not.Null, "荷役の網の見た目");
                Transform netPlane = net.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Net");
                Assert.That(netPlane.position.y, Is.InRange(platform.Origin.Y, platform.Origin.Y + platform.HalfSize.Y), "網の面が跳ね返す箱の上の半分にない");
                Transform trolley = authoring.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Trolley");
                Assert.That(trolley.IsChildOf(net.transform), Is.False, "トロリーが網と一緒に動く（梁に残るはず）");
                Assert.That(trolley.position.y, Is.EqualTo(24.5f - 0.25f).Within(0.01f), "トロリーがクレーンの梁の下にない");
            }

            // ピッチャーマシン: 2 つのタイヤ・送りのレール・ボールの筒。照明塔の柱に埋まらない。
            Scene stadium = Open("Assets/_Project/Scenes/Stages/Stage_Stadium.unity");
            foreach (StageTrapView pitcher in InScene<StageTrapView>(stadium))
            {
                Assert.That(pitcher.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Wheel")), Is.EqualTo(2), "2 つのタイヤ");
                Assert.That(pitcher.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Magazine"), Is.True, "ボールの筒");
                float poleFace = Mathf.Sign(pitcher.transform.position.x) * 32.4f;
                foreach (Renderer part in pitcher.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Tire" || r.name == "Tube"))
                {
                    float inner = poleFace < 0f ? part.bounds.min.x : -part.bounds.max.x;
                    Assert.That(inner, Is.GreaterThan(-Mathf.Abs(poleFace) - 0.2f), $"{pitcher.name} の {part.name} が照明塔の柱に埋まる");
                }
            }
        }

        [Test]
        public void Kitchen_DonutFryer_SitsOnTheFridge()
        {
            // 2026-11-24 の指摘: フライヤーが左の外の白い棚（大きな冷蔵庫）の上に乗っていない。冷蔵庫の上の面に乗り、はみ出さない。
            Scene kitchen = Open("Assets/_Project/Scenes/Stages/Stage_Kitchen.unity");
            Transform fryer = InScene<Transform>(kitchen).Single(t => t.name == "Fryer");
            Bounds body = fryer.Find("Body").GetComponent<Renderer>().bounds;
            Transform fridge = InScene<Transform>(kitchen).Where(t => t.name == "Prop_kitchenFridgeLarge").OrderBy(t => t.position.x).First();
            Renderer[] parts = fridge.GetComponentsInChildren<Renderer>();
            Bounds top = parts[0].bounds;
            foreach (Renderer part in parts)
            {
                top.Encapsulate(part.bounds);
            }
            string where = $"フライヤー {body.min}〜{body.max}、冷蔵庫 {top.min}〜{top.max}";
            Assert.That(body.min.y, Is.EqualTo(top.max.y).Within(0.15f), $"フライヤーが冷蔵庫の上の面に乗っていない（{where}）");
            Assert.That(body.min.x, Is.GreaterThanOrEqualTo(top.min.x - 0.05f), $"フライヤーが冷蔵庫からはみ出す（{where}）");
            Assert.That(body.max.x, Is.LessThanOrEqualTo(top.max.x + 0.05f), $"フライヤーが冷蔵庫からはみ出す（{where}）");
            Assert.That(body.min.z, Is.GreaterThanOrEqualTo(top.min.z - 0.05f), $"フライヤーが冷蔵庫の手前へはみ出す（{where}）");
            Assert.That(InScene<Transform>(kitchen).Any(t => t.name == "ChuteLeg"), Is.False, "宙に浮く樋の脚が残っている");
        }

        [Test]
        public void FifthGimmickBatch_SpaceStation_HasItsLooks_AndMatchesTheHitboxes()
        {
            // 2026-11-21 の方針の第 5 弾（宇宙ステーション）と、2026-11-24 の要望（ワープゲートは前の紫の楕円の見た目を精巧に、コイルはコイルガンに）。
            Scene space = Open("Assets/_Project/Scenes/Stages/Stage_SpaceStation.unity");
            // ワープゲート: 楕円の渦の面（Airerage/Wormhole）と出口の逆回りの渦。判定も渦と同じ楕円。前の平たい球はない。
            WormholeView[] mouths = InScene<WormholeView>(space).ToArray();
            Assert.That(mouths.Length, Is.EqualTo(3), "ワープゲートの渦");
            foreach (WormholeView mouth in mouths)
            {
                StageZone zone = mouth.GetComponentInParent<StageZoneAuthoring>().ToZone();
                Assert.That(zone.Round, Is.True, $"{zone}: 判定が楕円でない");
                Assert.That(mouth.Half.x, Is.EqualTo((zone.MaxX - zone.MinX) * 0.5f).Within(0.001f), "見た目の渦の大きさが判定と違う");
                Assert.That(mouth.Half.y, Is.EqualTo((zone.MaxY - zone.MinY) * 0.5f).Within(0.001f), "見た目の渦の大きさが判定と違う");
                Assert.That(mouth.Mouth.sharedMaterial.shader.name, Is.EqualTo("Airerage/Wormhole"));
                Assert.That(mouth.Mouth.transform.localPosition.z, Is.GreaterThan(0.1f), "渦の面がキャラクターの手前にある");
                Vector2 target = mouth.GetComponentInParent<StageZoneAuthoring>().Target;
                Assert.That(mouth.ExitMouth, Is.Not.Null, "出口の渦がない");
                Assert.That(Vector2.Distance(mouth.ExitMouth.transform.position, target), Is.LessThan(0.01f), "出口の渦が出口にない");
            }
            Assert.That(InScene<Transform>(space).Any(t => t.name == "Gate" || t.name == "Exit" || t.name == "Mouth"), Is.False,
                "前の平たい球のゲート・レンズの板が残っている");
            // 無重力: 宇宙船の中の漂う物と水の玉（Airerage/WaterDrop）と手すり。前の紫の板はない。漂う物は遊ぶ面より奥。
            MicrogravityView[] cabins = InScene<MicrogravityView>(space).ToArray();
            Assert.That(cabins.Length, Is.EqualTo(2), "宇宙船の中");
            foreach (MicrogravityView cabin in cabins)
            {
                Renderer[] drops = cabin.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "WaterDrop").ToArray();
                Assert.That(drops.Length, Is.EqualTo(3), "水の玉");
                Assert.That(drops.All(d => d.sharedMaterial.shader.name == "Airerage/WaterDrop"), Is.True, "水の玉の材質");
                Assert.That(cabin.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Handrail"), Is.EqualTo(2), "床と天井の手すり");
                Assert.That(cabin.GetComponentsInChildren<Light>(true).Count(l => l.type == LightType.Point && l.shadows == LightShadows.None), Is.EqualTo(2),
                    "船内の照明（影を落とさない点の光）");
                foreach (Transform child in cabin.transform)
                {
                    if (child.name != "Handrail" && child.name != "HandrailPost" && child.name != "CabinLight" && child.name != "CabinLamp")
                    {
                        Assert.That(child.localPosition.z, Is.GreaterThan(0.5f), $"{child.name} が遊ぶ面の手前に漂う（キャラクターを隠す）");
                    }
                }
            }
            Assert.That(InScene<Transform>(space).Any(t => t.name == "Field" || t.name == "FieldFront"), Is.False, "前の紫の板が残っている");
            // コイルガン（2026-11-24）: コイル 3 つの光る輪と火花・砲口の火花・軌道の線。前の金属の輪の枠はない。
            CoilAcceleratorView[] coils = InScene<CoilAcceleratorView>(space).ToArray();
            Assert.That(coils.Length, Is.EqualTo(3), "コイルガン");
            Assert.That(InScene<StageZoneAuthoring>(space).Any(z => z.Kind == StageZoneKind.BoostRing), Is.False, "宇宙ステーションに加速リングが残っている");
            foreach (CoilAcceleratorView coil in coils)
            {
                StageZone zone = coil.GetComponentInParent<StageZoneAuthoring>().ToZone();
                Assert.That(zone.Kind, Is.EqualTo(StageZoneKind.CoilGun), "コイルが撃ち出すギミックでない");
                Assert.That(coil.transform.parent.GetComponentsInChildren<LineRenderer>(true).Any(l => l.name == "Trajectory"), Is.True, "軌道の線がない");
                Assert.That(coil.BandCount, Is.EqualTo(3), "ため具合を外から見せる光る帯がない");
                Assert.That(coil.GetComponent<CoilDischargeView>(), Is.Not.Null, "ためと発射の演出（放電・光の玉・閃光）がない");
                Renderer[] liners = coil.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Liner").ToArray();
                Assert.That(liners.Length, Is.EqualTo(3), "コイルの光る輪");
                Assert.That(liners.All(l => l.sharedMaterial.shader.name == "Airerage/Emissive"), Is.True);
                Assert.That(coil.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(4), "コイルの火花と砲口の火花");
                Assert.That(coil.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Piece"), Is.False, "前の輪の枠が残っている");
                // コイルの並ぶ長さが範囲の幅に収まる。
                Transform first = coil.transform.Find("Coil1");
                Transform last = coil.transform.Find("Coil3");
                Assert.That(Vector3.Distance(first.position, last.position), Is.LessThan(zone.MaxX - zone.MinX), "コイルが範囲からはみ出す");
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
