using GhostHunt.Core;
using GhostHunt.Network;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace GhostHunt.UI
{
    /// <summary>
    /// Lobby UI: room code display, player list, role selection, ready button.
    /// Works on all platforms. Layout adapts via Canvas Scaler.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [Header("Room Code")]
        [SerializeField] private TMP_Text _roomCodeText;
        [SerializeField] private TMP_InputField _joinCodeInput;
        [SerializeField] private Button _createRoomButton;
        [SerializeField] private Button _joinRoomButton;

        [Header("Player List")]
        [SerializeField] private Transform _playerListContainer;
        [SerializeField] private GameObject _playerSlotPrefab;

        [Header("Role Selection")]
        [SerializeField] private Button _chaserButton;
        [SerializeField] private Button _ambusherButton;
        [SerializeField] private Button _flankerButton;
        [SerializeField] private Button _wildcardButton;
        [SerializeField] private Button _targetButton;

        [Header("Ready")]
        [SerializeField] private Button _readyButton;
        [SerializeField] private TMP_Text _readyButtonText;
        [SerializeField] private TMP_Text _countdownText;

        [Header("Panels")]
        [SerializeField] private GameObject _joinPanel;
        [SerializeField] private GameObject _lobbyPanel;

        private RoomManager _roomManager;
        private LobbyManager _lobbyManager;
        private bool _isReady;

        private void Start()
        {
            _roomManager = FindFirstObjectByType<RoomManager>();

            _createRoomButton.onClick.AddListener(OnCreateRoom);
            _joinRoomButton.onClick.AddListener(OnJoinRoom);
            _readyButton.onClick.AddListener(OnToggleReady);

            _chaserButton.onClick.AddListener(() => RequestRole(PlayerRole.Chaser));
            _ambusherButton.onClick.AddListener(() => RequestRole(PlayerRole.Ambusher));
            _flankerButton.onClick.AddListener(() => RequestRole(PlayerRole.Flanker));
            _wildcardButton.onClick.AddListener(() => RequestRole(PlayerRole.Wildcard));
            _targetButton.onClick.AddListener(() => RequestRole(PlayerRole.Target));

            _roomManager.OnRoomCreated += OnRoomCreated;
            _roomManager.OnRoomJoined += OnRoomJoined;
            _roomManager.OnRoomError += OnRoomError;

            ShowJoinPanel();
        }

        private async void OnCreateRoom()
        {
            _createRoomButton.interactable = false;
            await _roomManager.CreateRoom();
        }

        private async void OnJoinRoom()
        {
            string code = _joinCodeInput.text.Trim().ToUpperInvariant();
            if (code.Length != 6)
            {
                Debug.LogWarning("[LobbyUI] Room code must be 6 characters");
                return;
            }

            _joinRoomButton.interactable = false;
            await _roomManager.JoinRoom(code);
        }

        private void OnRoomCreated(string code)
        {
            _roomCodeText.text = FormatRoomCode(code);
            ShowLobbyPanel();
        }

        private void OnRoomJoined()
        {
            _roomCodeText.text = FormatRoomCode(_roomManager.RoomCode);
            ShowLobbyPanel();
        }

        private void OnRoomError(string error)
        {
            Debug.LogError($"[LobbyUI] Room error: {error}");
            _createRoomButton.interactable = true;
            _joinRoomButton.interactable = true;
        }

        private void RequestRole(PlayerRole role)
        {
            if (_lobbyManager == null) return;
            // TODO: Get local PlayerRef and call RPC
            // _lobbyManager.RPC_RequestRole(localPlayer, role);
        }

        private void OnToggleReady()
        {
            _isReady = !_isReady;
            _readyButtonText.text = _isReady ? "CANCEL" : "READY";

            if (_lobbyManager == null) return;
            // TODO: Get local PlayerRef and call RPC
            // _lobbyManager.RPC_ToggleReady(localPlayer);
        }

        private void Update()
        {
            if (_lobbyManager == null)
            {
                _lobbyManager = FindFirstObjectByType<LobbyManager>();
                return;
            }

            UpdatePlayerList();
            UpdateCountdown();
        }

        private void UpdatePlayerList()
        {
            // Clear existing slots
            foreach (Transform child in _playerListContainer)
                Destroy(child.gameObject);

            // Create slot for each lobby player
            for (int i = 0; i < _lobbyManager.Players.Count; i++)
            {
                var player = _lobbyManager.Players[i];
                var slot = Instantiate(_playerSlotPrefab, _playerListContainer);

                var nameText = slot.transform.Find("Name")?.GetComponent<TMP_Text>();
                var roleText = slot.transform.Find("Role")?.GetComponent<TMP_Text>();
                var platformText = slot.transform.Find("Platform")?.GetComponent<TMP_Text>();
                var readyIndicator = slot.transform.Find("Ready")?.GetComponent<Image>();

                if (nameText != null) nameText.text = player.DisplayName.ToString();
                if (roleText != null) roleText.text = player.AssignedRole.ToString();
                if (platformText != null) platformText.text = PlatformIcon(player.Platform);
                if (readyIndicator != null) readyIndicator.color = player.IsReady ? Color.green : Color.gray;
            }
        }

        private void UpdateCountdown()
        {
            if (_lobbyManager.AllReady && _lobbyManager.CountdownSeconds > 0)
            {
                _countdownText.gameObject.SetActive(true);
                _countdownText.text = _lobbyManager.CountdownSeconds.ToString();
            }
            else
            {
                _countdownText.gameObject.SetActive(false);
            }
        }

        private void ShowJoinPanel()
        {
            _joinPanel.SetActive(true);
            _lobbyPanel.SetActive(false);
        }

        private void ShowLobbyPanel()
        {
            _joinPanel.SetActive(false);
            _lobbyPanel.SetActive(true);
        }

        private static string FormatRoomCode(string code)
        {
            // Display as "ABC-DEF" for readability
            if (code.Length == 6)
                return $"{code[..3]}-{code[3..]}";
            return code;
        }

        private static string PlatformIcon(PlatformType platform)
        {
            return platform switch
            {
                PlatformType.VR => "[VR]",
                PlatformType.PC => "[PC]",
                PlatformType.Mobile => "[MOB]",
                PlatformType.Console => "[CON]",
                PlatformType.Browser => "[WEB]",
                _ => "[?]"
            };
        }
    }
}
