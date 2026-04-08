using System;
using System.Collections;
using System.Collections.Generic;
using BuilderLib;
using MyBox;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using Util;

[ExecuteAlways]
public class BuildNode : MonoBehaviour
{
    private BoxCollider _intakeCollider;
    public GamePiece currentGamePiece;
    [SerializeField] private bool Preload;

    [ConditionalField(true, nameof(showIntakeStuff))] [SerializeField]
    private Vector3 intakeSize = new Vector3(3f, 3f, 3f);

    [ConditionalField(nameof(Preload))]
    [SerializeField] private PieceNames pieceName;

    public NodeState currentState;
    public NodeAction[] Actions;
    private PlayerInput _playerInput;
    private InputActionMap _inputMap;
    private GameObject _robotParent;
    private Vector3 _halfExtents;
    private List<GamePiece> pieces = new List<GamePiece>();
    private static GameObject[] Pieces;
    private bool hasIntake = false;
    private bool showIntakeStuff() => hasIntake;

    private Vector3 _lastIntakePosition;
    private Quaternion _lastIntakeRotation;

    private void Start()
    {
        if (!Application.isPlaying) return;

        foreach (var child in Utils.GetAllChildren(transform))
        {
            if (child.TryGetComponent(typeof(BoxCollider), out var col))
            {
                _intakeCollider = (BoxCollider)col;
                _halfExtents = _intakeCollider.bounds.extents / 2;
            }
        }

        if (_intakeCollider)
        {
            _lastIntakePosition = _intakeCollider.transform.localPosition;
            _lastIntakeRotation = _intakeCollider.transform.localRotation;
        }

        _robotParent = Utils.FindParentPlayerInput(gameObject);
        _playerInput = _robotParent.GetComponent<PlayerInput>();
        _inputMap = _playerInput.actions.FindActionMap("Robot");
        _inputMap.Enable();

        Pieces ??= Resources.LoadAll<GameObject>("Pieces");
        SpawnPiece(pieceName.ToString(), Pieces);
    }

    private void SpawnPiece(string pieceName, GameObject[] pieces)
    {
        if (!Preload) return;
        foreach (var piece in pieces)
        {
            if (piece.name != pieceName) continue;
            currentGamePiece = Instantiate(piece, transform.position, transform.rotation, transform).GetComponent<GamePiece>();
            currentState = NodeState.Stowing;
            return;
        }
    }

    private void OnEnable()
    {
    }

