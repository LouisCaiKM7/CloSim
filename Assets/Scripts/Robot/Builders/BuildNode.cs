using System;
using System.Collections;
using System.Collections.Generic;
using Core;
using Field.Core;
using MyBox;
using Robot.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Utilities;

namespace Robot.Builders
{
    public enum BuildNodeBehaviorMode
    {
        Rebuilt,
        ReefscapeLegacy
    }
    
    [ExecuteAlways]
    public class BuildNode : MonoBehaviour
    {
        private BoxCollider _intakeCollider;
        public GamePiece currentGamePiece;

        [FormerlySerializedAs("Preload")] [SerializeField]
        private bool preload;

        [ConditionalField(true, nameof(ShowIntakeStuff))] [SerializeField]
        private Vector3 intakeSize = new Vector3(3f, 3f, 3f);

        [ConditionalField(nameof(preload))] [SerializeField]
        private PieceNames pieceName;

        public NodeState currentState;
        [FormerlySerializedAs("Actions")] public NodeAction[] actions;
        [SerializeField] private string actionMapName = "Robot";
        private PlayerInput _playerInput;
        private InputActionMap _inputMap;
        private GameObject _robotParent;
        private Vector3 _halfExtents;
        private readonly List<GamePiece> _gamePieces = new List<GamePiece>();
        private static GameObject[] _pieces;
        private bool _hasIntake;
        private bool ShowIntakeStuff() => _hasIntake;
        
        [SerializeField] private BuildNodeBehaviorMode behaviorMode = BuildNodeBehaviorMode.Rebuilt;

        private bool UseLegacyReefscapeBehavior => behaviorMode == BuildNodeBehaviorMode.ReefscapeLegacy;
        
        private Vector3 _lastIntakePosition;
        private Quaternion _lastIntakeRotation;

        private bool[] _cachedPressed;
        private bool[] _cachedHeld;
        private readonly float _minimumActionInterval = 0.02f;
        private bool[] _tapTransferActive;

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

            if (HasInputRequiredAction())
            {
                TryResolveInput();
            }

            _pieces ??= Resources.LoadAll<GameObject>("Pieces");
            SpawnPiece(pieceName.ToString(), _pieces);
        }

        private void EnsureInputCache()
        {
            int count = actions?.Length ?? 0;

            if (_cachedPressed == null || _cachedPressed.Length != count)
                _cachedPressed = new bool[count];

            if (_cachedHeld == null || _cachedHeld.Length != count)
                _cachedHeld = new bool[count];

            if (_tapTransferActive == null || _tapTransferActive.Length != count)
                _tapTransferActive = new bool[count];
        }

