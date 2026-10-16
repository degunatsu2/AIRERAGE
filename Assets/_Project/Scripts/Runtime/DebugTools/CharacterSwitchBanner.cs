using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.DebugTools
{
    /// <summary>
    /// オフラインのキャラクターの選択（F3。docs/13 T5-5）で切り替えたとき、画面の中央にキャラクターのロゴと名前を少しの間出す
    /// （docs/13 T5-8。オンラインの選択は待機所に出す）。最初のキャラクターでは出さない。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class CharacterSwitchBanner : MonoBehaviour
    {
        private const float ShowSeconds = 1.6f;
        private const float FadeSeconds = 0.4f;
        private const float LogoSize = 96f;

        [SerializeField] private SimulationRunner _runner;

        private CharacterConfigAsset _shown;
        private float _changedAt = float.NegativeInfinity;
        private GUIStyle _name;

        /// <summary>今出しているキャラクター（出していなければ null。テスト用）。</summary>
        public CharacterConfigAsset Showing => Time.time - _changedAt < ShowSeconds ? _shown : null;

        private void Awake()
        {
            if (_runner == null)
            {
                Debug.LogError($"{name}: SimulationRunner が設定されていません", this);
                enabled = false;
            }
        }

        private void Update()
        {
            CharacterConfigAsset current = _runner.PlayerCharacter;
            if (current == _shown)
            {
                return;
            }
            if (_shown != null)
            {
                _changedAt = Time.time;
            }
            _shown = current;
        }

        private void OnGUI()
        {
            CharacterConfigAsset character = Showing;
            if (character == null)
            {
                return;
            }
            _name = _name ?? new GUIStyle(GUI.skin.label)
            {
                fontSize = 30, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, wordWrap = false,
                normal = { textColor = Color.white },
            };
            float elapsed = Time.time - _changedAt;
            float alpha = Mathf.Clamp01(Mathf.Min(elapsed / 0.12f, (ShowSeconds - elapsed) / FadeSeconds));
            float rise = (1f - Mathf.Clamp01(elapsed / 0.2f)) * 16f;
            float centerY = Screen.height * 0.32f + rise;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(0f, centerY - LogoSize * 0.5f - 14f, Screen.width, LogoSize + 64f), Texture2D.whiteTexture);
            if (character.Logo != null)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(new Rect((Screen.width - LogoSize) * 0.5f, centerY - LogoSize * 0.5f, LogoSize, LogoSize), character.Logo,
                    ScaleMode.ScaleToFit);
            }
            GUI.color = previous;
            GuidePlate.DrawText(new Rect(0f, centerY + LogoSize * 0.5f + 2f, Screen.width, 40f), character.DisplayName, _name,
                new Color(1f, 1f, 1f, alpha));
        }
    }
}
