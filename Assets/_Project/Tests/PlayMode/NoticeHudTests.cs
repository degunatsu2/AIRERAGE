using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 試合中のお知らせとタイマー（ユーザーの選択、2026-10-14）: タイマーの下の札（拠点の移動、100 枚のコイン）、画面を横切る帯
    /// （JACKPOT INCOMING、COIN RUSH）、タイマーの札の見出しと数字、残り 10 秒の音。
    /// </summary>
    public sealed class NoticeHudTests
    {
        private const float GroundY = 0.91f;

        // SimWorld を毎フレーム 1 ティック進める、試験用の読み取り口。
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
        public IEnumerator Notices_ChipsAndBands_FollowTheMatchTimeline()
        {
            // 15 秒の試合。拠点は 2 秒ごとに移り 1 秒前に予告、COIN RUSH は 1 秒後から、100 枚のコインは 2.5 秒後に予告して 0.5 秒後に出る。
            var rules = new MatchRules
            {
                CountdownTicks = 0,
                DurationTicks = 900,
                LateGameTicks = 840,
                BankMoveIntervalTicks = 120,
                BankNoticeTicks = 60,
                JackpotTicksBeforeEnd = 750,
                JackpotNoticeTicks = 30,
            };
            var terrain = new BoxTerrain().AddBox(-30f, -1f, 30f, 0f);
            var spawns = new[] { new CoinSpawnPoint(new System.Numerics.Vector2(10f, 8f), 100, CoinSpawnKind.Jackpot) };
            var banks = new[]
            {
                new BankSpot(new System.Numerics.Vector2(0f, 6f), true),
                new BankSpot(new System.Numerics.Vector2(-10f, 6f), false),
            };
            var world = new SimWorld(terrain, null, null, rules, spawns, banks);
            world.AddCharacter(new CharacterConfig(), new System.Numerics.Vector3(-5f, GroundY, 0f));

            var root = new GameObject("NoticeHudTest");
            root.SetActive(false);
            var feed = root.AddComponent<TestFeed>();
            feed.World = world;
            var notices = root.AddComponent<NoticeHud>();
            typeof(NoticeHud).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(notices, feed);
            var hud = root.AddComponent<MatchHud>();
            typeof(MatchHud).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(hud, feed);
            root.SetActive(true);
            try
            {
                bool sawBankChip = false;
                bool sawRushBand = false;
                bool sawJackpotBand = false;
                bool sawJackpotChip = false;
                bool sawJackpotAppeared = false;
                while (world.Tick < 260)
                {
                    yield return null;
                    IReadOnlyList<string> chips = notices.ChipTitles;
                    sawBankChip |= chips.Contains("BANK MOVES");
                    sawRushBand |= notices.BandTitle == "COIN RUSH";
                    sawJackpotBand |= notices.BandTitle == "JACKPOT INCOMING";
                    sawJackpotChip |= chips.Contains("JACKPOT");
                    sawJackpotAppeared |= chips.Contains("JACKPOT 100");
                    if (world.Tick > 60 && world.Tick < 120)
                    {
                        Assert.That(chips.Contains("BANK MOVES"), Is.True, $"ティック {world.Tick}: 拠点の移動の予告の札がない");
                    }
                    if (world.Tick > 125 && world.Tick < 175)
                    {
                        Assert.That(chips.Contains("BANK MOVES"), Is.False, $"ティック {world.Tick}: 移った後も予告の札が残っている");
                    }
                }
                Assert.That(sawBankChip, Is.True, "拠点の移動の予告の札が出ない");
                Assert.That(sawRushBand, Is.True, "COIN RUSH の帯が出ない");
                Assert.That(sawJackpotBand, Is.True, "JACKPOT INCOMING の帯が出ない");
                Assert.That(sawJackpotChip, Is.True, "100 枚のコインの予告の札が出ない");
                Assert.That(sawJackpotAppeared, Is.True, "100 枚のコインが出た後の札が出ない");
                Assert.That(notices.BandsShown, Is.EqualTo(2), "帯の数が違う（COIN RUSH と JACKPOT INCOMING の 1 回ずつ）");

                Assert.That(hud.TimerLabel, Is.EqualTo("TIME"));
                Assert.That(hud.TimerDigits, Is.EqualTo(hud.TimerText));
                Assert.That(hud.CountdownTicksPlayed, Is.Zero, "残り 10 秒より前に音を鳴らした");

                // 残り 10 秒まで飛ばすと、1 秒ごとに音を鳴らす。
                while (world.Tick < 900 - 600)
                {
                    world.Step(new PlayerInput[1]);
                }
                float start = Time.time;
                while (Time.time - start < 1.5f)
                {
                    yield return null;
                }
                Assert.That(hud.CountdownTicksPlayed, Is.GreaterThanOrEqualTo(1), "残り 10 秒の音を鳴らさない");
            }
            finally
            {
                Object.Destroy(root);
            }
        }
    }
}
