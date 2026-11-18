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
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 試合の始まりの演出（D-98・D-99。2026-11-17 のユーザーの決定: ゲート T12 → シネマスコープの帯つきで空撮 I2 とステージ名・札 I1 →
    /// 集中線とズーム → ラウンドコール。2026-11-18 の要望: Loading のゲージと Complete、ON YOUR MARKS → GET READY…… → ROLL! と始まりまでの
    /// ゲージ、試合中の表示は演出の後にフェードイン、ゲートは重いフレームでも開く演出を飛ばさない）。カウントダウンのティックだけで進む（表示だけ）。
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
            // 空撮・名前と札・Loading・集中線とズーム・ON YOUR MARKS・GET READY が入る長さ（6 秒。呼び出しが 1 回増えた。2026-11-18）。
            Assert.That(new MatchRules().CountdownTicks, Is.EqualTo(360));
            Assert.That(MatchIntroTimeline.DesignSeconds * Airerage.Core.Common.SimTime.TicksPerSecond, Is.EqualTo(360f));
            Assert.That(MatchIntroTimeline.AerialEnd, Is.LessThan(MatchIntroTimeline.Flash));
            Assert.That(MatchIntroTimeline.Flash, Is.LessThan(MatchIntroTimeline.DiveEnd));
            Assert.That(MatchIntroTimeline.ReadyStart, Is.LessThan(MatchIntroTimeline.SetStart));
            Assert.That(MatchIntroTimeline.SetStart, Is.LessThan(MatchIntroTimeline.DesignSeconds));
            Assert.That(MatchIntroTimeline.LabelsOut, Is.LessThan(MatchIntroTimeline.AerialEnd));
            // Complete の 2 回の点滅が、ズームの前に終わる。
            Assert.That(MatchIntroTimeline.LoadingEnd + MatchIntroTimeline.CompleteBlink * 3f, Is.LessThanOrEqualTo(MatchIntroTimeline.AerialEnd));
            // 呼び出しの長さはそろえる（ON YOUR MARKS と GET READY……）。
            Assert.That(MatchIntroTimeline.SetStart - MatchIntroTimeline.ReadyStart,
                Is.EqualTo(MatchIntroTimeline.DesignSeconds - MatchIntroTimeline.SetStart).Within(0.05f));
        }

        [Test]
        public void Loading_JumpsUpTo100_ThenBlinksCompleteTwice()
        {
            Assert.That(MatchIntroTimeline.LoadingPercent(0f), Is.EqualTo(0));
            int last = 0;
            int jumps = 0;
            for (float t = 0f; t < MatchIntroTimeline.LoadingEnd; t += 0.01f)
            {
                int percent = MatchIntroTimeline.LoadingPercent(t);
                Assert.That(percent, Is.GreaterThanOrEqualTo(last), "Loading の数が減った");
                Assert.That(percent, Is.LessThan(100), "100% になる前に Complete の時間になった");
                jumps += percent > last ? 1 : 0;
                last = percent;
            }
            Assert.That(jumps, Is.GreaterThan(6), "少しずつ跳んで増えない");
            Assert.That(MatchIntroTimeline.LoadingPercent(MatchIntroTimeline.LoadingEnd), Is.EqualTo(100));
            // 出る → 消える → 出る → 消える → 出たまま（2 回点滅）。
            int blinks = 0;
            bool shown = true;
            for (float t = MatchIntroTimeline.LoadingEnd; t < MatchIntroTimeline.AerialEnd; t += 0.005f)
            {
                bool now = MatchIntroTimeline.CompleteVisible(t);
                blinks += shown && !now ? 1 : 0;
                shown = now;
            }
            Assert.That(blinks, Is.EqualTo(2), "Complete を 2 回点滅させない");
            Assert.That(MatchIntroTimeline.CompleteVisible(MatchIntroTimeline.AerialEnd - 0.01f), Is.True);
        }

        [Test]
        public void Gate_DoesNotSkipTheOpening_OnAHeavyFrame()
        {
            // 閉じた後、読み込み直後の重いフレームで開く演出を一度に使い切っていた（2026-11-18 の指摘）。
            var root = new GameObject("GateTest");
            var bars = new GameObject("Bars");
            bars.transform.SetParent(root.transform);
            try
            {
                var graphic = bars.AddComponent<CurtainGraphic>();
                var curtain = root.AddComponent<ScreenCurtain>();
                curtain.Configure(bars, graphic, null);
                bars.SetActive(true);
                curtain.Open();
                MethodInfo step = typeof(ScreenCurtain).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance);
                // 読み込み直後の重いフレーム（1 秒）では進めない（落ち着くのを待つ）。
                step.Invoke(curtain, new object[] { 1f });
                Assert.That(curtain.OpenProgressSeconds, Is.EqualTo(0f), "重いフレームで開き始めた");
                step.Invoke(curtain, new object[] { 0.016f });
                step.Invoke(curtain, new object[] { 0.016f });
                // 落ち着いた後の重いフレームでも、1 フレームで進むのは少しだけ。
                step.Invoke(curtain, new object[] { 1.5f });
                Assert.That(curtain.OpenProgressSeconds, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.05f), "重いフレームで開く演出を飛ばした");
                Assert.That(curtain.Showing, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
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
        public IEnumerator Hud_ShowsTheStageAndLoading_HidesTheHud_ThenCallsTheStart()
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
                Assert.That(hud.LoadingPercent, Is.EqualTo(0));
                Assert.That(HudFade.Reveal, Is.EqualTo(0f), "演出の間に試合中の表示を出している");

                feed.Tick = TickAt(rules, 1.4f);
                yield return null;
                Assert.That(hud.PlatesShown, Is.EqualTo(2), "プレイヤーの札が滑り込まない");
                Assert.That(hud.LoadingPercent, Is.GreaterThan(0).And.LessThan(100), "Loading が増えない");
                Assert.That(HudFade.Reveal, Is.EqualTo(0f));

                feed.Tick = TickAt(rules, 2.05f);
                yield return null;
                Assert.That(hud.LoadingPercent, Is.EqualTo(100));
                Assert.That(hud.LoadingComplete, Is.True, "100% で Complete を出さない");

                feed.Tick = TickAt(rules, 4.2f);
                yield return null;
                Assert.That(hud.RoundCall, Is.EqualTo(MatchIntroTimeline.CallMarks), "ズームの後に ON YOUR MARKS を出さない");
                Assert.That(hud.StartGauge, Is.GreaterThan(0f).And.LessThan(0.5f));
                Assert.That(HudFade.Reveal, Is.GreaterThan(0f), "演出の後に試合中の表示を出し始めない");
                Assert.That(hud.TimerText, Is.Not.Empty, "試合中の表示と一緒にタイマーを出さない");

                feed.Tick = TickAt(rules, 5.5f);
                yield return null;
                Assert.That(hud.RoundCall, Is.EqualTo(MatchIntroTimeline.CallSet), "GET READY…… に替わらない");
                Assert.That(hud.StartGauge, Is.GreaterThan(0.5f).And.LessThan(1f));
                Assert.That(HudFade.Reveal, Is.EqualTo(1f));

                feed.Tick = rules.CountdownTicks + 1;
                yield return null;
                Assert.That(hud.ShowingIntro, Is.False);
                Assert.That(hud.RoundCall, Is.EqualTo(MatchIntroTimeline.CallGo), "始まった瞬間に ROLL! を出さない");
                Assert.That(hud.StartGauge, Is.EqualTo(1f));
                Assert.That(HudFade.Reveal, Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
            }
            yield return null;
            Assert.That(HudFade.Reveal, Is.EqualTo(1f), "外れた後も試合中の表示を隠したまま");
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }
    }
}
