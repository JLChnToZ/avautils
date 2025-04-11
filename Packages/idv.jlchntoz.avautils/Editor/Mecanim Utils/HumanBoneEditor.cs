using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;

using static JLChnToZ.CommonUtils.Dynamic.Limitless;
using JLChnToZ.EditorExtensions;

[EditorTool(NAME, typeof(Animator))]
public class HumanBoneEditor : EditorTool {
    const string NAME = "Human Bone Editor";
    const string INVALID_AVATAR_MESSAGE = "Avatar is not valid or not humanoid";
    const string UNDO_MESSAGE = "Edit Human Bone";
    static readonly dynamic AnimationRecording = Static("UnityEditorInternal.AnimationRecording, UnityEditor");
    static readonly dynamic AnimationWindow = Static("UnityEditor.AnimationWindow, UnityEditor");
    static string[] musclePropertyNames;
    static GUIContent icon, tempContent;
    static EditorWindow animationEditorWindow;
    static dynamic animWindowState, controlInterface;
    HumanPoseHandler poseHandler;
    HumanPose pose;
    Transform poseRoot;
    readonly HashSet<HumanBodyBones> selectedBones = new HashSet<HumanBodyBones>();
    Animator activeAnimator;
    AnimationClip activeClip;
    float currentTime;
    bool invalidAvatarMessageShown;
    bool isEditingAnimationClip;

    public override GUIContent toolbarIcon {
        get {
            if (icon == null)
                icon = new GUIContent(EditorGUIUtility.IconContent("Avatar Icon")) {
                    text = NAME,
                    tooltip = NAME,
                };
            return icon;
        }
    }

    static void Init() {
        if (musclePropertyNames == null) {
            musclePropertyNames = HumanTrait.MuscleName;
            musclePropertyNames[55] = "LeftHand.Thumb.1 Stretched";
            musclePropertyNames[56] = "LeftHand.Thumb.Spread";
            musclePropertyNames[57] = "LeftHand.Thumb.2 Stretched";
            musclePropertyNames[58] = "LeftHand.Thumb.3 Stretched";
            musclePropertyNames[59] = "LeftHand.Index.1 Stretched";
            musclePropertyNames[60] = "LeftHand.Index.Spread";
            musclePropertyNames[61] = "LeftHand.Index.2 Stretched";
            musclePropertyNames[62] = "LeftHand.Index.3 Stretched";
            musclePropertyNames[63] = "LeftHand.Middle.1 Stretched";
            musclePropertyNames[64] = "LeftHand.Middle.Spread";
            musclePropertyNames[65] = "LeftHand.Middle.2 Stretched";
            musclePropertyNames[66] = "LeftHand.Middle.3 Stretched";
            musclePropertyNames[67] = "LeftHand.Ring.1 Stretched";
            musclePropertyNames[68] = "LeftHand.Ring.Spread";
            musclePropertyNames[69] = "LeftHand.Ring.2 Stretched";
            musclePropertyNames[70] = "LeftHand.Ring.3 Stretched";
            musclePropertyNames[71] = "LeftHand.Little.1 Stretched";
            musclePropertyNames[72] = "LeftHand.Little.Spread";
            musclePropertyNames[73] = "LeftHand.Little.2 Stretched";
            musclePropertyNames[74] = "LeftHand.Little.3 Stretched";
            musclePropertyNames[75] = "RightHand.Thumb.1 Stretched";
            musclePropertyNames[76] = "RightHand.Thumb.Spread";
            musclePropertyNames[77] = "RightHand.Thumb.2 Stretched";
            musclePropertyNames[78] = "RightHand.Thumb.3 Stretched";
            musclePropertyNames[79] = "RightHand.Index.1 Stretched";
            musclePropertyNames[80] = "RightHand.Index.Spread";
            musclePropertyNames[81] = "RightHand.Index.2 Stretched";
            musclePropertyNames[82] = "RightHand.Index.3 Stretched";
            musclePropertyNames[83] = "RightHand.Middle.1 Stretched";
            musclePropertyNames[84] = "RightHand.Middle.Spread";
            musclePropertyNames[85] = "RightHand.Middle.2 Stretched";
            musclePropertyNames[86] = "RightHand.Middle.3 Stretched";
            musclePropertyNames[87] = "RightHand.Ring.1 Stretched";
            musclePropertyNames[88] = "RightHand.Ring.Spread";
            musclePropertyNames[89] = "RightHand.Ring.2 Stretched";
            musclePropertyNames[90] = "RightHand.Ring.3 Stretched";
            musclePropertyNames[91] = "RightHand.Little.1 Stretched";
            musclePropertyNames[92] = "RightHand.Little.Spread";
            musclePropertyNames[93] = "RightHand.Little.2 Stretched";
            musclePropertyNames[94] = "RightHand.Little.3 Stretched";
        }
    }

