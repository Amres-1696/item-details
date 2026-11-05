using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>生成成分表的所有行（表头 + 每一条成分）；hidden 是低于显示阈值而未列出的项数。</summary>
    internal static List<TooltipLine> Build(WaterInfo.Result r, GameItem item, out int hidden)
    {
        hidden = 0;
        var lines = new List<TooltipLine>();
        int totalMl = r.TotalParts / 1000;
        bool liters = Volume.UseLiters(totalMl);

        int capacityMl = 0;
        try { capacityMl = item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt() / 1000; } catch { }
        float ratio = capacityMl > 0 ? Math.Min(1f, (float)totalMl / capacityMl) : 0f;

        var comps = new List<(string Name, int Parts, RenderHandler.ColorPalette Color)>();
        if (r.WaterParts > 0)
        {
            var color = ColorOf("water", 0);
            string name = r.WaterName.Length > 0 ? r.WaterName : "?";
            bool mismatch = !string.IsNullOrEmpty(r.Label) && !string.IsNullOrEmpty(r.Tier)
                         && !string.Equals(r.Label, r.Tier, StringComparison.Ordinal);
            comps.Add((mismatch ? name + Loc.T("water.mismatch", " (doesn't match label)") : name,
                r.WaterParts, color));
        }
        int activeIndex = 0;
        foreach (var active in r.Actives)
            comps.Add((active.Name, active.Parts, ColorOf(active.Id, activeIndex++)));

        int colorIndex = 0;
        foreach (var part in r.Impurities)
        {
            if (part.Parts < WaterInfo.MinVisibleParts) { hidden++; continue; }
            if (part.Parts * 100.0 / r.TotalParts < 0.005) { hidden++; continue; }
            comps.Add((part.Name, part.Parts, ColorOf(part.Id, colorIndex++)));
        }
        comps.Sort((a, b) => b.Parts.CompareTo(a.Parts));

        string label = Loc.T("water.components", "Components");
        float nameEm = Em(label);
        float volEm = 0f;
        foreach (var c in comps)
        {
            nameEm = Math.Max(nameEm, Em(c.Name));
            volEm = Math.Max(volEm, Em(VolumeOf(c.Parts, liters)));
        }
        float volStart = nameEm + ColumnGapEm;
        float pctStart = volStart + volEm + ColumnGapEm;

        string tail = " " + WaterLayout.Capacity(totalMl, capacityMl, liters, Loc.Culture)
                    + (r.Tier.Length > 0 ? " " + r.Tier : "");
        // 条长 6：原版 10 格加上表头文字会折行
        lines.Add(new TooltipLine(label + Pos(volStart), ratio, 6, tail,
            RenderHandler.ColorPalette.LightCyan, RenderHandler.ColorPalette.VeryDarkCyan));

        foreach (var c in comps)
        {
            string text = c.Name + Pos(volStart) + VolumeOf(c.Parts, liters) + Pos(pctStart)
                        + WaterLayout.PercentText(c.Parts, r.TotalParts, Loc.Culture) + "%";
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
