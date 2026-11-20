using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 悪魔の手に握られた人の見た目（恐怖の館。ユーザーの要望、2026-10-26。表示だけ）: 紫の時空の渦が広がって体を吸い込む（suck 0→1。
    /// 体は CharacterView が小さくして回す）。牢屋に現れたときは渦が一瞬広がって消える（Arrive）。渦は起動時に、渡された材質で作る。
    /// 握る手はステージの悪魔の手そのもの（DemonHandView が指を閉じる）。前はここで箱の指を別に出していて、手を作り直した後に古い手が
    /// 残って見えた（2026-11-20 の指摘）ので、出さない。
    /// </summary>
    public sealed class CaptureEffectView : MonoBehaviour
    {
        private const int SpiralPoints = 64;
        private const float ArriveSeconds = 0.6f;

        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Color _vortexColor = new Color(0.65f, 0.25f, 1f, 0.9f);

        private LineRenderer _spiral;
        private LineRenderer _rim;
        private float _arriveStarted = -10f;
        private Vector3 _arriveAt;

        /// <summary>握っている見た目を出しているか（テスト用）。</summary>
        public bool Showing { get; private set; }

        /// <summary>渦を出しているか（テスト用）。</summary>
        public bool ShowsVortex => _spiral != null && _spiral.enabled;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Material lineMaterial, Color vortexColor)
        {
            _lineMaterial = lineMaterial;
            _vortexColor = vortexColor;
        }

        private void Awake()
        {
            _spiral = CreateLine("Spiral", SpiralPoints, 0.12f);
            _rim = CreateLine("Rim", 40, 0.08f);
            _rim.loop = true;
            HideAll();
        }

        private LineRenderer CreateLine(string name, int points, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _lineMaterial;
            line.useWorldSpace = true;
            line.positionCount = points;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = _vortexColor;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        /// <summary>
        /// 握られている見た目を出す（center は体の中心。grip は指の閉じ具合 0〜1、suck は渦の進み 0〜1）。
        /// </summary>
        public void Show(Vector3 center, float grip, float suck)
        {
            Showing = true;
            float radius = Mathf.Lerp(0.2f, 1.8f, Mathf.Sin(Mathf.Clamp01(suck) * Mathf.PI * 0.85f));
            DrawVortex(center, suck > 0f ? radius : 0f, suck > 0f ? 1f : 0f);
        }

        /// <summary>握られている見た目を消す。</summary>
        public void Hide()
        {
            if (Showing)
            {
                HideAll();
            }
        }

        /// <summary>牢屋に現れたときの、一瞬の渦（center は現れた位置）。</summary>
        public void Arrive(Vector3 center)
        {
            _arriveStarted = Time.time;
            _arriveAt = center;
        }

        private void HideAll()
        {
            Showing = false;
            if (_spiral != null)
            {
                _spiral.enabled = false;
                _rim.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (Showing)
            {
                return;
            }
            float t = (Time.time - _arriveStarted) / ArriveSeconds;
            if (t >= 0f && t <= 1f)
            {
                DrawVortex(_arriveAt, Mathf.Lerp(1.6f, 0.1f, t), 1f - t);
            }
            else if (_spiral.enabled)
            {
                _spiral.enabled = false;
                _rim.enabled = false;
            }
        }

        // 回る渦巻きと、縁の輪（radius が 0 なら出さない）。
        private void DrawVortex(Vector3 center, float radius, float alpha)
        {
            bool show = radius > 0.01f && alpha > 0f;
            _spiral.enabled = show;
            _rim.enabled = show;
            if (!show)
            {
                return;
            }
            Color color = _vortexColor;
            color.a *= Mathf.Clamp01(alpha);
            _spiral.startColor = _spiral.endColor = color;
            _rim.startColor = _rim.endColor = color;
            float spin = Time.time * 540f * Mathf.Deg2Rad;
            for (int i = 0; i < SpiralPoints; i++)
            {
                float u = i / (float)(SpiralPoints - 1);
                float angle = spin + u * Mathf.PI * 6f;
                float r = radius * u;
                _spiral.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, -1.2f));
            }
            for (int i = 0; i < _rim.positionCount; i++)
            {
                float angle = -spin * 0.5f + i / (float)_rim.positionCount * Mathf.PI * 2f;
                _rim.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -1.2f));
            }
        }
    }
}