    static GUIContent GetTempContent(string text, Texture image = null, string tooltip = "") {
        if (tempContent == null) tempContent = new GUIContent();
        tempContent.text = text;
        tempContent.image = image;
        tempContent.tooltip = tooltip;
        return tempContent;
    }

    static bool TryGetCurrentRecordingAnimationClip(out AnimationClip clip, out float time) {
        clip = null;
        time = 0;
        try {
            foreach (var window in AnimationWindow.GetAllAnimationWindows()) {
                var animEditor = window.animEditor;
                if (animEditor == null) continue;
                animWindowState = animEditor.state;
                if (animWindowState == null || !animWindowState.recording) continue;
                clip = animWindowState.activeAnimationClip;
                time = animWindowState.currentTime;
                controlInterface = animWindowState.controlInterface;
                animationEditorWindow = window;
                return true;
            }
        } catch (Exception e) {
            Debug.LogException(e);
        }
        return false;
    }

    static bool TryGetMuscleLimits(HumanDescription humanDescription, int humanId, int axis, out int muscle, out float min, out float max) {
        muscle = HumanTrait.MuscleFromBone(humanId, axis);
        if (muscle < 0) {
            min = max = 0;
            return false;
        }
        var humanLimits = humanDescription.human;
        if (humanId < humanLimits.Length) {
            var limit = humanLimits[humanId].limit;
            if (!limit.useDefaultValues) {
                min = limit.min[axis];
                max = limit.max[axis];
                return true;
            }
        }
        min = HumanTrait.GetMuscleDefaultMin(muscle);
        max = HumanTrait.GetMuscleDefaultMax(muscle);
        return true;
    }

    static void AddRootFloatKey(dynamic state, string property, float value) => AnimationRecording.AddKey(
        state, EditorCurveBinding.FloatCurve("", typeof(Animator), property), typeof(float), value, value
    );

    static float DoDiscHandle(Color color, Vector3 position, Vector3 normal, float value) {
        Handles.color = color;
        var forward = Vector3.Cross(normal, Vector3.up);
        return Vector3.SignedAngle(
            forward,
            Handles.Disc(
                Quaternion.AngleAxis(value, normal),
                position, normal,
                HandleUtility.GetHandleSize(position),
                true, 0
            ) * forward,
            normal
        );
    }

    public override void OnActivated() {
        Undo.undoRedoPerformed += Repose;
    }

    public override void OnWillBeDeactivated() => OnDestroy();

    void OnDestroy() {
        invalidAvatarMessageShown = false;
        selectedBones.Clear();
        poseHandler?.Dispose();
        poseHandler = null;
        poseRoot = null;
        activeAnimator = null;
        Undo.undoRedoPerformed -= Repose;
    }

    void Repose() {
        if (poseHandler == null && activeAnimator) {
            poseRoot = activeAnimator.transform;
            poseHandler = new HumanPoseHandler(activeAnimator.avatar, poseRoot);
        }
        if (!poseRoot) return;
        poseRoot.GetLocalPositionAndRotation(out var t, out var r);
        var s = poseRoot.localScale;
        var parent = poseRoot.parent;
        if (parent) poseRoot.SetParent(null, false);
        poseRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        poseRoot.localScale = Vector3.one;
        poseHandler.GetHumanPose(ref pose);
        if (parent) poseRoot.SetParent(parent, false);
        poseRoot.SetLocalPositionAndRotation(t, r);
        poseRoot.localScale = s;
    }

