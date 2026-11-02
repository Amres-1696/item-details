using System;
using System.Collections.Generic;

namespace ItemDetails;

/// <summary>
/// 密封箱预览：把带锁容器（保险箱 / 枪柜等）的内容物交给 <see cref="ContentsGrid"/> 显示，
/// 并在额外窗口标题中给出箱内估价。正文不生成文字清单、数量摘要或独立价值行。
/// 嵌套容器只计数、不递归求值；任何子项被跳过时，估价以“+”表示还有未计入部分。
///
/// ⚠️ 这是**透视**功能：原版必须先开锁才能看到箱内。
/// 开关由调用方（ModEntry）判断，模块本身不认识配置。
/// </summary>
internal static class SealedBoxInfo
{
    /// <summary>异常数据上限：超过就不显示内容物。</summary>
    private const int MaxChildren = 512;
    private const int MaxEntries = 16;
    private const float CacheSeconds = 5f;
    private const float ChildCacheSeconds = 10f;

    private sealed class ChildValue
    {
        internal long Fingerprint;
        internal bool FingerprintValid;
        internal float At;
        internal long Unit;
        internal bool Read;
    }

    private sealed class Entry
    {
        internal GameItem Item = null!;
        internal GameGridInventory Grid = null!;
        internal float At;
        internal List<ContainerItemSignature> Items = new();
        internal string Estimate = "";
        internal readonly Dictionary<GameItem, ChildValue> Values =
            new(ReferenceIdentityComparer<GameItem>.Instance);
    }

    private static readonly List<Entry> Entries = new();

    internal static void Stop() => Entries.Clear();

    /// <param name="allowGrid">tooltip 正文才传 true：允许把内容物交给图标格。</param>
    internal static List<TooltipLine> Collect(GameItem? item, bool allowGrid = false)
    {
        var lines = new List<TooltipLine>();
        try
        {
            if (!allowGrid || item == null || !LockedContainer.IsLocked(item)
                || ThirdPartyProviders.HandlesTooltip(item, ItemDetailsTooltipSection.Contents)) return lines;

            GameGridInventory? grid = null;
            try
            {
                var cw = item.contentWindow;
                if (cw != null) grid = cw.child as GameGridInventory;
            }
            catch (Exception ex) { Log.Warn("SealedBox.Window", ex); return lines; }
            if (grid == null) return lines;

            List<GameItem>? items = null;
            try { items = grid.childItems; }
            catch (Exception ex) { Log.Warn("SealedBox.Items", ex); }
            if (items == null || items.Count == 0) return lines;
            if (items.Count > MaxChildren)
            {
                Log.Warn("SealedBox.Items", $"内容物 {items.Count} 件，超过上限 {MaxChildren}，跳过");
                return lines;
            }

            string estimate = EstimateTitleValue(item, grid, items);
            // 没有文字回退：图标格不可用时，本次就不显示内容物与估价。
            _ = ContentsGrid.Claim(item, items, sealedBox: true, sealedEstimate: estimate);
        }
        catch (Exception ex)
        {
            Log.Warn("SealedBoxInfo", ex);
        }
        return lines;
    }

    /// <summary>标题中的箱内估价；价格指纹与内容签名分缓存，变化最迟 5 秒刷新。</summary>
    internal static string EstimateTitleValue(GameItem item, GameGridInventory grid, List<GameItem> items)
    {
        float now = UnityEngine.Time.unscaledTime;
        Entry? entry = Find(item, grid);
        if (entry != null && now - entry.At <= CacheSeconds
            && ContainerItemSignature.Matches(entry.Items, items)) return entry.Estimate;

        if (entry == null)
        {
            entry = new Entry { Item = item, Grid = grid };
            Entries.Add(entry);
        }

        long sum = 0;
        bool skipped = false;
        foreach (var child in items)
        {
            if (child == null) { skipped = true; continue; }
            int stack = Math.Max(1, ContainerItemSignature.ReadStack(child));

            // 嵌套容器只计数，不展开另一棵库存树。
            if (child.contentWindow != null) { skipped = true; continue; }

            long unit = ReadUnitValue(entry, child, now, out bool read);
            if (!read) { skipped = true; continue; }
            if (unit <= 0) continue;
            if (!StackValue.TryTotal(unit, stack, out long lineTotal)
                || lineTotal > long.MaxValue - sum) skipped = true;
            else sum += lineTotal;
        }

        entry.At = now;
        entry.Items = ContainerItemSignature.Capture(items);
        entry.Estimate = sum + (skipped ? "+" : "");
        PruneChildren(entry, items);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        return entry.Estimate;
    }

    private static long ReadUnitValue(Entry entry, GameItem child, float now, out bool read)
    {
        bool fingerprintValid = ItemValueFingerprint.TryRead(child, out long fingerprint);
        if (entry.Values.TryGetValue(child, out var cached)
            && fingerprintValid && cached.FingerprintValid
            && cached.Fingerprint == fingerprint && now - cached.At <= ChildCacheSeconds)
        {
            read = cached.Read;
            return cached.Unit;
        }

        long unit = 0;
        read = false;
        try
        {
            unit = child.GetCurrentValue(useRetailMarkup: false, withChild: true,
                                         includeEvents: true, forceMarkup: false);
            read = true;
        }
        catch { }

        entry.Values[child] = new ChildValue
        {
            Fingerprint = fingerprint,
            FingerprintValid = fingerprintValid,
            At = now,
            Unit = unit,
            Read = read,
        };
        return unit;
    }

    private static Entry? Find(GameItem item, GameGridInventory grid)
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];
            if (ReferenceEquals(entry.Item, item) && ReferenceEquals(entry.Grid, grid)) return entry;
        }
        return null;
    }

    private static void PruneChildren(Entry entry, List<GameItem> items)
    {
        if (entry.Values.Count == 0) return;
        var present = new HashSet<GameItem>(ReferenceIdentityComparer<GameItem>.Instance);
        foreach (var child in items) if (child != null) present.Add(child);
        List<GameItem>? removed = null;
        foreach (var child in entry.Values.Keys)
        {
            if (present.Contains(child)) continue;
            (removed ??= new List<GameItem>()).Add(child);
        }
        if (removed != null) foreach (var child in removed) entry.Values.Remove(child);
    }
}
