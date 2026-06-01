using UnityEngine;

namespace BuilderLib
{
    public class BuildMechanism : MonoBehaviour
    {
        public virtual JointController GetController()
        {
            return null;
        }
    }
}
