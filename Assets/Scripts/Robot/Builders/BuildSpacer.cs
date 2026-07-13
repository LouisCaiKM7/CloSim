using Core;
using UnityEngine;
using Utilities;

namespace Robot.Builders
{
    [ExecuteInEditMode]
    public class BuildSpacer : GeneratePart
    {
        [SerializeField] private SpacerType spacerType;

        [SerializeField] private bool shouldCollide = true;

        [SerializeField] private Units units;

        [SerializeField] private float spacerLength = 1;
        
        private static GameObject[] _loadedSpacers;

        private GameObject _spacer;

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

        public void SetShaft(SpacerType type)
        {
            spacerType = type;
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

            _loadedSpacers ??= Resources.LoadAll<GameObject>("Parts/Spacers");

            foreach (var loadedSpacer in _loadedSpacers)
            {
                if (loadedSpacer.name == spacerType.ToString())
                {
                    _spacer = loadedSpacer;
                }
            }

            Vector3 partScale = new Vector3(1, 1, spacerLength * _scaleFactor);

            if (!part || part != _spacer)
            {

                part = _spacer;

                partName = "spacer";

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