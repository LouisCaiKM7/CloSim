using System.Collections.Generic;
using UnityEngine;

public class WheelBehaviour : MonoBehaviour
{
    [HideInInspector] public float wheelDiameter;

    [HideInInspector] public List<Vector3> collisionPoints  = new List<Vector3>();
    [HideInInspector] public List<Vector3> collisionNormals = new List<Vector3>();

    [SerializeField] private float wheelWidth = 0.2f;

    private void FixedUpdate()
    {
        collisionPoints.Clear();
        collisionNormals.Clear();

        Vector3 axle       = transform.right;
        float   radius     = wheelDiameter / 2f;

        Vector3 point1 = transform.position + axle * (wheelWidth / 2f);
        Vector3 point2 = transform.position - axle * (wheelWidth / 2f);

        if (Physics.CapsuleCast(point1, point2, radius * 0.3f,
                -transform.up, out RaycastHit hit, radius * 1.1f))
        {
            collisionPoints.Add(hit.point);

            Vector3 tangentNormal = Vector3.Cross(axle, transform.up);
            collisionNormals.Add(tangentNormal.normalized);
        }
    }
}