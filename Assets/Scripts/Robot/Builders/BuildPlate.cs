using Core;
using MyBox;
using UnityEngine;
using Utilities;

namespace Robot.Builders
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
        
        private static GameObject[] _loadedPlates;

        private GameObject _plate;

        private ColliderDisabler _colliderDisabler;
        
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
            
            if (_colliderDisabler)
            {
                _colliderDisabler.SetState(shouldCollide);
            }
            else if (GetLoadedPart())
            {
                _colliderDisabler = Utils.TryGetComponentOnChild<ColliderDisabler>(GetLoadedPart());
            }

            _loadedPlates ??= Resources.LoadAll<GameObject>("Parts/Plates");

            foreach (var plate in _loadedPlates)
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

            if (!part || part != _plate)
            {

                part = _plate;

                partName = "plate";

                loadedPartLocation = Vector3.zero;

                loadedPartRotation = Quaternion.Euler(Vector3.zero);

                loadedPartScale = plateScale;
            }
            else if (part)
            {
                loadedPartLocation = Vector3.zero;

                loadedPartRotation = Quaternion.Euler(Vector3.zero);

                loadedPartScale = plateScale;
            }
        }
    }
}