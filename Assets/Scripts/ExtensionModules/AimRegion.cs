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

    public BoxCollider RegionBox { get; private set; }

    private void Awake()
    {
        RegionBox = GetComponent<BoxCollider>();
    }
}