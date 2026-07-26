// CloSim Online Multiplayer — Phase 3 Gameplay Sync (T2). Namespace: Online.Sync.
//
// SERVER-ONLY synthetic input source for a remote-owned robot. The stock SwerveController reads its own
// PlayerInput ("Drive"/"Rotate" of the "Robot" map) every FixedUpdate; on the server a remote robot has no
// local device, so this component feeds those actions from the networked values via a DEDICATED synthetic
// Gamepad paired to the robot's InputUser. Because the values arrive through the normal Gamepad binding path
// (leftStick=Drive, rightStick=Rotate), SwerveController's fieldCentric / reversed branches behave EXACTLY as
// they do for a local player — we deliberately avoid SwerveController.OverideInputs(), which forces the
// field-centric branch and would change the feel.
//
// OWNERSHIP-BOUNDARY NOTE (see final report): SwerveController is game content and MUST NOT be edited. This
// synthetic-device injection is the non-invasive route, but it depends on Input System device/update timing
// that can only be verified inside the Unity editor. If editor testing shows drift/feel issues, the clean
// fallback is a ~5-line additive hook in SwerveController — documented in the T2 report for the lead to
// authorize (it is NOT applied here because SwerveController is outside T2's ownership).

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;

namespace Online.Sync
{
    // Runs before the default execution order so the synthetic control state is written before
    // SwerveController.FixedUpdate reads it within the same physics step.
    [DefaultExecutionOrder(-50)]
    public sealed class ServerRobotInputSource : MonoBehaviour
    {
        private const string GamepadControlScheme = "Gamepad";

        private Gamepad _pad;
        private PlayerInput _playerInput;
        private string _actionMapName = "Robot";
        private bool _active;

        private Vector2 _drive;
        private Vector2 _rotate;

        /// <summary>Server-side setup: create + pair a dedicated synthetic Gamepad for this robot.</summary>
        public void Initialize(int slot, string actionMapName, string driveActionName, string rotateActionName)
        {
            _actionMapName = string.IsNullOrEmpty(actionMapName) ? "Robot" : actionMapName;

            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput == null || _playerInput.actions == null)
            {
                Debug.LogError($"[ServerRobotInputSource] Robot slot {slot} has no PlayerInput/actions; cannot inject remote input.");
                return;
            }

            _pad = InputSystem.AddDevice<Gamepad>($"CloSimServerPad_{slot}_{GetInstanceID()}");
            if (_pad == null)
            {
                Debug.LogError($"[ServerRobotInputSource] Failed to create synthetic gamepad for robot slot {slot}.");
                return;
            }

            BindSyntheticPad();
            _active = _pad != null;
        }

        private void BindSyntheticPad()
        {
            try
            {
                _playerInput.DeactivateInput();
                _playerInput.neverAutoSwitchControlSchemes = true;
                _playerInput.defaultActionMap = _actionMapName;

                _playerInput.actions.Disable();
                _playerInput.actions.bindingMask = null;

                if (_playerInput.user.valid)
                    _playerInput.user.UnpairDevices();

                InputUser.PerformPairingWithDevice(_pad, _playerInput.user);

                _playerInput.SwitchCurrentControlScheme(GamepadControlScheme, _pad);
                _playerInput.SwitchCurrentActionMap(_actionMapName);

                _playerInput.actions.bindingMask = InputBinding.MaskByGroup(GamepadControlScheme);
                _playerInput.ActivateInput();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ServerRobotInputSource] Failed to bind synthetic gamepad: {ex}");
                _active = false;
            }
        }

        /// <summary>Server-side: store the latest networked input (applied every physics step).</summary>
        public void SetInput(Vector2 drive, Vector2 rotate)
        {
            _drive = drive;
            _rotate = rotate;
        }

        private void FixedUpdate()
        {
            if (!_active || _pad == null)
                return;

            // Feed the synthetic sticks; SwerveController reads these as ordinary gamepad input this step.
            InputState.Change(_pad.leftStick, _drive);
            InputState.Change(_pad.rightStick, _rotate);
        }

        private void OnDestroy()
        {
            if (_pad == null)
                return;

            if (_playerInput != null && _playerInput.user.valid)
                _playerInput.user.UnpairDevicesAndRemoveUser();

            InputSystem.RemoveDevice(_pad);
            _pad = null;
        }
    }
}
