using Core;
using UnityEngine;

namespace Robot.Builders
{
    [ExecuteInEditMode]
    public class BuildTubing : GeneratePart
    {
        [SerializeField] private TubeType tubeType;
        
        [SerializeField] private Units units;
        
        [SerializeField] private float length = 10;

        private static GameObject[] _loadedTubes;

        private GameObject _tube;

        private float _factor;
        
        private Vector3 _position;
        private Vector3 _rotation;

        // Start is called before the first frame update
        void Start()
        {
            Startup();
        }

        private void OnEnable()
        {
            Startup();
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
            _factor = units switch
            {
                Units.Inch => 0.0254f,
                Units.Meter => 1,
                Units.Centimeter => 0.01f,
                Units.Millimeter => 0.001f,
                _ => 0.0254f
            };

            _loadedTubes ??= Resources.LoadAll<GameObject>("Parts/Tubing");

            foreach (var loadedTube in _loadedTubes)
            {
                if (loadedTube.name == tubeType.ToString())
                {
                    _tube = loadedTube;
                }
            }

            if (!part)
            {

                part = _tube;

                partName = "tube";

                loadedPartLocation = Vector3.zero;

                loadedPartRotation = Quaternion.Euler(Vector3.zero);

                loadedPartScale = Vector3.one;
            }
            else if (part != _tube)
            {
                part = _tube;

                partName = "tube";

                loadedPartLocation = _position;

                loadedPartRotation = Quaternion.Euler(_rotation);

                loadedPartScale = new Vector3(1,1,length * _factor);
            }
            else if (part != null)
            {
                loadedPartLocation = _position;

                loadedPartRotation = Quaternion.Euler(_rotation);

                loadedPartScale = new Vector3(1,1,length * _factor);
            }
        }
    }
}