using Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace CameraControls
{
    public class LookAtRobot : MonoBehaviour
    {
        [SerializeField, FormerlySerializedAs("camera")]
        private Transform cameraTransform;

        [Tooltip("0 = Robot A, 1 = Robot B")]
        [SerializeField] private int robotSlot;

        private LoadMatch _loadMatch;
        private Transform _target;

        private void Awake()
        {
            ResolveCameraTransform();
        }

        private void Start()
        {
            if (_loadMatch == null)
                _loadMatch = FindFirstObjectByType<LoadMatch>();

            RefreshTarget();
        }

        private void LateUpdate()
        {
            if (_loadMatch == null)
            {
                _loadMatch = FindFirstObjectByType<LoadMatch>();
                if (_loadMatch == null)
                    return;
            }

            if (_loadMatch.GetTrackingType() != TrackingType.TrackRobot)
                return;

            if (_target == null)
                RefreshTarget();

            if (cameraTransform == null)
                ResolveCameraTransform();

            if (_target != null && cameraTransform != null)
                cameraTransform.LookAt(_target);
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
            if (_loadMatch == null)
            {
                _target = null;
                return;
            }

            GameObject robot = _loadMatch.GetRobotLoaded(robotSlot);
            _target = robot != null ? robot.transform : null;
        }

        public void Initialize(LoadMatch match, int slot)
        {
            _loadMatch = match;
            robotSlot = slot;
            RefreshTarget();
        }

        public void SetRobotSlot(int slot)
        {
            robotSlot = slot;
            RefreshTarget();
        }
    }
}