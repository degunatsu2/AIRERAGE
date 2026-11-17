using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 試合の始まりの演出（D-98・D-99。2026-11-17 のユーザーの決定: ゲート T12 → シネマスコープの帯つきで空撮 I2 とステージ名・札 I1 →
    /// 集中線とズーム → READY → GO!）。カウントダウンのティックだけで進む（表示だけ）。
    /// </summary>
    public sealed class MatchIntroTests
    {
        private sealed class Feed : MonoBehaviour, ISimulationFeed
        {
            public CharacterState[] States = new CharacterState[2];
            public MatchRules MatchRules = new MatchRules();
            public int Tick;

            public int CharacterCount => States.Length;
            public int LocalCharacter => 0;
            public CharacterState GetState(int index) => States[index];
            public CharacterConfig GetConfig(int index) => new CharacterConfig();
            public CharacterConfigAsset CharacterOf(int index) => null;
            public string LabelOf(int index) => $"P{index + 1}";
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
            public IReadOnlyList<BankSpot> BankSpots => new BankSpot[0];
#pragma warning disable 67
            public event Action Ticked;
#pragma warning restore 67
        }

        // いつものカメラの仕組み（CinemachineBrain）の代わり。止められたかだけを見る。
        private sealed class FakeBrain : MonoBehaviour
        {
        }

        private static int TickAt(MatchRules rules, float designSeconds)
        {
            return Mathf.CeilToInt(designSeconds / MatchIntroTimeline.DesignSeconds * rules.CountdownTicks);
        }

        [Test]
        public void Countdown_IsLongEnough_ForTheWholeIntro()
        {
            // 空撮・名前と札・集中線とズーム・READY が入る長さ（5 秒。3 秒から延ばした）。
            Assert.That(new MatchRules().CountdownTicks, Is.EqualTo(300));
            Assert.That(MatchIntroTimeline.AerialEnd, Is.LessThan(MatchIntroTimeline.Flash));
            Assert.That(MatchIntroTimeline.Flash, Is.LessThan(MatchIntroTimeline.DiveEnd));
            Assert.That(MatchIntroTimeline.ReadyStart, Is.LessThan(MatchIntroTimeline.DesignSeconds));
            Assert.That(MatchIntroTimeline.LabelsOut, Is.LessThan(MatchIntroTimeline.AerialEnd));
        }

        [UnityTest]
        public IEnumerator Camera_FliesOverTheStage_DivesHome_ThenGivesTheCameraBack()
        {
            var root = new GameObject("IntroCameraTest");
            root.SetActive(false);
            var feed = root.AddComponent<Feed>();
            var cameraObject = new GameObject("TestCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.SetPositionAndRotation(new Vector3(3f, 2f, -30f), Quaternion.identity);
            camera.fieldOfView = 40f;
            var brain = cameraObject.AddComponent<FakeBrain>();
            var intro = root.AddComponent<MatchIntroCamera>();
            Set(intro, "_source", feed);
            Set(intro, "_camera", camera);
            Set(intro, "_brain", brain);
            root.SetActive(true);
            try
            {
                MatchRules rules = feed.MatchRules;
                feed.Tick = 0;
                // いつものカメラが位置を決めるのを待ってから（2 フレーム）、いつもの位置を覚えて動かし始める。
                for (int i = 0; i < 6; i++)
                {
                    yield return null;
                }
                Assert.That(intro.Directing, Is.True, "カウントダウンの間にカメラを動かさない");
                Assert.That(brain.enabled, Is.False, "いつものカメラを止めない（2 つで取り合う）");
                Assert.That(intro.Stage, Is.EqualTo("aerial"));
                Assert.That(Vector3.Distance(camera.transform.position, new Vector3(3f, 2f, -30f)), Is.GreaterThan(15f), "空撮で高い所から見ていない");

                feed.Tick = TickAt(rules, 3.2f);
                yield return null;
                Assert.That(intro.Stage, Is.EqualTo("dive"), "集中線とズーム（突進）にならない");
                Assert.That(camera.fieldOfView, Is.GreaterThan(40f), "突進で画角を広げない");

                feed.Tick = TickAt(rules, 3.6f);
                yield return null;
                Assert.That(intro.Stage, Is.EqualTo("home"), "白い光の後にいつもの位置へ戻らない");

                feed.Tick = TickAt(rules, 4.0f);
                yield return null;
                Assert.That(intro.Directing, Is.False, "READY の前にカメラを返さない");
                Assert.That(brain.enabled, Is.True, "いつものカメラを戻さない");
                Assert.That(Vector3.Distance(camera.transform.position, new Vector3(3f, 2f, -30f)), Is.LessThan(0.01f), "いつもの位置に戻らない");
                Assert.That(camera.fieldOfView, Is.EqualTo(40f).Within(0.01f));
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator Hud_ShowsTheStageAndPlates_ThenReady_ThenGo()
        {
            var root = new GameObject("IntroHudTest");
            root.SetActive(false);
            var feed = root.AddComponent<Feed>();
            var hud = root.AddComponent<MatchHud>();
            Set(hud, "_source", feed);
            root.SetActive(true);
            try
            {
                MatchRules rules = feed.MatchRules;
                feed.Tick = 0;
                yield return null;
                Assert.That(hud.ShowingIntro, Is.True, "カウントダウンで帯を出さない（ゲートが開いたときに付いている）");
                Assert.That(hud.StageName, Is.Not.Empty, "ステージの名前がない");
                Assert.That(hud.RoundCall, Is.Empty);

                feed.Tick = TickAt(rules, 2.0f);
                yield return null;
                Assert.That(hud.PlatesShown, Is.EqualTo(2), "プレイヤーの札が滑り込まない");

                feed.Tick = TickAt(rules, 4.2f);
                yield return null;
                Assert.That(hud.RoundCall, Is.EqualTo("READY"), "ズームの後に READY を出さない");

                feed.Tick = rules.CountdownTicks + 1;
                yield return null;
                Assert.That(hud.ShowingIntro, Is.False);
                Assert.That(hud.RoundCall, Is.EqualTo("GO!"), "始まった瞬間に GO! を出さない");
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
            }
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }
    }
}
