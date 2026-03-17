using GhostHunt.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunt.Player
{
    /// <summary>
    /// Abstracts input across all platforms using the new Unity Input System.
    /// Loads bindings from GhostHuntInput.inputactions, auto-switches control
    /// scheme based on active device. Factory Create() still detects platform
    /// for camera/HUD decisions, but input routing is unified.
    /// </summary>
    public class PlatformInputProvider
    {
        public PlatformType Platform { get; }

        private readonly InputActionAsset _asset;
        private readonly InputActionMap _playerMap;
        private readonly InputAction _move;
        private readonly InputAction _look;
        private readonly InputAction _action;
        private readonly InputAction _secondary;

        private PlatformInputProvider(PlatformType platform, InputActionAsset asset)
        {
            Platform = platform;
            _asset = asset;
            _playerMap = _asset.FindActionMap("Player");
            _move = _playerMap.FindAction("Move");
            _look = _playerMap.FindAction("Look");
            _action = _playerMap.FindAction("Action");
            _secondary = _playerMap.FindAction("Secondary");
            _playerMap.Enable();
        }

        /// <summary>
        /// Create provider for the detected platform. Loads the shared
        /// InputActionAsset and enables the Player action map.
        /// </summary>
        public static PlatformInputProvider Create(PlatformType platform)
        {
            var asset = Resources.Load<InputActionAsset>("GhostHuntInput");
            if (asset == null)
            {
                // Fallback: build actions programmatically
                asset = BuildFallbackActions();
                Debug.LogWarning("[Input] GhostHuntInput asset not found in Resources, using fallback bindings");
            }

            // Clone so each player gets independent action state
            asset = Object.Instantiate(asset);
            return new PlatformInputProvider(platform, asset);
        }

        public Vector2 GetMoveInput() => _move.ReadValue<Vector2>();
        public Vector2 GetLookInput() => _look.ReadValue<Vector2>();
        public bool GetActionPressed() => _action.WasPressedThisFrame();
        public bool GetSecondaryPressed() => _secondary.WasPressedThisFrame();

        /// <summary>
        /// True while primary action is held (for charge-style abilities).
        /// </summary>
        public bool GetActionHeld() => _action.IsPressed();

        public void Dispose()
        {
            _playerMap?.Disable();
            if (_asset != null)
                Object.Destroy(_asset);
        }

        /// <summary>
        /// Fallback when the .inputactions asset isn't in Resources.
        /// Builds keyboard+mouse + gamepad bindings programmatically.
        /// </summary>
        private static InputActionAsset BuildFallbackActions()
        {
            var asset = InputActionAsset.FromJson(@"{
                ""name"": ""FallbackInput"",
                ""maps"": [{
                    ""name"": ""Player"",
                    ""id"": ""f0000000-0000-4000-8000-000000000001"",
                    ""actions"": [
                        { ""name"": ""Move"",      ""type"": ""Value"",  ""id"": ""f0000001-0000-4000-8000-000000000001"", ""expectedControlType"": ""Vector2"" },
                        { ""name"": ""Look"",      ""type"": ""Value"",  ""id"": ""f0000001-0000-4000-8000-000000000002"", ""expectedControlType"": ""Vector2"" },
                        { ""name"": ""Action"",    ""type"": ""Button"", ""id"": ""f0000001-0000-4000-8000-000000000003"" },
                        { ""name"": ""Secondary"", ""type"": ""Button"", ""id"": ""f0000001-0000-4000-8000-000000000004"" }
                    ],
                    ""bindings"": [
                        { ""name"": ""WASD"", ""id"": ""f0000002-0001-4000-8000-000000000001"", ""path"": """", ""action"": ""Move"", ""isComposite"": true },
                        { ""name"": ""up"",    ""id"": ""f0000002-0001-4000-8000-000000000002"", ""path"": ""<Keyboard>/w"",     ""action"": ""Move"", ""isPartOfComposite"": true },
                        { ""name"": ""down"",  ""id"": ""f0000002-0001-4000-8000-000000000003"", ""path"": ""<Keyboard>/s"",     ""action"": ""Move"", ""isPartOfComposite"": true },
                        { ""name"": ""left"",  ""id"": ""f0000002-0001-4000-8000-000000000004"", ""path"": ""<Keyboard>/a"",     ""action"": ""Move"", ""isPartOfComposite"": true },
                        { ""name"": ""right"", ""id"": ""f0000002-0001-4000-8000-000000000005"", ""path"": ""<Keyboard>/d"",     ""action"": ""Move"", ""isPartOfComposite"": true },
                        { ""path"": ""<Gamepad>/leftStick"",   ""id"": ""f0000002-0002-4000-8000-000000000001"", ""action"": ""Move"" },
                        { ""path"": ""<Mouse>/delta"",         ""id"": ""f0000002-0003-4000-8000-000000000001"", ""action"": ""Look"", ""processors"": ""ScaleVector2(x=0.1,y=0.1)"" },
                        { ""path"": ""<Gamepad>/rightStick"",  ""id"": ""f0000002-0004-4000-8000-000000000001"", ""action"": ""Look"", ""processors"": ""ScaleVector2(x=3,y=3)"" },
                        { ""path"": ""<Mouse>/leftButton"",    ""id"": ""f0000002-0005-4000-8000-000000000001"", ""action"": ""Action"" },
                        { ""path"": ""<Gamepad>/buttonSouth"",  ""id"": ""f0000002-0006-4000-8000-000000000001"", ""action"": ""Action"" },
                        { ""path"": ""<Mouse>/rightButton"",   ""id"": ""f0000002-0007-4000-8000-000000000001"", ""action"": ""Secondary"" },
                        { ""path"": ""<Gamepad>/buttonWest"",   ""id"": ""f0000002-0008-4000-8000-000000000001"", ""action"": ""Secondary"" },
                        { ""path"": ""<Keyboard>/e"",          ""id"": ""f0000002-0009-4000-8000-000000000001"", ""action"": ""Secondary"" }
                    ]
                }],
                ""controlSchemes"": []
            }");
            return asset;
        }
    }
}
