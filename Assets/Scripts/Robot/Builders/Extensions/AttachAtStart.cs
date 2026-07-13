using UnityEngine;
using Utilities;

namespace Robot.Builders.Extensions
{
    public class AttachAtStart : MonoBehaviour
    {
        private Joint _joint;

        private bool _startup;
        // Start is called before the first frame update
        void Start()
        {
            _joint = gameObject.GetComponent<Joint>();
            _startup = true;
        }

        // Update is called once per frame
        void Update()
        {
            if (_startup)
            {
                _joint.connectedBody = Utils.FindParentObjectComponent<Rigidbody>(gameObject);
            }
        }
    }
}
