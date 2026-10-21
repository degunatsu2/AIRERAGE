using System;
using System.Collections.Generic;
using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using UnityEngine;

namespace Airerage.Editor
{
    /// <summary>
    /// キャラクターの仮の見た目（衣装。CostumeView。ユーザーの要望、2026-10-20）を、キャラクターの箱の子に作る。部品は箱の真ん中から
    /// の位置と大きさを m で書き、箱の大きさ（MovementTuning の初期値 0.8 × 1.8 × 0.8）で割って子の位置と大きさにする。向きで左右が
    /// 変わる部品（サングラス、背中の生命維持装置）は向きの印の子に作る。カメラは -Z 側にいるので、正面は z = -0.4。
    /// - RISK TAKER: アフロ、金色の服と襟、サングラス。
    /// - STORM RUNNER: ブレードシューズ（靴と 2 本の刃）、胸に「風」のゼッケン。
    /// - SPACE TRAVELER: 白い宇宙服と胸のパネル、首の輪、透明な宇宙帽、背中の生命維持装置。
    /// - PASTRY CHEF: クリーム色のコック帽（ふくらんだ上と帯）とコックコート（2 列のボタン、首のスカーフ）。
    /// </summary>
    internal static class CostumeBuilder
    {
        private static readonly Vector3 Body = new Vector3(0.8f, 1.8f, 0.8f);

