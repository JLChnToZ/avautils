using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace JLChnToZ.EditorExtensions {
    public static class EditorUtilsExtension {
#if UNITY_2020_1_OR_NEWER
        static readonly GUILayoutOption[] listLayoutOptions = new[] { GUILayout.ExpandWidth(true) };
#endif

        public static void DoScrollableLayoutList(this ReorderableList list, ref Vector2 scrollPosition, params GUILayoutOption[] options) {
#if UNITY_2020_1_OR_NEWER
            StrongBox<Rect> state = null;
#endif
            using (new EditorGUILayout.VerticalScope(options))
            using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPosition, Array.Empty<GUILayoutOption>())) {
                scrollPosition = scrollView.scrollPosition;
#if UNITY_2020_1_OR_NEWER
                if (Event.current.type != EventType.Used) {
                    var rect = GUILayoutUtility.GetRect(10F, list.GetHeight(), listLayoutOptions);
                    state = GUIUtility.GetStateObject(typeof(StrongBox<Rect>), GUIUtility.GetControlID(FocusType.Passive, rect)) as StrongBox<Rect>;
                    state.Value.position = scrollPosition;
                    list.DoList(rect, state.Value);
                }
#else
                list.DoLayoutList();
#endif
            }
#if UNITY_2020_1_OR_NEWER
            var eventType = Event.current.type;
            if (eventType != EventType.Layout && eventType != EventType.Used)
                state.Value = GUILayoutUtility.GetLastRect();
#endif
        }
    }
}