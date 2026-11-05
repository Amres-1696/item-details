#if ITEMDETAILS_SELFTEST
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ItemDetails;

/// <summary>
/// 一次性测试套件：把指定存档改造成"方便逐项看 Item Details 功能"的样子，然后存档退出。
///
/// 触发：<c>ModData/item_details/testkit.flag</c>，内容是要改造的存档槽位 ID。
/// 启动游戏不带参数（避免 Steam 的自定义参数确认框），自动读取该槽位后：
///   1. 清空主仓库与后仓里原有的物品；
///   2. 造两个大储藏舱（storage_bay_large），各用扩容套件扩到 5 个标签页（每页 17×10），
///      每页按主题命名；再造三个机器舱（machine_bay_ext，外形 4×4、内部 11×14）。
///      储藏舱不收机器和非嵌套容器（密封箱、档案夹等），这些进机器舱。都优先放后仓，放不下再放主仓库；
///   3. 调用原版调试面板的套件与按主题列出的物品，装进对应标签页；
///      每种机器在主仓库留一台（主仓库放满为止），重复的和放不下的进机器舱；
///      主仓库的机器尽量装上满电电池、两个随机模组和对应原料；
///   4. 调用原版 PlayerStore.SaveGame() 写回该槽位，写报告 testkit.txt 后退出。
///
/// 物品全部由游戏自己的工厂与套件生成，标签和子容器完整；不认识的 ID 跳过并记入报告，
/// 全部可用 ID 另写到 testkit_ids.txt。只在 SelfTest 构建里编译，正常 Release 不含这段代码。
/// </summary>
internal static class TestKit
{
    private const string TriggerFile = "testkit.flag";
    private const string ReportFile = "testkit.txt";
    private const string IdsFile = "testkit_ids.txt";

    private static string _modId = "item_details";
    private static int _slot = -1;
    private static bool _armed;
    private static readonly StringBuilder Report = new();

    // 标签页名，游戏限 12 字
    private const string TabAnimals = "养殖与肉类";
    private const string TabFarm = "种植与酿酒";
    private const string TabAppraisal = "鉴定与真伪";
    private const string TabWater = "水质与净水";
    private const string TabIllicit = "违禁武器彩票";
    private const string TabMaterials = "材料化学电池";
    private const string TabGoods = "杂货与密封箱";
    private const string TabDocs = "文档与书";
    private const string TabMachines = "机器与模组";
    private const string TabOverflow = "其他";

    /// <summary>两个储藏舱各自的标签页。</summary>
    private static readonly (string Label, string[] Tabs)[] BayLayout =
    {
        ("测试物品 A", new[] { TabAnimals, TabFarm, TabAppraisal, TabWater, TabIllicit }),
        ("测试物品 B", new[] { TabMaterials, TabGoods, TabDocs, TabMachines, TabOverflow }),
    };

    private const int MachineBayCount = 3;

    /// <summary>主仓库每种留一台的机器，按优先级排；放不下的进「机器与模组」页。</summary>
    private static readonly string[] Machines =
    {
        "water_purifier", "furnace", "wine_rack", "industrial_fridge", "fridge", "battery_hub", "evaporator",
        "moisture_farm", "chem_finisher", "heating_plate", "blender", "bottle_printer", "current_converter",
        "turbo_booster_adv", "desequencer", "plant_dna_manipulator", "deal_maker", "livestock_feed_dispenser",
        "turbo_booster", "vacuum_robot", "security_alarm", "printer", "labeler",
    };

