using System.Collections.Generic;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 画面の HUD（右上の表・タイマー・告知の札・技の札・コンボ）とキャラクターが重なったら、その HUD を少し透かす（ユーザーの要望、2026-10-30:
    /// キャラクターとステージがしっかり見えるように）。毎フレーム、キャラクター（復帰を待っている人・飛び去っている人・透明の人を除く）の体の
    /// 箱を画面の四角（OnGUI の座標）にして覚え、HUD が OnGUI で AlphaFor を呼ぶと、その部品の四角と重なっていれば FadedAlpha へ、離れたら 1 へ
    /// 滑らかに寄せた不透明さを返す。HUD はその値で HudFade.Begin の範囲を描く。シーンに 1 つ置く（なければ HUD は透かさない）。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class HudOcclusion : MonoBehaviour
    {
        /// <summary>重なっている HUD の不透明さ【仮】。</summary>
        public const float FadedAlpha = 0.35f;

        /// <summary>透かす・戻すのにかかる秒数【仮】。</summary>
        public const float FadeSeconds = 0.15f;

        [Tooltip("状態を読む相手（SimulationRunner・LocalMatchRunner・OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("キャラクターの位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("体の箱の周りに足す余白（画面の点）")]
        [SerializeField] private float _padding = 10f;

        private readonly List<Rect> _bodies = new List<Rect>();
        private readonly Dictionary<(int, int), Fade> _fades = new Dictionary<(int, int), Fade>();
        private ISimulationFeed _feed;

        private struct Fade
        {
            public float Alpha;
            public int Frame;
        }

        /// <summary>今のフレームのキャラクターの体の四角（OnGUI の座標。テスト用）。</summary>
        public IReadOnlyList<Rect> Bodies => _bodies;

        /// <summary>
        /// HUD が持つ、シーンの HudOcclusion への参照（最初に使うときに 1 回だけ探す。シーンになければ透かさない）。
        /// </summary>
        public sealed class Link
        {
            private HudOcclusion _occlusion;
            private bool _searched;

            /// <summary>owner の part 番目の部品（画面の四角 guiRect）の不透明さ（HudOcclusion.AlphaFor。なければ 1）。</summary>
            public float AlphaFor(Object owner, int part, Rect guiRect)
            {
                if (!_searched)
                {
                    _occlusion = FindFirstObjectByType<HudOcclusion>();
                    _searched = true;
                }
                return _occlusion != null ? _occlusion.AlphaFor(owner, part, guiRect) : 1f;
            }
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            _bodies.Clear();
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
            {
                return;
            }
            for (int i = 0; i < _feed.CharacterCount; i++)
            {
                CharacterState state = _feed.GetState(i);
                if (state.Combat.IsWaitingToRespawn || state.Combat.IsAway || state.Combat.InvisibleTicksLeft > 0)
                {
                    continue;
                }
                CharacterConfig config = _feed.GetConfig(i);
                var half = new Vector3(config.Movement.HalfWidth, config.Movement.HalfHeight, 0f);
                var center = new Vector3(state.Position.X, state.Position.Y, 0f);
                if (TryScreenRect(camera, center - half, center + half, out Rect rect))
                {
                    _bodies.Add(new Rect(rect.x - _padding, rect.y - _padding, rect.width + _padding * 2f, rect.height + _padding * 2f));
                }
            }
        }

        /// <summary>画面の四角 guiRect（OnGUI の座標）が、だれかの体と重なっているか。</summary>
        public bool Overlaps(Rect guiRect)
        {
            foreach (Rect body in _bodies)
            {
                if (body.Overlaps(guiRect))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// HUD の部品（owner の part 番目）の不透明さ。guiRect（OnGUI の座標）が体と重なっていれば FadedAlpha へ、離れていれば 1 へ、
        /// 1 フレームに 1 回だけ滑らかに寄せる（OnGUI は 1 フレームに何回も呼ばれる）。
        /// </summary>
        public float AlphaFor(Object owner, int part, Rect guiRect)
        {
            var key = (owner.GetInstanceID(), part);
            if (!_fades.TryGetValue(key, out Fade fade))
            {
                fade = new Fade { Alpha = 1f, Frame = -1 };
            }
            if (fade.Frame != Time.frameCount)
            {
                float target = Overlaps(guiRect) ? FadedAlpha : 1f;
                float step = Time.unscaledDeltaTime / FadeSeconds * (1f - FadedAlpha);
                fade.Alpha = Mathf.MoveTowards(fade.Alpha, target, step);
                fade.Frame = Time.frameCount;
                _fades[key] = fade;
            }
            return fade.Alpha;
        }

        // 世界の箱（min〜max）を画面の四角（OnGUI の座標）にする。カメラの後ろなら false。
        private static bool TryScreenRect(Camera camera, Vector3 min, Vector3 max, out Rect rect)
        {
            Vector3 a = camera.WorldToScreenPoint(min);
            Vector3 b = camera.WorldToScreenPoint(max);
            rect = default;
            if (a.z <= 0f || b.z <= 0f)
            {
                return false;
            }
            float xMin = Mathf.Min(a.x, b.x);
            float xMax = Mathf.Max(a.x, b.x);
            float yMin = Screen.height - Mathf.Max(a.y, b.y);
            float yMax = Screen.height - Mathf.Min(a.y, b.y);
            rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }
    }
}
