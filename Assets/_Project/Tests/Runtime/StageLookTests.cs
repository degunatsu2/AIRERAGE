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
            Assert.That(blocks.Length, Is.EqualTo(terrain.Length), "地形の箱ごとに面取りした見た目がない");
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
                if ((renderer.name != "Skyline" && renderer.name != "Block") || Mathf.Abs(renderer.bounds.center.x) > 40f)
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
        public void Kitchen_AndManor_UseTheKayKitProps()
        {
            // 2026-11-18 のユーザーの指示: push したアセット（KayKit。CC0）を背景に組み込む。小物は色の絵の材質で、当たりはない。
            foreach (string stage in new[] { "Stage_Kitchen", "Stage_Manor" })
            {
                Scene scene = Open($"Assets/_Project/Scenes/Stages/{stage}.unity");
                Transform decor = scene.GetRootGameObjects().Single(g => g.name == "Decor").transform;
                Transform[] props = decor.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Prop_")).ToArray();
                Assert.That(props.Length, Is.GreaterThan(8), $"{stage} に背景の小物が少ない");
                foreach (Transform prop in props)
                {
                    Renderer renderer = prop.GetComponentInChildren<Renderer>();
                    Assert.That(renderer, Is.Not.Null, $"{stage} の {prop.name} に見た目がない");
                    Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null, $"{stage} の {prop.name} に色の絵がない");
                    Assert.That(prop.GetComponentsInChildren<Collider>(true), Is.Empty, $"{stage} の {prop.name} に当たりがある");
                    // 遊ぶ面（z 0 のまわり）に出ない。
                    Assert.That(renderer.bounds.min.z, Is.GreaterThan(0f), $"{stage} の {prop.name} が遊ぶ面より手前に出ている");
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
        public void DefaultPostProcessing_IsNeutral_SoStagesWithoutAVolumeLookTheSame()
        {
            // URP の設定のアセットの既定の Volume（テンプレートの Bloom・モーションブラーなど）は外した。ステージの Volume だけが効く。
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.volumeProfile, Is.Null, "既定の Volume が残っている（モーションブラーなどが全ステージに効く）");
        }
    }
}
