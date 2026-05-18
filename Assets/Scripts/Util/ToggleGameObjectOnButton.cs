using UnityEngine;
using UnityEngine.InputSystem;
using Util;

public class ToggleGameObjectOnButton : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private GameObject targetObject;

    [Header("Button Control Settings")]
    [SerializeField] private ControllerInputs controllerButton;
    [SerializeField] private KeyboardInputs keyboardButton;

    [Header("Input Settings")]
    [SerializeField] private string actionMapName = "Robot";

    [Header("Behavior")]
    [SerializeField] private bool startActive = false;

    private PlayerInput _playerInput;
    private InputActionMap _inputMap;
    private InputAction _controllerAction;
    private InputAction _keyboardAction;

    private void Start()
    {
        ResolveInput();

        if (targetObject != null)
            targetObject.SetActive(startActive);
    }

    private void Update()
    {
        if (_inputMap == null || _controllerAction == null && _keyboardAction == null)
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

        _controllerAction = _inputMap.FindAction(controllerButton.ToString());
        _keyboardAction = _inputMap.FindAction(keyboardButton.ToString());

        if (_controllerAction == null)
            Debug.LogWarning($"{name}: Could not find controller action '{controllerButton}' in '{actionMapName}'.");

        if (_keyboardAction == null)
            Debug.LogWarning($"{name}: Could not find keyboard action '{keyboardButton}' in '{actionMapName}'.");
    }

    private bool GetButtonPressedThisFrame()
    {
        bool controllerPressed =
            _controllerAction != null &&
            _controllerAction.WasPressedThisFrame();

        bool keyboardPressed =
            _keyboardAction != null &&
            _keyboardAction.WasPressedThisFrame();

        return controllerPressed || keyboardPressed;
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