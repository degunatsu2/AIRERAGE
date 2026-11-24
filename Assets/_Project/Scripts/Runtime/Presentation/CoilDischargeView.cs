using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// コイルガンのためと発射の演出（2026-11-24 の要望: いつ撃つか分かるようにチャージの光を派手に、チャージ中に放電、発射の火花を派手に。
    /// 表示だけ）。コイルガンの状態（CoilAcceleratorView のため具合・砲身の向き・撃った回数）を見て出す。手本はゲーム・アニメの重火器と
    /// 電気の能力の演出（ためは「粒が吸い込まれ、砲口の光の玉が育ち、満ちると点滅して知らせる」、発射は「砲口の閃光 → 衝撃の輪 → 弾の
    /// 光の筋 → 火花」の重ね。電気は折れ曲がった光の筋が明滅しながら飛ぶ）。
    /// - ため: コイルとコイル・コイルとレール・砲口のまわりに電気の弧（中点をずらした折れ線。0.05 秒ごとに作り直して明滅）が走り、本数は
    ///   ため具合で増える。砲口に光の玉が育ち、まわりから光の粒が吸い込まれる。満ちる直前（ReadyCharge から）は、光の輪が砲口へ縮み、
    ///   玉が速く脈打つ（もうすぐ撃つ合図）。
    /// - 発射: 砲口に 8 本の光の星（閃光）、砲身と直角の向きにつぶれた衝撃の輪、撃ち出す向きの光の筋、外へはじける電気の弧。
    /// </summary>
    public sealed class CoilDischargeView : MonoBehaviour
    {
        [Tooltip("コイルガン（ため具合・砲身の向き・撃った回数を読む）")]
        [SerializeField] private CoilAcceleratorView _gun;
        [Tooltip("コイル（後ろから砲口への順。弧の端）と、コイルの外側の半径・レールの高さ（砲身の中心から。m）")]
        [SerializeField] private Transform[] _coils = new Transform[0];
        [SerializeField] private float _coilRadius = 1.7f;
        [SerializeField] private float _railOffset = 1.75f;
        [Tooltip("電気の弧（Airerage/GlowLine の線）")]
        [SerializeField] private LineRenderer[] _arcs = new LineRenderer[0];
        [Tooltip("砲口の光の玉と閃光（Airerage/GlowStar の板）")]
        [SerializeField] private MeshRenderer _orb;
        [SerializeField] private MeshRenderer _flash;
        [Tooltip("衝撃の輪と、撃ち出す向きの光の筋（Airerage/GlowLine の線）")]
        [SerializeField] private LineRenderer _shockRing;
        [SerializeField] private LineRenderer _streak;
        [Tooltip("砲口へ吸い込まれる光の粒")]
        [SerializeField] private ParticleSystem _gather;
        [Tooltip("砲口の位置（砲身の向きに沿った、中心からの距離。m）と光の色")]
        [SerializeField] private float _muzzleDistance = 1.9f;
        [SerializeField] private Color _color = new Color(0.35f, 0.85f, 1f);

        /// <summary>弧の折れ線の点の数と、作り直す間隔（秒）。</summary>
        public const int ArcPoints = 12;
        public const float ArcRefreshSeconds = 0.05f;

        /// <summary>もうすぐ撃つ合図を出し始めるため具合。</summary>
        public const float ReadyCharge = 0.82f;

        /// <summary>発射の閃光・衝撃の輪・光の筋の長さ（秒）。</summary>
        public const float FlashSeconds = 0.22f;
        public const float ShockSeconds = 0.4f;
        public const float StreakSeconds = 0.32f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;
        private float _nextArcs;
        private int _seenShots;
        private float _firedAt = -10f;
        private Vector3 _firedMuzzle;
        private Vector2 _firedAim = Vector2.right;
        private Gradient _gradient;

        /// <summary>今光っている電気の弧の数（テスト用）。</summary>
        public int ActiveArcs { get; private set; }

        /// <summary>砲口の光の玉の大きさ（m。テスト用）と、閃光を出した回数（テスト用）。</summary>
        public float OrbSize { get; private set; }

        public int Flashes { get; private set; }

        /// <summary>もうすぐ撃つ合図を出しているか（テスト用）。</summary>
        public bool Ready { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(CoilAcceleratorView gun, Transform[] coils, float coilRadius, float railOffset, LineRenderer[] arcs, MeshRenderer orb,
            MeshRenderer flash, LineRenderer shockRing, LineRenderer streak, ParticleSystem gather, float muzzleDistance, Color color)
        {
            _gun = gun;
            _coils = coils;
            _coilRadius = coilRadius;
            _railOffset = railOffset;
            _arcs = arcs;
            _orb = orb;
            _flash = flash;
            _shockRing = shockRing;
            _streak = streak;
            _gather = gather;
            _muzzleDistance = muzzleDistance;
            _color = color;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            foreach (LineRenderer arc in _arcs)
            {
                if (arc != null)
                {
                    arc.positionCount = ArcPoints;
                    arc.enabled = false;
                }
            }
            SetVisible(_orb, false);
            SetVisible(_flash, false);
            if (_shockRing != null)
            {
                _shockRing.enabled = false;
            }
            if (_streak != null)
            {
                _streak.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_gun == null)
            {
                return;
            }
            float charge = _gun.Charge;
            Vector2 aim = new Vector2(Mathf.Cos(_gun.Angle * Mathf.Deg2Rad), Mathf.Sin(_gun.Angle * Mathf.Deg2Rad));
            Vector3 muzzle = _gun.transform.position + (Vector3)(aim * _muzzleDistance) + new Vector3(0f, 0f, -0.25f);
            if (_gun.Shots > _seenShots)
            {
                _seenShots = _gun.Shots;
                Fire(muzzle, aim);
            }
            float sinceFire = Time.time - _firedAt;
            RenderArcs(charge, muzzle, aim, sinceFire);
            RenderOrb(charge, muzzle);
            RenderGather(charge, muzzle);
            RenderShock(charge, muzzle, aim, sinceFire);
            RenderFlash(sinceFire);
            RenderStreak(sinceFire);
        }

        private void Fire(Vector3 muzzle, Vector2 aim)
        {
            _firedAt = Time.time;
            _firedMuzzle = muzzle;
            _firedAim = aim;
            Flashes++;
            _nextArcs = 0f;
        }

        // 電気の弧: ためている間は、ため具合に合わせて本数を増やし、0.05 秒ごとに作り直して明滅させる。撃った直後の 0.15 秒は、全部の弧が
        // 砲口から外へはじける。
        private void RenderArcs(float charge, Vector3 muzzle, Vector2 aim, float sinceFire)
        {
            bool bursting = sinceFire < 0.15f;
            int wanted = bursting ? _arcs.Length : charge > 0f ? Mathf.CeilToInt(Mathf.Clamp01(charge * 1.15f) * _arcs.Length) : 0;
            if (Time.time < _nextArcs && wanted == ActiveArcs)
            {
                return;
            }
            _nextArcs = Time.time + ArcRefreshSeconds;
            ActiveArcs = 0;
            Vector3 up = _gun.transform.up;
            for (int i = 0; i < _arcs.Length; i++)
            {
                LineRenderer arc = _arcs[i];
                if (arc == null)
                {
                    continue;
                }
                // 明滅: 作り直すたびに、少しの確率で消える（満ちるほど消えにくい）。
                bool on = i < wanted && (bursting || Random.value < 0.55f + 0.45f * charge);
                arc.enabled = on;
                if (!on)
                {
                    continue;
                }
                ActiveArcs++;
                Vector3 from;
                Vector3 to;
                if (bursting)
                {
                    from = _firedMuzzle;
                    float spread = Random.Range(-1.2f, 1.2f);
                    Vector2 dir = Rotate(_firedAim, spread);
                    to = _firedMuzzle + (Vector3)(dir * Random.Range(2.5f, 5f));
                }
                else
                {
                    int kind = i % 3;
                    if (kind == 0 && _coils.Length > 1)
                    {
                        // コイルから隣のコイルへ（巻き線の外側をまたぐ）。
                        int c = Random.Range(0, _coils.Length - 1);
                        from = RingPoint(_coils[c], up, Random.Range(0f, Mathf.PI * 2f));
                        to = RingPoint(_coils[c + 1], up, Random.Range(0f, Mathf.PI * 2f));
                    }
                    else if (kind == 1 && _coils.Length > 0)
                    {
                        // コイルから上下のレールへ。
                        Transform coil = _coils[Random.Range(0, _coils.Length)];
                        float side = Random.value < 0.5f ? 1f : -1f;
                        from = RingPoint(coil, up, side > 0f ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f);
                        to = coil.position + up * (_railOffset * side) + (Vector3)(aim * Random.Range(-0.6f, 0.6f));
                    }
                    else
                    {
                        // 砲口の光の玉のまわり。
                        float angle = Random.Range(0f, Mathf.PI * 2f);
                        float reach = Mathf.Max(0.6f, OrbSize * 1.3f);
                        from = muzzle;
                        to = muzzle + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * reach;
                    }
                }
                Jag(arc, from, to, bursting ? 0.35f : 0.25f);
                float strength = bursting ? 1f - sinceFire / 0.15f : 0.45f + 0.55f * charge;
                Color color = Color.Lerp(_color, Color.white, 0.55f + 0.3f * charge);
                color.a = Mathf.Clamp01(strength) * Random.Range(0.75f, 1f);
                Paint(arc, color);
                arc.widthMultiplier = (bursting ? 0.12f : 0.05f + 0.05f * charge) * Random.Range(0.7f, 1.2f);
            }
        }

        // 折れ線: 両端を結ぶ線の間の点を、線と直角（画面の中の向き）に乱数でずらす（真ん中ほど大きく。端は動かさない）。
        private static void Jag(LineRenderer arc, Vector3 from, Vector3 to, float jag)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            Vector3 side = length > 1e-4f ? Vector3.Cross(along / length, Vector3.forward) : Vector3.up;
            for (int k = 0; k < ArcPoints; k++)
            {
                float t = k / (ArcPoints - 1f);
                Vector3 p = Vector3.Lerp(from, to, t);
                if (k > 0 && k < ArcPoints - 1)
                {
                    float bulge = Mathf.Sin(t * Mathf.PI);
                    p += side * (Random.Range(-1f, 1f) * jag * length * bulge * 0.5f) + new Vector3(0f, 0f, Random.Range(-0.15f, 0.15f));
                }
                arc.SetPosition(k, p);
            }
        }

        private Vector3 RingPoint(Transform coil, Vector3 up, float angle)
        {
            // コイルの輪は砲身と直角の面（砲身の上の向きと、画面の奥行きの向き）。手前の半分（z が負）に寄せる。
            float z = -Mathf.Abs(Mathf.Sin(angle));
            return coil.position + up * (Mathf.Cos(angle) * _coilRadius) + new Vector3(0f, 0f, z * _coilRadius);
        }

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // 砲口の光の玉: ため具合で育ち、満ちる直前は速く脈打つ。
        private void RenderOrb(float charge, Vector3 muzzle)
        {
            Ready = charge >= ReadyCharge;
            if (_orb == null)
            {
                return;
            }
            if (charge <= 0f)
            {
                OrbSize = 0f;
                SetVisible(_orb, false);
                return;
            }
            float pulse = Ready ? 1f + 0.25f * Mathf.Sin(Time.time * 55f) : 1f + 0.08f * Mathf.Sin(Time.time * 18f);
            OrbSize = Mathf.Lerp(0.3f, 2.2f, Mathf.Pow(charge, 1.4f)) * pulse;
            _orb.transform.position = muzzle;
            _orb.transform.rotation = Quaternion.Euler(0f, 0f, Time.time * 90f);
            _orb.transform.localScale = Vector3.one * OrbSize;
            Color color = Color.Lerp(_color, Color.white, charge * 0.7f);
            color.a = 0.55f + 0.45f * charge;
            Tint(_orb, color);
            SetVisible(_orb, true);
        }

        private void RenderGather(float charge, Vector3 muzzle)
        {
            if (_gather == null)
            {
                return;
            }
            _gather.transform.position = muzzle;
            ParticleSystem.EmissionModule emission = _gather.emission;
            emission.rateOverTime = charge > 0f ? Mathf.Lerp(20f, 110f, charge) : 0f;
        }

        // 衝撃の輪: もうすぐ撃つ間は砲口へ縮む輪（合図）。撃った後は、砲身と直角の向きにつぶれた輪が広がって消える。
        private void RenderShock(float charge, Vector3 muzzle, Vector2 aim, float sinceFire)
        {
            if (_shockRing == null)
            {
                return;
            }
            if (sinceFire < ShockSeconds)
            {
                float k = sinceFire / ShockSeconds;
                float radius = Mathf.Lerp(0.6f, 4.5f, 1f - Mathf.Pow(1f - k, 3f));
                DrawEllipse(_shockRing, _firedMuzzle, _firedAim, radius * 0.35f, radius, 0.28f * (1f - k) + 0.04f, 1f - k);
                return;
            }
            if (charge >= ReadyCharge)
            {
                // 満ちる直前の、縮んでいく輪（くり返す）。
                float k = Mathf.Repeat(Time.time * 4f, 1f);
                float radius = Mathf.Lerp(3f, 0.5f, k);
                DrawEllipse(_shockRing, muzzle, aim, radius, radius, 0.06f + 0.06f * k, 0.3f + 0.6f * k);
                return;
            }
            _shockRing.enabled = false;
        }

        // 砲口の閃光（8 本の光の星）: 一瞬で大きく開いて、すぐ消える。
        private void RenderFlash(float sinceFire)
        {
            if (_flash == null)
            {
                return;
            }
            if (sinceFire >= FlashSeconds)
            {
                SetVisible(_flash, false);
                return;
            }
            float k = sinceFire / FlashSeconds;
            float size = Mathf.Lerp(1.2f, 5.5f, 1f - Mathf.Pow(1f - Mathf.Clamp01(k * 3f), 3f)) * (1f - k * 0.4f);
            _flash.transform.position = _firedMuzzle + new Vector3(0f, 0f, -0.1f);
            _flash.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(_firedAim.y, _firedAim.x) * Mathf.Rad2Deg + 11.25f);
            _flash.transform.localScale = Vector3.one * size;
            Color color = Color.Lerp(Color.white, _color, k);
            color.a = 1f - k * k;
            Tint(_flash, color);
            SetVisible(_flash, true);
        }

        // 撃ち出す向きの光の筋（弾の通り道。砲口から 9m。根元ほど太く、時間で細って消える）。
        private void RenderStreak(float sinceFire)
        {
            if (_streak == null)
            {
                return;
            }
            if (sinceFire >= StreakSeconds)
            {
                _streak.enabled = false;
                return;
            }
            float k = sinceFire / StreakSeconds;
            _streak.positionCount = 2;
            _streak.SetPosition(0, _firedMuzzle);
            _streak.SetPosition(1, _firedMuzzle + (Vector3)(_firedAim * Mathf.Lerp(4f, 9f, k)));
            _streak.widthMultiplier = 0.9f * (1f - k);
            Color color = Color.Lerp(Color.white, _color, k);
            color.a = 1f - k;
            Paint(_streak, color, 0f);
            _streak.enabled = true;
        }

        private void DrawEllipse(LineRenderer line, Vector3 center, Vector2 aim, float alongRadius, float acrossRadius, float width, float alpha)
        {
            const int Points = 40;
            line.positionCount = Points;
            line.loop = true;
            Vector2 across = new Vector2(-aim.y, aim.x);
            for (int i = 0; i < Points; i++)
            {
                float angle = i * Mathf.PI * 2f / Points;
                Vector2 p = aim * (Mathf.Cos(angle) * alongRadius) + across * (Mathf.Sin(angle) * acrossRadius);
                line.SetPosition(i, center + (Vector3)p);
            }
            line.widthMultiplier = width;
            Color color = Color.Lerp(_color, Color.white, 0.6f);
            color.a = Mathf.Clamp01(alpha);
            Paint(line, color);
            line.enabled = true;
        }

        private void Paint(LineRenderer line, Color color, float endAlpha = -1f)
        {
            _gradient = _gradient ?? new Gradient();
            float end = endAlpha < 0f ? color.a : endAlpha;
            _gradient.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(end, 1f) });
            line.colorGradient = _gradient;
        }

        private void Tint(MeshRenderer renderer, Color color)
        {
            _block = _block ?? new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        private static void SetVisible(Renderer renderer, bool visible)
        {
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }
    }
}
