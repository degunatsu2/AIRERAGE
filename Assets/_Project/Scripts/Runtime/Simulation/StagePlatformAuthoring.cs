using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// シーンに置く動く足場と扉（Core の StagePlatform。ユーザーの要望、2026-10-26）。この物体の位置が道のりの元の位置（扉は閉じたときの
    /// 中心）で、大きさ _size の箱（回転と拡大縮小は使わない）。道のりは周期の中の秒と、元の位置からのずれの並び。見た目は
    /// StagePlatformView が子の物体を動かして描く（この物体は動かさない）。値はシーンを作るコード（エディタのビルダー）で決める【仮】。
    /// </summary>
    public sealed class StagePlatformAuthoring : MonoBehaviour
    {
        [Serializable]
        public struct Key
        {
            [Tooltip("周期の中の時刻（秒）")]
            public float Seconds;

            [Tooltip("元の位置からのずれ（m）")]
            public Vector2 Offset;

            public Key(float seconds, Vector2 offset)
            {
                Seconds = seconds;
                Offset = offset;
            }
        }

        [SerializeField] private StagePlatformKind _kind = StagePlatformKind.Path;
        [Tooltip("箱の大きさ（m）")]
        [SerializeField] private Vector2 _size = new Vector2(4f, 0.6f);
        [Tooltip("ワイヤーがつながるか")]
        [SerializeField] private bool _attachable = true;
        [Tooltip("道のり（周期の中の秒の順。最初は 0 秒）")]
        [SerializeField] private Key[] _keys = { new Key(0f, Vector2.zero) };
        [Tooltip("周期（秒）")]
        [SerializeField] private float _periodSeconds = 10f;
        [Tooltip("周期のずれ（秒）")]
        [SerializeField] private float _phaseSeconds;
        [Tooltip("ある間（秒。終わりが始まり以下なら、いつもある）")]
        [SerializeField] private float _activeFromSeconds;
        [SerializeField] private float _activeToSeconds;
        [Tooltip("点の間を、ゆっくり動き出して止まるように動くか")]
        [SerializeField] private bool _smooth;
        [Tooltip("扉: 開く高さ（m）と、全開まで・閉じきるまでの秒")]
        [SerializeField] private float _openHeight = 3f;
        [SerializeField] private float _openSeconds = 0.5f;
        [SerializeField] private float _closeSeconds = 1f;
        [Tooltip("扉とボタンの組（0 なら組なし。ボタンで開く扉と、そのボタンに同じ正の数）")]
        [SerializeField] private int _group;
        [Tooltip("周期で開く扉: 周期と、周期の初めの開いている長さ（秒。周期が 0 なら周期では開かない。ずれは _phaseSeconds）")]
        [SerializeField] private float _openEverySeconds;
        [SerializeField] private float _openForSeconds;

        public StagePlatformKind Kind => _kind;
        public Vector2 Size => _size;
        public int Group => _group;

        /// <summary>道のりを動く足場にする（シーンのビルダー用）。</summary>
        public void ConfigurePath(Vector2 size, bool attachable, Key[] keys, float periodSeconds, float phaseSeconds = 0f, float activeFromSeconds = 0f,
            float activeToSeconds = 0f, bool smooth = false)
        {
            _kind = StagePlatformKind.Path;
            _size = size;
            _attachable = attachable;
            _keys = keys;
            _periodSeconds = periodSeconds;
            _phaseSeconds = phaseSeconds;
            _activeFromSeconds = activeFromSeconds;
            _activeToSeconds = activeToSeconds;
            _smooth = smooth;
        }

        /// <summary>
        /// 扉にする（シーンのビルダー用）。group が正ならボタンで開き、openEverySeconds が正なら周期で開く（周期の初めの openForSeconds の間。
        /// ずれ phaseSeconds）。
        /// </summary>
        public void ConfigureDoor(Vector2 size, bool attachable, float openHeight, float openSeconds, float closeSeconds, int group = 0,
            float openEverySeconds = 0f, float openForSeconds = 0f, float phaseSeconds = 0f)
        {
            _kind = StagePlatformKind.Door;
            _size = size;
            _attachable = attachable;
            _openHeight = openHeight;
            _openSeconds = openSeconds;
            _closeSeconds = closeSeconds;
            _group = group;
            _openEverySeconds = openEverySeconds;
            _openForSeconds = openForSeconds;
            _phaseSeconds = phaseSeconds;
        }

        /// <summary>扉のボタンにする（シーンのビルダー用。ワイヤーがつながる）。</summary>
        public void ConfigureButton(Vector2 size, int group)
        {
            _kind = StagePlatformKind.Button;
            _size = size;
            _attachable = true;
            _group = group;
        }

        /// <summary>Core の足場にする（秒はティックに丸める）。値が不正なら ArgumentException。</summary>
        public StagePlatform ToPlatform()
        {
            Vector3 p = transform.position;
            var origin = new NVector2(p.x, p.y);
            var half = new NVector2(_size.x * 0.5f, _size.y * 0.5f);
            if (_kind == StagePlatformKind.Door)
            {
                if (_openEverySeconds > 0f)
                {
                    return StagePlatform.TimedDoor(origin, half, _attachable, _openHeight, Ticks(_openSeconds), Ticks(_closeSeconds),
                        Ticks(_openEverySeconds), Ticks(_openForSeconds), Ticks(_phaseSeconds));
                }
                return StagePlatform.Door(origin, half, _attachable, _openHeight, Ticks(_openSeconds), Ticks(_closeSeconds), _group);
            }
            if (_kind == StagePlatformKind.Button)
            {
                return StagePlatform.Button(origin, half, _group);
            }
            var keys = new PlatformKey[_keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new PlatformKey(Ticks(_keys[i].Seconds), new NVector2(_keys[i].Offset.x, _keys[i].Offset.y));
            }
            return StagePlatform.Path(origin, half, _attachable, keys, Ticks(_periodSeconds), Ticks(_phaseSeconds), Ticks(_activeFromSeconds),
                Ticks(_activeToSeconds), _smooth);
        }

        private static int Ticks(float seconds)
        {
            return Mathf.Max(0, Mathf.RoundToInt(seconds * SimTime.TicksPerSecond));
        }

        /// <summary>シーンの足場をすべて集める（不正なものは errors に書いて除く）。</summary>
        public static List<StagePlatform> Collect(ICollection<string> errors)
        {
            var platforms = new List<StagePlatform>();
            foreach (StagePlatformAuthoring authoring in FindObjectsByType<StagePlatformAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                try
                {
                    platforms.Add(authoring.ToPlatform());
                }
                catch (ArgumentException e)
                {
                    errors.Add($"動く足場（{authoring.name}）を除いた: {e.Message}");
                }
            }
            return platforms;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _kind == StagePlatformKind.Door ? new Color(0.4f, 1f, 0.6f)
                : _kind == StagePlatformKind.Button ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.8f, 0.3f);
            Gizmos.DrawWireCube(transform.position, new Vector3(_size.x, _size.y, 0f));
        }
    }
}
