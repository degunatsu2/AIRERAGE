using System.Collections;
using System.Threading;
using Airerage.Runtime.DebugTools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 止まったときの見張り（2026-11-17 の指摘: ウインドウを戻すと同時に Esc を押すと止まる）。メインの処理が止まったら、別のスレッドが
    /// どの段階で止まったかをログに書く。
    /// </summary>
    public sealed class HangWatchdogTests
    {
        [UnityTest]
        public IEnumerator Watchdog_Reports_WhereTheMainThreadStalled_AndRemovesItsMarkers()
        {
            var go = new GameObject("HangWatchdog");
            var watchdog = go.AddComponent<HangWatchdog>();
            go.SendMessage("OnApplicationFocus", true);
            yield return null;
            int beats = watchdog.Beats;
            yield return null;
            Assert.That(watchdog.Beats, Is.GreaterThan(beats), "毎フレームの段階の印が進まない");
            Assert.That(MarkerCount(), Is.GreaterThan(0), "PlayerLoop に印がない");
            Assert.That(watchdog.Reports, Is.Zero, "動いているのに止まったと書いた");

            // メインの処理を止める（見張りのスレッドが気づいて書く）。
            Thread.Sleep((int)((HangWatchdog.StallSeconds + 1f) * 1000f));
            yield return null;
            Assert.That(watchdog.Reports, Is.EqualTo(1), "止まったことを書かない");
            StringAssert.Contains("[HangWatchdog]", watchdog.LastReport);
            StringAssert.Contains("止まった段階", watchdog.LastReport);

            Object.Destroy(go);
            yield return null;
            Assert.That(MarkerCount(), Is.Zero, "外した後も PlayerLoop に印が残る");
        }

        private static int MarkerCount()
        {
            int count = 0;
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (loop.subSystemList == null)
            {
                return 0;
            }
            foreach (PlayerLoopSystem phase in loop.subSystemList)
            {
                if (phase.subSystemList == null)
                {
                    continue;
                }
                foreach (PlayerLoopSystem system in phase.subSystemList)
                {
                    if (system.type != null && system.type.Name == "PhaseMarker")
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