        private void EditorUpdate()
        {
            bool hasIntake = false;

            if (actions != null)
            {
                foreach (var action in actions)
                {
                    if (action.type == NodeType.Intake)
                    {
                        hasIntake = true;
                        break;
                    }
                }
            }

            _hasIntake = hasIntake;

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
                    DestroyImmediate(box);
            }
        }

        private bool HasInputRequiredAction()
        {
            if (actions == null)
                return false;

            foreach (var t in actions)
            {
                if (t is { inputRequired: true })
                    return true;
            }

            return false;
        }

        private bool TryResolveInput()
        {
            if (_playerInput == null)
            {
                if (_robotParent == null)
                    _robotParent = Utils.FindParentPlayerInput(gameObject);

                if (_robotParent != null)
                    _playerInput = _robotParent.GetComponent<PlayerInput>();

                if (_playerInput == null)
                    _playerInput = GetComponentInParent<PlayerInput>();
            }

            if (_playerInput == null || _playerInput.actions == null)
                return false;

            _inputMap = _playerInput.actions.FindActionMap(actionMapName);
            if (_inputMap == null)
                return false;

            _inputMap.Enable();
            return true;
        }

        private void SpawnPiece(string gamePieceName, GameObject[] pieces)
        {
            if (!preload) return;

            // ONLINE: host-authoritative game pieces (Online.Sync.GamePieceNetworkRegistrar). A client-only
            // instance of a networked robot must not spawn its own local preload piece -- the host owns
            // creation and replicates it via Mirror. Offline (no network session) this is always false.
            if (Online.Sync.GamePieceNetworkRegistrar.SuppressLocalSpawn)
                return;

            foreach (var piece in pieces)
            {
                if (piece.name != gamePieceName) continue;

                currentGamePiece = Instantiate(piece, transform.position, transform.rotation, transform)
                    .GetComponent<GamePiece>();

                // ONLINE: publish the host-spawned preload piece to clients. No-op offline and on clients.
                Online.Sync.GamePieceNetworkRegistrar.RegisterSpawned(currentGamePiece.gameObject);

                currentState = NodeState.Stowing;
                return;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                EditorUpdate();
                return;
            }

            CacheInput();

            if (UseLegacyReefscapeBehavior)
            {
                RunNodeActionsFixed();
                ClearPressedCache();
            }
        }

        private void CacheInput()
        {
            if (actions == null)
                return;

            EnsureInputCache();

            for (int i = 0; i < actions.Length; i++)
            {
                ref NodeAction action = ref actions[i];

                if (action.inputRequired)
                {
                    if (_inputMap == null && !TryResolveInput())
                        continue;

                    _cachedPressed[i] |= RobotInputBindingUtility.WasPressedThisFrame(_inputMap, action);
                    _cachedHeld[i] = RobotInputBindingUtility.IsPressed(_inputMap, action);
                }
                else
                {
                    _cachedPressed[i] = true;
                    _cachedHeld[i] = true;
                }
            }
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying)
                return;

            if (UseLegacyReefscapeBehavior)
                return;

            RunNodeActionsFixed();
            ClearPressedCache();
        }

        private void ClearPressedCache()
        {
            if (_cachedPressed == null)
                return;

            for (int i = 0; i < _cachedPressed.Length; i++)
                _cachedPressed[i] = false;
        }

        private void RunNodeActionsFixed()
        {
            if (actions == null)
                return;

            EnsureInputCache();

            var actionFinished = false;
            var actionDone = false;

            for (int i = 0; i < actions.Length; i++)
            {
                ref NodeAction action = ref actions[i];

                bool buttonPressed = _cachedPressed[i];
                bool buttonHeld = _cachedHeld[i];

                switch (action.type)
                {
                    case NodeType.Intake:
                        if (Fms.RobotState == RobotState.Disabled) break;
                        if (!_intakeCollider) break;

                        switch (action.controlType)
                        {
                            case NodeControlType.Hold:
                                if ((buttonHeld && !currentGamePiece) ||
                                    (currentState == NodeState.Intaking && currentGamePiece))
                                {
                                    actionDone = true;
                                }

                                IntakePiece(buttonHeld, action);
                                break;

                            case NodeControlType.Tap:
                                if (UseLegacyReefscapeBehavior)
                                {
                                    if (buttonPressed)
                                    {
                                        actionDone = true;
                                        StartCoroutine(TransferPieceCo(buttonPressed, action));
                                    }
                                }
                                else
                                {
                                    if (buttonPressed)
                                        _tapTransferActive[i] = true;

                                    if (_tapTransferActive[i])
                                    {
                                        actionDone = true;
                                        var finishedTransfer = TransferPiece(true, buttonPressed, ref action);

                                        if (finishedTransfer)
                                            _tapTransferActive[i] = false;
                                    }
                                }

                                break;

                            case NodeControlType.AlwaysPerform:
                                if (!currentGamePiece ||
                                    (currentState == NodeState.Intaking && currentGamePiece))
                                {
                                    actionDone = true;
                                }

                                IntakePiece(true, action);
                                break;
                        }

                        break;

                    case NodeType.Transfer:
                    {
                        if (!action.moveTo || !currentGamePiece || action.moveTo.currentGamePiece ||
                            action.pieceType != currentGamePiece.pieceType)
                        {
                            if (_tapTransferActive != null && i < _tapTransferActive.Length)
                                _tapTransferActive[i] = false;

                            break;
                        }

                        var finishedTransfer = false;

                        switch (action.controlType)
                        {
                            case NodeControlType.Hold:
                                if (buttonHeld)
                                    actionDone = true;

                                finishedTransfer = TransferPiece(buttonHeld, buttonPressed, ref action);
                                break;

                            case NodeControlType.Tap:
                                if (buttonPressed)
                                    _tapTransferActive[i] = true;

                                if (_tapTransferActive[i])
                                {
                                    actionDone = true;
                                    finishedTransfer = TransferPiece(true, buttonPressed, ref action);

                                    if (finishedTransfer)
                                        _tapTransferActive[i] = false;
                                }

                                break;

                            case NodeControlType.AlwaysPerform:
                                actionDone = true;
                                finishedTransfer = TransferPiece(
                                    true,
                                    currentState != NodeState.Transfering,
                                    ref action
                                );
                                currentState = NodeState.Transfering;
                                break;
                        }

                        if (finishedTransfer)
                            actionFinished = true;

                        break;
                    }

                    case NodeType.OutTake:
                        HandleOuttakeAction(ref action, buttonPressed, buttonHeld, false, ref actionDone,
                            ref actionFinished);
                        break;

                    case NodeType.Hp:
                        HandleOuttakeAction(ref action, buttonPressed, buttonHeld, true, ref actionDone,
                            ref actionFinished);
                        break;
                }
            }

            if ((currentGamePiece && currentState == NodeState.Stowing) || (!actionDone && currentGamePiece))
            {
                StowCurrentPiece();
            }
            else if (actionFinished)
            {
                currentGamePiece = null;
                currentState = NodeState.Stowing;
            }
        }

        private void HandleOuttakeAction(
            ref NodeAction action,
            bool buttonPressed,
            bool buttonHeld,
            bool isHumanPlayerRelease,
            ref bool actionDone,
            ref bool actionFinished)
        {
            if (Fms.RobotState == RobotState.Disabled) return;
            if (!currentGamePiece) return;
            if (action.pieceType != currentGamePiece.pieceType) return;

            bool wantsOuttake = action.controlType switch
            {
                NodeControlType.Hold => buttonHeld,
                NodeControlType.Tap => buttonPressed,
                NodeControlType.AlwaysPerform => true,
                _ => false
            };

            if (!wantsOuttake) return;

            actionDone = true;

            bool timerOk = action.controlType switch
            {
                NodeControlType.Hold => PerformTimerCheck(ref action, buttonPressed),
                NodeControlType.Tap => PerformTimerCheck(ref action, buttonPressed),
                NodeControlType.AlwaysPerform => PerformTimerCheck(ref action),
                _ => false
            };

            if (!timerOk) return;

            currentState = NodeState.Outaking;

            float originalSpeed = action.speed;

            if (isHumanPlayerRelease)
            {
                action.speed = UnityEngine.Random.Range(
                    action.speed - action.hpRandomizer,
                    action.speed + action.hpRandomizer
                );
            }

            var finishedOuttake = GamePieceManager.ReleaseToWorld(
                currentGamePiece,
                action,
                isHumanPlayerRelease
            );
            var releasedPiece = currentGamePiece;

            action.speed = originalSpeed;

            StartCoroutine(GamePieceManager.EnableColliders(releasedPiece));

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
        }

        private bool PerformTimerCheck(ref NodeAction action, bool onPressed = false, bool dontReset = false)
        {
            if (onPressed)
                action.performTimer = 0f;

            if (UseLegacyReefscapeBehavior)
            {
                action.performTimer += Time.deltaTime;

                if (action.performTimer > action.delayTimer || action.delayTimer == 0)
                {
                    if (dontReset) return true;

                    action.performTimer = 0f;
                    return true;
                }

                return false;
            }

            float requiredDelay = action.delayTimer <= 0f
                ? 0f
                : Mathf.Max(action.delayTimer, _minimumActionInterval);

            action.performTimer += Time.fixedDeltaTime;

            if (requiredDelay <= 0f || action.performTimer >= requiredDelay)
            {
                if (dontReset)
                    return true;

                action.performTimer = 0f;
                return true;
            }

            return false;
        }

        private bool TransferPiece(bool button, bool buttonPressed, ref NodeAction action)
        {
            if (!currentGamePiece) return false;
            if (action.pieceType != currentGamePiece.pieceType) return false;
            if (!PerformTimerCheck(ref action, buttonPressed, true)) return false;

            var succeeded = false;

            if (!currentGamePiece) return false;
            if (currentGamePiece.pieceType != action.pieceType) return false;

            if (button && currentGamePiece)
            {
                if (action.animate)
                {
                    currentState = NodeState.Transfering;
                    succeeded = GamePieceManager.AnimateTo(currentGamePiece, action);
                }
                else
                {
                    currentState = NodeState.Transfering;
                    succeeded = GamePieceManager.TeleportTo(currentGamePiece, action);
                }

                if (succeeded)
                {
                    action.moveTo.currentState = NodeState.Stowing;
                    action.performTimer = 0;
                }
            }

            return succeeded;
        }

        private void IntakePiece(bool button, NodeAction action)
        {
            if (button && !currentGamePiece)
            {
                var pieces = PoolObjects(action);
                currentGamePiece = ClosestPiece(pieces);

                if (!currentGamePiece) return;

                currentGamePiece.startingDistance = DistanceToPiece(currentGamePiece);
                currentState = NodeState.Intaking;
            }
            else if (currentState == NodeState.Intaking && currentGamePiece)
            {
                currentState = NodeState.Intaking;

                if (action.animate)
                {
                    if (!currentGamePiece) return;

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
                    if (GamePieceManager.TeleportTo(currentGamePiece, transform))
                    {
                        currentState = NodeState.Stowing;
                    }
                }
            }
        }

        private List<GamePiece> PoolObjects(NodeAction action)
        {
            _gamePieces.Clear();

            var mask = LayerMask.GetMask("Piece");

            var colliders = Physics.OverlapBox(
                _intakeCollider.transform.position,
                _halfExtents,
                _intakeCollider.transform.rotation,
                mask
            );

            foreach (Collider coll in colliders)
            {
                var objectThing = coll.gameObject;
                var piece = Utils.FindParentObjectComponent<GamePiece>(objectThing);

                if (!piece) continue;
                if (piece.pieceType != action.pieceType || piece.state != GamePieceState.World) continue;

                _gamePieces.Add(piece);
            }

            return _gamePieces;
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
        
        private void StowCurrentPiece()
        {
            if (!currentGamePiece)
                return;

            if (UseLegacyReefscapeBehavior)
            {
                currentState = NodeState.Stowing;
                GamePieceManager.TeleportTo(currentGamePiece, transform);
                return;
            }

            GamePieceManager.TeleportTo(currentGamePiece, transform);

            currentGamePiece.state = GamePieceState.Stationary;

            if (currentGamePiece.colliderParent)
                currentGamePiece.colliderParent.SetActive(false);

            if (currentGamePiece.rb)
            {
                currentGamePiece.rb.velocity = Vector3.zero;
                currentGamePiece.rb.angularVelocity = Vector3.zero;
            }

            currentState = NodeState.Stowing;
        }
        
        private IEnumerator TransferPieceCo(bool buttonPressed, NodeAction action)
        {
            if (!currentGamePiece || action.pieceType != currentGamePiece.pieceType)
                yield break;

            bool finished = false;

            while (!finished && currentGamePiece)
            {
                finished = TransferPiece(buttonPressed, buttonPressed, ref action);
                yield return null;
            }

            currentGamePiece = null;
        }
    }

    [Serializable]
    public class NodeAction
    {
        [FormerlySerializedAs("Name")] public string name;

        [FormerlySerializedAs("Type")] [Header("Node Behaviour on Action")]
        public NodeType type;

        [FormerlySerializedAs("Animate")] [ConditionalField(true, nameof(IsNotOuttakeLike))]
        public bool animate;

        [FormerlySerializedAs("Speed")] [ConditionalField(true, nameof(SpeedVisible))]
        public float speed;

        [FormerlySerializedAs("HpRandomizer")] [ConditionalField(true, nameof(IsHp))]
        public float hpRandomizer = 25f;

        public float? overrideSpeed { get; set; }

        [FormerlySerializedAs("AngularSpeed")] [ConditionalField(true, nameof(AngularVisible))]
        public float angularSpeed;

        [FormerlySerializedAs("MoveTo")] [ConditionalField(true, nameof(IsTransfer))]
        public BuildNode moveTo;

        [FormerlySerializedAs("Direction")] [ConditionalField(true, nameof(IsOuttakeLike))]
        public Direction direction;

        [FormerlySerializedAs("Spin")] [ConditionalField(true, nameof(IsOuttakeLike))]
        public Vector3 spin;

        [FormerlySerializedAs("DelayTimer")] [ConditionalField(true, nameof(IsNotIntake))]
        public float delayTimer;

        [FormerlySerializedAs("PieceType")] [Header("General Settings")]
        public PieceNames pieceType;

        [FormerlySerializedAs("ControlType")] public NodeControlType controlType;

        [HideInInspector] public float performTimer;

        [FormerlySerializedAs("InputRequired")] [Header("Input Settings")]
        public bool inputRequired = true;

        [FormerlySerializedAs("Command")] [ConditionalField(nameof(inputRequired))]
        public RobotCommand command;

        private bool IsTransfer() => type is NodeType.Transfer;
        private bool IsOuttake() => type is NodeType.OutTake;
        private bool IsHp() => type is NodeType.Hp;
        private bool IsOuttakeLike() => IsOuttake() || IsHp();
        private bool IsNotOuttakeLike() => !IsOuttakeLike();
        private bool IsNotIntake() => type is not NodeType.Intake;
        private bool SpeedVisible() => (IsNotOuttakeLike() && animate) || IsOuttakeLike();
        private bool AngularVisible() => IsNotOuttakeLike() && animate;
    }
}