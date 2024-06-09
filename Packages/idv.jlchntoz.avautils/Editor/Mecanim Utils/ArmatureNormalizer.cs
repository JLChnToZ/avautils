using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using UnityObject = UnityEngine.Object;
using static UnityEngine.Object;

namespace JLChnToZ.EditorExtensions {
    public sealed class ArmatureNormalizer {
        const string undoDescription = "Normalize Armature";
        static readonly Quaternion identity = Quaternion.identity;
        static readonly Quaternion downward = Quaternion.Euler(180, 0, 0);
        static readonly Quaternion foot = Quaternion.Euler(120, 0, 0);
        static readonly Quaternion forward = Quaternion.Euler(90, 0, 0);
        static readonly Quaternion left = Quaternion.Euler(0, 0, 90);
        static readonly Quaternion right = Quaternion.Euler(0, 0, -90);

        // An array of "normalized" rotations that represents the T-Pose of a humanoid avatar.
        // This should be friendly to IK libraries such as FinalIK.
        static readonly Quaternion[] TPoseRotations = new[] {
            identity, // Hips
            downward, downward, downward, downward, foot, foot, // Legs
            identity, identity, identity, identity, // Spine to Neck
            left, right, left, right, left, right, // Arms
            left, right, forward, forward, // Hand & Toes
            identity, identity, identity, // Face
            left, left, left, // Left Thumb
            left, left, left, // Left Index
            left, left, left, // Left Middle
            left, left, left, // Left Ring
            left, left, left, // Left Little
            right, right, right, // Right Thumb
            right, right, right, // Right Index
            right, right, right, // Right Middle
            right, right, right, // Right Ring
            right, right, right, // Right Little
        };

        readonly Dictionary<Transform, HumanBodyBones> boneToHumanBone = new Dictionary<Transform, HumanBodyBones>();
        readonly Dictionary<Transform, Matrix4x4> movedBones = new Dictionary<Transform, Matrix4x4>();
        readonly Dictionary<Transform, TranslateRotate> cachedPositions = new Dictionary<Transform, TranslateRotate>();
        readonly HashSet<Transform> skeletonBoneTransforms = new HashSet<Transform>();
        readonly HashSet<UnityObject> assets = new HashSet<UnityObject>();
        readonly Animator animator;
        readonly Transform root;
        readonly string srcAssetPath;
        readonly bool undoable;
        Avatar avatar;

        public static void NormalizeArmature(Animator animator, bool undoable = true) {
            var normalizer = new ArmatureNormalizer(animator, undoable);
            normalizer.Normalize();
            normalizer.UpdateBindposes();
            normalizer.FixCrossLeg();
            normalizer.RegenerateAvatar();
            normalizer.ApplyAvatar();
            normalizer.SaveAssets();
            if (undoable) Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        ArmatureNormalizer(Animator animator, bool undoable) {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            this.animator = animator;
            this.undoable = undoable;
            avatar = animator.avatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new ArgumentException("The avatar is not a vaild humanoid.", nameof(animator));
            srcAssetPath = AssetDatabase.GetAssetPath(avatar);
            if (string.IsNullOrEmpty(srcAssetPath) && PrefabUtility.IsPartOfPrefabAsset(animator))
                srcAssetPath = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(animator));
            root = animator.transform;
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++) {
                var transform = animator.GetBoneTransform(bone);
                if (transform == null) continue;
                boneToHumanBone[transform] = bone;
            }
        }

        #region Steps
        void Normalize() {
            for (var bone = HumanBodyBones.LastBone - 1; bone >= HumanBodyBones.Hips; bone--) {
                var transform = animator.GetBoneTransform(bone);
                if (transform == null) continue;
                bool canContainTwistBone = false;
                switch (bone) {
                    case HumanBodyBones.LeftUpperLeg: case HumanBodyBones.RightUpperLeg:
                    case HumanBodyBones.LeftLowerLeg: case HumanBodyBones.RightLowerLeg:
                    case HumanBodyBones.LeftUpperArm: case HumanBodyBones.RightUpperArm:
                    case HumanBodyBones.LeftLowerArm: case HumanBodyBones.RightLowerArm:
                        canContainTwistBone = true;
                        break;
                }
                Transform twistBone = null;
                for (int i = transform.childCount - 1; i >= 0; i--) {
                    var child = transform.GetChild(i);
                    if (canContainTwistBone && child.name.IndexOf("twist", StringComparison.OrdinalIgnoreCase) >= 0) {
                        twistBone = child;
                        continue;
                    }
                    CachePosition(child);
                }
                if (undoable) Undo.RecordObject(transform, undoDescription);
                var orgMatrix = transform.localToWorldMatrix;
                var twistOrgMatrix = twistBone != null ? twistBone.localToWorldMatrix : Matrix4x4.identity;
                transform.rotation = TPoseRotations[(int)bone] * root.rotation;
                movedBones[transform] = transform.worldToLocalMatrix * orgMatrix;
                if (twistBone != null) {
                    if (undoable) Undo.RecordObject(twistBone, undoDescription);
                    twistBone.localRotation = identity;
                    movedBones[twistBone] = twistBone.worldToLocalMatrix * twistOrgMatrix;
                }
                RestoreCachedPositions();
            }
            for (
                var transform = animator.GetBoneTransform(HumanBodyBones.Hips).parent;
                transform != null && transform != root;
                transform = transform.parent
            ) {
                for (int i = transform.childCount - 1; i >= 0; i--)
                    CachePosition(transform.GetChild(i));
                if (undoable) Undo.RecordObject(transform, undoDescription);
                var orgMatrix = transform.localToWorldMatrix;
                transform.SetPositionAndRotation(root.position, root.rotation);
                movedBones[transform] = transform.worldToLocalMatrix * orgMatrix;
                RestoreCachedPositions();
            }
        }