    private static readonly (string Tab, string[] Ids)[] Groups =
    {
        (TabAppraisal, new[]
        {
            "hand", "hand", "hand", "hand_steel", "ribwich", "ribwich", "ribwich", "plasma_fuel", "plasma_fuel",
            "plasma_fuel", "energy_credit_counterfeit", "counterfeit", "packet_red_cigarette", "cigarette_color",
            "black_lamp", "magnifier", "logo_checker", "metal_scanner", "aug_scanner", "gene_scanner",
            "power_analyser", "spectral_analyser", "plant_analyser", "chem_scanner", "aquascan",
        }),
        (TabWater, new[]
        {
            "water_test1", "water_jug", "water_pitcher", "canteen", "bottled_water", "bottled_water_premium",
            "large_bottled_water", "small_bottled_water", "water_ration", "water_ration_small", "water_tank",
            "portable_water_purifier", "water_tablet_container", "water_tablet", "water_filter", "water_filter_adv",
            "uv_filter", "hydroreed_filter", "water_test_strip", "water_test_container", "full_water_test_strip_box",
        }),
        (TabFarm, new[]
        {
            "nutrifruit", "nutrifruit", "wine_berry", "wine_berry", "wine_berry", "nutrifruit_seed", "fillerweed_seed",
            "lifeweed", "hydroreed_seed", "kotton_seed", "dream_cap_seed", "nutrifruit_seed_packet",
            "plant_dna_disk", "wine_bottle", "wine_bottle", "wine_yeast", "wine_yeast_red", "wine_superyeast",
            "red_beer", "empty_beer_bottle", "organic_waste",
        }),
        (TabIllicit, new[]
        {
            "lottery_ticket", "lottery_ticket", "lottery_ticket", "scratcher", "scratcher", "scratcher",
            "scratcher_stack", "gamblers_coin", "restricted_chemical", "c4", "frag_grenade", "smoke_grenade",
            "black_injector", "unlicensed_phagimycin_pill", "unlicensed_phagimycin_pill_bottle", "phagimycin_pill",
            "phagimycin_pill_bottle", "oxycodone_pill_bottle", "blood_bag", "organ_heart", "organ_kidney",
            "organ_transport_box", "serum_red", "med_bottle_red", "zyanide_pill",
        }),
        (TabMaterials, new[]
        {
            "metal_ingot", "metal_ingot", "rare_ore", "common_ore", "scrap_metal", "steel_sheet", "steel_sheet_rough",
            "common_electronic", "rare_electronic", "common_chemical", "crowbar", "screwdriver", "toolbox",
            "wire_cutter", "lockpicks", "kitchen_knife", "flashlight", "energy_credit", "energy_credit",
            "energy_credit_ext", "energy_credit_breeder", "powerblock",
        }),
        (TabGoods, new[]
        {
            "lootbox_medium", "luxury_supply_crate", "expedition_box", "lunch_box",
            "firstaid_box", "first_aid_box", "satchel", "backpack_small", "fanny_pack", "plastic_bag",
        }),
        (TabDocs, new[]
        {
            "newspaper", "handnote", "dossier", "tutorial_book", "wine_book", "ingot_book", "water_guide",
            "hydroponic_guide", "chemistry_guide", "hand_guide", "ribwich_guide", "fuel_guide", "cigarette_guide",
            "cigarette_color_guide", "injector_guide", "stamp_guide",
        }),
        (TabMachines, new[]
        {
            "random_performance_module", "random_efficiency_module", "random_quality_module", "random_eco_module",
            "random_overclock_module", "random_fineness_module", "equipement_module_performance",
            "equipement_module_efficiancy", "equipement_module_eco", "equipement_module_overclock",
            "system_module_performance", "system_module_quality", "furnace_module_blast",
            "evaporator_module_solvent_recovery", "chem_module", "storage_bay_expansion_kit",
        }),
    };

    /// <summary>KeepMachines：套件带来的机器是否每种留一台在主仓库（损坏机器套件全部进机器页）。</summary>
    private static readonly (string Tab, string Name, bool KeepMachines, Action Spawn)[] Kits =
    {
        (TabAnimals, "循环水养殖（RAS 与鱼）", true, DebugPanel.SpawnAquacultureKit),
        (TabFarm, "水培", true, DebugPanel.SpawnHydroponicKit),
        (TabFarm, "植物 DNA", true, DebugPanel.SpawnPlantDnaKit),
        (TabFarm, "种植", true, DebugPanel.SpawnFarmingKit),
        (TabAnimals, "鼠类养殖", true, DebugPanel.SpawnRatKit),
        (TabIllicit, "枪械", true, DebugPanel.SpawnGunKit),
        (TabIllicit, "彩票", true, DebugPanel.SpawnLotteryKit),
        (TabIllicit, "走私", true, DebugPanel.SpawnSmugglerKit),
        (TabAppraisal, "香烟检查", true, DebugPanel.SpawnCigaretteInspectionKit),
        (TabWater, "水质检测", true, DebugPanel.SpawnWaterTestingKit),
        (TabMaterials, "化学", true, DebugPanel.SpawnChemKit),
        (TabMaterials, "熔炉锭纯度", true, IngotPurityHelper.SpawnFurnaceIngotTestingKit),
        (TabMachines, "损坏机器", false, DebugPanel.SpawnBrokenMachineKit),
        (TabGoods, "食品", true, DebugPanel.SpawnMarketingFood),
        (TabGoods, "医疗", true, DebugPanel.SpawnMarketingMedical),
        (TabGoods, "日用品", true, DebugPanel.SpawnMarketingHousehold),
        (TabMaterials, "材料", true, DebugPanel.SpawnMarketingMaterials),
        (TabIllicit, "黑货", true, DebugPanel.SpawnMarketingShady),
        (TabOverflow, "超科技", true, DebugPanel.SpawnHypertechItems),
    };

