using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 悪魔の手（恐怖の館。ユーザーの要望、2026-10-26）の指の動き（表示だけ）。地面を這う間は指を交互に曲げ伸ばしし、近くで人を握って
    /// いる間（CombatState.IsCaptured の人が手の上にいる）は、指を閉じる。手そのものの位置は StageZoneView が動かす（この物体の親）。
    /// </summary>
    public sealed class DemonHandView : MonoBehaviour
    {
        [Tooltip("指の付け根（ここを回して曲げる）")]
        [SerializeField] private Transform[] _fingers = new Transform[0];
        [Tooltip("指の節（付け根の先の節。付け根といっしょに曲げる）")]
        [SerializeField] private Transform[] _joints = new Transform[0];
        [Tooltip("握っている人を探す距離（m）")]
        [SerializeField] private float _reach = 2.5f;

        private ISimulationFeed _feed;
        private float _nextFeedSearch;
        private float _close;

        /// <summary>今、握っている形か（テスト用）。</summary>
        public bool Gripping { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Transform[] fingers, Transform[] joints)
        {
            _fingers = fingers;
            _joints = joints;
            _rest = null;
        }

        // 付け根の、組み立てたときの回転（親指を開いた形・扇の広がり）。
        private Quaternion[] _rest;

        private void LateUpdate()
        {
            FindFeed();
            Gripping = false;
            if (_feed != null && _feed.CharacterCount > 0)
            {
                Vector3 hand = transform.position;
                for (int i = 0; i < _feed.CharacterCount; i++)
                {
                    var state = _feed.GetState(i);
                    if (state.Combat.IsCaptured && Mathf.Abs(state.Position.X - hand.x) < _reach && Mathf.Abs(state.Position.Y - hand.y) < _reach * 1.5f)
                    {
                        Gripping = true;
                        break;
                    }
                }
            }
            _close = Mathf.MoveTowards(_close, Gripping ? 1f : 0f, Time.deltaTime * 5f);
            if (_rest == null || _rest.Length != _fingers.Length)
            {
                _rest = new Quaternion[_fingers.Length];
                for (int i = 0; i < _fingers.Length; i++)
                {
                    _rest[i] = _fingers[i] != null ? _fingers[i].localRotation : Quaternion.identity;
                }
            }
            for (int i = 0; i < _fingers.Length; i++)
            {
                if (_fingers[i] == null)
                {
                    continue;
                }
                // 這う: 指ごとに少しずらして曲げ伸ばし。握る: 内へ深く曲げる。
                float crawl = Mathf.Sin(Time.time * 5f + i * 1.3f) * 18f;
                float side = _fingers[i].localPosition.x >= 0f ? -1f : 1f;
                _fingers[i].localRotation = _rest[i] * Quaternion.Euler(0f, 0f, Mathf.Lerp(crawl, 55f * side, _close));
            }
            // 節: 這う間は付け根より遅れて小さく曲がり、握ると内へ深く曲がる（指が丸まる）。手前へも少し曲げて、立体的に握る。
            for (int i = 0; i < _joints.Length; i++)
            {
                if (_joints[i] == null)
                {
                    continue;
                }
                float wave = Mathf.Sin(Time.time * 5f + i * 0.9f - 0.8f) * 14f;
                float side = _joints[i].position.x >= transform.position.x ? -1f : 1f;
                _joints[i].localRotation = Quaternion.Euler(Mathf.Lerp(-6f, -30f, _close), 0f, Mathf.Lerp(wave, 38f * side, _close));
            }
        }

        private void FindFeed()
        {
            if (_feed is MonoBehaviour alive && alive == null)
            {
                _feed = null;
            }
            if (_feed != null || Time.unscaledTime < _nextFeedSearch)
            {
                return;
            }
            _nextFeedSearch = Time.unscaledTime + 1f;
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is ISimulationFeed feed)
                {
                    _feed = feed;
                    break;
                }
            }
        }
    }
}
