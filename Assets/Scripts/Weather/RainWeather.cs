using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>读取父物体的天气，用循环复用的窄 Capsule 在相机视野内表现斜雨。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Weather/Rain Weather")]
[DefaultExecutionOrder(1100)]
public class RainWeather : MonoBehaviour
{
    [Header("引用（留空自动查找）")]
    [SerializeField, Tooltip("WeatherObject 上的 WeatherBase；留空时查找父物体。")]
    private WeatherBase weather;
    [SerializeField, Tooltip("显示雨水的相机；留空时使用 MainCamera。")]
    private Camera targetCamera;

    [Header("雨滴")]
    [SerializeField, Range(-80f, 80f), InspectorName("倾斜角度"), Tooltip("相对画面竖直方向的角度。0 为竖直下落，正数向右下，负数向左下。")]
    private float tiltAngle = 18f;
    [SerializeField, Min(0f), InspectorName("下落速度"), Tooltip("沿雨滴倾斜方向移动的速度，单位为世界单位/秒。")]
    private float fallSpeed = 8f;
    [SerializeField, Min(0.001f), InspectorName("雨滴宽度")]
    private float dropWidth = 0.025f;
    [SerializeField, Min(0.001f), InspectorName("雨滴长度")]
    private float dropLength = 0.35f;
    [SerializeField, Range(0, 1000), InspectorName("密集程度"), Tooltip("视野及边缘缓冲区内的雨滴数量。0 为不显示；调整时复用已创建的雨滴。")]
    private int density = 160;
    [SerializeField, InspectorName("雨滴颜色")]
    private Color rainColor = new Color(0.7f, 0.85f, 1f, 0.45f);

    [Header("显示")]
    [SerializeField, Min(0.1f), InspectorName("距离相机"), Tooltip("雨幕距相机的距离。雨幕随相机移动，支持透视和正交相机。")]
    private float cameraDistance = 5f;
    [SerializeField, InspectorName("忽略时间缩放"), Tooltip("勾选后，Time.timeScale 为 0 时雨滴仍然移动。")]
    private bool useUnscaledTime;

    private sealed class Drop
    {
        public Transform Transform;
        public Vector2 ViewportPosition;
    }

    private readonly List<Drop> drops = new List<Drop>();
    // 视觉随机数不消耗 UnityEngine.Random，避免雨滴影响植物扩张等游戏规则的随机序列。
    private readonly System.Random random = new System.Random();
    private GameObject rainRoot;
    private Mesh capsuleMesh;
    private Material rainMaterial;
    private int activeCount;
    private bool wasRaining;
    private bool warnedMissingShader;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Reset()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (weather == null)
            weather = GetComponentInParent<WeatherBase>();
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void LateUpdate()
    {
        ResolveReferences();
        int count = Mathf.Clamp(density, 0, 1000);
        if (weather == null || weather.weatherType != E_WeatherType.Rain
            || targetCamera == null || !targetCamera.isActiveAndEnabled || count == 0)
        {
            HideRain();
            return;
        }

        float width = Mathf.Max(0.001f, dropWidth);
        float length = Mathf.Max(width, dropLength);
        float clipPadding = width + 0.01f;
        float minDepth = targetCamera.nearClipPlane + clipPadding;
        float maxDepth = targetCamera.farClipPlane - clipPadding;
        if (maxDepth <= minDepth)
        {
            HideRain();
            return;
        }

        float depth = Mathf.Clamp(cameraDistance, minDepth, maxDepth);
        Vector3 bottomLeft = targetCamera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 right = targetCamera.ViewportToWorldPoint(new Vector3(1f, 0f, depth)) - bottomLeft;
        Vector3 up = targetCamera.ViewportToWorldPoint(new Vector3(0f, 1f, depth)) - bottomLeft;
        float viewWidth = right.magnitude;
        float viewHeight = up.magnitude;
        if (viewWidth < 0.0001f || viewHeight < 0.0001f || !EnsureResources())
        {
            HideRain();
            return;
        }

        // 在边缘之外循环，保证长雨滴完整离开画面后才回到另一端。
        Vector2 padding = new Vector2(length / viewWidth, length / viewHeight);
        EnsureDropCount(count, padding);
        if (!wasRaining)
        {
            for (int i = 0; i < count; i++)
                drops[i].ViewportPosition = RandomPosition(padding);
        }

        rainRoot.SetActive(true);
        rainMaterial.SetColor(ColorId, rainColor);
        float angle = Mathf.Clamp(tiltAngle, -80f, 80f);
        float radians = angle * Mathf.Deg2Rad;
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        Vector2 step = new Vector2(Mathf.Sin(radians) / viewWidth, -Mathf.Cos(radians) / viewHeight)
            * (Mathf.Max(0f, fallSpeed) * deltaTime);
        Quaternion rotation = targetCamera.transform.rotation * Quaternion.Euler(0f, 0f, angle);
        // Unity 的 Capsule 原始高度为 2，直径为 1。
        Vector3 scale = new Vector3(width, length * 0.5f, width);
        for (int i = 0; i < count; i++)
        {
            Drop drop = drops[i];
            Vector2 point = drop.ViewportPosition + step;
            point.x = Mathf.Repeat(point.x + padding.x, 1f + padding.x * 2f) - padding.x;
            point.y = Mathf.Repeat(point.y + padding.y, 1f + padding.y * 2f) - padding.y;
            drop.ViewportPosition = point;
            drop.Transform.SetPositionAndRotation(bottomLeft + right * point.x + up * point.y, rotation);
            drop.Transform.localScale = scale;
            drop.Transform.gameObject.layer = gameObject.layer;
        }
        wasRaining = true;
    }

