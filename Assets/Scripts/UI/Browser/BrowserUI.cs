using System.Runtime.InteropServices;
using GhostHunt.Core;
using GhostHunt.Network;
using GhostHunt.Network.Transport;
using UnityEngine;
using TMPro;

namespace GhostHunt.UI.Browser
{
    /// <summary>
    /// Browser-specific UI layer. Handles:
    /// - Zero-install join flow (room code from URL params)
    /// - Latency indicator with transport type
    /// - Smart role suggestion (target role for high-latency, ghost for low)
    /// - Touch-friendly controls for mobile browsers
    /// - Fullscreen toggle
    /// </summary>
    public class BrowserUI : MonoBehaviour
    {
        [Header("Join Flow")]
        [SerializeField] private GameObject _joinOverlay;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _transportIndicator;

        [Header("Latency")]
        [SerializeField] private TMP_Text _latencyText;
        [SerializeField] private TMP_Text _roleSuggestion;

        [Header("Controls")]
        [SerializeField] private GameObject _touchControlsOverlay;
        [SerializeField] private GameObject _virtualJoystick;
        [SerializeField] private GameObject _actionButton;
        [SerializeField] private GameObject _secondaryButton;

        private RoomManager _roomManager;
        private WebTransportBridge _transport;
        private bool _isMobileBrowser;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string GetURLParameter(string name);

        [DllImport("__Internal")]
        private static extern int IsMobileDevice();

        [DllImport("__Internal")]
        private static extern void RequestFullscreen();

        [DllImport("__Internal")]
        private static extern void CopyToClipboard(string text);

        [DllImport("__Internal")]
        private static extern void SetPageTitle(string title);
#endif

        private void Start()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            // Not a browser build — disable this component
            gameObject.SetActive(false);
            return;
#endif

            _roomManager = FindFirstObjectByType<RoomManager>();
            _transport = FindFirstObjectByType<WebTransportBridge>();

            DetectMobileBrowser();
            CheckURLRoomCode();
        }

        /// <summary>
        /// Check if the URL contains a room code (e.g., ?room=ABC-DEF).
        /// If so, auto-join without showing the join UI.
        /// </summary>
        private void CheckURLRoomCode()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string roomCode = GetURLParameter("room");
            if (!string.IsNullOrEmpty(roomCode))
            {
                // Strip dashes for raw code
                roomCode = roomCode.Replace("-", "").ToUpperInvariant().Trim();
                if (roomCode.Length == 6)
                {
                    _statusText.text = $"Joining {roomCode[..3]}-{roomCode[3..]}...";
                    _roomManager.JoinRoom(roomCode);
                    return;
                }
            }
#endif
            // No room code in URL — show join UI
            _joinOverlay.SetActive(true);
        }

        private void DetectMobileBrowser()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _isMobileBrowser = IsMobileDevice() == 1;
#else
            _isMobileBrowser = false;
#endif

            if (_isMobileBrowser)
            {
                _touchControlsOverlay.SetActive(true);
                _virtualJoystick.SetActive(true);
                _actionButton.SetActive(true);
                _secondaryButton.SetActive(true);
            }
        }

        private void Update()
        {
            UpdateLatencyDisplay();
            UpdateRoleSuggestion();
        }

        private void UpdateLatencyDisplay()
        {
            if (_transport == null) return;

            float rtt = _transport.CurrentRTT;
            string transportType = _transport.IsWebTransport ? "WebTransport" : "WebSocket";

            _latencyText.text = $"{rtt:F0}ms";
            _transportIndicator.text = transportType;

            // Color code: green < 80ms, yellow < 150ms, red > 150ms
            if (rtt < 80)
                _latencyText.color = Color.green;
            else if (rtt < 150)
                _latencyText.color = Color.yellow;
            else
                _latencyText.color = Color.red;
        }

        /// <summary>
        /// Suggest optimal role based on current latency.
        /// Target role is strategic (top-down, less twitch) — better for high latency.
        /// Ghost roles need fast reactions — better for low latency.
        /// </summary>
        private void UpdateRoleSuggestion()
        {
            if (_transport == null || _roleSuggestion == null) return;

            float rtt = _transport.CurrentRTT;

            if (rtt > 100)
            {
                _roleSuggestion.text = "SUGGESTED: TARGET (strategic view, latency-friendly)";
                _roleSuggestion.color = Color.yellow;
            }
            else if (rtt > 60)
            {
                _roleSuggestion.text = "SUGGESTED: WILDCARD (area control, less twitch)";
                _roleSuggestion.color = new Color(0.8f, 0.6f, 0.2f);
            }
            else
            {
                _roleSuggestion.text = "ALL ROLES AVAILABLE (low latency)";
                _roleSuggestion.color = Color.green;
            }
        }

        /// <summary>
        /// Generate a shareable room link for the browser version.
        /// Called from lobby UI "Share Link" button.
        /// </summary>
        public void ShareRoomLink(string roomCode)
        {
            // Format: https://ghosthunt.example.com/?room=ABC-DEF
            string formatted = roomCode.Length == 6
                ? $"{roomCode[..3]}-{roomCode[3..]}"
                : roomCode;

            string link = $"https://play.ghosthunt.game/?room={formatted}";

#if UNITY_WEBGL && !UNITY_EDITOR
            CopyToClipboard(link);
            SetPageTitle($"Ghost Hunt — Room {formatted}");
#endif

            Debug.Log($"[Browser] Room link copied: {link}");
        }

        public void ToggleFullscreen()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RequestFullscreen();
#endif
        }
    }
}
