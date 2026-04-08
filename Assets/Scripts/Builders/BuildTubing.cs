using UnityEngine;
using Util;

namespace Generators
{
    [ExecuteInEditMode]
    public class BuildTubing : GeneratePart
    {
        [SerializeField] private TubeType tubeType;
        
        [SerializeField] private Units units;
        
        [SerializeField] private float length = 10;

        private static GameObject[] loadedTubes;

        private GameObject _tube;

        private float _factor;
        
        private Vector3 position;
        private Vector3 rotation;

        // Start is called before the first frame update
        void Start()
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
            buildObjects();
        }

        public void setTube(TubeType tubeType)
        {
            this.tubeType = tubeType;
            buildObjects();
        }

        public void setLength(float length)
        {
            this.length = length;
            buildObjects();
        }

        public void SetPosition(Vector3 position)
        {
            this.position = position;
            buildObjects();
        }

        public void SetRotation(Vector3 rotation)
        {
            this.rotation = rotation;
            buildObjects();
        }
        

        // Update is called once per frame
        void Update()
        {
            buildObjects();
        }

        private void buildObjects()
        {
            _factor = units switch
            {
                Units.Inch => 0.0254f,
                Units.Meter => 1,
                Units.Centimeter => 0.01f,
                Units.Millimeter => 0.001f,
                _ => 0.0254f
            };

            loadedTubes ??= Resources.LoadAll<GameObject>("Parts/Tubing") as GameObject[];

            foreach (var loadedTube in loadedTubes)
            {
                if (loadedTube.name == tubeType.ToString())
                {
                    _tube = loadedTube;
                }
            }

            if (!Part)
            {

                Part = _tube;

                PartName = "tube";

                LoadedPartLocation = Vector3.zero;

                LoadedPartRotation = Quaternion.Euler(Vector3.zero);

                LoadedPartScale = Vector3.one;
            }
            else if (Part != _tube)
            {
                Part = _tube;

                PartName = "tube";

                LoadedPartLocation = position;

                LoadedPartRotation = Quaternion.Euler(rotation);

                LoadedPartScale = new Vector3(1,1,length * _factor);
            }
            else if (Part != null)
            {
                LoadedPartLocation = position;

                LoadedPartRotation = Quaternion.Euler(rotation);

                LoadedPartScale = new Vector3(1,1,length * _factor);
            }
        }
    }
}
