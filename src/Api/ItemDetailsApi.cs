using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ItemDetails;

/// <summary>机器加成行中要说明的项目。</summary>
public enum ItemDetailsModuleStat
{
    Performance,
    Efficiency,
    Quality,
}

/// <summary>净化手段走哪个游戏函数；Item Details 用该函数在探针上模拟净化收益。</summary>
public enum ItemDetailsPurifierKind
{
    /// <summary>机器，模拟 MachinePurifier.PurifyContainer（读机器自身的性能/品质加成）。</summary>
    Machine,
    /// <summary>手持净水器，模拟 WaterHelper.PurifyToBaseWater。</summary>
    Portable,
    /// <summary>可溶化学剂，模拟 ChemicalHelper.DissolveInto 加温度反应链（按物品自身的液体含量）。</summary>
    ChemicalAgent,
}

/// <summary>
/// 第三方可接管显示的信息块。可按位或组合；每个值只抑制 Item Details 的对应追加内容，
/// 不删除原版 tooltip，也不禁用分类徽章、价格行改写等界面补丁。
/// </summary>
[Flags]
public enum ItemDetailsTooltipSection
{
    None = 0,
    /// <summary>真伪结论与赝品原因。</summary>
    Authenticity = 1 << 0,
    /// <summary>鉴定物品的总价、完美区间与价值分解。</summary>
    ValueBreakdown = 1 << 1,
    /// <summary>果实品质与酿酒值。</summary>
    BerryQuality = 1 << 2,
    /// <summary>真实耐久/纯度与隐藏特性。</summary>
    HiddenInfo = 1 << 3,
    /// <summary>性价比与容器压缩率。</summary>
    ValueEnhance = 1 << 4,
    /// <summary>普通/密封容器的内容物图标格与机器通用内容物文字清单。</summary>
    Contents = 1 << 5,
    /// <summary>机器内部状态摘要与专门适配；通用内容物清单由 Contents 单独控制。</summary>
    MachineInterior = 1 << 6,
    /// <summary>自学习模组进度。</summary>
    ModuleLearning = 1 << 7,
    /// <summary>瑕疵和状态对价值的影响。</summary>
    ConditionEffects = 1 << 8,
    /// <summary>违禁品黑市溢价。</summary>
    Contraband = 1 << 9,
    /// <summary>彩票结果。</summary>
    Lottery = 1 << 10,
    /// <summary>水质、活性成分与杂质分析。</summary>
    LiquidAnalysis = 1 << 11,
    /// <summary>刮刮乐结果。</summary>
    Scratcher = 1 << 12,
    /// <summary>种子性状。</summary>
    Seed = 1 << 13,
    /// <summary>净水手段与纯度提升建议。</summary>
    WaterAdvice = 1 << 14,
    /// <summary>果酿分数、成分与赝品信息。</summary>
    Wine = 1 << 15,
    /// <summary>肉类来源与朊病毒信息。</summary>
    Meat = 1 << 16,
    /// <summary>市场事件价格修正。</summary>
    Market = 1 << 17,
    /// <summary>以上全部追加信息块。</summary>
    All = (1 << 18) - 1,
}

/// <summary>一项已经由提供方解释清楚的价值贡献。</summary>
public readonly struct ItemDetailsBreakdownPart
{
    public string Label { get; }
    public int Amount { get; }
    public bool IsBase { get; }

    public ItemDetailsBreakdownPart(string label, int amount, bool isBase = false)
    {
        Label = label;
        Amount = amount;
        IsBase = isBase;
    }
}

/// <summary>第三方鉴定类型的完整价值分解。</summary>
public sealed class ItemDetailsBreakdown
{
    public int Total { get; }
    public IReadOnlyList<ItemDetailsBreakdownPart> Parts { get; }

    public ItemDetailsBreakdown(int total, IEnumerable<ItemDetailsBreakdownPart> parts)
    {
        Total = total;
        Parts = (parts ?? throw new ArgumentNullException(nameof(parts))).ToArray();
    }
}

