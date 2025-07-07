using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace JLChnToZ.EditorExtensions {
    public static class MecanimUtils {
        delegate Dictionary<int, Transform> MapBones(Transform root, Dictionary<Transform, bool> validBones);
        static readonly FieldInfo parentNameField = typeof(SkeletonBone).GetField("parentName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        static string[] humanNames, muscleNames;
        static int[] parentBones;
        static bool[] requiredBones;
        static MapBones mapBones;

        public static string[] HumanBoneNames => humanNames;

        public static string[] MuscleNames => muscleNames;

        public static int[] ParentBones => parentBones;

        public static bool[] RequiredBones => requiredBones;

        public static readonly HumanDescription defaultHumanDescription = new() {
            human = Array.Empty<HumanBone>(),
            skeleton = Array.Empty<SkeletonBone>(),
            armStretch = 0.05F,
            upperArmTwist = 0.5F,
            lowerArmTwist = 0.5F,
            legStretch = 0.05F,
            lowerLegTwist = 0.5F,
            upperLegTwist = 0.5F,
            feetSpacing = 0.0F,
            hasTranslationDoF = false,
        };

        [InitializeOnLoadMethod]
        static void Init() {
            int boneCount = HumanTrait.BoneCount;
            humanNames = HumanTrait.BoneName;
            muscleNames = HumanTrait.MuscleName;
            requiredBones = new bool[boneCount];
            parentBones = new int[boneCount];
            for (int i = 0; i < boneCount; i++) {
                requiredBones[i] = HumanTrait.RequiredBone(i);
                parentBones[i] = HumanTrait.GetParentBone(i);
            }
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
                    p = parentBones[p];
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
                    if (requiredBones[p]) break;
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

        public static HumanDescription GetHumanDescriptionOrDefault(this Avatar avatar) =>
            avatar == null ? defaultHumanDescription : avatar.humanDescription;

        public static void ApplyTPose(this Animator animator, bool humanBoneOnly = true, bool applyScale = true, bool undo = false) {
            if (animator == null) return;
            ApplyTPose(animator.avatar, animator.transform, humanBoneOnly, applyScale, undo);
        }

        public static void ApplyTPose(this Avatar avatar, Transform root, bool humanBoneOnly = true, bool applyScale = true, bool undo = false) {
            if (undo && EditorApplication.isPlayingOrWillChangePlaymode) undo = false;
            if (avatar == null || root == null) return;
            var walker = new Queue<Transform>();
            HashSet<Transform> whiteList = null;
            Transform hips = null;
            if (humanBoneOnly && avatar.isHuman) {
                var bones = new Transform[HumanTrait.BoneCount];
                FetchHumanoidBodyBones(avatar, root, bones, walker);
                hips = bones[0];
                whiteList = new HashSet<Transform>(bones.Length);
                foreach (var bone in bones)
                    if (bone != null)
                        whiteList.Add(bone);
            }
            var humanDesc = avatar.humanDescription;
            var skeletonMapping = new Dictionary<(string bone, string parent), SkeletonBone>();
            string rootBoneName = null;
            for (int i = 0; i < humanDesc.skeleton.Length; i++) {
                var skeleton = humanDesc.skeleton[i];
                if (string.IsNullOrEmpty(skeleton.name)) continue;
                var parentName = parentNameField.GetValue(skeleton) as string;
                if (i == 0)
                    rootBoneName = skeleton.name;
                else if (rootBoneName != null && rootBoneName == parentName)
                    parentName = null;
                skeletonMapping[(skeleton.name, parentName ?? "")] = skeleton;
            }
            foreach (Transform child in root) walker.Enqueue(child);
            while (walker.TryDequeue(out var current)) {
                foreach (Transform child in current) walker.Enqueue(child);
                if ((whiteList != null && !whiteList.Contains(current)) ||
                    !(skeletonMapping.TryGetValue((current.name, current.parent.name), out var skeletonBone) ||
                    skeletonMapping.TryGetValue((current.name, ""), out skeletonBone)))
                    continue;
                if (undo) Undo.RecordObject(current, "Apply T-Pose");
                current.SetLocalPositionAndRotation(skeletonBone.position, skeletonBone.rotation);
                if (applyScale) current.localScale = skeletonBone.scale;
            }
            if (hips != null) {
                var trs = Matrix4x4.identity;
                for (var current = hips; current != null && current != root; current = current.parent)
                    if (skeletonMapping.TryGetValue((current.name, current.parent.name), out var skeletonBone) ||
                        skeletonMapping.TryGetValue((current.name, ""), out skeletonBone))
                        trs = Matrix4x4.TRS(skeletonBone.position, skeletonBone.rotation, skeletonBone.scale) * trs;
                trs = hips.parent.worldToLocalMatrix * root.localToWorldMatrix * trs;
                hips.SetLocalPositionAndRotation(trs.GetPosition(), trs.rotation);
                if (applyScale) hips.localScale = trs.lossyScale;
            }
            if (undo) Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }
    }
}