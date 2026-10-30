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
using Airerage.Runtime.Data;
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
            public CharacterConfigAsset CharacterOf(int index) => null;
            public string LabelOf(int index) => $"P{index + 1}";
            public Color ColorOf(int index) => Color.white;
            public int CurrentTick => World.Tick;
            public MatchRules Rules => World.Rules;
            public MatchState Match => World.Match;
            public CoinField Coins => World.Coins.Field;
            public Airerage.Core.Objects.WorldObjects Objects => World.Objects;
            public System.Collections.Generic.IReadOnlyList<StagePlatform> Platforms => World.Platforms;
            public System.Collections.Generic.IReadOnlyList<StageTrap> Traps => World.Traps;
            public StageState Stage => World.Stage;
            public System.Collections.Generic.IReadOnlyList<StageZone> Zones => World.Zones;
            public bool TryGetClone(int index, out CharacterState clone) => SimWorldFeed.TryGetClone(World, index, out clone);
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

        // 状態を手で書き換える読み取り口（撃破の数と最後に攻撃した人を、テストが決める）。
        private sealed class ManualFeed : MonoBehaviour, ISimulationFeed
        {
            public CharacterState[] States = new CharacterState[0];
            public MatchRules MatchRules = new MatchRules();
            public int Tick;
            public BankSpot[] Banks = new BankSpot[0];

            public int CharacterCount => States.Length;
            public int LocalCharacter => 0;
            public CharacterState GetState(int index) => States[index];
            public CharacterConfig GetConfig(int index) => new CharacterConfig();
            public CharacterConfigAsset CharacterOf(int index) => null;
            public string LabelOf(int index) => $"CPU{index + 1}";
            public Color ColorOf(int index) => Color.white;
            public int CurrentTick => Tick;
            public MatchRules Rules => MatchRules;
            public MatchState Match => MatchState.None;
            public CoinField Coins => null;
            public Airerage.Core.Objects.WorldObjects Objects => null;
            public IReadOnlyList<StagePlatform> Platforms => null;
            public IReadOnlyList<StageTrap> Traps => null;
            public StageState Stage => null;
            public IReadOnlyList<StageZone> Zones => null;
            public bool TryGetClone(int index, out CharacterState clone)
            {
                clone = default;
                return false;
            }
            public IReadOnlyList<CoinSpawnPoint> CoinSpawns => new CoinSpawnPoint[0];
            public IReadOnlyList<BankSpot> BankSpots => Banks;
#pragma warning disable 67
            public event Action Ticked;
#pragma warning restore 67
        }

        [UnityTest]
        public IEnumerator KillLog_ShowsWhoKnockedOutWhom_NewestOnTop_ThenRetractsAfterFiveSeconds()
        {
            // 撃破が起きると、タイマーの右に「撃破した人 → 撃破された人」を出す（ユーザーの要望、2026-10-30）。
            var root = new GameObject("KillLogTest");
            root.SetActive(false);
            var feed = root.AddComponent<ManualFeed>();
            feed.States = new CharacterState[3];
            var notices = root.AddComponent<NoticeHud>();
            typeof(NoticeHud).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(notices, feed);
            root.SetActive(true);
            try
            {
                // CPU2 が CPU3 を攻撃している。
                feed.States[2].Combat.HasLastAttacker = true;
                feed.States[2].Combat.LastAttacker = 1;
                yield return null;
                yield return null;
                Assert.That(notices.KillLog, Is.Empty, "前提: まだ撃破はない");

                // CPU3 がステージの外へ（撃破された数が増え、CPU2 の撃破の数が増える。戻ると最後に攻撃した人は消える）。
                feed.States[2].Combat.Falls = 1;
                feed.States[2].Combat.HasLastAttacker = false;
                feed.States[1].Combat.Knockouts = 1;
                yield return null;
                Assert.That(notices.KillLog.Count, Is.EqualTo(1), "キルログが出ない");
                Assert.That(notices.KillLog[0].Killer, Is.EqualTo("CPU2"));
                Assert.That(notices.KillLog[0].Victim, Is.EqualTo("CPU3"));

                // 少し後に、CPU1 がだれにも攻撃されずに落ちる（撃破した人の名前はない）。新しい札がいちばん上。
                float first = Time.time;
                while (Time.time - first < 1f)
                {
                    yield return null;
                }
                feed.States[0].Combat.Falls = 1;
                yield return null;
                Assert.That(notices.KillLog.Count, Is.EqualTo(2));
                Assert.That(notices.KillLog[0].Killer, Is.Empty, "自分で落ちたのに、撃破した人がいる");
                Assert.That(notices.KillLog[0].Victim, Is.EqualTo("CPU1"));
                Assert.That(notices.KillLog[1].Victim, Is.EqualTo("CPU3"), "前からの札が下にない");
                Assert.That(notices.KillsShown, Is.EqualTo(2));

                // 出てから 5 秒で引っ込む（先に出た札から）。
                while (Time.time - first < NoticeHud.NoticeSeconds + 0.2f)
                {
                    yield return null;
                }
                Assert.That(notices.KillLog.Count, Is.EqualTo(1), "5 秒で引っ込まない");
                Assert.That(notices.KillLog[0].Victim, Is.EqualTo("CPU1"));
                while (Time.time - first < NoticeHud.NoticeSeconds + 1.4f)
                {
                    yield return null;
                }
                Assert.That(notices.KillLog, Is.Empty);
                Assert.That(notices.ChipTitles, Is.Empty);
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator HudOverACharacter_FadesWhileTheyOverlap()
        {
            // キャラクターと重なった HUD を透かす（HudOcclusion。ユーザーの要望、2026-10-30）。原点のキャラクターを正面から映す。
            var root = new GameObject("HudOcclusionTest");
            root.SetActive(false);
            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -30f);
            var feed = root.AddComponent<ManualFeed>();
            feed.States = new CharacterState[1];
            var occlusion = root.AddComponent<HudOcclusion>();
            typeof(HudOcclusion).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(occlusion, feed);
            typeof(HudOcclusion).GetField("_camera", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(occlusion, camera);
            root.SetActive(true);
            try
            {
                yield return null;
                Assert.That(occlusion.Bodies.Count, Is.EqualTo(1), "キャラクターの体の四角がない");
                Rect body = occlusion.Bodies[0];
                Assert.That(body.center.x, Is.EqualTo(Screen.width * 0.5f).Within(2f), "体が画面の中央にない");
                var over = new Rect(body.center.x - 20f, body.center.y - 20f, 40f, 40f);
                var away = new Rect(4f, 4f, 40f, 40f);
                Assert.That(occlusion.Overlaps(over), Is.True);
                Assert.That(occlusion.Overlaps(away), Is.False);
                float start = Time.time;
                float overAlpha = 1f;
                float awayAlpha = 1f;
                while (Time.time - start < HudOcclusion.FadeSeconds * 3f)
                {
                    overAlpha = occlusion.AlphaFor(root, 0, over);
                    awayAlpha = occlusion.AlphaFor(root, 1, away);
                    yield return null;
                }
                Assert.That(overAlpha, Is.EqualTo(HudOcclusion.FadedAlpha).Within(0.01f), "重なった HUD が透けない");
                Assert.That(awayAlpha, Is.EqualTo(1f), "重なっていない HUD が透けた");

                // 復帰を待っている人（見えない）とは重ならない。
                feed.States[0].Combat.RespawnTicksLeft = 60;
                yield return null;
                Assert.That(occlusion.Bodies, Is.Empty, "見えない人の体で透かす");
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(camera.gameObject);
            }
        }

        // 毎フレーム、画面の外の物の誘導表示を出す（EdgeGuideLayer がそのフレームの分を消した後に）。
        private sealed class GuideSource : MonoBehaviour
        {
            public EdgeGuideLayer Layer;
            public Vector3 World;

            private void Update()
            {
                Layer.Add(World, Color.yellow, null, "COIN 20", announce: "ko");
            }
        }

        [UnityTest]
        public IEnumerator EdgeGuides_FlashWhenTheyAppear_AndAgainWhenTheyMove()
        {
            // 撃破ドロップのかたまりや拠点の移動の誘導表示は、出てきたときに軽く目立たせる（ユーザーの要望、2026-10-30）。
            var camera = new GameObject("TestCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -30f);
            var layerObject = new GameObject("EdgeGuides");
            layerObject.SetActive(false);
            var layer = layerObject.AddComponent<EdgeGuideLayer>();
            layer.Configure(camera);
            layerObject.SetActive(true);
            var source = new GameObject("GuideSource").AddComponent<GuideSource>();
            source.Layer = layer;
            source.World = new Vector3(200f, 0f, 0f);
            try
            {
                yield return null;
                yield return null;
                Assert.That(layer.Count, Is.EqualTo(1), "画面の外の物の誘導表示がない");
                Assert.That(layer.AppearingCount, Is.EqualTo(1), "出てきたときに目立たせない");
                float start = Time.time;
                while (Time.time - start < EdgeGuideLayer.AppearSeconds + 0.2f)
                {
                    yield return null;
                }
                Assert.That(layer.AppearingCount, Is.Zero, "目立たせるのが終わらない");

                // 別の所（反対の端）へ移ったら、もう一度目立たせる（拠点が移ったとき）。
                source.World = new Vector3(-200f, 0f, 0f);
                yield return null;
                yield return null;
                Assert.That(layer.AppearingCount, Is.EqualTo(1), "移った札を目立たせない");
            }
            finally
            {
                Object.Destroy(source.gameObject);
                Object.Destroy(layer.gameObject);
                Object.Destroy(camera.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator CountdownNotices_StayUntilTheirTime_PastFiveSeconds()
        {
            // 秒を数える告知（拠点の移動の予告）は、5 秒で引っ込む決まりから外す（ユーザーの要望、2026-10-30）。ティックを止めて、予告の間に留める。
            var root = new GameObject("CountdownNoticeTest");
            root.SetActive(false);
            var feed = root.AddComponent<ManualFeed>();
            feed.States = new CharacterState[1];
            feed.MatchRules = new MatchRules { CountdownTicks = 0, DurationTicks = 6000, BankMoveIntervalTicks = 1200, BankNoticeTicks = 1200 };
            feed.Tick = 600;
            feed.Banks = new[]
            {
                new BankSpot(new System.Numerics.Vector2(0f, 6f), true),
                new BankSpot(new System.Numerics.Vector2(-10f, 6f), false),
            };
            var notices = root.AddComponent<NoticeHud>();
            typeof(NoticeHud).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(notices, feed);
            root.SetActive(true);
            try
            {
                yield return null;
                Assert.That(notices.ChipTitles, Does.Contain("BANK MOVES"), "前提: 拠点の移動の予告が出ている");
                float start = Time.time;
                while (Time.time - start < NoticeHud.NoticeSeconds + 1f)
                {
                    yield return null;
                }
                Assert.That(notices.ChipTitles, Does.Contain("BANK MOVES"), "秒を数える告知が 5 秒で引っ込んだ");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator TakingTheJackpot_MakesTheTakersPlateGlowRainbow_ThenReturnsToNormal()
        {
            // 100 枚のコインが、立っているキャラクター 0 の上に出て、そのまま拾われる（予告 1 秒、出るまで 0.5 秒）。
            var rules = new MatchRules
            {
                CountdownTicks = 0,
                DurationTicks = 900,
                JackpotTicksBeforeEnd = 840,
                JackpotNoticeTicks = 30,
                BankMoveIntervalTicks = 100000,
            };
            var terrain = new BoxTerrain().AddBox(-30f, -1f, 30f, 0f);
            var spawns = new[] { new CoinSpawnPoint(new System.Numerics.Vector2(0f, 1f), 100, CoinSpawnKind.Jackpot) };
            var world = new SimWorld(terrain, null, null, rules, spawns);
            world.AddCharacter(new CharacterConfig(), new System.Numerics.Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new System.Numerics.Vector3(-8f, GroundY, 0f));

            var root = new GameObject("JackpotCelebrationTest");
            root.SetActive(false);
            var feed = root.AddComponent<TestFeed>();
            feed.World = world;
            var board = root.AddComponent<ScoreboardHud>();
            typeof(ScoreboardHud).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(board, feed);
            root.SetActive(true);
            try
            {
                while (world.Tick < 120)
                {
                    yield return null;
                }
                yield return null;
                Assert.That(world.GetState(0).Coins.Held, Is.GreaterThanOrEqualTo(100), "前提: キャラクター 0 が 100 枚のコインを拾った");
                Assert.That(board.JackpotCelebrations, Is.EqualTo(1), "取ったことを見つけない");
                Assert.That(board.IsCelebrating(0), Is.True, "取った人の札が光らない");
                Assert.That(board.IsCelebrating(1), Is.False, "取っていない人の札が光った");
                Assert.That(board.LookOf(0).Jackpot, Is.GreaterThan(0f));

                float start = Time.time;
                while (Time.time - start < 3.2f)
                {
                    yield return null;
                }
                Assert.That(board.IsCelebrating(0), Is.False, "演出の後に、ふつうの札に戻らない");
                Assert.That(board.LookOf(0).Jackpot, Is.EqualTo(0f));
            }
            finally
            {
                Object.Destroy(root);
            }
        }
    }
}
