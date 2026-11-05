using System;
using System.Text;
using HarmonyLib;

namespace ItemDetails;

/// <summary>
/// 把原版 tooltip 里的 `[=====     ]` 文字进度条换成一排 `█`：
/// 已填部分用调用方给的前景色，未填部分用它给的暗色（原版每处都传了 VeryDarkCyan 之类的背景色，
/// 但 <c>AddProgressBar</c> 里把背景色强制清成 None，等于没用上）。
///
/// 不跳过 <c>AddProgressBar</c>：原方法照常拼串、Add、再把这一段标成不折行；这里只在它
/// 调用 Add 的那一刻换掉文本。所以不折行标记、tooltip 换行宽度和其它模组对 AddProgressBar
/// 的补丁都保留。原方法传给 Add 的文本不是 `[` 开头 `]` 结尾时（被别人改过）原样放过。
///
/// 字形用 U+2588：原版自己的背景色层就是用它铺底（RichTextBuilder.TextSlice.GetFillString），
/// tooltip 字体里一定有这个字。
///
/// 物品 tooltip 里优先画成 sprite 条（外框 + 暗底 + 填充，见 <see cref="SpriteProgressBar"/>），
/// 宽度仍按 length 格计；sprite 不可用或不在 tooltip 构建期间时退回 `█` 条。
/// </summary>
internal static class ProgressBarStyle
{
    private const string HarmonyId = "item_details.progress_bar";
    private const char Block = '█';
    /// <summary>调用方没给暗色时，未填部分用前景色加这个透明度。</summary>
    private const string DimAlpha = "<alpha=#40>";

    private static Harmony? _harmony;
    private static bool _patched;

    /// <summary>AddProgressBar 正在构建的那一条；只在它执行期间非空，由它里面唯一一次 Add 消费。</summary>
    private static Pending? _pending;

    private sealed class Pending
    {
        internal float Ratio;
        internal int Length;
        internal bool FullLength;
        internal RenderHandler.ColorPalette Dark;
    }

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
            var add = AccessTools.Method(typeof(RichTextBuilder), "Add", new[]
            {
                typeof(string), typeof(RenderHandler.ColorPalette), typeof(string), typeof(bool),
                typeof(bool), typeof(bool), typeof(bool), typeof(RenderHandler.ColorPalette),
                typeof(RenderHandler.ColorPalette), typeof(RenderHandler.ColorPalette)
            });
            if (bar == null || add == null)
            {
                Log.Warn("ProgressBar.Apply", "未找到 RichTextBuilder.AddProgressBar/Add，进度条保持原版");
                return;
            }

            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(bar,
                prefix: new HarmonyMethod(typeof(ProgressBarStyle), nameof(BeforeBar)),
                finalizer: new HarmonyMethod(typeof(ProgressBarStyle), nameof(AfterBar)));
            _harmony.Patch(add, prefix: new HarmonyMethod(typeof(ProgressBarStyle), nameof(AddPrefix)));
            _patched = true;
            Log.Msg("ProgressBarStyle patched RichTextBuilder.AddProgressBar/Add");
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
        _pending = null;
    }

    private static void BeforeBar(float __0, int __1, RenderHandler.ColorPalette __3, bool __6)
    {
        _pending = ModSettings.BlockProgressBar && __1 > 0
            ? new Pending { Ratio = __0, Length = __1, Dark = __3, FullLength = __6 }
            : null;
    }

    private static void AfterBar()
    {
        _pending = null;
    }

    private static void AddPrefix(ref string __0)
    {
        var p = _pending;
        if (p == null) return;
        _pending = null;   // 只换 AddProgressBar 里那一次 Add

        try
        {
            string text = __0;
            if (text == null || text.Length < 2 || text[0] != '[' || text[text.Length - 1] != ']') return;
            // 原版这一段带不折行标记，sprite 标签里的空格不会被当成断行点。
            string? sprite = p.FullLength && TooltipPipeline.InTooltip
                ? SpriteProgressBar.TryRender(p.Ratio, p.Length, p.Dark)
                : null;
            __0 = sprite ?? RenderBlocks(p.Ratio, p.Length, p.Dark, p.FullLength);
        }
        catch (Exception ex)
        {
            Log.Warn("ProgressBar.Render", ex);
        }
    }

    /// <summary>
    /// 直接生成进度条文本。供模组内新增行（如学习进度）直接写入 RichTextBuilder，
    /// 避免调用原生 AddProgressBar 引入 noBreak 影响整段 tooltip 折行宽度。
    ///
    /// allowSprite：调用方已确认整行不会被折行时才传 true。这条路径没有不折行标记，
    /// 原版折行会在 slice 文本的空格处断开，而 sprite 标签内部带空格。
    /// </summary>
    internal static string Format(float ratio, int length, RenderHandler.ColorPalette dark, bool fullLength = true,
        bool allowSprite = false)
    {
        if (length <= 0) return "";
        if (!ModSettings.BlockProgressBar) return RenderAscii(ratio, length, fullLength);
        string? sprite = allowSprite && fullLength && TooltipPipeline.InTooltip
            ? SpriteProgressBar.TryRender(ratio, length, dark)
            : null;
        return sprite ?? RenderBlocks(ratio, length, dark, fullLength);
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
