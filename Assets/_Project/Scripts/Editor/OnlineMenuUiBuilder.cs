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
    /// JOIN WITH CODE・HOST (LAN)・JOIN (LAN)・BACK。参加コードは docs/15 T6-10）、待機所（1P〜4P の札、READY・時間の制限・
    /// 参加コードのコピー・LEAVE）、読み込み中。キャラクター選択とステージ選択は、メニューの画面のものを使う。
    /// 基準の画面の大きさは 1920×1080。
    /// </summary>
    internal static class OnlineMenuUiBuilder
    {
        public static OnlineMenu Create(Transform canvas, MenuScreens screens, CharacterSelectScreen characterSelect,
            StageSelectScreen stageSelect, StageCatalog stages)
        {
            var top = new Vector2(0f, 1f);
            var item = new Vector2(560f, 72f);

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
                top, new Vector2(110f, -280f), new Vector2(700f, 44f), MenuUiBuilder.Accent);
            InputField code = MenuUiBuilder.CreateInputField(connect, "Code", "JOIN CODE", top, new Vector2(110f, -330f), fieldSize);
            code.characterLimit = JoinCodes.MaxLength;
            code.contentType = InputField.ContentType.Alphanumeric;
            MenuOption hostOnline = MenuUiBuilder.CreateMenuItem(connect, "HostOnline", "HOST ONLINE", top, new Vector2(110f, -410f), small);
            MenuOption joinOnline = MenuUiBuilder.CreateMenuItem(connect, "JoinOnline", "JOIN WITH CODE", top, new Vector2(110f, -488f), small);
            MenuOption back = MenuUiBuilder.CreateMenuItem(connect, "Back", "BACK", top, new Vector2(110f, -586f), small);
            MenuUiBuilder.CreateText(connect, "LanTitle", "SAME NETWORK (LAN)", 30, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                top, new Vector2(960f, -280f), new Vector2(700f, 44f), MenuUiBuilder.Accent);
            InputField address = MenuUiBuilder.CreateInputField(connect, "Address", "ADDRESS", top, new Vector2(960f, -330f), fieldSize);
            InputField port = MenuUiBuilder.CreateInputField(connect, "Port", "PORT", top, new Vector2(960f, -400f), fieldSize);
            MenuOption host = MenuUiBuilder.CreateMenuItem(connect, "Host", "HOST (LAN)", top, new Vector2(960f, -480f), small);
            MenuOption join = MenuUiBuilder.CreateMenuItem(connect, "Join", "JOIN (LAN)", top, new Vector2(960f, -558f), small);
            Text connectMessage = MenuUiBuilder.CreateText(connect, "Message", string.Empty, 26, FontStyle.Bold, TextAnchor.UpperLeft,
                top, new Vector2(110f, -690f), new Vector2(1500f, 120f), new Color(1f, 0.85f, 0.3f));
            connectMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuUiBuilder.CreateText(connect, "Hint",
                "HOST ONLINE: get a JOIN CODE and tell it to your friends.   JOIN WITH CODE: enter the host's code.   LAN: the host's ADDRESS and PORT.",
                22, FontStyle.Bold, TextAnchor.LowerLeft, Vector2.zero, new Vector2(90f, 50f), new Vector2(1750f, 40f), new Color(1f, 1f, 1f, 0.7f));
            connect.gameObject.SetActive(false);

            // 待機所。
            RectTransform waiting = MenuUiBuilder.CreateFull(canvas, "OnlineWaiting");
            MenuUiBuilder.CreateFill(waiting, "Dim", new Color(0f, 0f, 0f, 0.5f), Vector2.zero, Vector2.one);
            MenuUiBuilder.CreateText(waiting, "Title", "WAITING ROOM", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                top, new Vector2(90f, -60f), new Vector2(900f, 90f), Color.white);
            Text info = MenuUiBuilder.CreateText(waiting, "Info", string.Empty, 24, FontStyle.Bold, TextAnchor.UpperLeft,
                top, new Vector2(94f, -150f), new Vector2(1400f, 40f), MenuUiBuilder.Accent);
            var plates = new Object[OnlineSession.MaxPlayers];
            var names = new Object[OnlineSession.MaxPlayers];
            var states = new Object[OnlineSession.MaxPlayers];
            var colors = new Color[OnlineSession.MaxPlayers];
            for (int p = 0; p < OnlineSession.MaxPlayers; p++)
            {
                colors[p] = SandboxOnlineSceneBuilder.PlayerColors[p].Color;
                var slot = new GameObject($"Slot{p + 1}P", typeof(RectTransform));
                slot.transform.SetParent(waiting, false);
                var rect = (RectTransform)slot.transform;
                rect.anchorMin = rect.anchorMax = top;
                rect.pivot = top;
                rect.anchoredPosition = new Vector2(110f, -210f - p * 96f);
                rect.sizeDelta = new Vector2(780f, 84f);
                var plate = slot.AddComponent<SlantedPlate>();
                plate.Configure(0.2f, 3f, new Color(0.03f, 0.05f, 0.09f, 0.45f), new Color(1f, 1f, 1f, 0.2f));
                plate.raycastTarget = false;
                plates[p] = plate;
                MenuUiBuilder.CreateText(slot.transform, "Number", $"{p + 1}P", 40, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(100f, 60f), colors[p]);
                names[p] = MenuUiBuilder.CreateText(slot.transform, "Name", string.Empty, 30, FontStyle.Bold, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(420f, 60f), Color.white);
                states[p] = MenuUiBuilder.CreateText(slot.transform, "State", string.Empty, 28, FontStyle.BoldAndItalic, TextAnchor.MiddleRight,
                    new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(240f, 60f), Color.white);
            }
            MenuOption ready = MenuUiBuilder.CreateMenuItem(waiting, "Ready", "READY", top, new Vector2(990f, -210f), item);
            MenuOption timeLimit = MenuUiBuilder.CreateMenuItem(waiting, "TimeLimit", "TIME LIMIT: ON", top, new Vector2(990f, -298f), item);
            MenuOption copyCode = MenuUiBuilder.CreateMenuItem(waiting, "CopyCode", "COPY JOIN CODE", top, new Vector2(990f, -386f), item);
            MenuOption leave = MenuUiBuilder.CreateMenuItem(waiting, "Leave", "LEAVE", top, new Vector2(990f, -474f), item);
            Text waitingMessage = MenuUiBuilder.CreateText(waiting, "Message", string.Empty, 26, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                top, new Vector2(110f, -610f), new Vector2(1400f, 44f), Color.white);
            MenuUiBuilder.CreateText(waiting, "Hint", "Up / Down: select     Enter / A: OK     Esc / B: cancel ready, leave", 22, FontStyle.Bold,
                TextAnchor.LowerLeft, Vector2.zero, new Vector2(90f, 50f), new Vector2(1400f, 40f), new Color(1f, 1f, 1f, 0.7f));
            waiting.gameObject.SetActive(false);

            // 読み込み中。
            RectTransform loading = MenuUiBuilder.CreateFull(canvas, "OnlineLoading");
            MenuUiBuilder.CreateFill(loading, "Dim", new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one);
            MenuUiBuilder.CreateText(loading, "Text", "LOADING...", 56, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 90f), Color.white);
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
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotNames", names);
            SandboxWireSceneBuilder.SetReferenceArray(menu, "_slotStates", states);
            SandboxWireSceneBuilder.SetReference(menu, "_ready", ready);
            SandboxWireSceneBuilder.SetReference(menu, "_timeLimit", timeLimit);
            SandboxWireSceneBuilder.SetReference(menu, "_copyCode", copyCode);
            SandboxWireSceneBuilder.SetReference(menu, "_leave", leave);
            SandboxWireSceneBuilder.SetReference(menu, "_waitingMessage", waitingMessage);
            SandboxWireSceneBuilder.SetReference(menu, "_loadingPanel", loading.gameObject);
            SandboxOnlineSceneBuilder.SetColors(menu, "_colors", colors);
            return menu;
        }
    }
}
