#if ITEMDETAILS_SELFTEST
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ItemDetails;

/// <summary>
/// 一次性排查探针：ModData/item_details/crateprobe.flag（内容为存档槽位 ID）。
/// 读档后不做任何修改、不存档，只把锁定物资箱的状态写进 crateprobe.txt 然后退出：
///   - 存档里每个 sec_box / evidence_box / med_box / eng_box / service_box 的标签、内容物件数；
///   - 现造的 LootCrateSecurity / LootCrateEvidence（原版会用箱子填充表填满并上锁）作对照；
///   - 解锁后内容物是否仍在、StorageInfo 能否读到、内容物图标能不能取到精灵。
/// 只在 SelfTest 构建里编译。
/// </summary>
internal static class CrateProbe
{
    private const string TriggerFile = "crateprobe.flag";
    private const string ReportFile = "crateprobe.txt";

    private static string _modId = "item_details";
    private static int _slot = -1;
    private static bool _armed;
    private static readonly StringBuilder Report = new();

    internal static void MaybeStart(string modId)
    {
        try
        {
            if (!ModHelper.ModDataFileExists(modId, TriggerFile)) return;
            _modId = modId;
            string text = (ModHelper.ReadModDataFileText(modId, TriggerFile) ?? "").Trim();
            ModHelper.DeleteModDataFile(modId, TriggerFile);
            if (!int.TryParse(text, out _slot) || _slot < 0) { Finish(false, "flag 不是有效槽位：" + text); return; }
            _armed = true;
            Write("RUNNING", "等待读取存档槽位 " + _slot);
            ModHook.OnGameLoadedLate += Arm;
            var go = new GameObject("ItemDetails_CrateProbeAutoLoad");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AutoLoader>();
        }
        catch (Exception ex)
        {
            Log.Error("CrateProbe.MaybeStart", ex);
            if (_armed) Finish(false, "启动失败：" + ex);
        }
    }

    private sealed class AutoLoader : MonoBehaviour
    {
        private float _elapsed;
        private float _requestedAt = -1f;
        private int _tries;
        private bool _done;

