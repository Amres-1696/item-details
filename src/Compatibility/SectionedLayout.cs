using System;
using ItemDetails.Integration;

namespace ItemDetails;

/// <summary>逐件读取第三方排版声明；没有声明时兼容 SectionedTooltips 1.6.2 的实际开关。</summary>
internal static class SectionedLayout
{
    private const string ModId = "amres.sectioned_tooltips";

    private readonly struct Layout
    {
        internal readonly TooltipLayoutOptions Options;
        internal readonly bool LegacyContentsPanel;

        internal Layout(TooltipLayoutOptions options, bool legacyContentsPanel = false)
        {
            Options = options;
            LegacyContentsPanel = legacyContentsPanel;
        }
    }

    private static int cachedBuild;
    private static Layout cachedLayout;

    internal static bool UsesNativeBars => TooltipPipeline.Current is GameItem item
        && (For(item).Options & TooltipLayoutOptions.NativeProgressBars) != 0;

    internal static bool UsesFlowingText(GameItem item)
        => (For(item).Options & TooltipLayoutOptions.FlowingText) != 0;

    internal static bool DrawsContents(GameItem item)
        => (For(item).Options & TooltipLayoutOptions.DrawsContents) != 0;

    internal static bool LegacyContentsPanel(GameItem item) => For(item).LegacyContentsPanel;

    private static Layout For(GameItem item)
    {
        if (!ReferenceEquals(item, TooltipPipeline.Current)) return Read(item);
        if (cachedBuild != TooltipPipeline.BuildId)
        {
            cachedLayout = Read(item);
            cachedBuild = TooltipPipeline.BuildId;
        }
        return cachedLayout;
    }

    private static Layout Read(GameItem item)
    {
        if (AppDomain.CurrentDomain.GetData(ItemDetailsClient.LayoutSlot) is Func<GameItem, int?> providers)
        {
            foreach (Func<GameItem, int?> describe in providers.GetInvocationList())
            {
                try
                {
                    if (describe(item) is int options) return new Layout((TooltipLayoutOptions)options);
                }
                catch (Exception ex) { Log.Warn("TooltipLayout", ex); }
            }
        }

        if (!ModLoader.Mods.Exists(m => m.IsEnabled && m.Manifest.ID == ModId)) return default;
        var config = ModConfig.Find(ModId);
        if ((config?.Get("Enabled") as ConfigEntry<bool>)?.Value == false) return default;
        bool contents = (config?.Get("ContentsPanel") as ConfigEntry<bool>)?.Value != false;
        return new Layout(TooltipLayoutOptions.NativeProgressBars | TooltipLayoutOptions.FlowingText, contents);
    }
}
