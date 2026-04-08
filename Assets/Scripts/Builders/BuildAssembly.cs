using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Util;

namespace Generators
{
    [ExecuteInEditMode]
    public class BuildAssembly : GeneratePart
    {
        [SerializeField] private InspectorDropdown assemblies;

        [SerializeField] private bool scaleFix = false;
        
        private static GameObject[] loadedAssembly;
        

        private GameObject _motor;

        private List<string> names = new List<string>();
        
        private static bool alreadyReset;

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
            alreadyReset = false;
            
            loadedAssembly = Resources.LoadAll<GameObject>("Parts/Assembly") as GameObject[];
            
            names.Clear();
            
            foreach (var t in loadedAssembly)
            {
                names.Add(t.name);
            }
            
            assemblies.canBeSelected = names.ToList();
            
            foreach (var assembly in loadedAssembly)
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

            if (!Part || Part != _motor)
            {

                Part = _motor;

                PartName = "Assembly";

                LoadedPartLocation = Vector3.zero;

                LoadedPartRotation = Quaternion.Euler(Vector3.zero);

                LoadedPartScale = scale;
            }
            else if (Part)
            {
                LoadedPartLocation = Vector3.zero;

                LoadedPartRotation = Quaternion.Euler(Vector3.zero);

                LoadedPartScale = scale;
            }
        }
    }
}