using System;
using System.Collections.Generic;

namespace ItemDetails;

/// <summary>
/// 容器直接子物品的轻量签名。用于发现数量变化、同数量替换、堆叠变化和显示名变化，
/// 不包含价格状态；密封箱的估价指纹由 SealedBoxInfo 另行维护。
/// </summary>
internal readonly struct ContainerItemSignature
{
    private readonly GameItem? Item;
    private readonly int UniqueId;
    private readonly string Name;
    private readonly int Stack;

    private ContainerItemSignature(GameItem? item)
    {
        Item = item;
        UniqueId = GameRead.UniqueId(item);
        Name = DisplayName(item);
        Stack = ReadStack(item);
    }

    internal static List<ContainerItemSignature> Capture(List<GameItem> items)
    {
        var result = new List<ContainerItemSignature>(items.Count);
        foreach (var item in items) result.Add(new ContainerItemSignature(item));
        return result;
    }

    internal static bool Matches(List<ContainerItemSignature> expected, List<GameItem> current)
    {
        if (expected.Count != current.Count) return false;
        for (int i = 0; i < expected.Count; i++)
        {
            var item = current[i];
            var signature = expected[i];
            if (!ReferenceEquals(signature.Item, item)
                || signature.UniqueId != GameRead.UniqueId(item)
                || signature.Stack != ReadStack(item)
                || !string.Equals(signature.Name, DisplayName(item), StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 签名用的物品名：<c>item.name</c> 原样（创建/读档时固化），只做变化检测。
    /// 要给玩家看的名字用 <see cref="GameRead.ItemDisplayName"/>——那个跟随游戏当前语言，
    /// 且会读本地化库和时钟，不能进这条纯数据路径（离线契约测试依赖它只碰字段）。
    /// </summary>
    internal static string DisplayName(GameItem? item)
    {
        if (item == null) return "";
        string name = item.name ?? "";
        return name.Length > 0 ? name : item.identifier ?? "?";
    }

    internal static int ReadStack(GameItem? item)
    {
        if (item == null) return 1;
        return item.unitCount > 1 ? item.unitCount : 1;
    }
}
