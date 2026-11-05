using System;
using System.Collections.Generic;
using System.Globalization;
using ItemDetails.Integration;
using TMPro;
using UnityEngine;

namespace ItemDetails;

/// <summary>
/// 水质成分表排版：三列用 TMP 的 &lt;pos&gt; 标签精确定位，不再靠空格补位。
///
/// 依据：反编译正式版的 <c>TMP_Text.ValidateHtmlTag</c>，这版 TMP 支持
/// <c>&lt;pos&gt;</c>（像素 / em / 百分比三种单位，走 <c>m_xAdvance = …</c>）。
/// 用 em（字号倍数）定位与字体无关，中英文混排也不会错位：
///
///     成分     ██████ 2000/2000mL 纯水
///     水       1998.0mL   99.90%
///     化学污染物  0.6mL    0.03%
///
/// 列宽按实测字宽算（名称列取最宽的名称/表头），但落点交给 &lt;pos&gt;，
/// 所以每条成分的体积、百分比都在同一条竖直线上。
/// </summary>
internal static class WaterColumns
{
    /// <summary>数字列与上一列之间的间距（em）。</summary>
    private const float ColumnGapEm = 0.9f;

    /// <summary>
    /// 固定配色（按液体 identifier）：水蓝、化学污染物紫、有机废物绿、微生物黄、
    /// 矿物质白、重金属灰、物理污染物红；其它液体按顺序取剩余颜色。
    /// </summary>
    private static readonly Dictionary<string, RenderHandler.ColorPalette> FixedColors = new(StringComparer.Ordinal)
    {
        ["water"] = RenderHandler.ColorPalette.LightBlue,
        ["chemical_contaminant"] = RenderHandler.ColorPalette.LightPurple,
        ["organic_waste"] = RenderHandler.ColorPalette.LightGreen,
        ["microbe"] = RenderHandler.ColorPalette.LightYellow,
        ["mineral"] = RenderHandler.ColorPalette.White,
        ["heavy_metal"] = RenderHandler.ColorPalette.Gray,
        ["physical_contaminant"] = RenderHandler.ColorPalette.LightRed,
    };

    /// <summary>没有固定配色的液体依次取这些颜色。</summary>
    private static readonly RenderHandler.ColorPalette[] FallbackColors =
    {
        RenderHandler.ColorPalette.LightOrange,
        RenderHandler.ColorPalette.LightTurquoise,
        RenderHandler.ColorPalette.Purple,
        RenderHandler.ColorPalette.ERed,
        RenderHandler.ColorPalette.Orange,
        RenderHandler.ColorPalette.LightCyan,
    };

    // ---- 字体测量 ----
    private static TMP_Text? _probe;
    private static readonly Dictionary<string, float> Cache = new(StringComparer.Ordinal);
    private const float ProbeFontSize = 16f;

    private static void Ensure()
    {
        if (_probe != null) return;
        try
        {
            var go = new GameObject("ItemDetails_WaterMeasure", typeof(RectTransform), typeof(TextMeshProUGUI));
            UnityEngine.Object.DontDestroyOnLoad(go);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = ContentsGrid.TemplateFont
                       ?? UnityEngine.Object.FindObjectOfType<TMP_Text>()?.font
                       ?? TMP_Settings.defaultFontAsset;
            tmp.fontSize = ProbeFontSize;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            _probe = tmp;
        }
        catch (Exception ex) { Log.Warn("WaterColumns.Probe", ex); }
    }

    /// <summary>字符串宽度（像素，按 16 号字）。</summary>
    internal static float Measure(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        if (Cache.TryGetValue(text!, out float cached)) return cached;
        Ensure();
        float width;
        try
        {
            width = _probe != null
                ? _probe.GetPreferredValues(text!, 5000f, 5000f).x
                : text!.Length * 8f;
        }
        catch { width = text!.Length * 8f; }
        Cache[text!] = width;
        return width;
    }

    private static float Em(string? text) => Measure(text) / ProbeFontSize;

    private static string Pos(float em)
        => "<pos=" + em.ToString("0.##", CultureInfo.InvariantCulture) + "em>";

    /// <summary>表头文字（「成分」）与三条列的起点，单位是 em。</summary>
    internal readonly struct Layout
    {
        internal readonly string Label;
        internal readonly float VolumeStart;
        internal readonly float PercentStart;

        internal Layout(string label, float volumeStart, float percentStart)
        {
            Label = label;
            VolumeStart = volumeStart;
            PercentStart = percentStart;
        }

        /// <summary>把文案放到体积列。</summary>
        internal string ToVolume(string text) => text + Pos(VolumeStart);

        /// <summary>把文案放到百分比列（体积列已写过的行用这个）。</summary>
        internal string ToPercent(string text) => text + Pos(PercentStart);
    }

    /// <summary>
    /// 按表头与所有行的名称/用量算出列落点。液体容器与固体化学品共用这一套，
    /// 两条路径的列位置因此一致。
    /// </summary>
    internal static Layout Measure(string label, IEnumerable<(string Name, string Amount)> rows)
    {
        float nameEm = Em(label);
        float volEm = 0f;
        foreach (var row in rows)
        {
            nameEm = Math.Max(nameEm, Em(row.Name));
            volEm = Math.Max(volEm, Em(row.Amount));
        }
        float volStart = nameEm + ColumnGapEm;
        return new Layout(label, volStart, volStart + volEm + ColumnGapEm);
    }

