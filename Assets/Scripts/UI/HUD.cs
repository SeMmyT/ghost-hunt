using GhostHunt.Core;
using GhostHunt.Network;
using UnityEngine;
using TMPro;

namespace GhostHunt.UI
{
    /// <summary>
    /// In-game HUD. Adapts layout based on platform and role.
    /// VR: world-space canvas attached to wrist or floating.
    /// PC/Console: screen-space overlay.
    /// Mobile: minimal touch-friendly overlay.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        [Header("Shared")]
        [SerializeField] private TMP_Text _timerText;
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private TMP_Text _phaseText;
        [SerializeField] private GameObject _radarPanel;

        [Header("Ghost-Specific")]
        [SerializeField] private TMP_Text _roleNameText;
        [SerializeField] private TMP_Text _burstCooldownText;
        [SerializeField] private GameObject _wailOverlay; // Full-screen tint during frightened mode

        [Header("Target-Specific")]
        [SerializeField] private TMP_Text _collectiblesText;
        [SerializeField] private TMP_Text _speedBoostText;
        [SerializeField] private TMP_Text _decoysText;

        [Header("Platform Panels")]
        [SerializeField] private GameObject _vrHUD;      // World-space canvas
        [SerializeField] private GameObject _flatHUD;    // Screen-space overlay
        [SerializeField] private GameObject _mobileHUD;  // Simplified touch overlay

        private GameManager _gameManager;
        private PlayerController _localPlayer;

        private void Start()
        {
            _gameManager = FindFirstObjectByType<GameManager>();
            ActivatePlatformHUD();
        }

        private void Update()
        {
            if (_gameManager == null) return;
            if (_localPlayer == null)
            {
                // Find local player
                foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                {
                    if (pc.HasInputAuthority)
                    {
                        _localPlayer = pc;
                        SetupRoleHUD();
                        break;
                    }
                }
                return;
            }

            UpdateSharedHUD();

            if (_localPlayer.State.Role == PlayerRole.Target)
                UpdateTargetHUD();
            else
                UpdateGhostHUD();
        }

        private void UpdateSharedHUD()
        {
            var state = _gameManager.State;

            // Timer
            float remaining = state.RoundTimer.RemainingTime(_gameManager.Runner) ?? 0;
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            _timerText.text = $"{minutes}:{seconds:D2}";

            // Score
            _scoreText.text = $"GHOSTS {state.GhostScore} | TARGET {state.TargetScore}";

            // Phase indicator
            _phaseText.text = state.Phase switch
            {
                GamePhase.Hunt => "",
                GamePhase.Frightened => "!! POWER PELLET !!",
                GamePhase.RoundEnd => "ROUND OVER",
                _ => ""
            };

            // Wail overlay
            if (_wailOverlay != null)
                _wailOverlay.SetActive(state.IsPowerPelletActive && _localPlayer.State.Role != PlayerRole.Target);
        }

        private void UpdateGhostHUD()
        {
            // Burst cooldown
            float burstRemaining = _localPlayer.State.BurstCooldown.RemainingTime(_gameManager.Runner) ?? 0;
            if (burstRemaining > 0)
                _burstCooldownText.text = $"BURST {burstRemaining:F1}s";
            else
                _burstCooldownText.text = "BURST READY";
        }

        private void UpdateTargetHUD()
        {
            var state = _gameManager.State;
            _collectiblesText.text = $"{state.CollectiblesRemaining}/{state.TotalCollectibles}";

            float speedRemaining = _localPlayer.Role.SpeedBoostTimer.RemainingTime(_gameManager.Runner) ?? 0;
            _speedBoostText.text = speedRemaining > 0 ? $"SPEED {speedRemaining:F1}s" : "";

            _decoysText.text = $"DECOYS: {_localPlayer.Role.DecoysRemaining}";
        }

        private void SetupRoleHUD()
        {
            if (_localPlayer.State.Role == PlayerRole.Target)
            {
                _roleNameText.text = "TARGET";
                if (_radarPanel != null) _radarPanel.SetActive(true); // Full radar for target
            }
            else
            {
                _roleNameText.text = _localPlayer.State.Role.ToString().ToUpperInvariant();
                if (_radarPanel != null) _radarPanel.SetActive(true); // Limited radar for ghosts
            }
        }

        private void ActivatePlatformHUD()
        {
#if UNITY_ANDROID
            if (UnityEngine.XR.XRSettings.isDeviceActive)
            {
                SetHUD(_vrHUD);
                return;
            }
            SetHUD(_mobileHUD);
#elif UNITY_IOS
            SetHUD(_mobileHUD);
#elif UNITY_STANDALONE
            if (UnityEngine.XR.XRSettings.isDeviceActive)
                SetHUD(_vrHUD);
            else
                SetHUD(_flatHUD);
#else
            SetHUD(_flatHUD);
#endif
        }

        private void SetHUD(GameObject activeHUD)
        {
            if (_vrHUD != null) _vrHUD.SetActive(_vrHUD == activeHUD);
            if (_flatHUD != null) _flatHUD.SetActive(_flatHUD == activeHUD);
            if (_mobileHUD != null) _mobileHUD.SetActive(_mobileHUD == activeHUD);
        }
    }
}