    // ---------------- 触发与读档 ----------------

    internal static void MaybeStart(string modId)
    {
        try
        {
            if (!ModHelper.ModDataFileExists(modId, TriggerFile)) return;
            _modId = modId;
            string text = (ModHelper.ReadModDataFileText(modId, TriggerFile) ?? "").Trim();
            ModHelper.DeleteModDataFile(modId, TriggerFile);
            if (!int.TryParse(text, out _slot) || _slot < 0)
            {
                Finish(false, "testkit.flag 里不是有效的存档槽位 ID：'" + text + "'");
                return;
            }
            _armed = true;
            WriteReport("RUNNING", $"等待读取存档槽位 {_slot}");
            ModHook.OnGameLoadedLate += Arm;
            var go = new GameObject("ItemDetails_TestKitAutoLoad");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AutoLoader>();
            Log.Msg($"[TestKit] armed for save slot {_slot}");
        }
        catch (Exception ex)
        {
            Log.Error("TestKit.MaybeStart", ex);
            if (_armed) Finish(false, "启动失败：" + ex);
        }
    }

    /// <summary>主菜单自动读取指定槽位；做法同 SelfTest.AutoLoader（LoadGameUI.LoadSlot 的赋值 + StartNewGameClick）。</summary>
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
                try { loaded = PlayerStore.Instance != null; }
                catch { loaded = false; }
                if (loaded) { _done = true; Arm(); return; }

                if (_requestedAt >= 0f && Time.unscaledTime - _requestedAt < 20f) return;
                if (_tries >= 3) { _done = true; Finish(false, "自动读档失败"); return; }

                var slots = SaveUIManager.GetOrderedSlotIds();
                if (slots == null || !slots.Contains(_slot))
                {
                    _done = true;
                    Finish(false, $"存档槽位 {_slot} 不存在");
                    return;
                }
                var menu = MainMenuUIController.Instance;
                var newGame = NewGameData.Instance;
                if (menu == null || newGame == null) return;

