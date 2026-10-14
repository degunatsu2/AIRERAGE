using System;
using Airerage.Core.Common;
using UnityEngine;

namespace Airerage.Net
{
    /// <summary>表示の補正の調整値（docs/10_online_plan.md T2-8）。初期値はすべて【仮】。</summary>
    [Serializable]
    public sealed class CorrectionSmoothingSettings
    {
        /// <summary>補正するか（比べるために切り替えられる）。</summary>
        public bool Enabled = true;

        /// <summary>時定数（秒）。表示のずれが約 37%（1/e）に減るまでの時間。約3倍の時間でほぼ消える。</summary>
        [Min(0.001f)]
        public float TimeConstantSeconds = 0.08f;

        /// <summary>これより大きいずれ（m）は寄せずに、すぐ正しい位置に表示する（長い距離を滑って見えないように）。</summary>
        [Min(0.001f)]
        public float SnapDistance = 3f;

        public void Validate()
        {
            var v = new TuningValidator(nameof(CorrectionSmoothingSettings));
            v.Positive(nameof(TimeConstantSeconds), TimeConstantSeconds);
            v.Positive(nameof(SnapDistance), SnapDistance);
            v.ThrowIfInvalid();
        }
    }

    /// <summary>
    /// 巻き戻しでキャラクターの位置が飛んだとき、表示だけを少しずつ正しい位置へ寄せる（docs/10_online_plan.md T2-8）。
    /// シミュレーションの状態は変えない。飛んだ量の逆向きのずれ（オフセット）を表示に足し、ずれを時間とともに小さくする。
    /// </summary>
    public sealed class CorrectionSmoother
    {
        // これより小さいずれは 0 にする（m）。
        private const float StopDistance = 0.001f;

        private readonly CorrectionSmoothingSettings _settings;
        private readonly Vector3[] _offsets;

        public CorrectionSmoother(int players, CorrectionSmoothingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            _offsets = new Vector3[players];
        }

        /// <summary>プレイヤー player の表示に足すずれ。</summary>
        public Vector3 Offset(int player)
        {
            return _offsets[player];
        }

        /// <summary>プレイヤー player の位置が correction だけ飛んだ。表示はいったん元の位置に残し、そこから寄せる。</summary>
        public void AddCorrection(int player, Vector3 correction)
        {
            if (!_settings.Enabled || correction == Vector3.zero)
            {
                return;
            }
            Vector3 offset = _offsets[player] - correction;
            _offsets[player] = offset.magnitude > _settings.SnapDistance ? Vector3.zero : offset;
        }

        /// <summary>毎フレーム呼ぶ。ずれを時間に応じて小さくする。</summary>
        public void Update(float deltaTime)
        {
            float keep = _settings.Enabled ? Mathf.Exp(-deltaTime / _settings.TimeConstantSeconds) : 0f;
            for (int p = 0; p < _offsets.Length; p++)
            {
                Vector3 offset = _offsets[p] * keep;
                _offsets[p] = offset.magnitude < StopDistance ? Vector3.zero : offset;
            }
        }
    }
}
