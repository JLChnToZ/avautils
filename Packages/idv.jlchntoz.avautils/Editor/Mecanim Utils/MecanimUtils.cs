using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace JLChnToZ.EditorExtensions {
    public static class MecanimUtils {
        delegate Dictionary<int, Transform> MapBones(Transform root, Dictionary<Transform, bool> validBones);
        static string[] humanNames, muscleNames;
        static MapBones mapBones;

        public static string[] HumanBoneNames => humanNames;

        public static string[] MuscleNames => muscleNames;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        static void Init() {
            humanNames = HumanTrait.BoneName;
            muscleNames = HumanTrait.MuscleName;
            var type = Type.GetType("UnityEditor.AvatarAutoMapper, UnityEditor", false);
            if (type != null) {
                var delegateType = typeof(MapBones);
                var method = type.GetMethod(
                    "MapBones",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Array.ConvertAll(delegateType.GetMethod(nameof(MapBones.Invoke)).GetParameters(), p => p.ParameterType),
                    null
                );
                if (method != null) mapBones = (MapBones)Delegate.CreateDelegate(delegateType, method, false);
            }
        }

        public static Transform[] GuessHumanoidBodyBones(Transform root, IEnumerable<Transform> validBones = null, bool ignoreAvatar = false) {
            var result = new Transform[HumanTrait.BoneCount];
            var hw = new Queue<Transform>();
            if (!ignoreAvatar) {
                var animator = root.GetComponentInParent<Animator>(true);
                if (animator != null && FetchHumanoidBodyBones(animator.avatar, root, result, hw)) return result;
            }
            if (mapBones == null) {
                Debug.LogWarning("Cannot find AvatarAutoMapper, humanoid bone mapping is not available.");
                return result;
            }
            // We already have hips when we try to fetch humanoid bones from avatar
            if (result[0] != null)
                root = result[0].parent;
            else {
                hw.Enqueue(root);
                while (hw.TryDequeue(out var current)) {
                    if (current.name.Equals("hips", StringComparison.OrdinalIgnoreCase)) {
                        root = current.parent;
                        break;
                    }
                    foreach (Transform child in current) hw.Enqueue(child);
                }
                hw.Clear();
            }
            var vaildBoneMap = new Dictionary<Transform, bool>();
            if (validBones != null)
                foreach (var bone in validBones)
                    vaildBoneMap[bone] = true;
            else {
                hw.Enqueue(root);
                while (hw.TryDequeue(out var current)) {
                    vaildBoneMap[current] = true;
                    foreach (Transform child in current) hw.Enqueue(child);
                }
            }
            var rawResult = mapBones(root, vaildBoneMap);
            foreach (var kv in rawResult)
                if (result[kv.Key] == null)
                    result[kv.Key] = kv.Value;
            return result;
        }

        // This is slower but less strict than using Animator.GetBoneTransform
        static bool FetchHumanoidBodyBones(Avatar avatar, Transform root, Transform[] result, Queue<Transform> hw) {
            if (avatar == null || root == null) return false;
            var desc = avatar.humanDescription;
            if (desc.human == null && desc.human.Length == 0) return false;
            var boneNames = new string[humanNames.Length];
            foreach (var bone in desc.human) {
                if (string.IsNullOrEmpty(bone.humanName) || string.IsNullOrEmpty(bone.boneName)) continue;
                int i = Array.IndexOf(humanNames, bone.humanName);
                if (i < 0) continue;
                boneNames[i] = bone.boneName;
            }
            for (int i = 0; i < boneNames.Length; i++) {
                if (boneNames[i] == null) continue;
                int p = i;
                while (true) {
                    p = HumanTrait.GetParentBone(p);
                    // Negative parent index means it is root.
                    if (p < 0) {
                        hw.Enqueue(root);
                        break;
                    }
                    if (result[p] != null) {
                        hw.Enqueue(result[p]);
                        break;
                    }
                    // If it is a required bone and we can't find it, the hierarchy is already broken.
                    if (HumanTrait.RequiredBone(p)) break;
                }
                while (hw.TryDequeue(out var current)) {
                    if (boneNames[i] == current.name) {
                        result[i] = current;
                        break;
                    }
                    foreach (Transform child in current) hw.Enqueue(child);
                }
                hw.Clear();
            }
            return avatar.isHuman;
        }

        public static Transform[] FetchHumanoidBodyBones(Avatar avatar, Transform root) {
            var result = new Transform[HumanTrait.BoneCount];
            FetchHumanoidBodyBones(avatar, root, result, new());
            return result;
        }
    }
}