# ItemDetailsClient 快速上手

`ItemDetailsClient.cs` 是给第三方 Mod 的**复制即用**客户端：把文件放进你的工程（C# 12+，引用游戏程序集即可，不引用 ItemDetails.dll）。所有类型在 `ItemDetails.Integration` 命名空间内是 `internal` 的，不会污染你的公共 API。物品详情没装、没启用或版本不符时，每个入口都安静地返回 false，你照常走原版显示。

## 生命周期

```csharp
using ItemDetails.Integration;

public class MyMod : IMod
{
    private IDisposable? _watch;
    private IDisposable? _claim;

    public void OnEnable()
    {
        _watch = ItemDetailsClient.WatchTooltipSections();
        _claim = ItemDetailsClient.RegisterTooltipSections(OnSection);
    }

    public void OnDisable()
    {
        _claim?.Dispose(); _claim = null;
        _watch?.Dispose(); _watch = null;
    }

    // 只接管你这次确实画得出的区块；其余保留物品详情的输出。
    private bool OnSection(GameItem item, RichTextBuilder builder, TooltipSection section)
    {
        if (section.Id != TooltipSectionIds.Appraisal) return false;
        if (!section.TryGetAppraisal(out var appraisal)) return false;
        _pendingAppraisal = appraisal;   // 本次构建暂存；tooltip 关闭时清理
        return true;
    }
}
```

## 在原版钩子里读取整份提示框

物品详情的区块快照按 `RichTextBuilder` 实例归档。你在 `ModHook.OnCreateTooltipLate` 里拿到的就是同一个 builder，直接查询：

```csharp
private void OnTooltipLate(RichTextBuilder builder, GameItem item)
{
    if (!ItemDetailsClient.TryGetTooltipSections(builder, out var sections)) return;
    foreach (var section in sections)
    {
        if (!section.CanReplace) continue;   // 原生行的展示，别删除，改写对应原生行
        foreach (var run in section.Runs)
        {
            // run.Kind: text / line / break / bar（bar 带 Ratio、Length、Tail）
            ReformatMyStyle(run, section.Fields);
        }
    }
}
```

## 规则清单

- **可用性**：`SectionProtocolAvailable` / `LiquidProtocolAvailable` 反映物品详情是否在线；区块是否**适用**仍以快照/查询返回为准（设置关闭、缺扫描仪时返回空）。
- **接管要成套**：`RegisterTooltipSections` 的回调只在本次构建区块准备完成后返回 true；返回 false 或抛错都算不接管。接管 `contents`/`sealed-contents` 时同步用 `RegisterTooltipLayout` 声明 `DrawsContents`，让分类徽章和图标浮窗贴你的窗口。
- **字段只增不改**：区块 ID 用 `TooltipSectionIds` 常量；读取用 `TryGet` 并给默认值。`CanReplace=false` 的区块（`label`、`manufacturer`、`inspect-hint` 等）是原生行的展示，物品详情没有独立输出它们。
- **布局声明**：`NativeProgressBars` 让附加进度条走游戏 `AddProgressBar`（比例可直接读）；`FlowingText` 让液体表自然换行。回调返回 null 表示本次不接管排版。
- **锚点**：`RegisterTooltipAnchor` 每次返回当前可见提示框的 `RectTransform`；窗口重建后无需重新注册。
- **液体**：`TryGetLiquidAnalysis` / `TryGetWaterAdvice` 与通用区块独立。净化收益单位是**百分点**；空列表是有效结果（当前无可列手段）。
- **线程与清理**：全部在游戏主线程；每帧悬停可能重建。逐件暂存随本次构建清理，句柄在 `OnDisable` 释放。

字段与区块清单见 [CONTRIBUTING.md](../CONTRIBUTING.md)。
