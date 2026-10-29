using System;
using System.Collections.Generic;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// THIEF が奪って置いたギミック・仕掛けの見た目に、ステージの本物の見た目（シーンの物体）を写して使う（ユーザーの要望、2026-10-29。
    /// ThiefEffectsView が使う）。
    /// - ギミック: 本物の物体（StageZoneAuthoring・StageZoneView と見た目の子）を写し、置いたギミックの範囲・向き・周期にする
    ///   （StageZoneAuthoring.ConfigureCopy。写しの StageZoneView が本物と同じ線・予告・音を出す）。見た目の子は、大きさを縮めた分だけ縮め、
    ///   左右を反転したら位置と回りを反転する。本物の StageZoneView が起動時に作った線と予告は消す（写しが作り直す）。
    /// - 仕掛け: 本物の物体（発射装置・赤外線・ランプ・ミサイル、ピッチャーマシン・ボール）を写し、StageTrapView と StageTrapAuthoring を
    ///   外す（置いた仕掛けの状態は ThiefEffectsView が見た目に当てる）。
    /// シミュレーションのギミック・仕掛けの番号から本物の物体を探すのは、値が同じ物（StageZone.Equals、StageTrap.SameAs）。
    /// </summary>
    public sealed class PlacedGimmickLooks
    {
        private IReadOnlyList<StageZone> _zonesFor;
        private StageZoneAuthoring[] _zoneSources = Array.Empty<StageZoneAuthoring>();
        private IReadOnlyList<StageTrap> _trapsFor;
        private StageTrapAuthoring[] _trapSources = Array.Empty<StageTrapAuthoring>();

        /// <summary>ギミックの一覧 zones の番号 index の、本物の物体（なければ null）。</summary>
        public StageZoneAuthoring ZoneSource(IReadOnlyList<StageZone> zones, int index)
        {
            if (zones == null || index < 0 || index >= zones.Count)
            {
                return null;
            }
            if (!ReferenceEquals(zones, _zonesFor))
            {
                _zonesFor = zones;
                _zoneSources = new StageZoneAuthoring[zones.Count];
                foreach (StageZoneAuthoring authoring in Object.FindObjectsByType<StageZoneAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (authoring.IsCopy || !TryZoneOf(authoring, out StageZone zone))
                    {
                        continue;
                    }
                    for (int i = 0; i < zones.Count; i++)
                    {
                        if (_zoneSources[i] == null && zones[i].Equals(zone))
                        {
                            _zoneSources[i] = authoring;
                            break;
                        }
                    }
                }
            }
            return _zoneSources[index];
        }

        /// <summary>仕掛けの一覧 traps の番号 index の、本物の物体（なければ null）。</summary>
        public StageTrapAuthoring TrapSource(IReadOnlyList<StageTrap> traps, int index)
        {
            if (traps == null || index < 0 || index >= traps.Count)
            {
                return null;
            }
            if (!ReferenceEquals(traps, _trapsFor))
            {
                _trapsFor = traps;
                _trapSources = new StageTrapAuthoring[traps.Count];
                foreach (StageTrapAuthoring authoring in Object.FindObjectsByType<StageTrapAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!TryTrapOf(authoring, out StageTrap trap))
                    {
                        continue;
                    }
                    for (int i = 0; i < traps.Count; i++)
                    {
                        if (_trapSources[i] == null && traps[i].SameAs(trap))
                        {
                            _trapSources[i] = authoring;
                            break;
                        }
                    }
                }
            }
            return _trapSources[index];
        }

        /// <summary>
        /// 本物のギミック source の見た目を写して、置いたギミック placed（mirrored なら左右を反転）にする。写しは parent の子。
        /// </summary>
        public static GameObject CopyZone(StageZoneAuthoring source, in StageZone placed, bool mirrored, Transform parent)
        {
            if (!TryZoneOf(source, out StageZone original))
            {
                return null;
            }
            GameObject copy = CopyInactive(source.gameObject, out GameObject holder);
            // 本物の StageZoneView が起動時に作った線と予告（写しの StageZoneView が作り直す）。
            foreach (Transform child in Children(copy.transform))
            {
                if (child.GetComponent<LineRenderer>() != null || child.GetComponent<WarningOverlay>() != null)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            copy.GetComponent<StageZoneAuthoring>().ConfigureCopy(placed);
            float sx = (placed.MaxX - placed.MinX) / Mathf.Max(0.01f, original.MaxX - original.MinX);
            float sy = (placed.MaxY - placed.MinY) / Mathf.Max(0.01f, original.MaxY - original.MinY);
            foreach (Transform child in Children(copy.transform))
            {
                Fit(child, sx, sy, mirrored);
            }
            return Finish(copy, holder, parent, $"Placed{source.name}");
        }

        /// <summary>本物の仕掛け source の見た目を写す（StageTrapView・StageTrapAuthoring は外す。mirrored なら左右を反転）。写しは parent の子。</summary>
        public static GameObject CopyTrap(StageTrapAuthoring source, bool mirrored, Transform parent)
        {
            GameObject copy = CopyInactive(source.gameObject, out GameObject holder);
            Object.DestroyImmediate(copy.GetComponent<StageTrapView>());
            Object.DestroyImmediate(copy.GetComponent<StageTrapAuthoring>());
            if (mirrored)
            {
                foreach (Transform child in Children(copy.transform))
                {
                    Fit(child, 1f, 1f, true);
                }
            }
            return Finish(copy, holder, parent, $"Placed{source.name}");
        }

        /// <summary>本物のピッチャーマシン source のボールの見た目を写す（なければ null）。</summary>
        public static GameObject CopyBall(StageTrapAuthoring source, Transform parent)
        {
            Transform ball = source != null ? source.transform.Find("Ball") : null;
            if (ball == null)
            {
                return null;
            }
            GameObject copy = Object.Instantiate(ball.gameObject, parent);
            copy.transform.localScale = ball.lossyScale;
            copy.SetActive(true);
            return copy;
        }

        private static bool TryZoneOf(StageZoneAuthoring authoring, out StageZone zone)
        {
            try
            {
                zone = authoring.ToZone();
                return true;
            }
            catch (ArgumentException)
            {
                // 不正な設定のギミックは、試合の始まりに StageZoneAuthoring.Collect がエラーとして出して除いている。
                zone = default;
                return false;
            }
        }

        private static bool TryTrapOf(StageTrapAuthoring authoring, out StageTrap trap)
        {
            try
            {
                trap = authoring.ToTrap();
                return true;
            }
            catch (ArgumentException)
            {
                // 不正な設定の仕掛けは、試合の始まりに StageTrapAuthoring.Collect がエラーとして出して除いている。
                trap = null;
                return false;
            }
        }

        // 動き出す前に手を入れられるように、隠した入れ物の下で写す。
        private static GameObject CopyInactive(GameObject source, out GameObject holder)
        {
            holder = new GameObject("CopyHolder");
            holder.SetActive(false);
            GameObject copy = Object.Instantiate(source, holder.transform);
            copy.transform.position = source.transform.position;
            return copy;
        }

        private static GameObject Finish(GameObject copy, GameObject holder, Transform parent, string copyName)
        {
            copy.name = copyName;
            copy.transform.SetParent(parent, true);
            Object.Destroy(holder);
            return copy;
        }

        private static List<Transform> Children(Transform root)
        {
            var children = new List<Transform>(root.childCount);
            foreach (Transform child in root)
            {
                children.Add(child);
            }
            return children;
        }

        // 見た目の子を、範囲の大きさの比（sx, sy）に合わせ、mirrored なら左右を反転する（位置と回り）。見た目のない入れ物（動く見た目の親）は
        // その子に当てる。
        private static void Fit(Transform child, float sx, float sy, bool mirrored)
        {
            if (child.GetComponent<Renderer>() == null && child.childCount > 0 && child.localPosition == Vector3.zero && child.localScale == Vector3.one)
            {
                foreach (Transform inner in Children(child))
                {
                    Fit(inner, sx, sy, mirrored);
                }
                return;
            }
            Vector3 p = child.localPosition;
            child.localPosition = new Vector3(p.x * sx * (mirrored ? -1f : 1f), p.y * sy, p.z);
            Vector3 s = child.localScale;
            child.localScale = new Vector3(s.x * sx, s.y * sy, s.z);
            if (mirrored)
            {
                Vector3 e = child.localEulerAngles;
                child.localRotation = Quaternion.Euler(e.x, -e.y, -e.z);
            }
        }
    }
}
