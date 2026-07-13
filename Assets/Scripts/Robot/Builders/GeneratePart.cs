using UnityEngine;
using UnityEngine.Serialization;
using Utilities;

namespace Robot.Builders
{
    [ExecuteAlways]
    public class GeneratePart : MonoBehaviour
    {
        private string _partName;
        [FormerlySerializedAs("ObjectSpawned")] [HideInInspector] public bool objectSpawned;
    
        /// <summary>
        /// The name of the Object to use
        /// </summary>
        [FormerlySerializedAs("PartName")] [HideInInspector] public string partName;

        /// <summary>
        /// The part(GameObject) to generate
        /// </summary>
        [FormerlySerializedAs("Part")] [HideInInspector] public GameObject part;
        /// <summary>
        /// The transform relative to the scripts object to put the part
        /// </summary>
        [FormerlySerializedAs("LoadedPartLocation")] [HideInInspector] public Vector3 loadedPartLocation;
        /// <summary>
        /// The rotation relative to the scripts object to put the part
        /// </summary>
        [FormerlySerializedAs("LoadedPartRotation")] [HideInInspector] public Quaternion loadedPartRotation;
        /// <summary>
        /// The Scale for the object to use.
        /// </summary>
        [FormerlySerializedAs("LoadedPartScale")] [HideInInspector] public Vector3 loadedPartScale;
    
        private GameObject _currentPart;
    
        private GameObject _loadedPart;
    

        // Start is called before the first frame update
        void OnEnable()
        {
            Startup();
        }

        protected GameObject GetLoadedPart()
        {
            return _loadedPart;
        }
    
        void OnDisable()
        {
            CancelInvoke(nameof(Run));
        }

        private void OnDestroy()
        {
            DestroyImmediate(_loadedPart);
        }

        protected void Run()
        {
            if (partName != null && part != null)
            {
                _partName = partName;
                objectSpawned = _loadedPart;
                if (_loadedPart)
                {
                    if (_loadedPart.name != partName || _currentPart != part)
                    {
                        DestroyImmediate(_loadedPart);
                    }
                }

                if (!_loadedPart && part)
                {
                    _currentPart = part;
                    _loadedPart = Instantiate(part, loadedPartLocation, loadedPartRotation, transform);
                    _loadedPart.name = partName;
                }

                _loadedPart.transform.localPosition = loadedPartLocation;
                _loadedPart.transform.localRotation = loadedPartRotation;

                var scaleAdjustedScale = new Vector3(loadedPartScale.x / transform.localScale.x,
                    loadedPartScale.y / transform.localScale.y, loadedPartScale.z / transform.localScale.z);
                _loadedPart.transform.localScale = scaleAdjustedScale;
            }

            objectSpawned = _loadedPart;
        }

        protected void Startup()
        {
            if (_partName != null)
            {
                partName = _partName;
            }

            _loadedPart = Utils.FindChild(partName, gameObject);
            _currentPart = part;
        
            InvokeRepeating(nameof(Run), 0f, 0.2f); //does the same thing as fixed update but doesn't require it be selected in editor
        }
    }
}
