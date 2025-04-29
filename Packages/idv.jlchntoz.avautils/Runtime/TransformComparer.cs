using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Compares two <see cref="Transform" /> based on their hierarchy and position in the scene.
/// </summary>
public class TransformComparer : IComparer<Transform> {
    readonly Dictionary<Transform, double> scores = new Dictionary<Transform, double>();
    readonly Mode mode;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransformComparer"/> class.
    /// </summary>
    /// <param name="mode">The mode of comparison.</param>
    public TransformComparer(Mode mode) {
        this.mode = mode;
    }

    double GetScore(Transform transform) {
        if (transform == null) return 0;
        if (scores.TryGetValue(transform, out var score)) return score;
        int depth = 0;
        for (var t = transform; t != null; t = t.parent) {
            depth++;
            score /= transform.childCount + 1;
            score += transform.GetSiblingIndex() + 1;
        }
        var scene = transform.gameObject.scene;
        if (scene.IsValid()) score /= scene.rootCount;
        if (mode == Mode.DepthFirst) score += depth;
        scores[transform] = score;
        return score;
    }

    /// <summary>
    /// Compares two transforms based on their hierarchy and position in the scene.
    /// </summary>
    /// <param name="x"> The first transform to compare.</param>
    /// <param name="y"> The second transform to compare.</param>
    /// <returns>
    /// A negative value if <paramref name="x"/> is less than <paramref name="y"/>;
    /// a possitive value if <paramref name="x"/> is greater than <paramref name="y"/>;
    /// and zero if they are equal.
    /// </returns>
    public int Compare(Transform x, Transform y) {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        return GetScore(x).CompareTo(GetScore(y));
    }

    /// <summary>
    /// Resets the comparer, clearing any cached scores.
    /// </summary>
    public void Reset() {
        scores.Clear();
    }

    /// <summary>
    /// The mode of comparison for the transform comparer.
    /// </summary>
    public enum Mode {
        /// <summary>
        /// Compares siblings first, then depth.
        /// </summary>
        SiblingFirst,
        /// <summary>
        /// Compares depth first, then siblings.
        /// </summary>
        DepthFirst,
    }
}