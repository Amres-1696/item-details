using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ItemDetails;

/// <summary>
/// 逻辑层与渲染层分离的进度条实现：
/// - 逻辑层（RichTextBuilder / TextSlice）：保留原版 `[=====     ]` 纯文本格式与字符宽度，
///   并在内部上下文中记录高精度条元数据（BarRecord）；
/// - 第三方层：像 SectionedTooltips 等美化排版 Mod 读取和切分原版文本时，看到的是标准原版字符，
///   正则提取与列宽计算完全不会被 TMP sprite 标签破坏；
/// - 渲染层（RichTextElement.SetRichText）：在实际向屏幕 TextMeshPro 提交富文本的前夕，
///   统一匹配 `[=====     ]` 并根据之前记录的高精度浮点数替换为高清 SpriteProgressBar（或回退为 █ 块）。
/// </summary>
internal static class ProgressBarStyle
{
    private const string HarmonyId = "item_details.progress_bar";
    private const char Block = '█';
    private const int MaxLength = 40;
    private const string DimAlpha = "<alpha=#40>";
    private static readonly Regex BarRegex = new Regex(@"\[([= ]{2,40})\]", RegexOptions.Compiled);

    private static Harmony? _harmony;
    private static bool _patched;

    private static FieldInfo? _textNodeField;
    private static PropertyInfo? _textProp;

    internal readonly struct BarRecord
    {
        internal readonly float Ratio;
        internal readonly int Length;
        internal readonly RenderHandler.ColorPalette Dark;
        internal readonly bool FullLength;

        internal BarRecord(float ratio, int length, RenderHandler.ColorPalette dark, bool fullLength = true)
        {
            Ratio = ratio;
            Length = length;
            Dark = dark;
            FullLength = fullLength;
        }
    }

    private static readonly ConditionalWeakTable<RichTextBuilder, List<BarRecord>> BuilderBars = new();
    private static readonly ConditionalWeakTable<RichText, List<BarRecord>> RichTextBars = new();
    private static List<BarRecord>? _lastBuildBars;

    internal static void Apply()
    {
        if (_patched) return;
        try
        {
            var bar = AccessTools.Method(typeof(RichTextBuilder), "AddProgressBar", new[]
            {
                typeof(float), typeof(int), typeof(RenderHandler.ColorPalette), typeof(RenderHandler.ColorPalette),
                typeof(char[]), typeof(string), typeof(bool), typeof(bool)
            });
            var addBuilder = AccessTools.Method(typeof(RichTextBuilder), "Add", new[] { typeof(RichTextBuilder) });
            var clear = AccessTools.Method(typeof(RichTextBuilder), "Clear", Type.EmptyTypes);
            var build = AccessTools.Method(typeof(RichTextBuilder), "Build", new[] { typeof(int) });
            var setRich = AccessTools.Method(typeof(RichTextElement), "SetRichText",
                new[] { typeof(RichText), typeof(int) });

            if (bar == null || build == null || setRich == null)
            {
                Log.Warn("ProgressBar.Apply", "未找到 AddProgressBar/Build/SetRichText，进度条保持原版");
                return;
            }

            _textNodeField = AccessTools.Field(typeof(RichTextElement), "textNode");
            _textProp = AccessTools.Property(typeof(TreeNodeRender), "text");

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(bar, prefix: new HarmonyMethod(typeof(ProgressBarStyle), nameof(BeforeBar)));
            if (addBuilder != null)
                _harmony.Patch(addBuilder, postfix: new HarmonyMethod(typeof(ProgressBarStyle), nameof(AfterAddBuilder)));
            if (clear != null)
                _harmony.Patch(clear, postfix: new HarmonyMethod(typeof(ProgressBarStyle), nameof(AfterClear)));
            _harmony.Patch(build, postfix: new HarmonyMethod(typeof(ProgressBarStyle), nameof(AfterBuild)));
            _harmony.Patch(setRich, prefix: new HarmonyMethod(typeof(ProgressBarStyle), nameof(BeforeSetRichText)));

            _patched = true;
            Log.Msg("ProgressBarStyle: hooked AddProgressBar/Build/SetRichText for decoupled bar rendering");
        }
        catch (Exception ex)
        {
            Log.Error("ProgressBar.Apply", ex);
        }
    }

