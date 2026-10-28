using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ローカル対戦の頭上の札（ユーザーの要望、2026-10-22: 全員が入るカメラでは自分のキャラクターを見失いやすい）。キャラクターの頭の上に、
    /// プレイヤーの色の枠の黒い四角と、プレイヤーの番号（ISimulationFeed.LabelOf。P1・CPU2 など）を出す。ローカル対戦のシーンにだけ置く。
    /// 撃破されて復帰を待つ間と、抜けた人には出さない。位置は表示の箱（CharacterView。補間した位置）に合わせる。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class PlayerTagHud : MonoBehaviour
    {
        [Tooltip("状態を読む相手（LocalMatchRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("キャラクターの表示（番号の順。頭の位置に使う）")]
        [SerializeField] private CharacterView[] _views = new CharacterView[0];
        [Tooltip("画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("頭の上から札の下の端までの高さ（m）")]
        [SerializeField] private float _headGap = 0.45f;
        [Tooltip("札の高さ（画面の点）と、文字の左右の余白（点）")]
        [SerializeField] private float _height = 30f;
        [SerializeField] private float _padding = 10f;
        [Tooltip("枠の太さ（画面の点）")]
        [SerializeField] private float _border = 3f;
        [SerializeField] private int _fontSize = 18;

        private ISimulationFeed _feed;
        private GUIStyle _style;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
                return;
            }
            if (_camera == null)
            {
                Debug.LogError($"{name}: カメラが設定されていません", this);
                enabled = false;
            }
        }

        /// <summary>キャラクター index の札を出すなら true と、画面の位置（GUI の座標）・文字・枠の色（テスト用）。</summary>
        public bool TryGetTag(int index, out Rect rect, out string label, out Color color)
        {
            rect = default;
            label = null;
            color = default;
            if (_feed == null || index < 0 || index >= _feed.CharacterCount || index >= _views.Length || _views[index] == null)
            {
                return false;
            }
            CharacterState state = _feed.GetState(index);
            if (state.Combat.IsWaitingToRespawn)
            {
                return false;
            }
            float halfHeight = _feed.GetConfig(index).Movement.HalfHeight;
            Vector3 body = _views[index].transform.position;
            if (state.Combat.InvisibleTicksLeft > 0)
            {
                // 透明の間（THIEF の E）は、分身の上に出す（分身がいなければ出さない）。
                if (!_views[index].ShowingClone)
                {
                    return false;
                }
                body = _views[index].ClonePosition;
            }
            Vector3 head = body + Vector3.up * (halfHeight + _headGap);
            Vector3 screen = _camera.WorldToScreenPoint(head);
            if (screen.z <= 0f)
            {
                return false;
            }
            label = _feed.LabelOf(index);
            color = _feed.ColorOf(index);
            // 幅は文字の数から決める（GUI の関数は OnGUI の中でしか使えないため。テストからも呼ぶ）。
            float width = Mathf.Max(_height * 1.6f, label.Length * _fontSize * 0.62f + _padding * 2f);
            float bottom = Screen.height - screen.y;
            rect = new Rect(screen.x - width * 0.5f, bottom - _height, width, _height);
            return true;
        }

        private void OnGUI()
        {
            if (_feed == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            Color previous = GUI.color;
            for (int i = 0; i < _feed.CharacterCount; i++)
            {
                if (!TryGetTag(i, out Rect rect, out string label, out Color color))
                {
                    continue;
                }
                // プレイヤーの色の枠、黒い四角、白い番号。
                GUI.color = new Color(color.r, color.g, color.b, 1f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(rect.x + _border, rect.y + _border, rect.width - _border * 2f, rect.height - _border * 2f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(rect, label, Style());
            }
            GUI.color = previous;
        }

        private GUIStyle Style()
        {
            return _style ?? (_style = TextFlash.Style(_fontSize, FontStyle.Bold, TextAnchor.MiddleCenter));
        }
    }
}
