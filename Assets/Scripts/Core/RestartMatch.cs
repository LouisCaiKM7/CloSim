using UnityEngine;
using UnityEngine.InputSystem;
using Utilities;

namespace Core
{
    public class RestartMatch : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private string actionMapName = "Robot";
        [SerializeField] private string restartActionName = "Restart";

        private LoadMatch _matchLoader;
        private PlayerInput _playerInput;
        private InputActionMap _inputMap;
        private InputAction _restartAction;

        private void Start()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnableInput();
        }

        private void OnDisable()
        {
            if (_restartAction != null)
                _restartAction.performed -= OnRestartPerformed;
        }

        private void ResolveReferences()
        {
            if (_matchLoader == null)
                _matchLoader = Utils.FindParentObjectComponent<LoadMatch>(gameObject);

            if (_playerInput == null)
                _playerInput = GetComponent<PlayerInput>();

            if (_playerInput == null || _playerInput.actions == null)
                return;

            _inputMap = _playerInput.actions.FindActionMap(actionMapName);

            if (_inputMap == null)
            {
                Debug.LogError($"{name}: Could not find action map '{actionMapName}'.");
                return;
            }

            InputAction resolvedRestartAction = _inputMap.FindAction(restartActionName);

            if (resolvedRestartAction == null)
            {
                Debug.LogError($"{name}: Could not find action '{restartActionName}' in action map '{actionMapName}'.");
                return;
            }

            if (_restartAction == resolvedRestartAction)
                return;

            if (_restartAction != null)
                _restartAction.performed -= OnRestartPerformed;

            _restartAction = resolvedRestartAction;
            _restartAction.performed += OnRestartPerformed;
        }

        private void EnableInput()
        {
            _inputMap?.Enable();
            _restartAction?.Enable();
        }

        private void OnRestartPerformed(InputAction.CallbackContext context)
        {
            if (_matchLoader == null)
                _matchLoader = Utils.FindParentObjectComponent<LoadMatch>(gameObject);

            if (_matchLoader == null)
            {
                Debug.LogWarning($"{name}: Cannot restart match because no LoadMatch was found.");
                return;
            }

            _matchLoader.ResetField();
        }
    }
}