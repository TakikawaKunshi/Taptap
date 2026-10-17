using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>通过外观的渲染范围选取生物，支持子物体上的精灵，不要求预设体包含碰撞体。</summary>
public static class SpeciesVisualPicker
{
    private struct Hit
    {
        public Component Target;
        public int Layer;
        public int Order;
        public float Distance;
    }

    public static Component Pick(TileMapManager map, Camera camera, Vector2 screenPosition)
    {
        Ray ray = camera.ScreenPointToRay(screenPosition);
        var best = new Hit();
        foreach (PlantVisual visual in Object.FindObjectsOfType<PlantVisual>())
        {
            PlantsBase plant = visual.Plant;
            if (!visual.isActiveAndEnabled || plant == null || plant.IsDead)
                continue;
            PlantVisualBinder binder = visual.GetComponentInParent<PlantVisualBinder>();
            if (binder == null || binder.Map != map || !map.TryGetCell(plant.Coordinates, out TileCell cell))
                continue;
            if (cell.Subject != plant && !Contains(cell.Cover, plant))
                continue;
            Consider(visual, camera, ray, ref best);
        }
        foreach (AnimalVisual visual in Object.FindObjectsOfType<AnimalVisual>())
        {
            AnimalsBase animal = visual.Animal;
            if (!visual.isActiveAndEnabled || animal == null || animal.IsDead
                || !(animal.CurrentCell is TileCell cell) || cell.Manager != map)
                continue;
            AnimalVisualBinder binder = visual.GetComponentInParent<AnimalVisualBinder>();
            if (binder == null || binder.Spawner == null || binder.Spawner.Map != map
                || !Contains(binder.Spawner.Animals, animal))
                continue;
            Consider(visual, camera, ray, ref best);
        }
        return best.Target;
    }

    private static bool Contains<T>(IReadOnlyList<T> list, T target) where T : class
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], target))
                return true;
        return false;
    }

    private static void Consider(Component visual, Camera camera, Ray ray, ref Hit best)
    {
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer.forceRenderingOff
                || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                || !TryHit(renderer, ray, out Vector3 point))
                continue;

            float depth = camera.WorldToViewportPoint(point).z;
            if (depth < camera.nearClipPlane || depth > camera.farClipPlane)
                continue;

            int layer = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
            int order = renderer.sortingOrder;
            // 用户在预设体根节点设置 SortingGroup 时，以整组的绘制顺序比较。
            foreach (SortingGroup group in renderer.GetComponentsInParent<SortingGroup>())
            {
                if (group.enabled)
                {
                    layer = SortingLayer.GetLayerValueFromID(group.sortingLayerID);
                    order = group.sortingOrder;
                }
            }
            float distance = Vector3.Dot(point - ray.origin, ray.direction);
            if (best.Target != null && (layer < best.Layer
                || (layer == best.Layer && order < best.Order)
                || (layer == best.Layer && order == best.Order && distance >= best.Distance)))
                continue;

            best = new Hit { Target = visual, Layer = layer, Order = order, Distance = distance };
        }
    }

    private static bool TryHit(Renderer renderer, Ray ray, out Vector3 point)
    {
        point = default;
        if (renderer is SpriteRenderer sprite)
        {
            if (sprite.sprite == null || sprite.color.a <= 0f)
                return false;
            // 在精灵自身的平面内判断范围，避免倾斜/旋转后的世界轴对齐包围盒误选。
            Transform transform = sprite.transform;
            Ray localRay = new Ray(transform.InverseTransformPoint(ray.origin),
                transform.InverseTransformVector(ray.direction));
            Bounds bounds = sprite.localBounds;
            if (!new Plane(Vector3.forward, bounds.center).Raycast(localRay, out float distance))
                return false;
            Vector3 localPoint = localRay.GetPoint(distance);
            if (localPoint.x < bounds.min.x || localPoint.x > bounds.max.x
                || localPoint.y < bounds.min.y || localPoint.y > bounds.max.y)
                return false;
            point = transform.TransformPoint(localPoint);
            return true;
        }

        // 其他自定义外观（例如 MeshRenderer）使用其可见范围。
        if (!renderer.bounds.IntersectRay(ray, out float hitDistance))
            return false;
        point = ray.GetPoint(hitDistance);
        return true;
    }
}