    /// <summary>液体容器的一行体积，单位随 <see cref="Volume.UseLiters"/>。</summary>
    internal static string AmountOf(int parts, bool liters) => VolumeOf(parts, liters);

    /// <summary>
    /// 固体化学品的成分行：名称 / 用量 / 百分比三列，与液体容器同一落点规则。
    /// 没有容器与体积条，表头只用「成分」。
    /// </summary>
    internal static List<TooltipLine> BuildSolid(LiquidAnalysis analysis)
    {
        var comps = new List<(string Name, int Parts, RenderHandler.ColorPalette Color)>();
        int activeIndex = 0, otherIndex = 0;
        foreach (var component in analysis.Components)
        {
            int index = component.Role == "active" ? activeIndex++ : component.Role == "other" ? otherIndex++ : 0;
            comps.Add((component.Name, component.Microliters, ColorOf(component.Id, index)));
        }

        string label = Loc.T("water.components", "Components");
        var rows = new List<(string Name, string Amount)>();
        foreach (var c in comps) rows.Add((c.Name, WaterInfo.Amount(c.Parts)));
        var layout = Measure(label, rows);

        var lines = new List<TooltipLine> { new(label, RenderHandler.ColorPalette.LightCyan) };
        foreach (var c in comps)
        {
            string text = layout.ToVolume(WaterInfo.Amount(c.Parts))
                        + layout.ToPercent(WaterLayout.PercentText(c.Parts, analysis.TotalMicroliters, Loc.Culture) + "%");
            lines.Add(new TooltipLine(text, c.Color));
        }
        if (analysis.HiddenComponents > 0)
            lines.Add(new TooltipLine(Loc.T("water.trace_count", "(+{0} more below display threshold)", analysis.HiddenComponents),
                RenderHandler.ColorPalette.Gray));
        return lines;
    }

    internal static List<TooltipLine> Build(LiquidAnalysis analysis, GameItem item)
    {
        var lines = new List<TooltipLine>();
        int totalMl = analysis.TotalMicroliters / 1000;
        int capacityMl = analysis.CapacityMicroliters / 1000;
        bool liters = Volume.UseLiters(totalMl);
        float ratio = analysis.CapacityMicroliters > 0
            ? Math.Min(1f, (float)analysis.TotalMicroliters / analysis.CapacityMicroliters) : 0f;

        var comps = new List<(string Name, int Parts, RenderHandler.ColorPalette Color)>();
        int activeIndex = 0, otherIndex = 0;
        foreach (var component in analysis.Components)
        {
            string name = component.Name;
            if (component.Role == "water" && analysis.LabelMismatch)
                name += Loc.T("water.mismatch", " (doesn't match label)");
            int index = component.Role == "active" ? activeIndex++ : component.Role == "other" ? otherIndex++ : 0;
            comps.Add((name, component.Microliters, ColorOf(component.Id, index)));
        }

        string label = Loc.T("water.components", "Components");
        var measureRows = new List<(string Name, string Amount)>();
        foreach (var c in comps) measureRows.Add((c.Name, VolumeOf(c.Parts, liters)));
        var layout = Measure(label, measureRows);
        string pos = Pos(layout.VolumeStart);
        string tail = " " + WaterLayout.Capacity(totalMl, capacityMl, liters, Loc.Culture)
                    + (analysis.Tier.Length > 0 ? " " + analysis.Tier : "");

        if (SectionedLayout.UsesFlowingText(item))
        {
            // 美化 MOD 接管了这件物品：它用自己的一套单元格宽度重排，不认识 <pos>，
            // 于是三列定位会失效。改成单行「名称 体积 百分比」，按它的补位单位（全角空格）分隔，
            // 列对齐和表头进度条的绘制交给它。
            var plain = new List<TooltipLine>
            {
                new(label + "  " + tail.TrimStart(), ratio, 6, "",
                    RenderHandler.ColorPalette.LightCyan, RenderHandler.ColorPalette.VeryDarkCyan),
            };
            foreach (var c in comps)
            {
                string text = c.Name + "　" + VolumeOf(c.Parts, liters) + "　"
                            + WaterLayout.PercentText(c.Parts, analysis.TotalMicroliters, Loc.Culture) + "%";
                plain.Add(new TooltipLine(text, c.Color));
            }
            return plain;
        }

        // 条长 6：原版 10 格加上表头文字会折行
        lines.Add(new TooltipLine(label + pos, ratio, 6, tail,
            RenderHandler.ColorPalette.LightCyan, RenderHandler.ColorPalette.VeryDarkCyan));

        foreach (var c in comps)
        {
            string text = c.Name + pos + VolumeOf(c.Parts, liters)
                        + layout.ToPercent(WaterLayout.PercentText(c.Parts, analysis.TotalMicroliters, Loc.Culture) + "%");
            lines.Add(new TooltipLine(text, c.Color));
        }
        return lines;
    }

    private static RenderHandler.ColorPalette ColorOf(string id, int fallbackIndex)
        => id != null && FixedColors.TryGetValue(id, out var color)
            ? color
            : FallbackColors[fallbackIndex % FallbackColors.Length];

    private static string VolumeOf(int parts, bool liters)
        => WaterLayout.VolumeText(parts, liters, Loc.Culture)
           + (liters ? WaterLayout.LiterUnit : WaterLayout.MilliliterUnit);
}
