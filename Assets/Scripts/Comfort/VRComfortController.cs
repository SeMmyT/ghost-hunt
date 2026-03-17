using GhostHunt.Core;
using UnityEngine;

namespace GhostHunt.Comfort
{
    /// <summary>
    /// VR comfort system: tunneling vignette, turn modes, teleport locomotion,
    /// height calibration. Attach to the XR Origin / camera rig.
    /// Only active when XR device is detected.
    ///
    /// All settings are exposed as serialized fields for runtime tuning
    /// via the debug server or a VR settings menu.
    /// </summary>
    public class VRComfortController : MonoBehaviour
    {
        // --- Settings (tunable at runtime) ---

        [Header("Comfort Profile")]
        [SerializeField] private ComfortLevel _comfortLevel = ComfortLevel.Moderate;

        [Header("Vignette")]
        [SerializeField] private Material _vignetteMaterial;
        [SerializeField] private float _moveVignetteIntensity = GameConstants.VignetteIntensity;
        [SerializeField] private float _turnVignetteIntensity = 0.6f;
        [SerializeField] private float _turnVignetteDuration = 0.15f;
        [SerializeField] private float _vignetteSmoothing = 8f;

        [Header("Turn Mode")]
        [SerializeField] private TurnMode _turnMode = TurnMode.Snap;
        [SerializeField] private float _snapTurnAngle = GameConstants.SnapTurnDegrees;
        [SerializeField] private float _snapTurnCooldown = 0.3f;
        [SerializeField] private float _smoothTurnSpeed = 90f;
        [SerializeField] private float _turnDeadzone = 0.3f;

        [Header("Locomotion")]
        [SerializeField] private LocomotionMode _locomotionMode = LocomotionMode.Continuous;
        [SerializeField] private float _teleportMaxRange = 8f;
        [SerializeField] private float _teleportFadeDuration = 0.15f;
        [SerializeField] private LayerMask _teleportLayerMask = ~0;
        [SerializeField] private LineRenderer _teleportArc;
        [SerializeField] private GameObject _teleportReticle;

        [Header("Height")]
        [SerializeField] private float _heightOffset;
        [SerializeField] private float _seatedHeightOffset = 0.5f;
        [SerializeField] private bool _seatedMode;

        [Header("References")]
        [SerializeField] private Transform _playerRig;
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private CanvasGroup _fadePanelCanvasGroup;

        // --- Internal state ---

        private float _currentMoveVignette;
        private float _currentTurnVignette;
        private float _snapTurnTimer;
        private Vector3 _lastPosition;
        private float _lastYaw;
        private bool _isTeleporting;
        private float _teleportFadeTimer;
        private Vector3 _teleportTarget;
        private float _calibratedFloorY;
        private bool _isAiming;

        public TurnMode CurrentTurnMode => _turnMode;
        public LocomotionMode CurrentLocomotionMode => _locomotionMode;
        public bool IsSeated => _seatedMode;

        private void Start()
        {
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
            {
                enabled = false;
                return;
            }

            _lastPosition = transform.position;
            _lastYaw = GetRigYaw();
            _calibratedFloorY = _playerRig != null ? _playerRig.position.y : 0f;

            ApplyComfortProfile(_comfortLevel);

            if (_teleportReticle != null) _teleportReticle.SetActive(false);
            if (_teleportArc != null) _teleportArc.enabled = false;

            Debug.Log($"[VRComfort] Active — turn:{_turnMode} loco:{_locomotionMode} seated:{_seatedMode}");
        }

        private void Update()
        {
            UpdateVignette();
            UpdateTurn();
            UpdateTeleport();
            UpdateHeight();
        }

        // --- Vignette ---

        private void UpdateVignette()
        {
            if (_vignetteMaterial == null) return;

            // Movement vignette — ramps with locomotion speed
            float speed = (transform.position - _lastPosition).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            _lastPosition = transform.position;

            float moveTarget = Mathf.Clamp01(speed / GameConstants.MaxMoveSpeed) * _moveVignetteIntensity;
            _currentMoveVignette = Mathf.Lerp(_currentMoveVignette, moveTarget, Time.deltaTime * _vignetteSmoothing);

            // Turn vignette — pulse on snap turn, sustained during smooth turn
            float yaw = GetRigYaw();
            float yawDelta = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw));
            _lastYaw = yaw;

