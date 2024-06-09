# Ava Utils

This is a set of utilities that I programmed myself to help manually manipulate VRChat avatars for daily basis. Some tools might not stable enough for use and those might not resolves your problem, use at your own risk!

This includes following tools:

- Under Tools -> JLChnToZ menu:
  - Bone Visualizer: Visualize humanoid avatars/skinned mesh renderer bound bone transforms
  - Bone Remapper: First-aids the skinned mesh renderer bone mapping by copying from its original prefab.
  - Advanced Fitting Room: An experimental tool to wear clothes to an avatar by merge/re-parent bones by rules.
  - Humanoid Avatar Builder: (Re)builds humanoid avatar, suitable after bone renaming or position adjusting and want to set as initial pose.
  - Mesh UV Utility: Manually scales mesh UV to fit in texture atlas, intended for use in combination with skinned mesh combiner.
  - Normalize Armature: An experimental tool attempts to fix cross leg issue.
  - Reset Armature to T-Pose: Resets the armature to T-Pose.
  - Skinned Mesh Combiner: Combine (skinned/non-skinned) meshes into one, while conditionally select whether blendshapes/specified bones/polygons to be kept or merged or removed. Originally this is a standlone tool but I want to maintain it here in the future.
  - Animation Hierarchy Editor: You can batch modify animation driven object paths by selecting animation clips/animators/animator controllers, also supports records path changes on the fly. Note that this is a modified version that has been re-licensed, which originally released under public domain.
  - Move Phys Bones: Move all PhysBone components under where a skinned mesh requires them, or "physbone" object if multiple or none of skinned meshes references it. Useful for auto suspends them when a clothes mesh hides.
  - Remove Unused PhysBone Colliders: Cleans up PhyBone colliders which no longer has PhysBone references them.
- Under context menu on components:
  - Skinned Mesh Renderer
    - Copy Bone References: Copy bone references to the skinned mesh renderer
    - Paste Bone Reference: Paste the bone references recently copied from other skinned mesh renderer.
    - Edit Bone References: Open bone editor to edit bone mapping for selected skinned mesh renderer.
  - Animator
    - (Re-)Build Humanoid Avatar: Short-cut for Humanoid Avatar Builder
  - State Machine Behaviour
    - Copy State Machine Behaviour: Copy selected state machine behaviour properties
    - Paste State Machine Behaviour Values: paste state machine behaviour properties
    - Paste State Machine Behaviour As New: create a new state machine behaviour at selected animation state/state machine with copied values

# Installation

You can install via [VCC](https://xtlcdn.github.io/vpm/).

# LICENSE

[MIT](LICENSE)