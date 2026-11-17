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
            StageBlock[] blocks = InScene<StageBlock>(scene);
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
            Assert.That(backdrop.GetComponentsInChildren<BackdropMotion>().Any(m => m.Kind == BackdropMotion.Mode.Drift), Is.True, "横切る飛行船がない（V17）");
            Assert.That(backdrop.GetComponentsInChildren<BackdropMotion>().Any(m => m.Kind == BackdropMotion.Mode.Spin), Is.True, "回る輪がない（V17）");

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