    internal static void Remove()
    {
        try { _harmony?.UnpatchAll(HarmonyId); } catch { }
        _harmony = null;
        _patched = false;
        _lastBuildBars = null;
    }

    internal static void RecordBar(RichTextBuilder builder, float ratio, int length, RenderHandler.ColorPalette dark, bool fullLength = true)
    {
        if (builder == null || length <= 0) return;
        lock (BuilderBars)
        {
            BuilderBars.GetOrCreateValue(builder).Add(new BarRecord(ratio, length, dark, fullLength));
        }
    }

    private static void BeforeBar(RichTextBuilder __instance, float __0, int __1, RenderHandler.ColorPalette __3, bool __6)
    {
        if (ModSettings.BlockProgressBar && __1 > 0)
        {
            RecordBar(__instance, __0, __1, __3, __6);
        }
    }

    private static void AfterAddBuilder(RichTextBuilder __instance, RichTextBuilder __0)
    {
        if (__0 == null) return;
        lock (BuilderBars)
        {
            if (BuilderBars.TryGetValue(__0, out var otherBars) && otherBars.Count > 0)
            {
                BuilderBars.GetOrCreateValue(__instance).AddRange(otherBars);
            }
        }
    }

    private static void AfterClear(RichTextBuilder __instance)
    {
        lock (BuilderBars)
        {
            if (BuilderBars.TryGetValue(__instance, out var bars)) bars.Clear();
        }
    }

    private static void AfterBuild(RichTextBuilder __instance, ref RichText __result)
    {
        if (__result == null) return;
        lock (BuilderBars)
        {
            if (BuilderBars.TryGetValue(__instance, out var bars) && bars.Count > 0)
            {
                var copy = new List<BarRecord>(bars);
                lock (RichTextBars)
                {
                    RichTextBars.Add(__result, copy);
                }
                _lastBuildBars = copy;
            }
        }
    }

    private static void BeforeSetRichText(RichTextElement __instance, ref RichText richText)
    {
        if (!ModSettings.BlockProgressBar || richText == null) return;
        string fg = richText.richTextForeground;
        if (string.IsNullOrEmpty(fg) || fg.IndexOf('[') < 0) return;

        List<BarRecord>? bars = null;
        lock (RichTextBars)
        {
            if (RichTextBars.TryGetValue(richText, out var list))
            {
                bars = new List<BarRecord>(list);
            }
        }
        if (bars == null && _lastBuildBars != null && _lastBuildBars.Count > 0)
        {
            bars = new List<BarRecord>(_lastBuildBars);
        }

        TMP_Text? targetText = null;
        try
        {
            if (_textNodeField != null && _textProp != null)
            {
                object? node = _textNodeField.GetValue(__instance);
                if (node != null) targetText = _textProp.GetValue(node) as TMP_Text;
            }
        }
        catch { }

        string newFg = BarRegex.Replace(fg, match =>
        {
            string inner = match.Groups[1].Value;
            int len = inner.Length;
            if (len < 2 || len > MaxLength) return match.Value;
            int firstSpace = inner.IndexOf(' ');
            if (firstSpace >= 0 && inner.IndexOf('=', firstSpace) >= 0) return match.Value;

            int equals = 0;
            for (int i = 0; i < len; i++) if (inner[i] == '=') equals++;
            float fallbackRatio = (float)equals / len;

            BarRecord? record = MatchBar(bars, len, equals);
            float ratio = record?.Ratio ?? fallbackRatio;
            var dark = record?.Dark ?? RenderHandler.ColorPalette.None;
            bool fullLength = record?.FullLength ?? true;

            string? sprite = fullLength
                ? SpriteProgressBar.TryRender(ratio, len, dark, targetText)
                : null;
            return sprite ?? RenderBlocks(ratio, len, dark, fullLength);
        });

        if (!string.Equals(newFg, fg, StringComparison.Ordinal))
        {
            richText = new RichText(
                richText.nakedText,
                richText.richTextStrikethrough,
                richText.richTextUnderline,
                newFg,
                richText.richTextBackground,
                richText.recommendedWidth,
                richText.recommendedHeight,
                richText.isTruncated
            );
        }
    }

