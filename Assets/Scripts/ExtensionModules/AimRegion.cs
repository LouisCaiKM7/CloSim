using UnityEngine;

public enum AimRegionId
{
    BlueAlliance,
    RedAlliance,
    Neutral
}

[RequireComponent(typeof(BoxCollider))]
public class AimRegion : MonoBehaviour
{
    [SerializeField] private AimRegionId regionId;
    public AimRegionId RegionId => regionId;

    private BoxCollider _regionBox;
    public BoxCollider RegionBox
    {
        get
        {
            if (_regionBox == null)
                _regionBox = GetComponent<BoxCollider>();

            return _regionBox;
        }
    }

    private void Awake()
    {
        _regionBox = GetComponent<BoxCollider>();
    }

    private void Reset()
    {
        _regionBox = GetComponent<BoxCollider>();
        _regionBox.isTrigger = true;
    }
}