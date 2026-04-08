using UnityEngine;
using Util;

public class LookAtRobot : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;

    [Tooltip("0 = Robot A, 1 = Robot B")]
    [SerializeField] private int robotSlot = 0;

    private LoadMatch loadMatch;
    private Transform target;

    void Start()
    {
        if (cameraTransform == null)
            cameraTransform = transform;

        loadMatch = FindFirstObjectByType<LoadMatch>();
        RefreshTarget();
    }

    void Update()
    {
        if (cameraTransform == null)
            cameraTransform = transform;

        if (loadMatch == null)
            loadMatch = FindFirstObjectByType<LoadMatch>();

        if (loadMatch == null)
            return;

        // Always refresh tracking state and reacquire target if needed
        if (target == null)
            RefreshTarget();

        bool lookTo = loadMatch.GetTrackingType() == TrackingType.TrackRobot;

        if (!lookTo)
            return;

        if (target == null)
        {
            var robot = loadMatch.GetRobotLoaded(robotSlot);
            target = robot != null ? robot.transform : null;
        }

        if (target != null)
            cameraTransform.LookAt(target);
    }

    private void RefreshTarget()
    {
        if (loadMatch == null) return;

        var robot = loadMatch.GetRobotLoaded(robotSlot);
        target = robot != null ? robot.transform : null;
    }

    public void SetRobotSlot(int slot)
    {
        robotSlot = slot;
        RefreshTarget();
    }
}