using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using ShaderPropertyType = UnityEditor.ShaderUtil.ShaderPropertyType;
using System.Text;

public static class MaterialUtility {

    [MenuItem("Tools/JLChnToZ/Clean Unused Material Properties")]
    static void CleanUnusedProperties() {
        var selection = Selection.GetFiltered<Material>(SelectionMode.Assets);
        if (selection.Length == 0) return;
        var stats = new RemovedPropertyStatistics();
        foreach (var m in selection) stats += CleanUnusedProperties(m);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Clean Unused Properties", $"Removed {stats} from {selection.Length} materials.", "OK");
    }

    [MenuItem("CONTEXT/Material/Clean Unused Properties")]
    static void CleanUnusedProperties(MenuCommand command) {
        var m = command.context as Material;
        if (m == null) return;
        var stats = CleanUnusedProperties(m);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Clean Unused Properties", $"Removed {stats} from {m.name}.", "OK");
    }

    public static RemovedPropertyStatistics CleanUnusedProperties(Material m) {
        var shader = m.shader;
        if (shader == null || shader.name == "Hidden/InternalErrorShader")
            return new RemovedPropertyStatistics();
        var existingProperties = new Dictionary<string, ShaderPropertyType>();
        for (int i = ShaderUtil.GetPropertyCount(shader) - 1; i >= 0; i--) {
            var name = ShaderUtil.GetPropertyName(shader, i);
            var type = ShaderUtil.GetPropertyType(shader, i);
            if (type == ShaderPropertyType.Range) type = ShaderPropertyType.Float;
            existingProperties[name] = type;
        }
        var stats = new RemovedPropertyStatistics();
        using (var so = new SerializedObject(m)) {
            stats.colorCount = CleanUnusedProperties(existingProperties, so.FindProperty("m_SavedProperties.m_Colors"), ShaderPropertyType.Color);
            stats.vectorCount += CleanUnusedProperties(existingProperties, so.FindProperty("m_SavedProperties.m_Vectors"), ShaderPropertyType.Vector);
            stats.floatCount += CleanUnusedProperties(existingProperties, so.FindProperty("m_SavedProperties.m_Floats"), ShaderPropertyType.Float);
            stats.texEnvCount += CleanUnusedProperties(existingProperties, so.FindProperty("m_SavedProperties.m_TexEnvs"), ShaderPropertyType.TexEnv);
            stats.intCount += CleanUnusedProperties(existingProperties, so.FindProperty("m_SavedProperties.m_Ints"), ShaderPropertyType.Int);
            so.ApplyModifiedProperties();
        }
        return stats;
    }

    static int CleanUnusedProperties(IReadOnlyDictionary<string, ShaderPropertyType> existingProperties, SerializedProperty arrayProperty, ShaderPropertyType type) {
        if (arrayProperty == null) return 0;
        int removed = 0;
        for (int i = arrayProperty.arraySize - 1; i >= 0; i--) {
            var prop = arrayProperty.GetArrayElementAtIndex(i);
            var name = prop.FindPropertyRelative("first").stringValue;
            if (existingProperties.TryGetValue(name, out var existingType) && existingType == type)
                continue;
            DeleteElementAtIndex(arrayProperty, i);
            removed++;
        }
        return removed;
    }

    static void DeleteElementAtIndex(SerializedProperty array, int index) {
        int count = array.arraySize;
        array.DeleteArrayElementAtIndex(index);
        if (count == array.arraySize) array.DeleteArrayElementAtIndex(index);
    }

    public struct RemovedPropertyStatistics {
        public int colorCount;
        public int vectorCount;
        public int floatCount;
        public int texEnvCount;
        public int intCount;

        public readonly override string ToString() {
            var entries = new List<string>(5);
            if (colorCount > 0) entries.Add($"{colorCount} colors");
            if (vectorCount > 0) entries.Add($"{vectorCount} vectors");
            if (floatCount > 0) entries.Add($"{floatCount} floats");
            if (texEnvCount > 0) entries.Add($"{texEnvCount} textures");
            if (intCount > 0) entries.Add($"{intCount} ints");
            return entries.Count > 0 ? string.Join(", ", entries) : "nothing";
        }

        public static RemovedPropertyStatistics operator +(RemovedPropertyStatistics a, RemovedPropertyStatistics b) => new RemovedPropertyStatistics {
            colorCount = a.colorCount + b.colorCount,
            vectorCount = a.vectorCount + b.vectorCount,
            floatCount = a.floatCount + b.floatCount,
            texEnvCount = a.texEnvCount + b.texEnvCount,
            intCount = a.intCount + b.intCount,
        };
    }
}
