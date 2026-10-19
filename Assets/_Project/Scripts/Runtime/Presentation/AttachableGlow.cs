using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ワイヤーが刺さる足場（WireAttachable）の光（表示だけ。ユーザーの要望、2026-10-19。ステージの明るさが違っても、刺さらない足場と
    /// 見分けられるように）: 正面の縁のオレンジの枠が脈打ち、明るい光の粒が縁を回る。枠と面の色はシーンのビルダーが作る
    /// （SandboxWireSceneBuilder.CreateTerrain）。動きは Time.time で決める（巻き戻しとは関係ない）。
    /// </summary>
    public sealed class AttachableGlow : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("脈打たせる縁の枠")]
        [SerializeField] private Renderer[] _rims = new Renderer[0];
        [Tooltip("縁を回る光の粒（足場 1 つに 1 本。_boxes と同じ順）")]
        [SerializeField] private LineRenderer[] _sparks = new LineRenderer[0];
        [Tooltip("足場の正面の範囲（x, y が最小、z, w が最大）")]
        [SerializeField] private Vector4[] _boxes = new Vector4[0];
        [SerializeField] private Color _color = new Color(1f, 0.5f, 0.08f);
        [Tooltip("正面の手前の位置（z）")]
        [SerializeField] private float _front = -2.05f;
        [Tooltip("脈打つ周期（秒）")]
        [SerializeField] private float _pulseSeconds = 1.6f;
        [Tooltip("光の粒の速さ（m/s）")]
        [SerializeField] private float _sparkSpeed = 6f;
        [Tooltip("光の粒の長さ（m）")]
        [SerializeField] private float _sparkLength = 1.6f;

        private MaterialPropertyBlock _block;

        /// <summary>今の縁の明るさ（0〜1。テスト用）。</summary>
        public float Brightness { get; private set; }

        /// <summary>光る足場の数（テスト用）。</summary>
        public int BoxCount => _boxes.Length;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Renderer[] rims, LineRenderer[] sparks, Vector4[] boxes, Color color, float front)
        {
            _rims = rims;
            _sparks = sparks;
            _boxes = boxes;
            _color = color;
            _front = front;
        }

        private void Awake()
        {
            if (_sparks.Length != _boxes.Length)
            {
                Debug.LogError($"{name}: 光の粒（{_sparks.Length}）と足場（{_boxes.Length}）の数が合いません", this);
                enabled = false;
            }
        }

        private void Update()
        {
            float phase = Mathf.Repeat(Time.time / Mathf.Max(0.1f, _pulseSeconds), 1f);
            Brightness = 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f));
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }
            _block.SetColor(BaseColorId, _color * Brightness);
            foreach (Renderer rim in _rims)
            {
                if (rim != null)
                {
                    rim.SetPropertyBlock(_block);
                }
            }
            for (int i = 0; i < _sparks.Length; i++)
            {
                RenderSpark(_sparks[i], _boxes[i], i);
            }
        }

        // 縁を回る光の粒（足場ごとに出発点をずらす）。
        private void RenderSpark(LineRenderer spark, Vector4 box, int index)
        {
            if (spark == null)
            {
                return;
            }
            float width = box.z - box.x;
            float height = box.w - box.y;
            float perimeter = 2f * (width + height);
            if (perimeter <= 0f)
            {
                return;
            }
            float head = Mathf.Repeat(Time.time * _sparkSpeed + index * 7.3f, perimeter);
            float length = Mathf.Min(_sparkLength, perimeter * 0.25f);
            spark.positionCount = 3;
            spark.SetPosition(0, PointOnPerimeter(box, head - length));
            spark.SetPosition(1, PointOnPerimeter(box, head - length * 0.5f));
            spark.SetPosition(2, PointOnPerimeter(box, head));
        }

        // 左下から時計回り（上の辺 → 右の辺 → 下の辺 → 左の辺）に、縁を distance だけ進んだ点。
        private Vector3 PointOnPerimeter(Vector4 box, float distance)
        {
            float width = box.z - box.x;
            float height = box.w - box.y;
            float perimeter = 2f * (width + height);
            float d = Mathf.Repeat(distance, perimeter);
            if (d < height)
            {
                return new Vector3(box.x, box.y + d, _front);
            }
            d -= height;
            if (d < width)
            {
                return new Vector3(box.x + d, box.w, _front);
            }
            d -= width;
            if (d < height)
            {
                return new Vector3(box.z, box.w - d, _front);
            }
            d -= height;
            return new Vector3(box.z - d, box.y, _front);
        }
    }
}
