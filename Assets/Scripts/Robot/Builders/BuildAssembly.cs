using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Utilities;

namespace Robot.Builders
{
    [ExecuteInEditMode]
    public class BuildAssembly : GeneratePart
    {
        [SerializeField] private InspectorDropdown assemblies;

        [SerializeField] private bool scaleFix;
        
        private static GameObject[] _loadedAssembly;
        

        private GameObject _motor;

        private readonly List<string> _names = new List<string>();

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
            _loadedAssembly = Resources.LoadAll<GameObject>("Parts/Assembly");
            
            _names.Clear();
            
            foreach (var t in _loadedAssembly)
            {
                _names.Add(t.name);
            }
            
            assemblies.canBeSelected = _names.ToList();
            
            foreach (var assembly in _loadedAssembly)
            {
                if (assembly.name == assemblies.selectedName)
                {
                    _motor = assembly;
                }
            }

            Vector3 scale = Vector3.one;

            if (scaleFix)
            {
                scale.x = 100;
                scale.y = 100;
                scale.z = 100;
            }

            if (!part || part != _motor)
            {

                part = _motor;

                partName = "Assembly";

                loadedPartLocation = Vector3.zero;

                loadedPartRotation = Quaternion.Euler(Vector3.zero);

                loadedPartScale = scale;
            }
            else if (part)
            {
                loadedPartLocation = Vector3.zero;

                loadedPartRotation = Quaternion.Euler(Vector3.zero);

                loadedPartScale = scale;
            }
        }
    }
}