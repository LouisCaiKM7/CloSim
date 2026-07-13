using Core;
using UnityEngine;
using Utilities;

namespace Robot.Builders
{
    [ExecuteInEditMode]
    public class BuildMotor : GeneratePart
    {
        [SerializeField] private MotorTypes motorType;

        [SerializeField] private bool shouldCollide = true;
        
        private static GameObject[] _loadedMotors;

        private GameObject _motor;

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

            _loadedMotors ??= Resources.LoadAll<GameObject>("Parts/Motor");
            
            foreach (var loadedMotor in _loadedMotors)
            {
                if (loadedMotor.name == motorType.ToString())
                {
                    _motor = loadedMotor;
                }
            }

            if (!part || part != _motor)
            {

                part = _motor;

                partName = "motor";

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