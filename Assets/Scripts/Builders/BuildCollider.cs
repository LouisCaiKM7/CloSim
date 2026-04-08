using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;

[ExecuteInEditMode]
public class BuildCollider : MonoBehaviour
{
    [SerializeField] Vector3 ColliderSize;

    [SerializeField] private Units units;
    
    private BoxCollider box;
    
    public void setUnits(Units units)
    {
        this.units = units;
        buildObjects();
    }

    public void setSize(Vector3 size)
    {
        ColliderSize = size;
        buildObjects();
    }

    private float _scale;
    // Start is called before the first frame update
    private float GetUnitScale()
    {
        return units switch
        {
            Units.Inch => 0.0254f,
            Units.Meter => 1.0f,
            Units.Centimeter => 0.01f,
            Units.Millimeter => 0.001f,
            _ => 0.0254f
        };
    }
    
    private void Awake()
    {
        box = GetComponentInChildren<BoxCollider>();
    }

    private void Update()
    {
        buildObjects();
    }

    private void buildObjects()
    {
        if (Application.isPlaying) return;

        if (!box)
        {
            var colliderObject = Utils.TryGetAddChild("Collider", gameObject); 
            box = Utils.TryGetAddComponent<BoxCollider>(colliderObject);
        }

        var scale = GetUnitScale();
        box.size = ColliderSize * scale;
        
        box.transform.localPosition = Vector3.zero;
        box.transform.localRotation = Quaternion.identity;
    }
}
