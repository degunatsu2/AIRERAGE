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

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>
        /// モデルを decor の下に置く。position はモデルの足もと（OBJ の原点）、scale は全体の倍率、yaw は縦の軸の回転（度）。
        /// tint はパックの絵に掛ける色（夜は暗く）。モデルがなければ警告して null。
        /// </summary>
        public static GameObject Place(StageDecor decor, Pack pack, string model, Vector3 position, float scale, float yaw, Color tint, string folder)
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
            Material material = MaterialFor(pack, tint, folder);
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
        private static Material MaterialFor(Pack pack, Color tint, string folder)
        {
            string key = $"{folder}|{pack.Folder}|{ColorUtility.ToHtmlStringRGB(tint)}";
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
            string name = $"Prop{pack.Folder.Replace("/", string.Empty)}_{ColorUtility.ToHtmlStringRGB(tint)}";
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", texture);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Smoothness", 0.25f);
                m.SetFloat("_Metallic", 0f);
            }, folder);
            Materials[key] = material;
            return material;
        }
    }
}
