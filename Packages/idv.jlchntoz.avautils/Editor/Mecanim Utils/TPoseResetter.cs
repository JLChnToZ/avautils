using System;
using UnityEngine;
using UnityEditor;

namespace JLChnToZ.EditorExtensions {
    public static class TPoseResetter {
        [Obsolete("Use MecanimUtils.ApplyTPose instead.")]
        public static void Run(Animator animator, bool undoable = true) =>
            MecanimUtils.ApplyTPose(animator, true, false, undoable);

        [MenuItem("Tools/JLChnToZ/Reset Armature to T-Pose")]
        static void Run() {
            var selection = Selection.GetFiltered<Animator>(SelectionMode.Editable);
            if (selection.Length == 0) return;
            foreach (var animator in selection)
                MecanimUtils.ApplyTPose(animator, true, false, true);
        }
    }
}