        void UpdateBindposes() {
            foreach (var skinnedMeshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var orgMesh = skinnedMeshRenderer.sharedMesh;
                var bones = skinnedMeshRenderer.bones;
                Matrix4x4[] bindposes = null;
                for (int i = 0; i < bones.Length; i++) {
                    var bone = bones[i];
                    if (bone != null && movedBones.TryGetValue(bone, out var deltaMatrix)) {
                        if (bindposes == null) bindposes = orgMesh.bindposes;
                        bindposes[i] = deltaMatrix * bindposes[i];
                    }
                }
                if (bindposes == null) continue;
                var clonedMesh = Instantiate(orgMesh);
                clonedMesh.name = orgMesh.name;
                clonedMesh.bindposes = bindposes;
                assets.Add(clonedMesh);
                if (undoable) Undo.RecordObject(skinnedMeshRenderer, undoDescription);
                skinnedMeshRenderer.sharedMesh = clonedMesh;
            }
        }

        void FixCrossLeg() {
            FixCrossLegSide(
                animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                animator.GetBoneTransform(HumanBodyBones.LeftFoot)
            );
            FixCrossLegSide(
                animator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                animator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                animator.GetBoneTransform(HumanBodyBones.RightFoot)
            );
        }

        void FixCrossLegSide(Transform thigh, Transform knee, Transform foot) {
            var vector = thigh.InverseTransformPoint(knee.position).normalized;
            if (Mathf.Abs(vector.x) < 0.001 && vector.z < 0) return; // Already fixed
            if (undoable) {
                Undo.RecordObject(thigh, undoDescription);
                Undo.RecordObject(knee, undoDescription);
                Undo.RecordObject(foot, undoDescription);
            }
            var footRotation = foot.rotation;
            var rotation =
                Quaternion.AngleAxis(Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg - 90F, Vector3.forward) *
                Quaternion.AngleAxis(Mathf.Atan2(vector.y, vector.z) * Mathf.Rad2Deg - 90.05F, Vector3.right);
            thigh.localRotation = rotation * thigh.localRotation;
            knee.localRotation = Quaternion.Inverse(rotation) * knee.localRotation;
            foot.rotation = footRotation;
        }

