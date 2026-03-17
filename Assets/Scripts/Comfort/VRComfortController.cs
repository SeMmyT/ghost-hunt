using GhostHunt.Core;
using UnityEngine;

namespace GhostHunt.Comfort
{
    /// <summary>
    /// VR comfort features: tunneling vignette, snap turn, floating locomotion.
    /// Attach to VR camera rig. Only active on VR platform.
    /// </summary>
    public class VRComfortController : MonoBehaviour
    {
        [Header("Tunneling Vignette")]
        [SerializeField] private Material _vignetteMaterial;
        [SerializeField] private float _vignetteIntensity = GameConstants.VignetteIntensity;
        [SerializeField] private float _vignetteSmoothing = 8f;

        [Header("Snap Turn")]
        [SerializeField] private float _snapTurnAngle = GameConstants.SnapTurnDegrees;
        [SerializeField] private float _snapTurnCooldown = 0.3f;
        [SerializeField] private Transform _playerRig;

        private float _currentVignetteStrength;
        private float _snapTurnTimer;
        private Vector3 _lastPosition;

        private void Start()
        {
            _lastPosition = transform.position;

#if !UNITY_ANDROID && !UNITY_STANDALONE
            // Disable on non-VR platforms
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
                enabled = false;
#endif
        }

        private void Update()
        {
            UpdateVignette();
            UpdateSnapTurn();
        }

        private void UpdateVignette()
        {
            if (_vignetteMaterial == null) return;

            // Calculate movement speed for vignette intensity
            float speed = (transform.position - _lastPosition).magnitude / Time.deltaTime;
            _lastPosition = transform.position;

            // Ramp vignette with movement speed
            float targetStrength = Mathf.Clamp01(speed / GameConstants.MaxMoveSpeed) * _vignetteIntensity;
            _currentVignetteStrength = Mathf.Lerp(_currentVignetteStrength, targetStrength, Time.deltaTime * _vignetteSmoothing);

            _vignetteMaterial.SetFloat("_VignetteStrength", _currentVignetteStrength);
        }

        private void UpdateSnapTurn()
        {
            if (_playerRig == null) return;

            _snapTurnTimer -= Time.deltaTime;
            if (_snapTurnTimer > 0) return;

            // Right thumbstick horizontal for snap turn
            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
            {
                if (Mathf.Abs(axis.x) > 0.7f)
                {
                    float direction = Mathf.Sign(axis.x);
                    _playerRig.Rotate(0, _snapTurnAngle * direction, 0);
                    _snapTurnTimer = _snapTurnCooldown;
                }
            }
        }
    }
}
