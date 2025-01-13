using UnityEngine;

public static class UnityPolyfill {
#if !UNITY_2019_2_OR_NEWER
    public static bool TryGetComponent<T>(this GameObject gameObject, out T target) where T : Component {
        target = gameObject.GetComponent<T>();
        return target;
    }

    public static bool TryGetComponent<T>(this Component component, out T target) where T : Component {
        target = component.GetComponent<T>();
        return target;
    }
#endif
#if !UNITY_2021_3_OR_NEWER
    public static void GetLocalPositionAndRotation(this Transform transform, out Vector3 position, out Quaternion rotation) {
        position = transform.localPosition;
        rotation = transform.localRotation;
    }

    public static void GetPositionAndRotation(this Transform transform, out Vector3 position, out Quaternion rotation) {
        position = transform.position;
        rotation = transform.rotation;
    }

    public static void SetLocalPositionAndRotation(this Transform transform, in Vector3 position, in Quaternion rotation) {
        transform.localPosition = position;
        transform.localRotation = rotation;
    }
#endif
}