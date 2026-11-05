using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ItemDetails;

/// <summary>
/// 水质成分表的三列排版：按 tooltip 实际字体的像素宽度算列宽，三列各自左对齐。
///
/// 为什么不能靠固定空格：正式版 tooltip 用 NotoSansSC，实测「水」16px/字、「0」8.3px/字，
/// 中日韩字约为西文的 1.9 倍。按字符数补空格时，5 字名称比 1 字名称宽出一截，列必然错位。
/// 这里用 TMP 自己量每个字符串的宽度，再补到该次成分里最宽的一列，视觉上就是真正的三列。
///
/// 取字体的顺序：优先用 <see cref="ContentsGrid"/> 从当前 tooltip 上抓到的字体，
/// 还没显示过 tooltip 时退回 <see cref="TMP_Settings.defaultFontAsset"/>；两者都没有时
/// 按西文 8px/字粗略估算（此时即使有轻微错位也只是视觉问题，不影响内容）。
/// </summary>
internal static class WaterColumns
{
    private static TMP_Text? _probe;
    private static readonly Dictionary<string, float> Cache = new(StringComparer.Ordinal);
    private static float _space = -1f;

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
            tmp.fontSize = 16f;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            _probe = tmp;
        }
        catch (Exception ex)
        {
            Log.Warn("WaterColumns.Probe", ex);
        }
    }

    /// <summary>字符串宽度（像素，按 16 号字；只用于比较大小与补位，绝对值不重要）。</summary>
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
        catch
        {
            width = text!.Length * 8f;
        }
        Cache[text!] = width;
        return width;
    }

    /// <summary>
    /// 一个空格的宽度。TMP 对纯空格串的 GetPreferredValues 会返回 0（末尾空白被裁），
    /// 所以用「0 0」与「00」的差来反推；再不行就按数字宽的一半兜底。
    /// </summary>
    internal static float SpaceWidth()
    {
        if (_space > 0f) return _space;
        try
        {
            float digit = Measure("0");
            float diff = Measure("0 0") - 2f * digit;
            _space = diff > 0.5f ? diff : Math.Max(1f, digit * 0.5f);
        }
        catch { _space = 4f; }
        return _space;
    }

    /// <summary>把字符串补到目标像素宽（补普通空格），返回补好后的文本。</summary>
    internal static string PadTo(string? text, float targetWidth)
    {
        text ??= "";
        float missing = targetWidth - Measure(text);
        float space = SpaceWidth();
        int count = missing > 0.5f && space > 0.1f ? (int)Math.Ceiling(missing / space) : 0;
        return count > 0 ? text + new string(' ', count) : text;
    }

}
