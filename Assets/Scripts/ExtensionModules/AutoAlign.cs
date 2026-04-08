using System;
using System.Collections;
using System.Collections.Generic;
using MyBox;
using UnityEngine;
using UnityEngine.InputSystem;
using Util;

public class AutoAlign : MonoBehaviour
{
    [SerializeField] private float alignDistance = 20;
    [SerializeField] private Pose2d alignOffset;
    [SerializeField] private AutoAlginType alginType = AutoAlginType.release;

    [ConditionalField(true, nameof(Predicate))] [SerializeField]
    private ControllerInputs controllerButton;

    [ConditionalField(true, nameof(Predicate))] [SerializeField]
    private KeyboardInputs keyboardButton;

    [SerializeField] private bool advanced;

    [ConditionalField(nameof(advanced))] [SerializeField]
    private PID drivePID;

    [ConditionalField(nameof(advanced))] [SerializeField]
    private PID rotationPID;
    private bool Predicate() => alginType == AutoAlginType.button;
    
    private SwerveController _swerveController;
    private PIDController _dPIDController;
    private PIDController _rPIDController;
    
    private List<Pose2d> targetNodes;
    
    private PlayerInput _playerInput;
    private InputActionMap _inputMap;

    
    [Serializable]
    private struct Pose2d
    {
        public float x;
        public float y;
        public float angle;

        public Pose2d(float x, float y, float angle)
        {
            this.x = x;
            this.y = y;
            this.angle = angle;
        }

        public Vector2 GetPosition()
        {
            return new Vector2(this.x, this.y);
        }

        public Pose2d(Transform trans)
        {
            this.x = trans.position.x;
            this.y = trans.position.z;
            this.angle = trans.rotation.eulerAngles.y;
        }

        public float getAngle()
        {
            return Mathf.Repeat(angle + 90, 360);
        }
    }
    
    // Start is called before the first frame update
    void Start()
    {
        _swerveController = GetComponent<SwerveController>();
        var nodes = Utils.FindGameObjectsOnLayer("AutoAlignNodes");

        targetNodes = new List<Pose2d>();
        
        foreach (var node in nodes)
        {
            targetNodes.Add(new Pose2d(node.transform)); 
        }

        if (advanced)
        {
            _rPIDController = new PIDController
            {
                proportionalGain = rotationPID.p,
                derivativeGain = rotationPID.d,
                integralGain = rotationPID.i,
                outputMax = Mathf.Clamp(rotationPID.max, 0, 1),
                outputMin = -Mathf.Clamp(rotationPID.max, 0, 1),
                integralSaturation = 1
            };
        
            _dPIDController = new PIDController
            {
                proportionalGain = drivePID.p,
                derivativeGain = drivePID.d,
                integralGain = drivePID.i,
                outputMax = Mathf.Clamp(drivePID.max, 0, 1),
                outputMin = -Mathf.Clamp(drivePID.max, 0, 1),
                integralSaturation = 1
            };
        }
        else
        {
            _rPIDController = new PIDController
            {
                proportionalGain = 0.1f,
                derivativeGain = 0,
                integralGain = 0,
                outputMax = 0.5f,
                outputMin = -0.5f,
                integralSaturation = 1
            };

            _dPIDController = new PIDController
            {
                proportionalGain = 1,
                derivativeGain = 0,
                integralGain = 0,
                outputMax = 0.5f,
                outputMin = -0.5f,
                integralSaturation = 1
            };
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (_playerInput == null)
        {
            _playerInput = gameObject.GetComponent<PlayerInput>();
            return;
        }
        else
        {
            _inputMap = _playerInput.actions.FindActionMap("Robot");
            _inputMap.Enable();
        }
        
        if (_swerveController == null)
        {
            _swerveController = GetComponent<SwerveController>();
        }

        switch (alginType)
        {
            case AutoAlginType.button:
                var controllerAction = _inputMap.FindAction(controllerButton.ToString());
                var keyboardAction = _inputMap.FindAction(keyboardButton.ToString());
                var controllerHeld = controllerAction.IsPressed() && 
                                     (controllerAction.activeControl?.device is Gamepad);
                var keyboardHeld = keyboardAction.IsPressed() && 
                                   (keyboardAction.activeControl?.device is Keyboard);
                var buttonHeld = controllerHeld || keyboardHeld;
                

                if (buttonHeld && distanceToClosestNode() <= alignDistance * 0.0254f)
                {
                    Align();
                }
                
                break;
            case AutoAlginType.release:
                if (distanceToClosestNode() <= alignDistance * 0.0254f)
                {
                    Align(true);
                }
                break;
        }
        
    }

    private void Align(bool disruptable = false)
    {
        Vector2 vector = vectorToClosestNode();
            
        float velocity = _dPIDController.UpdateLinear(Time.fixedDeltaTime, vector.magnitude, 0);
        float rInput = _rPIDController.UpdateAngle(Time.fixedDeltaTime, transform.localRotation.eulerAngles.y, closestNode().getAngle()+alignOffset.angle);

        Vector2 inputVector = vector.normalized * (velocity);

        _swerveController.overideInputs(-inputVector.y, inputVector.x, rInput, disruptable);
    }

    private float distanceToClosestNode()
    {
        Vector2 localizedVector = closestNode().GetPosition() - getRelativeAlignPosition();
        float distance = localizedVector.magnitude;
        return distance;
    }
    
    private float distanceToNode(Pose2d node)
    {
        Vector2 localizedVector = node.GetPosition() - getRelativeAlignPosition();
        float distance = localizedVector.magnitude;
        return distance;
    }

    private Vector2 vectorToClosestNode()
    {
        Vector2 localizedVector = getRelativeAlignPosition() - closestNode().GetPosition();
        return localizedVector;
    }
    
    private Vector2 getRelativeAlignPosition()
    {
        // 1. Convert the local offset to world space based on the current rotation
        // transform.TransformVector handles the rotation for us
        Vector3 localOffset = new Vector3(alignOffset.GetPosition().x * 0.0254f, 0, alignOffset.GetPosition().y * 0.0254f);
        Vector3 worldOffset = transform.TransformDirection(localOffset);

        // 2. Add the rotated offset to the current position
        return vec3ToVec2(transform.position + worldOffset);
    }

    private Pose2d closestNode()
    {
        Pose2d closestNode = targetNodes[0];
        float distance = distanceToNode(closestNode);
        
            
        foreach (var node in targetNodes)
        {
            if (distanceToNode(node) < distance)
            {
                closestNode = node;
                distance = distanceToNode(node);
            }
        }
        
        return closestNode;
    }

    public Vector2 vec3ToVec2(Vector3 vec3)
    {
        return new Vector2(vec3.x, vec3.z);
    }
}
