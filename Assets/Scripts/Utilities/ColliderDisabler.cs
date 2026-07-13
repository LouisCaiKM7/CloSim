using UnityEngine;

namespace Utilities
{
    public class ColliderDisabler : MonoBehaviour
    {
        [SerializeField] private Collider[] colliders;

        private void Awake()
        {
            CacheCollidersIfNeeded();
        }

        private void OnValidate()
        {
            CacheCollidersIfNeeded();
        }

        private void CacheCollidersIfNeeded()
        {
            if (colliders == null || colliders.Length == 0)
            {
                colliders = GetComponentsInChildren<Collider>(true);
            }
        }

        private void DisableCollider()
        {
            CacheCollidersIfNeeded();

            foreach (var coll in colliders)
            {
                if (coll != null)
                {
                    coll.enabled = false;
                }
            }
        }

        private void EnableCollider()
        {
            CacheCollidersIfNeeded();

            foreach (var coll in colliders)
            {
                if (coll != null)
                {
                    coll.enabled = true;
                }
            }
        }

        public void SetState(bool shouldCollide)
        {
            if (shouldCollide)
            {
                EnableCollider();
            }
            else
            {
                DisableCollider();
            }
        }
    }
}