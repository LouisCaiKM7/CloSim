using System;
using UnityEditor;
using UnityEngine;
using Util;

namespace Editor
{
    [CustomPropertyDrawer(typeof(InspectorDropdown))]
    public class DropdownEditor : PropertyDrawer 
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
        
            SerializedProperty selectedIndexProp = property.FindPropertyRelative("selectedIndex");
            SerializedProperty selectedNameProp = property.FindPropertyRelative("selectedName");
            SerializedProperty optionsProp = property.FindPropertyRelative("canBeSelected");
        
            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
        
            Rect dropdownRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        
            string[] options = GetStringArray(optionsProp);
        
            if (options != null && options.Length > 0)
            {
                string selectedName = selectedNameProp.stringValue;
                int currentSelectedIndex = selectedIndexProp.intValue;
                
                int newIndex = -1;
                newIndex = Array.IndexOf(options, selectedName);
                
                
                if (newIndex != -1)
                {
                    selectedIndexProp.intValue = newIndex;
                }
                else if (currentSelectedIndex >= options.Length || currentSelectedIndex < 0)
                {
                    selectedIndexProp.intValue = 0;
                }
                
                selectedIndexProp.intValue = EditorGUI.Popup(
                    dropdownRect, 
                    selectedIndexProp.intValue,
                    options
                );
                
                selectedNameProp.stringValue = options[selectedIndexProp.intValue];
            }
            else
            {
                EditorGUI.LabelField(dropdownRect, "No options defined.");
            }

            EditorGUI.EndProperty();
        }
    
        private string[] GetStringArray(SerializedProperty property)
        {
            string[] array = new string[property.arraySize];
            for (int i = 0; i < property.arraySize; i++)
            {
                array[i] = property.GetArrayElementAtIndex(i).stringValue;
            }
            return array;
        }
    
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
