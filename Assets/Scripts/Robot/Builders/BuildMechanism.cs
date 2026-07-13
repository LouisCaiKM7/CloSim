using Robot.Runtime;
using UnityEngine;

namespace Robot.Builders
{
    public class BuildMechanism : MonoBehaviour
    {
        public virtual JointController GetController()
        {
            return null;
        }
    }
}
