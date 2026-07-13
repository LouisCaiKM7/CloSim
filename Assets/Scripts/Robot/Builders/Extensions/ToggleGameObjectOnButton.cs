using Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robot.Builders.Extensions
{
    public class ToggleGameObjectOnButton : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private GameObject targetObject;

        [Header("Button Control Settings")]
        [SerializeField] private RobotCommand command = RobotCommand.RobotSpecial;

        [Header("Input Settings")]
        [SerializeField] private string actionMapName = "Robot";

        [Header("Behavior")]
        [SerializeField] private bool startActive;

        private PlayerInput _playerInput;
        private InputActionMap _inputMap;

        private void Start()
        {
            ResolveInput();

            if (targetObject != null)
                targetObject.SetActive(startActive);
        }

        private void Update()
        {
            if (_inputMap == null)
            {
                ResolveInput();
            }

            if (GetButtonPressedThisFrame())
            {
                ToggleTarget();
            }
        }

        private void ResolveInput()
        {
            if (_playerInput == null)
            {
                _playerInput = GetComponent<PlayerInput>();

                if (_playerInput == null)
                    _playerInput = GetComponentInParent<PlayerInput>();
            }

            if (_playerInput == null || _playerInput.actions == null)
                return;

            _inputMap = _playerInput.actions.FindActionMap(actionMapName);

            if (_inputMap == null)
            {
                Debug.LogWarning($"{name}: Could not find action map '{actionMapName}'.");
                return;
            }

            _inputMap.Enable();
        }

        private bool GetButtonPressedThisFrame()
        {
            if (_inputMap == null)
                return false;

            InputAction action = _inputMap.FindAction(command.ToString());

            return action != null && action.WasPressedThisFrame();
        }

        private void ToggleTarget()
        {
            if (targetObject == null)
            {
                Debug.LogWarning($"{name}: No targetObject assigned.");
                return;
            }

            targetObject.SetActive(!targetObject.activeSelf);
        }
    }
}
