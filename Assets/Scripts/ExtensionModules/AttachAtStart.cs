using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;

public class AttachAtStart : MonoBehaviour
{
    private Joint _joint;

    private bool startup;
    // Start is called before the first frame update
    void Start()
    {
        _joint = gameObject.GetComponent<Joint>();
        startup = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (startup)
        {
            _joint.connectedBody = Utils.FindParentObjectComponent<Rigidbody>(gameObject);
        }
    }
}
