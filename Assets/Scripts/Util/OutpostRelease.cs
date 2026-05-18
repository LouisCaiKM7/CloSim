using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using Util;

public class OutpostRelease : MonoBehaviour
{
    [Header("Alliance - Set In Inspector")]
    [Tooltip("True for Blue Dumper, false for Red Dumper.")]
    [SerializeField] private bool isBlue = true;

    [Header("Runtime Ownership - Auto-Resolved From LoadMatch")]
    [Tooltip("Set automatically at runtime. 0 = Robot 1 / Player 1, 1 = Robot 2 / Player 2.")]
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
    [SerializeField] private string bumperActionName = "RightBumper";
    [SerializeField] private string keyboardActionName = "P";
    [SerializeField] private bool useRightBumper = true;
    [SerializeField] private bool usePKey = true;

    private LoadMatch loadMatch;
    private PlayerInput playerInput;
    private InputActionMap inputMap;
    private InputAction bumperAction;
    private InputAction keyboardAction;

    private bool isConfigured;
    private bool isAnimating;
    private Coroutine moveCoroutine;

    private void Awake()
    {
        loadMatch = FindFirstObjectByType<LoadMatch>();
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
        playerInput = null;
        inputMap = null;
        bumperAction = null;
        keyboardAction = null;
    }

    private void Update()
    {
        if (!isConfigured || playerSlot < 0)
        {
            TryAutoConfigureOwnership();
            return;
        }

        if (!HumanPlayerRuntimeState.IsDumperAllowed(isBlue))
            return;

        if (FMS.RobotState == RobotState.disabled)
            return;

        if (playerInput == null || inputMap == null)
            TryResolveInput();

        if (inputMap == null)
            return;

        if (GetMoveButtonPressed())
            TryStartMove();
    }

    public void ConfigureOwnership(int ownerPlayerSlot)
    {
        playerSlot = ownerPlayerSlot;
        isConfigured = playerSlot >= 0;

        TryResolveInput();
    }

    public bool IsBlue()
    {
        return isBlue;
    }

    public int GetPlayerSlot()
    {
        return playerSlot;
    }

    private void TryAutoConfigureOwnership()
    {
        if (isConfigured && playerSlot >= 0)
            return;

        if (loadMatch == null)
            loadMatch = FindFirstObjectByType<LoadMatch>();

        if (loadMatch == null)
            return;

        playerSlot = loadMatch.GetHumanPlayerOwnerSlotForAlliance(isBlue);
        isConfigured = playerSlot >= 0;
    }

    private void TryResolveInput()
    {
        if (!isConfigured || playerSlot < 0)
            return;

        if (loadMatch == null)
            loadMatch = FindFirstObjectByType<LoadMatch>();

        if (loadMatch == null)
            return;

        GameObject robot = loadMatch.GetRobotLoaded(playerSlot);
        if (robot == null)
            return;

        playerInput = robot.GetComponent<PlayerInput>();
        if (playerInput == null || playerInput.actions == null)
            return;

        inputMap = playerInput.actions.FindActionMap(actionMapName);
        if (inputMap == null)
            return;

        inputMap.Enable();

        bumperAction = useRightBumper ? inputMap.FindAction(bumperActionName) : null;
        keyboardAction = usePKey ? inputMap.FindAction(keyboardActionName) : null;
    }

    private bool GetMoveButtonPressed()
    {
        bool buttonPressed = false;

        if (bumperAction != null &&
            bumperAction.triggered &&
            bumperAction.activeControl?.device is Gamepad)
        {
            buttonPressed = true;
        }

        if (keyboardAction != null &&
            keyboardAction.triggered &&
            keyboardAction.activeControl?.device is Keyboard)
        {
            buttonPressed = true;
        }

        return buttonPressed;
    }

    private void TryStartMove()
    {
        if (isAnimating)
            return;

        if (objectToMove == null || targetTransform == null)
            return;

        if (moveCoroutine != null)
            StopCoroutine(moveCoroutine);

        moveCoroutine = StartCoroutine(MoveToTargetThenBack());
    }

    private IEnumerator MoveToTargetThenBack()
    {
        isAnimating = true;

        Vector3 originalPosition = objectToMove.position;
        Quaternion originalRotation = objectToMove.rotation;

        Vector3 targetPosition = targetTransform.position;
        Quaternion targetRotation = targetTransform.rotation;

        yield return MoveLinearly(
            originalPosition,
            originalRotation,
            targetPosition,
            targetRotation
        );

        yield return new WaitForSeconds(waitBeforeReturning);

        yield return MoveLinearly(
            targetPosition,
            targetRotation,
            originalPosition,
            originalRotation
        );

        isAnimating = false;
        moveCoroutine = null;
    }

    private IEnumerator MoveLinearly(
        Vector3 startPosition,
        Quaternion startRotation,
        Vector3 endPosition,
        Quaternion endRotation
    )
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