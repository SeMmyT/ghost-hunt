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
    public class PlayerController : NetworkBehaviour
    {
        [Networked] public PlayerState State { get; set; }
        [Networked] public RoleData Role { get; set; }

        [SerializeField] private float _moveSpeed = GameConstants.GhostBaseSpeed;
        [SerializeField] private CharacterController _characterController;

        private PlatformInputProvider _inputProvider;

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
            if (!HasInputAuthority) return;

            var input = _inputProvider.GetMoveInput();
            var move = new Vector3(input.x, 0, input.y) * State.MoveSpeed * Runner.DeltaTime;

            if (_characterController != null)
                _characterController.Move(move);
            else
                transform.position += move;

            // Update networked position
            var state = State;
            state.Position = new Vector3Net(transform.position.x, transform.position.y, transform.position.z);
            state.Rotation = new QuaternionNet(
                transform.rotation.x, transform.rotation.y,
                transform.rotation.z, transform.rotation.w);
            State = state;
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
