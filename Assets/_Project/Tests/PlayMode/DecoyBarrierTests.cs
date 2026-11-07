using System.Collections;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Items;
using Airerage.Core.Objects;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 身代わり爆弾の周りの線（ユーザーの指定、2026-11-07）: 六角形のうち 3 本の線（左上・右上・下）を出し、攻撃を受けるたびに 1 本ずつ消す
    /// （あと何回で爆発するか）。
    /// </summary>
    public sealed class DecoyBarrierTests
    {
        [UnityTest]
        public IEnumerator DecoyBomb_ShowsThreeLines_AndLosesOnePerHit()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var view = Object.FindFirstObjectByType<ItemEffectsView>();
            Assert.That(view, Is.Not.Null, "ItemEffectsView がない");
            var feed = (ISimulationFeed)runner;
            Airerage.Core.Simulation.CharacterState player = runner.PlayerState;
            var position = new Vector2(player.Position.X + 2f, player.Position.Y + 1.5f);
            int id = feed.Objects.NextId;
            feed.Objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.Decoy, Owner = 0, Position = position, End = new Vector2(0.52f, 1.17f), SpawnTick = feed.CurrentTick,
                ExpireTick = int.MaxValue, Variant = (byte)ItemId.DecoyDoll, Stored = 1, BonusPercent = 80,
            });
            yield return WaitSeconds(0.2f);
            Assert.That(view.BarrierLinesShown(id), Is.EqualTo(3), "置いたときに 3 本の線が出ない");

            ItemRules.HitDecoys(feed.Objects, 901, position, Vector2.One * 2f, new AttackHit { Damage = 5 }, 1, AttackPower.Normal);
            yield return WaitSeconds(0.5f);
            Assert.That(view.BarrierLinesShown(id), Is.EqualTo(2), "1 回受けても線が 1 本減らない");

            ItemRules.HitDecoys(feed.Objects, 902, FindDecoy(feed, id), Vector2.One * 2f, new AttackHit { Damage = 5 }, 1, AttackPower.Normal);
            yield return WaitSeconds(0.5f);
            Assert.That(view.BarrierLinesShown(id), Is.EqualTo(1), "2 回受けたのに線が 1 本にならない");
        }

        private static Vector2 FindDecoy(ISimulationFeed feed, int id)
        {
            foreach (WorldObject item in feed.Objects.Items)
            {
                if (item.Id == id)
                {
                    return item.Position;
                }
            }
            Assert.Fail("爆弾がない");
            return Vector2.Zero;
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = UnityEngine.Time.time;
            while (UnityEngine.Time.time - start < seconds)
            {
                yield return null;
            }
        }
    }
}
