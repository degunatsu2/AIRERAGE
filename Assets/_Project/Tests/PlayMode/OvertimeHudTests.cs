using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Degunatsu.Core.Economy;
using Degunatsu.Core.Input;
using Degunatsu.Core.Match;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Degunatsu.PlayMode.Tests
{
    /// <summary>アディショナルタイムの表示（docs/12 T4-8）: OVERTIME と残り、拠点に着けば勝てる人、勝った人、結果の順位。</summary>
    public sealed class OvertimeHudTests
    {
        private const float GroundY = 0.91f;

        // 2 人の SimWorld を毎フレーム 1 ティック進める、試験用の読み取り口。
        private sealed class TestFeed : MonoBehaviour, ISimulationFeed
        {
            public SimWorld World;

            public int CharacterCount => World.CharacterCount;
            public int LocalCharacter => 0;
            public CharacterState GetState(int index) => World.GetState(index);
            public CharacterConfig GetConfig(int index) => World.GetConfig(index);
            public string LabelOf(int index) => $"P{index + 1}";
            public Color ColorOf(int index) => Color.white;
            public int CurrentTick => World.Tick;
            public MatchRules Rules => World.Rules;
            public MatchState Match => World.Match;
            public CoinField Coins => World.Coins.Field;
            public IReadOnlyList<CoinSpawnPoint> CoinSpawns => World.CoinSpawns;
            public IReadOnlyList<BankSpot> BankSpots => World.BankSpots;
            public event Action Ticked;

            private void Update()
            {
                World.Step(new PlayerInput[World.CharacterCount]);
                Ticked?.Invoke();
            }
        }

        [UnityTest]
        public IEnumerator Overtime_IsShown_AndTheWinnerComesFirst()
        {
            var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 20, OvertimeTicks = 600, BankMoveIntervalTicks = 100000 };
            var terrain = new BoxTerrain().AddBox(-30f, -1f, 30f, 0f);
            var world = new SimWorld(terrain, null, null, rules, null, new[] { new BankSpot(new System.Numerics.Vector2(0f, 1.5f), true) });
            world.AddCharacter(new CharacterConfig(), new System.Numerics.Vector3(-5f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new System.Numerics.Vector3(5f, GroundY, 0f));
            CharacterState leader = world.GetState(0);
            leader.Coins.Banked = 10;
            world.SetState(0, leader);
            CharacterState challenger = world.GetState(1);
            challenger.Coins.Banked = 2;
            challenger.Coins.Held = 9;
            world.SetState(1, challenger);

            var root = new GameObject("OvertimeHudTest");
            root.SetActive(false);
            var feed = root.AddComponent<TestFeed>();
            feed.World = world;
            var hud = root.AddComponent<MatchHud>();
            typeof(MatchHud).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(hud, feed);
            root.SetActive(true);
            try
            {
                float start = Time.time;
                while (world.Phase != MatchPhase.Overtime && Time.time - start < 3f)
                {
                    yield return null;
                }
                yield return null;
                Assert.That(world.Phase, Is.EqualTo(MatchPhase.Overtime), "前提: アディショナルタイムに入った");
                Assert.That(hud.TimerText, Does.StartWith("OVERTIME"), "残りの表示");
                Assert.That(hud.CenterText, Is.EqualTo("OVERTIME"), "OVERTIME を出さない");
                Assert.That(hud.ChallengersText, Does.Contain("P2"), "拠点に着けば勝てる人を出さない");

                // 逆転できる人が拠点に着く。
                challenger = world.GetState(1);
                challenger.Position = new System.Numerics.Vector3(0f, GroundY, 0f);
                world.SetState(1, challenger);
                yield return null;
                yield return null;
                Assert.That(world.Match.Winner, Is.EqualTo(1), "前提: 勝った");
                Assert.That(hud.CenterText, Is.EqualTo("P2 WINS!"), "勝った人を出さない");

                start = Time.time;
                while (!hud.ShowingResults && Time.time - start < 3f)
                {
                    yield return null;
                }
                Assert.That(hud.ShowingResults, Is.True, "結果が出ない");
                Assert.That(hud.Ranking[0], Is.EqualTo(1), "勝った人がいちばん上にならない");
            }
            finally
            {
                Object.Destroy(root);
            }
        }
    }
}
