using System;
using System.Collections;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Flow;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// メニューの演出（ユーザーの決定、2026-11-07）:
    /// - 画面の切り替え（D-87）: タイトル → メインはスカイダイブ（カメラの突進と速さの線）、メイン → キャラクター選択は斜めの札と
    ///   アイコンのめくれ、戻るときは逆向きの風。画面はすぐ入れ替わり、演出は写しで動き、終わると写しは残らない。押すと早送り。
    /// - ボタンの演出（D-88）: 導火線の縁・行きすぎと残像・裏の札・文字のデコード・背景の連動・決定の衝撃・アイコン・周りが引く。
    /// - 試合へ入るときは黒い帯が閉じ、読み込みが済むと開く（T10）。
    /// - 効果音: カーソルはダイヤルのカチッ、決定は新しい音（音声ファイル）。画面を進むと進む風、戻ると戻る風（2026-11-07）。
    /// - クリックした所の火花、メインメニューの項目の色・右の名前の縁取り・短い名前（2026-11-07）。
    /// </summary>
    public sealed class MenuMotionTests
    {
        [UnityTest]
        public IEnumerator Menu_PlaysTransitions_AndButtonEffects_ThenLeavesNoCopies()
        {
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                SceneManager.LoadScene("App");
                yield return null;
                var flow = Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = Object.FindFirstObjectByType<MenuScreens>();
                ScreenTransition transition = menu.Transition;
                Assert.That(transition, Is.Not.Null, "画面の切り替えの演出がない");
                var camera = Object.FindFirstObjectByType<MenuCameraDrift>();
                var sounds = Object.FindFirstObjectByType<UiSounds>();

                // タイトル → メイン: スカイダイブ。画面はすぐ入れ替わり、写しが動き、カメラが前へ突進する。
                menu.ContinueFromTitle();
                Assert.That(menu.ShowingMain, Is.True, "タイトルの決定ですぐにメインメニューへ入れ替わらない");
                yield return null;
                yield return null;
                yield return null;
                Assert.That(transition.Running, Is.True, "演出が動いていない");
                Assert.That(transition.LastKind, Is.EqualTo(TransitionKind.SkyDive));
                Assert.That(transition.GhostCount, Is.GreaterThan(0), "画面の写しを動かしていない");
                Assert.That(camera.Diving, Is.True, "背景のカメラが前へ突進しない");
                Assert.That(GameObject.Find("SpeedLines"), Is.Not.Null, "速さの線がない");
                // 切り替えの途中でも、写しは本物の動きに合わせる（選んだ SINGLE は右へ出て、ほかの項目は下がる B13。2026-11-08）。
                yield return WaitSeconds(0.45f);
                Assert.That(transition.Running, Is.True, "前提: まだ切り替えの途中");
                GameObject ghostMain = GameObject.Find("Main (Transition)");
                Assert.That(ghostMain, Is.Not.Null, "メインメニューの写しがない");
                var ghostSingle = (RectTransform)ghostMain.transform.Find("SINGLE");
                var ghostLocal = (RectTransform)ghostMain.transform.Find("LOCAL");
                Assert.That(ghostSingle.anchoredPosition.x - ghostLocal.anchoredPosition.x, Is.GreaterThan(15f),
                    "切り替えの途中で、選んだ項目が出ず、ほかの項目が下がらない（B13）");
                yield return WaitFor(() => !transition.Running, 3f, "スカイダイブが終わらない");
                yield return null;
                Assert.That(CopiesLeft(), Is.Zero, "演出が終わっても写しが残っている");
                Assert.That(menu.GetComponentInChildren<MainMenuBackdrop>(), Is.Not.Null);

                // ボタンの演出: 選ぶと導火線の縁・裏の札（振れてから揺れる）・背景の見出し・アイコン、ほかの項目は暗く下がる。カーソルの音。
                // 文字のデコード（B5）はなくした（2026-11-08）。
                var backdrop = Object.FindFirstObjectByType<MainMenuBackdrop>();
                Assert.That(backdrop.Word, Is.EqualTo("SINGLE"), "最初の項目の名前を背景に出さない（B7）");
                EventSystem.current.SetSelectedGameObject(menu.Training.gameObject);
                Assert.That(sounds.History[sounds.History.Count - 1], Is.EqualTo(SoundId.UiMove), "カーソルの音が鳴らない");
                Assert.That(menu.Training.Plate.FuseRunning, Is.True, "選んでも縁に光が走らない（B1）");
                Assert.That(menu.Training.LabelText.text, Is.EqualTo("TRAINING"), "選んだ名前の文字が変わった（デコードはなくした）");
                Assert.That(menu.Training.Plate.Swinging, Is.True, "選んでも裏の札が出る動きを始めない（2026-11-08）");
                Assert.That(menu.Training.Rise != null && menu.Training.Rise.Playing, Is.True, "選んでも名前の文字がはねない（W2）");
                Assert.That(menu.Training.GetComponentInChildren<MenuIcon>().Selected, Is.True, "絵が動かない（B12）");
                yield return WaitSeconds(0.45f);
                Assert.That(menu.Training.Label, Is.EqualTo("TRAINING"));
                Assert.That(menu.Training.Plate.Back, Is.GreaterThan(0.9f), "裏の札が出ない（B3）");
                // 見本のページの裏の札の動き（2026-11-08）: 時計回りに少し回って出て、少し行きすぎてから戻り、0.3 秒たったらゆっくり揺れる。
                Assert.That(menu.Training.Plate.BackAngle, Is.LessThan(-0.5f).And.GreaterThan(-3f), "裏の札が時計回りに少し回っていない");
                Assert.That(UiEase.CubicBezier(0.34f, 1.7f, 0.5f, 1f, 0.5f), Is.GreaterThan(1f), "出るときに少し行きすぎない");
                float before = menu.Training.Plate.BackAngle;
                yield return WaitSeconds(0.2f);
                Assert.That(Mathf.Abs(menu.Training.Plate.BackAngle - before), Is.LessThan(0.4f), "揺れがゆっくりでない");
                Assert.That(SlantedPlate.SwayAt(0f), Is.EqualTo(0f).Within(0.001f), "揺れが止まった形から始まらない");
                Assert.That(SlantedPlate.SwayAt(SlantedPlate.SwayPeriod * 0.5f), Is.EqualTo(1f).Within(0.001f), "揺れが 2.3 秒の周期でない");
                Assert.That(menu.Single.IsReceded, Is.True, "ほかの項目が暗く下がらない（B13）");
                // 文字の演出（D-90）: 名前の前に ›››、名前が右へ寄る（W17）、札の右に小さな説明（W16）。選んでいない項目には出ない。
                Assert.That(menu.Training.Chevrons != null && menu.Training.Chevrons.Shown, Is.True, "名前の前に ››› が出ない（W17）");
                Assert.That(menu.Training.LabelOffset.x, Is.GreaterThan(20f), "名前が右へ寄らない（W17）");
                Assert.That(menu.Training.Sub, Is.Not.Null, "札の右に説明がない（W16）");
                Assert.That(menu.Training.Sub.color.a, Is.GreaterThan(0.9f), "札の右に説明が出ない（W16）");
                Assert.That(menu.Single.Sub.color.a, Is.LessThan(0.1f), "選んでいない項目の説明が出ている（W16）");
                Assert.That(menu.Single.Chevrons.Shown, Is.False, "選んでいない項目に ››› が出ている（W17）");
                Assert.That(menu.Single.LabelOffset.magnitude, Is.LessThan(0.5f), "選んでいない項目の名前が戻らない");
                Assert.That(LetterRise.Rise(LetterRise.RiseSeconds * 0.4f), Is.EqualTo(1f).Within(0.01f), "文字がはね上がりきらない（W2）");
                Assert.That(LetterRise.Rise(LetterRise.RiseSeconds), Is.Zero, "文字が元の位置に戻らない（W2）");
                // 左の背景（D-91 の M2 を白地に）: 白地に、選んだ項目の色の斜めの帯。ロゴは黒。
                MainMenuPanel panel = backdrop.Panel;
                Assert.That(panel, Is.Not.Null, "左の背景がない（M2）");
                Assert.That(panel.Paper.r + panel.Paper.g + panel.Paper.b, Is.GreaterThan(2.7f), "左の背景が白地でない");
                Assert.That(panel.Index, Is.EqualTo(3), "左の背景の帯が選んだ項目に合わない");
                Color bandColor = panel.BandColor;
                Assert.That(new Vector3(bandColor.r, bandColor.g, bandColor.b),
                    Is.EqualTo(new Vector3(menu.Training.Accent.r, menu.Training.Accent.g, menu.Training.Accent.b)), "左の背景の帯が項目の色でない");
                UnityEngine.UI.Text logo = backdrop.transform.Find("Logo").GetComponent<UnityEngine.UI.Text>();
                Assert.That(logo.color.r + logo.color.g + logo.color.b, Is.LessThan(0.5f), "左のロゴが黒でない");
                Assert.That(menu.Single.Plate.Back, Is.LessThan(0.1f), "選んでいない項目の裏の札が残っている");
                Assert.That(backdrop.Word, Is.EqualTo("TRAINING"), "背景の名前が変わらない（B7）");
                Assert.That(backdrop.Description, Is.Not.Empty, "背景に説明を出さない（B7）");
                // 説明は白地に黒の文字の説明の札。縁は項目の色（ステージ選択と同じ。2026-11-08）。
                SlantedPlate descriptionPlate = backdrop.DescriptionPlate.Plate;
                Assert.That(descriptionPlate.color.r + descriptionPlate.color.g + descriptionPlate.color.b, Is.GreaterThan(2.7f), "説明の札が白地でない");
                Assert.That(new Vector3(descriptionPlate.BorderColor.r, descriptionPlate.BorderColor.g, descriptionPlate.BorderColor.b),
                    Is.EqualTo(new Vector3(menu.Training.Accent.r, menu.Training.Accent.g, menu.Training.Accent.b)), "説明の札の縁が項目の色でない");
                Color descriptionInk = backdrop.DescriptionPlate.Text.color;
                Assert.That(descriptionInk.r + descriptionInk.g + descriptionInk.b, Is.LessThan(0.5f), "説明の文字が黒でない");
                // 項目の色（2026-11-07）: 札の縁・裏の札・縁を回る光、右の見出しの札の縁と裏の札が項目の色。背景の色の光はない。
                Color accent = menu.Training.Accent;
                Assert.That(accent, Is.Not.EqualTo(menu.Single.Accent), "項目ごとに色が違わない");
                Assert.That(menu.Training.Plate.AccentColor, Is.EqualTo(accent), "裏の札が項目の色でない");
                Assert.That(menu.Training.Plate.BorderColor, Is.EqualTo(accent), "選んだ札の縁が項目の色でない");
                Assert.That(menu.Training.Plate.FuseColor, Is.Not.EqualTo(Color.white), "縁を回る光が項目の色でない");
                // 右の見出しは白地に黒の文字の札、縁と裏の札は項目の色（2026-11-08）。
                HeadingPlate heading = backdrop.Heading;
                Assert.That(heading, Is.Not.Null, "右の見出しの札がない");
                Color outline = heading.Plate.BorderColor;
                Assert.That(new Vector3(outline.r, outline.g, outline.b), Is.EqualTo(new Vector3(accent.r, accent.g, accent.b)), "見出しの札の縁が項目の色でない");
                Assert.That(heading.Plate.AccentColor, Is.EqualTo(heading.Plate.BorderColor), "見出しの裏の札が項目の色でない");
                Assert.That(heading.Plate.Back, Is.GreaterThan(0.9f), "見出しの裏の札が出ない");
                Color face = heading.Plate.color;
                Assert.That(face.r + face.g + face.b, Is.GreaterThan(2.7f), "見出しの札が白地でない");
                Color word = heading.TextColor;
                Assert.That(word.r + word.g + word.b, Is.LessThan(0.5f), "見出しの文字が黒でない");
                // 左の絵は項目の色（2026-11-07）。
                Color ink = menu.Training.GetComponentInChildren<MenuIcon>().Ink;
                Assert.That(Mathf.Abs(ink.g - accent.g) < 0.2f && Mathf.Abs(ink.b - accent.b) < 0.2f, Is.True, "左の絵が項目の色でない");
                Assert.That(backdrop.transform.Find("Backdrop/Tint"), Is.Null, "背景の色の光が残っている");
                Assert.That(menu.Local.Label, Is.EqualTo("LOCAL"), "LOCAL VS を LOCAL に縮めていない");
                Assert.That(menu.Network.Label, Is.EqualTo("NETWORK"), "NETWORK VS を NETWORK に縮めていない");

                // クリックした所の火花（2026-11-07）: メニューの最前面の層が、押した所に輪と火花を出す（何もない所でも）。
                ClickSparks sparks = menu.GetComponentInChildren<ClickSparks>();
                Assert.That(sparks, Is.Not.Null, "クリックの火花の層がない");
                Assert.That(sparks.transform.GetSiblingIndex(), Is.EqualTo(sparks.transform.parent.childCount - 1), "クリックの火花が最前面にない");
                Assert.That(sparks.raycastTarget, Is.False, "クリックの火花の層がクリックを受け止める");
                sparks.Spawn(new Vector2(Screen.width * 0.7f, Screen.height * 0.3f));
                Assert.That(sparks.Playing, Is.EqualTo(1), "クリックした所に火花を出さない");

                // メイン → キャラクター選択: 決定の衝撃と、斜めの札とアイコンのめくれ。
                menu.Training.Button.onClick.Invoke();
                Assert.That(new[] { sounds.History[sounds.History.Count - 2], sounds.History[sounds.History.Count - 1] },
                    Is.EquivalentTo(new[] { SoundId.UiConfirm, SoundId.UiWhoosh }), "決定の音と進む風の音が鳴らない");
                Assert.That(menu.ShowingCharacterSelect, Is.True, "決定ですぐにキャラクター選択へ入れ替わらない");
                Assert.That(transition.LastKind, Is.EqualTo(TransitionKind.SlantCascade));
                Assert.That(menu.Training.Burst, Is.Not.Null, "決定しても輪と火花が出ない（B10）");
                // 出ていくメインメニューの写しでも、小さな説明（W16）は選んでいた TRAINING の札にだけ出る（2026-11-08 の指摘）。
                GameObject leaving = GameObject.Find("Main (Transition)");
                Assert.That(leaving, Is.Not.Null, "メインメニューの写しがない");
                int subsShown = 0;
                foreach (UnityEngine.UI.Text text in leaving.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (text.name == "Sub" && text.color.a > 0.5f)
                    {
                        subsShown++;
                    }
                }
                Assert.That(subsShown, Is.LessThanOrEqualTo(1), "切り替えの写しで、選んでいない札にも小さな説明が出ている（W16）");
                yield return null;
                yield return null;
                yield return null;
                GameObject band = GameObject.Find("Band");
                Assert.That(band, Is.Not.Null, "斜めの札がない");
                // 斜めの札の 1 枚目は選んだモードのイメージカラー（2026-11-07）。
                bool modeColored = false;
                foreach (SlantedPlate plate in band.transform.parent.GetComponentsInChildren<SlantedPlate>())
                {
                    modeColored |= plate.name == "Band" && plate.color == accent;
                }
                Assert.That(modeColored, Is.True, "斜めの札がモードの色でない");
                yield return WaitFor(() => !transition.Running, 3f, "キャラクター選択への切り替えが終わらない");
                yield return null;
                Assert.That(CopiesLeft(), Is.Zero, "演出が終わっても写しが残っている");
                var group = menu.CharacterSelect.GetComponent<CanvasGroup>();
                Assert.That(group == null || group.alpha >= 0.999f, Is.True, "演出が終わってもキャラクター選択が透明のまま");

                // キャラクター選択の演出（D-89）: 演出の部品があり、カーソルの枠が光り、乗せたアイコンのロゴが持ち上がり色が流れ込む。
                var motion = menu.CharacterSelect.GetComponent<CharacterSelectMotion>();
                Assert.That(motion, Is.Not.Null, "キャラクター選択の演出がない");
                int hovered = menu.CharacterSelect.CursorIndex(0);
                yield return WaitSeconds(0.35f);
                Assert.That(motion.PopOf(hovered), Is.GreaterThan(0.8f), "乗せたアイコンのロゴが持ち上がらない（C1）");
                Assert.That(motion.FloodOf(hovered), Is.GreaterThan(0.8f), "乗せたアイコンに色が流れ込まない（C2）");
                Assert.That(menu.CharacterSelect.CursorRect(0).GetComponent<SlantedPlate>().Glow, Is.True, "カーソルの枠が光らない（C4 の代わり）");

                // 戻る: メインメニューへも斜めの札（「MAIN MENU」、水色、逆向き。2026-11-07）。
                yield return Tap(keyboard, Key.Escape);
                Assert.That(menu.ShowingMain, Is.True, "キャラクター選択から戻れない");
                Assert.That(transition.LastKind, Is.EqualTo(TransitionKind.SlantCascade), "メインメニューへ戻るときに斜めの札でない");
                Assert.That(transition.LastDirection, Is.EqualTo(-1), "戻るときに向きが逆にならない");
                Assert.That(sounds.History, Does.Contain(SoundId.UiWhooshBack), "戻るときに戻る風の音が鳴らない");
                yield return null;
                yield return null;
                yield return null;
                bool mainLabel = false;
                foreach (UnityEngine.UI.Text label in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
                {
                    mainLabel |= label.name == "Label" && label.text == "MAIN MENU";
                }
                Assert.That(mainLabel, Is.True, "戻るときの斜めの札に MAIN MENU がない");
                yield return WaitFor(() => !transition.Running, 3f, "戻る切り替えが終わらない");

                // 動いている途中に押すと、最後まで早送りする（押した入力は画面がそのまま受け取る）。
                menu.Training.Button.onClick.Invoke();
                yield return null;
                yield return null;
                yield return null;
                Assert.That(transition.Running, Is.True);
                yield return Tap(keyboard, Key.LeftShift);
                yield return null;
                Assert.That(transition.Running, Is.False, "押しても早送りしない");
                yield return null;
                Assert.That(CopiesLeft(), Is.Zero, "早送りしても写しが残っている");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        [UnityTest]
        public IEnumerator CharacterSelect_LockRollAndCancel_PlayTheirEffects()
        {
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                SceneManager.LoadScene("App");
                yield return null;
                var flow = Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = Object.FindFirstObjectByType<MenuScreens>();
                menu.ContinueFromTitle();
                menu.Transition.Complete();
                menu.Training.Button.onClick.Invoke();
                menu.Transition.Complete();
                yield return null;
                yield return null;
                CharacterSelectScreen characters = menu.CharacterSelect;
                var motion = characters.GetComponent<CharacterSelectMotion>();

                // 決める: ロゴが枠へ飛び、判が押される（C12・C13）。決定の演出の間はまだキャラクター選択のまま。
                yield return Tap(keyboard, Key.Enter);
                Assert.That(characters.IsReady(0), Is.True, "決めても READY にならない");
                Assert.That(menu.ShowingCharacterSelect, Is.True, "決定の演出を見せずにすぐ進んだ");
                Assert.That(motion.FlyingLogos, Is.GreaterThan(0), "ロゴが枠へ飛ばない（C13）");
                Assert.That(motion.StampShown(0), Is.True, "READY! の判を出さない（C12）");

                // 取り消す: 判がはがれ、ロゴが戻る（C20）。進まない。
                yield return Tap(keyboard, Key.Escape);
                Assert.That(characters.IsReady(0), Is.False, "取り消せない");
                Assert.That(motion.FlyingLogos, Is.GreaterThan(0), "取り消してもロゴが札へ戻らない（C20）");
                yield return WaitSeconds(CharacterSelectScreen.SingleHoldSeconds + 0.3f);
                Assert.That(menu.ShowingCharacterSelect, Is.True, "取り消したのに次の画面へ進んだ");
                Assert.That(motion.StampShown(0), Is.False, "取り消しても判が残っている");

                // RANDOM: カーソルがアイコンの上を回ってから止まり、そのキャラクターで決まる（C18）。
                for (int i = 0; i < 4; i++)
                {
                    yield return Tap(keyboard, Key.DownArrow);
                    if (characters.CharacterOf(0) == characters.RandomIndex)
                    {
                        break;
                    }
                }
                Assert.That(characters.CharacterOf(0), Is.EqualTo(characters.RandomIndex), "前提: RANDOM にいる");
                yield return Tap(keyboard, Key.Enter);
                Assert.That(characters.IsRolling(0), Is.True, "RANDOM で決めてもルーレットが回らない");
                yield return WaitFor(() => !characters.IsRolling(0), 5f, "ルーレットが止まらない");
                Assert.That(characters.CharacterOf(0), Is.LessThan(characters.RandomIndex), "ルーレットが止まってもキャラクターが決まらない");
                Assert.That(characters.IsReady(0), Is.True, "ルーレットが止まっても READY にならない");
                yield return WaitFor(() => menu.ShowingStageSelect, 3f, "ルーレットで決めてもステージ選択へ進まない");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        [UnityTest]
        public IEnumerator Options_UseTheOptionsColor()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var menu = Object.FindFirstObjectByType<MenuScreens>();
            menu.ContinueFromTitle();
            menu.Transition.Complete();
            Color optionsColor = menu.Options.Accent;
            menu.Options.Button.onClick.Invoke();
            menu.Transition.Complete();
            yield return null;
            int count = 0;
            foreach (MenuOption option in Object.FindFirstObjectByType<OptionsScreen>().GetComponentsInChildren<MenuOption>(true))
            {
                Assert.That(option.Accent, Is.EqualTo(optionsColor), $"{option.name} が OPTIONS の色でない");
                count++;
            }
            Assert.That(count, Is.GreaterThan(3), "オプションの札がない");
            // 以前は水色だった文字も OPTIONS の色（2026-11-08）。
            foreach (UnityEngine.UI.Text text in Object.FindFirstObjectByType<OptionsScreen>().GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (text.name == "KeyboardHeader" || text.name == "GamepadHeader" || text.name == "Prompt")
                {
                    // 白い背景の上なので、OPTIONS の色を黒に寄せた色（2026-11-08）。
                    Assert.That(SameHue(text.color, AccentTint.OnWhite(optionsColor)), Is.True, $"{text.name} の文字が OPTIONS の色でない");
                }
            }
        }

        /// <summary>
        /// 画面のイメージカラー（2026-11-08）: TRAINING から入ったキャラクター選択・ステージ選択の、以前は水色だった文字は TRAINING の色。
        /// ステージ選択の見出しは白地に黒の文字の札で、縁と裏の札はカーソルのあるステージの色。ステージの札の縁はそのステージの色。
        /// </summary>
        [UnityTest]
        public IEnumerator Screens_UseTheColorOfTheModeTheyCameFrom()
        {
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                SceneManager.LoadScene("App");
                yield return null;
                var flow = Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = Object.FindFirstObjectByType<MenuScreens>();
                menu.ContinueFromTitle();
                menu.Transition.Complete();
                Color training = menu.Training.Accent;
                menu.Training.Button.onClick.Invoke();
                menu.Transition.Complete();
                yield return null;
                yield return null;
                CharacterSelectScreen characters = menu.CharacterSelect;
                Assert.That(characters.Accent, Is.EqualTo(training), "キャラクター選択がモードの色でない");
                int tinted = 0;
                foreach (AccentTint tint in characters.GetComponentsInChildren<AccentTint>(true))
                {
                    // 白い背景は帯の色、白い背景の上の文字はモードの色を黒に寄せた色（2026-11-08）。
                    MainMenuPanel white = tint.GetComponent<MainMenuPanel>();
                    Color actual = white != null ? white.BandColor : tint.GetComponent<UnityEngine.UI.Graphic>().color;
                    Assert.That(SameHue(actual, tint.Expected(training)), Is.True, $"{tint.name} がモードの色でない");
                    tinted++;
                }
                Assert.That(tinted, Is.GreaterThan(3), "キャラクター選択に色を変える文字がない");

                yield return Tap(keyboard, Key.Enter);
                yield return WaitFor(() => menu.ShowingStageSelect, CharacterSelectScreen.SingleHoldSeconds + 3f, "ステージ選択へ進まない");
                menu.Transition.Complete();
                yield return null;
                yield return null;
                StageSelectScreen stages = menu.StageSelect;
                Assert.That(stages.Accent, Is.EqualTo(training), "ステージ選択がモードの色でない");
                Assert.That(stages.transform.Find("Band"), Is.Null, "ステージ選択の見出しの帯が残っている");
                HeadingPlate heading = stages.Heading;
                Assert.That(heading, Is.Not.Null, "ステージ選択の見出しの札がない");
                Assert.That(stages.Highlighted, Is.Not.Null, "前提: ステージにカーソルがある");
                Color stageColor = stages.Highlighted.Color;
                Assert.That(heading.Word, Is.EqualTo(stages.Highlighted.DisplayName));
                Assert.That(SameHue(heading.Plate.BorderColor, stageColor), Is.True, "見出しの札の縁がステージの色でない");
                Color word = heading.TextColor;
                Assert.That(word.r + word.g + word.b, Is.LessThan(0.5f), "見出しの文字が黒でない");
                for (int i = 0; i < stages.Count; i++)
                {
                    Color own = flow.Stages.Get(i).Color;
                    Assert.That(SameHue(stages.Option(i).Accent, own), Is.True, $"{stages.Option(i).name} の縁がステージの色でない");
                }
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        // 色の RGB が同じか（濃さは問わない）。
        private static bool SameHue(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        [UnityTest]
        public IEnumerator Training_ClosesTheCurtain_ThenOpensIt_AfterLoading()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var curtain = Object.FindFirstObjectByType<ScreenCurtain>();
            Assert.That(curtain, Is.Not.Null, "試合へ入るときの黒い帯がない");
            flow.StartTraining(flow.Stages.Get(0));
            yield return null;
            Assert.That(curtain.Showing, Is.True, "試合へ入るときに黒い帯を出さない");
            Assert.That(NavigationGate.Current.Held, Is.True, "帯を閉じている間にメニューの決定を止めない");
            yield return WaitSeconds(0.3f);
            Assert.That(flow.LoadedScenes, Does.Contain("Menu"), "帯が閉じる前にメニューを外した");
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 15f, "トレーニングにならない");
            yield return WaitFor(() => !curtain.Showing, 2f, "読み込みが済んでも黒い帯が開かない");
            Assert.That(NavigationGate.Current.Held, Is.False, "帯が開いてもメニューの決定を止めたまま");
            Assert.That(EventSystem.current.sendNavigationEvents, Is.True);
        }

        [UnityTest]
        public IEnumerator UiSounds_UseTheNewCursorAndConfirmFiles()
        {
            SceneManager.LoadScene("App");
            yield return null;
            // メニューの読み込みが済むまで待つ（読み込みの途中で終えると、次のテストの App の上に前のメニューのシーンが残る）。
            var flow = Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var sounds = Object.FindFirstObjectByType<UiSounds>();
            Assert.That(sounds, Is.Not.Null);
            Assert.That(sounds.ClipOf(SoundId.UiMove).name, Is.EqualTo("UiCursor"), "カーソルの音がダイヤルのカチッでない");
            Assert.That(sounds.ClipOf(SoundId.UiConfirm).name, Is.EqualTo("UiConfirm"), "決定の音が新しい音でない");
            Assert.That(sounds.ClipOf(SoundId.UiTitleStart).name, Is.EqualTo("TitleStart"));
            Assert.That(sounds.ClipOf(SoundId.UiCharacterDecide).name, Is.EqualTo("CharacterDecide"));
            Assert.That(sounds.ClipOf(SoundId.UiMove).length, Is.LessThan(0.1f), "カーソルの音が長い");
            Assert.That(sounds.ClipOf(SoundId.UiWhoosh).name, Is.EqualTo("UiWhoosh"), "進む風の音がない");
            Assert.That(sounds.ClipOf(SoundId.UiWhooshBack).name, Is.EqualTo("UiWhooshBack"), "戻る風の音がない");
        }

        // 演出の写し（名前の終わりが「(Transition)」）の数。
        private static int CopiesLeft()
        {
            int count = 0;
            foreach (RectTransform rect in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rect.name.EndsWith("(Transition)", StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
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

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
            }
        }
    }
}
