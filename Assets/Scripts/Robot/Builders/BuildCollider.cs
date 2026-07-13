using Core;
using UnityEngine;
using UnityEngine.Serialization;
using Utilities;

namespace Robot.Builders
{
    [ExecuteInEditMode]
    public class BuildCollider : MonoBehaviour
    {
        [FormerlySerializedAs("ColliderSize")] [SerializeField] private Vector3 colliderSize;

        [SerializeField] private Units units;
    
        private BoxCollider _box;

        private float _scale;
        // Start is called before the first frame update
        private float GetUnitScale()
        {
            return units switch
            {
                Units.Inch => 0.0254f,
                Units.Meter => 1.0f,
                Units.Centimeter => 0.01f,
                Units.Millimeter => 0.001f,
                _ => 0.0254f
            };
        }
    
        private void Awake()
        {
            _box = GetComponentInChildren<BoxCollider>();
        }

        private void Update()
        {
            BuildObjects();
        }

        private void BuildObjects()
        {
            if (Application.isPlaying) return;

            if (!_box)
            {
                var colliderObject = Utils.TryGetAddChild("Collider", gameObject); 
                _box = Utils.TryGetAddComponent<BoxCollider>(colliderObject);
            }

            var scale = GetUnitScale();
            _box.size = colliderSize * scale;
        
            _box.transform.localPosition = Vector3.zero;
            _box.transform.localRotation = Quaternion.identity;
        }
    }
}
