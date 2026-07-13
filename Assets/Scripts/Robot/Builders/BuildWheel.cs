using Core;
using UnityEngine;
using Utilities;

namespace Robot.Builders
{
    [ExecuteInEditMode]
    public class BuildWheel : GeneratePart
    {
        [SerializeField] private WheelTypes wheelType;

        [SerializeField] private bool shouldCollide = true;
        
        private static GameObject[] _loadedWheels;

        private GameObject _wheel;

        private ColliderDisabler _colliderDisabler;

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

            _loadedWheels ??= Resources.LoadAll<GameObject>("Parts/Wheel");

            foreach (var loadedWheel in _loadedWheels)
            {
                if (loadedWheel.name == wheelType.ToString())
                {
                    _wheel = loadedWheel;
                }
            }

            if (!part || part != _wheel)
            {

                part = _wheel;

                partName = "wheel";

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