using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 恐怖の館のかぼちゃの投石機（周期のある発射台 + ZoneLook.Catapult。2026-11-21 の方針: 汎用のギミックの見た目を変えるときは、効果と配置も
    /// 見た目に合わせる。前はいつも効く発射台だった。表示だけ）。手本はマンゴネル（ねじった綱の束の力で腕を振り上げ、腕が緩衝材を巻いた
    /// 横木に当たって止まった所で、かごの中の物が放たれる。横木の位置で放つ角度が決まる）と、かぼちゃを遠くへ飛ばす競技
    /// （パンキン・チャンキン）の投石機。
    /// - 予告の間: 巻き上げ機の歯止めが一段ずつ鳴りながら腕を引き絞り、腕が張力で小さく震える。
    /// - 効き始め（当たり: かごの中の人を放つ）: 腕が一気に跳ね上がって横木に当たり、小さく跳ね返る。かごのかぼちゃは奥の暗がりへ飛んでいく。
    /// - その後: 巻き上げ機が腕をゆっくり引き下ろし、下りきったらかごに次のかぼちゃが載る。
    /// 当たり（発射台）はかごの所だけ（StageZone のまま）。
    /// </summary>
    public sealed class CatapultView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("腕（軸の所。Z の軸の周りに回す）と、休む角度・横木に当たる角度（度）")]
        [SerializeField] private Transform _arm;
        [SerializeField] private float _restAngle = -16f;
        [SerializeField] private float _stopAngle = 100f;
        [Tooltip("巻き上げ機の胴（Z の軸の周りに回す）")]
        [SerializeField] private Transform _drum;
        [Tooltip("かごの中のかぼちゃ（腕の子）と、放ったかぼちゃ（ワールドで飛ばす）")]
        [SerializeField] private Transform _payload;
        [SerializeField] private Transform _thrown;
        [Tooltip("放ったかぼちゃの速さ（ワールド。奥へ飛ぶ）と重力")]
        [SerializeField] private Vector3 _throwVelocity = new Vector3(-6f, 22f, 16f);
        [SerializeField] private float _throwGravity = 22f;
        [Tooltip("巻き上げの綱（胴から腕の綱の掛け金へ）と、腕の掛け金")]
        [SerializeField] private LineRenderer _rope;
        [SerializeField] private Transform _ropeHook;

        /// <summary>腕が跳ね上がる時間・上で止まる時間・引き下ろす時間（秒）。</summary>
        public const float SwingSeconds = 0.14f;
        public const float HoldSeconds = 0.35f;
        public const float LowerSeconds = 1.3f;

        private float _fireTime = -100f;
        private bool _wasActive;
        private Vector3 _throwFrom;
        private float _lastClick;

        /// <summary>腕の今の角度（度。テスト用）。</summary>
        public float ArmAngle { get; private set; }

        /// <summary>放ったかぼちゃが飛んでいるか（テスト用）。</summary>
        public bool Throwing => _thrown != null && _thrown.gameObject.activeSelf;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, Transform arm, float restAngle, float stopAngle, Transform drum, Transform payload, Transform thrown,
            Vector3 throwVelocity, LineRenderer rope, Transform ropeHook)
        {
            _view = view;
            _arm = arm;
            _restAngle = restAngle;
            _stopAngle = stopAngle;
            _drum = drum;
            _payload = payload;
            _thrown = thrown;
            _throwVelocity = throwVelocity;
            _rope = rope;
            _ropeHook = ropeHook;
        }

        private void Awake()
        {
            if (_thrown != null)
            {
                _thrown.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (_view == null || _arm == null)
            {
                return;
            }
            bool active = _view.ShowsActive && _view.ActiveElapsedTicks >= 0;
            bool warning = _view.ShowsWarning && !active;
            if (active && !_wasActive)
            {
                _fireTime = Time.time;
                if (_payload != null && _thrown != null && _payload.gameObject.activeSelf)
                {
                    _throwFrom = _payload.position;
                    _thrown.position = _throwFrom;
                    _thrown.gameObject.SetActive(true);
                }
            }
            _wasActive = active;
            float since = Time.time - _fireTime;
            float angle = AngleAt(since, _restAngle, _stopAngle);
            bool reloaded = since >= SwingSeconds + HoldSeconds + LowerSeconds;
            if (warning && reloaded)
            {
                // 引き絞る: 予告が進むほど少し下へ引かれ、張力で細かく震える。
                int warningTicks = Mathf.Max(1, _view.CoreZone.Timing.WarningTicks);
                float progress = 1f - Mathf.Clamp01(_view.TicksUntilActive / (float)warningTicks);
                angle -= 3.5f * progress;
                angle += 0.5f * progress * Mathf.Sin(Time.time * 70f);
            }
            ArmAngle = angle;
            _arm.localRotation = Quaternion.Euler(0f, 0f, angle);
            if (_payload != null)
            {
                // 下りきったら次のかぼちゃが載る（ぽんと落ちてくる）。
                bool shown = reloaded || since < 0f;
                if (_payload.gameObject.activeSelf != shown)
                {
                    _payload.gameObject.SetActive(shown);
                }
                float drop = Mathf.Clamp01((since - (SwingSeconds + HoldSeconds + LowerSeconds)) / 0.18f);
                _payload.localScale = Vector3.one * (since > 0f && since < 100f ? Mathf.Lerp(0.6f, 1f, drop) : 1f);
            }
            UpdateDrum(since, warning && reloaded);
            UpdateRope(since);
            UpdateThrown(since);
        }

        /// <summary>撃ってから since 秒の腕の角度（跳ね上がる → 横木で小さく跳ね返る → 上で止まる → ゆっくり引き下ろされる）。</summary>
        public static float AngleAt(float since, float rest, float stop)
        {
            if (since < 0f)
            {
                return rest;
            }
            if (since < SwingSeconds)
            {
                float u = since / SwingSeconds;
                return Mathf.Lerp(rest, stop, u * u);
            }
            float after = since - SwingSeconds;
            if (after < HoldSeconds)
            {
                // 緩衝材で跳ね返り、すぐ収まる。
                return stop - 8f * Mathf.Exp(-after * 14f) * Mathf.Abs(Mathf.Sin(after * 32f));
            }
            after -= HoldSeconds;
            if (after < LowerSeconds)
            {
                float u = after / LowerSeconds;
                return Mathf.Lerp(stop, rest, u * u * (3f - 2f * u));
            }
            return rest;
        }

        private void UpdateDrum(float since, bool winding)
        {
            if (_drum == null)
            {
                return;
            }
            float lowering = since - SwingSeconds - HoldSeconds;
            if (lowering > 0f && lowering < LowerSeconds)
            {
                // 引き下ろす間は、綱を巻き取って回る。
                _drum.Rotate(0f, 0f, -260f * Time.deltaTime, Space.Self);
            }
            else if (winding && Time.time - _lastClick > 0.11f)
            {
                // 引き絞る間は、歯止めで一段ずつ。
                _lastClick = Time.time;
                _drum.Rotate(0f, 0f, -9f, Space.Self);
            }
        }

        private void UpdateRope(float since)
        {
            if (_rope == null || _drum == null || _ropeHook == null)
            {
                return;
            }
            Vector3 from = _drum.position;
            Vector3 to = _ropeHook.position;
            // 放った直後は綱がゆるむ（真ん中が垂れる）。引き下ろしている間と、休んでいる間は張る。
            float slack = since >= 0f && since < SwingSeconds + HoldSeconds ? 0.9f : 0.05f;
            int count = _rope.positionCount;
            for (int i = 0; i < count; i++)
            {
                float u = count > 1 ? i / (float)(count - 1) : 0f;
                Vector3 point = Vector3.Lerp(from, to, u);
                point.y -= slack * 4f * u * (1f - u);
                _rope.SetPosition(i, point);
            }
        }

        private void UpdateThrown(float since)
        {
            if (_thrown == null || !_thrown.gameObject.activeSelf)
            {
                return;
            }
            float t = since;
            if (t > 2.6f || t < 0f)
            {
                _thrown.gameObject.SetActive(false);
                return;
            }
            // 放る向き: 投石機の枠の左右の向き（裏返した枠は右へ。THIEF が左右を反転して置いた写しも）に合わせる。
            Vector3 velocity = _throwVelocity;
            velocity.x = -Mathf.Abs(velocity.x) * Mathf.Sign(transform.lossyScale.x);
            _thrown.position = _throwFrom + velocity * t + 0.5f * _throwGravity * t * t * Vector3.down;
            _thrown.Rotate(260f * Time.deltaTime, 0f, 140f * Time.deltaTime, Space.Self);
        }
    }
}
