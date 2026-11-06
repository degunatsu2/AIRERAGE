namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// ゲームパッドの ITEM MENU（X）の、この PC で操作しているキャラクターの今の状態（頭上のアイテムバッグの表示用。docs/20_item_plan.md）。
    /// </summary>
    public struct LocalItemMenu
    {
        /// <summary>ITEM MENU を押しているか。</summary>
        public bool Held;

        /// <summary>十字キーで枠を選んだ回数（増えたら選んだ）と、最後に選んだ枠（0〜2）、そのとき捨てる（A）だったか。</summary>
        public int Choices;
        public int Slot;
        public bool Drop;

        /// <summary>ITEM MENU と一緒に捨てるボタン（A）を押しているか（バッグの左にゴミ箱の印を出す）。</summary>
        public bool DropHeld;
    }

    /// <summary>この PC で操作しているキャラクターの ITEM MENU を読める（SimulationRunner・LocalMatchRunner・OnlineRunner）。</summary>
    public interface ILocalItemMenu
    {
        /// <summary>キャラクター index をこの PC のゲームパッドで操作していれば、ITEM MENU の状態を入れて true。</summary>
        bool TryGetItemMenu(int index, out LocalItemMenu menu);
    }
}
