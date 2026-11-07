using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// クリックした所の火花（ユーザーの要望、2026-11-07）: メニューでマウスの左ボタンを押すと、押した所に決定の衝撃（UiBurst）と
    /// 同じ水色の輪と金色の火花を出す。札の上でも、何もない所でも出す。マウスで札を決定したときは、札は自分の輪と火花を出さない
    /// （MenuOption.Punch。キーボード・ゲームパッドで決定したときは、今までどおり札が出す）。
    /// 画面いっぱいの透明な層（クリックを受け止めない）。メニューの Canvas の最後の子（画面の切り替えの写しより手前）や、一時停止・
    /// 結果の画面の中に置く（その画面を出している間だけ動く）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ClickSparks : MaskableGraphic
    {
        private const int MaxBursts = 8;
        private static readonly Vector2 RingFrom = new Vector2(14f, 14f);
        private static readonly Vector2 RingTo = new Vector2(78f, 78f);

        private struct Burst
        {
            public Vector2 Point;
            public float At;
            public int Seed;
        }

        private readonly List<Burst> _bursts = new List<Burst>();

        /// <summary>今動いている輪と火花の数（テスト用）。</summary>
        public int Playing
        {
            get
            {
                int count = 0;
                float now = Time.unscaledTime;
                foreach (Burst burst in _bursts)
                {
                    if (now - burst.At < UiBurst.Seconds)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// component と同じ Canvas に、今動いているクリックの火花の層があるか（マウスで決定した札が、自分の火花を出さないかを決める）。
        /// </summary>
        public static bool Covers(Component component)
        {
            Canvas canvas = component != null ? component.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
            {
                return false;
            }
            ClickSparks sparks = canvas.rootCanvas.GetComponentInChildren<ClickSparks>(false);
            return sparks != null && sparks.isActiveAndEnabled;
        }

        /// <summary>画面の位置 screen に、輪と火花を出す。</summary>
        public void Spawn(Vector2 screen)
        {
            Canvas canvas = this.canvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, cam, out Vector2 local))
            {
                return;
            }
            if (_bursts.Count >= MaxBursts)
            {
                _bursts.RemoveAt(0);
            }
            _bursts.Add(new Burst { Point = local, At = Time.unscaledTime, Seed = Random.Range(1, int.MaxValue) });
            SetVerticesDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnDisable()
        {
            _bursts.Clear();
            base.OnDisable();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Spawn(mouse.position.ReadValue());
            }
            if (_bursts.Count == 0)
            {
                return;
            }
            float now = Time.unscaledTime;
            _bursts.RemoveAll(b => now - b.At >= UiBurst.Seconds + 0.05f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!Application.isPlaying)
            {
                return;
            }
            float now = Time.unscaledTime;
            foreach (Burst burst in _bursts)
            {
                float t = now - burst.At;
                UiBurst.DrawRing(vh, burst.Point, RingFrom, RingTo, t);
                UiBurst.DrawSparks(vh, burst.Point, t, burst.Seed);
            }
        }
    }
}
