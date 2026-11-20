using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// プラズマ球の光の筋（研究所の跳ね返す球。2026-11-20 の方針: ギミックを実際の現象に寄せる。表示だけ）。手本はプラズマボール:
    /// 真ん中の電極からガラスの内側まで、気体が光る細い筋が何本も伸びる。筋は熱で温められた気体と一緒にゆらゆら上へ昇り、上へ着くと
    /// 消えて下の方からまた生まれる。色は封入した気体で決まる（ネオンの赤紫〜橙と、アルゴンなどの青紫）。筋は細かくちらつく。
    /// </summary>
    public sealed class PlasmaGlobeView : MonoBehaviour
    {
        [Tooltip("光る線の材質（Airerage/GlowLine）")]
        [SerializeField] private Material _lineMaterial;
        [Tooltip("ガラスの内側の半径（m）")]
        [SerializeField] private float _radius = 0.8f;
        [SerializeField] private int _filaments = 7;
        [SerializeField] private Color _coreColor = new Color(1f, 0.45f, 0.9f);
        [SerializeField] private Color _tipColor = new Color(0.55f, 0.4f, 1f);

        private const int Points = 12;
        private LineRenderer[] _lines;
        private Vector3[] _directions;
        private readonly Vector3[] _path = new Vector3[Points];
        private float _nextFlicker;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Material lineMaterial, float radius, int filaments)
        {
            _lineMaterial = lineMaterial;
            _radius = radius;
            _filaments = filaments;
        }

        private void Awake()
        {
            _lines = new LineRenderer[_filaments];
            _directions = new Vector3[_filaments];
            for (int i = 0; i < _filaments; i++)
            {
                var go = new GameObject($"Filament{i + 1}");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = _lineMaterial;
                line.useWorldSpace = true;
                line.positionCount = Points;
                line.numCapVertices = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.startWidth = 0.07f;
                line.endWidth = 0.025f;
                _lines[i] = line;
                _directions[i] = Respawn();
            }
        }

        // 新しい筋は球の下半分のどこかから。
        private static Vector3 Respawn()
        {
            Vector3 d = Random.onUnitSphere;
            d.y = -Mathf.Abs(d.y) * 0.8f;
            return d.normalized;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool flicker = Time.time >= _nextFlicker;
            if (flicker)
            {
                _nextFlicker = Time.time + 0.045f;
            }
            Vector3 center = transform.position;
            for (int i = 0; i < _lines.Length; i++)
            {
                // ゆらゆら漂いながら上へ（熱で軽くなった気体と一緒に）。てっぺんに着いたら下から生まれ直す。
                Vector3 d = _directions[i] + Random.insideUnitSphere * (1.6f * dt) + Vector3.up * (0.35f * dt);
                d = d.normalized;
                if (d.y > 0.88f)
                {
                    d = Respawn();
                }
                _directions[i] = d;
                if (!flicker)
                {
                    continue;
                }
                // 電極からガラスまで、細かく曲がる筋（毎回少しずつ違う形でちらつく）。
                Vector3 side = Vector3.Cross(d, Vector3.forward).sqrMagnitude > 1e-4f ? Vector3.Cross(d, Vector3.forward).normalized : Vector3.right;
                Vector3 other = Vector3.Cross(d, side);
                for (int k = 0; k < Points; k++)
                {
                    float t = k / (float)(Points - 1);
                    float bend = Mathf.Sin(t * Mathf.PI) * _radius * 0.12f;
                    _path[k] = center + d * (_radius * (0.16f + 0.84f * t)) + side * (Random.Range(-1f, 1f) * bend) + other * (Random.Range(-1f, 1f) * bend);
                }
                LineRenderer line = _lines[i];
                line.SetPositions(_path);
                float brightness = Random.Range(0.75f, 1f);
                Color core = _coreColor * brightness;
                Color tip = _tipColor * brightness;
                core.a = 1f;
                tip.a = 0.85f;
                line.startColor = core;
                line.endColor = tip;
            }
        }
    }
}
