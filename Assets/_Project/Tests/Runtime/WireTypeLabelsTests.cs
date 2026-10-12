using System;
using System.Collections.Generic;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.DebugTools;
using NUnit.Framework;

namespace Degunatsu.Runtime.Tests
{
    public sealed class WireTypeLabelsTests
    {
        [Test]
        public void Order_ContainsEveryWireModelTypeOnce()
        {
            var seen = new HashSet<WireModelType>(WireTypeLabels.Order);

            Assert.That(WireTypeLabels.Order.Count, Is.EqualTo(seen.Count), "重複がある");
            foreach (WireModelType type in Enum.GetValues(typeof(WireModelType)))
            {
                Assert.That(seen.Contains(type), Is.True, $"{type} が切り替えの順番にない");
                Assert.That(WireTypeLabels.Short(type), Is.Not.EqualTo(type.ToString()), $"{type} の表示名がない");
            }
        }

        [Test]
        public void Next_CyclesAThenBThenOldA()
        {
            Assert.That(WireTypeLabels.Next(WireModelType.Orbit), Is.EqualTo(WireModelType.Responsive));
            Assert.That(WireTypeLabels.Next(WireModelType.Responsive), Is.EqualTo(WireModelType.Inertial));
            Assert.That(WireTypeLabels.Next(WireModelType.Inertial), Is.EqualTo(WireModelType.Orbit));
        }
    }
}
