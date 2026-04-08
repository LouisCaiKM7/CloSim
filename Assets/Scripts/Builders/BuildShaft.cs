using System.Diagnostics;
using MyBox;
using UnityEngine;
using Util;

namespace Generators
{
    [ExecuteInEditMode]
    public class BuildShaft : GeneratePart
    {
        [SerializeField] private ShaftType shaftType;

        [SerializeField] private bool shouldCollide = true;

        [SerializeField] private Units units;

        [SerializeField] private float shaftLength = 10;

        [ConditionalField(true,  nameof(isDead))]
        [SerializeField] private float shaftDiameter = 2;
        
        private bool isDead() => shaftType == ShaftType.Dead;
        
        private static GameObject[] loadedShafts;

        private GameObject _shaft;

        private ColliderDisabler colliderDisabler;

        private float _scaleFactor;
        
        private Vector3 position;
        private Vector3 rotation;
        
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
            BuildObjects();
        }

        public void setLength(float length)
        {
            shaftLength = length;
            BuildObjects();
        }

        public void setDiameter(float diameter)
        {
            this.shaftDiameter = diameter;
            BuildObjects();
        }

        public void SetShaft(ShaftType type)
        {
            shaftType = type;
            BuildObjects();
        }

        public void SetPosition(Vector3 position)
        {
            this.position = position;
            BuildObjects();
        }

        public void SetRotation(Vector3 rotation)
        {
            this.rotation = rotation;
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
            
            if (colliderDisabler)
            {
                colliderDisabler.SetState(shouldCollide);
            }
            else if (getLoadedPart())
            {
                colliderDisabler = Utils.TryGetComponentOnChild<ColliderDisabler>(getLoadedPart());
            }

            loadedShafts ??= Resources.LoadAll<GameObject>("Parts/Shafts") as GameObject[];

            foreach (var loadedShaft in loadedShafts)
            {
                if (loadedShaft.name == shaftType.ToString())
                {
                    _shaft = loadedShaft;
                }
            }

            Vector3 partScale = new Vector3(shaftLength * _scaleFactor, 1, 1);

            if (isDead())
            {
                partScale.z = shaftDiameter * _scaleFactor;
                partScale.y = shaftDiameter * _scaleFactor;
            }

            if (!Part || Part != _shaft)
            {

                Part = _shaft;

                PartName = "shaft";

                LoadedPartLocation = position;

                LoadedPartRotation = Quaternion.Euler(rotation);

                LoadedPartScale = partScale;
            }
            else if (Part)
            {
                LoadedPartLocation = position;

                LoadedPartRotation = Quaternion.Euler(rotation);
                
                LoadedPartScale = partScale;
            }
        }
    }
}
