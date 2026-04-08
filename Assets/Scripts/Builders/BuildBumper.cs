using System.Collections;
using System.Collections.Generic;
using MyBox;
using UnityEngine;
using UnityEngine.Serialization;
using Util;

[ExecuteInEditMode]
public class BuildBumper : GeneratePart
{
    [SerializeField] private BumperType bumperType;

    private bool IsAdjustable() => bumperVariant == BumperVariants.Side;
    
    [SerializeField] private BumperVariants bumperVariant;
    
    [ConditionalField(true, nameof(IsAdjustable))]
    [SerializeField] private Units units;

    [ConditionalField(true, nameof(IsAdjustable))] 
    [SerializeField] private float bumperLength = 28;
    
    private static GameObject[] loadedPlates;

    private GameObject _bumper;

    private float _scaleFactor;

    [HideInInspector] public Vector3 position { private get; set; }
    
    [HideInInspector] public Vector3 rotation  { private get; set; }

    // Start is called before the first frame update
    private void Start()
    {
        Startup();
    }

    private void OnEnable()
    {
        Startup();
    }

    public void setUnits(Units units)
    {
        this.units = units;
        buildObjects();
    }

    public void setLength(float length)
    {
        bumperLength = length;
        buildObjects();
    }

    public void SetBumper(BumperType type, BumperVariants variant)
    {
        bumperType = type;
        bumperVariant = variant;
        buildObjects();
    }

    public void SetPosition(Vector3 position)
    {
        this.position = position;
        buildObjects();
    }

    public void SetRotation(Vector3 rotation)
    {
        this.rotation = rotation;
        buildObjects();
    }

    // Update is called once per frame
    void Update()
    {
       buildObjects();
    }

    private void buildObjects()
    {

        if (Application.isPlaying) return;

        switch (units)
        {
            case Units.Inch:
                _scaleFactor = 0.0254f;
                break;
            case Units.Centimeter:
                _scaleFactor = 0.01f;
                break;
            case Units.Meter:
                _scaleFactor = 1.0f;
                break;
            case Units.Millimeter:
                _scaleFactor = 0.001f;
                break;
        }

        loadedPlates ??= Resources.LoadAll<GameObject>("Parts/Bumper") as GameObject[];

        if (bumperType == BumperType.Modern && bumperVariant == BumperVariants.Lift)
        {
            bumperVariant = BumperVariants.Side;
        }

        foreach (var plate in loadedPlates)
        {
            if (plate.name == bumperType.ToString() + bumperVariant.ToString())
            {
                _bumper = plate;
            }
        }

        Vector3 bumperScale = Vector3.one;
        if (IsAdjustable())
        {
            bumperScale.z = bumperLength * _scaleFactor;
        }

        if (!Part || Part != _bumper)
        {

            Part = _bumper;

            PartName = "Bumper";

            LoadedPartLocation = position;

            LoadedPartRotation = Quaternion.Euler(rotation);

            LoadedPartScale = bumperScale;
        }
        else if (Part)
        {
            LoadedPartLocation = position;

            LoadedPartRotation = Quaternion.Euler(rotation);

            LoadedPartScale = bumperScale;
        }
    }
}
