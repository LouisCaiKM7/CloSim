using Core;
using MyBox;
using UnityEngine;
using Utilities;

namespace Robot.Builders
{
    [ExecuteInEditMode]
    public class BuildShaft : GeneratePart
    {
        [SerializeField] private ShaftType shaftType;

        [SerializeField] private bool shouldCollide = true;

        [SerializeField] private Units units;

        [SerializeField] private float shaftLength = 10;

        [ConditionalField(true,  nameof(IsDead))]
        [SerializeField] private float shaftDiameter = 2;
        
        private bool IsDead() => shaftType == ShaftType.Dead;
        
        private static GameObject[] _loadedShafts;

        private GameObject _shaft;

        private ColliderDisabler _colliderDisabler;

        private float _scaleFactor;
        
        private Vector3 _position;
        private Vector3 _rotation;
        
        // Start is called before the first frame update
        private void Start()
        {
            Startup();
        }

        private void OnEnable()
        {
            Startup();
        }

        public void SetShaft(ShaftType type)
        {
            shaftType = type;
            BuildObjects();
        }

        public void SetPosition(Vector3 position)
        {
            this._position = position;
            BuildObjects();
        }

        public void SetRotation(Vector3 rotation)
        {
            this._rotation = rotation;
            BuildObjects();
        }

        // Update is called once per frame
        void Update()
        {
            BuildObjects();
        }

        private void BuildObjects()
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

            _loadedShafts ??= Resources.LoadAll<GameObject>("Parts/Shafts");

            foreach (var loadedShaft in _loadedShafts)
            {
                if (loadedShaft.name == shaftType.ToString())
                {
                    _shaft = loadedShaft;
                }
            }

            Vector3 partScale = new Vector3(shaftLength * _scaleFactor, 1, 1);

            if (IsDead())
            {
                partScale.z = shaftDiameter * _scaleFactor;
                partScale.y = shaftDiameter * _scaleFactor;
            }

            if (!part || part != _shaft)
            {

                part = _shaft;

                partName = "shaft";

                loadedPartLocation = _position;

                loadedPartRotation = Quaternion.Euler(_rotation);

                loadedPartScale = partScale;
            }
            else if (part)
            {
                loadedPartLocation = _position;

                loadedPartRotation = Quaternion.Euler(_rotation);
                
                loadedPartScale = partScale;
            }
        }
    }
}