using System;
using System.Collections.Generic;
using System.Reflection;
using Core;
using MyBox;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Utilities
{
    public static class RobotInputBindingUtility
    {
        private const BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static bool WasPressedThisFrame(InputActionMap inputMap, object bindingSource)
        {
            return ReadInputState(inputMap, bindingSource, action => action.WasPressedThisFrame());
        }

        public static bool IsPressed(InputActionMap inputMap, object bindingSource)
        {
            return ReadInputState(inputMap, bindingSource, action => action.IsPressed());
        }

        private static bool ReadInputState(
            InputActionMap inputMap,
            object bindingSource,
            Func<InputAction, bool> readAction)
        {
            if (inputMap == null || bindingSource == null || readAction == null)
                return false;
            
            if (TryGetEnumName(bindingSource, "command", out string commandName) ||
                TryGetEnumName(bindingSource, "Command", out commandName) ||
                TryGetEnumName(bindingSource, "robotCommand", out commandName) ||
                TryGetEnumName(bindingSource, "RobotCommand", out commandName) ||
                TryGetEnumName(bindingSource, "inputCommand", out commandName) ||
                TryGetEnumName(bindingSource, "InputCommand", out commandName))
            {
                InputAction commandAction = inputMap.FindAction(commandName);
                if (commandAction != null)
                    return readAction(commandAction);
            }

            return false;
        }

        private static bool TryGetEnumName(object source, string memberName, out string value)
        {
            value = null;

            if (!TryGetMemberValue(source, memberName, out object raw) || raw == null)
                return false;

            Type rawType = raw.GetType();
            if (!rawType.IsEnum)
                return false;

            value = raw.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetMemberValue(object source, string memberName, out object value)
        {
            value = null;
            Type type = source.GetType();

            while (type != null)
            {
                FieldInfo field = type.GetField(memberName, ReflectionFlags);
                if (field != null)
                {
                    value = field.GetValue(source);
                    return true;
                }

                PropertyInfo property = type.GetProperty(memberName, ReflectionFlags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    value = property.GetValue(source);
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }
    }

    [Serializable]
    public class InspectorDropdown
    {
        [HideInInspector] public List<String> canBeSelected = new List<String>();
        [HideInInspector] public int selectedIndex;
        [HideInInspector] public string selectedName = "";
    }

    [Serializable]
    public class SetPoint
    {
        public string setpointName;
        
        [Header("Behaviour Settings")]
        public ControlType controlType;
        
        [ConditionalField(true, nameof(IsSequence))]
        public SequenceType sequenceType;
        
        [ConditionalField(true,nameof(ShowSequenceTo))]
        public string sequenceTo;
        
        [ConditionalField(true, nameof(ShouldShowDelay))]
        public float delay;
        private bool ShouldShowDelay() => sequenceType == SequenceType.Delay;
        private bool IsSequence() => controlType is ControlType.Sequence or ControlType.SequenceStart;
        private bool ShowPersist() => (controlType is ControlType.Sequence or ControlType.LastPressed or ControlType.SequenceStart);
        private bool ShowSequenceTo() => IsSequence() && sequenceType != SequenceType.End;

        private bool HidePoint() => ShowPersist() && persist && controlType is ControlType.Sequence or ControlType.LastPressed or ControlType.SequenceStart;


        [Header("Generic")]
        [ConditionalField(true, nameof(ShowPersist))] [SerializeField]
        private bool persist;
        [ConditionalField(true, nameof(HidePoint), true)]
        [SerializeField]
        private float point;
        
        [HideInInspector]
        public bool shouldScaleToUnits;
        [HideInInspector]
        public Units units;

        public float GetPoint()
        {
            return shouldScaleToUnits ? point * units switch
            {
                Units.Inch => 0.0254f,
                Units.Centimeter => 0.01f,
                Units.Meter => 1.0f,
                Units.Millimeter => 0.001f,
                _ => 1.0f
                
            } : point;
        }

        public bool GetPersist()
        {
            return persist;
        }

        [FormerlySerializedAs("Command")]
        [Header("Control Settings")]
        [Tooltip("When enabled, this setpoint reads the named semantic Input Action instead of the legacy controller/keyboard action names.")]
        public RobotCommand command;
    }
    
    [Serializable]
    public struct Pid
    {
        public float p;
        public float i;
        public float d;
        public float max;
    }
}
