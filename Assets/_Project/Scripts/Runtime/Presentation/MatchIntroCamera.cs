using System.Collections.Generic;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 試合の始まりの空撮のカメラ（D-98 の I2。2026-11-17 のユーザーの決定）。カウントダウンの間だけ、いつものカメラ（Cinemachine の
    /// CinemachineBrain）を止めてカメラを動かす: ステージ全体を高い所から見下ろして横へ回り込み（空撮）、最後に集中線と一緒に前へ突進し、
    /// 白い光の間に少し後ろへ移って、いつもの位置へ戻る。
    /// カウントダウンの間は全員が出現位置で止まっているので、始めに覚えた「いつもの位置」へ戻せばつなぎ目は出ない。
    /// 表示だけで、シミュレーションには何も書かない。時間割は MatchIntroTimeline。
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class MatchIntroCamera : MonoBehaviour
    {
        [Tooltip("状態を読む相手（ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("動かすカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("止めるいつものカメラの仕組み（CinemachineBrain）")]
        [SerializeField] private Behaviour _brain;

        private ISimulationFeed _feed;
        private MatchRules _rules;
        private readonly MatchIntroTimeline.TickClock _clock = new MatchIntroTimeline.TickClock();
        private int _framesSeen;
        private bool _directing;
        private bool _done;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private float _homeFov;
        private Vector3 _center;
        private Vector2 _size;

        /// <summary>カメラを動かしているか（テスト用）。</summary>
        public bool Directing => _directing;

        /// <summary>今の演出の段階の名前（テスト用。aerial ／ dive ／ home ／ 空）。</summary>
        public string Stage { get; private set; } = string.Empty;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _camera == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）かカメラが設定されていません", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            Release();
        }

        private void LateUpdate()
        {
            MatchRules rules = _feed.Rules;
            if (!ReferenceEquals(rules, _rules))
            {
                // 試合が始まった・始め直した。初めから。
                Release();
                _rules = rules;
                _framesSeen = 0;
                _done = false;
            }
            Stage = string.Empty;
            if (rules == null || rules.CountdownTicks <= 0 || _done)
            {
                return;
            }
            int tick = _feed.CurrentTick;
            float t = MatchIntroTimeline.DesignTime(rules, tick, _clock.Between(tick));
            if (tick >= rules.CountdownTicks || t >= MatchIntroTimeline.DiveEnd)
            {
                Release();
                _done = true;
                return;
            }
            if (!_directing)
            {
                // いつものカメラが位置を決めるのを 2 フレーム待ってから、その位置を覚えて止める（この順で動くように実行の順を最後にした）。
                if (_framesSeen++ < 2)
                {
                    return;
                }
                Capture();
            }
            Apply(t);
        }

        private void Capture()
        {
            Transform cam = _camera.transform;
            _homePosition = cam.position;
            _homeRotation = cam.rotation;
            _homeFov = _camera.fieldOfView;
            StageBounds? bounds = KnockoutBounds.Find(new List<string>());
            if (bounds.HasValue)
            {
                StageBounds b = bounds.Value;
                _center = new Vector3((b.MinX + b.MaxX) * 0.5f, (b.MinY + b.MaxY) * 0.5f, 0f);
                _size = new Vector2(b.MaxX - b.MinX, b.MaxY - b.MinY);
            }
            else
            {
                _center = new Vector3(_homePosition.x, _homePosition.y, 0f);
                _size = new Vector2(80f, 50f);
            }
            if (_brain != null)
            {
                _brain.enabled = false;
            }
            _directing = true;
        }

        private void Release()
        {
            if (!_directing)
            {
                return;
            }
            _camera.transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _camera.fieldOfView = _homeFov;
            if (_brain != null)
            {
                _brain.enabled = true;
            }
            _directing = false;
        }

        // 基準の秒 t のカメラ。
        private void Apply(float t)
        {
            Pose(t, out Vector3 position, out Quaternion rotation, out float fov);
            _camera.transform.SetPositionAndRotation(position, rotation);
            _camera.fieldOfView = fov;
        }

        /// <summary>基準の秒 t のカメラの位置・向き・画角（空撮 → 突進 → いつもの位置）。</summary>
        public void Pose(float t, out Vector3 position, out Quaternion rotation, out float fov)
        {
            float halfFov = _homeFov * 0.5f * Mathf.Deg2Rad;
            float aspect = Mathf.Max(0.5f, _camera.aspect);
            // ステージ全体が入る距離。
            float whole = Mathf.Max(_size.y * 0.62f / Mathf.Tan(halfFov), _size.x * 0.62f / (Mathf.Tan(halfFov) * aspect));
            Vector3 startPos = _center + new Vector3(-0.42f * _size.x, 0.62f * _size.y, -whole * 1.08f);
            Vector3 startLook = _center + new Vector3(-0.08f * _size.x, 0.04f * _size.y, 0f);
            Vector3 endPos = _center + new Vector3(0.3f * _size.x, 0.22f * _size.y, -whole * 0.86f);
            Vector3 endLook = _center + new Vector3(0.06f * _size.x, -0.04f * _size.y, 0f);
            Vector3 forward = _homeRotation * Vector3.forward;
            if (t < MatchIntroTimeline.AerialEnd)
            {
                Stage = "aerial";
                float k = UiEase.InOutCubic(t / MatchIntroTimeline.AerialEnd);
                // 空撮: 高い所から斜めに見下ろし、横へ回り込みながら少し寄る（真ん中で少し前へふくらむ弧）。
                position = Vector3.Lerp(startPos, endPos, k) + forward * Mathf.Sin(k * Mathf.PI) * whole * 0.06f;
                Vector3 look = Vector3.Lerp(startLook, endLook, k);
                rotation = Quaternion.LookRotation(look - position, Vector3.up);
                fov = _homeFov;
                return;
            }
            Quaternion aerialEnd = Quaternion.LookRotation(endLook - endPos, Vector3.up);
            if (t < MatchIntroTimeline.Flash)
            {
                // 突進: 加速しながらいつもの位置の前まで（スカイダイブと同じ。画角を少し広げて速さを出す）。
                Stage = "dive";
                float k = UiEase.InCubic((t - MatchIntroTimeline.AerialEnd) / (MatchIntroTimeline.Flash - MatchIntroTimeline.AerialEnd));
                position = Vector3.Lerp(endPos, _homePosition + forward * 18f, k);
                rotation = Quaternion.Slerp(aerialEnd, _homeRotation, UiEase.OutCubic(k));
                fov = Mathf.Lerp(_homeFov, _homeFov * 1.18f, k);
                return;
            }
            // 白い光の間に少し後ろへ移って、いつもの位置へ戻る。
            Stage = "home";
            float back = UiEase.OutCubic((t - MatchIntroTimeline.Flash) / (MatchIntroTimeline.DiveEnd - MatchIntroTimeline.Flash));
            position = Vector3.Lerp(_homePosition - forward * 10f, _homePosition, back);
            rotation = _homeRotation;
            fov = Mathf.Lerp(_homeFov * 1.1f, _homeFov, back);
        }
    }
}