            if (yawDelta > 1f) // Turning
                _currentTurnVignette = _turnVignetteIntensity;
            else
                _currentTurnVignette = Mathf.Lerp(_currentTurnVignette, 0f, Time.deltaTime / _turnVignetteDuration);

            float combined = Mathf.Max(_currentMoveVignette, _currentTurnVignette);
            _vignetteMaterial.SetFloat("_VignetteStrength", combined);
        }

        // --- Turn modes ---

        private void UpdateTurn()
        {
            if (_playerRig == null) return;

            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (!rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                return;

            switch (_turnMode)
            {
                case TurnMode.Snap:
                    UpdateSnapTurn(axis.x);
                    break;
                case TurnMode.Smooth:
                    UpdateSmoothTurn(axis.x);
                    break;
                case TurnMode.Disabled:
                    break;
            }
        }

        private void UpdateSnapTurn(float input)
        {
            _snapTurnTimer -= Time.deltaTime;
            if (_snapTurnTimer > 0) return;

            if (Mathf.Abs(input) > 0.7f)
            {
                float dir = Mathf.Sign(input);
                _playerRig.Rotate(0, _snapTurnAngle * dir, 0);
                _snapTurnTimer = _snapTurnCooldown;
            }
        }

        private void UpdateSmoothTurn(float input)
        {
            if (Mathf.Abs(input) < _turnDeadzone) return;

            // Remap past deadzone to 0-1
            float remapped = (Mathf.Abs(input) - _turnDeadzone) / (1f - _turnDeadzone);
            float rotation = Mathf.Sign(input) * remapped * _smoothTurnSpeed * Time.deltaTime;
            _playerRig.Rotate(0, rotation, 0);
        }

        // --- Teleport locomotion ---

        private void UpdateTeleport()
        {
            if (_locomotionMode != LocomotionMode.Teleport) return;

            // Fade during teleport
            if (_isTeleporting)
            {
                _teleportFadeTimer -= Time.deltaTime;
                float t = 1f - (_teleportFadeTimer / _teleportFadeDuration);

                if (t < 0.5f)
                {
                    // Fading out
                    SetFade(t * 2f);
                }
                else if (t < 0.5f + 0.01f)
                {
                    // At peak fade — move player
                    if (_playerRig != null)
                    {
                        Vector3 offset = _playerRig.position - _cameraTransform.position;
                        offset.y = 0;
                        _playerRig.position = _teleportTarget + offset;
                    }
                }
                else
                {
                    // Fading back in
                    SetFade(2f * (1f - t));
                }

                if (_teleportFadeTimer <= 0)
                {
                    _isTeleporting = false;
                    SetFade(0f);
                }
                return;
            }

            // Aim with left thumbstick push
            var leftHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            bool thumbstickPressed = false;
            if (leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out bool clicked))
                thumbstickPressed = clicked;

            if (thumbstickPressed && !_isAiming)
            {
                _isAiming = true;
                if (_teleportArc != null) _teleportArc.enabled = true;
            }
            else if (!thumbstickPressed && _isAiming)
            {
                _isAiming = false;
                if (_teleportArc != null) _teleportArc.enabled = false;
                if (_teleportReticle != null && _teleportReticle.activeSelf)
                {
                    // Execute teleport
                    ExecuteTeleport(_teleportReticle.transform.position);
                }
                if (_teleportReticle != null) _teleportReticle.SetActive(false);
            }

            if (_isAiming)
                UpdateTeleportArc();
        }

        private void UpdateTeleportArc()
        {
            if (_cameraTransform == null) return;

            Vector3 origin = _cameraTransform.position;
            Vector3 forward = _cameraTransform.forward;

            // Simple raycast (could upgrade to parabolic arc)
            if (Physics.Raycast(origin, forward, out RaycastHit hit, _teleportMaxRange, _teleportLayerMask))
            {
                // Only teleport to walkable surfaces (floors, not walls)
                if (Vector3.Angle(hit.normal, Vector3.up) < 30f)
                {
                    if (_teleportReticle != null)
                    {
                        _teleportReticle.SetActive(true);
                        _teleportReticle.transform.position = hit.point;
                    }

                    if (_teleportArc != null)
                    {
                        _teleportArc.positionCount = 2;
                        _teleportArc.SetPosition(0, origin);
                        _teleportArc.SetPosition(1, hit.point);
                    }
                    return;
                }
            }

            // No valid target
            if (_teleportReticle != null) _teleportReticle.SetActive(false);
            if (_teleportArc != null)
            {
                _teleportArc.positionCount = 2;
                _teleportArc.SetPosition(0, origin);
                _teleportArc.SetPosition(1, origin + forward * _teleportMaxRange);
            }
        }

        private void ExecuteTeleport(Vector3 target)
        {
            _isTeleporting = true;
            _teleportTarget = target;
            _teleportFadeTimer = _teleportFadeDuration * 2f; // Full cycle: fade out + fade in
        }

        private void SetFade(float alpha)
        {
            if (_fadePanelCanvasGroup != null)
            {
                _fadePanelCanvasGroup.alpha = alpha;
                _fadePanelCanvasGroup.blocksRaycasts = alpha > 0.5f;
            }
        }

        // --- Height calibration ---

        private void UpdateHeight()
        {
            if (_playerRig == null) return;

            float offset = _seatedMode ? _seatedHeightOffset + _heightOffset : _heightOffset;
            if (Mathf.Abs(offset) > 0.01f)
            {
                var pos = _playerRig.localPosition;
                pos.y = _calibratedFloorY + offset;
                _playerRig.localPosition = pos;
            }
        }

        /// <summary>
        /// Calibrate floor height to current headset position.
        /// Called from settings menu or via debug command.
        /// </summary>
        public void CalibrateHeight()
        {
            if (_cameraTransform != null && _playerRig != null)
            {
                _calibratedFloorY = _playerRig.position.y;
                _heightOffset = 0f;
                Debug.Log($"[VRComfort] Height calibrated at {_calibratedFloorY:F2}m");
            }
        }

        // --- Comfort profiles ---

        /// <summary>
        /// Apply a predefined comfort profile. Can be changed at runtime.
        /// </summary>
        public void ApplyComfortProfile(ComfortLevel level)
        {
            _comfortLevel = level;

            switch (level)
            {
                case ComfortLevel.Full:
                    // No comfort aids — experienced VR user
                    _turnMode = TurnMode.Smooth;
                    _locomotionMode = LocomotionMode.Continuous;
                    _moveVignetteIntensity = 0f;
                    _turnVignetteIntensity = 0f;
                    break;

                case ComfortLevel.Moderate:
                    // Snap turn + light vignette — most players
                    _turnMode = TurnMode.Snap;
                    _locomotionMode = LocomotionMode.Continuous;
                    _moveVignetteIntensity = GameConstants.VignetteIntensity;
                    _turnVignetteIntensity = 0.5f;
                    _snapTurnAngle = GameConstants.SnapTurnDegrees;
                    break;

                case ComfortLevel.Maximum:
                    // Teleport + snap turn + heavy vignette — sensitive users
                    _turnMode = TurnMode.Snap;
                    _locomotionMode = LocomotionMode.Teleport;
                    _moveVignetteIntensity = 0.7f;
                    _turnVignetteIntensity = 0.8f;
                    _snapTurnAngle = 30f;
                    break;
            }

            Debug.Log($"[VRComfort] Profile: {level}");
        }

        // --- Public setters for runtime tuning ---

        public void SetTurnMode(TurnMode mode) => _turnMode = mode;
        public void SetLocomotionMode(LocomotionMode mode) => _locomotionMode = mode;
        public void SetSeatedMode(bool seated) => _seatedMode = seated;
        public void SetHeightOffset(float offset) => _heightOffset = offset;
        public void SetSnapAngle(float degrees) => _snapTurnAngle = degrees;
        public void SetSmoothTurnSpeed(float degreesPerSecond) => _smoothTurnSpeed = degreesPerSecond;

        private float GetRigYaw()
        {
            return _playerRig != null ? _playerRig.eulerAngles.y : 0f;
        }
    }

    // --- Enums ---

    public enum ComfortLevel
    {
        Full,       // No comfort aids (experienced VR user)
        Moderate,   // Snap turn + light vignette (default)
        Maximum     // Teleport + heavy vignette (motion-sensitive)
    }

    public enum TurnMode
    {
        Snap,       // Instant rotation in fixed increments
        Smooth,     // Continuous rotation (can cause nausea)
        Disabled    // No artificial turn (room-scale only)
    }

    public enum LocomotionMode
    {
        Continuous, // Thumbstick walking (standard)
        Teleport    // Point-and-teleport with fade
    }
}
