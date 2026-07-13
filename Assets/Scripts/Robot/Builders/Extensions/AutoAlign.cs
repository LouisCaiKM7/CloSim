using System;
using System.Collections.Generic;
using Core;
using MyBox;
using Robot.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Utilities;

namespace Robot.Builders.Extensions
{
    public class AutoAlign : MonoBehaviour
    {
        [SerializeField] private float alignDistance = 20;
        [SerializeField] private Pose2d alignOffset;
        [SerializeField] private AutoAlignType alginType = AutoAlignType.Release;

        [Header("Button Control Settings")]
        [ConditionalField(true, nameof(Predicate))]
        [SerializeField] private RobotCommand command = RobotCommand.AutoAlign;

        [Header("Input Settings")]
        [SerializeField] private string actionMapName = "Robot";

        [SerializeField] private bool advanced;

        [FormerlySerializedAs("drivePID")]
        [ConditionalField(nameof(advanced))]
        [SerializeField] private Pid drivePid;

        [FormerlySerializedAs("rotationPID")]
        [ConditionalField(nameof(advanced))]
        [SerializeField] private Pid rotationPid;

        private bool Predicate() => alginType == AutoAlignType.Button;

        private SwerveController _swerveController;
        private PidController _dPidController;
        private PidController _rPidController;

        private List<Pose2d> _targetNodes;

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
                return new Vector2(x, y);
            }

            public Pose2d(Transform trans)
            {
                x = trans.position.x;
                y = trans.position.z;
                angle = trans.rotation.eulerAngles.y;
            }

            public float GetAngle()
            {
                return Mathf.Repeat(angle + 90, 360);
            }
        }

        private void Start()
        {
            _swerveController = GetComponent<SwerveController>();

            var nodes = Utils.FindGameObjectsOnLayer("AutoAlignNodes");

            _targetNodes = new List<Pose2d>();

            foreach (var node in nodes)
            {
                _targetNodes.Add(new Pose2d(node.transform));
            }

            if (advanced)
            {
                _rPidController = new PidController
                {
                    proportionalGain = rotationPid.p,
                    derivativeGain = rotationPid.d,
                    integralGain = rotationPid.i,
                    outputMax = Mathf.Clamp(rotationPid.max, 0, 1),
                    outputMin = -Mathf.Clamp(rotationPid.max, 0, 1),
                    integralSaturation = 1
                };

                _dPidController = new PidController
                {
                    proportionalGain = drivePid.p,
                    derivativeGain = drivePid.d,
                    integralGain = drivePid.i,
                    outputMax = Mathf.Clamp(drivePid.max, 0, 1),
                    outputMin = -Mathf.Clamp(drivePid.max, 0, 1),
                    integralSaturation = 1
                };
            }
            else
            {
                _rPidController = new PidController
                {
                    proportionalGain = 0.1f,
                    derivativeGain = 0,
                    integralGain = 0,
                    outputMax = 0.5f,
                    outputMin = -0.5f,
                    integralSaturation = 1
                };

                _dPidController = new PidController
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

        private void FixedUpdate()
        {
            if (!Application.isPlaying)
                return;

            if (_swerveController == null)
                _swerveController = GetComponent<SwerveController>();

            if (_swerveController == null)
                return;

            if (_targetNodes == null || _targetNodes.Count == 0)
                return;

            switch (alginType)
            {
                case AutoAlignType.Button:
                    if (CheckButtonCondition() && DistanceToClosestNode() <= alignDistance * 0.0254f)
                    {
                        Align();
                    }

                    break;

                case AutoAlignType.Release:
                    if (DistanceToClosestNode() <= alignDistance * 0.0254f)
                    {
                        Align(true);
                    }

                    break;
            }
        }

        private bool CheckButtonCondition()
        {
            if (!TryResolveInput())
                return false;

            InputAction action = _inputMap.FindAction(command.ToString());

            return action != null && action.IsPressed();
        }

        private bool TryResolveInput()
        {
            if (_playerInput == null)
                _playerInput = GetComponent<PlayerInput>() ?? GetComponentInParent<PlayerInput>();

            if (_playerInput == null || _playerInput.actions == null)
                return false;

            _inputMap ??= _playerInput.actions.FindActionMap(actionMapName);

            if (_inputMap == null)
                return false;

            _inputMap.Enable();
            return true;
        }

        private void Align(bool disruptable = false)
        {
            Vector2 vector = VectorToClosestNode();

            float velocity = _dPidController.UpdateLinear(
                Time.fixedDeltaTime,
                vector.magnitude,
                0
            );

            float rInput = _rPidController.UpdateAngle(
                Time.fixedDeltaTime,
                transform.localRotation.eulerAngles.y,
                ClosestNode().GetAngle() + alignOffset.angle
            );

            Vector2 inputVector = vector.normalized * velocity;

            _swerveController.OverideInputs(-inputVector.y, inputVector.x, rInput, disruptable);
        }

        private float DistanceToClosestNode()
        {
            Vector2 localizedVector = ClosestNode().GetPosition() - GetRelativeAlignPosition();
            return localizedVector.magnitude;
        }

        private float DistanceToNode(Pose2d node)
        {
            Vector2 localizedVector = node.GetPosition() - GetRelativeAlignPosition();
            return localizedVector.magnitude;
        }

        private Vector2 VectorToClosestNode()
        {
            return GetRelativeAlignPosition() - ClosestNode().GetPosition();
        }

        private Vector2 GetRelativeAlignPosition()
        {
            Vector3 localOffset = new Vector3(
                alignOffset.GetPosition().x * 0.0254f,
                0,
                alignOffset.GetPosition().y * 0.0254f
            );

            Vector3 worldOffset = transform.TransformDirection(localOffset);

            return Vec3ToVec2(transform.position + worldOffset);
        }

        private Pose2d ClosestNode()
        {
            Pose2d closestNode = _targetNodes[0];
            float distance = DistanceToNode(closestNode);

            foreach (var node in _targetNodes)
            {
                float nodeDistance = DistanceToNode(node);

                if (nodeDistance < distance)
                {
                    closestNode = node;
                    distance = nodeDistance;
                }
            }

            return closestNode;
        }

        public Vector2 Vec3ToVec2(Vector3 vec3)
        {
            return new Vector2(vec3.x, vec3.z);
        }
    }
}