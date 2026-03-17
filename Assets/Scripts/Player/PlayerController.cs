using Fusion;
using GhostHunt.Core;
using GhostHunt.Network;
using UnityEngine;

namespace GhostHunt.Player
{
    /// <summary>
    /// Cross-platform player controller. Handles movement for all device types.
    /// Client-authoritative for own position — host validates catches via server hitbox.
    /// </summary>
    public class PlayerController : NetworkBehaviour, IPlayerStateAccess
    {
        [Networked] public PlayerState State { get; set; }
        [Networked] public RoleData Role { get; set; }

        [SerializeField] private float _moveSpeed = GameConstants.GhostBaseSpeed;
        [SerializeField] private CharacterController _characterController;

        private PlatformInputProvider _inputProvider;
        private float _yaw;

        public override void Spawned()
        {
            if (HasInputAuthority)
            {
                _inputProvider = PlatformInputProvider.Create(DetectPlatform());
                Debug.Log($"[PlayerController] Spawned on {_inputProvider.Platform}");
            }

            // Initialize state
            if (Runner.IsServer)
            {
                State = new PlayerState
                {
                    Role = PlayerRole.None, // Assigned by lobby
                    Platform = HasInputAuthority ? DetectPlatform() : PlatformType.PC,
                    IsAlive = true,
                    MoveSpeed = _moveSpeed
                };
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasInputAuthority || _inputProvider == null) return;

            // Movement — relative to current facing on flat platforms, absolute on VR/top-down
            var moveInput = _inputProvider.GetMoveInput();
            Vector3 move;

            if (_inputProvider.Platform == PlatformType.VR ||
                _inputProvider.Platform == PlatformType.Mobile ||
                _inputProvider.Platform == PlatformType.Browser)
            {
                // World-space movement (VR uses head direction, top-down is absolute)
                move = new Vector3(moveInput.x, 0, moveInput.y);
            }
            else
            {
                // Camera-relative movement for PC/Console third-person
                move = transform.forward * moveInput.y + transform.right * moveInput.x;
                move.y = 0;
                if (move.sqrMagnitude > 1f) move.Normalize();
            }

            move *= State.MoveSpeed * Runner.DeltaTime;

            if (_characterController != null)
                _characterController.Move(move);
            else
                transform.position += move;

            // Look — rotate player on flat platforms (VR uses head tracking)
            var lookInput = _inputProvider.GetLookInput();
            if (_inputProvider.Platform != PlatformType.VR && lookInput.sqrMagnitude > 0.001f)
            {
                _yaw += lookInput.x;
                transform.rotation = Quaternion.Euler(0, _yaw, 0);
            }

            // Sync position to network state
            var state = State;
            state.Position = new Vector3Net(transform.position.x, transform.position.y, transform.position.z);
            state.Rotation = new QuaternionNet(
                transform.rotation.x, transform.rotation.y,
                transform.rotation.z, transform.rotation.w);
            State = state;
        }

        private void OnDestroy()
        {
            _inputProvider?.Dispose();
        }

        // --- IPlayerStateAccess ---

        public PlayerRole GetRole() => State.Role;
        public bool GetIsAlive() => State.IsAlive;
        public bool GetIsInWailState() => State.IsInWailState;
        public float GetMoveSpeed() => State.MoveSpeed;

        public void SetAlive(bool alive)
        {
            var s = State; s.IsAlive = alive; State = s;
        }

        public void SetWailState(bool wailing, float speed)
        {
            var s = State;
            s.IsInWailState = wailing;
            s.MoveSpeed = speed;
            State = s;
        }

        public void SetRespawnTimer(float seconds)
        {
            var s = State;
            s.RespawnTimer = TickTimer.CreateFromSeconds(Runner, seconds);
            State = s;
        }

        public bool IsRespawnExpired() => State.RespawnTimer.Expired(Runner);

        public void SetPosition(float x, float y, float z)
        {
            transform.position = new Vector3(x, y, z);
            var s = State;
            s.Position = new Vector3Net(x, y, z);
            State = s;
        }

        private static PlatformType DetectPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Quest is Android — check for XR
            if (UnityEngine.XR.XRSettings.isDeviceActive)
                return PlatformType.VR;
            return PlatformType.Mobile;
#elif UNITY_IOS
            return PlatformType.Mobile;
#elif UNITY_WEBGL
            return PlatformType.Browser;
#elif UNITY_STANDALONE
            if (UnityEngine.XR.XRSettings.isDeviceActive)
                return PlatformType.VR;
            return PlatformType.PC;
#elif UNITY_SWITCH || UNITY_PS5 || UNITY_GAMECORE
            return PlatformType.Console;
#else
            return PlatformType.PC;
#endif
        }
    }
}
