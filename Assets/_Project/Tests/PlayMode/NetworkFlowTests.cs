using System;
using System.Collections;
using System.Collections.Generic;
using Airerage.Core.Rollback;
using Airerage.Net;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.Settings;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// ネットワーク対戦の流れ（docs/15 T6-7。ユーザーの要望、2026-10-19）: メインメニューの NETWORK VS → ホスト → 待機所で全員が READY →
    /// キャラクター選択（アイコンを選ぶ画面。相手の枠も出る）→ 全員が決定 → ステージ選択（ホスト）→ 全員が選んだステージを読み込んで
    /// 試合。相手は、テストの中で同じ PC から本当の通信（127.0.0.1）でつなぐもう 1 つのセッション（画面なし）。最後まで同じ状態になる
    /// こと、結果の CHARACTER SELECT で全員がロビーのキャラクター選択へ戻ること（T6-8）、抜けると相手の接続も切れることを確かめる。
    /// </summary>
    public sealed class NetworkFlowTests
    {
        // 相手のセッションを毎フレーム進める（入力なし）。
        private sealed class BotPump : MonoBehaviour
        {
            public OnlineSession Session;

            private void Update()
            {
                Session?.Update(Time.deltaTime, tick => default);
            }
        }

        [UnityTest]
        public IEnumerator NetworkVs_GoesFromTheWaitingRoom_ThroughCharacterAndStageSelect_ToAMatchInSync()
        {
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            int savedCharacter = PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0);
            OnlineSession bot = null;
            GameObject pump = null;
            try
            {
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, 0);
                SceneManager.LoadScene("App");
                yield return null;
                var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                menu.ContinueFromTitle();
                yield return null;
                menu.Network.Button.onClick.Invoke();
                yield return null;
                var online = UnityEngine.Object.FindFirstObjectByType<OnlineMenu>();
                Assert.That(online, Is.Not.Null, "メニューにネットワーク対戦のメニューがない");
                Assert.That(online.IsOpen && online.ShownPanel == "Connect", Is.True, "NETWORK VS で接続の画面を出さない");

                // ホストとして待ち受け、相手がつなぐ。
                online.PortField.text = "0";
                online.NameField.text = "HOST";
                online.Host();
                yield return null;
                var connection = UnityEngine.Object.FindFirstObjectByType<OnlineConnection>();
                Assert.That(connection, Is.Not.Null, "App のシーンに接続がない");
                OnlineSession host = connection.Session;
                Assert.That(host, Is.Not.Null, "ホストにならない");
                yield return WaitFor(() => online.ShownPanel == "Waiting", 2f, "待機所を出さない");
                bot = OnlineSession.Join("127.0.0.1", host.Port, new RollbackSettings());
                bot.LocalName = "BOT";
                pump = new GameObject("BotPump");
                UnityEngine.Object.DontDestroyOnLoad(pump);
                pump.AddComponent<BotPump>().Session = bot;
                yield return WaitFor(() => bot.LocalPlayer == 1 && host.LobbyPlayerCount == 2 && host.Roster[1].Name == "BOT", 10f, "相手がつながらない");

                // 待機所のチーム分け（docs/19 T9-5）: ホストが MODE でチーム対戦にすると、札が 4 色の列に並ぶ。ホストは札を決定してその人を
                // 次のチームへ、自分のチームは MY TEAM で変えられる。MODE をもう一度で個人戦に戻す（この後は今までと同じ流れ）。
                Assert.That(online.TeamButton.gameObject.activeSelf, Is.False, "個人戦なのに MY TEAM がある");
                online.ModeButton.Button.onClick.Invoke();
                yield return null;
                Assert.That(host.TeamMode, Is.True, "MODE でチーム対戦にならない");
                yield return WaitFor(() => bot.TeamMode && bot.LocalTeam == 2, 10f, "相手にチーム対戦とチームが届かない");
                Assert.That(online.TeamButton.gameObject.activeSelf && online.ShuffleButton.gameObject.activeSelf, Is.True);
                Assert.That(online.SlotPosition(1).y, Is.LessThan(online.SlotPosition(0).y), "チームが縦に並んでいない（BLUE の札が RED の下にない）");
                online.SlotButton(1).Button.onClick.Invoke();
                yield return WaitFor(() => bot.LocalTeam == 3, 10f, "ホストが札を決定しても相手のチームが変わらない");
                online.TeamButton.Button.onClick.Invoke();
                Assert.That(host.LocalTeam, Is.EqualTo(2), "MY TEAM で自分のチームが変わらない");
                online.SplitButton.Button.onClick.Invoke();
                online.ShuffleButton.Button.onClick.Invoke();
                yield return null;
                Assert.That(host.Roster[0].Team != host.Roster[1].Team, Is.True, "2 人を 1 対 1 に分けていない");
                // チームの見出し（YELLOW）を決定すると、そのチームに入る（ユーザーの要望、2026-11-02）。
                online.TeamHeader(3).Button.onClick.Invoke();
                Assert.That(host.LocalTeam, Is.EqualTo(4), "見出しを決定してもそのチームに入らない");
                // 観戦の待機所: 相手が入ると、右の端の列に札が並ぶ。ホストは入れない。
                // ホストも観戦の見出しで観戦に回れる（2026-11-03）。チームの見出しで戻る。
                Assert.That(online.SpectatorHeader.Button.interactable, Is.True, "ホストが観戦の見出しを選べない");
                online.SpectatorHeader.Button.onClick.Invoke();
                Assert.That(host.LocalSpectator, Is.True, "ホストが観戦にならない");
                online.TeamHeader(3).Button.onClick.Invoke();
                Assert.That(host.LocalSpectator, Is.False, "チームの見出しで観戦から戻れない");
                Assert.That(host.LocalTeam, Is.EqualTo(4));
                bot.LocalSpectator = true;
                yield return WaitFor(() => host.Roster[1].Spectator, 10f, "相手が観戦の待機所に入らない");
                yield return null;
                Assert.That(online.SlotPosition(1).x, Is.GreaterThan(1500f), "観戦の人の札が観戦の列にない");
                Assert.That(online.WaitingMessage, Does.Contain(Loc.T("WAITING FOR PLAYERS", "プレイヤーを待っています")), "参加者が 1 人なのに進めそうな案内");
                bot.LocalTeam = 1;
                yield return WaitFor(() => !host.Roster[1].Spectator && host.Roster[1].Team == 1, 10f, "観戦から見出しのチームに戻れない");
                online.ModeButton.Button.onClick.Invoke();
                yield return WaitFor(() => !bot.TeamMode, 10f, "個人戦に戻らない");

                // 待機所: 全員が READY でキャラクター選択へ。
                online.ReadyButton.Button.onClick.Invoke();
                Assert.That(host.LocalReady, Is.True, "READY で準備完了にならない");
                bot.LocalReady = true;
                yield return WaitFor(() => online.ShownPanel == "CharacterSelect" && bot.Phase == LobbyPhase.CharacterSelect, 10f,
                    "全員が READY でもキャラクター選択へ進まない");
                CharacterSelectScreen characters = menu.CharacterSelect;
                Assert.That(characters.Mode, Is.EqualTo(CharacterSelectScreen.SelectMode.Network));
                bot.LocalCharacter = 2;
                yield return WaitFor(() => characters.IsJoined(1) && characters.CharacterOf(1) == 2, 10f, "相手の枠に相手のカーソルを出さない");

                // 自分はキーボードでカーソルを動かして決定する。相手も決定すると、ステージ選択へ。
                yield return Tap(keyboard, Key.RightArrow);
                int hostCharacter = characters.CharacterOf(0);
                Assert.That(hostCharacter, Is.EqualTo(1), "矢印で自分のカーソルが動かない");
                yield return WaitFor(() => bot.Roster[0].Character == 1, 10f, "自分のカーソルが相手に届かない");
                yield return Tap(keyboard, Key.Enter);
                Assert.That(host.LocalReady, Is.True, "決定で準備完了にならない");
                bot.LocalReady = true;
                yield return WaitFor(() => online.ShownPanel == "StageSelect", 10f, "全員が決定してもステージ選択へ進まない");
                StageSelectScreen stages = menu.StageSelect;
                Assert.That(stages.NetworkMode && !stages.ReadOnly, Is.True, "ホストがステージを選べない");

                // ホストがステージを決めると、全員がそのステージを読み込む（結果まで見るので、短い試合にする）。
                connection.MatchRules.CountdownTicks = 30;
                connection.MatchRules.DurationTicks = 60;
                stages.Option(0).Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.OnlineMatch && !flow.IsLoading, 15f, "試合のシーンを読み込まない");
                Assert.That(flow.LoadedScenes, Is.EqualTo(new[] { "Stage_Athletic", "Match_Online" }), "選んだステージに試合の仕組みを重ねていない");
                yield return WaitFor(() => host.State == OnlineSession.SessionState.Running, 5f, "読み込んだのにホストの試合が始まらない");
                Assert.That(bot.State, Is.EqualTo(OnlineSession.SessionState.Loading));
                Assert.That(bot.PendingStage, Is.EqualTo(0), "開始のメッセージのステージが違う");

                // 相手も同じステージ（読み込んだシーン）で始める。
                bot.BeginPending(StageFromScene(), bot.PendingStage);
                // カウントダウン 30＋試合 60＋同点のアディショナルタイム（5 秒）が終わるまで。
                const int ticks = 480;
                host.TickLimit = ticks;
                bot.TickLimit = ticks;
                yield return WaitFor(() => host.Session.CurrentTick == ticks && bot.Session.CurrentTick == ticks
                    && host.Session.ConfirmedTick >= ticks - 1 && bot.Session.ConfirmedTick >= ticks - 1, 20f, "最後のティックまで進まない");
                yield return null;
                yield return null;
                Assert.That(bot.World.Checksum(), Is.EqualTo(host.World.Checksum()), "ホストと相手の状態が違う");
                Assert.That(host.Desyncs, Is.Empty);
                Assert.That(host.PlayerCharacters, Is.EqualTo(new[] { hostCharacter, 2 }), "選んだキャラクターで始まらない");
                Assert.That(host.PlayerNames, Is.EqualTo(new[] { "HOST", "BOT" }));

                // 結果の項目（ホストは REMATCH と CHARACTER SELECT を押せる）。CHARACTER SELECT で、全員がロビーのキャラクター選択へ。
                var results = UnityEngine.Object.FindFirstObjectByType<ResultsMenu>();
                Assert.That(results, Is.Not.Null, "結果の画面の項目がない");
                yield return WaitFor(() => results.IsOpen, 10f, "試合が終わっても結果の項目を出さない");
                Assert.That(results.Rematch.Button.interactable && results.CharacterSelect.Button.interactable, Is.True, "ホストが選べない");
                // ネットワーク対戦の 3 つ目は MAIN MENU ではなく BACK ROOM（全員を待機所へ戻す。2026-11-03）。
                Assert.That(results.BackRoom.LabelText.text, Is.EqualTo("BACK ROOM"), "3 つ目の項目が BACK ROOM でない");
                Assert.That(results.BackRoom.Button.interactable, Is.True, "ホストが BACK ROOM を押せない");
                results.CharacterSelect.Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューに戻らない");
                // メニューのシーンを読み込み直したので、画面を探し直す。
                menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                online = UnityEngine.Object.FindFirstObjectByType<OnlineMenu>();
                yield return WaitFor(() => online.ShownPanel == "CharacterSelect" && bot.State == OnlineSession.SessionState.Lobby
                    && bot.Phase == LobbyPhase.CharacterSelect, 10f, "全員がキャラクター選択へ戻らない");
                Assert.That(connection.IsConnected, Is.True, "接続が切れた");
                Assert.That(host.LobbyPlayerCount, Is.EqualTo(2), "相手がロビーにいない");
                Assert.That(menu.CharacterSelect.Mode, Is.EqualTo(CharacterSelectScreen.SelectMode.Network));
                Assert.That(menu.CharacterSelect.CharacterOf(0), Is.EqualTo(hostCharacter), "選んでいたキャラクターでない");

                // 抜けると、相手の接続も切れる。
                connection.Leave();
                Assert.That(connection.Session, Is.Null, "接続が残っている");
                yield return WaitFor(() => bot.State == OnlineSession.SessionState.Ended, 5f, "相手の接続が切れない");
            }
            finally
            {
                bot?.Dispose();
                if (pump != null)
                {
                    UnityEngine.Object.Destroy(pump);
                }
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, savedCharacter);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        // 読み込んでいるステージのシーンから、OnlineRunner と同じようにステージを作る。
        private static OnlineStage StageFromScene()
        {
            var errors = new List<string>();
            var spawns = new List<System.Numerics.Vector3>();
            foreach (Vector3 spawn in SpawnPointAuthoring.Collect())
            {
                spawns.Add(spawn.ToNumerics());
            }
            var stage = new OnlineStage(SceneTerrainBuilder.Build(1, errors), spawns, KnockoutBounds.Find(errors),
                StageZoneAuthoring.Collect(errors), CoinSpawnAuthoring.Collect(errors), BankSpotAuthoring.Collect(errors));
            Assert.That(errors, Is.Empty);
            return stage;
        }

        private static IEnumerator Tap(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds, string message)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > seconds)
                {
                    Assert.Fail(message);
                }
                yield return null;
            }
        }
    }
}
