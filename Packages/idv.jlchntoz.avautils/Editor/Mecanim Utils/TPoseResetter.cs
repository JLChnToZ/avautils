using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace JLChnToZ.EditorExtensions {
    public static class TPoseResetter {
        const string undoDescription = "Reset to T-Pose";

        // An array of floats that represents the T-Pose of a humanoid avatar.
        static readonly float[] TPoseMuscles = new[] {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // Spine to Neck
            0, 0, 0, 0, 0, 0, 0, 0, 0, // Head & Face
            0.6F, 0, 0, 1, 0, 0, 0, 0, // Left Foot
            0.6F, 0, 0, 1, 0, 0, 0, 0, // Right Foot
            0, 0, 0.4F, 0.3F, 0.1F, 1, -0.1F, 0, 0, // Left Hand
            0, 0, 0.4F, 0.3F, 0.1F, 1, -0.1F, 0, 0, // Right Hand
            -0.7F, 0.5F, 0.6F, 0.6F, // Left Thumb
            0.7F, -0.5F, 0.8F, 0.8F, // Left Index
            0.7F, -0.6F, 0.8F, 0.8F, // Left Middle
            0.7F, -0.6F, 0.8F, 0.8F, // Left Ring
            0.7F, -0.5F, 0.8F, 0.8F, // Left Little
            -0.7F, 0.5F, 0.6F, 0.6F, // Right Thumb
            0.7F, -0.5F, 0.8F, 0.8F, // Right Index
            0.7F, -0.6F, 0.8F, 0.8F, // Right Middle
            0.7F, -0.6F, 0.8F, 0.8F, // Right Ring
            0.7F, -0.5F, 0.8F, 0.8F, // Right Little
        };

        public static void Run(Animator animator, bool undoable = true) {
            var avatar = animator.avatar;
            if (avatar == null || !avatar.isHuman)
                throw new ArgumentException("The avatar is not humanoid.", nameof(animator));
            if (undoable) Undo.RecordObject(animator, undoDescription);
            RunUnchecked(avatar, animator.transform);
        }

        [MenuItem("Tools/JLChnToZ/Reset Armature to T-Pose")]
        static void Run() {
            var selection = Selection.GetFiltered<Animator>(SelectionMode.Editable);
            if (selection.Length == 0) return;
            var bones = new List<Transform>((int)HumanBodyBones.LastBone);
            foreach (var animator in selection) {
                var avatar = animator.avatar;
                if (avatar == null || !avatar.isHuman) continue;
                for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++) {
                    var transform = animator.GetBoneTransform(bone);
                    if (transform == null) continue;
                    bones.Add(transform);
                }
                Undo.RecordObjects(bones.ToArray(), undoDescription);
                bones.Clear();
                RunUnchecked(avatar, animator.transform);
            }
        }

        static void RunUnchecked(Avatar avatar, Transform root) {
            using (var handler = new HumanPoseHandler(avatar, root)) {
                var tPose = new HumanPose {
                    bodyPosition = Vector3.up,
                    bodyRotation = Quaternion.identity,
                    muscles = TPoseMuscles,
                };
                handler.SetHumanPose(ref tPose);
            }
        }
    }
}