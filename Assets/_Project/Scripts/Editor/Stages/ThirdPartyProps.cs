using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 受け取ったアセット（KayKit など。CC0。`Assets/_Project/ThirdParty/`）のモデルを、ステージの背景の小物として置く（2026-11-18 の
    /// ユーザーの指示: push したアセットを使った背景への組み込みを進める）。モデルは OBJ をそのまま使い、材質はパックごとの色の絵 1 枚で
    /// 作る（URP の Lit。夜のステージは色を暗くした材質）。当たりはない（見た目だけ。地形の当たりは今の箱のまま）。
    /// </summary>
    internal static class ThirdPartyProps
    {
        private const string Root = "Assets/_Project/ThirdParty";

        /// <summary>パック（フォルダと色の絵）。</summary>
        public readonly struct Pack
        {
            public readonly string Folder;
            public readonly string Texture;

            public Pack(string folder, string texture)
            {
                Folder = folder;
                Texture = texture;
            }
        }

        public static readonly Pack Halloween = new Pack("KayKit/Halloween", "halloweenbits_texture.png");
        public static readonly Pack Restaurant = new Pack("KayKit/Restaurant", "restaurantbits_texture.png");
        public static readonly Pack SpaceBase = new Pack("KayKit/SpaceBase", "spacebits_texture.png");
        public static readonly Pack CityCommercial = new Pack("Kenney/CityCommercial", "colormap.png");
        public static readonly Pack CityIndustrial = new Pack("Kenney/CityIndustrial", "colormap.png");
        public static readonly Pack Watercraft = new Pack("Kenney/Watercraft", "colormap.png");
        public static readonly Pack Holiday = new Pack("Kenney/Holiday", "colormap.png");
        public static readonly Pack CityRoads = new Pack("Kenney/CityRoads", "colormap.png");

        /// <summary>夜に窓だけを光らせる発光の絵（tools/assets/kenney_night_emission.py で色の絵から作る）。</summary>
        public const string CityCommercialNightEmission = "colormap_night_emission.png";

        /// <summary>背景の小物の材質のシェーダー（色・法線・発光の絵をキーワードなしで使う。Shaders/PropLit）。</summary>
        public const string PropShader = "Airerage/PropLit";

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>
        /// モデルを decor の下に置く。position はモデルの足もと（OBJ の原点）、scale は全体の倍率、yaw は縦の軸の回転（度）。
        /// tint はパックの絵に掛ける色（夜は暗く）。モデルがなければ警告して null。
        /// </summary>
        public static GameObject Place(StageDecor decor, Pack pack, string model, Vector3 position, float scale, float yaw, Color tint, string folder)
        {
            return Place(decor, pack, model, position, scale, yaw, tint, folder, null, Color.black);
        }

        /// <summary>
        /// 発光の絵つきで置く（emissionTexture はパックのフォルダの中の絵の名前。emission はその絵に掛ける色。HDR でブルームににじむ）。
        /// </summary>
        public static GameObject Place(StageDecor decor, Pack pack, string model, Vector3 position, float scale, float yaw, Color tint, string folder,
            string emissionTexture, Color emission)
        {
            string path = $"{Root}/{pack.Folder}/{model}.obj";
            GameObject source = LoadModel(path);
            if (source == null)
            {
                Debug.LogWarning($"背景の小物のモデルがない（{path}）。置かない");
                return null;
            }
            GameObject go = Object.Instantiate(source, decor.Root);
            go.name = $"Prop_{model}";
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            Material material = MaterialFor(pack, tint, folder, emissionTexture, emission);
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return go;
        }

        /// <summary>
        /// 材質が何枚もあるモデル（Quaternius など。面ごとに OBJ の usemtl の材質名がある）を置く。材質は名前から materials で選ぶ
        /// （null を返した名前と、名前が分からない面は fallback）。rotation・scale はそのまま。モデルがなければ警告して null。
        /// </summary>
        public static GameObject PlaceMulti(StageDecor decor, string packFolder, string model, Vector3 position, Quaternion rotation, Vector3 scale,
            System.Func<string, Material> materials, Material fallback)
        {
            string path = $"{Root}/{packFolder}/{model}.obj";
            GameObject source = LoadNamedModel(path);
            if (source == null)
            {
                Debug.LogWarning($"背景の小物のモデルがない（{path}）。置かない");
                return null;
            }
            GameObject go = Object.Instantiate(source, decor.Root);
            go.name = $"Prop_{model}";
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] imported = renderer.sharedMaterials;
                var assigned = new Material[imported.Length];
                for (int i = 0; i < imported.Length; i++)
                {
                    Material chosen = imported[i] != null ? materials(imported[i].name) : null;
                    if (chosen == null)
                    {
                        Debug.LogWarning($"背景の小物 {model} の材質 {(imported[i] != null ? imported[i].name : "(なし)")} に当てる材質がない。代わりの材質にする");
                        chosen = fallback;
                    }
                    assigned[i] = chosen;
                }
                renderer.sharedMaterials = assigned;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return go;
        }

        /// <summary>
        /// 材質の色や絵が .mtl に書いてあるモデル（Kenney の Nature・Furniture・Racing、Quaternius の Nature など）を置く。
        /// .mtl の Kd（色）・map_Kd（色の絵）・map_Bump（法線の絵）・map_d（切り抜き）から、材質の名前ごとに背景の小物の材質を作る
        /// （色の絵があるときは Kd の代わりに tint だけを掛ける）。絵はモデルと同じフォルダに置く（.mtl の絵の行はファイル名だけ）。
        /// </summary>
        public static GameObject PlaceMtl(StageDecor decor, string packFolder, string model, Vector3 position, float yaw, float scale, Color tint, string folder)
        {
            Dictionary<string, MtlEntry> entries = ReadMtl($"{Root}/{packFolder}/{model}.mtl");
            Material fallback = MtlMaterial(packFolder, "Fallback", new MtlEntry { Color = new Color(0.6f, 0.6f, 0.6f) }, tint, folder);
            return PlaceMulti(decor, packFolder, model, position, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale,
                name => entries.TryGetValue(name, out MtlEntry entry) ? MtlMaterial(packFolder, name, entry, tint, folder) : null, fallback);
        }

        private struct MtlEntry
        {
            public Color Color;
            public string ColorMap;
            public string NormalMap;
            public bool Cutout;
        }

        // .mtl を読む（newmtl ごとの Kd・map_Kd・map_Bump・map_d）。
        private static Dictionary<string, MtlEntry> ReadMtl(string path)
        {
            var result = new Dictionary<string, MtlEntry>();
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"背景の小物の .mtl がない（{path}）。代わりの材質にする");
                return result;
            }
            string current = null;
            var entry = new MtlEntry { Color = Color.white };
            foreach (string raw in System.IO.File.ReadAllLines(path))
            {
                string line = raw.Trim();
                string[] parts = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }
                switch (parts[0])
                {
                    case "newmtl":
                        if (current != null)
                        {
                            result[current] = entry;
                        }
                        current = parts[1];
                        entry = new MtlEntry { Color = Color.white };
                        break;
                    case "Kd" when parts.Length >= 4:
                        entry.Color = new Color(Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
                        break;
                    case "map_Kd":
                        entry.ColorMap = parts[parts.Length - 1];
                        break;
                    case "map_Bump":
                    case "bump":
                        entry.NormalMap = parts[parts.Length - 1];
                        break;
                    case "map_d":
                        entry.Cutout = true;
                        break;
                }
            }
            if (current != null)
            {
                result[current] = entry;
            }
            return result;
        }

        private static float Parse(string text)
        {
            return float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }

        // .mtl の 1 つの材質から背景の小物の材質を作る（パック・名前・色合いごとに 1 つ。ステージの材質のフォルダに保存）。
        private static Material MtlMaterial(string packFolder, string name, MtlEntry entry, Color tint, string folder)
        {
            string key = $"{folder}|{packFolder}|{name}|{ColorUtility.ToHtmlStringRGB(tint)}";
            if (Materials.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }
            Texture2D colorMap = entry.ColorMap != null ? LoadTextureFile($"{Root}/{packFolder}/{entry.ColorMap}", false) : null;
            Texture2D normalMap = entry.NormalMap != null ? LoadTextureFile($"{Root}/{packFolder}/{entry.NormalMap}", true) : null;
            Color color = colorMap != null ? tint : entry.Color * tint;
            color.a = 1f;
            string asset = $"Mtl{packFolder.Replace("/", string.Empty)}_{name}_{ColorUtility.ToHtmlStringRGB(tint)}";
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(asset, PropShader, m =>
            {
                m.SetTexture("_BaseMap", colorMap);
                m.SetColor("_BaseColor", color);
                m.SetTexture("_BumpMap", normalMap);
                m.SetFloat("_BumpScale", 1f);
                m.SetTexture("_EmissionMap", null);
                m.SetColor("_EmissionColor", Color.black);
                m.SetFloat("_Smoothness", 0.2f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Cutoff", entry.Cutout ? 0.5f : 0f);
                m.SetFloat("_Cull", entry.Cutout ? 0f : 2f);
            }, folder);
            Materials[key] = material;
            return material;
        }

        // 絵のファイルを読む（拡張子つきのパス）。normalMap なら法線の絵として読み込み直す。なければ警告して null。
        private static Texture2D LoadTextureFile(string path, bool normalMap)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != type || importer.sRGBTexture == normalMap || !importer.mipmapEnabled)
                {
                    importer.textureType = type;
                    importer.sRGBTexture = !normalMap;
                    importer.mipmapEnabled = true;
                    importer.SaveAndReimport();
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning($"背景の小物の絵がない（{path}）");
            }
            return texture;
        }

        /// <summary>パックのフォルダの絵を読む（normalMap なら法線の絵として読み込み直す。色の絵は sRGB）。なければ警告して null。</summary>
        public static Texture2D LoadTexture(string packFolder, string name, bool normalMap)
        {
            string path = $"{Root}/{packFolder}/{name}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != type || importer.sRGBTexture == normalMap || !importer.mipmapEnabled || importer.anisoLevel < 8)
                {
                    importer.textureType = type;
                    importer.sRGBTexture = !normalMap;
                    importer.mipmapEnabled = true;
                    importer.anisoLevel = 8;
                    importer.SaveAndReimport();
                }
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning($"背景の小物の絵がない（{path}）");
            }
            return texture;
        }

        // 材質の名前を残して OBJ を読む（面ごとの材質の名前で、置くときに材質を選ぶ。法線はモデルのものを使い、接線は計算する）。
        private static GameObject LoadNamedModel(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                return null;
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription
                || importer.materialLocation != ModelImporterMaterialLocation.InPrefab
                || importer.importNormals != ModelImporterNormals.Import || importer.importTangents != ModelImporterTangents.CalculateMikk
                || importer.importCameras || importer.importLights || importer.isReadable)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // OBJ を読む（材質は作らず、法線は計算する）。
        private static GameObject LoadModel(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                return null;
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importNormals != ModelImporterNormals.Calculate
                || importer.importCameras || importer.importLights || importer.isReadable)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.normalSmoothingAngle = 40f;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // パックの色の絵の材質（色合いごと。ステージの材質のフォルダに保存して GUID を保つ）。
        private static Material MaterialFor(Pack pack, Color tint, string folder, string emissionTexture, Color emission)
        {
            bool glows = !string.IsNullOrEmpty(emissionTexture);
            string glowKey = glows ? $"_E{ColorUtility.ToHtmlStringRGB(emission / Mathf.Max(1f, emission.maxColorComponent))}{Mathf.RoundToInt(emission.maxColorComponent * 10f)}" : string.Empty;
            string key = $"{folder}|{pack.Folder}|{ColorUtility.ToHtmlStringRGB(tint)}{glowKey}";
            if (Materials.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }
            string texturePath = $"{Root}/{pack.Folder}/{pack.Texture}";
            if (AssetImporter.GetAtPath(texturePath) is TextureImporter textureImporter && (!textureImporter.sRGBTexture || !textureImporter.mipmapEnabled))
            {
                textureImporter.sRGBTexture = true;
                textureImporter.mipmapEnabled = true;
                textureImporter.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Texture2D emissionMap = null;
            if (glows)
            {
                string emissionPath = $"{Root}/{pack.Folder}/{emissionTexture}";
                if (AssetImporter.GetAtPath(emissionPath) is TextureImporter emissionImporter && (!emissionImporter.sRGBTexture || !emissionImporter.mipmapEnabled))
                {
                    emissionImporter.sRGBTexture = true;
                    emissionImporter.mipmapEnabled = true;
                    emissionImporter.SaveAndReimport();
                }
                emissionMap = AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath);
                if (emissionMap == null)
                {
                    Debug.LogWarning($"背景の小物の発光の絵がない（{emissionPath}）。光らせない");
                }
            }
            string name = $"Prop{pack.Folder.Replace("/", string.Empty)}_{ColorUtility.ToHtmlStringRGB(tint)}{glowKey}";
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, PropShader, m =>
            {
                m.SetTexture("_BaseMap", texture);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Smoothness", 0.25f);
                m.SetFloat("_Metallic", 0f);
                m.SetTexture("_EmissionMap", emissionMap);
                m.SetColor("_EmissionColor", emissionMap != null ? emission : Color.black);
            }, folder);
            Materials[key] = material;
            return material;
        }
    }
}