    void Update()
    {
        if (!Application.isPlaying)
        {
            bool hasIntake = false;
            if (Actions != null)
            {
                foreach (var action in Actions)
                {
                    if (action.Type == NodeType.Intake)
                    {
                        hasIntake = true;
                        break;
                    }
                }
            }

            this.hasIntake = hasIntake;

            if (hasIntake)
            {
                if (!_intakeCollider)
                {
                    var intakeParent = Utils.TryGetAddChild("IntakeBox", gameObject, out var existed);
                    _intakeCollider = Utils.TryGetAddComponent<BoxCollider>(intakeParent);
                    if (!existed)
                    {
                        _intakeCollider.size = intakeSize * 0.0254f;
                        _intakeCollider.transform.localPosition = _lastIntakePosition;
                        _intakeCollider.transform.localRotation = _lastIntakeRotation;
                    }
                }
                else
                {
                    _intakeCollider.size = intakeSize * 0.0254f;
                    _intakeCollider.isTrigger = true;
                    _lastIntakePosition = _intakeCollider.transform.localPosition;
                    _lastIntakeRotation = _intakeCollider.transform.localRotation;
                }
            }
            else
            {
                var box = Utils.FindChild("IntakeBox", gameObject);
                if (box)
                {
                    DestroyImmediate(box);
                }
            }

            return;
        }

        var actionFinished = false;
        var actionDone = false;

        for (int i = 0; i < Actions.Length; i++)
        {
            ref NodeAction action = ref Actions[i];

            var controllerAction = _inputMap.FindAction(action.ControllerButton.ToString());
            var keyboardAction = _inputMap.FindAction(action.KeyboardButton.ToString());

            var buttonPressed = false;
            if (controllerAction.triggered && (controllerAction.activeControl?.device is Gamepad))
                buttonPressed = true;

            if (keyboardAction.triggered && (keyboardAction.activeControl?.device is Keyboard))
                buttonPressed = true;

            var controllerHeld = controllerAction.IsPressed() && (controllerAction.activeControl?.device is Gamepad);
            var keyboardHeld = keyboardAction.IsPressed() && (keyboardAction.activeControl?.device is Keyboard);
            var buttonHeld = controllerHeld || keyboardHeld;

            switch (action.Type)
            {
                case NodeType.Intake:
                    if (FMS.RobotState == RobotState.disabled) break;
                    if (!_intakeCollider) break;

                    switch (action.ControlType)
                    {
                        case NodeControlType.Hold:
                            if ((buttonHeld && !currentGamePiece) || (currentState == NodeState.Intakeing && currentGamePiece))
                                actionDone = true;
                            IntakePiece(buttonHeld, action);
                            break;
                        case NodeControlType.Tap:
                            if ((buttonPressed && !currentGamePiece) || (currentState == NodeState.Intakeing && currentGamePiece))
                                actionDone = true;
                            IntakePiece(buttonPressed, action);
                            break;
                        case NodeControlType.AlwaysPerform:
                            if (!currentGamePiece || (currentState == NodeState.Intakeing && currentGamePiece))
                                actionDone = true;
                            IntakePiece(true, action);
                            break;
                    }

                    break;

                case NodeType.Transfer:
                    if (!action.MoveTo || !currentGamePiece) break;
                    if (action.MoveTo.currentGamePiece) break;
                    if (action.PieceType != currentGamePiece.pieceType) break;

                    var finishedTransfer = false;
                    switch (action.ControlType)
                    {
                        case NodeControlType.Hold:
                            if (buttonHeld) actionDone = true;
                            finishedTransfer = TransferPiece(buttonHeld, buttonPressed, ref action);
                            break;
                        case NodeControlType.Tap:
                            if (buttonPressed) actionDone = true;
                            StartCoroutine(TransferPieceCo(buttonPressed, action));
                            break;
                        case NodeControlType.AlwaysPerform:
                            actionDone = true;
                            finishedTransfer = TransferPiece(true, currentState != NodeState.Transfering, ref action);
                            currentState = NodeState.Transfering;
                            break;
                    }

                    if (finishedTransfer)
                        actionFinished = true;

                    break;

                case NodeType.Outake:
                    if (FMS.RobotState == RobotState.disabled) break;
                    if (!currentGamePiece) break;
                    if (action.PieceType != currentGamePiece.pieceType) break;

                    bool wantsOuttake = action.ControlType switch
                    {
                        NodeControlType.Hold => buttonHeld,
                        NodeControlType.Tap => buttonPressed,
                        NodeControlType.AlwaysPerform => true,
                        _ => false
                    };

                    if (!wantsOuttake) break;

                    actionDone = true;

                    bool timerOk = action.ControlType switch
                    {
                        NodeControlType.Hold => PerformTimerCheck(ref action, buttonPressed),
                        NodeControlType.Tap => PerformTimerCheck(ref action, buttonPressed),
                        NodeControlType.AlwaysPerform => PerformTimerCheck(ref action),
                        _ => false
                    };

                    if (!timerOk) break;

                    currentState = NodeState.Outaking;

                    var finishedOuttake = GamePieceManager.ReleaseToWorld(currentGamePiece, action);
                    var releasedPiece = currentGamePiece;

                    StartCoroutine(GamePieceManager.enableColliders(releasedPiece));

                    if (finishedOuttake)
                    {
                        currentGamePiece = null;
                        currentState = NodeState.Stowing;
                        actionFinished = true;
                    }
                    else
                    {
                        currentState = NodeState.Stowing;
                    }

                    break;
            }
        }

        if ((currentGamePiece && currentState == NodeState.Stowing) || (!actionDone && currentGamePiece))
        {
            currentState = NodeState.Stowing;
            GamePieceManager.teleportTo(currentGamePiece, transform);
        }
        else if (actionFinished)
        {
            currentGamePiece = null;
            currentState = NodeState.Stowing;
        }
    }

    private IEnumerator TransferPieceCo(bool buttonPressed, NodeAction action)
    {
        if (action.PieceType != currentGamePiece.pieceType)
        {
            yield return null;
        }
        bool finished = false;
        while (!finished)
        {
            finished = TransferPiece(buttonPressed, buttonPressed, ref action);
            yield return null;
        }

        currentGamePiece = null;
    }

    private bool PerformTimerCheck(ref NodeAction action, bool onPressed = false, bool dontReset = false)
    {
        if (onPressed)
        {
            action.performTimer = 0;
        }

        action.performTimer += Time.deltaTime;

        if (action.performTimer > action.DelayTimer || (action.DelayTimer == 0))
        {
            if (dontReset) return true;
            action.performTimer = 0;
            return true;
        }
        else
        {
            return false;
        }
    }

