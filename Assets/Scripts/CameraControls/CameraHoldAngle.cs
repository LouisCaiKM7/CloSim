using UnityEngine;

namespace CameraControls
{
    public class CameraHoldAngle : MonoBehaviour
    {
        private Quaternion _startingRotation;
        void Start()
        {
            _startingRotation = transform.rotation;
        }
        
        void Update()
        {
            transform.rotation = _startingRotation;
        }
    }
}
