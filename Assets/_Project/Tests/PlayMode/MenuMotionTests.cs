using System;
using System.Collections;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Data;
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
                // 白い背景と試合の画面の境目は傾ける（D-92。2026-11-09）。
                Assert.That(panel.Edges, Is.EqualTo(MainMenuPanel.EdgeSides.Right), "メインメニューの白い背景の右の端が境目になっていない");
                // 境目の模様は D-94 の H19「ぬいめ」の手直し（2026-11-14）: 白い背景の内側に縫い目を 1 列だけ。光る粒と外の 2 列はなくした。
                Assert.That(MainMenuPanel.OuterStitchCount, Is.EqualTo(0), "境目の外に縫い目がある");
                Assert.That(MainMenuPanel.InnerStitchCount, Is.EqualTo(1), "白い背景の内側の縫い目が 1 列でない");
                // 縫い目の線は境目と同じ向き（線の両端の丸みの分で傾きがずれていた。2026-11-14 の指摘）。
                MainMenuPanel.StitchEnds(0f, 1f, 0f, 16f, 10f, 26f, 5f, out Vector2 dashA, out Vector2 dashB);
                Assert.That((dashB.x - dashA.x) / (dashB.y - dashA.y), Is.EqualTo(MainMenuPanel.EdgeLean).Within(0.0001f), "縫い目の傾きが境目と違う");
                // 境目はぼかす（くっきりしすぎて浮いて見えないように。縫い目はぼかさない。2026-11-14）。
                Assert.That(MainMenuPanel.EdgeFeather, Is.InRange(2f, 6f), "境目のぼかしがないか、強すぎる（16・10 では強すぎた。2026-11-16）");
                // ロゴは白に項目の色の縁取りと前の菱形（D-92 の T2・T7）。縁の色は選んだ項目の色へ移る。
                UnityEngine.UI.Text logo = backdrop.transform.Find("Logo").GetComponent<UnityEngine.UI.Text>();
                Assert.That(logo.color.r + logo.color.g + logo.color.b, Is.GreaterThan(2.9f), "左のロゴが白でない");
                Assert.That(logo.text, Is.EqualTo("MAIN MENU"), "メインメニューの見出しが MAIN MENU でない（2026-11-12）");
                Assert.That(backdrop.Logo, Is.Not.Null, "左のロゴに縁取りがない");
                yield return new WaitForSecondsRealtime(TitleStroke.AccentSeconds + 0.05f);
                Color logoAccent = backdrop.Logo.Accent;
                Assert.That(new Vector3(logoAccent.r, logoAccent.g, logoAccent.b),
                    Is.EqualTo(new Vector3(menu.Training.Accent.r, menu.Training.Accent.g, menu.Training.Accent.b)), "ロゴの縁が選んだ項目の色に移らない");
                TitleMark logoMark = logo.GetComponentInChildren<TitleMark>();
                Assert.That(logoMark != null && logoMark.Kind == TitleMark.Shape.Diamond, Is.True, "ロゴの前に菱形の印がない");
                Assert.That(logoMark.TryGetTextStart(out float logoLeft, out _), Is.True);
                Assert.That(logoLeft, Is.EqualTo(logo.rectTransform.rect.xMin).Within(12f), "菱形の印の基準がロゴの左端にない（文字が動いた）");
                Assert.That(menu.Single.Plate.Back, Is.LessThan(0.1f), "選んでいない項目の裏の札が残っている");
                Assert.That(backdrop.Word, Is.EqualTo("TRAINING"), "背景の名前が変わらない（B7）");
                Assert.That(backdrop.Description, Is.Not.Empty, "背景に説明を出さない（B7）");
                // 説明は白地に黒の文字の説明の札。縁は項目の色（ステージ選択と同じ。2026-11-08）。
                SlantedPlate descriptionPlate = backdrop.DescriptionPlate.Plate;
                Assert.That(descriptionPlate.color.r + descriptionPlate.color.g + descriptionPlate.color.b, Is.GreaterThan(2.7f), "説明の札が白地でない");
                Assert.That(new Vector3(descriptionPlate.BorderColor.r, descriptionPlate.BorderColor.g, descriptionPlate.BorderColor.b),
                    Is.EqualTo(new Vector3(menu.Training.Accent.r, menu.Training.Accent.g, menu.Training.Accent.b)), "説明の札の縁が項目の色でない");
                Color descriptionInk = backdrop.DescriptionPlate.Text.color;
                Assert.That(descriptionInk.r + descriptionInk.g + descriptionInk.b, Is.InRange(0.3f, 0.75f), "説明の文字が墨色でない（D-92 の T1）");
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
                Assert.That(word.r + word.g + word.b, Is.InRange(0.3f, 0.75f), "見出しの文字が墨色でない（D-92 の T1）");
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
                Assert.That(menu.CharacterSelect.CursorRect(0).GetComponent<SlantedPlate>().Glow, Is.True, "カーソルの印が光らない（C4 の代わり）");
                // カーソルは札を囲む枠ではなく、選んでいる名前の札の左に置くプレイヤーの色の印（2026-11-16）。
                var cursorCorners = new Vector3[4];
                var plateCorners = new Vector3[4];
                menu.CharacterSelect.CursorRect(0).GetWorldCorners(cursorCorners);
                menu.CharacterSelect.Icon(hovered).Rect.GetWorldCorners(plateCorners);
                // 印も札も斜めなので、高さの真ん中の斜めの縁どうしで比べる。
                float plateLeftMid = plateCorners[0].x + 0.35f * (plateCorners[1].y - plateCorners[0].y) * 0.5f;
                float cursorRightMid = cursorCorners[2].x - 0.35f * (cursorCorners[1].y - cursorCorners[0].y) * 0.5f;
                Assert.That(cursorRightMid, Is.LessThan(plateLeftMid), "カーソルの印が名前の札の左にない（札に重なる）");
                Assert.That(cursorCorners[0].y, Is.GreaterThanOrEqualTo(plateCorners[0].y - 1f), "カーソルの印が札の高さからはみ出す");
                Assert.That(cursorCorners[1].y, Is.LessThanOrEqualTo(plateCorners[1].y + 1f), "カーソルの印が札の高さからはみ出す");
                Assert.That(menu.CharacterSelect.CursorRect(0).GetComponent<SlantedPlate>().color.a, Is.GreaterThan(0.9f), "カーソルの印がプレイヤーの色で塗られていない");

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
                for (int i = 0; i < characters.IconCount; i++)
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
            // メインメニューのボタンの演出（小さな説明を除く）は、どの画面のボタンにも 1 つずつ付いている（2026-11-12）。
            foreach (MenuOption option in menu.GetComponentsInChildren<MenuOption>(true))
            {
                Assert.That(option.Chevrons, Is.Not.Null, $"{option.name} に「›››」がない");
                Assert.That(option.GetComponentsInChildren<ChevronMarks>(true).Length, Is.EqualTo(1), $"{option.name} の「›››」が重なっている");
                Assert.That(option.Recedes, Is.True, $"{option.name} が、ほかの札を選んでいる間に下がらない");
                Assert.That(option.AccentFuse, Is.True, $"{option.name} の導火線が札の色でない");
            }
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
                    // 縁取りした見出し（D-92 の T2）は、縁の色がモードの色（文字は白のまま）。
                    MainMenuPanel white = tint.GetComponent<MainMenuPanel>();
                    TitleStroke stroke = tint.GetComponent<TitleStroke>();
                    Color actual = white != null ? white.BandColor : stroke != null ? stroke.TargetAccent : tint.GetComponent<UnityEngine.UI.Graphic>().color;
                    Assert.That(SameHue(actual, tint.Expected(training)), Is.True, $"{tint.name} がモードの色でない");
                    tinted++;
                }
                Assert.That(tinted, Is.GreaterThan(3), "キャラクター選択に色を変える文字がない");
                Assert.That(characters.GetComponentInChildren<TitleStroke>(true), Is.Not.Null, "キャラクター選択の見出しに縁取りがない");
                // 縁と影は文字の写しを周りにずらして重ねて作る。写しが少ないと斜めの辺で段（ギザギザ）になる（2026-11-12 の指摘）。
                Assert.That(TitleStroke.CopiesPerLayer, Is.GreaterThanOrEqualTo(40), "縁と影の写しが少ない（斜めの辺がギザギザになる）");
                // 配置は D-95 の L3 × G1（2026-11-14）: 左の白い背景（右の端だけが境目）にキャラクターの名前の札を縦に並べ、白い背景の境目と
                // 同じ向きに傾けて上の札ほど右へずらす。右にカーソルのキャラクターを大きく出す。
                MainMenuPanel backdrop = characters.GetComponentInChildren<MainMenuPanel>(true);
                Assert.That(backdrop.Edges, Is.EqualTo(MainMenuPanel.EdgeSides.Right), "キャラクター選択の白い背景の右の端が境目になっていない");
                Assert.That(backdrop.rectTransform.anchorMax.x, Is.EqualTo(0f), "キャラクター選択の白い背景が画面の左にない");
                Assert.That(backdrop.rectTransform.rect.width, Is.InRange(500f, 800f), "キャラクター選択の白い背景の幅");
                // 名前の札はまっすぐ縦に並べ（境目の向きに合わせない）、札の左の縁を見出しの 1 文字目の線にそろえる（2026-11-14）。
                for (int i = 1; i < characters.IconCount; i++)
                {
                    Assert.That(characters.IconHome(i).y, Is.LessThan(characters.IconHome(i - 1).y - 40f), "名前の札が縦に並んでいない");
                    Assert.That(characters.IconHome(i).x, Is.EqualTo(characters.IconHome(0).x).Within(0.01f), "名前の札がまっすぐ並んでいない");
                }
                var corners = new Vector3[4];
                ((RectTransform)characters.transform.Find("Title")).GetWorldCorners(corners);
                float titleLeft = corners[0].x;
                float pixel = characters.GetComponentInParent<Canvas>().transform.lossyScale.x;
                characters.Icon(characters.IconCount - 1).Rect.GetWorldCorners(corners);
                Assert.That(corners[0].x - titleLeft, Is.EqualTo(pixel).Within(2f * pixel), "名前の札の左の縁が見出しの 1 文字目の線にない");
                // 名前の札はメインメニューの札と同じ演出（画面が選ぶ MenuOption）。カーソルの札だけを選んだ見た目にする。
                int cursorIcon = characters.CursorIndex(0);
                MenuOption iconOption = characters.Icon(cursorIcon).GetComponent<MenuOption>();
                Assert.That(iconOption, Is.Not.Null, "名前の札にメインメニューの札の演出がない");
                Assert.That(iconOption.External && iconOption.IsSelected, Is.True, "カーソルの名前の札が選んだ見た目でない");
                Assert.That(iconOption.Chevrons, Is.Not.Null, "名前の札に「›››」がない");
                Assert.That(iconOption.Sub, Is.Null, "名前の札に説明（W16）がある");
                Assert.That(characters.Icon((cursorIcon + 1) % characters.IconCount).GetComponent<MenuOption>().IsSelected, Is.False, "カーソルでない札も選んだ見た目");
                // 選んだ札に流れ込む色（C2）は縁の内側だけで、縁は中より濃い色（境目が見える。2026-11-16）。
                for (int i = 0; i < characters.IconCount; i++)
                {
                    CharacterConfigAsset each = characters.CharacterAt(i);
                    if (each == null)
                    {
                        continue;
                    }
                    MenuOption eachOption = characters.Icon(i).GetComponent<MenuOption>();
                    var flood = (RectTransform)characters.Icon(i).Rect.Find("Flood");
                    Assert.That(flood, Is.Not.Null, $"{each.DisplayName} の札に色の流れ込みがない");
                    Assert.That(flood.offsetMin.y, Is.EqualTo(eachOption.Plate.BorderWidth).Within(0.01f), $"{each.DisplayName} の流れ込む色が縁まで覆う");
                    Assert.That(flood.offsetMin.x, Is.GreaterThan(eachOption.Plate.BorderWidth), $"{each.DisplayName} の流れ込む色が斜めの縁まで覆う");
                    if (i == cursorIcon)
                    {
                        Color.RGBToHSV(eachOption.Plate.BorderColor, out _, out _, out float borderValue);
                        Color.RGBToHSV(each.ThemeColor, out _, out _, out float themeValue);
                        Assert.That(borderValue, Is.LessThan(themeValue - 0.1f), $"{each.DisplayName} の選んだ札の縁が中の色より濃くない");
                    }
                }
                // 白い背景はメインメニューと同じで、帯はカーソルのキャラクターの位置にある（色は画面のイメージカラー）。
                Assert.That(characters.BackdropIndex, Is.EqualTo(cursorIcon), "白い背景の帯がカーソルのキャラクターの位置にない");
                Assert.That(SameHue(backdrop.BandColor, training), Is.True, "白い背景の帯が画面のイメージカラーでない");

                Assert.That(characters.ShowcaseShown, Is.True, "右の大きな表示がない");
                CharacterConfigAsset focused = characters.CharacterAt(characters.CursorIndex(0));
                Assert.That(characters.ShowcaseName, Is.EqualTo(focused != null ? focused.DisplayName : "RANDOM"), "右の大きな表示がカーソルのキャラクターでない");
                Assert.That(characters.ShowcaseSkill(0), Is.Not.Empty, "右の大きな表示に技の名前がない");
                // ハッシュタグはモードのイメージカラー。だれが選んでいるかと操作の案内は右に出さない（下の札と下の帯にある。2026-11-16）。
                Assert.That(SameHue(characters.ShowcaseTagsColor, training), Is.True, "右のハッシュタグがモードの色でない");
                Assert.That(characters.transform.Find("ShowcaseArea/Showcase/Player"), Is.Null, "右に「1P SELECTING」が残っている");
                Assert.That(characters.transform.Find("ShowcaseArea/Message"), Is.Null, "右に操作の案内が残っている");
                // ロゴは大きく出してもぼやけない大きさ（数式の形から 1024 ピクセルで描く。2026-11-14）。
                if (focused != null)
                {
                    Assert.That(focused.Logo.width, Is.GreaterThanOrEqualTo(1024), "ロゴの絵が小さい（大きく出すとぼやける）");
                }
                // 右は名前 → ロゴ → 技の札の順に、同じ縦の線に右寄せ（2026-11-14）。技の札もメインメニューの札と同じ演出。
                yield return new WaitForSecondsRealtime(0.6f);
                Transform showcase = characters.transform.Find("ShowcaseArea/Showcase");
                ((RectTransform)showcase.Find("Name")).GetWorldCorners(corners);
                float nameRight = corners[2].x;
                float nameBottom = corners[0].y;
                ((RectTransform)showcase.Find("Diamond")).GetWorldCorners(corners);
                float diamondTop = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
                float diamondRight = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
                float diamondBottom = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
                Assert.That(diamondRight, Is.EqualTo(nameRight).Within(4f * pixel), "ロゴが名前と同じ縦の線に右寄せでない");
                Assert.That(diamondTop, Is.LessThan(nameBottom), "ロゴが名前の下にない");
                foreach (string key in new[] { "Q", "E", "R" })
                {
                    var row = (RectTransform)showcase.Find($"Skills/Skill{key}");
                    MenuOption rowOption = row.GetComponent<MenuOption>();
                    Assert.That(rowOption != null && rowOption.IsSelected, Is.True, $"技の札 {key} にメインメニューの札の演出がない");
                    // 技の札だけ、縁が光る演出（導火線・明滅）を出さない（2026-11-16）。
                    Assert.That(rowOption.EdgeLight, Is.False, $"技の札 {key} の縁が光る");
                    Assert.That(rowOption.Plate.FuseRunning || rowOption.Plate.Glow, Is.False, $"技の札 {key} の縁が光っている");
                    row.GetWorldCorners(corners);
                    Assert.That(corners[2].x, Is.EqualTo(nameRight).Within(4f * pixel), $"技の札 {key} が名前と同じ縦の線に右寄せでない");
                    Assert.That(corners[1].y, Is.LessThan(diamondBottom + 4f * pixel), $"技の札 {key} がロゴの下にない");
                }
                // 下のプレイヤーの札（トレーニングは 1P だけ）も同じ縦の線に右寄せ。
                characters.CornerRect(0).GetWorldCorners(corners);
                Assert.That(corners[2].x, Is.EqualTo(nameRight).Within(6f * pixel), "プレイヤーの札が同じ縦の線に右寄せでない");

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
                Assert.That(word.r + word.g + word.b, Is.InRange(0.3f, 0.75f), "見出しの文字が墨色でない（D-92 の T1）");
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

        /// <summary>
        /// NETWORK からメインメニューへ戻るときも、ほかの画面と同じ逆向きの風で切り替わる（出ていく接続の画面の写しが動く。2026-11-08 の指摘）。
        /// </summary>
        [UnityTest]
        public IEnumerator Network_BackToMain_UsesTheBackTransition()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var menu = Object.FindFirstObjectByType<MenuScreens>();
            ScreenTransition transition = menu.Transition;
            menu.ContinueFromTitle();
            transition.Complete();
            yield return null;
            menu.Network.Button.onClick.Invoke();
            transition.Complete();
            yield return null;
            var online = Object.FindFirstObjectByType<Airerage.Net.OnlineMenu>();
            Assert.That(online.IsOpen, Is.True, "前提: ネットワーク対戦の画面が開いている");
            online.BackButton.Button.onClick.Invoke();
            Assert.That(menu.ShowingMain, Is.True, "BACK でメインメニューへ戻らない");
            Assert.That(online.IsOpen, Is.False, "BACK でネットワーク対戦の画面が閉じない");
            Assert.That(transition.Running, Is.True, "メインメニューへ戻るときに画面の切り替えが動かない");
            Assert.That(transition.LastKind, Is.EqualTo(TransitionKind.Gust), "戻るときに風でない");
            Assert.That(transition.LastDirection, Is.EqualTo(-1), "戻るときに向きが逆にならない");
            Assert.That(GameObject.Find("OnlineConnect (Transition)"), Is.Not.Null, "出ていく接続の画面の写しがない");
            yield return WaitFor(() => !transition.Running, 3f, "切り替えが終わらない");
            yield return null;
            Assert.That(CopiesLeft(), Is.Zero, "演出が終わっても写しが残っている");
        }

        /// <summary>
        /// 日本語入力（IME）は、文字の入力欄に入力している間だけ有効（2026-11-16 の指摘: 入力欄の外で IME の窓が左上に出ていた）。
        /// </summary>
        [UnityTest]
        public IEnumerator Ime_IsOnlyEnabledWhileTypingInAField()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                SceneManager.LoadScene("App");
                yield return null;
                var flow = Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var guard = Object.FindFirstObjectByType<ImeGuard>();
                Assert.That(guard, Is.Not.Null, "いつも読み込むシーンに IME の制御がない");
                var menu = Object.FindFirstObjectByType<MenuScreens>();
                menu.ContinueFromTitle();
                menu.Transition.Complete();
                yield return null;
                yield return null;
                Assert.That(guard.ImeEnabled, Is.False, "メニューで IME が有効のまま");
                Assert.That(Input.imeCompositionMode, Is.EqualTo(IMECompositionMode.Off), "メニューで古い入力の IME が切れていない（エディタで窓が残る）");
                // ほか（エディタ）が毎フレーム書き戻しても、毎フレーム書き合わない（2026-11-17 の指摘: ウインドウを戻すとたまに止まる）。
                int writes = guard.LegacyWrites;
                float started = Time.unscaledTime;
                for (int i = 0; i < 20; i++)
                {
                    Input.imeCompositionMode = IMECompositionMode.Auto;
                    yield return null;
                }
                int allowed = 1 + Mathf.FloorToInt((Time.unscaledTime - started) / ImeGuard.RetrySeconds);
                Assert.That(guard.LegacyWrites - writes, Is.LessThanOrEqualTo(allowed), "書き戻されるたびに IME の設定を書き合っている");
                // ほかのアプリから戻っても IME に触らない（2026-11-17 の 2 回目の指摘: 戻すと同時に Esc を押すと止まる）。入力欄のない
                // 画面（メインメニュー・試合）では、ほかが書き換えても合わせ直さない。
                guard.OnApplicationFocus(false);
                guard.OnApplicationFocus(true);
                Input.imeCompositionMode = IMECompositionMode.Auto;
                writes = guard.LegacyWrites;
                yield return null;
                yield return null;
                Assert.That(guard.Settling, Is.True, "戻った直後に待たない");
                yield return WaitFor(() => !guard.Settling, 3f, "落ち着かない");
                float waited = Time.unscaledTime;
                while (Time.unscaledTime - waited < ImeGuard.RetrySeconds * 1.5f)
                {
                    yield return null;
                }
                Assert.That(guard.LegacyWrites, Is.EqualTo(writes), "入力欄のない画面で、戻ってから IME を切り替えている");
                Assert.That(guard.Busy, Is.Null, "IME の操作の途中のまま");
                menu.Network.Button.onClick.Invoke();
                menu.Transition.Complete();
                yield return null;
                var online = Object.FindFirstObjectByType<Airerage.Net.OnlineMenu>();
                UnityEngine.UI.InputField field = online.NameField;
                EventSystem.current.SetSelectedGameObject(field.gameObject);
                field.ActivateInputField();
                yield return null;
                yield return null;
                Assert.That(field.isFocused, Is.True, "前提: 名前の入力欄に入力している");
                Assert.That(guard.ImeEnabled, Is.True, "入力欄に入力しているのに IME が無効");
                Assert.That(Input.imeCompositionMode, Is.EqualTo(IMECompositionMode.On), "入力欄に入力しているのに古い入力の IME が無効");
                field.DeactivateInputField();
                EventSystem.current.SetSelectedGameObject(online.BackButton.gameObject);
                yield return null;
                yield return null;
                Assert.That(guard.ImeEnabled, Is.False, "入力欄を離れても IME が有効のまま");
                // uGUI の入力欄は入力の終わりに「自動」へ戻すが、ImeGuard が切る。
                Assert.That(Input.imeCompositionMode, Is.EqualTo(IMECompositionMode.Off), "入力欄を離れても古い入力の IME が切れない");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        /// <summary>
        /// NETWORK を開いてすぐ（切り替えの途中）に戻っても、出ていく接続の画面の白い背景がフェードアウトする（2026-11-16 の指摘: 前の
        /// 切り替えを早送りした同じフレームに写すと、消す予約の入った無効の CanvasGroup まで写り、濃さが効かずに白い背景が残った）。
        /// </summary>
        [UnityTest]
        public IEnumerator Network_BackRightAfterOpening_FadesTheWhitePanel()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var menu = Object.FindFirstObjectByType<MenuScreens>();
            ScreenTransition transition = menu.Transition;
            menu.ContinueFromTitle();
            transition.Complete();
            yield return null;
            var online = Object.FindFirstObjectByType<Airerage.Net.OnlineMenu>();
            foreach (int frames in new[] { 1, 3 })
            {
                menu.Network.Button.onClick.Invoke();
                for (int f = 0; f < frames; f++)
                {
                    yield return null;
                }
                Assert.That(transition.Running, Is.True, "前提: メイン → ネットワークの切り替えの途中");
                online.BackButton.Button.onClick.Invoke();
                GameObject ghost = GameObject.Find("OnlineConnect (Transition)");
                Assert.That(ghost, Is.Not.Null, "出ていく接続の画面の写しがない");
                var group = ghost.GetComponent<CanvasGroup>();
                Assert.That(group.enabled, Is.True, "写しの CanvasGroup が無効（濃さが効かない）");
                var side = ghost.transform.Find("Side").GetComponent<MainMenuPanel>();
                float start = Time.realtimeSinceStartup;
                yield return WaitFor(() => Time.realtimeSinceStartup - start > 0.26f || !transition.Running, 3f, "時間が進まない");
                if (ghost != null && ghost.activeInHierarchy)
                {
                    Assert.That(side.canvasRenderer.GetInheritedAlpha(), Is.LessThan(0.5f),
                        $"戻る切り替えの途中で白い背景が薄くならない（{frames} フレームで戻った）");
                }
                yield return WaitFor(() => !transition.Running, 3f, "切り替えが終わらない");
                yield return null;
                Assert.That(CopiesLeft(), Is.Zero, "演出が終わっても写しが残っている");
                foreach (CanvasGroup each in menu.GetComponentsInChildren<CanvasGroup>(true))
                {
                    Assert.That(each.enabled, Is.True, $"{each.name} の CanvasGroup が無効のまま残っている");
                }
            }
        }

        /// <summary>
        /// タイトルロゴ（D-96。2026-11-16）: タイトルを出すたびに演出を始め（メインメニューから戻ったときも）、終わると止まる。
        /// </summary>
        [UnityTest]
        public IEnumerator Title_PlaysTheLogo_EachTimeItIsShown()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var menu = Object.FindFirstObjectByType<MenuScreens>();
            TitleLogo logo = menu.TitleLogo;
            Assert.That(logo, Is.Not.Null, "タイトルロゴがない");
            Assert.That(logo.isActiveAndEnabled, Is.True, "タイトルにロゴが出ていない");
            Assert.That(logo.Elapsed, Is.LessThan(TitleLogoTimeline.Duration + 3f), "タイトルを出したのにロゴの演出を始めていない");
            yield return WaitFor(() => !logo.Playing, TitleLogoTimeline.Duration + 3f, "ロゴの演出が終わらない");
            menu.ContinueFromTitle();
            menu.Transition.Complete();
            yield return null;
            menu.ShowTitle();
            Assert.That(logo.Playing, Is.True, "メインメニューから戻ったときにロゴの演出をやり直さない");
            Assert.That(logo.Elapsed, Is.LessThan(0.2f));
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
            Assert.That(curtain.Showing, Is.True, "試合へ入るときにゲートを出さない");
            // ゲート（D-99 の T12）: 行き先のステージの色の扉と、合わせ目の札にステージの名前。
            Assert.That(curtain.Label, Is.EqualTo(flow.Stages.Get(0).DisplayName), "札にステージの名前がない");
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
