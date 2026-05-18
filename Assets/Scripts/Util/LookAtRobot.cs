using UnityEngine;
using UnityEngine.Serialization;
using Util;

public class LookAtRobot : MonoBehaviour
{
    [SerializeField, FormerlySerializedAs("camera")]
    private Transform cameraTransform;

    [Tooltip("0 = Robot A, 1 = Robot B")]
    [SerializeField] private int robotSlot = 0;

    private LoadMatch loadMatch;
    private Transform target;

    private void Awake()
    {
        ResolveCameraTransform();
    }

    private void Start()
    {
        if (loadMatch == null)
            loadMatch = FindFirstObjectByType<LoadMatch>();

        RefreshTarget();
    }

    private void LateUpdate()
    {
        if (loadMatch == null)
        {
            loadMatch = FindFirstObjectByType<LoadMatch>();
            if (loadMatch == null)
                return;
        }

        if (loadMatch.GetTrackingType() != TrackingType.TrackRobot)
            return;

        if (target == null)
            RefreshTarget();

        if (cameraTransform == null)
            ResolveCameraTransform();

        if (target != null && cameraTransform != null)
            cameraTransform.LookAt(target);
    }

    private void ResolveCameraTransform()
    {
        if (cameraTransform != null)
            return;

        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            cameraTransform = cam.transform;
            return;
        }

        cameraTransform = transform;
    }

    private void RefreshTarget()
    {
        if (loadMatch == null)
        {
            target = null;
            return;
        }

        GameObject robot = loadMatch.GetRobotLoaded(robotSlot);
        target = robot != null ? robot.transform : null;
    }

    public void Initialize(LoadMatch match, int slot)
    {
        loadMatch = match;
        robotSlot = slot;
        RefreshTarget();
    }

    public void SetRobotSlot(int slot)
    {
        robotSlot = slot;
        RefreshTarget();
    }
}