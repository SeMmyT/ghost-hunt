using GhostHunt.Core;
using UnityEngine;

namespace GhostHunt.Player
{
    /// <summary>
    /// Abstracts input across all platforms.
    /// Returns normalized movement vectors regardless of device.
    /// Factory pattern — Create() detects platform and returns the right provider.
    /// </summary>
    public abstract class PlatformInputProvider
    {
        public abstract PlatformType Platform { get; }

        /// <summary>
        /// Get movement input as normalized XZ direction.
        /// </summary>
        public abstract Vector2 GetMoveInput();

        /// <summary>
        /// Get look/turn input. VR uses head tracking; others use stick/mouse.
        /// </summary>
        public abstract Vector2 GetLookInput();

        /// <summary>
        /// Primary action (catch attempt for ghosts, use ability for target).
        /// </summary>
        public abstract bool GetActionPressed();

        /// <summary>
        /// Secondary action (ghost burst, target decoy, etc).
        /// </summary>
        public abstract bool GetSecondaryPressed();

        public static PlatformInputProvider Create(PlatformType platform)
        {
            return platform switch
            {
                PlatformType.VR => new VRInputProvider(),
                PlatformType.PC => new PCInputProvider(),
                PlatformType.Mobile => new MobileInputProvider(),
                PlatformType.Console => new ConsoleInputProvider(),
                PlatformType.Browser => new MobileInputProvider(), // Browser uses touch-like input
                _ => new PCInputProvider()
            };
        }
    }

    /// <summary>
    /// VR input via XR controllers. Joystick locomotion + hand tracking.
    /// </summary>
    public class VRInputProvider : PlatformInputProvider
    {
        public override PlatformType Platform => PlatformType.VR;

        public override Vector2 GetMoveInput()
        {
            // Unity XR Input System — left thumbstick
            var leftHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                return axis;
            return Vector2.zero;
        }

        public override Vector2 GetLookInput()
        {
            // VR look is head-tracked — no manual input needed
            // Snap turn handled separately by VRComfortController
            return Vector2.zero;
        }

        public override bool GetActionPressed()
        {
            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool pressed))
                return pressed;
            return false;
        }

        public override bool GetSecondaryPressed()
        {
            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool pressed))
                return pressed;
            return false;
        }
    }

    /// <summary>
    /// PC input via keyboard + mouse.
    /// </summary>
    public class PCInputProvider : PlatformInputProvider
    {
        public override PlatformType Platform => PlatformType.PC;

        public override Vector2 GetMoveInput()
        {
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        }

        public override Vector2 GetLookInput()
        {
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        }

        public override bool GetActionPressed() => Input.GetMouseButtonDown(0);
        public override bool GetSecondaryPressed() => Input.GetMouseButtonDown(1);
    }

    /// <summary>
    /// Mobile/touch input. Virtual joystick + tap actions.
    /// </summary>
    public class MobileInputProvider : PlatformInputProvider
    {
        public override PlatformType Platform => PlatformType.Mobile;

        public override Vector2 GetMoveInput()
        {
            // TODO: Hook up to on-screen virtual joystick UI
            // For now, fall back to any connected gamepad
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        }

        public override Vector2 GetLookInput() => Vector2.zero; // Top-down view, no look

        public override bool GetActionPressed()
        {
            return Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
        }

        public override bool GetSecondaryPressed()
        {
            return Input.touchCount > 1 && Input.GetTouch(1).phase == TouchPhase.Began;
        }
    }

    /// <summary>
    /// Console input via gamepad (Switch, Xbox, PS).
    /// </summary>
    public class ConsoleInputProvider : PlatformInputProvider
    {
        public override PlatformType Platform => PlatformType.Console;

        public override Vector2 GetMoveInput()
        {
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        }

        public override Vector2 GetLookInput()
        {
            // Right stick
            return new Vector2(Input.GetAxis("RightStickHorizontal"), Input.GetAxis("RightStickVertical"));
        }

        public override bool GetActionPressed() => Input.GetButtonDown("Fire1");
        public override bool GetSecondaryPressed() => Input.GetButtonDown("Fire2");
    }
}