    private static BarRecord? MatchBar(List<BarRecord>? bars, int length, int equalsCount)
    {
        if (bars == null || bars.Count == 0) return null;

        // 1. 优先：长度与已填格数均完全一致（第三方即使重排行也能精准找回原纪录）
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            if (b.Length == length && ProgressFill.Filled(b.Ratio, b.Length) == equalsCount)
            {
                bars.RemoveAt(i);
                return b;
            }
        }

        // 2. 次选：长度匹配
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            if (b.Length == length)
            {
                bars.RemoveAt(i);
                return b;
            }
        }

        // 3. 兜底：取第一个
        var first = bars[0];
        bars.RemoveAt(0);
        return first;
    }

    /// <summary>
    /// 生成进度条文本。在逻辑层返回标准原版格式 `[=====     ]`，
    /// 同时在 builder 上记录高精度条元数据，供后续渲染层替换为 Sprite 进度条。
    /// </summary>
    internal static string Format(RichTextBuilder? builder, float ratio, int length, RenderHandler.ColorPalette dark,
        bool fullLength = true)
    {
        if (length <= 0) return "";
        if (builder != null && ModSettings.BlockProgressBar)
        {
            RecordBar(builder, ratio, length, dark, fullLength);
        }
        return RenderAscii(ratio, length, fullLength);
    }

    /// <summary>无 builder 重载（向下兼容）：生成纯 ASCII 条</summary>
    internal static string Format(float ratio, int length, RenderHandler.ColorPalette dark, bool fullLength = true,
        bool allowSprite = false)
    {
        if (length <= 0) return "";
        return RenderAscii(ratio, length, fullLength);
    }

    /// <summary>原版 RichTextBuilder 计折行时的可见字符数：跳过 &lt;…&gt; 标签与控制字符。</summary>
    internal static int VisibleLength(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int n = 0;
        bool tag = false;
        foreach (char c in text!)
        {
            if (c == '<') { tag = true; continue; }
            if (c == '>') { tag = false; continue; }
            if (!tag && !char.IsControl(c)) n++;
        }
        return n;
    }

    /// <summary>
    /// 一行文字在原版 Build 里会不会折行。中日韩（字宽缩放 &gt; 1）取不带不折行片段时的 0.66 倍宽，
    /// 这是两种系数里较窄的一个，判断偏保守。
    /// </summary>
    internal static bool FitsOneLine(RichTextBuilder builder, int visibleLength)
    {
        float scale = UISettings.current != null ? UISettings.current.GetCharWidthScale() : 1f;
        int limit = scale > 1f ? Math.Max(1, (int)(builder.maxCharWidth * 0.66f)) : builder.maxCharWidth;
        return visibleLength <= limit;
    }

    /// <summary>格数同原版 floor(比例 × 长度)，只是接近满时按满画（见 <see cref="ProgressFill"/>）。</summary>
    private static string RenderBlocks(float rawRatio, int length, RenderHandler.ColorPalette dark, bool fullLength)
    {
        int filled = ProgressFill.Filled(rawRatio, length);
        int empty = fullLength ? length - filled : 0;

        var sb = new StringBuilder(length + 32);
        if (filled > 0) sb.Append(Block, filled);
        if (empty > 0)
        {
            string rest = new string(Block, empty);
            sb.Append(dark != RenderHandler.ColorPalette.None
                ? RenderHandler.ApplyRichTextColorTag(rest, dark)
                : DimAlpha + rest + "<alpha=#FF>");
        }
        return sb.ToString();
    }

    private static string RenderAscii(float rawRatio, int length, bool fullLength)
    {
        int filled = ProgressFill.Filled(rawRatio, length);
        int empty = fullLength ? length - filled : 0;
        var sb = new StringBuilder(length + 2);
        sb.Append('[');
        if (filled > 0) sb.Append('=', filled);
        if (empty > 0) sb.Append(' ', empty);
        sb.Append(']');
        return sb.ToString();
    }
}
