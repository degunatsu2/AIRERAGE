using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// プラズマ球の光の筋（研究所の跳ね返す球。2026-11-20 の方針: ギミックを実際の現象に寄せる。表示だけ）。手本はプラズマボール:
    /// 真ん中の電極からガラスの内側まで、気体が光る細い筋が何本も伸びる。筋は熱で温められた気体と一緒にゆらゆら上へ昇り、上へ着くと
    /// 消えて下の方からまた生まれる。色は封入した気体で決まる（ネオンの赤紫〜橙と、アルゴンなどの青紫）。筋は細かくちらつく。
    /// ガラスに指を触れると、指が電気の通りやすい道になり、筋がその一点へ集まって太く明るくなる（Wikipedia「Plasma globe」）。ここでは
    /// 球につないだワイヤー（電気を帯びたワイヤー。WireView.ShowsCharged）の出ていく向きへ、筋を 1 本ずつ集める（2026-11-20）。
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
        private WireView[] _wires = new WireView[0];
        private float _nextWireSearch;
        private readonly Vector3[] _focus = new Vector3[4];

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
            int focused = FindFocus(center);
            for (int i = 0; i < _lines.Length; i++)
            {
                if (i < focused)
                {
                    // つないだワイヤーへ集まる筋（まっすぐに近く、太く明るい）。
                    if (flicker)
                    {
                        Focus(_lines[i], center, _focus[i]);
                    }
                    continue;
                }
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
                line.startWidth = 0.07f;
                line.endWidth = 0.025f;
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

        // 電気を帯びたワイヤーが球の真ん中につながっていれば、ロープが出ていく向きを focus に入れる（数を返す）。
        private int FindFocus(Vector3 center)
        {
            if (Time.time >= _nextWireSearch)
            {
                _nextWireSearch = Time.time + 1f;
                _wires = FindObjectsByType<WireView>(FindObjectsSortMode.None);
            }
            int count = 0;
            foreach (WireView wire in _wires)
            {
                if (wire == null || !wire.ShowsCharged || count >= _focus.Length || count >= _lines.Length
                    || (wire.ChargedAnchor - center).sqrMagnitude > _radius * _radius * 2.25f)
                {
                    continue;
                }
                Vector3 toward = wire.ChargedToward - center;
                _focus[count++] = toward.sqrMagnitude > 1e-6f ? toward.normalized : Vector3.up;
            }
            return count;
        }

        private void Focus(LineRenderer line, Vector3 center, Vector3 direction)
        {
            Vector3 side = Vector3.Cross(direction, Vector3.forward).sqrMagnitude > 1e-4f ? Vector3.Cross(direction, Vector3.forward).normalized : Vector3.right;
            for (int k = 0; k < Points; k++)
            {
                float t = k / (float)(Points - 1);
                float bend = Mathf.Sin(t * Mathf.PI) * _radius * 0.05f;
                _path[k] = center + direction * (_radius * (0.16f + 0.84f * t)) + side * (Random.Range(-1f, 1f) * bend);
            }
            line.SetPositions(_path);
            line.startWidth = 0.11f;
            line.endWidth = 0.08f;
            Color core = _coreColor * 1.3f;
            Color tip = Color.Lerp(_tipColor, Color.white, 0.4f) * 1.3f;
            core.a = 1f;
            tip.a = 1f;
            line.startColor = core;
            line.endColor = tip;
        }
    }
}
