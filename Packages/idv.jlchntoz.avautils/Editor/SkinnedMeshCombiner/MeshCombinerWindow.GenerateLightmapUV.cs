/**
 * The MIT License (MIT)
 *
 * Copyright (c) 2023 Jeremy Lam aka. Vistanz
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
using UnityEngine;
using UnityEditor;

namespace JLChnToZ.EditorExtensions.SkinnedMeshCombiner {
    public partial class MeshCombinerWindow : EditorWindow {
        const string GENERATE_LIGHTMAP_UV_INFO = "You can regenerate lightmap UVs in this tab.\n" +
            "This operation will overwrite the existing lightmap UVs (UV2) of the combined mesh.";
        bool generateLightmapUVs;
        UnwrapParam unwrapParam;

        void DrawRegenerateUVTab() {
            EditorGUILayout.HelpBox(GENERATE_LIGHTMAP_UV_INFO, MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            generateLightmapUVs = EditorGUILayout.ToggleLeft("Regenerate Lightmap UVs", generateLightmapUVs);
            if (generateLightmapUVs && GUILayout.Button("Reset Parameters", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                UnwrapParam.SetDefaults(out unwrapParam);
            EditorGUILayout.EndHorizontal();
            if (generateLightmapUVs) {
                EditorGUI.indentLevel++;
                unwrapParam.hardAngle = EditorGUILayout.Slider("Hard Angle", unwrapParam.hardAngle, 0, 180);
                unwrapParam.angleError = EditorGUILayout.Slider("Angle Error", unwrapParam.angleError, 0, 1);
                unwrapParam.areaError = EditorGUILayout.Slider("Area Error", unwrapParam.areaError, 0, 1);
                unwrapParam.packMargin = EditorGUILayout.Slider("Pack Margin", unwrapParam.packMargin, 0, 1);
                EditorGUI.indentLevel--;
            }
            GUILayout.FlexibleSpace();
        }
    }
}