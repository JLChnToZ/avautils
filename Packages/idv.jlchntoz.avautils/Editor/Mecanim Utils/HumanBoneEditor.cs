using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;

using static JLChnToZ.CommonUtils.Dynamic.Limitless;

[EditorTool(NAME, typeof(Animator))]
public class HumanBoneEditor : EditorTool {
    const string NAME = "Human Bone Editor";
    static readonly dynamic AnimationRecording = Static("UnityEditorInternal.AnimationRecording, UnityEditor");
    static readonly dynamic AnimationWindow = Static("UnityEditor.AnimationWindow, UnityEditor");
    static string[] musclePropertyNames;
    static string[] boneNames;
    static GUIContent icon, tempContent;
    static EditorWindow animationEditorWindow;
    static dynamic animWindowState, controlInterface;
    HumanPoseHandler poseHandler;
    HumanPose pose;
    HumanBodyBones selectedBone = HumanBodyBones.LastBone;
    AnimationClip activeClip;
    float currentTime;
    bool notAvailableMessageShown, invalidAvatarMessageShown;

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
        if (boneNames == null) boneNames = HumanTrait.BoneName;
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

    float GetFloatValue(EditorCurveBinding binding, float defaultValue = 0) {
        var curve = AnimationUtility.GetEditorCurve(activeClip, binding);
        if (curve != null) return curve.Evaluate(currentTime);
        return defaultValue;
    }

    void OnDestroy() {
        if (poseHandler != null) poseHandler.Dispose();
    }

    public override void OnWillBeDeactivated() {
        notAvailableMessageShown = false;
        invalidAvatarMessageShown = false;
    }

    public override void OnToolGUI(EditorWindow window) {
        if (!TryGetCurrentRecordingAnimationClip(out activeClip, out currentTime)) {
            if (!notAvailableMessageShown) {
                window.ShowNotification(GetTempContent("Start recording in Animation Window to use this tool"));
                notAvailableMessageShown = true;
            }
            return;
        }
        var animator = target as Animator;
        if (animator == null || !animator.gameObject.activeInHierarchy) return;
        var avatar = animator.avatar;
        if (avatar == null || !avatar.isValid || !avatar.isHuman) {
            if (!invalidAvatarMessageShown) {
                window.ShowNotification(GetTempContent("Avatar is not valid or not human"));
                invalidAvatarMessageShown = true;
            }
            return;
        }
        notAvailableMessageShown = false;
        invalidAvatarMessageShown = false;
        Init();
        var humanDescription = avatar.humanDescription;
        var _avatar = Wrap(avatar);
        Quaternion[] rotations = new Quaternion[3];
        for (var boneEnum = (HumanBodyBones)0; boneEnum < HumanBodyBones.LastBone; boneEnum++) {
            var bone = animator.GetBoneTransform(boneEnum);
            if (bone == null) continue;
#if UNITY_2021_3_OR_NEWER
            bone.GetPositionAndRotation(out var position, out var rotation);
#else
            var position = bone.position;
            var rotation = bone.rotation;
#endif
            Handles.color = selectedBone == boneEnum ? Handles.selectedColor : Handles.centerColor;
            if (selectedBone != boneEnum) {
                float handleSize = HandleUtility.GetHandleSize(position) * 0.1f;
                if (Handles.Button(position, Quaternion.identity, handleSize, handleSize, Handles.SphereHandleCap))
                    selectedBone = boneEnum;
                else
                    continue;
            }
            int humanId = (int)boneEnum;
            Handles.BeginGUI();
            var screenPos = HandleUtility.WorldToGUIPoint(position);
            var label = GetTempContent(boneNames[humanId]);
            var size = GUI.skin.label.CalcSize(label);
            GUI.color = Handles.centerColor;
            GUI.Label(new Rect(screenPos.x - size.x / 2, screenPos.y - size.y / 2, size.x, size.y), label);
            Handles.EndGUI();
            if (boneEnum == HumanBodyBones.Hips) {
                HandleRootBone(animator, avatar, bone, position, rotation);
                continue;
            }
            var parentRotation = bone.parent.rotation;
            var sign = (Vector3)_avatar.GetLimitSign(humanId);
            for (int axis = 0; axis < 3; axis++)
                try {
                    HandleMuscle(humanDescription, humanId, axis, position, parentRotation, rotation, _avatar, sign);
                } catch (Exception ex) {
                    Debug.LogException(ex);
                }
        }
    }

    void HandleRootBone(Animator animator, Avatar avatar, Transform bone, Vector3 position, Quaternion rotation) {
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
            if (poseHandler == null) poseHandler = new HumanPoseHandler(avatar, animator.transform);
            bone.SetPositionAndRotation(newPosition, newRotation);
            poseHandler.GetHumanPose(ref pose);
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
        int muscle = HumanTrait.MuscleFromBone(humanId, axis);
        if (muscle < 0) return;
        float min, max, range;
        var humanLimits = humanDescription.human;
        if (humanId >= humanLimits.Length) return;
        var limit = humanLimits[humanId].limit;
        if (limit.useDefaultValues) {
            min = HumanTrait.GetMuscleDefaultMin(muscle);
            max = HumanTrait.GetMuscleDefaultMax(muscle);
        } else {
            min = limit.min[axis];
            max = limit.max[axis];
        }
        range = (max - min) * 0.5f;
        var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), musclePropertyNames[muscle]);
        float value = GetFloatValue(binding, 0);
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
