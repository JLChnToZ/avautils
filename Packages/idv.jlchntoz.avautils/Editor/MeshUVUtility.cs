using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Moves/Translates UV per sub mesh
public class MeshUVUtility : EditorWindow {
    Mesh mesh, clone;
    Rect[] uvModifyRects;

    [MenuItem("Tools/JLChnToZ/Mesh UV Utility")]
    static void ShowWindow() {
        GetWindow<MeshUVUtility>("Mesh UV Utility");
    }

    void OnGUI() {
        mesh = EditorGUILayout.ObjectField("Mesh", mesh, typeof(Mesh), true) as Mesh;
        if (mesh == null) return;
        if (uvModifyRects == null || uvModifyRects.Length != mesh.subMeshCount) {
            uvModifyRects = new Rect[mesh.subMeshCount];
            for (int i = 0; i < uvModifyRects.Length; i++)
                uvModifyRects[i] = new Rect(0, 0, 1, 1);
        }
        for (int i = 0; i < uvModifyRects.Length; i++) {
            uvModifyRects[i] = EditorGUILayout.RectField($"Sub Mesh {i} UV Modify Rect", uvModifyRects[i]);
        }
        if (GUILayout.Button("Apply")) {
            if (clone != null) {
                if (!AssetDatabase.Contains(clone)) {
                    DestroyImmediate(clone);
                }
            }
            clone = Instantiate(mesh);
            var uvs = clone.uv;
            var subMeshes = clone.subMeshCount;
            var subMeshesUVs = new Vector2[subMeshes][];
            for (int i = 0; i < subMeshes; i++) {
                var triangles = clone.GetTriangles(i);
                var subMeshUVs = new Vector2[triangles.Length];
                for (int j = 0; j < triangles.Length; j++) {
                    var uv = uvs[triangles[j]];
                    uv.x = Mathf.Lerp(uvModifyRects[i].xMin, uvModifyRects[i].xMax, uv.x);
                    uv.y = Mathf.Lerp(uvModifyRects[i].yMin, uvModifyRects[i].yMax, uv.y);
                    subMeshUVs[j] = uv;
                }
                subMeshesUVs[i] = subMeshUVs;
            }
            for (int i = 0, j = 0; i < subMeshes; i++) {
                var triangles = clone.GetTriangles(i);
                for (int k = 0; k < triangles.Length; k++, j++) {
                    uvs[triangles[k]] = subMeshesUVs[i][k];
                }
            }
            clone.uv = uvs;
        }
        if (GUILayout.Button("Save as")) {
            var path = EditorUtility.SaveFilePanelInProject("Save as", mesh.name, "asset", "Save as");
            if (!string.IsNullOrEmpty(path)) {
                AssetDatabase.CreateAsset(clone, path);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
