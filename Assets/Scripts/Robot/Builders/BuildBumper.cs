using Core;
using MyBox;
using UnityEngine;

namespace Robot.Builders
{
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
    
        private static GameObject[] _loadedPlates;

        private GameObject _bumper;

        private float _scaleFactor;

        private Vector3 position { get; set; }

        private Vector3 rotation  { get; set; }

        // Start is called before the first frame update
        private void Start()
        {
            Startup();
        }

        private void OnEnable()
        {
            Startup();
        }

        public void SetUnits(Units buildUnits)
        {
            this.units = buildUnits;
            BuildObjects();
        }

        public void SetLength(float length)
        {
            bumperLength = length;
            BuildObjects();
        }

        public void SetBumper(BumperType type, BumperVariants variant)
        {
            bumperType = type;
            bumperVariant = variant;
            BuildObjects();
        }

        public void SetPosition(Vector3 buildPosition)
        {
            this.position = buildPosition;
            BuildObjects();
        }

        public void SetRotation(Vector3 buildRotation)
        {
            this.rotation = buildRotation;
            BuildObjects();
        }

        // Update is called once per frame
        void Update()
        {
            BuildObjects();
        }

        private void BuildObjects()
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

            _loadedPlates ??= Resources.LoadAll<GameObject>("Parts/Bumper");

            if (bumperType == BumperType.Modern && bumperVariant == BumperVariants.Lift)
            {
                bumperVariant = BumperVariants.Side;
            }

            foreach (var plate in _loadedPlates)
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

            if (!part || part != _bumper)
            {

                part = _bumper;

                partName = "Bumper";

                loadedPartLocation = position;

                loadedPartRotation = Quaternion.Euler(rotation);

                loadedPartScale = bumperScale;
            }
            else if (part)
            {
                loadedPartLocation = position;

                loadedPartRotation = Quaternion.Euler(rotation);

                loadedPartScale = bumperScale;
            }
        }
    }
}
