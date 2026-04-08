using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;

public class BuildGear : GeneratePart
{
    [SerializeField] private GearType gearType;
    private GearType previousGearType;
    [SerializeField] private InspectorDropdown toothCount;
    
    [SerializeField] private bool shouldCollide = true;
        
    private static GameObject[] loadedGears;

    private GameObject _gear;

    private ColliderDisabler colliderDisabler;

    private List<string> selectableToothCount = new List<string>();
    
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
        selectableToothCount.Clear();
        switch (gearType)
        {
            case (GearType.hex) :
                selectableToothCount.Add("18t");
                selectableToothCount.Add("24t");
                selectableToothCount.Add("32t");
                selectableToothCount.Add("48t");
                selectableToothCount.Add("54t");
                break;
            case (GearType.pinion) :
                selectableToothCount.Add("10t");
                selectableToothCount.Add("12t");
                selectableToothCount.Add("14t");
                break;
            case (GearType.spline) :
                selectableToothCount.Add("48t");
                selectableToothCount.Add("54t");
                selectableToothCount.Add("62t");
                selectableToothCount.Add("72t");
                break;
        }

        if (previousGearType != gearType || toothCount.canBeSelected.Count == 0)
        {
            toothCount.canBeSelected = selectableToothCount;
        }

        previousGearType = gearType;
        
        
        BuildPart();
    }

    private void BuildPart()
    {
        if (colliderDisabler)
        {
            colliderDisabler.SetState(shouldCollide);
        }
        else if (getLoadedPart())
        {
            colliderDisabler = Utils.TryGetComponentOnChild<ColliderDisabler>(getLoadedPart());
        }

        loadedGears ??= Resources.LoadAll<GameObject>("Parts/Gears") as GameObject[];
            
        foreach (var loadedGear in loadedGears)
        {
            if (loadedGear.name == gearType.ToString() + toothCount.selectedName)
            {
                _gear = loadedGear;
            }
        }

        if (!Part || Part != _gear)
        {

            Part = _gear;

            PartName = "gear";

            LoadedPartLocation = Vector3.zero;

            LoadedPartRotation = Quaternion.Euler(Vector3.zero);

            LoadedPartScale = Vector3.one;
        }
        else if (Part)
        {
            LoadedPartLocation = Vector3.zero;

            LoadedPartRotation = Quaternion.Euler(Vector3.zero);

            LoadedPartScale = Vector3.one;
        }
    }
}
