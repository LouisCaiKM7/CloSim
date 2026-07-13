using System.Collections;
using Core;
using Field.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Field.SeasonSpecific.Rebuilt
{
    public class OutpostRelease : MonoBehaviour
    {
        [Header("Alliance")]
        [SerializeField] private bool isBlue = true;

        [Header("Runtime Ownership")]
        [SerializeField] private int playerSlot = -1;

        [Header("Object To Move")]
        [SerializeField] private Transform objectToMove;

        [Header("Target")]
        [SerializeField] private Transform targetTransform;

        [Header("Movement")]
        [SerializeField] private float moveDuration = 1.5f;
        [SerializeField] private float waitBeforeReturning = 2f;

        [Header("Input")]
        [SerializeField] private string actionMapName = "Robot";
        [SerializeField] private RobotCommand command = RobotCommand.HumanPlayerDump;

        private LoadMatch _loadMatch;
        private PlayerInput _playerInput;
        private InputActionMap _inputMap;
        private InputAction _commandAction;

        private bool _isConfigured;
        private bool _isAnimating;
        private Coroutine _moveCoroutine;

        private void Awake()
        {
            _loadMatch = FindFirstObjectByType<LoadMatch>();
            TryAutoConfigureOwnership();
        }

        private void Start()
        {
            TryAutoConfigureOwnership();
            TryResolveInput();
        }

        private void OnEnable()
        {
            TryAutoConfigureOwnership();
            TryResolveInput();
        }

        private void OnDisable()
        {
            _playerInput = null;
            _inputMap = null;
            _commandAction = null;
        }

        private void Update()
        {
            if (!_isConfigured || playerSlot < 0)
            {
                TryAutoConfigureOwnership();
                return;
            }

            if (!HumanPlayerRuntimeState.IsDumperAllowed(isBlue))
                return;

            if (Fms.RobotState == RobotState.Disabled)
                return;

            if (_playerInput == null || _inputMap == null || _commandAction == null)
                TryResolveInput();

            if (_commandAction != null && _commandAction.WasPressedThisFrame())
                TryStartMove();
        }

        public void ConfigureOwnership(int ownerPlayerSlot)
        {
            playerSlot = ownerPlayerSlot;
            _isConfigured = playerSlot >= 0;
            TryResolveInput();
        }

        public bool IsBlue()
        {
            return isBlue;
        }

        private void TryAutoConfigureOwnership()
        {
            if (_isConfigured && playerSlot >= 0)
                return;

            if (_loadMatch == null)
                _loadMatch = FindFirstObjectByType<LoadMatch>();

            if (_loadMatch == null)
                return;

            playerSlot = _loadMatch.GetHumanPlayerOwnerSlotForAlliance(isBlue);
            _isConfigured = playerSlot >= 0;
        }

        private void TryResolveInput()
        {
            if (!_isConfigured || playerSlot < 0)
                return;

            if (_loadMatch == null)
                _loadMatch = FindFirstObjectByType<LoadMatch>();

            GameObject robot = _loadMatch != null ? _loadMatch.GetRobotLoaded(playerSlot) : null;
            if (robot == null)
                return;

            _playerInput = robot.GetComponent<PlayerInput>();
            if (_playerInput == null || _playerInput.actions == null)
                return;

            _inputMap = _playerInput.actions.FindActionMap(actionMapName);
            if (_inputMap == null)
                return;

            _inputMap.Enable();
            _commandAction = _inputMap.FindAction(command.ToString());
        }

        private void TryStartMove()
        {
            if (_isAnimating || objectToMove == null || targetTransform == null)
                return;

            if (_moveCoroutine != null)
                StopCoroutine(_moveCoroutine);

            _moveCoroutine = StartCoroutine(MoveToTargetThenBack());
        }

        private IEnumerator MoveToTargetThenBack()
        {
            _isAnimating = true;

            Vector3 originalPosition = objectToMove.position;
            Quaternion originalRotation = objectToMove.rotation;

            Vector3 targetPosition = targetTransform.position;
            Quaternion targetRotation = targetTransform.rotation;

            yield return MoveLinearly(originalPosition, originalRotation, targetPosition, targetRotation);
            yield return new WaitForSeconds(waitBeforeReturning);
            yield return MoveLinearly(targetPosition, targetRotation, originalPosition, originalRotation);

            _isAnimating = false;
            _moveCoroutine = null;
        }

        private IEnumerator MoveLinearly(
            Vector3 startPosition,
            Quaternion startRotation,
            Vector3 endPosition,
            Quaternion endRotation)
        {
            float elapsed = 0f;

            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveDuration);

                objectToMove.position = Vector3.Lerp(startPosition, endPosition, t);
                objectToMove.rotation = Quaternion.Lerp(startRotation, endRotation, t);

                yield return null;
            }

            objectToMove.position = endPosition;
            objectToMove.rotation = endRotation;
        }
    }
}