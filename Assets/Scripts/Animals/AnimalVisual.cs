using UnityEngine;

/// <summary>
/// 将用户的动物预设体实例关联到逻辑对象，外观、动画及渲染排序由预设体自行定义。
/// 不要求根节点具有 SpriteRenderer，也不生成占位图或携带物标记。
/// </summary>
[DisallowMultipleComponent]
public class AnimalVisual : MonoBehaviour
{
    public AnimalsBase Animal { get; private set; }
    public GameObject SourcePrefab { get; private set; }

    public void Bind(AnimalsBase target, GameObject sourcePrefab)
    {
        Animal = target;
        SourcePrefab = sourcePrefab;
        Refresh();
    }

    /// <summary>只刷新调试名称；自定义表现脚本可以通过 Animal 读取状态。</summary>
    public void Refresh()
    {
        if (Animal != null)
            gameObject.name = Animal.DescribeShort();
    }
}