    public override void OnToolGUI(EditorWindow window) {
        isEditingAnimationClip = TryGetCurrentRecordingAnimationClip(out activeClip, out currentTime);
        var animator = target as Animator;
        if (animator == null || !animator.gameObject.activeInHierarchy) {
            activeAnimator = null;
            return;
        }
        var avatar = animator.avatar;
        if (avatar == null || !avatar.isValid || !avatar.isHuman) {
            if (!invalidAvatarMessageShown) {
                window.ShowNotification(GetTempContent(INVALID_AVATAR_MESSAGE));
                invalidAvatarMessageShown = true;
            }
            activeAnimator = null;
            return;
        }
        invalidAvatarMessageShown = false;
        Init();
        var humanDescription = avatar.humanDescription;
        var _avatar = Wrap(avatar);
        Quaternion[] rotations = new Quaternion[3];
        if (activeAnimator != animator || poseHandler == null) {
            activeAnimator = animator;
            poseHandler?.Dispose();
            poseHandler = null;
            Repose();
        }
        var boneNames = MecanimUtils.HumanBoneNames;
        for (var boneEnum = (HumanBodyBones)0; boneEnum < HumanBodyBones.LastBone; boneEnum++) {
            var bone = animator.GetBoneTransform(boneEnum);
            if (bone == null) continue;
            bone.GetPositionAndRotation(out var position, out var rotation);
            bool selected = selectedBones.Contains(boneEnum);
            var evt = Event.current;
            if (!selected || evt.shift) {
                Handles.color = selected ? Handles.selectedColor : Handles.centerColor;
                float handleSize = HandleUtility.GetHandleSize(position) * 0.1f;
                if (Handles.Button(position, Quaternion.identity, handleSize, handleSize, Handles.SphereHandleCap)) {
                    if (evt.shift) {
                        if (selectedBones.Add(boneEnum))
                            selected = true;
                        else if(selectedBones.Remove(boneEnum))
                            selected = false;
                    } else {
                        selectedBones.Clear();
                        selectedBones.Add(boneEnum);
                        selected = true;
                    }
                }
            }
            if (!selected) continue;
            int humanId = (int)boneEnum;
            Handles.BeginGUI();
            var screenPos = HandleUtility.WorldToGUIPoint(position);
            var label = GetTempContent(boneNames[humanId]);
            var size = GUI.skin.label.CalcSize(label);
            GUI.color = Handles.centerColor;
            GUI.Label(new Rect(screenPos.x - size.x / 2, screenPos.y - size.y / 2, size.x, size.y), label);
            Handles.EndGUI();
            if (boneEnum == HumanBodyBones.Hips) {
                HandleRootBone(bone, position, rotation);
                continue;
            }
            var parentRotation = bone.parent.rotation;
            Vector3 sign = _avatar.GetLimitSign(humanId);
            for (int axis = 0; axis < 3; axis++)
                try {
                    HandleMuscle(humanDescription, humanId, axis, position, parentRotation, rotation, _avatar, sign);
                } catch (Exception ex) {
                    Debug.LogException(ex);
                }
        }
    }