        void RegenerateAvatar() {
            var boneNames = new HashSet<string>();
            var transformsToAdd = new List<(Transform, int)>();
            foreach (var bone in movedBones.Keys)
                for (var parent = bone; parent != null && parent != root; parent = parent.parent) {
                    if (!skeletonBoneTransforms.Add(parent)) break;
                    boneNames.Add(parent.name);
                }
            var ambiguousNameMap = new Dictionary<Transform, string>();
            var stack = new Stack<(int, Transform)>();
            stack.Push((0, root));
            while (stack.Count > 0) {
                var (index, bone) = stack.Pop();
                if (index >= bone.childCount) continue;
                var child = bone.GetChild(index);
                stack.Push((index + 1, bone));
                stack.Push((0, child));
                if (skeletonBoneTransforms.Contains(child))
                    transformsToAdd.Add((child, stack.Count));
                else if (boneNames.Contains(child.name)) {
                    Debug.LogWarning($"[Armature Normalizer] Transform name \"{child.name}\" is ambiguous, this may prevents generated humanoid avatar working. You will have to fix it afterward.", child);
                    ambiguousNameMap[child] = child.name;
                    var temp = new string[boneNames.Count];
                    boneNames.CopyTo(temp);
                    var tempName = ObjectNames.GetUniqueName(temp, child.name);
                    boneNames.Add(tempName);
                    child.name = tempName;
                }
            }
            int i;
            var skeletonBones = new SkeletonBone[transformsToAdd.Count];
            var humanBones = new HumanBone[boneToHumanBone.Count];
            var humanBoneNames = HumanTrait.BoneName;
            transformsToAdd.Sort(CompareDepth);
            i = 0;
            foreach (var (bone, _) in transformsToAdd)
                skeletonBones[i++] = new SkeletonBone {
                    name = bone.name,
                    position = bone.localPosition,
                    rotation = bone.localRotation,
                    scale = bone.localScale,
                };
            i = 0;
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++) {
                var boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform == null) continue;
                humanBones[i++] = new HumanBone {
                    boneName = boneTransform.name,
                    humanName = humanBoneNames[(int)bone],
                    limit = new HumanLimit { useDefaultValues = true },
                };
            }
            var desc = avatar.humanDescription;
            desc = new HumanDescription {
                armStretch = desc.armStretch,
                upperArmTwist = desc.upperArmTwist,
                lowerArmTwist = desc.lowerArmTwist,
                legStretch = desc.legStretch,
                lowerLegTwist = desc.lowerLegTwist,
                upperLegTwist = desc.upperLegTwist,
                feetSpacing = desc.feetSpacing,
                hasTranslationDoF = desc.hasTranslationDoF,
                human = humanBones,
                skeleton = skeletonBones,
            };
            avatar = AvatarBuilder.BuildHumanAvatar(root.gameObject, desc);
            foreach (var kv in ambiguousNameMap) kv.Key.name = kv.Value;
        }

        void ApplyAvatar() {
            foreach (var child in skeletonBoneTransforms) CachePosition(child, true);
            if (undoable) Undo.RecordObject(animator, undoDescription);
            animator.avatar = avatar;
            RestoreCachedPositions();
        }

        void SaveAssets() {
            string assetPath;
            if (string.IsNullOrEmpty(srcAssetPath)) {
                assetPath = avatar.name;
                if (string.IsNullOrEmpty(assetPath)) assetPath = animator.name;
                assetPath = EditorUtility.SaveFilePanelInProject(
                    "Save Normalized Avatar And Meshes", assetPath,
                    "asset", "Save normalized avatar and meshes as a grouped asset (You can cancel this step and save them manually)"
                );
            } else {
                assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{Path.GetDirectoryName(srcAssetPath)}/{Path.GetFileNameWithoutExtension(srcAssetPath)} Normalized.asset"
                );
                assetPath = EditorUtility.SaveFilePanelInProject(
                    "Save Normalized Avatar And Meshes", Path.GetFileName(assetPath),
                    "asset", "Save normalized avatar and meshes as a grouped asset (You can cancel this step and save them manually)",
                    Path.GetDirectoryName(assetPath)
                );
            }
            if (string.IsNullOrEmpty(assetPath)) {
                Undo.RegisterCreatedObjectUndo(avatar, undoDescription);
                foreach (var subAsset in assets)
                    Undo.RegisterCreatedObjectUndo(subAsset, undoDescription);
                return;
            }
            AssetDatabase.CreateAsset(avatar, assetPath);
            foreach (var subAsset in assets)
                AssetDatabase.AddObjectToAsset(subAsset, assetPath);
            AssetDatabase.SaveAssets();
        }
        #endregion

        #region Utility
        int CompareDepth((Transform, int) a, (Transform, int) b) => a.Item2 - b.Item2;

        void CachePosition(Transform transform, bool isLocal = false) {
            cachedPositions[transform] = new TranslateRotate(transform, isLocal);
        }

        void RestoreCachedPositions() {
            foreach (var c in cachedPositions) {
                Undo.RecordObject(c.Key, undoDescription);
                c.Value.ApplyTo(c.Key);
            }
            cachedPositions.Clear();
        }
        #endregion

        #region Menu Items

        [MenuItem("Tools/JLChnToZ/Normalize Armature")]
        static void NormalizeSelectedArmature() {
            var selection = Selection.transforms;
            if (selection.Length == 0) return;
            var animators = new HashSet<Animator>();
            foreach (var transform in selection) {
                var animator = transform.GetComponentInParent<Animator>();
                if (animator != null) animators.Add(animator);
            }
            foreach (var animator in animators) NormalizeArmature(animator);
        }
        #endregion
    }

    struct TranslateRotate {
        public Vector3 position;
        public Quaternion rotation;
        public bool isLocal;

        public TranslateRotate(Transform transform, bool isLocal = false) {
            if (isLocal) {
                position = transform.localPosition;
                rotation = transform.localRotation;
            } else {
                position = transform.position;
                rotation = transform.rotation;
            }
            this.isLocal = isLocal;
        }

        public void ApplyTo(Transform transform) {
            if (isLocal) {
                #if UNITY_2021_3_OR_NEWER
                transform.SetLocalPositionAndRotation(position, rotation);
                #else
                transform.localPosition = position;
                transform.localRotation = rotation;
                #endif
            } else
                transform.SetPositionAndRotation(position, rotation);
        }
    }
}