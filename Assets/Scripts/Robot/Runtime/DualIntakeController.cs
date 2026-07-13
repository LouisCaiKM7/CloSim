using Robot.Builders;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robot.Runtime
{
    [DefaultExecutionOrder(10000)]
    public class DualIntakeController : MonoBehaviour
    {
        private enum IntakeSide
        {
            None,
            Left,
            Right
        }

        private enum ConflictMode
        {
            FirstPressedWins,
            LastPressedWins,
            LeftPriority,
            RightPriority
        }

        [Header("Input")]
        [SerializeField] private string actionMapName = "Robot";
        [SerializeField] private string leftActionName = "Intake";
        [SerializeField] private string rightActionName = "RobotSpecial";
        [SerializeField] private ConflictMode conflictMode = ConflictMode.FirstPressedWins;

        [Header("Left Intake")]
        [Tooltip("Drag the BuildArm components belonging to the left intake here.")]
        [SerializeField] private BuildArm[] leftArms;

        [Tooltip("Drag the Build elevator components belonging to the left intake here.")]
        [SerializeField] private BuildElevator[] leftElevators;

        [Header("Right Intake")]
        [Tooltip("Drag the BuildArm components belonging to the right intake here.")]
        [SerializeField] private BuildArm[] rightArms;

        [Tooltip("Drag the Build elevator components belonging to the right intake here.")]
        [SerializeField] private BuildElevator[] rightElevators;

        private PlayerInput _playerInput;
        private InputActionMap _inputMap;
        private InputAction _leftAction;
        private InputAction _rightAction;

        private JointController[] _leftControllers;
        private JointController[] _rightControllers;

        private int _leftMechanismCount = -1;
        private int _rightMechanismCount = -1;

        private IntakeSide _activeSide = IntakeSide.None;

        private void Awake()
        {
            _playerInput = GetComponentInParent<PlayerInput>();
            ResolveInputActions();
        }

        private void LateUpdate()
        {
            ResolveInputActions();
            ResolveControllers();

            bool leftHeld = _leftAction != null && _leftAction.IsPressed();
            bool rightHeld = _rightAction != null && _rightAction.IsPressed();

            bool leftPressed = _leftAction != null && _leftAction.WasPressedThisFrame();
            bool rightPressed = _rightAction != null && _rightAction.WasPressedThisFrame();

            UpdateActiveSide(leftHeld, rightHeld, leftPressed, rightPressed);
            EnforceLockout();
        }

        private void ResolveInputActions()
        {
            if (_playerInput == null)
                _playerInput = GetComponentInParent<PlayerInput>();

            if (_playerInput == null || _playerInput.actions == null)
                return;

            if (_inputMap == null)
            {
                _inputMap = _playerInput.actions.FindActionMap(actionMapName);
                _inputMap?.Enable();
            }

            if (_inputMap == null)
                return;

            _leftAction ??= _inputMap.FindAction(leftActionName);
            _rightAction ??= _inputMap.FindAction(rightActionName);
        }

        private void ResolveControllers()
        {
            _leftControllers = ResolveControllers(
                leftArms,
                leftElevators,
                _leftControllers,
                ref _leftMechanismCount
            );

            _rightControllers = ResolveControllers(
                rightArms,
                rightElevators,
                _rightControllers,
                ref _rightMechanismCount
            );
        }

        private static JointController[] ResolveControllers(
            BuildArm[] arms,
            BuildElevator[] elevators,
            JointController[] existing,
            ref int existingMechanismCount)
        {
            int armCount = arms?.Length ?? 0;
            int elevatorCount = elevators?.Length ?? 0;
            int mechanismCount = armCount + elevatorCount;

            if (existing != null &&
                existing.Length == mechanismCount &&
                existingMechanismCount == mechanismCount)
            {
                bool allValid = true;

                foreach (var controller in existing)
                {
                    if (controller == null)
                    {
                        allValid = false;
                        break;
                    }
                }

                if (allValid)
                    return existing;
            }

            JointController[] resolved = new JointController[mechanismCount];
            int index = 0;

            if (arms != null)
            {
                foreach (var t in arms)
                {
                    if (t != null)
                        resolved[index] = t.GetController();

                    index++;
                }
            }

            if (elevators != null)
            {
                foreach (var t in elevators)
                {
                    if (t != null)
                        resolved[index] = t.GetController();

                    index++;
                }
            }

            existingMechanismCount = mechanismCount;
            return resolved;
        }

        private void UpdateActiveSide(
            bool leftHeld,
            bool rightHeld,
            bool leftPressed,
            bool rightPressed)
        {
            if (!leftHeld && !rightHeld)
            {
                _activeSide = IntakeSide.None;
                return;
            }

            if (leftHeld && !rightHeld)
            {
                _activeSide = IntakeSide.Left;
                return;
            }

            if (!leftHeld)
            {
                _activeSide = IntakeSide.Right;
                return;
            }

            // Both are held.
            switch (conflictMode)
            {
                case ConflictMode.LeftPriority:
                    _activeSide = IntakeSide.Left;
                    break;

                case ConflictMode.RightPriority:
                    _activeSide = IntakeSide.Right;
                    break;

                case ConflictMode.LastPressedWins:
                    if (leftPressed && !rightPressed)
                        _activeSide = IntakeSide.Left;
                    else if (rightPressed && !leftPressed)
                        _activeSide = IntakeSide.Right;
                    else if (leftPressed)
                        _activeSide = IntakeSide.Right;
                    else if (_activeSide == IntakeSide.None)
                        _activeSide = IntakeSide.Left;
                    break;

                case ConflictMode.FirstPressedWins:
                default:
                    if (_activeSide == IntakeSide.None)
                    {
                        if (leftPressed && !rightPressed)
                            _activeSide = IntakeSide.Left;
                        else if (rightPressed && !leftPressed)
                            _activeSide = IntakeSide.Right;
                        else
                            _activeSide = IntakeSide.Left;
                    }
                    break;
            }
        }

        private void EnforceLockout()
        {
            switch (_activeSide)
            {
                case IntakeSide.Left:
                    ForceHome(_rightControllers);
                    break;

                case IntakeSide.Right:
                    ForceHome(_leftControllers);
                    break;

                case IntakeSide.None:
                    break;
            }
        }

        private static void ForceHome(JointController[] controllers)
        {
            if (controllers == null)
                return;

            foreach (var controller in controllers)
            {
                if (controller == null)
                    continue;

                controller.targetPosition = controller.home;
            }
        }
    }
}