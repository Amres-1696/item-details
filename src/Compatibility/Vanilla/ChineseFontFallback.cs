using System;
using TMPro;
using UnityEngine;

namespace ItemDetails;

/// <summary>
/// 给 TMP 补一个全局中文字体回退。
///
/// 背景：F8 控制台（Quantum Console）把 <c>QuantumTheme.Font</c> 套到所有日志文本上，
/// 那套西文字体没有中文字形。日志里出现中文时，TMP 每次重建文本都会写一条
/// <c>The character with Unicode value ... was not found</c>，同时控制台把中文画成空框，
/// 看起来就是日志刷屏。
///
/// 修法不是改日志语言，而是把游戏自带的中文字体登记到
/// <see cref="TMP_Settings.fallbackFontAssets"/>：TMP 在字体自身和
/// <c>fallbackFontAssetTable</c> 都找不到字形后，最后会查这张全局表。
/// 这样控制台、模组列表以及其它只配了西文字体的 TMP 文本都能显示中文。
///
/// 只加一次；字体资源来自游戏自带的 <see cref="FontLoader"/>，不打包也不复制字体文件。
/// 日志文案故意全用英文：本类失败时控制台可能仍然缺字，不能再用中文写失败原因。
/// </summary>
internal static class ChineseFontFallback
{
    /// <summary>英文兜底名，供全局表里已经有同名字体时判断，避免重复添加。</summary>
    private const string AssetNameHint = "NotoSansSC";

    private static TMP_FontAsset? _registered;

    internal static void Apply()
    {
        try
        {
            var fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks == null)
            {
                Log.Warn("TmpFallback.Settings", "TMP_Settings has no fallback font list; console may still show boxes");
                return;
            }

            var font = FindChineseFont();
            if (font == null)
            {
                Log.Warn("TmpFallback.Font", "simplified Chinese font asset not found; console may still show boxes");
                return;
            }

            if (fallbacks.Contains(font))
            {
                Log.Msg($"TmpFallback: '{font.name}' is already a global fallback font");
                return;
            }

            // TMP 遍历全局表时遇到 null 就停下；插到第一个空位之前，确保新项真的会被查到。
            int gap = -1;
            for (int i = 0; i < fallbacks.Count; i++)
            {
                if (fallbacks[i] == null) { gap = i; break; }
            }
            if (gap >= 0) fallbacks.Insert(gap, font);
            else fallbacks.Add(font);

            _registered = font;
            Log.Msg($"TmpFallback: added '{font.name}' as a global fallback font");
        }
        catch (Exception ex)
        {
            Log.Warn("TmpFallback.Apply", ex);
        }
    }

    /// <summary>撤掉本模组添加的那一项；别的模组已经加过就不动它。</summary>
    internal static void Remove()
    {
        var font = _registered;
        _registered = null;
        if (font == null) return;

        try
        {
            var fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks != null && fallbacks.Contains(font)) fallbacks.Remove(font);
        }
        catch { }
    }

    /// <summary>
    /// 优先用游戏 FontLoader 暴露的简体中文字体；它缺失时再按资源名找已加载的字体，
    /// 避免只因为主菜单还没建好 FontLoader 就放弃。
    /// </summary>
    private static TMP_FontAsset? FindChineseFont()
    {
        try
        {
            var loader = FontLoader.Instance;
            if (loader != null && loader.simplifiedChineseFont != null) return loader.simplifiedChineseFont;

            // Instance 只在 Awake 里赋值；场景切换或加载较早时组件可能已经在，但还没轮到 Awake。
            foreach (var candidate in Resources.FindObjectsOfTypeAll<FontLoader>())
            {
                if (candidate != null && candidate.simplifiedChineseFont != null) return candidate.simplifiedChineseFont;
            }
        }
        catch { }

        try
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font == null || string.IsNullOrEmpty(font.name)) continue;
                if (font.name.IndexOf(AssetNameHint, StringComparison.OrdinalIgnoreCase) >= 0) return font;
            }
        }
        catch { }

        return null;
    }
}
