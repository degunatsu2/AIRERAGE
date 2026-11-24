using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Airerage.Editor
{
    /// <summary>
    /// 画面全体の処理の準備（2026-11-24。FATAL の衝撃波 F6）: PC の描き方（PC_Renderer）に、URP の全画面の処理
    /// （FullScreenPassRendererFeature）を 1 つ足し、Airerage/Shockwave の材質で、仕上げの画面処理（ブルームなど）の前に画面を描き直す。
    /// 波がない間は写した画面をそのまま返す（ScreenShockwaves が値を入れる）。何度呼んでも 1 つだけ（あれば設定を直す）。
    /// </summary>
    internal static class ScreenEffectsSetup
    {
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string FeatureName = "Shockwave";
        private const string MaterialFolder = "Assets/_Project/Materials/ScreenEffects";

        public static void EnsureShockwavePass()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                Debug.LogError($"描き方の設定が見つからない（{RendererPath}）。FATAL の衝撃波は出ない");
                return;
            }
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("Shockwave", "Airerage/Shockwave", null, MaterialFolder);
            var feature = renderer.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f != null && f.name == FeatureName);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = FeatureName;
                AssetDatabase.AddObjectToAsset(feature, renderer);
                // 描き方の設定の一覧と、その番号の表（m_RendererFeatureMap）の両方に足す（インスペクターで足すのと同じ）。
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                var serialized = new SerializedObject(renderer);
                SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
                SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.requirements = ScriptableRenderPassInput.None;
            feature.passMaterial = material;
            feature.passIndex = 0;
            feature.bindDepthStencilAttachment = false;
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }
    }
}
