using System;
using MyBox;
using UnityEngine;
using UnityEngine.Serialization;
using Util;

namespace Generators
{
    [ExecuteInEditMode]
    public class BuildPlate : GeneratePart
    {
        [SerializeField] private PlateType plateType;

        private bool IsCustomPlate() => plateType is PlateType.Rectangle or PlateType.Triangle;
        
        [ConditionalField(true, nameof(IsCustomPlate))]
        [SerializeField] private PlateMaterials plateMaterial;
        
        [ConditionalField(true, nameof(IsCustomPlate))]
        [SerializeField] private Units units;

        [ConditionalField(true, nameof(IsCustomPlate))] [SerializeField]
        private float plateHeight = 5;
        
        [ConditionalField(true, nameof(IsCustomPlate))]
        [SerializeField] private float plateWidth = 5;

        [SerializeField] private bool shouldCollide = true;
        
        private static GameObject[] loadedPlates;

        private GameObject _plate;

        private ColliderDisabler colliderDisabler;
        
        private float _scaleFactor;

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
        void Update()
        {
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
            
            if (colliderDisabler)
            {
                colliderDisabler.SetState(shouldCollide);
            }
            else if (getLoadedPart())
            {
                colliderDisabler = Utils.TryGetComponentOnChild<ColliderDisabler>(getLoadedPart());
            }

            loadedPlates ??= Resources.LoadAll<GameObject>("Parts/Plates") as GameObject[];

            foreach (var plate in loadedPlates)
            {
                if (IsCustomPlate())
                {
                    if (plate.name == plateMaterial.ToString() + plateType.ToString())
                    {
                        _plate = plate;
                    }
                }
                if (plate.name == plateType.ToString())
                {
                    _plate = plate;
                }
            }

            Vector3 plateScale = Vector3.one;
            if (IsCustomPlate())
            {
                plateScale.x = plateWidth * _scaleFactor;
                plateScale.z = plateHeight * _scaleFactor;
            }

            if (!Part || Part != _plate)
            {

                Part = _plate;

                PartName = "plate";

                LoadedPartLocation = Vector3.zero;

                LoadedPartRotation = Quaternion.Euler(Vector3.zero);

                LoadedPartScale = plateScale;
            }
            else if (Part)
            {
                LoadedPartLocation = Vector3.zero;

                LoadedPartRotation = Quaternion.Euler(Vector3.zero);

                LoadedPartScale = plateScale;
            }
        }
    }
}