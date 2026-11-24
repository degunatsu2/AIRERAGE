using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 画面の衝撃波（ScreenShockwaves）を毎フレーム画面の値にしてシェーダーへ入れる、隠れた物（ScreenShockwaves が最初の波を出したときに
    /// 作る）。カメラは MainCamera のタグのカメラ（なければ、最初に見つけた動いているカメラ）。
    /// </summary>
    internal sealed class ScreenShockwaveDriver : MonoBehaviour
    {
        private Camera _camera;
        private float _nextSearch;

        private void LateUpdate()
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
                if (_camera == null && Time.unscaledTime >= _nextSearch)
                {
                    _nextSearch = Time.unscaledTime + 1f;
                    _camera = FindFirstObjectByType<Camera>();
                }
            }
            ScreenShockwaves.Submit(_camera);
        }

        private void OnDisable()
        {
            ScreenShockwaves.Clear();
        }
    }
}
