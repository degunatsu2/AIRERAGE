using Airerage.Core.Common;
using Airerage.Core.Items;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ゲームパッドで ITEM MENU（X）を押している間に、操作しているキャラクターの頭上に出すアイテムバッグ（ユーザーの指定、2026-11-05。
    /// docs/20_item_plan.md）。SkillHud が持ち、SkillHudFront（ほかの表示より前）で描く。
    /// - 押すとフェードインし、何も選ばずに離すとそのまま全体がフェードアウトする。
    /// - 十字キーで使える枠を選ぶと、ほかの枠は即座に消え、選んだアイテムが光って上昇しながらフェードアウトする。捨てる操作では演出を出さず、
    ///   バッグを出したまま枠が空になる（落ちていく絵を、実際に落としたアイテムと見間違えるため。2026-11-06）。
    /// - 空の枠・パッシブアイテム・クールタイム中のアイテムを選ぶと、その枠に赤いバツ印が付いてバッグ全体が震える。空の枠で足もとの
    ///   アイテムを拾ったとき（少し後に拾った数が増える）は、拾ったアイテムが光って上昇する。
    /// - 捨てるボタン（A）も押している間は、バッグの左にゴミ箱の印を出す（捨てる入力だと分かるように。2026-11-06）。
    /// - 枠の上の三角は、その枠の十字キーの向き（左・上・右）を指す。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class ItemWheel
    {
        private const float CellSize = 46f;
        private const float CellGap = 6f;
        private const float HeadGap = 34f;
        private const float FadeInSeconds = 0.12f;
        private const float FadeOutSeconds = 0.22f;
        private const float ConfirmSeconds = 0.55f;
        private const float RiseHeight = 46f;
        private const float DenySeconds = 0.4f;
        private const float ShakeAmplitude = 7f;
        // 空の枠を選んだとき、拾ったかどうかを待つ長さ（拾うのはシミュレーションの次のティック）。
        private const float PickupWaitSeconds = 0.15f;

        private static readonly ItemTuning ItemValues = new ItemTuning();

        private enum Choice
        {
            None,
            Confirm,
            Deny,
            Pending,
        }

        private int _character = -1;
        private int _choicesSeen;
        private bool _held;
        private bool _dropHeld;
        private float _alpha;
        private Choice _choice;
        private int _slot;
        private ItemId _item;
        private float _choiceAt = -10f;
        private int _pickupsBefore;
        private GUIStyle _seconds;
        private Sprite _glow;

        /// <summary>選んだ回数と、赤いバツ印を出した回数（テスト用）。</summary>
        public int ConfirmCount { get; private set; }
        public int DenyCount { get; private set; }

        /// <summary>ゴミ箱の印を出しているか（テスト用）。</summary>
        public bool TrashShown => _dropHeld && Shown;

        /// <summary>バッグを出しているか（テスト用）。</summary>
        public bool Shown => _alpha > 0.01f || _choice == Choice.Confirm;

        /// <summary>作った光の絵を消す（SkillHud の OnDestroy から）。</summary>
        public void Release()
        {
            SoftSprites.Release(_glow);
            _glow = null;
        }

        /// <summary>毎フレーム呼ぶ（index は表示するキャラクター。いなければ -1）。</summary>
        public void Update(ISimulationFeed feed, int index)
        {
            float now = Time.time;
            if (index < 0 || !(feed is ILocalItemMenu source) || !source.TryGetItemMenu(index, out LocalItemMenu menu))
            {
                _held = false;
                _dropHeld = false;
                _character = -1;
                Fade(false);
                return;
            }
            CharacterState state = feed.GetState(index);
            if (index != _character)
            {
                _character = index;
                _choicesSeen = menu.Choices;
                _choice = Choice.None;
            }
            _held = menu.Held;
            _dropHeld = menu.Held && menu.DropHeld;
            if (menu.Choices != _choicesSeen)
            {
                _choicesSeen = menu.Choices;
                Choose(state.Items, menu.Slot, menu.Drop, now);
            }
            if (_choice == Choice.Pending)
            {
                // 空の枠: 拾えたら拾ったアイテムを光らせる。拾えなければ赤いバツ印。
                if (state.Items.PickupCount != _pickupsBefore && ItemCatalog.IsValid(state.Items.Get(_slot)))
                {
                    _item = state.Items.Get(_slot);
                    Confirm(now);
                }
                else if (now - _choiceAt >= PickupWaitSeconds)
                {
                    Deny(now);
                }
            }
            if (_choice == Choice.Confirm && now - _choiceAt >= ConfirmSeconds)
            {
                // 演出が終わったら、まだ押していればもう一度出す。
                _choice = Choice.None;
                _alpha = 0f;
            }
            if (_choice == Choice.Deny && now - _choiceAt >= DenySeconds)
            {
                _choice = Choice.None;
            }
            Fade(_held && _choice != Choice.Confirm);
        }

        private void Fade(bool shown)
        {
            float step = Time.deltaTime / (shown ? FadeInSeconds : FadeOutSeconds);
            _alpha = Mathf.Clamp01(_alpha + (shown ? step : -step));
        }

        private void Choose(in ItemBag bag, int slot, bool drop, float now)
        {
            if (slot < 0 || slot >= ItemCatalog.BagSlots)
            {
                return;
            }
            _slot = slot;
            _choiceAt = now;
            ItemId id = bag.Get(slot);
            _item = id;
            if (id == ItemId.None)
            {
                if (drop)
                {
                    Deny(now);
                    return;
                }
                _choice = Choice.Pending;
                _pickupsBefore = bag.PickupCount;
                return;
            }
            if (drop)
            {
                // 捨てるときは演出を出さない（バッグはそのまま。落ちたアイテムは地面に出る）。
                _choice = Choice.None;
                ConfirmCount++;
                return;
            }
            bool usable = ItemCatalog.IsActive(id) && (ItemRules.ChargesLeft(bag, slot, ItemValues) > 0 || ItemRules.CanRecall(bag, id));
            if (usable)
            {
                Confirm(now);
            }
            else
            {
                Deny(now);
            }
        }

        private void Confirm(float now)
        {
            _choice = Choice.Confirm;
            _choiceAt = now;
            ConfirmCount++;
        }

        private void Deny(float now)
        {
            _choice = Choice.Deny;
            _choiceAt = now;
            DenyCount++;
        }

        /// <summary>頭上のバッグを描く（OnGUI から）。</summary>
        public void Draw(ISimulationFeed feed, Camera camera)
        {
            if (_character < 0 || camera == null || feed == null || _character >= feed.CharacterCount || !Shown)
            {
                return;
            }
            CharacterState state = feed.GetState(_character);
            float top = state.Position.Y + feed.GetConfig(_character).Movement.HalfHeight;
            Vector3 p = camera.WorldToScreenPoint(new Vector3(state.Position.X, top, 0f));
            if (p.z <= 0f)
            {
                return;
            }
            float now = Time.time;
            float since = now - _choiceAt;
            float width = ItemCatalog.BagSlots * CellSize + (ItemCatalog.BagSlots - 1) * CellGap;
            float shake = _choice == Choice.Deny ? Mathf.Sin(since * 70f) * ShakeAmplitude * (1f - Mathf.Clamp01(since / DenySeconds)) : 0f;
            float x = p.x - width * 0.5f + shake;
            float y = Screen.height - p.y - HeadGap - CellSize;
            Color previous = GUI.color;
            ItemBag bag = state.Items;
            if (_choice == Choice.Confirm)
            {
                // ほかの枠は即座に消え、選んだアイテムは光って上昇しながら消える。
                float t = Mathf.Clamp01(since / ConfirmSeconds);
                float alpha = 1f - t * t;
                float offset = -RiseHeight * (1f - (1f - t) * (1f - t));
                var cell = new Rect(x + _slot * (CellSize + CellGap), y + offset, CellSize, CellSize);
                Color accent = ItemIcons.Accent(_item);
                float glow = 1f + 0.6f * (1f - t);
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.55f * alpha);
                if (_glow == null)
                {
                    _glow = SoftSprites.CreateGlow("ItemWheelGlow");
                }
                Texture2D halo = _glow != null ? _glow.texture : null;
                if (halo != null)
                {
                    float size = CellSize * 2.2f * glow;
                    GUI.DrawTexture(new Rect(cell.center.x - size * 0.5f, cell.center.y - size * 0.5f, size, size), halo);
                }
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Texture2D icon = ItemIcons.Get(_item);
                if (icon != null)
                {
                    GUI.DrawTexture(cell, icon, ScaleMode.ScaleToFit);
                }
                // 白く光る（はじめだけ）。
                GUI.color = new Color(1f, 1f, 1f, 0.6f * (1f - Mathf.Clamp01(t * 3f)));
                if (icon != null)
                {
                    GUI.DrawTexture(cell, icon, ScaleMode.ScaleToFit);
                }
                GUI.color = previous;
                return;
            }
            float a = _alpha;
            // 背景の帯（斜めの区切りの 3 枠）。
            GUI.color = new Color(0.03f, 0.05f, 0.09f, 0.82f * a);
            GUI.DrawTexture(new Rect(x - 6f, y - 6f, width + 12f, CellSize + 12f), Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.85f, 1f, a);
            GUI.DrawTexture(new Rect(x - 6f, y + CellSize + 4f, width + 12f, 2f), Texture2D.whiteTexture);
            if (_dropHeld)
            {
                DrawTrash(new Rect(x - 12f - CellSize * 0.8f, y + CellSize * 0.1f, CellSize * 0.8f, CellSize * 0.8f), a);
            }
            for (int i = 0; i < ItemCatalog.BagSlots; i++)
            {
                var cell = new Rect(x + i * (CellSize + CellGap), y, CellSize, CellSize);
                GUI.color = new Color(1f, 1f, 1f, 0.08f * a);
                GUI.DrawTexture(cell, Texture2D.whiteTexture);
                ItemId id = bag.Get(i);
                Texture2D icon = ItemIcons.Get(id);
                if (icon != null)
                {
                    bool passive = ItemCatalog.IsPassive(id);
                    GUI.color = new Color(1f, 1f, 1f, (passive ? 0.55f : 1f) * a);
                    GUI.DrawTexture(cell, icon, ScaleMode.ScaleToFit);
                    int cooldown = bag.GetCooldown(i);
                    if (ItemCatalog.IsActive(id) && cooldown != 0 && ItemRules.ChargesLeft(bag, i, ItemValues) == 0)
                    {
                        int full = Mathf.Max(1, ItemValues.CooldownOf(id));
                        float left = cooldown < 0 ? 1f : Mathf.Clamp01(cooldown / (float)full);
                        GUI.color = new Color(0f, 0f, 0f, 0.62f * a);
                        GUI.DrawTexture(new Rect(cell.x, cell.y, cell.width, cell.height * left), Texture2D.whiteTexture);
                        if (cooldown > 0)
                        {
                            DrawSeconds(cell, cooldown, a);
                        }
                    }
                }
                // 十字キーの向き（左・上・右）の小さな三角。
                DrawArrow(new Vector2(cell.center.x, cell.y - 9f), i, a);
                if (_choice == Choice.Deny && i == _slot)
                {
                    DrawCross(cell, a * (1f - Mathf.Clamp01((since - DenySeconds * 0.6f) / (DenySeconds * 0.4f))));
                }
            }
            GUI.color = previous;
        }

        private void DrawSeconds(Rect cell, int ticks, float alpha)
        {
            if (_seconds == null)
            {
                _seconds = UI.GameFonts.Use(new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, fontSize = 22 });
            }
            string text = Mathf.CeilToInt(ticks / (float)SimTime.TicksPerSecond).ToString();
            GUI.color = Color.white;
            _seconds.normal.textColor = new Color(0f, 0f, 0f, 0.9f * alpha);
            GUI.Label(new Rect(cell.x + 1.5f, cell.y + 1.5f, cell.width, cell.height), text, _seconds);
            _seconds.normal.textColor = new Color(1f, 1f, 1f, alpha);
            GUI.Label(cell, text, _seconds);
        }

        // 枠 slot に対応する十字キーの向き（0 左 ◁・1 上 △・2 右 ▷）の三角（細い線を重ねて描く。先が細い）。
        private static void DrawArrow(Vector2 center, int slot, float alpha)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.75f * alpha);
            for (int k = 0; k < 4; k++)
            {
                // k = 0 が三角の底（いちばん長い）、k = 3 が先。
                float length = 8f - k * 2f;
                switch (slot)
                {
                    case 0:
                        GUI.DrawTexture(new Rect(center.x + 2f - k * 2f, center.y - length * 0.5f, 2f, length), Texture2D.whiteTexture);
                        break;
                    case 2:
                        GUI.DrawTexture(new Rect(center.x - 4f + k * 2f, center.y - length * 0.5f, 2f, length), Texture2D.whiteTexture);
                        break;
                    default:
                        GUI.DrawTexture(new Rect(center.x - length * 0.5f, center.y + 2f - k * 2f, length, 2f), Texture2D.whiteTexture);
                        break;
                }
            }
        }

        // ゴミ箱の印（ふたと持ち手、縦の筋の入った箱。赤い丸の上に白で描く）。
        private static void DrawTrash(Rect area, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.85f, 0.15f, 0.12f, 0.9f * alpha);
            GUI.DrawTexture(new Rect(area.x - 4f, area.y - 4f, area.width + 8f, area.height + 8f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float w = area.width;
            float h = area.height;
            // 持ち手とふた。
            GUI.DrawTexture(new Rect(area.x + w * 0.38f, area.y + h * 0.04f, w * 0.24f, h * 0.08f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(area.x + w * 0.12f, area.y + h * 0.14f, w * 0.76f, h * 0.1f), Texture2D.whiteTexture);
            // 箱（縁と縦の筋）。
            var box = new Rect(area.x + w * 0.2f, area.y + h * 0.3f, w * 0.6f, h * 0.64f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.yMax - 3f, box.width, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.y, 3f, box.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.xMax - 3f, box.y, 3f, box.height), Texture2D.whiteTexture);
            for (int i = 1; i <= 2; i++)
            {
                GUI.DrawTexture(new Rect(box.x + box.width * i / 3f - 1f, box.y + 5f, 2f, box.height - 10f), Texture2D.whiteTexture);
            }
            GUI.color = previous;
        }

        // 赤いバツ印（2 本の斜めの太い線）。
        private static void DrawCross(Rect cell, float alpha)
        {
            Matrix4x4 matrix = GUI.matrix;
            Vector2 center = cell.center;
            GUI.color = new Color(1f, 0.15f, 0.12f, alpha);
            foreach (float angle in new[] { 45f, -45f })
            {
                GUI.matrix = matrix;
                GUIUtility.RotateAroundPivot(angle, center);
                GUI.DrawTexture(new Rect(center.x - cell.width * 0.5f, center.y - 3.5f, cell.width, 7f), Texture2D.whiteTexture);
            }
            GUI.matrix = matrix;
        }
    }
}
