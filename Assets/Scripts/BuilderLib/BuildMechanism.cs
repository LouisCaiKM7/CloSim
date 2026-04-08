using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BuildMechanism : MonoBehaviour
{
    public virtual JointController GetController()
    {
        return null;
    }
}
