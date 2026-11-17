using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ステージの足場の見た目（D-97。2026-11-17 のユーザーの決定）: 地形の箱（当たりの BoxCollider）と同じ大きさの、面取りした箱の網
    /// （StageBlockMesh。V1）を、出したときに作る（網はシーンに保存しない）。小さな浮いた足場は、見た目だけをゆっくり数 cm 上下させる
    /// （V21 足場の呼吸。当たりの箱は動かさないので、遊びとはずれない）。当たりはこの物ではなく、地形の箱が持つ。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class StageBlock : MonoBehaviour
    {
        [SerializeField] private Vector3 _size = Vector3.one;
        [Tooltip("面取りの幅（m）")]
        [SerializeField] private float _bevel = 0.15f;
        [Tooltip("光る線の明滅と呼吸の位相（ラジアン）")]
        [SerializeField] private float _phase;
        [Tooltip("ワイヤーが刺さる足場（光る線を付けない。オレンジの縁がある）")]
        [SerializeField] private bool _attachable;
        [Tooltip("呼吸の上下の幅（m。0 なら動かさない）")]
        [SerializeField] private float _breathAmplitude;
        [Tooltip("呼吸の 1 周の秒数")]
        [SerializeField] private float _breathSeconds = 4f;

        private Mesh _mesh;
        private Vector3 _rest;
        private bool _hasRest;

        /// <summary>箱の大きさ（m）。</summary>
        public Vector3 Size => _size;

        /// <summary>呼吸の上下の幅（m）。</summary>
        public float BreathAmplitude => _breathAmplitude;

        /// <summary>ワイヤーが刺さる足場か。</summary>
        public bool Attachable => _attachable;

        /// <summary>今の網（テスト用）。</summary>
        public Mesh Mesh => _mesh;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Vector3 size, float bevel, float phase, bool attachable, float breathAmplitude, float breathSeconds)
        {
            _size = size;
            _bevel = bevel;
            _phase = phase;
            _attachable = attachable;
            _breathAmplitude = breathAmplitude;
            _breathSeconds = Mathf.Max(0.5f, breathSeconds);
            Rebuild();
        }

        private void OnEnable()
        {
            Rebuild();
            _rest = transform.localPosition;
            _hasRest = true;
        }

        private void OnDisable()
        {
            if (_hasRest && Application.isPlaying)
            {
                transform.localPosition = _rest;
            }
            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }
                _mesh = null;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || _breathAmplitude <= 0f || !_hasRest)
            {
                return;
            }
            float angle = Time.time / _breathSeconds * Mathf.PI * 2f + _phase;
            transform.localPosition = _rest + new Vector3(0f, Mathf.Sin(angle) * _breathAmplitude, 0f);
        }

        private void Rebuild()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "StageBlock", hideFlags = HideFlags.DontSave };
            }
            StageBlockMesh.Fill(_mesh, _size, _bevel, _phase, _attachable);
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }
    }
}
