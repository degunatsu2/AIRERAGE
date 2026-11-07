using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 画面のイメージカラーで塗る印（以前は水色だった文字と、それに付く線・印。2026-11-08 のユーザーの要望）。画面を出すときに
    /// AccentTint.Apply でその画面の色にする（濃さは元のまま）。キャラクター選択・ステージ選択・アイテムを選ぶ画面は、入ってきた項目
    /// （SINGLE・LOCAL・NETWORK・TRAINING・OPTIONS）の色になる。
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class AccentTint : MonoBehaviour
    {
        /// <summary>root の中の印をすべて accent にする。</summary>
        public static void Apply(Component root, Color accent)
        {
            if (root == null)
            {
                return;
            }
            foreach (AccentTint tint in root.GetComponentsInChildren<AccentTint>(true))
            {
                tint.Set(accent);
            }
        }

        /// <summary>graphic に印を付ける（シーンのビルダー用）。同じ graphic を返す。</summary>
        public static T Mark<T>(T graphic) where T : Graphic
        {
            if (graphic != null && graphic.GetComponent<AccentTint>() == null)
            {
                graphic.gameObject.AddComponent<AccentTint>();
            }
            return graphic;
        }

        /// <summary>この印の色を accent にする（濃さは元のまま）。</summary>
        public void Set(Color accent)
        {
            Graphic graphic = GetComponent<Graphic>();
            if (graphic == null)
            {
                return;
            }
            Color color = accent;
            color.a = graphic.color.a;
            graphic.color = color;
        }
    }
}
