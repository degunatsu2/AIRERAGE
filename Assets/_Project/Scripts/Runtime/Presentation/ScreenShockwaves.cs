using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 画面の衝撃波（FATAL の空気の塊。2026-11-24 の要望: F6 の歪む輪は透明にして、背景を歪ませる）。表示だけ（シミュレーションには
    /// 影響しない）。Play でワールドの位置から広がる波を足し（同時に 4 つまで。あふれたらいちばん古い波と入れ替える）、毎フレーム、
    /// カメラで画面の位置と大きさに直して、全画面の処理のシェーダー（Airerage/Shockwave。PC_Renderer の全画面の処理）の値に入れる。
    /// 波の広がり: 半径は StartRadius → EndRadius（m）へ素早く広がって遅くなり（4 乗のイーズアウト）、押す強さと色収差は弱まっていく。
    /// 2026-11-25 の要望（波が見えにくい。もっと大きく揺らす・色収差を強く）で、押す強さ 2.5 倍・輪の幅 1.5 倍・長さ 1.2 倍、色収差
    /// 2.7 倍、縁の明暗（CrestLight）と、当たった瞬間の画面の小さな揺れ（Jitter・JitterSeconds。_ShockJitter）を足した。【仮】
    /// </summary>
    public static class ScreenShockwaves
    {
        /// <summary>同時に出せる波の数（シェーダーの MAX_WAVES と同じ）。</summary>
        public const int MaxWaves = 4;

        /// <summary>波の長さ（秒）・半径（m）・輪の幅（m）・押す強さと色収差（画面の高さに対する割合）。</summary>
        public const float Duration = 0.9f;
        public const float StartRadius = 0.4f;
        public const float EndRadius = 14f;
        public const float RingWidth = 2.4f;
        public const float Push = 0.055f;
        public const float Chroma = 0.016f;

        /// <summary>輪の縁の明暗の強さ（明るさの割合）。</summary>
        public const float CrestLight = 0.22f;

        /// <summary>当たった瞬間の画面の揺れの大きさ（画面の高さに対する割合）と長さ（秒）。</summary>
        public const float Jitter = 0.006f;
        public const float JitterSeconds = 0.28f;

        private static readonly int WavesId = Shader.PropertyToID("_ShockWaves");
        private static readonly int ShapeId = Shader.PropertyToID("_ShockShape");
        private static readonly int CountId = Shader.PropertyToID("_ShockWaveCount");
        private static readonly int JitterId = Shader.PropertyToID("_ShockJitter");

        private struct Wave
        {
            public Vector3 Center;
            public float StartedAt;
            public float Scale;
        }

        private static readonly Wave[] _waves = new Wave[MaxWaves];
        private static int _count;
        private static readonly Vector4[] _wavesBuffer = new Vector4[MaxWaves];
        private static readonly Vector4[] _shapeBuffer = new Vector4[MaxWaves];
        private static ScreenShockwaveDriver _driver;

        /// <summary>今広がっている波の数（テスト用）。</summary>
        public static int ActiveCount
        {
            get
            {
                Prune(Time.unscaledTime);
                return _count;
            }
        }

        /// <summary>シェーダーに入れた波の数と、画面の揺れの大きさ（テスト用。最後に毎フレームの更新をしたとき）。</summary>
        public static int SubmittedCount { get; private set; }

        public static float SubmittedJitter { get; private set; }

        /// <summary>center から波を広げる（scale は大きさと強さの倍率）。</summary>
        public static void Play(Vector3 center, float scale = 1f)
        {
            EnsureDriver();
            float now = Time.unscaledTime;
            Prune(now);
            var wave = new Wave { Center = center, StartedAt = now, Scale = Mathf.Max(0.1f, scale) };
            if (_count < MaxWaves)
            {
                _waves[_count++] = wave;
                return;
            }
            int oldest = 0;
            for (int i = 1; i < _count; i++)
            {
                if (_waves[i].StartedAt < _waves[oldest].StartedAt)
                {
                    oldest = i;
                }
            }
            _waves[oldest] = wave;
        }

        // 終わった波を除く。
        private static void Prune(float now)
        {
            int kept = 0;
            for (int i = 0; i < _count; i++)
            {
                if (now - _waves[i].StartedAt < Duration)
                {
                    _waves[kept++] = _waves[i];
                }
            }
            _count = kept;
        }

        /// <summary>今のフレームの波を、カメラ camera の画面の値にしてシェーダーへ入れる（なければ 0 個）。</summary>
        public static void Submit(Camera camera)
        {
            float now = Time.unscaledTime;
            Prune(now);
            int shown = 0;
            Vector2 jitter = Vector2.zero;
            if (camera != null && camera.pixelHeight > 0)
            {
                for (int i = 0; i < _count; i++)
                {
                    Wave wave = _waves[i];
                    float age = now - wave.StartedAt;
                    float k = Mathf.Clamp01(age / Duration);
                    if (age < JitterSeconds)
                    {
                        // 揺れ: 速く向きを変えながら小さくなる（波ごとに位相をずらす）。
                        float shake = Jitter * wave.Scale * (1f - age / JitterSeconds) * (1f - age / JitterSeconds);
                        jitter += new Vector2(Mathf.Sin(age * 97f + i * 1.3f), Mathf.Cos(age * 83f + i * 2.1f)) * shake;
                    }
                    float ease = 1f - Mathf.Pow(1f - k, 4f);
                    float radius = Mathf.Lerp(StartRadius, EndRadius, ease) * wave.Scale;
                    Vector3 screen = camera.WorldToScreenPoint(wave.Center);
                    if (screen.z <= 0f)
                    {
                        continue;
                    }
                    // 半径と幅を画面の高さの割合に（中心の横に半径だけ離した点を写した距離）。
                    Vector3 side = camera.WorldToScreenPoint(wave.Center + camera.transform.right * radius);
                    Vector3 widthEdge = camera.WorldToScreenPoint(wave.Center + camera.transform.right * RingWidth * wave.Scale);
                    float height = camera.pixelHeight;
                    float screenRadius = Vector2.Distance(screen, side) / height;
                    float screenWidth = Mathf.Max(0.004f, Vector2.Distance(screen, widthEdge) / height);
                    float fade = Mathf.Pow(1f - k, 1.6f);
                    _wavesBuffer[shown] = new Vector4(screen.x / camera.pixelWidth, screen.y / height, screenRadius, Push * wave.Scale * fade);
                    _shapeBuffer[shown] = new Vector4(screenWidth, Chroma * wave.Scale * Mathf.Clamp01(1f - k * 1.1f), CrestLight * fade, 0f);
                    shown++;
                }
            }
            for (int i = shown; i < MaxWaves; i++)
            {
                _wavesBuffer[i] = Vector4.zero;
                _shapeBuffer[i] = Vector4.zero;
            }
            Shader.SetGlobalVectorArray(WavesId, _wavesBuffer);
            Shader.SetGlobalVectorArray(ShapeId, _shapeBuffer);
            Shader.SetGlobalFloat(CountId, shown);
            Shader.SetGlobalVector(JitterId, jitter);
            SubmittedCount = shown;
            SubmittedJitter = jitter.magnitude;
        }

        // 毎フレームの更新をする隠れた物（最初の波を出したときに作る。シーンをまたいで残る）。
        private static void EnsureDriver()
        {
            if (_driver != null)
            {
                return;
            }
            var go = new GameObject("ScreenShockwaves") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying)
            {
                Object.DontDestroyOnLoad(go);
            }
            _driver = go.AddComponent<ScreenShockwaveDriver>();
        }

        /// <summary>波を全部消して、画面を元に戻す（ScreenShockwaveDriver が消えるとき）。</summary>
        internal static void Clear()
        {
            _count = 0;
            Shader.SetGlobalFloat(CountId, 0f);
            Shader.SetGlobalVector(JitterId, Vector4.zero);
            SubmittedCount = 0;
            SubmittedJitter = 0f;
        }
    }
}