    void HandleRootBone(Transform bone, Vector3 position, Quaternion rotation) {
        var newPosition = position;
        var newRotation = rotation;
        bool positionChanged = false, rotationChanged = false;
        using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
            newPosition = Handles.DoPositionHandle(position, rotation);
            positionChanged = changeCheck.changed;
        }
        using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
            newRotation = Handles.DoRotationHandle(rotation, position);
            rotationChanged = changeCheck.changed;
        }
        if (positionChanged || rotationChanged) {
            if (!isEditingAnimationClip) Undo.RecordObject(bone, UNDO_MESSAGE);
            bone.SetPositionAndRotation(newPosition, newRotation);
            pose.bodyPosition = newPosition - position + pose.bodyPosition;
            pose.bodyRotation = newRotation * Quaternion.Inverse(rotation) * pose.bodyRotation;
            if (!isEditingAnimationClip) return;
            bone.SetPositionAndRotation(position, rotation);
            try {
                using (var modifyKey = new KeyModificationScope(controlInterface)) {
                    if (positionChanged) {
                        AddRootFloatKey(modifyKey.state, "RootT.x", pose.bodyPosition.x);
                        AddRootFloatKey(modifyKey.state, "RootT.y", pose.bodyPosition.y);
                        AddRootFloatKey(modifyKey.state, "RootT.z", pose.bodyPosition.z);
                    }
                    if (rotationChanged) {
                        AddRootFloatKey(modifyKey.state, "RootQ.x", pose.bodyRotation.x);
                        AddRootFloatKey(modifyKey.state, "RootQ.y", pose.bodyRotation.y);
                        AddRootFloatKey(modifyKey.state, "RootQ.z", pose.bodyRotation.z);
                        AddRootFloatKey(modifyKey.state, "RootQ.w", pose.bodyRotation.w);
                    }
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
        }
    }

    void HandleMuscle(HumanDescription humanDescription, int humanId, int axis, Vector3 position, Quaternion parentRotation, Quaternion rotation, dynamic avatar, Vector3 sign) {
        if (!TryGetMuscleLimits(humanDescription, humanId, axis, out var muscle, out var min, out var max)) return;
        var range = (max - min) * 0.5f;
        var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), musclePropertyNames[muscle]);
        float value = 0;
        bool hasCurve = false;
        if (isEditingAnimationClip) {
            var curve = AnimationUtility.GetEditorCurve(activeClip, binding);
            if (curve != null) {
                value = curve.Evaluate(currentTime);
                hasCurve = true;
            }
        }
        if (!hasCurve) value = pose.muscles[muscle];
        float newValue = value;
        using (var changeCheck = new EditorGUI.ChangeCheckScope()) {
            Color color = default;
            Vector3 vector = default;
            Quaternion q = default;
            switch (axis) {
                case 0:
                    color = Handles.xAxisColor;
                    q = rotation * (Quaternion)avatar.GetPostRotation(humanId);
                    break;
                case 1:
                    color = Handles.yAxisColor;
                    q = avatar.GetZYPostQ(humanId, parentRotation, rotation);
                    break;
                case 2:
                    color = Handles.zAxisColor;
                    q = parentRotation * (Quaternion)avatar.GetPreRotation(humanId);
                    break;
            }
            vector[axis] = sign[axis];
            newValue = DoDiscHandle(color, position, q * vector, value * range) / range;
            if (!changeCheck.changed) return;
        }
        if (isEditingAnimationClip) {
            SetAnimationKey(binding, value, newValue);
            if (selectedBones.Count > 1) {
                foreach (var bone in selectedBones) {
                    if (bone == (HumanBodyBones)humanId) continue;
                    int otherMuscle = HumanTrait.MuscleFromBone((int)bone, axis);
                    if (otherMuscle < 0) continue;
                    var otherBinding = EditorCurveBinding.FloatCurve("", typeof(Animator), musclePropertyNames[otherMuscle]);
                    var otherCurve = AnimationUtility.GetEditorCurve(activeClip, otherBinding);
                    float otherValue;
                    if (otherCurve != null)
                        otherValue = otherCurve.Evaluate(currentTime);
                    else
                        otherValue = pose.muscles[otherMuscle];
                    SetAnimationKey(otherBinding, otherValue, newValue);
                }
            }
        } else {
            var animator = target as Animator;
            for (var boneEnum = (HumanBodyBones)0; boneEnum < HumanBodyBones.LastBone; boneEnum++) {
                var bone = animator.GetBoneTransform(boneEnum);
                if (bone != null) Undo.RecordObject(bone, UNDO_MESSAGE);
            }
            pose.muscles[muscle] = newValue;
            if (selectedBones.Count > 1) {
                foreach (var bone in selectedBones) {
                    if (bone == (HumanBodyBones)humanId) continue;
                    int otherMuscle = HumanTrait.MuscleFromBone((int)bone, axis);
                    if (otherMuscle < 0) continue;
                    pose.muscles[otherMuscle] = newValue;
                }
            }
            poseHandler.SetHumanPose(ref pose);
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }
    }

    void SetAnimationKey(EditorCurveBinding binding, float value, float newValue) {
        using (var modifyKey = new KeyModificationScope(controlInterface))
            AnimationRecording.AddKey(modifyKey.state, binding, typeof(float), value, newValue);
    }

    readonly struct KeyModificationScope : IDisposable {
        public readonly dynamic controlInterface;
        public readonly dynamic state;

        public KeyModificationScope(dynamic controlInterface) {
            this.controlInterface = controlInterface;
            state = Construct(controlInterface.RecordingState, animWindowState, controlInterface.RecordingStateMode.AutoKey);
            controlInterface.BeginKeyModification();
        }

        public void Dispose() {
            controlInterface.EndKeyModification();
            controlInterface.ResampleAnimation();
        }
    }
}
