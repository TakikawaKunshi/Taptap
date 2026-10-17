using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>卡片只显示当前类别对应的物种选项，避免同时配置植物和动物。</summary>
[CustomPropertyDrawer(typeof(SpeciesInfo))]
public class SpeciesInfoDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded)
            return height;
        foreach (string field in Fields(property))
            height += EditorGUIUtility.standardVerticalSpacing
                + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true);
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (string field in Fields(property))
            {
                SerializedProperty child = property.FindPropertyRelative(field);
                row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                row.height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(row, child, new GUIContent(Label(field)), true);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    private static IEnumerable<string> Fields(SerializedProperty property)
    {
        yield return "Icon";
        yield return "Name";
        yield return "Description";
        yield return "DailyCount";
        yield return "Kind";
        SpeciesKind kind = (SpeciesKind)property.FindPropertyRelative("Kind").intValue;
        if (kind == SpeciesKind.Plant)
            yield return "PlantSpecies";
        else if (kind == SpeciesKind.Animal)
            yield return "AnimalSpecies";
    }

    private static string Label(string field)
    {
        switch (field)
        {
            case "Icon": return "图标";
            case "Name": return "名称";
            case "Description": return "描述";
            case "DailyCount": return "每日数量";
            case "Kind": return "物种类别";
            case "PlantSpecies": return "植物物种";
            case "AnimalSpecies": return "动物物种";
            default: return field;
        }
    }
}
