using UnityEngine;

namespace Airerage.Runtime.DebugTools
{
    /// <summary>
    /// タイム計測コースのスタートかゴールの範囲。範囲は同じ GameObject の BoxCollider（トリガー。地形としては読み込まれない）。
    /// スタートはシーンに1つ。ゴールはコースごとに1つ置き、コース名を付ける。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CourseZone : MonoBehaviour
    {
        public enum ZoneKind
        {
            Start,
            Goal,
        }

        [SerializeField] private ZoneKind _kind = ZoneKind.Goal;
        [Tooltip("ゴールのコース名")]
        [SerializeField] private string _course = "";

        public ZoneKind Kind => _kind;
        public string Course => _course;

        /// <summary>XY 平面上の範囲。</summary>
        public Rect Area
        {
            get
            {
                Bounds bounds = GetComponent<BoxCollider>().bounds;
                return Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
            }
        }

        /// <summary>シーンを組み立てるとき（エディタのビルダー）に使う。</summary>
        public void Configure(ZoneKind kind, string course)
        {
            _kind = kind;
            _course = course;
        }

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }
    }
}
