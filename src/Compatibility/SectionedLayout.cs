using System;

namespace ItemDetails;

/// <summary>
/// 第三方美化 MOD（实测「物品详情美化」/ SectionedTooltips，id amres.sectioned_tooltips）
/// 的兼容层。它按物品接管 tooltip 后，会用自己的一套「单元格」宽度模型重排全部文本，
/// 并且**不认识 TMP 的 &lt;pos&gt; 标签**——我们把 &lt;pos&gt; 当普通文本塞进去，列定位会失效。
///
/// 进度条随整个提示框的排版走。内容物/液体段则逐件判断：
///   1. 它通过 <see cref="ThirdPartyProviders"/> 注册的逐件接管（Contents 段）；
///   2. 程序集已加载但没注册（旧版）：液体容器一律按接管处理——宁可退化成单行，
///      也不要在它的窗口里显示错位的列。
/// </summary>
internal static class SectionedLayout
{
    private const string ModId = "amres.sectioned_tooltips";
    private static bool? _loaded;

    // 1.6.2 的排版总开关可在局内切换。GraphicBars 关闭时也应交给它显示原生文字条。
    internal static bool UsesNativeBars => TooltipPipeline.InTooltip
        && ModLoader.Mods.Exists(m => m.IsEnabled && m.Manifest.ID == ModId)
        && (ModConfig.Find(ModId)?.Get("Enabled") as ConfigEntry<bool>)?.Value != false;

    /// <summary>美化 MOD 是否已加载。</summary>
    internal static bool Present => _loaded ??= Detect();

    private static bool Detect()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(asm.GetName().Name, "SectionedTooltips", StringComparison.Ordinal))
                    return true;
        }
        catch { }
        return false;
    }

    /// <summary>这件物品的内容物/液体段是否已被它接管。</summary>
    internal static bool Handles(GameItem item)
    {
        if (item == null) return false;
        if (!Present) return false;
        // 逐件注册：只有真的接管了才让路
        if (ThirdPartyProviders.HasItemTooltipDisplays)
            return ThirdPartyProviders.HandlesTooltip(item, ItemDetailsTooltipSection.Contents);
        // 旧版没有逐件注册，按「接管所有容器」处理
        try { return item.IsTag("LIQUID_CONTAINER_TAG"); }
        catch { return false; }
    }
}
