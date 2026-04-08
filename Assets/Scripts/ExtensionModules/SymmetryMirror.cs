using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using System.Reflection;
#endif

[ExecuteInEditMode]
public class SymmetryTool : MonoBehaviour
{
#if UNITY_EDITOR
    [Header("Symmetry Settings")]
    [Tooltip("The plane to mirror across (in local space)")]
    public MirrorPlane plane = MirrorPlane.YZ;
    
    [Tooltip("Auto-update mirrored object when toMirror changes")]
    public bool autoUpdate = true;
    
    [Tooltip("Sync component values from toMirror to mirrored")]
    public bool syncComponents = true;
    
    public enum MirrorPlane
    {
        YZ, // Mirror across YZ plane (X axis normal)
        XZ, // Mirror across XZ plane (Y axis normal)
        XY  // Mirror across XY plane (Z axis normal)
    }
    
    private Transform toMirror;
    private Transform mirrored;
    
    private Dictionary<Transform, Transform> mirrorPairs = new Dictionary<Transform, Transform>();
    
    private int lastToMirrorChildCount;
    private Vector3 lastToMirrorPos;
    private Quaternion lastToMirrorRot;
    private Vector3 lastToMirrorScale;
    private MirrorPlane lastPlane;
    private int updateFrameCounter = 0;
    