/// <summary>返回 null 表示本次不提供分解；总价必须和该物品当前规则一致。</summary>
public delegate ItemDetailsBreakdown? ItemDetailsAppraisalProvider(GameItem item);

/// <summary>返回已本地化的注释正文（不含括号）；null 或空串表示这一项不写注释。</summary>
public delegate string? ItemDetailsMachineEffectProvider(GameItem machine, ItemDetailsModuleStat stat);

/// <summary>
/// 第三方模组的显式接入口。提供方只返回自己理解的语义，Item Details 不采样、不反推规则。
/// 在第三方模组的 OnEnable 中注册，并保存返回的注册句柄，在 OnDisable 中 Dispose。
/// 同一注册键同时只允许一个提供方或显示接管方，避免隐式覆盖其它模组。
/// </summary>
public static class ItemDetailsApi
{
    /// <summary>按 AppraisalItemType.id 注册鉴定分解提供方。</summary>
    public static IDisposable RegisterAppraisalBreakdown(string appraisalTypeId, ItemDetailsAppraisalProvider provider)
        => ThirdPartyProviders.RegisterAppraisal(appraisalTypeId, provider);

    /// <summary>按机器物品 identifier 注册加成注释提供方；注册后该 ID 的原版注释由提供方接管。</summary>
    public static IDisposable RegisterMachineEffects(string itemId, ItemDetailsMachineEffectProvider provider)
        => ThirdPartyProviders.RegisterMachine(itemId, provider);

    /// <summary>
    /// 按物品来源 Mod（MOD_ID_TAG 或官方物品工厂来源）声明第三方接管指定信息块的显示。
    /// sections 可按位或组合；命中后 Item Details 不再输出这些追加信息，其它信息块不受影响。
    /// 第三方仍须通过自己的 tooltip 钩子输出替代内容。注销后恢复 Item Details 的显示。
    /// </summary>
    public static IDisposable RegisterTooltipDisplay(string modId, ItemDetailsTooltipSection sections)
        => ThirdPartyProviders.RegisterTooltip(modId, sections);

    /// <summary>只接管内容物显示；等同于 RegisterTooltipDisplay(modId, Contents)。</summary>
    public static IDisposable RegisterContentsDisplay(string modId)
        => RegisterTooltipDisplay(modId, ItemDetailsTooltipSection.Contents);

    /// <summary>
    /// 注册分类徽章使用的固定提示框锚点。徽章贴在它上方，父节点和字体也取自该提示框。
    /// 内容物图标窗口也贴在它旁边
    /// 同时只允许一个锚点提供方；Dispose 后恢复原版提示框。
    /// </summary>
    public static IDisposable RegisterTooltipAnchor(RectTransform tooltip)
    {
        if (tooltip == null) throw new ArgumentNullException(nameof(tooltip));
        return ThirdPartyProviders.RegisterTooltipAnchor(() => tooltip);
    }

    /// <summary>
    /// 注册动态提示框锚点提供器，在每次 tooltip 显示时调用。返回当前可见提示框的 RectTransform；
    /// 返回 null 或抛出异常时回退原版提示框。适用于会重建 tooltip 的美化 Mod。
    /// </summary>
    public static IDisposable RegisterTooltipAnchor(Func<RectTransform?> provider)
        => ThirdPartyProviders.RegisterTooltipAnchor(provider);

    /// <summary>
    /// 按物品 identifier 声明它是哪种净水手段，水质建议会用对应的原版函数模拟收益。
    /// 未注册的物品仍会按回调证据与标准试验自动识别；注册后以这里为准。
    /// </summary>
    public static IDisposable RegisterWaterPurifier(string itemId, ItemDetailsPurifierKind kind)
        => ThirdPartyProviders.RegisterPurifier(itemId, kind);
}