        private void Update()
        {
            if (_done || !_armed) return;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < 4f) return;
            try
            {
                bool loaded;
                try { loaded = PlayerStore.Instance != null; } catch { loaded = false; }
                if (loaded) { _done = true; Arm(); return; }
                if (_requestedAt >= 0f && Time.unscaledTime - _requestedAt < 20f) return;
                if (_tries >= 3) { _done = true; Finish(false, "自动读档失败"); return; }
                var slots = SaveUIManager.GetOrderedSlotIds();
                if (slots == null || !slots.Contains(_slot)) { _done = true; Finish(false, $"槽位 {_slot} 不存在"); return; }
                var menu = MainMenuUIController.Instance;
                var newGame = NewGameData.Instance;
                if (menu == null || newGame == null) return;
                _tries++;
                _requestedAt = Time.unscaledTime;
                newGame.saveSlot = _slot;
                newGame.saveFile = null;
                menu.StartNewGameClick();
            }
            catch (Exception ex) { Log.Warn("CrateProbe.AutoLoad", ex); }
        }
    }

    private static bool _started;

    private static void Arm()
    {
        try
        {
            ModHook.OnGameLoadedLate -= Arm;
            if (!_armed || _started) return;
            if (PlayerStore.Instance.saveSlotId != _slot) { Finish(false, $"读到的槽位是 {PlayerStore.Instance.saveSlotId}"); return; }
            _started = true;
            var go = new GameObject("ItemDetails_CrateProbe");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }
        catch (Exception ex) { Log.Error("CrateProbe.Arm", ex); Finish(false, "无法启动：" + ex); }
    }

    private sealed class Runner : MonoBehaviour
    {
        private float _elapsed;
        private float _readyAt = -1f;
        private bool _done;

        private void Update()
        {
            if (_done) return;
            _elapsed += Time.deltaTime;
            if (_readyAt < 0f)
            {
                GameItem? probe = null;
                try { probe = DirectoryMaster.Create<GameItem>("plasma_fuel"); } catch { }
                if (probe == null) { if (_elapsed > 40f) { _done = true; Finish(false, "物品目录一直没就绪"); } return; }
                try { probe.Destroy(); } catch { }
                _readyAt = _elapsed;
                return;
            }
            if (_elapsed - _readyAt < 2f) return;
            _done = true;
            try
            {
                Report.AppendLine($"slot={_slot}");
                Report.AppendLine($"设置：InfoEnhance={ModSettings.InfoEnhance} ValueEnhance={ModSettings.ValueEnhance} " +
                                  $"Container={ModSettings.Container} ContentsGrid={ModSettings.ContentsGrid} SealedBox={ModSettings.SealedBox}");
                foreach (var (id, item) in WalkCrates()) Dump("存档 " + id, item);

                foreach (var (name, make) in new (string, Func<GameItem>)[] {
                    ("LootCrateSecurity", PreBuiltItemHelper.LootCrateSecurity),
                    ("LootCrateEvidence", PreBuiltItemHelper.LootCrateEvidence),
                    ("LootCrateMedical", PreBuiltItemHelper.LootCrateMedical),
                })
                {
                    GameItem crate;
                    try { crate = make(); } catch (Exception ex) { Report.AppendLine($"[{name}] 造不出来：{ex.GetType().Name} {ex.Message}"); continue; }
                    Report.AppendLine();
                    Dump(name + "（锁定）", crate);
                    var grids = StorageInfo.GridStorage(crate);
                    Report.AppendLine($"  StorageInfo.GridStorage：{(grids == null ? "null" : grids.Count + " 个网格")}" +
                                      (grids == null ? "" : $"，ChildItems={StorageInfo.ChildItems(grids).Count}"));
                    try { LockHelper.UnlockContainer(crate, force: true); } catch (Exception ex) { Report.AppendLine("  解锁异常：" + ex.GetType().Name); }
                    Report.AppendLine();
                    Dump(name + "（解锁后）", crate);
                    grids = StorageInfo.GridStorage(crate);
                    Report.AppendLine($"  StorageInfo.GridStorage：{(grids == null ? "null" : grids.Count + " 个网格")}" +
                                      (grids == null ? "" : $"，ChildItems={StorageInfo.ChildItems(grids).Count}"));
                    try { crate.Destroy(); } catch { }
                }

                var cw = EmporiumEntry.Instance.invElement;
                Report.AppendLine();
                Report.AppendLine($"存档里其它 sec_box 类：{string.Join(", ", cw.childItems.Select(x => x.identifier))}");
                Finish(true, Report.ToString());
            }
            catch (Exception ex) { Finish(false, Report + Environment.NewLine + "探针异常：" + ex); }
        }
    }

    /// <summary>主仓库/后仓及一层子容器里的箱子类物品。</summary>
    private static IEnumerable<(string, GameItem)> WalkCrates()
    {
        var ids = new HashSet<string> { "sec_box", "evidence_box", "med_box", "eng_box", "service_box" };
        foreach (var root in new[] { EmporiumEntry.Instance.invElement, EmporiumEntry.Instance.backInvinvElement })
        {
            foreach (var item in root.childItems.ToList())
            {
                if (ids.Contains(item.identifier)) yield return ("根目录/" + item.identifier, item);
                foreach (var grid in StorageInfo.GridStorage(item) ?? new List<GameGridInventory>())
                    foreach (var child in grid.childItems.ToList())
                        if (ids.Contains(child.identifier)) yield return ($"{item.identifier} 内/{child.identifier}", child);
            }
        }
    }

    private static void Dump(string label, GameItem item)
    {
        var sb = new StringBuilder();
        sb.Append($"[{label}] id={item.identifier} 名称={GameRead.ItemDisplayName(item)} 外形={Shape(item)}");
        sb.Append($" LOCKED_CONTAINER_TAG={Tag(item, "LOCKED_CONTAINER_TAG")} LOCK_STATUS={BoolTag(item, "LOCK_STATUS")}");
        sb.Append($" SUPPLY_CRATE={Type(item, "SUPPLY_CRATE")} IGNORE_CHILD_VALUE={Tag(item, "IGNORE_CHILD_VALUE")}");
        sb.Append($" 锁ID={StrTag(item, "LOCK_ID")} 箱型={StrTag(item, "LOOT_CRATE_ID_STRING")} 已扫描={Tag(item, "LOOT_CRATE_SCANNED_TAG")}");
        Report.AppendLine(sb.ToString());

        try
        {
            var win = item.contentWindow;
            Report.AppendLine($"  contentWindow：{(win == null ? "null" : $"isLocked={win.isLocked} childElement={win.childElement?.GetType().Name ?? "null"} child={win.child?.GetType().Name ?? "null"}")}");
            var grids = StorageInfo.GridStorage(item);
            if (grids == null) { Report.AppendLine("  网格：null"); return; }
            int total = 0;
            foreach (var g in grids)
            {
                var children = g.childItems;
                total += children.Count;
                var detail = children.Select(c =>
                {
                    var icon = (c as GameItemElement)?.background?.image?.sprite;
                    return $"{c.identifier}×{Math.Max(1, c.unitCount)}{(icon == null ? "(无图标)" : "")}";
                });
                Report.AppendLine($"  网格 {g.inventoryShape?.width}x{g.inventoryShape?.height}：{children.Count} 件" +
                                  (children.Count > 0 ? " → " + string.Join(", ", detail) : "（空）"));
            }
            Report.AppendLine($"  合计 {total} 件；LockedContainer.IsLocked={LockedContainer.IsLocked(item)}");
        }
        catch (Exception ex) { Report.AppendLine("  读取异常：" + ex.GetType().Name + " " + ex.Message); }
    }

    private static string Shape(GameItem item)
    {
        try { return item.shape == null ? "?" : $"{item.shape.width}x{item.shape.height}"; }
        catch { return "?"; }
    }

    private static string Tag(GameItem item, string tag)
    {
        try { return item.IsTag(tag) ? "是" : "否"; } catch { return "?"; }
    }

    private static string Type(GameItem item, string type)
    {
        try { return item.IsGameItemType(type) ? "是" : "否"; } catch { return "?"; }
    }

    private static string BoolTag(GameItem item, string tag)
    {
        try { return item.IsTag(tag) ? item.GetTagReadonly(tag).GetBool().ToString() : "无"; } catch { return "?"; }
    }

    private static string StrTag(GameItem item, string tag)
    {
        try { return item.IsTag(tag) ? item.GetTagReadonly(tag).GetString() : "无"; } catch { return "?"; }
    }

    private static void Write(string status, string details)
    {
        try { ModHelper.WriteModDataFileText(_modId, ReportFile, $"status={status}{Environment.NewLine}{details}"); }
        catch (Exception ex) { Log.Error("CrateProbe.Write", ex); }
    }

    private static void Finish(bool ok, string details)
    {
        _armed = false;
        Write(ok ? "DONE" : "FAIL", details);
        Log.Msg($"[CrateProbe] {(ok ? "DONE" : "FAILED")}{Environment.NewLine}{details}");
        Application.Quit(ok ? 0 : 1);
    }
}
#endif
