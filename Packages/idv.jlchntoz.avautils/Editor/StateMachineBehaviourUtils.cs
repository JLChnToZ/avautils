using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public static class StateMachineBehaviourUtils {
    const string BEHAVIOUR_PATH = "CONTEXT/StateMachineBehaviour/";
    const string STATE_PATH = "CONTEXT/AnimatorState/";
    const string STATE_MACHINE_PATH = "CONTEXT/AnimatorStateMachine/";
    const string COPY = "Copy State Machine Behaviour";
    const string PASTE = "Paste State Machine Behaviour Values";
    const string PASTE_NEW = "Paste State Machine Behaviour As New";
    const int DEFAULT_PRIORITY = 1000;

    static StateMachineBehaviour copiedStateMachineBehaviour;

    [MenuItem(BEHAVIOUR_PATH + COPY, priority = DEFAULT_PRIORITY)]
    static void CopyStateMachineBehaviour(MenuCommand command) {
        copiedStateMachineBehaviour = command.context as StateMachineBehaviour;
    }

    [MenuItem(BEHAVIOUR_PATH + PASTE, priority = DEFAULT_PRIORITY)]
    static void PasteStateMachineBehaviour(MenuCommand command) {
        var stateMachineBehaviour = command.context as StateMachineBehaviour;
        if (copiedStateMachineBehaviour != null && stateMachineBehaviour.GetType() == copiedStateMachineBehaviour.GetType())
            EditorUtility.CopySerialized(copiedStateMachineBehaviour, stateMachineBehaviour);
    }

    [MenuItem(BEHAVIOUR_PATH + PASTE_NEW, priority = DEFAULT_PRIORITY)]
    [MenuItem(STATE_PATH + PASTE_NEW, priority = DEFAULT_PRIORITY)]
    [MenuItem(STATE_MACHINE_PATH + PASTE_NEW, priority = DEFAULT_PRIORITY)]
    static void PasteStateMachineBehaviourAsNew(MenuCommand _) {
        if (copiedStateMachineBehaviour == null) return;
        var selectedStates = Selection.GetFiltered<AnimatorState>(SelectionMode.Unfiltered);
        var type = copiedStateMachineBehaviour.GetType();
        foreach (var state in selectedStates) {
            var newBehaviour = state.AddStateMachineBehaviour(type);
            if (newBehaviour != null) EditorUtility.CopySerialized(copiedStateMachineBehaviour, newBehaviour);
        }
        var selectedStateMachines = Selection.GetFiltered<AnimatorStateMachine>(SelectionMode.Unfiltered);
        foreach (var stateMachine in selectedStateMachines) {
            var newBehaviour = stateMachine.AddStateMachineBehaviour(type);
            if (newBehaviour != null) EditorUtility.CopySerialized(copiedStateMachineBehaviour, newBehaviour);
        }
    }

    [MenuItem(BEHAVIOUR_PATH + PASTE_NEW, true)]
    [MenuItem(STATE_PATH + PASTE_NEW, true)]
    [MenuItem(STATE_MACHINE_PATH + PASTE_NEW, true)]
    static bool CanPaste() => copiedStateMachineBehaviour != null;


    [MenuItem(BEHAVIOUR_PATH + PASTE, true)]
    static bool CanPaste(MenuCommand command) =>
        copiedStateMachineBehaviour != null &&
        command.context.GetType() == copiedStateMachineBehaviour.GetType();
}
