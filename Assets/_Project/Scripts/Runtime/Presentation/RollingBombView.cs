using Airerage.Core.Common;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 転がる噴石（火山。2026-11-20 の要望: 奥の山から手前へ転がってくる。表示だけ）。手本は、噴火で弾道を描いて飛ぶ火山弾（USGS・UNDRR）と、
    /// 斜面を落ちる岩（落石の研究: 自由落下・跳ね・転がり・滑りを組み合わせて動き、斜面に当たるたびに跳ね返る）。
    /// Core の危険（StageZone の柱。周期は 15〜25 秒のばらつき）の予告が始まると、柱の横の位置 X の奥の火口の壁の尾根の上に警告の記号が
    /// 出て、尾根の向こうで土煙が上がる。① 噴石が尾根の向こうから弾道を描いて飛び出し、② 尾根の内側の斜面に落ちて跳ね、転がりながら
    /// 下り、③ 岩に当たって大きく跳ね、遊ぶ面（z = 0）を、判定の範囲（柱の中心の高さ）で、効いている間のちょうど真ん中に横切る。
    /// その後は手前へ落ちて画面の外へ出る。尾根の高さは、シーンのビルダーが火口の壁の高さの網から焼いた格子（_heights）。
    /// 時間は Core のティック（予告の残り・効き始めてから）から決め、ティックの間はなめらかにつなぐ。
    /// </summary>
    public sealed class RollingBombView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("噴石の岩（中心が原点）")]
        [SerializeField] private Transform _bomb;
        [Tooltip("噴石の半径（m）")]
        [SerializeField] private float _radius = 2f;
        [Tooltip("飛ぶ間の煙と火の粉（ワールドの空間。噴石と一緒に動かし、出す量を変える）")]
        [SerializeField] private ParticleSystem[] _trail = new ParticleSystem[0];
        [Tooltip("斜面に当たったときの土煙と火の粉（当たった所へ動かして出す）")]
        [SerializeField] private ParticleSystem[] _impact = new ParticleSystem[0];
        [Tooltip("予告の間、尾根の向こうで上がる土煙")]
        [SerializeField] private ParticleSystem _rumble;
        [Tooltip("警告の記号の材質（Sprites/Default）")]
        [SerializeField] private Material _warningMaterial;
        [Tooltip("奥の地面の高さの格子（x が速く変わる順）と、その範囲")]
        [SerializeField] private float[] _heights = new float[0];
        [SerializeField] private int _columns;
        [SerializeField] private int _rows;
        [SerializeField] private float _minX;
        [SerializeField] private float _minZ;
        [SerializeField] private float _step = 1f;

        // 道のりの時間割（秒。横切る瞬間〈効いている間の真ん中〉が 0）。飛び出し → 斜面に落ちる → 転がる → 跳ねて横切る → 画面の外へ。
        private const float EjectAt = -5f;
        private const float FlightSeconds = 2.4f;
        private const float RollSeconds = 0.8f;
        private const float HideAfter = 1.5f;
        // 跳ねてから横切るまでの重力（m/s²）と、飛び出すときに尾根を越える高さ（m）。
        private const float HopGravity = 16f;
        private const float ApexClearance = 6f;
        // 警告の記号の大きさの倍率（遠い尾根の上でも見えるように）と、記号の尾根からの高さ（m）。
        private const float SignScale = 2.4f;
        private const float SignHeight = 6f;

        private struct Path
        {
            public float X;
            public float CrestZ;
            public float CrestY;
            public Vector3 Start;
            public Vector3 Land;
            public Vector3 Kick;
            public float CrossY;
            public float FlightUp;
            public float FlightGravity;
            public float FlightVy;
            public float HopVy;
            public float HopVz;
        }

        private Path _path;
        private bool _running;
        private float _t;
        private float _previousT;
        private float _spin;
        private Vector3 _previousPosition;
        private WarningOverlay _sign;
        private readonly Vector2[] _signAt = new Vector2[1];
        private float _nextRumble;

        /// <summary>噴石が見えているか（テスト用）。</summary>
        public bool Flying => _bomb != null && _bomb.gameObject.activeSelf;

        /// <summary>尾根の上に警告の記号を出しているか（テスト用）。</summary>
        public bool SignShown => _sign != null && _sign.IconCount > 0;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, Transform bomb, float radius, ParticleSystem[] trail, ParticleSystem[] impact, ParticleSystem rumble,
            Material warningMaterial)
        {
            _view = view;
            _bomb = bomb;
            _radius = radius;
            _trail = trail;
            _impact = impact;
            _rumble = rumble;
            _warningMaterial = warningMaterial;
        }

        /// <summary>奥の地面の高さの格子を決める（シーンのビルダー用。heights は x が速く変わる順で columns × rows）。</summary>
        public void ConfigureTerrain(float[] heights, int columns, int rows, float minX, float minZ, float step)
        {
            _heights = heights;
            _columns = columns;
            _rows = rows;
            _minX = minX;
            _minZ = minZ;
            _step = step;
        }

        /// <summary>奥の地面の高さ（格子を線形に補う。格子の外は端の値）。</summary>
        public float HeightAt(float x, float z)
        {
            if (_heights == null || _heights.Length == 0 || _columns < 2 || _rows < 2)
            {
                return -20f;
            }
            float u = Mathf.Clamp((x - _minX) / _step, 0f, _columns - 1.001f);
            float v = Mathf.Clamp((z - _minZ) / _step, 0f, _rows - 1.001f);
            int i = (int)u;
            int j = (int)v;
            float fu = u - i;
            float fv = v - j;
            float a = _heights[j * _columns + i];
            float b = _heights[j * _columns + i + 1];
            float c = _heights[(j + 1) * _columns + i];
            float d = _heights[(j + 1) * _columns + i + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fu), Mathf.Lerp(c, d, fu), fv);
        }

        private void Awake()
        {
            if (_warningMaterial != null)
            {
                var sign = new GameObject("RidgeWarning");
                sign.transform.SetParent(transform, false);
                sign.transform.localScale = new Vector3(SignScale, SignScale, 1f);
                _sign = sign.AddComponent<WarningOverlay>();
                _sign.Initialize(_warningMaterial, 40f);
            }
            Hide();
            // 出し続ける粒は始まりから出そろえてある（prewarm）ので、飛ぶまでは消しておく。
            foreach (ParticleSystem trail in _trail)
            {
                if (trail != null)
                {
                    trail.Clear(true);
                }
            }
        }

        private void LateUpdate()
        {
            if (_view == null || _bomb == null)
            {
                return;
            }
            float tickSeconds = 1f / SimTime.TicksPerSecond;
            int active = _view.CoreZone.ActiveTicks;
            bool known = false;
            float derived = 0f;
            if (_view.ShowsActive && _view.ActiveElapsedTicks >= 0)
            {
                derived = (_view.ActiveElapsedTicks - active * 0.5f) * tickSeconds;
                known = true;
            }
            else if (_view.ShowsWarning && _view.TicksUntilActive > 0)
            {
                derived = -(_view.TicksUntilActive + active * 0.5f) * tickSeconds;
                known = true;
            }
            if (known)
            {
                Vector2 column = _view.ShownColumn;
                float x = (column.x + column.y) * 0.5f;
                if (!_running || Mathf.Abs(x - _path.X) > 0.01f || derived < _t - 0.5f)
                {
                    Begin(x);
                    _t = derived;
                }
                // ティックの間はなめらかに（シミュレーションが止まれば止まる）。
                _t = Mathf.Abs(_t - derived) > 0.25f ? derived : Mathf.Clamp(_t + Time.deltaTime, derived, derived + tickSeconds);
            }
            else if (_running)
            {
                _t += Time.deltaTime;
            }
            if (!_running)
            {
                return;
            }
            if (_t > HideAfter)
            {
                Hide();
                return;
            }
            Render();
            _previousT = _t;
        }

        // 予告が始まった: 柱の横の位置 x の道のりを決める。
        private void Begin(float x)
        {
            _running = true;
            _path = Plan(x);
            _previousT = float.NegativeInfinity;
            _spin = 0f;
        }

        private Path Plan(float x)
        {
            var path = new Path { X = x };
            // 尾根（奥の 28〜66m でいちばん高い所）。
            path.CrestZ = 40f;
            path.CrestY = float.MinValue;
            for (float z = 28f; z <= 66f; z += 0.5f)
            {
                float h = HeightAt(x, z);
                if (h > path.CrestY)
                {
                    path.CrestY = h;
                    path.CrestZ = z;
                }
            }
            float landZ = path.CrestZ - 5f;
            float kickZ = Mathf.Max(20f, landZ - 8f);
            path.Start = new Vector3(x, HeightAt(x, path.CrestZ + 8f) - _radius - 1f, path.CrestZ + 8f);
            path.Land = new Vector3(x, HeightAt(x, landZ) + _radius, landZ);
            path.Kick = new Vector3(x, HeightAt(x, kickZ) + _radius, kickZ);
            path.CrossY = _view.CoreZone.Center.Y;
            // 飛び出し: 尾根を ApexClearance だけ越える山なりで、FlightSeconds で斜面に落ちる（上りと下りの時間から重力を決める）。
            float apex = Mathf.Max(path.CrestY + _radius + ApexClearance, Mathf.Max(path.Start.y, path.Land.y) + 2f);
            float up = Mathf.Sqrt(apex - path.Start.y);
            float down = Mathf.Sqrt(apex - path.Land.y);
            path.FlightUp = FlightSeconds * up / (up + down);
            path.FlightGravity = 2f * (apex - path.Start.y) / (path.FlightUp * path.FlightUp);
            path.FlightVy = path.FlightGravity * path.FlightUp;
            // 跳ねて横切る: 岩から、横切る瞬間（0 秒）にちょうど判定の高さを通る放物線。
            float hop = -(EjectAt + FlightSeconds + RollSeconds);
            path.HopVz = -path.Kick.z / hop;
            path.HopVy = (path.CrossY - path.Kick.y + 0.5f * HopGravity * hop * hop) / hop;
            return path;
        }

        /// <summary>時刻 t（秒。横切る瞬間が 0）の噴石の中心（テスト用にも使う）。</summary>
        public Vector3 PositionAt(float t)
        {
            float landAt = EjectAt + FlightSeconds;
            float kickAt = landAt + RollSeconds;
            if (t <= landAt)
            {
                float s = Mathf.Max(0f, t - EjectAt);
                float y = _path.Start.y + _path.FlightVy * s - 0.5f * _path.FlightGravity * s * s;
                float z = Mathf.Lerp(_path.Start.z, _path.Land.z, s / FlightSeconds);
                return new Vector3(_path.X, y, z);
            }
            if (t <= kickAt)
            {
                // 斜面を転がる（だんだん速く。落ちたときの約 6 m/s から、跳ねるときの約 14 m/s へ）。凸凹に沿い、小さく跳ねる。
                float s = (t - landAt) / RollSeconds;
                float z = Mathf.Lerp(_path.Land.z, _path.Kick.z, s * (0.6f + 0.4f * s));
                float bump = Mathf.Abs(Mathf.Sin(s * Mathf.PI * 2f)) * 0.5f;
                return new Vector3(_path.X, HeightAt(_path.X, z) + _radius + bump, z);
            }
            float h = t - kickAt;
            return new Vector3(_path.X, _path.Kick.y + _path.HopVy * h - 0.5f * HopGravity * h * h, _path.Kick.z + _path.HopVz * h);
        }

        private void Render()
        {
            float landAt = EjectAt + FlightSeconds;
            float kickAt = landAt + RollSeconds;
            // 警告の記号（尾根の上。飛び出して斜面に落ちるまで、明滅しながら）。
            if (_sign != null)
            {
                if (_t < landAt)
                {
                    _signAt[0] = new Vector2(_path.X, _path.CrestY + SignHeight);
                    _sign.SetDepth(_path.CrestZ - 2f);
                    float blink = 0.55f + 0.45f * Mathf.Sin(Time.time * 9f);
                    _sign.Show(System.Array.Empty<Rect>(), _signAt, blink);
                }
                else if (_sign.IconCount > 0)
                {
                    _sign.Hide();
                }
            }
            // 飛び出す前: 尾根の向こうで土煙が上がる。
            if (_t < EjectAt)
            {
                SetBomb(false);
                if (_rumble != null && Time.time >= _nextRumble)
                {
                    _nextRumble = Time.time + 0.25f;
                    _rumble.transform.position = new Vector3(_path.X + Random.Range(-1.5f, 1.5f), _path.CrestY - 1f, _path.CrestZ + 4f);
                    _rumble.Emit(2);
                }
                return;
            }
            SetBomb(true);
            Vector3 position = PositionAt(_t);
            if (_previousT > float.NegativeInfinity)
            {
                // 転がる・回る（動いた距離を半径で割った角度。飛ぶ間は少しずつ回る）。
                float moved = Vector3.Distance(position, _previousPosition);
                _spin += moved / _radius * Mathf.Rad2Deg;
                if (Crossed(_previousT, _t, EjectAt))
                {
                    Burst(new Vector3(_path.X, _path.CrestY, _path.CrestZ + 3f), 1.4f);
                }
                if (Crossed(_previousT, _t, landAt))
                {
                    Burst(position - Vector3.up * _radius, 1f);
                }
                if (Crossed(_previousT, _t, kickAt))
                {
                    Burst(position - Vector3.up * _radius, 1.2f);
                }
            }
            _bomb.position = position;
            _bomb.rotation = Quaternion.Euler(-_spin, _spin * 0.15f, 0f);
            _previousPosition = position;
            foreach (ParticleSystem trail in _trail)
            {
                if (trail != null)
                {
                    trail.transform.position = position;
                }
            }
        }

        private static bool Crossed(float before, float after, float at)
        {
            return before < at && after >= at;
        }

        private void Burst(Vector3 at, float scale)
        {
            foreach (ParticleSystem system in _impact)
            {
                if (system == null)
                {
                    continue;
                }
                system.transform.position = at;
                system.Emit(Mathf.RoundToInt(18f * scale));
            }
        }

        private void SetBomb(bool shown)
        {
            if (_bomb.gameObject.activeSelf != shown)
            {
                _bomb.gameObject.SetActive(shown);
            }
            foreach (ParticleSystem trail in _trail)
            {
                if (trail == null)
                {
                    continue;
                }
                ParticleSystem.EmissionModule emission = trail.emission;
                if (emission.enabled != shown)
                {
                    emission.enabled = shown;
                }
            }
        }

        private void Hide()
        {
            _running = false;
            if (_bomb != null)
            {
                SetBomb(false);
            }
            if (_sign != null)
            {
                _sign.Hide();
            }
        }
    }
}
