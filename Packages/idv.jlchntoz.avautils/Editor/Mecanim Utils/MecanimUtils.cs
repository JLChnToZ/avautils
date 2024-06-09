using System;
using System.Collections.Generic;
using UnityEngine;
using JLChnToZ.CommonUtils.Dynamic;

namespace JLChnToZ.EditorExtensions {
    public static class MecanimUtils {
        delegate Dictionary<int, Transform> MapBonesDelegate(Transform root, Dictionary<Transform, bool> validBones);
        static readonly MapBonesDelegate MapBones = Delegates.BindMethod<MapBonesDelegate>("UnityEditor.AvatarAutoMapper, UnityEditor", "MapBones");
        
        public static Transform[] GuessHumanoidBodyBones(Transform root, IEnumerable<Transform> validBones = null) {
            var animator = root.GetComponentInParent<Animator>();
            var result = new Transform[HumanTrait.BoneCount];
            if (animator != null && animator.avatar != null && animator.avatar.isHuman) {
                var rootGameObject = animator.gameObject;
                bool wasEnabled = animator.enabled, wasActive = rootGameObject.activeSelf;
                if (!wasEnabled) animator.enabled = true;
                if (!wasActive) rootGameObject.SetActive(true);
                for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++) {
                    var boneTransform = animator.GetBoneTransform(bone);
                    if (boneTransform != null) result[(int)bone] = boneTransform;
                }
                if (!wasEnabled) animator.enabled = wasEnabled;
                if (!wasActive) rootGameObject.SetActive(wasActive);
                return result;
            }
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0) {
                var current = stack.Pop();
                if (current.name.Equals("hips", StringComparison.OrdinalIgnoreCase)) {
                    root = current.parent;
                    break;
                }
                foreach (Transform child in current) stack.Push(child);
            }
            if (MapBones == null) {
                Debug.LogWarning("Can not find AvatarAutoMapper, humanoid bone mapping is not available.");
                return null;
            }
            var vaildBoneMap = new Dictionary<Transform, bool>();
            if (validBones != null)
                foreach (var bone in validBones)
                    vaildBoneMap[bone] = true;
            else {
                stack.Clear();
                stack.Push(root);
                while (stack.Count > 0) {
                    var current = stack.Pop();
                    vaildBoneMap[current] = true;
                    foreach (Transform child in current) stack.Push(child);
                }
            }
            var rawResult = MapBones(root, vaildBoneMap);
            foreach (var kv in rawResult) result[kv.Key] = kv.Value;
            return result;
        }
    }
}