using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraHoldAngle : MonoBehaviour
{
    private Quaternion startingRotation;
    // Start is called before the first frame update
    void Start()
    {
        startingRotation = transform.rotation;
    }

    // Update is called once per frame
    void Update()
    {
        transform.rotation = startingRotation;
    }
}
