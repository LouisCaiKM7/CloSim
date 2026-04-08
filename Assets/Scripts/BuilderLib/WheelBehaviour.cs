using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WheelBehaviour : MonoBehaviour
{
    [HideInInspector] public float wheelDiameter;
    
    [HideInInspector] public List<Vector3> collisionPoints = new List<Vector3>();
    [HideInInspector] public List<Vector3> collisionNormals = new List<Vector3>();
    
    private List<Vector3> directions = new List<Vector3>();
    

    // Start is called before the first frame update
    private void Start()
    {
        directions.Add(-transform.up);                                          
        directions.Add((-transform.up * 2 + transform.forward).normalized);     
        directions.Add((-transform.up * 2 - transform.forward).normalized);     
        directions.Add((-transform.up + transform.forward).normalized);         
        directions.Add((-transform.up - transform.forward).normalized);         
        directions.Add((-transform.up + transform.forward * 2).normalized);    
        directions.Add((-transform.up - transform.forward * 2).normalized);     
        directions.Add(transform.forward);
        directions.Add(-transform.forward);
    }

    // Fixed Update is called every Physics Tick
    private void FixedUpdate()
    {
        collisionPoints.Clear();
        collisionNormals.Clear();

        Vector3 axle = transform.right;

        for (int i = 0; i < directions.Count; i++)
        {
            Vector3 worldDir = transform.TransformDirection(directions[i]);

            if (Physics.Raycast(transform.position, worldDir, out RaycastHit hit, wheelDiameter/2))
            {
                collisionPoints.Add(hit.point);
                Vector3 tangentNormal = Vector3.Cross(axle, -worldDir);
                
                collisionNormals.Add(tangentNormal.normalized);
            }
        }
    }
}
