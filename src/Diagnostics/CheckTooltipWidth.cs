#if ITEMDETAILS_SELFTEST
using System;
using HarmonyLib;

namespace ItemDetails;

/// <summary>
/// 框宽修正的自检：确认 Harmony 已挂上 RichText.recommendedWidth，
/// 且可见宽度按「最长一行去掉标签后的字符数」计算。
/// </summary>
internal static class CheckTooltipWidth
{
    internal static string Run(out bool ok)
    {
        ok = false;
        try
        {
            var build = AccessTools.Method(typeof(RichTextBuilder), nameof(RichTextBuilder.Build));
            if (build == null) return "tooltip width: 未找到 RichTextBuilder.Build";
            var info = Harmony.GetPatchInfo(build);
            bool patched = false;
            if (info?.Postfixes != null)
                foreach (var patch in info.Postfixes)
                    if (patch.owner == "item_details.tooltip_width") { patched = true; break; }

            // 「水<pos=5.9em>196.37 mL<pos=11.67em>78.55%」：原始 40 字符，去掉标签后可见 16 字符。
            const string sample = "水<pos=5.9em>196.37 mL<pos=11.67em>78.55%";
            int visible = TooltipWidthFix.VisibleWidth(sample);
            int raw = sample.Length;
            bool shrunk = visible == 16 && visible < raw;

            ok = patched && shrunk;
            return $"tooltip width: patched={patched} visible={visible} raw={raw} " +
                   $"(期望 patched=True、visible=16 < raw=40)";
        }
        catch (Exception ex) { return "tooltip width: " + ex; }
    }
}
#endif
