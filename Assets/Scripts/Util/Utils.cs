using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Vector3 = System.Numerics.Vector3;

namespace Util
{
    public class Utils:MonoBehaviour
    {
        // Start is called before the first frame update
        public Utils()
        {
        
        }
        
        public static Transform[] GetAllChildren(Transform transform)
        {
            List<Transform> children = new List<Transform>();
            
            foreach (Transform child in transform)
            {
                children.Add(child);
            }

            return children.ToArray();
        }

        public static T TryGetComponentOnChild<T>(GameObject parent) where T : Component
        {
            var children = GetAllChildren(parent.transform);

            return children.Select(child => child.GetComponent<T>()).FirstOrDefault(component => component);
        }


        /// <summary>
        /// Finds a child with a given name by only searching the children instead of everything.
        /// </summary>
        /// <param name="childName"></param>
        /// <param name="parent"></param>
        /// <returns></returns>
        public static GameObject FindChild(string childName, GameObject parent)
        {
            if (parent == null) return null;
            
            for (int i = 0; i < parent.transform.childCount; i++)
            {
                if (parent.transform.GetChild(i).name == childName)
                {
                    return parent.transform.GetChild(i).gameObject;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// attempts to get the component. if it is not on the object it adds it
        /// </summary>
        /// <param name="parent"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T TryGetAddComponent<T>(GameObject parent) where T: Component
        {
            // 1. Try to get the component.
            if (parent.TryGetComponent(typeof(T), out var existingComponent))
            {
                // Found it, return the existing component.
                return (T)existingComponent;
            }

            // 2. Not found, add the component.
            Component newComponent = parent.AddComponent(typeof(T));

            // 3. Return the newly created component.
            return (T)newComponent;
        }
        
        /// <summary>
        /// attempts to get the component. if it is not on the object it adds it
        /// </summary>
        /// <param name="parent"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static GameObject TryGetAddChild(string childName,GameObject parent, out bool existed)
        {
            var child = FindChild(childName, parent);

            if (child)
            {
                existed = true;
                return child;
            }
            
            child = new GameObject(childName);
            child.transform.SetParent(parent.transform, false);
            existed = false;
            return child;
        }

        public static GameObject TryGetAddChild(string childName, GameObject parent)
        {
            return TryGetAddChild(childName, parent, out bool existed);
        }

        /// <summary>
        /// Finds the first Parent objcet which contains a rigid body
        /// </summary>
        /// <param name="child"></param>
        /// <returns></returns>
        public static GameObject FindParentRB(GameObject child)
        {
            var t = child.transform.parent;
            while (t.GetComponent<Rigidbody>() == null)
            {
                if (t.parent == null)
                {
                    return null;
                }
                t = t.parent.transform;
            }
            
            return t.gameObject;
        }

        /// <summary>
        /// Finds the first Parent objcet which contains a object T
        /// </summary>
        /// <param name="value"></param>
        /// <param name="child"></param>
        /// <returns></returns>
        public static T FindParentObjectComponentActual<T>(GameObject child) where T : Component
        {
            if (child == null)
            {
                return null;
            }

            Transform currentTransform = child.transform.parent;

            while (currentTransform != null)
            {
                T component = currentTransform.GetComponent<T>();
                if (component)
                {
                    return component;
                }
                currentTransform = currentTransform.parent;
            }
            return null;
        }
        
        private static readonly Dictionary<GameObject, Component> ParentComponentCache = new Dictionary<GameObject, Component>();

        public static void resetParentCache()
        {
            ParentComponentCache.Clear();
        }
        
        public static T FindParentObjectComponent<T>(GameObject child) where T : Component
        {
            // 1. Check the cache first
            if (ParentComponentCache.TryGetValue(child, out Component cachedComponent) && cachedComponent is T resultT)
            {
                return resultT;
            }

            // 2. Perform the expensive traversal
            T foundComponent = FindParentObjectComponentActual<T>(child);

            // 3. Cache the result (even if null)
            if (foundComponent != null)
            {
                ParentComponentCache[child] = foundComponent;
            }
            else
            {
                // Cache a marker for "not found" to prevent future null searches
                // A common technique is to cache a known dummy component or null, 
                // but for simplicity here we'll just skip caching null for now
                // as it would require more complex cache management.
            }

            return foundComponent;
        }
        
        /// <summary>
        /// finds the first parent object with a player input object.
        /// </summary>
        /// <param name="child"></param>
        /// <returns></returns>
        public static GameObject FindParentPlayerInput(GameObject child)
        {
            var t = child.transform;
            while (t.GetComponent<PlayerInput>() == null)
            {
                t = t.parent.transform;
            }
            
            return t.gameObject;
        }
        
        /// <summary>
        /// Flips the angle 180
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static float FlipAngle(float angle)
        {
            angle = -angle;
            angle = Mathf.Repeat(angle, 360); 
            if (angle < 0)
            {
                angle += 360;
            }
            return angle;
        }
        
        /// <summary>
        /// Wraps the angle into -180 180 from 360
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static float WrapAngle180(float angle)
        {
            angle = Mathf.Repeat(angle, 360);
            if (angle > 180)
            {
                angle -= 360; // Convert to -180 to 180 range
            }
            return angle;
        }

        /// <summary>
        /// wraps angle to 0 to 360
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static float WrapAngle360(float angle)
        {
            angle = Mathf.Repeat(angle, 360);
            return angle;
        }
        
        /// <summary>
        /// returns the difference between two angles
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static float AngleDifference(float a, float b) {
            return (a - b + 540) % 360 - 180;
        }
        
        public static List<GameObject> FindGameObjectsOnLayer(string layerName)
        {
            // 1. Get the integer ID for the layer name.
            int layerID = LayerMask.NameToLayer(layerName);

            // Check if the layer name is valid (returns -1 if invalid).
            if (layerID == -1)
            {
                Debug.LogWarning($"Layer '{layerName}' not found in Unity's layer settings.");
                return new List<GameObject>();
            }

            // 2. Find all active GameObjects in the scene.
            // NOTE: This can be a slow operation and shouldn't be called every frame (e.g., in Update).
            GameObject[] allGameObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // 3. Filter the array to only include objects on the target layer.
            List<GameObject> objectsOnLayer = allGameObjects
                .Where(go => go.layer == layerID)
                .ToList();

            return objectsOnLayer;
        }
    }
}