using Airerage.Runtime.Input;
using NUnit.Framework;
using UnityEngine;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// マウスポインターの位置を、ゲームプレイ平面（Z = 0）上の点に写す計算（マウスでの照準に使う）。
    /// </summary>
    public sealed class InputSystemAdapterTests
    {
        private GameObject _cameraObject;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("TestCamera");
            _camera = _cameraObject.AddComponent<Camera>();
            _cameraObject.transform.position = new Vector3(3f, 2f, -10f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_cameraObject);
        }

        [Test]
        public void ScreenCenter_ProjectsToPointInFrontOfCamera()
        {
            var center = new Vector2(_camera.pixelWidth * 0.5f, _camera.pixelHeight * 0.5f);

            Assert.That(InputSystemAdapter.TryProjectToGameplayPlane(_camera, center, out Vector2 world), Is.True);
            Assert.That(world.x, Is.EqualTo(3f).Within(1e-3f));
            Assert.That(world.y, Is.EqualTo(2f).Within(1e-3f));
        }

        [Test]
        public void ScreenCorner_MatchesScreenToWorldPointAtPlaneDepth()
        {
            var corner = new Vector2(_camera.pixelWidth, _camera.pixelHeight);
            Vector3 expected = _camera.ScreenToWorldPoint(new Vector3(corner.x, corner.y, 10f));

            Assert.That(InputSystemAdapter.TryProjectToGameplayPlane(_camera, corner, out Vector2 world), Is.True);
            Assert.That(world.x, Is.EqualTo(expected.x).Within(1e-3f));
            Assert.That(world.y, Is.EqualTo(expected.y).Within(1e-3f));
        }

        [Test]
        public void CameraFacingAway_ReturnsFalse()
        {
            _cameraObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var center = new Vector2(_camera.pixelWidth * 0.5f, _camera.pixelHeight * 0.5f);

            Assert.That(InputSystemAdapter.TryProjectToGameplayPlane(_camera, center, out _), Is.False);
        }
    }
}
