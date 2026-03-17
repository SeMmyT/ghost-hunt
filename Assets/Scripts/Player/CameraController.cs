using GhostHunt.Core;
using UnityEngine;

namespace GhostHunt.Player
{
    /// <summary>
    /// Platform-adaptive camera. Activates the right view based on device.
    /// VR: handled by XR Origin (this script deactivates).
    /// PC: third-person follow cam with mouse look.
    /// Mobile/Browser: top-down orthographic.
    /// Console: third-person with right-stick look.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Camera References")]
        [SerializeField] private Camera _thirdPersonCam;
        [SerializeField] private Camera _topDownCam;
        [SerializeField] private GameObject _xrOrigin; // Disabled on flat platforms

        [Header("Third Person Settings")]
        [SerializeField] private float _followDistance = 8f;
        [SerializeField] private float _followHeight = 6f;
        [SerializeField] private float _followSmoothing = 5f;
        [SerializeField] private float _mouseSensitivity = 2f;

        [Header("Top Down Settings")]
        [SerializeField] private float _topDownHeight = 20f;
        [SerializeField] private float _topDownSize = 15f;

        private Transform _target;
        private PlatformType _platform;
        private float _yaw;
        private float _pitch = 30f;

        public void Initialize(Transform target, PlatformType platform)
        {
            _target = target;
            _platform = platform;

            switch (platform)
            {
                case PlatformType.VR:
                    // VR camera handled by XR Origin
                    if (_thirdPersonCam != null) _thirdPersonCam.gameObject.SetActive(false);
                    if (_topDownCam != null) _topDownCam.gameObject.SetActive(false);
                    if (_xrOrigin != null) _xrOrigin.SetActive(true);
                    break;

                case PlatformType.PC:
                case PlatformType.Console:
                    if (_thirdPersonCam != null) _thirdPersonCam.gameObject.SetActive(true);
                    if (_topDownCam != null) _topDownCam.gameObject.SetActive(false);
                    if (_xrOrigin != null) _xrOrigin.SetActive(false);
                    Cursor.lockState = CursorLockMode.Locked;
                    break;

                case PlatformType.Mobile:
                case PlatformType.Browser:
                    if (_thirdPersonCam != null) _thirdPersonCam.gameObject.SetActive(false);
                    if (_topDownCam != null)
                    {
                        _topDownCam.gameObject.SetActive(true);
                        _topDownCam.orthographic = true;
                        _topDownCam.orthographicSize = _topDownSize;
                    }
                    if (_xrOrigin != null) _xrOrigin.SetActive(false);
                    break;
            }
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            switch (_platform)
            {
                case PlatformType.PC:
                    UpdateThirdPerson(true);
                    break;
                case PlatformType.Console:
                    UpdateThirdPerson(false);
                    break;
                case PlatformType.Mobile:
                case PlatformType.Browser:
                    UpdateTopDown();
                    break;
                    // VR: no update needed, XR Origin handles tracking
            }
        }

        private void UpdateThirdPerson(bool useMouse)
        {
            if (_thirdPersonCam == null) return;

            if (useMouse)
            {
                _yaw += Input.GetAxis("Mouse X") * _mouseSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * _mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch, 10f, 80f);
            }
            else
            {
                // Console: right stick
                _yaw += Input.GetAxis("RightStickHorizontal") * _mouseSensitivity * 2f;
                _pitch -= Input.GetAxis("RightStickVertical") * _mouseSensitivity * 2f;
                _pitch = Mathf.Clamp(_pitch, 10f, 80f);
            }

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0);
            Vector3 offset = rotation * new Vector3(0, 0, -_followDistance);
            offset.y += _followHeight;

            Vector3 targetPos = _target.position + offset;
            _thirdPersonCam.transform.position = Vector3.Lerp(
                _thirdPersonCam.transform.position, targetPos,
                Time.deltaTime * _followSmoothing
            );
            _thirdPersonCam.transform.LookAt(_target.position + Vector3.up * 1.5f);
        }

        private void UpdateTopDown()
        {
            if (_topDownCam == null) return;

            Vector3 targetPos = _target.position + Vector3.up * _topDownHeight;
            _topDownCam.transform.position = Vector3.Lerp(
                _topDownCam.transform.position, targetPos,
                Time.deltaTime * _followSmoothing
            );
            _topDownCam.transform.rotation = Quaternion.Euler(90, 0, 0);
        }
    }
}
