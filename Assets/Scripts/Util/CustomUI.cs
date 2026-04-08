using System;
using System.Collections.Generic;
using MyBox;
using UnityEngine;

namespace Util
{
    [Serializable]
    public class InspectorDropdown
    {
        [HideInInspector] public List<String> canBeSelected = new List<String>();
        [HideInInspector] public int selectedIndex = 0;
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
        private bool ShouldShowDelay() => sequenceType == SequenceType.delay;
        private bool IsSequence() => controlType is ControlType.Sequence or ControlType.SequenceStart;
        private bool ShowPersist() => (controlType is ControlType.Sequence or ControlType.LastPressed or ControlType.SequenceStart);
        private bool ShowSequenceTo() => IsSequence() && sequenceType != SequenceType.end;

        private bool HidePoint() => ShowPersist() && persist && controlType is ControlType.Sequence or ControlType.LastPressed or ControlType.SequenceStart;


        [Header("Generic")] 
        [ConditionalField(true, nameof(ShowPersist))] [SerializeField]
        private bool persist;
        [ConditionalField(true, nameof(HidePoint), true)]
        [SerializeField]
        private float point;
        
        [HideInInspector]
        public bool shouldScaleToUnits = false;
        [HideInInspector]
        public Units units;

        public float getPoint()
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

        public bool getPersist()
        {
            return persist;
        }

        [Header("Control Settings")]
        public ControllerInputs controllerButton;
        public KeyboardInputs keyboardButton; 
    }
    
    [Serializable]
    public struct PID
    {
        public float p;
        public float i;
        public float d;
        public float max;
    }
}
