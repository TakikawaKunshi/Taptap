using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>在自由缩放后修正生态精灵的裁剪与排序，不改变相机位置、角度或缩放。</summary>
[RequireComponent(typeof(Camera)), DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class EcosystemCameraVisibility : MonoBehaviour
{
    [SerializeField, Tooltip("植物、动物及余屑外观的共同父物体。")]
    private Transform ecosystemRoot;
    [SerializeField, Tooltip("生态箱地面的 Renderer，生物精灵会绘制在它前面。")]
    private Renderer groundRenderer;
    [SerializeField, Min(0.001f)] private float minimumNearClip = 0.01f;
    [SerializeField, Min(0f)] private float clipPadding = 0.1f;
    [SerializeField] private bool keepSpritesAboveGround = true;
    [SerializeField, Tooltip("精灵按此世界轴排序，避免透视距离随缩放变化造成前后跳变。")]
    private Vector3 sortingAxis = Vector3.up;

    private struct GroupState
    {
        public SortingGroup Group;
        public bool Added;
        public int Layer;
        public int Order;
    }

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<SpriteRenderer> sprites = new List<SpriteRenderer>();
    private readonly Dictionary<Transform, GroupState> groups = new Dictionary<Transform, GroupState>();
    private readonly List<Transform> gone = new List<Transform>();
    private Camera targetCamera;
    private float originalNear;
    private float originalFar;
    private int originalMask;
    private bool originalOcclusion;
    private float[] originalDistances;
    private float[] distances;
    private TransparencySortMode originalSortMode;
    private Vector3 originalSortAxis;

    private void OnEnable()
    {
        targetCamera = GetComponent<Camera>();
        originalNear = targetCamera.nearClipPlane;
        originalFar = targetCamera.farClipPlane;
        originalMask = targetCamera.cullingMask;
        originalOcclusion = targetCamera.useOcclusionCulling;
        originalDistances = targetCamera.layerCullDistances;
        distances = (float[])originalDistances.Clone();
        originalSortMode = targetCamera.transparencySortMode;
        originalSortAxis = targetCamera.transparencySortAxis;
        RenderPipelineManager.beginCameraRendering += BeforeRender;
    }

    private void LateUpdate() => RefreshNow();
    private void OnPreCull() => RefreshNow();
    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == targetCamera) RefreshNow();
    }

    /// <summary>供相机切换和验证使用；在 Billboard 旋转完成后读取实际包围盒。</summary>
    public void RefreshNow()
    {
        if (!isActiveAndEnabled || targetCamera == null || ecosystemRoot == null) return;
        targetCamera.useOcclusionCulling = false;
        targetCamera.transparencySortMode = TransparencySortMode.CustomAxis;
        targetCamera.transparencySortAxis = sortingAxis.sqrMagnitude > 0f ? sortingAxis.normalized : Vector3.up;
        if (keepSpritesAboveGround && groundRenderer != null) UpdateSorting();

        ecosystemRoot.GetComponentsInChildren(false, renderers);
        float nearest = originalNear;
        float farthest = Mathf.Max(originalFar, targetCamera.farClipPlane);
        Vector3 forward = targetCamera.transform.forward;
        Vector3 origin = targetCamera.transform.position;
        Vector3 absoluteForward = new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z));
        int mask = targetCamera.cullingMask;
        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled || renderer.forceRenderingOff) continue;
            Bounds bounds = renderer.bounds;
            float centerDepth = Vector3.Dot(bounds.center - origin, forward);
            float radius = Vector3.Dot(bounds.extents, absoluteForward);
            if (centerDepth + radius <= 0f) continue;
            nearest = Mathf.Min(nearest, centerDepth - radius - clipPadding);
            farthest = Mathf.Max(farthest, centerDepth + radius + clipPadding);
            int layer = renderer.gameObject.layer;
            mask |= 1 << layer;
            distances[layer] = 0f; // 该层使用修正后的远裁剪面，不另设距离剔除。
        }
        targetCamera.nearClipPlane = Mathf.Max(Mathf.Min(minimumNearClip, originalNear), nearest);
        targetCamera.farClipPlane = Mathf.Max(targetCamera.nearClipPlane + 0.01f, farthest);
        targetCamera.cullingMask = mask;
        targetCamera.layerCullDistances = distances;
    }

    private void UpdateSorting()
    {
        gone.Clear();
        foreach (var pair in groups)
            if (pair.Key == null || pair.Key.parent != ecosystemRoot) gone.Add(pair.Key);
        foreach (Transform root in gone)
        {
            RestoreGroup(groups[root]);
            groups.Remove(root);
        }
        foreach (Transform root in ecosystemRoot)
        {
            if (!root.gameObject.activeInHierarchy) continue;
            if (!groups.TryGetValue(root, out GroupState state))
            {
                root.GetComponentsInChildren(false, sprites);
                if (sprites.Count == 0) continue;
                SortingGroup group = root.GetComponent<SortingGroup>();
                bool added = group == null;
                if (added) group = root.gameObject.AddComponent<SortingGroup>();
                state = new GroupState { Group = group, Added = added,
                    Layer = added ? sprites[0].sortingLayerID : group.sortingLayerID,
                    Order = added ? sprites[0].sortingOrder : group.sortingOrder };
                groups.Add(root, state);
            }
            if (state.Group == null) continue;
            int layer = state.Layer;
            int order = state.Order;
            if (SortingLayer.GetLayerValueFromID(layer) < SortingLayer.GetLayerValueFromID(groundRenderer.sortingLayerID))
            {
                layer = groundRenderer.sortingLayerID;
                order = groundRenderer.sortingOrder + 1;
            }
            else if (layer == groundRenderer.sortingLayerID)
                order = Mathf.Max(order, groundRenderer.sortingOrder + 1);
            state.Group.sortingLayerID = layer;
            state.Group.sortingOrder = Mathf.Clamp(order, -32768, 32767);
        }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeRender;
        if (targetCamera != null)
        {
            targetCamera.nearClipPlane = originalNear;
            targetCamera.farClipPlane = originalFar;
            targetCamera.cullingMask = originalMask;
            targetCamera.layerCullDistances = originalDistances;
            targetCamera.useOcclusionCulling = originalOcclusion;
            targetCamera.transparencySortMode = originalSortMode;
            targetCamera.transparencySortAxis = originalSortAxis;
        }
        foreach (GroupState state in groups.Values) RestoreGroup(state);
        groups.Clear();
    }

    private void RestoreGroup(GroupState state)
    {
        if (state.Group == null) return;
        if (state.Added)
        {
            state.Group.enabled = false;
            if (Application.isPlaying) Destroy(state.Group);
            else DestroyImmediate(state.Group);
        }
        else
        {
            state.Group.sortingLayerID = state.Layer;
            state.Group.sortingOrder = state.Order;
        }
    }
}
