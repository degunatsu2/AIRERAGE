namespace Degunatsu.Core.Tests.Support
{
    /// <summary>
    /// 複数のテストで使う地形。
    /// </summary>
    public static class TestArenas
    {
        /// <summary>地面（上面 y = 0）だけ。</summary>
        public static BoxWorld Ground()
        {
            return new BoxWorld().AddBox(-100f, -1f, 100f, 0f);
        }

        /// <summary>
        /// 地面、接続できる天井（下面 y = 10）、左右の壁、細い壁、浮いた足場、アンカーポイントを含む遊び場。
        /// </summary>
        public static BoxWorld Playground()
        {
            return new BoxWorld()
                .AddBox(-30f, -1f, 30f, 0f)                       // 地面
                .AddBox(-30f, 10f, 30f, 11f, attachable: true)    // 天井（接続できる）
                .AddBox(-31f, -1f, -30f, 11f)                     // 左の壁
                .AddBox(30f, -1f, 31f, 11f)                       // 右の壁
                .AddBox(8f, 0f, 8.2f, 6f)                         // 細い壁
                .AddBox(-4f, 4f, -1f, 4.5f, attachable: true)     // 浮いた足場
                .AddAnchor(3f, 7f)
                .AddAnchor(-10f, 6f);
        }
    }
}