    private bool TransferPiece(bool button, bool butonPressed, ref NodeAction action)
    {
        if (!currentGamePiece) return false;
        if (action.PieceType != currentGamePiece.pieceType) return false;
        if (!PerformTimerCheck(ref action, butonPressed, true)) return false;

        var succeeded = false;
        if (!currentGamePiece) return false;
        if (currentGamePiece.pieceType != action.PieceType) return false;

        if (button && currentGamePiece)
        {
            if (action.Animate)
            {
                currentState = NodeState.Transfering;
                succeeded = GamePieceManager.AnimateTo(currentGamePiece, action);
            }
            else
            {
                currentState = NodeState.Transfering;
                succeeded = GamePieceManager.teleportTo(currentGamePiece, action);
            }

            if (succeeded)
            {
                action.MoveTo.currentState = NodeState.Stowing;
                action.performTimer = 0;
            }
        }

        return succeeded;
    }

    private bool IntakePiece(bool button, NodeAction action)
    {
        if (button && !currentGamePiece)
        {
            var pieces = PoolObjects(action);
            currentGamePiece = ClosestPiece(pieces);
            if (!currentGamePiece) return false;
            currentGamePiece.startingDistance = DistanceToPiece(currentGamePiece);
            currentState = NodeState.Intakeing;
        }
        else if (currentState == NodeState.Intakeing && currentGamePiece)
        {
            currentState = NodeState.Intakeing;
            if (action.Animate)
            {
                if (!currentGamePiece) return false;
                if (GamePieceManager.AnimateTo(currentGamePiece, action, transform))
                {
                    currentState = NodeState.Stowing;
                }
                else
                {
                    if (currentGamePiece.startingDistance < DistanceToPiece(currentGamePiece))
                    {
                        currentState = NodeState.Stowing;
                        currentGamePiece.colliderParent.SetActive(true);
                        currentGamePiece.state = GamePieceState.World;
                        currentGamePiece.transform.parent = currentGamePiece.originalParent;
                        currentGamePiece = null;
                    }
                }
            }
            else
            {
                if (GamePieceManager.teleportTo(currentGamePiece, transform))
                {
                    currentState = NodeState.Stowing;
                }
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    private List<GamePiece> PoolObjects(NodeAction action)
    {
        pieces.Clear();
        var mask = LayerMask.GetMask("Piece");
        var colliders = Physics.OverlapBox(_intakeCollider.transform.position, _halfExtents,
            _intakeCollider.transform.rotation, mask);

        foreach (Collider coll in colliders)
        {
            var objectThing = coll.gameObject;
            var piece = Utils.FindParentObjectComponent<GamePiece>(objectThing);
            if (!piece) continue;
            if (piece.pieceType != action.PieceType || piece.state != GamePieceState.World) continue;
            pieces.Add(piece);
        }

        return pieces;
    }

    private GamePiece ClosestPiece(List<GamePiece> pieces)
    {
        switch (pieces.Count)
        {
            case 0:
                return null;
            case 1:
                return pieces[0];
        }

        var closest = pieces[0];
        var distance = DistanceToPiece(closest);

        foreach (var piece in pieces)
        {
            if (DistanceToPiece(piece) < distance)
            {
                closest = piece;
                distance = DistanceToPiece(piece);
            }
        }

        return closest;
    }

    private float DistanceToPiece(GamePiece piece)
    {
        var pose = transform.InverseTransformPoint(piece.transform.position);
        return pose.magnitude;
    }

    public void OverideActionSpeed(float speed, NodeAction action)
    {
        action.Speed = speed;
    }
}

[Serializable]
public class NodeAction
{
    public string Name;

    [Header("Node Behaviour on Action")] public NodeType Type;

    [ConditionalField(true, nameof(IsNotOuttake))]
    public bool Animate;

    [ConditionalField(true, nameof(SpeedVisible))]
    public float Speed;

    [HideInInspector] public float? overideSpeed { get; set; }

    [ConditionalField(true, nameof(AngularVisible))]
    public float AngularSpeed;

    [ConditionalField(true, nameof(IsTransfer))]
    public BuildNode MoveTo;

    [ConditionalField(true, nameof(IsOuttake))]
    public Direction Direction;

    [ConditionalField(true, nameof(IsOuttake))]
    public Vector3 Spin;

    [ConditionalField(true, nameof(IsNotIntake))]
    public float DelayTimer;

    [Header("General Settings")] public PieceNames PieceType;
    public NodeControlType ControlType;
    [HideInInspector] public float performTimer;
    public ControllerInputs ControllerButton;
    public KeyboardInputs KeyboardButton;

    private bool IsTransfer() => Type is NodeType.Transfer;
    private bool IsOuttake() => Type is NodeType.Outake;
    private bool IsNotOuttake() => Type is not NodeType.Outake;
    private bool IsNotIntake() => Type is not NodeType.Intake;
    private bool SpeedVisible() => (IsNotOuttake() && Animate) || IsOuttake();
    private bool AngularVisible() => (IsNotOuttake() && Animate);
}