        public static void Add(CharacterView view, Transform facing)
        {
            int count = Enum.GetValues(typeof(CharacterCostume)).Length;
            var bodySets = new GameObject[count];
            var facingSets = new GameObject[count];
            var texts = new List<TextMesh>();
            Material black = Lit("CostumeBlack", new Color(0.04f, 0.04f, 0.05f));

            // RISK TAKER: アフロ、金色の服、サングラス。
            Transform risk = Set(view.transform, "CostumeRiskTaker");
            bodySets[(int)CharacterCostume.RiskTaker] = risk.gameObject;
            Part(risk, PrimitiveType.Sphere, "Afro", new Vector3(0f, 0.95f, 0.05f), new Vector3(1.25f, 0.82f, 1.15f), Lit("CostumeAfro", new Color(0.16f, 0.09f, 0.05f)));
            Material gold = Lit("CostumeGold", new Color(1f, 0.78f, 0.16f));
            Part(risk, PrimitiveType.Cube, "GoldSuit", new Vector3(0f, -0.2f, 0f), new Vector3(0.86f, 0.98f, 0.86f), gold);
            Part(risk, PrimitiveType.Cube, "GoldCollar", new Vector3(0f, 0.31f, 0f), new Vector3(0.92f, 0.07f, 0.92f), Lit("CostumeGoldDark", new Color(0.8f, 0.55f, 0.08f)));
            Transform riskFacing = Set(facing, "CostumeRiskTakerFacing");
            facingSets[(int)CharacterCostume.RiskTaker] = riskFacing.gameObject;
            Part(riskFacing, PrimitiveType.Cube, "LensBack", new Vector3(0.02f, 0.5f, -0.435f), new Vector3(0.22f, 0.14f, 0.03f), black);
            Part(riskFacing, PrimitiveType.Cube, "LensFront", new Vector3(0.3f, 0.5f, -0.435f), new Vector3(0.22f, 0.14f, 0.03f), black);
            Part(riskFacing, PrimitiveType.Cube, "Bridge", new Vector3(0.16f, 0.53f, -0.435f), new Vector3(0.08f, 0.03f, 0.03f), black);

            // STORM RUNNER: ブレードシューズと「風」のゼッケン。
            Transform storm = Set(view.transform, "CostumeStormRunner");
            bodySets[(int)CharacterCostume.StormRunner] = storm.gameObject;
            Part(storm, PrimitiveType.Cube, "Shoes", new Vector3(0f, -0.76f, 0f), new Vector3(0.9f, 0.2f, 0.86f), Lit("CostumeShoes", new Color(0.12f, 0.14f, 0.2f)));
            Material blade = Lit("CostumeBlade", new Color(0.85f, 0.9f, 0.95f));
            Part(storm, PrimitiveType.Cube, "BladeFront", new Vector3(0f, -0.88f, -0.25f), new Vector3(1.05f, 0.04f, 0.06f), blade);
            Part(storm, PrimitiveType.Cube, "BladeRear", new Vector3(0f, -0.88f, 0.25f), new Vector3(1.05f, 0.04f, 0.06f), blade);
            Part(storm, PrimitiveType.Cube, "Bib", new Vector3(0f, -0.08f, -0.415f), new Vector3(0.56f, 0.5f, 0.02f), Lit("CostumeBib", Color.white));
            texts.Add(Text(storm, "BibText", "風", new Vector3(0f, -0.08f, -0.432f), 0.4f, new Color(0.05f, 0.25f, 0.15f)));

            // SPACE TRAVELER: 宇宙服と宇宙帽。
            Transform space = Set(view.transform, "CostumeSpaceTraveler");
            bodySets[(int)CharacterCostume.SpaceTraveler] = space.gameObject;
            Part(space, PrimitiveType.Cube, "Suit", new Vector3(0f, -0.2f, 0f), new Vector3(0.86f, 1.06f, 0.86f), Lit("CostumeSuit", new Color(0.92f, 0.93f, 0.96f)));
            Part(space, PrimitiveType.Cube, "ChestPanel", new Vector3(0f, 0.04f, -0.44f), new Vector3(0.3f, 0.16f, 0.02f),
                SandboxWireSceneBuilder.LoadOrCreateMaterial("CostumePanel", new Color(0.35f, 0.8f, 1f), unlit: true));
            Part(space, PrimitiveType.Cube, "NeckRing", new Vector3(0f, 0.33f, 0f), new Vector3(0.92f, 0.07f, 0.92f), Lit("CostumeRing", new Color(0.6f, 0.62f, 0.68f)));
            Part(space, PrimitiveType.Sphere, "Helmet", new Vector3(0f, 0.62f, 0f), new Vector3(1.1f, 0.8f, 1.1f),
                SandboxWireSceneBuilder.LoadOrCreateTransparentMaterial("CostumeHelmet", new Color(0.6f, 0.85f, 1f, 0.45f)));
            Transform spaceFacing = Set(facing, "CostumeSpaceTravelerFacing");
            facingSets[(int)CharacterCostume.SpaceTraveler] = spaceFacing.gameObject;
            Part(spaceFacing, PrimitiveType.Cube, "Backpack", new Vector3(-0.52f, 0f, 0f), new Vector3(0.24f, 0.78f, 0.62f), Lit("CostumePack", new Color(0.75f, 0.77f, 0.82f)));

            // PASTRY CHEF: クリーム色のコック帽とコックコート。
            Transform chef = Set(view.transform, "CostumePastryChef");
            bodySets[(int)CharacterCostume.PastryChef] = chef.gameObject;
            Material cream = Lit("CostumeCream", new Color(1f, 0.95f, 0.8f));
            Part(chef, PrimitiveType.Cube, "ChefCoat", new Vector3(0f, -0.18f, 0f), new Vector3(0.88f, 1.06f, 0.88f), cream);
            Material button = Lit("CostumeChefButton", new Color(0.78f, 0.62f, 0.45f));
            for (int row = 0; row < 3; row++)
            {
                float y = 0.12f - row * 0.2f;
                Part(chef, PrimitiveType.Sphere, $"ButtonLeft{row}", new Vector3(-0.12f, y, -0.445f), new Vector3(0.07f, 0.07f, 0.03f), button);
                Part(chef, PrimitiveType.Sphere, $"ButtonRight{row}", new Vector3(0.12f, y, -0.445f), new Vector3(0.07f, 0.07f, 0.03f), button);
            }
            Part(chef, PrimitiveType.Cube, "Scarf", new Vector3(0f, 0.32f, 0f), new Vector3(0.94f, 0.08f, 0.94f), Lit("CostumeChefScarf", new Color(1f, 0.6f, 0.68f)));
            Part(chef, PrimitiveType.Cylinder, "HatBand", new Vector3(0f, 1.0f, 0f), new Vector3(0.74f, 0.11f, 0.74f), cream);
            Part(chef, PrimitiveType.Sphere, "HatPuff", new Vector3(0f, 1.3f, 0f), new Vector3(1.0f, 0.62f, 1.0f), cream);

            var costume = view.gameObject.AddComponent<CostumeView>();
            costume.Configure(bodySets, facingSets, texts.ToArray(), view.GetComponent<Renderer>());
            SandboxWireSceneBuilder.SetReference(view, "_costume", costume);
        }

        private static Material Lit(string name, Color color)
        {
            return SandboxWireSceneBuilder.LoadOrCreateMaterial(name, color);
        }

        // 衣装 1 つの部品の親（最初は隠す）。
        private static Transform Set(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            return go.transform;
        }

        // 部品 1 つ（center と size は箱の真ん中からの m）。
        private static void Part(Transform parent, PrimitiveType type, string name, Vector3 center, Vector3 size, Material material)
        {
            GameObject go = SandboxWireSceneBuilder.CreateVisual(type, name, material, Divide(size));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Divide(center);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 文字（高さ height m。箱の大きさで伸びないように、子の大きさで打ち消す）。
        private static TextMesh Text(Transform parent, string name, string text, Vector3 center, float height, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Divide(center);
            go.transform.localScale = Divide(Vector3.one);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 64;
            // TextMesh の 1 行の高さは、およそ characterSize × fontSize / 10（m）。
            mesh.characterSize = height * 10f / 64f;
            mesh.color = color;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return mesh;
        }

        private static Vector3 Divide(Vector3 value)
        {
            return new Vector3(value.x / Body.x, value.y / Body.y, value.z / Body.z);
        }
    }
}