    void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        SetupChildren();
    }
    
    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }
    
    void SetupChildren()
    {
        // Find or create toMirror
        Transform existingToMirror = transform.Find("toMirror");
        if (existingToMirror == null)
        {
            GameObject toMirrorObj = new GameObject("toMirror");
            toMirrorObj.transform.SetParent(transform);
            toMirrorObj.transform.localPosition = Vector3.zero;
            toMirrorObj.transform.localRotation = Quaternion.identity;
            toMirrorObj.transform.localScale = Vector3.one;
            toMirror = toMirrorObj.transform;
        }
        else
        {
            toMirror = existingToMirror;
        }
        
        // Find or create mirrored
        Transform existingMirrored = transform.Find("mirrored");
        if (existingMirrored == null)
        {
            GameObject mirroredObj = new GameObject("mirrored");
            mirroredObj.transform.SetParent(transform);
            mirrored = mirroredObj.transform;
        }
        else
        {
            mirrored = existingMirrored;
        }
        
        InitializeLastValues();
        UpdateMirror();
    }
    
    void InitializeLastValues()
    {
        if (toMirror != null)
        {
            lastToMirrorChildCount = toMirror.childCount;
            lastToMirrorPos = toMirror.localPosition;
            lastToMirrorRot = toMirror.localRotation;
            lastToMirrorScale = toMirror.localScale;
        }
        lastPlane = plane;
    }
    
    void OnEditorUpdate()
    {
        if (!autoUpdate || toMirror == null || mirrored == null) return;
        
        updateFrameCounter++;
        
        // Check transforms every frame
        bool needsUpdate = false;
        
        if (toMirror.childCount != lastToMirrorChildCount ||
            toMirror.localPosition != lastToMirrorPos ||
            toMirror.localRotation != lastToMirrorRot ||
            toMirror.localScale != lastToMirrorScale ||
            plane != lastPlane)
        {
            needsUpdate = true;
        }
        
        // Check all child transforms for changes
        if (!needsUpdate && HasChildTransformChanged())
        {
            needsUpdate = true;
        }
        
        if (needsUpdate)
        {
            UpdateMirror();
            InitializeLastValues();
        }
        // Sync components every 5 frames to reduce overhead
        else if (syncComponents && updateFrameCounter % 5 == 0)
        {
            SyncComponentValues();
        }
        // Also update mirror transforms every frame for smooth real-time updates
        else
        {
            UpdateMirrorTransformsOnly();
        }
    }
    
    bool HasChildTransformChanged()
    {
        return CheckTransformChangesRecursive(toMirror);
    }
    
    bool CheckTransformChangesRecursive(Transform parent)
    {
        if (parent.hasChanged)
        {
            parent.hasChanged = false;
            return true;
        }
        
        foreach (Transform child in parent)
        {
            if (CheckTransformChangesRecursive(child))
            {
                return true;
            }
        }
        
        return false;
    }
    
    void UpdateMirrorTransformsOnly()
    {
        if (mirrorPairs.Count == 0) return;
        
        Vector3 planeNormal = GetPlaneNormal();
        
        foreach (var pair in mirrorPairs)
        {
            if (pair.Key == null || pair.Value == null) continue;
            
            Transform source = pair.Key;
            Transform target = pair.Value;
            
            // Sync component existence between source and target
            SyncComponentExistence(source.gameObject, target.gameObject);
            
            // Mirror the local transform
            Vector3 mirroredPos = MirrorPosition(source.localPosition, planeNormal);
            target.localPosition = mirroredPos;
            
            Quaternion mirroredRot = MirrorRotation(source.localRotation, planeNormal);
            target.localRotation = mirroredRot;
            
            target.localScale = source.localScale;
        }
    }
    
    void SyncComponentExistence(GameObject source, GameObject target)
    {
        Component[] sourceComponents = source.GetComponents<Component>();
        Component[] targetComponents = target.GetComponents<Component>();
        
        // First clean missing scripts from both
        CleanMissingScripts(source);
        CleanMissingScripts(target);
        
        // Refresh after cleanup
        sourceComponents = source.GetComponents<Component>();
        targetComponents = target.GetComponents<Component>();
        
        // Build list of valid source component types
        HashSet<System.Type> sourceTypes = new HashSet<System.Type>();
        foreach (Component sourceComp in sourceComponents)
        {
            if (sourceComp == null) continue;
            if (sourceComp is Transform) continue;
            sourceTypes.Add(sourceComp.GetType());
        }
        
        // Remove components from target that aren't in source
        for (int i = targetComponents.Length - 1; i >= 0; i--)
        {
            Component targetComp = targetComponents[i];
            if (targetComp == null) continue;
            if (targetComp is Transform) continue;
            
            if (!sourceTypes.Contains(targetComp.GetType()))
            {
                DestroyImmediate(targetComp);
            }
        }
    }
    
    void CleanMissingScripts(GameObject obj)
    {
        Component[] components = obj.GetComponents<Component>();
        bool hasMissing = false;
        
        foreach (Component comp in components)
        {
            if (comp == null)
            {
                hasMissing = true;
                break;
            }
        }
        
        if (hasMissing)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(obj);
        }
    }
    
    [ContextMenu("Update Mirror")]
    public void UpdateMirror()
    {
        if (toMirror == null || mirrored == null)
        {
            SetupChildren();
            return;
        }
        
        // Clear existing mirrored children
        while (mirrored.childCount > 0)
        {
            DestroyImmediate(mirrored.GetChild(0).gameObject);
        }
        
        mirrorPairs.Clear();
        
        Vector3 planeNormal = GetPlaneNormal();
        
        // Mirror position and rotation of the container
        Vector3 mirroredPos = MirrorPosition(toMirror.localPosition, planeNormal);
        mirrored.localPosition = mirroredPos;
        
        Quaternion mirroredRot = MirrorRotation(toMirror.localRotation, planeNormal);
        mirrored.localRotation = mirroredRot;
        
        mirrored.localScale = toMirror.localScale;
        
        // Duplicate all children
        foreach (Transform child in toMirror)
        {
            DuplicateAndMirror(child, mirrored, planeNormal);
        }
    }
    
    void DuplicateAndMirror(Transform original, Transform parent, Vector3 planeNormal)
    {
        GameObject duplicate = Instantiate(original.gameObject, parent);
        duplicate.name = original.name;
        
        Transform dupTransform = duplicate.transform;
        
        // Store the pair for component syncing
        mirrorPairs[original] = dupTransform;
        
        // Mirror the local transform
        Vector3 mirroredPos = MirrorPosition(original.localPosition, planeNormal);
        dupTransform.localPosition = mirroredPos;
        
        Quaternion mirroredRot = MirrorRotation(original.localRotation, planeNormal);
        dupTransform.localRotation = mirroredRot;
        
        dupTransform.localScale = original.localScale;
        
        // Clear duplicated children
        while (dupTransform.childCount > 0)
        {
            DestroyImmediate(dupTransform.GetChild(0).gameObject);
        }
        
        // Recursively mirror children
        foreach (Transform child in original)
        {
            DuplicateAndMirror(child, dupTransform, planeNormal);
        }
    }
    
    void SyncComponentValues()
    {
        foreach (var pair in mirrorPairs)
        {
            if (pair.Key == null || pair.Value == null) continue;
            
            SyncGameObjectComponents(pair.Key.gameObject, pair.Value.gameObject);
        }
    }
    
    void SyncGameObjectComponents(GameObject source, GameObject target)
    {
        Component[] sourceComponents = source.GetComponents<Component>();
        Component[] targetComponents = target.GetComponents<Component>();
        
        // Clean up missing scripts from target first
        for (int i = targetComponents.Length - 1; i >= 0; i--)
        {
            if (targetComponents[i] == null)
            {
                // This is a missing script - use GameObjectUtility to remove it
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(target);
                targetComponents = target.GetComponents<Component>();
                break;
            }
        }
        
        // Build list of valid source component types (excluding nulls/missing)
        List<System.Type> sourceTypes = new List<System.Type>();
        foreach (Component sourceComp in sourceComponents)
        {
            if (sourceComp == null) continue;
            if (sourceComp is Transform) continue;
            sourceTypes.Add(sourceComp.GetType());
        }
        
        // Remove components from target that don't exist in source
        List<Component> toRemove = new List<Component>();
        foreach (Component targetComp in targetComponents)
        {
            if (targetComp == null)
            {
                // Missing script - will be cleaned up
                continue;
            }
            if (targetComp is Transform) continue;
            
            System.Type targetType = targetComp.GetType();
            if (!sourceTypes.Contains(targetType))
            {
                toRemove.Add(targetComp);
            }
        }
        
        // Remove components that don't exist in source
        foreach (Component comp in toRemove)
        {
            DestroyImmediate(comp);
        }
        
        // Refresh target components after removal
        targetComponents = target.GetComponents<Component>();
        
        // Sync existing components
        foreach (Component sourceComp in sourceComponents)
        {
            if (sourceComp == null) continue;
            if (sourceComp is Transform) continue;
            
            System.Type sourceType = sourceComp.GetType();
            Component matchingTarget = null;
            
            // Find matching component in target
            foreach (Component targetComp in targetComponents)
            {
                if (targetComp != null && targetComp.GetType() == sourceType)
                {
                    matchingTarget = targetComp;
                    break;
                }
            }
            
            if (matchingTarget != null)
            {
                CopyComponentValues(sourceComp, matchingTarget);
            }
        }
    }
    
    void CopyComponentValues(Component source, Component target)
    {
        System.Type type = source.GetType();
        
        // Copy all public fields
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (FieldInfo field in fields)
        {
            if (field.IsNotSerialized) continue;
            
            try
            {
                object value = field.GetValue(source);
                field.SetValue(target, value);
            }
            catch { }
        }
        
        // Copy all serialized properties
        SerializedObject sourceObj = new SerializedObject(source);
        SerializedObject targetObj = new SerializedObject(target);
        
        SerializedProperty prop = sourceObj.GetIterator();
        while (prop.NextVisible(true))
        {
            if (prop.name == "m_Script") continue;
            
            SerializedProperty targetProp = targetObj.FindProperty(prop.name);
            if (targetProp != null && targetProp.propertyType == prop.propertyType)
            {
                try
                {
                    targetObj.CopyFromSerializedProperty(prop);
                }
                catch { }
            }
        }
        
        targetObj.ApplyModifiedPropertiesWithoutUndo();
    }
    
    Vector3 GetPlaneNormal()
    {
        switch (plane)
        {
            case MirrorPlane.YZ: return Vector3.right;
            case MirrorPlane.XZ: return Vector3.up;
            case MirrorPlane.XY: return Vector3.forward;
            default: return Vector3.right;
        }
    }
    
    Vector3 MirrorPosition(Vector3 pos, Vector3 normal)
    {
        return pos - 2f * Vector3.Dot(pos, normal) * normal;
    }
    
    Quaternion MirrorRotation(Quaternion rot, Vector3 normal)
    {
        Matrix4x4 rotMatrix = Matrix4x4.Rotate(rot);
        Matrix4x4 reflectionMatrix = CreateReflectionMatrix(normal);
        Matrix4x4 mirroredMatrix = reflectionMatrix * rotMatrix;
        return QuaternionFromMatrix(mirroredMatrix);
    }
    
    Matrix4x4 CreateReflectionMatrix(Vector3 normal)
    {
        Matrix4x4 m = Matrix4x4.identity;
        
        m.m00 = 1f - 2f * normal.x * normal.x;
        m.m01 = -2f * normal.x * normal.y;
        m.m02 = -2f * normal.x * normal.z;
        
        m.m10 = -2f * normal.y * normal.x;
        m.m11 = 1f - 2f * normal.y * normal.y;
        m.m12 = -2f * normal.y * normal.z;
        
        m.m20 = -2f * normal.z * normal.x;
        m.m21 = -2f * normal.z * normal.y;
        m.m22 = 1f - 2f * normal.z * normal.z;
        
        return m;
    }
    
    Quaternion QuaternionFromMatrix(Matrix4x4 m)
    {
        Vector3 forward = new Vector3(m.m02, m.m12, m.m22);
        Vector3 up = new Vector3(m.m01, m.m11, m.m21);
        
        if (forward.sqrMagnitude < 0.001f || up.sqrMagnitude < 0.001f)
            return Quaternion.identity;
            
        return Quaternion.LookRotation(forward, up);
    }
    
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(SymmetryTool))]
public class SymmetryToolEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        SymmetryTool tool = (SymmetryTool)target;
        
        EditorGUILayout.Space();
        if (GUILayout.Button("Force Update Mirror", GUILayout.Height(30)))
        {
            tool.UpdateMirror();
        }
        
        EditorGUILayout.HelpBox(
            "Add child objects to 'toMirror'. The 'mirrored' child will automatically duplicate and reflect them.\n\n" +
            "Sync Components: When enabled, component values (scripts, materials, etc.) are continuously copied from toMirror to mirrored.\n\n" +
            "YZ plane = mirror across X axis (left/right)\n" +
            "XZ plane = mirror across Y axis (up/down)\n" +
            "XY plane = mirror across Z axis (forward/back)",
            MessageType.Info);
    }
}
#endif