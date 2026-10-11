using UnityEngine;

/// <summary>让当前所有直接子物体朝向主相机，支持运行时投放和移除物种。</summary>
public class FacingCamera : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;

        // 相机移动后再同步；直接遍历当前层级，避免新增子物体导致旧缓存越界。
        Quaternion rotation = camera.transform.rotation;
        foreach (Transform child in transform)
            child.rotation = rotation;
    }
}
