using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ColliderDisabler : MonoBehaviour
{
    [SerializeField] private Collider[] colliders;

    public void disableCollider()
    {
        foreach (var coll in colliders)
        {
            coll.enabled = false;
        }
    }

    public void enableCollider()
    {
        foreach (var coll in colliders)
        {
            coll.enabled = true;
        }
    }

    public void SetState(bool ShouldCollide)
    {
        if (ShouldCollide)
        {
            enableCollider();
        }
        else
        {
            disableCollider();
        }
    }
}
