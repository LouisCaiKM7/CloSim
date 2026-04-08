using UnityEngine;
using Util;

namespace Generators
{
    [ExecuteInEditMode]
    public class BuildWheel : GeneratePart
    {
        [SerializeField] private WheelTypes wheelType;

        [SerializeField] private bool shouldCollide = true;
        
        private static GameObject[] loadedWheels;

        private GameObject _wheel;

        private ColliderDisabler colliderDisabler;

        public void setWheelType(WheelTypes wheelType)
        {
            this.wheelType = wheelType;
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

            loadedWheels ??= Resources.LoadAll<GameObject>("Parts/Wheel") as GameObject[];

            foreach (var loadedWheel in loadedWheels)
            {
                if (loadedWheel.name == wheelType.ToString())
                {
                    _wheel = loadedWheel;
                }
            }

            if (!Part || Part != _wheel)
            {

                Part = _wheel;

                PartName = "wheel";

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
