using Airerage.Net;
using Airerage.Runtime.Flow;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Airerage.Editor
{
    /// <summary>
    /// ネットワーク対戦のメニュー（OnlineMenu。docs/15 T6-7）の画面を作る: 接続（名前・参加コード・アドレス・ポート、HOST ONLINE・
    /// JOIN WITH CODE・HOST (LAN)・JOIN (LAN)・BACK。参加コードは docs/15 T6-10）、待機所（1P〜8P の札、チーム対戦の 4 色の列の見出し、
    /// READY・自分のチーム・チーム対戦か・人数配分・均等に分ける・時間の制限・参加コードのコピー・LEAVE。docs/19 T9-4、T9-5）、読み込み中。キャラクター選択とステージ選択は、メニューの画面のものを使う。
    /// 基準の画面の大きさは 1920×1080。
    /// </summary>
    internal static class OnlineMenuUiBuilder
    {
        public static OnlineMenu Create(Transform canvas, MenuScreens screens, CharacterSelectScreen characterSelect,
            StageSelectScreen stageSelect, StageCatalog stages, ItemSelectScreen itemSelect)
        {
            var top = new Vector2(0f, 1f);

            // 接続の画面。
            RectTransform connect = MenuUiBuilder.CreateFull(canvas, "OnlineConnect");
            MenuUiBuilder.CreateFill(connect, "Side", new Color(0f, 0f, 0f, 0.5f), Vector2.zero, new Vector2(0.92f, 1f));
            MenuUiBuilder.CreateText(connect, "Title", "NETWORK VS", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                top, new Vector2(90f, -60f), new Vector2(900f, 90f), Color.white);
            // 左の列: 名前と、参加コード（インターネット越し。Relay）と BACK。右の列: IP アドレスとポート（同じ LAN）。
            var fieldSize = new Vector2(700f, 60f);
            var small = new Vector2(560f, 66f);
            InputField name = MenuUiBuilder.CreateInputField(connect, "Name", "NAME", top, new Vector2(110f, -190f), fieldSize);
            MenuUiBuilder.CreateText(connect, "OnlineTitle", "ONLINE (JOIN CODE)", 30, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                top, new Vector2(110f, -280f), new Vector2(700f, 44f), MenuAccents.Network);
            InputField code = MenuUiBuilder.CreateInputField(connect, "Code", "JOIN CODE", top, new Vector2(110f, -330f), fieldSize);
            code.characterLimit = JoinCodes.MaxLength;
            code.contentType = InputField.ContentType.Alphanumeric;
            MenuOption hostOnline = MenuUiBuilder.CreateMenuItem(connect, "HostOnline", "HOST ONLINE", top, new Vector2(110f, -410f), small);
            MenuOption joinOnline = MenuUiBuilder.CreateMenuItem(connect, "JoinOnline", "JOIN WITH CODE", top, new Vector2(110f, -488f), small);
            MenuOption back = MenuUiBuilder.CreateMenuItem(connect, "Back", "BACK", top, new Vector2(110f, -586f), small);
            MenuUiBuilder.CreateText(connect, "LanTitle", "SAME NETWORK (LAN)", 30, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                top, new Vector2(960f, -280f), new Vector2(700f, 44f), MenuAccents.Network);
            InputField address = MenuUiBuilder.CreateInputField(connect, "Address", "ADDRESS", top, new Vector2(960f, -330f), fieldSize);
            InputField port = MenuUiBuilder.CreateInputField(connect, "Port", "PORT", top, new Vector2(960f, -400f), fieldSize);
            MenuOption host = MenuUiBuilder.CreateMenuItem(connect, "Host", "HOST (LAN)", top, new Vector2(960f, -480f), small);
            MenuOption join = MenuUiBuilder.CreateMenuItem(connect, "Join", "JOIN (LAN)", top, new Vector2(960f, -558f), small);
            foreach (MenuOption option in new[] { hostOnline, joinOnline, back, host, join })
            {
                // 選んでいない項目は暗く下げる（D-88 の B13）。
                option.SetRecede(true);
            }
            Text connectMessage = MenuUiBuilder.CreateText(connect, "Message", string.Empty, 26, FontStyle.Bold, TextAnchor.UpperLeft,
                top, new Vector2(110f, -690f), new Vector2(1500f, 120f), new Color(1f, 0.85f, 0.3f));
            connectMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuUiBuilder.CreateHint(connect, "HOST ONLINE: get a JOIN CODE and tell it to your friends.   JOIN WITH CODE: enter the host's code.   LAN: the host's ADDRESS and PORT.", "HOST ONLINE: 参加コードを受け取って友だちに伝える　　JOIN WITH CODE: ホストのコードを入れる　　LAN: ホストのアドレスとポートを入れる");
            connect.gameObject.SetActive(false);

            // 待機所。
            RectTransform waiting = MenuUiBuilder.CreateFull(canvas, "OnlineWaiting");
            MenuUiBuilder.CreateFill(waiting, "Dim", new Color(0f, 0f, 0f, 0.5f), Vector2.zero, Vector2.one);
            MenuUiBuilder.CreateText(waiting, "Title", "WAITING ROOM", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                top, new Vector2(90f, -60f), new Vector2(900f, 90f), Color.white);
            Text info = MenuUiBuilder.CreateText(waiting, "Info", string.Empty, 24, FontStyle.Bold, TextAnchor.UpperLeft,
                top, new Vector2(94f, -150f), new Vector2(1400f, 40f), MenuAccents.Network);
            // 札を並べる場所（左。はみ出す分は縦にスクロールする。2026-11-03 のユーザーの要望）。札の位置は実行中に OnlineMenu が決める
            // （個人戦は PLAYERS の下に 1 列、チーム対戦は 4 色のチームを縦に並べ、人が入ったチームはその分だけ下へ広がる）。
            RectTransform slotContent = CreateScrollArea(waiting, new Vector2(110f, -200f), new Vector2(800f, 580f), out ScrollRect slotScroll);
            // プレイヤーの札（8 枚。docs/19 T9-4）。チーム対戦では、ホストは札を選んで決定すると、その人を次のチームへ移せる（自分の札は
            // 自分でも。D-78）。観戦の人の札は、実行中に右の端の観戦の列（スクロールの外）へ移す。
            var plates = new Object[OnlineSession.MaxPlayers];
            var slotButtons = new Object[OnlineSession.MaxPlayers];
            var numbers = new Object[OnlineSession.MaxPlayers];
            var names = new Object[OnlineSession.MaxPlayers];
            var states = new Object[OnlineSession.MaxPlayers];
            var hostIcons = new Object[OnlineSession.MaxPlayers];
            var youIcons = new Object[OnlineSession.MaxPlayers];
            var colors = new Color[OnlineSession.MaxPlayers];
            for (int p = 0; p < OnlineSession.MaxPlayers; p++)
            {
                colors[p] = SandboxOnlineSceneBuilder.PlayerColors[p].Color;
                var slot = new GameObject($"Slot{p + 1}P", typeof(RectTransform));
                slot.transform.SetParent(slotContent, false);
                var rect = (RectTransform)slot.transform;
                rect.anchorMin = rect.anchorMax = top;
                rect.pivot = top;
                rect.anchoredPosition = new Vector2(110f, -200f - p * 72f);
                rect.sizeDelta = new Vector2(780f, 64f);
                var plate = slot.AddComponent<SlantedPlate>();
                plate.Configure(0.2f, 3f, new Color(0.03f, 0.05f, 0.09f, 0.45f), new Color(1f, 1f, 1f, 0.2f));
                plates[p] = plate;
                var button = slot.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.targetGraphic = plate;
                button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                button.interactable = false;
                slotButtons[p] = slot.AddComponent<PlateButton>();
                numbers[p] = MenuUiBuilder.CreateText(slot.transform, "Number", $"{p + 1}P", 34, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(90f, 56f), colors[p]);
                names[p] = MenuUiBuilder.CreateText(slot.transform, "Name", string.Empty, 28, FontStyle.Bold, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.5f), new Vector2(130f, 0f), new Vector2(420f, 56f), Color.white);
                states[p] = MenuUiBuilder.CreateText(slot.transform, "State", string.Empty, 26, FontStyle.BoldAndItalic, TextAnchor.MiddleRight,
                    new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(240f, 56f), Color.white);
                ((Text)names[p]).resizeTextForBestFit = true;
                ((Text)names[p]).resizeTextMinSize = 14;
                ((Text)names[p]).resizeTextMaxSize = 28;
                // ホストと自分の印（名前の前。絵は実行中に OnlineMenu が描いて付け、位置も OnlineMenu が決める）。
                hostIcons[p] = CreateMark(slot.transform, "HostIcon");
                youIcons[p] = CreateMark(slot.transform, "YouIcon");
            }
            // 列の見出し（チーム対戦は 4 色のチームの名前と人数。個人戦は 1 つ目を「PLAYERS」に広げる）。選んで決定（クリック）すると、
            // 自分がそのチームに入る（観戦の待機所からも。ユーザーの要望、2026-11-02）。位置と色は実行中に OnlineMenu が決める。
            var headers = new Object[Airerage.Core.Match.TeamRules.MaxTeams];
            var headerPlates = new Object[Airerage.Core.Match.TeamRules.MaxTeams];
            var headerButtons = new Object[Airerage.Core.Match.TeamRules.MaxTeams];
            var headerTexts = new Object[Airerage.Core.Match.TeamRules.MaxTeams];
            for (int t = 0; t < headers.Length; t++)
            {
                Color teamColor = Airerage.Runtime.Presentation.TeamColors.Of(t + 1);
                GameObject header = CreateHeader(slotContent, $"Team{t + 1}", new Vector2(110f + t * 198f, -200f), new Vector2(190f, 50f), teamColor,
                    Airerage.Runtime.Presentation.TeamColors.NameOf(t + 1), out SlantedPlate plate, out PlateButton button, out Text label);
                headers[t] = header;
                headerPlates[t] = plate;
                headerButtons[t] = button;
                headerTexts[t] = label;
            }
            // 観戦の待機所（右の端の列。個人戦でもチーム対戦でも。選んで決定すると、自分がそこへ移る。ホストは移れない）。
            GameObject spectators = CreateHeader(waiting, "Spectators", new Vector2(1600f, -200f), new Vector2(280f, 50f), new Color(0.75f, 0.75f, 0.8f),
                "SPECTATORS", out SlantedPlate spectatorPlate, out PlateButton spectatorButton, out Text spectatorText);
            spectators.SetActive(true);
            // 右の列: READY、自分のチーム、チーム対戦か（ホスト）、人数配分と均等に分ける（ホスト、チーム対戦）、時間の制限（ホスト）、
            // 参加コードのコピー、LEAVE。左のプレイヤーの札へも動けるように、上下左右で動く。
            var row = new Vector2(560f, 64f);
            float rowY = -200f;
            MenuOption ready = WaitingItem(waiting, "Ready", "READY", ref rowY, row);
            MenuOption team = WaitingItem(waiting, "Team", "MY TEAM: RED", ref rowY, row);
            MenuOption mode = WaitingItem(waiting, "Mode", "MODE: FREE FOR ALL", ref rowY, row);
            MenuOption split = WaitingItem(waiting, "Split", "SPLIT: 4 VS 4", ref rowY, row);
            MenuOption shuffle = WaitingItem(waiting, "Shuffle", "RANDOM SPLIT", ref rowY, row);
            // アイテムのルール（docs/20_item_plan.md。並べ方は OnlineMenu が出す項目に合わせて詰める）。
            MenuOption itemMode = WaitingItem(waiting, "ItemMode", "ITEMS: DEFAULT", ref rowY, row);
            MenuOption itemCount = WaitingItem(waiting, "ItemCount", "START ITEM COUNT: 3", ref rowY, row);
            MenuOption itemPick = WaitingItem(waiting, "ItemPick", "SELECT START ITEMS", ref rowY, row);
            MenuOption dropRate = WaitingItem(waiting, "DropRate", "DROP RATE: MID", ref rowY, row);
            MenuOption dropPool = WaitingItem(waiting, "DropPool", "SELECT DROP ITEMS", ref rowY, row);
            MenuOption timeLimit = WaitingItem(waiting, "TimeLimit", "TIME LIMIT: ON", ref rowY, row);
            MenuOption copyCode = WaitingItem(waiting, "CopyCode", "COPY JOIN CODE", ref rowY, row);
            MenuOption leave = WaitingItem(waiting, "Leave", "LEAVE", ref rowY, row);
            Text waitingMessage = MenuUiBuilder.CreateText(waiting, "Message", string.Empty, 26, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                top, new Vector2(110f, -800f), new Vector2(1500f, 44f), Color.white);
            MenuUiBuilder.CreateHint(waiting, "↑↓←→: select     Enter / A: OK     Esc / B: cancel ready, leave", "↑↓←→:選択　　Enter ／ A: 決定　　Esc ／ B: 準備完了の取り消し・退室");
            waiting.gameObject.SetActive(false);

            // 読み込み中。
            RectTransform loading = MenuUiBuilder.CreateFull(canvas, "OnlineLoading");
            MenuUiBuilder.CreateFill(loading, "Dim", new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one);
            MenuUiBuilder.Localize(MenuUiBuilder.CreateText(loading, "Text", "LOADING...", 56, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 90f), Color.white), "読み込み中……");
            loading.gameObject.SetActive(false);

            var menu = canvas.gameObject.AddComponent<OnlineMenu>();
            SandboxWireSceneBuilder.SetReference(menu, "_menu", screens);
            SandboxWireSceneBuilder.SetReference(menu, "_characterSelect", characterSelect);
            SandboxWireSceneBuilder.SetReference(menu, "_stageSelect", stageSelect);
            SandboxWireSceneBuilder.SetReference(menu, "_stages", stages);
            SandboxWireSceneBuilder.SetReference(menu, "_connectPanel", connect.gameObject);
            SandboxWireSceneBuilder.SetReference(menu, "_name", name);
            SandboxWireSceneBuilder.SetReference(menu, "_code", code);
            SandboxWireSceneBuilder.SetReference(menu, "_address", address);
            SandboxWireSceneBuilder.SetReference(menu, "_port", port);
            SandboxWireSceneBuilder.SetReference(menu, "_hostOnline", hostOnline);
            SandboxWireSceneBuilder.SetReference(menu, "_joinOnline", joinOnline);
            SandboxWireSceneBuilder.SetReference(menu, "_host", host);
            SandboxWireSceneBuilder.SetReference(menu, "_join", join);
            SandboxWireSceneBuilder.SetReference(menu, "_back", back);
            SandboxWireSceneBuilder.SetReference(menu, "_connectMessage", connectMessage);
            SandboxWireSceneBuilder.SetReference(menu, "_waitingPanel", waiting.gameObject);
            SandboxWireSceneBuilder.SetReference(menu, "_waitingInfo", info);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotPlates", plates);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotButtons", slotButtons);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotNumbers", numbers);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotNames", names);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotStates", states);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotHostIcons", hostIcons);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotYouIcons", youIcons);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_teamHeaders", headers);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_teamHeaderPlates", headerPlates);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_teamHeaderButtons", headerButtons);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_teamHeaderTexts", headerTexts);
            SandboxWireSceneBuilder.SetReference(menu, "_spectatorHeader", spectatorButton);
            SandboxWireSceneBuilder.SetReference(menu, "_slotContent", slotContent);
            SandboxWireSceneBuilder.SetReference(menu, "_slotScroll", slotScroll);
            SandboxWireSceneBuilder.SetReference(menu, "_spectatorHeaderText", spectatorText);
            SandboxWireSceneBuilder.SetReference(menu, "_ready", ready);
            SandboxWireSceneBuilder.SetReference(menu, "_team", team);
            SandboxWireSceneBuilder.SetReference(menu, "_mode", mode);
            SandboxWireSceneBuilder.SetReference(menu, "_split", split);
            SandboxWireSceneBuilder.SetReference(menu, "_shuffle", shuffle);
            SandboxWireSceneBuilder.SetReference(menu, "_timeLimit", timeLimit);
            SandboxWireSceneBuilder.SetReference(menu, "_itemMode", itemMode);
            SandboxWireSceneBuilder.SetReference(menu, "_itemCount", itemCount);
            SandboxWireSceneBuilder.SetReference(menu, "_itemPick", itemPick);
            SandboxWireSceneBuilder.SetReference(menu, "_dropRate", dropRate);
            SandboxWireSceneBuilder.SetReference(menu, "_dropPool", dropPool);
            SandboxWireSceneBuilder.SetReference(menu, "_itemSelect", itemSelect);
            SandboxWireSceneBuilder.SetReference(menu, "_copyCode", copyCode);
            SandboxWireSceneBuilder.SetReference(menu, "_leave", leave);
            SandboxWireSceneBuilder.SetReference(menu, "_waitingMessage", waitingMessage);
            SandboxWireSceneBuilder.SetReference(menu, "_loadingPanel", loading.gameObject);
            SandboxOnlineSceneBuilder.SetColors(menu, "_colors", colors);
            return menu;
        }

        // 縦にスクロールする場所（左上 position、大きさ size。中身の RectTransform を返す。右に細いスクロールバー。はみ出さなければ隠す）。
        // 札の名前の前に置く印（RawImage。クリックを通す）。
        private static RawImage CreateMark(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(32f, 32f);
            var image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }

        internal static RectTransform CreateScrollArea(Transform parent, Vector2 position, Vector2 size, out ScrollRect scroll)
        {
            var viewportGo = new GameObject("SlotArea", typeof(RectTransform));
            viewportGo.transform.SetParent(parent, false);
            var viewport = (RectTransform)viewportGo.transform;
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0f, 1f);
            viewport.anchoredPosition = position;
            viewport.sizeDelta = size;
            viewportGo.AddComponent<RectMask2D>();
            // スクロールの操作（ホイール・ドラッグ）を受けるための透明な面。
            var catcher = viewportGo.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(size.x - 20f, size.y);

            var barGo = new GameObject("Scrollbar", typeof(RectTransform));
            barGo.transform.SetParent(parent, false);
            var bar = (RectTransform)barGo.transform;
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 1f);
            bar.anchoredPosition = position + new Vector2(size.x - 10f, 0f);
            bar.sizeDelta = new Vector2(8f, size.y);
            var track = barGo.AddComponent<Image>();
            track.color = new Color(1f, 1f, 1f, 0.12f);
            var handleArea = new GameObject("Area", typeof(RectTransform));
            handleArea.transform.SetParent(bar, false);
            var areaRect = (RectTransform)handleArea.transform;
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = areaRect.offsetMax = Vector2.zero;
            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(handleArea.transform, false);
            var handle = (RectTransform)handleGo.transform;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            // スクロールの印は画面のイメージカラー（画面を出すときに AccentTint.Apply で塗る。2026-11-08）。
            var handleImage = AccentTint.Mark(handleGo.AddComponent<Image>());
            handleImage.color = new Color(MenuUiBuilder.Accent.r, MenuUiBuilder.Accent.g, MenuUiBuilder.Accent.b, 0.8f);
            var scrollbar = barGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

            scroll = viewportGo.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        // 待機所の列の見出し 1 つ（斜めの札と名前。選んで決定できる。上下左右で動く）。初めは隠す。
        private static GameObject CreateHeader(Transform parent, string name, Vector2 position, Vector2 size, Color color, string text,
            out SlantedPlate plate, out PlateButton button, out Text label)
        {
            var header = new GameObject(name, typeof(RectTransform));
            header.transform.SetParent(parent, false);
            var rect = (RectTransform)header.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            plate = header.AddComponent<SlantedPlate>();
            plate.Configure(0.2f, 3f, new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.9f), color);
            var selectable = header.AddComponent<Button>();
            selectable.transition = Selectable.Transition.None;
            selectable.targetGraphic = plate;
            selectable.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button = header.AddComponent<PlateButton>();
            label = MenuUiBuilder.CreateText(header.transform, "Label", text, 26, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x - 20f, size.y - 4f), color);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = 26;
            header.SetActive(false);
            return header;
        }

        // 待機所の右の列の項目 1 つ（上から順に y を進める。上下左右で動く）。
        private static MenuOption WaitingItem(Transform parent, string name, string label, ref float y, Vector2 size)
        {
            MenuOption item = MenuUiBuilder.CreateMenuItem(parent, name, label, new Vector2(0f, 1f), new Vector2(990f, y), size);
            item.Button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            // 長い項目（SPLIT: 2 VS 2 VS 2 VS 2 など）も 1 行のまま枠に収める（2026-11-03 の指摘）。
            item.SetShrinkToFit(true);
            y -= size.y + 10f;
            return item;
        }
    }
}
