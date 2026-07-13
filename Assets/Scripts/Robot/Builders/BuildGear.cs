using System.Collections.Generic;
using Core;
using UnityEngine;
using Utilities;

namespace Robot.Builders
{
    public class BuildGear : GeneratePart
    {
        [SerializeField] private GearType gearType;
        private GearType _previousGearType;
        [SerializeField] private InspectorDropdown toothCount;
    
        [SerializeField] private bool shouldCollide = true;
        
        private static GameObject[] _loadedGears;

        private GameObject _gear;

        private ColliderDisabler _colliderDisabler;

        private readonly List<string> _selectableToothCount = new List<string>();
    
        // Start is called before the first frame update
        private void Start()
        {
            Startup();
        }

        private void OnEnable()
        {
            Startup();
        }

        // Update is called once per frame
        private void Update()
        {
            _selectableToothCount.Clear();
            switch (gearType)
            {
                case (GearType.Hex) :
                    _selectableToothCount.Add("18t");
                    _selectableToothCount.Add("24t");
                    _selectableToothCount.Add("32t");
                    _selectableToothCount.Add("48t");
                    _selectableToothCount.Add("54t");
                    break;
                case (GearType.Pinion) :
                    _selectableToothCount.Add("10t");
                    _selectableToothCount.Add("12t");
                    _selectableToothCount.Add("14t");
                    break;
                case (GearType.Spline) :
                    _selectableToothCount.Add("48t");
                    _selectableToothCount.Add("54t");
                    _selectableToothCount.Add("62t");
                    _selectableToothCount.Add("72t");
                    break;
            }

            if (_previousGearType != gearType || toothCount.canBeSelected.Count == 0)
            {
                toothCount.canBeSelected = _selectableToothCount;
            }

            _previousGearType = gearType;
        
        
            BuildPart();
        }

        private void BuildPart()
        {
            if (_colliderDisabler)
            {
                _colliderDisabler.SetState(shouldCollide);
            }
            else if (GetLoadedPart())
            {
                _colliderDisabler = Utils.TryGetComponentOnChild<ColliderDisabler>(GetLoadedPart());
            }

            _loadedGears ??= Resources.LoadAll<GameObject>("Parts/Gears");
            
            foreach (var loadedGear in _loadedGears)
            {
                if (loadedGear.name == gearType.ToString() + toothCount.selectedName)
                {
                    _gear = loadedGear;
                }
            }

            if (!part || part != _gear)
            {

                part = _gear;

                partName = "gear";

                loadedPartLocation = Vector3.zero;

                loadedPartRotation = Quaternion.Euler(Vector3.zero);

                loadedPartScale = Vector3.one;
            }
            else if (part)
            {
                loadedPartLocation = Vector3.zero;

                loadedPartRotation = Quaternion.Euler(Vector3.zero);

                loadedPartScale = Vector3.one;
            }
        }
    }
}