    private bool EnsureResources()
    {
        if (rainMaterial == null)
        {
            // Resources 中的着色器随游戏一起打包，避免只在编辑器中能找到 Shader。
            Shader shader = Resources.Load<Shader>("Weather/RainCapsule");
            if (shader == null)
            {
                if (!warnedMissingShader)
                    Debug.LogError("RainWeather：缺少 Resources/Weather/RainCapsule.shader。", this);
                warnedMissingShader = true;
                return false;
            }
            rainMaterial = new Material(shader) { name = "Rain Capsule (Runtime)", hideFlags = HideFlags.DontSave };
            rainMaterial.enableInstancing = true;
        }

        if (rainRoot == null)
        {
            // 独立根节点保持单位缩放，不受 WeatherObject 的位置、旋转和缩放影响。
            rainRoot = new GameObject("Rain Capsules (Runtime)") { hideFlags = HideFlags.DontSave };
            rainRoot.SetActive(false);
        }

        if (capsuleMesh == null)
        {
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            template.SetActive(false);
            capsuleMesh = template.GetComponent<MeshFilter>().sharedMesh;
            CapsuleCollider collider = template.GetComponent<CapsuleCollider>();
            if (collider != null)
                collider.enabled = false;
            Destroy(template);
        }
        return true;
    }

    private void EnsureDropCount(int count, Vector2 padding)
    {
        while (drops.Count < count)
        {
            var go = new GameObject("Rain Capsule", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(rainRoot.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = capsuleMesh;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = rainMaterial;
            renderer.sortingOrder = 32767;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            drops.Add(new Drop { Transform = go.transform, ViewportPosition = RandomPosition(padding) });
        }
        for (int i = activeCount; i < count; i++)
        {
            drops[i].ViewportPosition = RandomPosition(padding);
            drops[i].Transform.gameObject.SetActive(true);
        }
        for (int i = count; i < activeCount; i++)
            drops[i].Transform.gameObject.SetActive(false);
        activeCount = count;
    }

    private Vector2 RandomPosition(Vector2 padding)
    {
        return new Vector2((float)random.NextDouble() * (1f + 2f * padding.x) - padding.x,
            (float)random.NextDouble() * (1f + 2f * padding.y) - padding.y);
    }

    private void HideRain()
    {
        if (rainRoot != null && rainRoot.activeSelf)
            rainRoot.SetActive(false);
        wasRaining = false;
    }

    private void OnDisable()
    {
        HideRain();
    }

    private void OnDestroy()
    {
        if (rainRoot != null)
            Destroy(rainRoot);
        if (rainMaterial != null)
            Destroy(rainMaterial);
    }
}