                _tries++;
                _requestedAt = Time.unscaledTime;
                newGame.saveSlot = _slot;
                newGame.saveFile = null;
                menu.StartNewGameClick();
                Log.Msg($"[TestKit] loading save slot {_slot} (try {_tries})");
            }
            catch (Exception ex)
            {
                Log.Warn("TestKit.AutoLoad", ex);
            }
        }
    }

    private static bool _runnerStarted;

    private static void Arm()
    {
        try
        {
            ModHook.OnGameLoadedLate -= Arm;
            if (!_armed || _runnerStarted) return;
            if (PlayerStore.Instance.saveSlotId != _slot)
            {
                Finish(false, $"读到的槽位 {PlayerStore.Instance.saveSlotId} 不是要改造的 {_slot}，不写入");
                return;
            }
            _runnerStarted = true;
            var go = new GameObject("ItemDetails_TestKit");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }
        catch (Exception ex)
        {
            Log.Error("TestKit.Arm", ex);
            Finish(false, "无法启动：" + ex);
        }
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
                if (probe == null)
                {
                    if (_elapsed > 40f) { _done = true; Finish(false, "物品目录一直没有就绪"); }
                    return;
                }
                try { probe.Destroy(); } catch { }
                _readyAt = _elapsed;
                return;
            }
            // 场景里其它管理器（窗口、店面）再给两秒落定
            if (_elapsed - _readyAt < 2f) return;

            _done = true;
            try
            {
                Build();
                PlayerStore.Instance.SaveGame();
                Finish(true, Report.ToString());
            }
            catch (Exception ex)
            {
                Finish(false, Report + Environment.NewLine + "执行失败（未存档）：" + ex);
            }
        }
    }

    // ---------------- 改造 ----------------

    private static GameGridInventory Main => EmporiumEntry.Instance.invElement;
    private static GameGridInventory Back => EmporiumEntry.Instance.backInvinvElement;

    /// <summary>标签页名 → 该页格子。</summary>
    private static readonly Dictionary<string, GameGridInventory> Tabs = new();
    private static readonly Dictionary<string, int> TabCounts = new();
    private static readonly List<GameItem> MachineBays = new();
    private static readonly HashSet<string> MachinesInMain = new();
    private static HashSet<string>? _known;
    private static int _lost;
    private static readonly List<string> Missing = new();

    private static void Build()
    {
        Report.AppendLine($"slot={_slot}");
        Report.AppendLine($"主仓库 {Shape(Main.inventoryShape)}，后仓 {Shape(Back.inventoryShape)}");
        LoadKnownIds();

        int removedMain = Clear(Main);
        int removedBack = Clear(Back);
        Report.AppendLine($"清空：主仓库 {removedMain} 件，后仓 {removedBack} 件");

        foreach (var (label, tabs) in BayLayout) CreateBay(label, tabs);
        if (Tabs.Count == 0) throw new InvalidOperationException("一个储藏舱都没建成，不改存档");
        for (int i = 1; i <= MachineBayCount; i++) CreateMachineBay("测试箱与机器 " + i);

        // 原版套件往主仓库放：每跑一个就把新东西挪进标签页，主仓库只留每种机器的第一台
        foreach (var (tab, name, keep, spawn) in Kits) RunKit(tab, name, keep, spawn);
        PlaceCrates();

        foreach (var (tab, ids) in Groups)
        {
            int placed = 0, wanted = 0;
            foreach (string id in ids)
            {
                var item = Spawn(id);
                if (item == null) continue;
                wanted++;
                GeneralHelper.SetItemOwned(item, own: true);
                if (Put(tab, item)) placed++;
            }
            Report.AppendLine($"[{tab}] 按清单放入 {placed}/{wanted}");
        }

        foreach (string id in Machines) PlaceMachine(id);

        Report.AppendLine();
        Report.AppendLine("各标签页件数：" + string.Join("，", TabCounts.Select(kv => $"{kv.Key} {kv.Value}")));
        foreach (var bay in MachineBays) Report.AppendLine($"{CustomName(bay)}：{Contents(bay)}");
        if (Missing.Count > 0) Report.AppendLine("游戏里不存在、已跳过的 ID：" + string.Join(", ", Missing.Distinct()));
        Report.AppendLine($"放不下而丢弃：{_lost} 件");
        Report.AppendLine($"最终：主仓库 {Main.childItems.Count} 件，后仓 {Back.childItems.Count} 件");
        foreach (var it in Main.childItems) Report.AppendLine($"  主仓库 {it.identifier}（{Shape(it)}）");
        foreach (var it in Back.childItems) Report.AppendLine($"  后仓 {it.identifier}（{Shape(it)}）");
    }

    private static void LoadKnownIds()
    {
        try
        {
            var ids = new HashSet<string>(DirectoryMaster.GetIdentifierList<GameItem>());
            foreach (string key in PreBuiltItemHelper.preBuiltItems.Keys) ids.Add(key);
            _known = ids;
            ModHelper.WriteModDataFileText(_modId, IdsFile, string.Join("\n", ids.OrderBy(x => x, StringComparer.Ordinal)));
            Report.AppendLine($"游戏物品 ID 共 {ids.Count} 个（全表见 {IdsFile}）");
        }
        catch (Exception ex)
        {
            _known = null;
            Report.AppendLine("读不到物品 ID 表，改用生成后比对：" + ex.GetType().Name);
        }
    }

    // ---------------- 储藏舱 ----------------

    private static void CreateBay(string label, string[] tabNames)
    {
        var bay = Spawn("storage_bay_large") ?? Spawn("storage_bay");
        if (bay == null) { Report.AppendLine($"[{label}] 储藏舱造不出来"); return; }
        GeneralHelper.SetItemOwned(bay, own: true);

        // 扩容到 5 页；扩容套件会被 Expand 消耗
        for (int i = 0; i < 8 && StorageExpansionHelper.CanExpand(bay); i++)
        {
            var kit = Spawn(StorageExpansionHelper.IDENTIFIER);
            if (kit == null) break;
            GeneralHelper.SetItemOwned(kit, own: true);
            StorageExpansionHelper.Expand(bay, kit);
        }

        string? where = GraphUtils.TryAcceptAll(Back, bay) == 0 ? "后仓"
            : GraphUtils.TryAcceptAll(Main, bay) == 0 ? "主仓库" : null;
        if (where == null)
        {
            Report.AppendLine($"[{label}] {bay.identifier}（{Shape(bay)}）后仓和主仓库都放不下");
            try { bay.Destroy(); } catch { }
            return;
        }

        GeneralHelper.SetCustomName(bay, label);
        var grids = TabGrids(bay);
        for (int i = 0; i < tabNames.Length; i++)
        {
            if (grids.Count == 0) break;
            int index = Math.Min(i, grids.Count - 1);
            if (i < grids.Count)
            {
                try { StorageExpansionHelper.SetTabName(bay, i, tabNames[i]); }
                catch (Exception ex) { Log.Warn("TestKit.TabName", ex); }
            }
            Tabs[tabNames[i]] = grids[index];
            TabCounts[tabNames[i]] = 0;
        }
        Report.AppendLine($"[{label}] {bay.identifier}（外形 {Shape(bay)}）放在{where}，" +
                          $"{grids.Count} 页 × {(grids.Count > 0 ? Shape(grids[0].inventoryShape) : "?")}：" +
                          string.Join(" / ", tabNames.Take(Math.Max(grids.Count, 1))));
    }

    private static void CreateMachineBay(string label)
    {
        var bay = Spawn("machine_bay_ext") ?? Spawn("machine_bay");
        if (bay == null) { Report.AppendLine($"[{label}] 机器舱造不出来"); return; }
        GeneralHelper.SetItemOwned(bay, own: true);
        string? where = GraphUtils.TryAcceptAll(Back, bay) == 0 ? "后仓"
            : GraphUtils.TryAcceptAll(Main, bay) == 0 ? "主仓库" : null;
        if (where == null)
        {
            Report.AppendLine($"[{label}] {bay.identifier}（{Shape(bay)}）后仓和主仓库都放不下");
            try { bay.Destroy(); } catch { }
            return;
        }
        GeneralHelper.SetCustomName(bay, label);
        MachineBays.Add(bay);
        var grids = TabGrids(bay);
        Report.AppendLine($"[{label}] {bay.identifier}（外形 {Shape(bay)}）放在{where}，" +
                          $"内部 {(grids.Count > 0 ? Shape(grids[0].inventoryShape) : "?")}");
    }

    private static string CustomName(GameItem bay)
    {
        try { return bay.GetTagReadonly("CUSTOM_NAME_TAG").GetString() ?? bay.identifier; }
        catch { return bay.identifier; }
    }

    private static string Contents(GameItem bay)
    {
        var names = TabGrids(bay).SelectMany(g => g.childItems).Select(x => x.identifier).ToList();
        return names.Count == 0 ? "空" : string.Join(", ", names);
    }

    private static List<GameGridInventory> TabGrids(GameItem bay)
    {
        var list = new List<GameGridInventory>();
        var element = bay.contentWindow?.childElement;
        if (element is MachineryPanelRow row && row.GetPanel("storage")?.content is MachineryTabs tabs)
        {
            for (int i = 0; i < tabs.count; i++)
                if ((tabs.GetPage(i) as MachineryIntake)?.content is GameGridInventory grid)
                    list.Add(grid);
        }
        else if (element is GameGridInventory single)
        {
            list.Add(single);
        }
        return list;
    }

    /// <summary>放进指定页；满了（或储藏舱不收）依次试「其他」页、其余各页、机器舱、后仓、主仓库，都不行才丢弃。</summary>
    private static bool Put(string tab, GameItem item)
    {
        var order = new List<string> { tab, TabOverflow };
        order.AddRange(Tabs.Keys);
        var tried = new HashSet<GameGridInventory>();
        foreach (string name in order)
        {
            if (!Tabs.TryGetValue(name, out var grid) || !tried.Add(grid)) continue;
            if (GraphUtils.TryAcceptAll(grid, item) == 0)
            {
                TabCounts[name] = TabCounts.TryGetValue(name, out int n) ? n + 1 : 1;
                return true;
            }
        }
        foreach (var bay in MachineBays)
            if (GraphUtils.TryAcceptAll(bay, item) == 0) return true;
        if (item.parentInventory == Main || item.parentInventory == Back) return true;   // 已在仓库里，就地留着
        if (GraphUtils.TryAcceptAll(Back, item) == 0 || GraphUtils.TryAcceptAll(Main, item) == 0) return true;
        Discard(item);
        return false;
    }

    // ---------------- 套件与机器 ----------------

    private static void RunKit(string tab, string name, bool keepMachines, Action spawn)
    {
        var beforeMain = new HashSet<GameItem>(Main.childItems);
        var beforeBack = new HashSet<GameItem>(Back.childItems);
        try { spawn(); }
        catch (Exception ex)
        {
            Report.AppendLine($"[{name}] 套件异常：{ex.GetType().Name} {ex.Message}");
            Log.Warn("TestKit.Kit." + name, ex);
        }
        var added = Main.childItems.Where(x => !beforeMain.Contains(x))
            .Concat(Back.childItems.Where(x => !beforeBack.Contains(x))).ToList();
        var kept = new List<string>();
        int moved = 0;
        foreach (var item in added)
        {
            if (keepMachines && IsMachine(item) && item.parentInventory == Main && MachinesInMain.Add(item.identifier))
            {
                kept.Add(item.identifier);
                continue;
            }
            if (Put(IsMachine(item) ? TabMachines : tab, item)) moved++;
        }
        Report.AppendLine($"[{name}] 新增 {added.Count} 件 → {tab} {moved} 件" +
                          (kept.Count > 0 ? "；主仓库留下 " + string.Join(", ", kept) : ""));
    }

    /// <summary>
    /// 原版填充好的上锁物资箱（5 种）与配套钥匙卡。
    /// 必须用 PreBuiltItemHelper.LootCrate*：ItemSpawner.Spawn("sec_box") 只造出空的锁定外壳，
    /// 里面一件东西都没有（存档里 childItems 为 []），悬停自然看不到内容物。
    /// 箱子带 CONTAINER_TAG，储藏舱不收，会落进机器舱；钥匙卡进「杂货与密封箱」页。
    /// </summary>
    private static void PlaceCrates()
    {
        var crates = new (string Name, Func<GameItem> Make)[]
        {
            ("治安部物资箱", PreBuiltItemHelper.LootCrateSecurity),
            ("证物箱", PreBuiltItemHelper.LootCrateEvidence),
            ("医疗物资箱", PreBuiltItemHelper.LootCrateMedical),
            ("工程物资箱", PreBuiltItemHelper.LootCrateEngineering),
            ("服务物资箱", PreBuiltItemHelper.LootCrateService),
        };
        foreach (var (name, make) in crates)
        {
            GameItem? crate = null;
            try { crate = make(); }
            catch (Exception ex) { Report.AppendLine($"[{name}] 造不出来：{ex.GetType().Name}"); Log.Warn("TestKit.Crate." + name, ex); }
            if (crate == null) continue;
            GeneralHelper.SetItemOwned(crate, own: true);
            string where = Put(TabMachines, crate) ? Where(crate) : "丢弃";
            Report.AppendLine($"[{name}] {crate.identifier} → {where}，内容物 {CrateContents(crate)} 件");
        }
        foreach (string key in new[]
                 { "sec_keycard", "sec_keycard", "med_keycard", "eng_keycard", "cmd_keycard", "ser_keycard", "sup_keycard" })
        {
            var card = Spawn(key);
            if (card == null) continue;
            GeneralHelper.SetItemOwned(card, own: true);
            Put(TabGoods, card);
        }
    }

    private static string CrateContents(GameItem crate)
    {
        try
        {
            var grids = StorageInfo.GridStorage(crate);
            return grids == null ? "?" : StorageInfo.ChildItems(grids).Count.ToString();
        }
        catch { return "?"; }
    }

    private static void PlaceMachine(string id)
    {
        var machine = Spawn(id);
        if (machine == null) return;
        GeneralHelper.SetItemOwned(machine, own: true);

        bool inMain = !MachinesInMain.Contains(id) && GraphUtils.TryAcceptAll(Main, machine) == 0;
        if (inMain) MachinesInMain.Add(id);

        var extras = new List<string> { "energy_credit", "random_performance_module", "random_efficiency_module" };
        if (id == "battery_hub") { extras.Add("energy_credit_40"); extras.Add("energy_credit_10"); }
        if (id == "water_purifier") extras.Add("water_test1");
        if (id == "furnace") { extras.Add("furnace_module_blast"); extras.Add("common_ore"); extras.Add("common_ore"); extras.Add("flux_agent"); }
        if (id == "wine_rack") { extras.Add("wine_bottle"); extras.Add("red_beer"); }
        if (id is "industrial_fridge" or "fridge") { extras.Add("raw_meat"); extras.Add("nutrifruit"); }
        if (id == "moisture_farm") extras.Add("water_jug");
        if (id == "bottle_printer") extras.Add("printer_plastic");

        var fitted = new List<string>();
        foreach (string extra in extras)
        {
            GameItem? part = extra.StartsWith("energy_credit", StringComparison.Ordinal)
                ? PreBuiltItemHelper.EnergyCredit(extra == "energy_credit_40" ? 40 : extra == "energy_credit_10" ? 10 : 100)
                : Spawn(extra);
            if (part == null) continue;
            GeneralHelper.SetItemOwned(part, own: true);
            if (GraphUtils.TryAcceptAll(machine, part) == 0) fitted.Add(extra);
            else part.Destroy();   // 机器没有对应槽位：不留散件
        }

        string where = inMain ? "主仓库" : Put(TabMachines, machine) ? Where(machine) : "丢弃";
        Report.AppendLine($"[机器] {id} → {where}：{(fitted.Count > 0 ? string.Join(", ", fitted) : "无附件")}");
    }

    private static string Where(GameItem item)
    {
        var parent = item.parentInventory;
        if (parent == Main) return "主仓库";
        if (parent == Back) return "后仓";
        foreach (var bay in MachineBays)
            if (TabGrids(bay).Any(g => g == parent)) return CustomName(bay);
        foreach (var kv in Tabs)
            if (kv.Value == parent) return kv.Key;
        return "?";
    }

    private static bool IsMachine(GameItem item)
    {
        try { return item.IsGameItemType("MACHINE"); }
        catch { return false; }
    }

    // ---------------- 小工具 ----------------

    private static GameItem? Spawn(string id)
    {
        if (_known != null && !_known.Contains(id)) { Missing.Add(id); return null; }
        GameItem? item = null;
        try { item = ItemSpawner.Spawn(id); }
        catch (Exception ex) { Log.Warn("TestKit.Spawn." + id, ex); }
        if (item != null && item.identifier != id && !PreBuiltItemHelper.preBuiltItems.ContainsKey(id))
        {
            // 不认识的 ID 会生成占位物品，不留
            try { item.Destroy(); } catch { }
            item = null;
        }
        if (item == null) Missing.Add(id);
        return item;
    }

    private static int Clear(GameGridInventory inv)
    {
        int n = 0;
        for (int i = inv.childItems.Count - 1; i >= 0; i--)
        {
            var item = inv.childItems[i];
            try { item.CloseContentWindow(includeChildren: true); } catch { }
            item.Destroy();
            n++;
        }
        return n;
    }

    private static string Shape(GameItem item)
    {
        try { return Shape(item.shape); } catch { return "?"; }
    }

    private static string Shape(GridShape? shape)
        => shape == null ? "?" : $"{shape.width}x{shape.height}";

    private static void Discard(GameItem item)
    {
        _lost++;
        Report.AppendLine($"  丢弃 {item.identifier}（{Shape(item)}）");
        try { item.Destroy(); } catch { }
    }

    // ---------------- 结果 ----------------

    private static void WriteReport(string status, string details)
    {
        try
        {
            ModHelper.WriteModDataFileText(_modId, ReportFile,
                $"status={status}{Environment.NewLine}{details}");
        }
        catch (Exception ex) { Log.Error("TestKit.WriteReport", ex); }
    }

    private static void Finish(bool success, string details)
    {
        _armed = false;
        WriteReport(success ? "DONE" : "FAIL", details);
        Log.Msg($"[TestKit] {(success ? "DONE" : "FAILED")}{Environment.NewLine}{details}");
        Application.Quit(success ? 0 : 1);
    }
}
#endif
