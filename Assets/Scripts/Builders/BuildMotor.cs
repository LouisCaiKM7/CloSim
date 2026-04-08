using System;
using UnityEngine;
using Util;

namespace Generators
{
    [ExecuteInEditMode]
    public class BuildMotor : GeneratePart
    {
        [SerializeField] private MotorTypes motorType;

        [SerializeField] private bool shouldCollide = true;
        
        private static GameObject[] loadedMotors;

        private GameObject _motor;

        private ColliderDisabler colliderDisabler;

        public void setMotor(MotorTypes motor)
        {
            this.motorType = motor;
            BuildPart();
        }
        public void setCollide(bool enabled)
        {
            shouldCollide = enabled;
            BuildPart();
        }

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
            if (colliderDisabler)
            {
                colliderDisabler.SetState(shouldCollide);
            }
            else if (getLoadedPart())
            {
                colliderDisabler = Utils.TryGetComponentOnChild<ColliderDisabler>(getLoadedPart());
            }

            loadedMotors ??= Resources.LoadAll<GameObject>("Parts/Motor") as GameObject[];
            
            foreach (var loadedMotor in loadedMotors)
            {
                if (loadedMotor.name == motorType.ToString())
                {
                    _motor = loadedMotor;
                }
            }

            if (!Part || Part != _motor)
            {

                Part = _motor;

                PartName = "motor";

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
}
