using System;
using System.Collections.Generic;
using ItemDetails.Integration;

namespace ItemDetails;

/// <summary>
/// 各 tooltip 追加器。顺序由 <c>ModEntry.Renderers</c> 决定，本文件只负责"某一类物品多写几行"。
///
/// 约定：模块自己判断开关与适用性，不适用就返回 false 且不写任何内容；
/// 真正的异常交给 <c>ModEntry.RunRenderers</c> 的边界统一处理，方法内不吞错。
/// </summary>
public sealed partial class ModEntry
{
    // ---------------- 真伪 ----------------
    private bool TryAppendAuthenticity(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Auth) return false;
        var r = Authenticity.Analyze(item);
        if (!r.HasData) return false;

        if (r.Faults > 0 || r.IsGenuine)
        {
            builder.Field("genuine", r.IsGenuine && r.Faults == 0);
            if (builder.Capturing) builder.Field("faults", r.FaultNames.AsReadOnly());
        }
        if (r.Faults > 0)
        {
            string names = string.Join(Loc.T("list.sep", ", "), r.FaultNames);
            builder.AddLine(Loc.T("auth.counterfeit", "× Counterfeit ({0})", names),
                RenderHandler.ColorPalette.LightRed);
        }
        else if (r.IsGenuine)
        {
            builder.AddLine(Loc.T("auth.genuine", "√ Genuine"), RenderHandler.ColorPalette.Green);
        }
        else
        {
            // 有原版检查项读取失败，且没有发现已知瑕疵 → 不下"正品"结论
            Log.Warn("Auth.Incomplete",
                $"只读到 {r.ReadableFlags}/{r.ExpectedFlags} 项，跳过真伪结论");
            return false;
        }
        return true;
    }

    // ---------------- 价值构成（机械手/汉堡/等离子） ----------------
    private bool TryAppendValue(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Value) return false;
        if (!GameRead.IsTag(item, "APPRAISAL_ITEM")) return false;
        if (ThirdPartyProviders.HasAppraisal(item)) return false;

        var r = ValueBreakdown.Analyze(item);
        if (r == null) return false;

        // 总价与完美区间合并成一行，省一行留白。Value 已把完美区间并入同一开关。
        // Analyze 已校验 r.Total == 游戏真实值，不再重复调用 TrueValue。
        string totalLine = Loc.T("value.total_line", "Total: {0}", r.Total);
        var (lo, hi) = AppraisalRanges.GamePerfect(r.Total);
        builder.Field("total", r.Total);
        builder.Field("perfectMin", lo);
        builder.Field("perfectMax", hi);
        totalLine += Loc.T("value.perfect_range", " | perfect {0}-{1}", lo, hi);
        builder.AddLine(totalLine, RenderHandler.ColorPalette.Green);

        AppendParts(builder, r);
        return true;
    }

    /// <summary>
    /// 来源明细单行输出；标签已缩短，避免原版定宽折行把数字拆断。
    ///
    /// 符号与分隔符合一：正值写「+加固60」，负值写「-破裂150」并用红字。
    /// 旧写法把符号塞进数值（「+破裂-150」），一个负项会出现两个符号，既难读也占宽。
    /// </summary>
    private void AppendParts(TooltipSections.Writer builder, ValueBreakdown.Result r)
    {
        builder.Add(Loc.T("value.from", "From: "), RenderHandler.ColorPalette.White);

        var parts = builder.Capturing ? new List<(string Label, int Amount, bool IsBase)>() : null;
        foreach (var p in r.Parts)
        {
            // 文案由纯公式层给出「键 + 英文兜底 + 参数」，这里只做解析。
            string label = Loc.T(p.Key, p.En, p.Args);
            parts?.Add((label, p.Amount, p.IsBase));

            if (p.IsBase)
            {
                builder.Add($"{label}{p.Amount}", RenderHandler.ColorPalette.White);
                continue;
            }

            if (p.Amount < 0)
                builder.Add($"-{label}{-p.Amount}", RenderHandler.ColorPalette.LightRed);
            else
                builder.Add($"+{label}{p.Amount}", RenderHandler.ColorPalette.White);
        }

        if (parts != null) builder.Field("parts", parts.AsReadOnly());
        builder.NextLine();
    }

    // ---------------- 赝品肋明治：具体原因 ----------------
    private bool TryAppendRibwichReasons(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Auth) return false;   // 赝品原因属于真伪鉴定

        var lines = RibwichCounterfeit.Collect(item);
        if (lines.Count == 0) return false;
        if (builder.Capturing) builder.Field("reasons", lines.AsReadOnly());

        builder.AddLine(Loc.T("ribwich.header", "Why it's counterfeit:"), RenderHandler.ColorPalette.LightRed);
        foreach (string s in lines)
            builder.AddLine("• " + s, RenderHandler.ColorPalette.LightRed);
        return true;
    }

    // ---------------- 第三方显式提供的鉴定分解 ----------------
    private bool TryAppendInjectedValue(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Value) return false;

        var r = ThirdPartyProviders.Appraisal(item);
        if (r == null) return false;

        string totalLine = Loc.T("value.total_line", "Total: {0}", r.Total);
        var (lo, hi) = AppraisalRanges.GamePerfect(r.Total);
        builder.Field("total", r.Total);
        builder.Field("perfectMin", lo);
        builder.Field("perfectMax", hi);
        totalLine += Loc.T("value.perfect_range", " | perfect {0}-{1}", lo, hi);
        builder.AddLine(totalLine, RenderHandler.ColorPalette.Green);

        var fields = builder.Capturing ? new List<(string Label, int Amount, bool IsBase)>() : null;
        var parts = new List<string>();
        foreach (var p in r.Parts)
        {
            if (string.IsNullOrWhiteSpace(p.Label)) continue;
            fields?.Add((p.Label, p.Amount, p.IsBase));
            string amount = p.IsBase || p.Amount < 0 ? p.Amount.ToString() : "+" + p.Amount;
            parts.Add($"{p.Label} {amount}");
        }
        if (fields != null) builder.Field("parts", fields.AsReadOnly());
        if (parts.Count > 0)
            builder.AddLine(Loc.T("generic.line", "From: {0}", string.Join(Loc.T("list.sep", ", "), parts)),
                RenderHandler.ColorPalette.White);
        return true;
    }

    // ---------------- 果实品质（BERRY_*） ----------------
    private bool TryAppendBerry(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Plant && !ModSettings.Wine) return false;

        var q = GameRead.ClassifyBerry(item);
        if (q == BerryQuality.NotBerry) return false;

        string? name = BerryQualityClassifier.DisplayName(q);
        bool showQuality = ModSettings.Plant && name != null;
        if (!showQuality && !ModSettings.Wine) return false;

        // 主体取物品自己的名字：BERRY_TAG 不是荧光莓专属，第三方果实只要沿用原版的
        // WineHelper.InitBerry 就会带上它（实测酿酒拓展的 18 种果实），写死"荧光莓"
        // 会把别人的水果一起叫成荧光莓。名字读不到就不写这行，不拿默认值凑结论。
        // 取名走当前语言（item.name 是创建时固化的，见 GameRead.ItemDisplayName）。
        string itemName = GameRead.ItemDisplayName(item);
        if (itemName.Length == 0 || itemName == "?") return false;

        // 较小用紫色：这是「发育不良但还能用」的一档，和发霉/干枯这类坏档区分开。
        var color = q switch
        {
            BerryQuality.Normal => RenderHandler.ColorPalette.Green,
            BerryQuality.Small => RenderHandler.ColorPalette.Purple,
            _ => RenderHandler.ColorPalette.Orange,
        };

        try
        {
            if (showQuality) builder.Field("quality", name!);
            if (showQuality)
                builder.Add(Loc.T("berry.line", "{0} quality: {1}", itemName, name!), color);
            // 酿酒值：这颗莓在 WineHelper.StartFermentation 里给果酿评分加的点数，灰字补在同一行末尾。
            if (ModSettings.Wine)
            {
                int wineScore = GameRead.BerryWineScore(item);
                builder.Field("wineScore", wineScore);
                string score = wineScore.ToString("+0;-0;0");
                builder.Add(showQuality
                        ? Loc.T("berry.wine", " (wine value {0})", score)
                        : Loc.T("berry.wine_value", "Wine value: {0}", score),
                    RenderHandler.ColorPalette.Gray);
            }
            builder.NextLine();
        }
        catch (Exception ex)
        {
            Log.Warn("Berry.AddLine", ex);
            return false;
        }
        return true;
    }

    // ---------------- P2 隐藏信息（真实耐久/纯度） ----------------
    private bool TryAppendHidden(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.HiddenTruth) return false;
        if (IsLiquid(item)) return false;   // 水质模块已汇总
        return AppendLines(builder, HiddenInfo.Collect(item));
    }

    // ---------------- 性价比（兜底） ----------------
    // 正常由 TooltipPipeline 写在价格块下一行；价格行没改写时才落到这里，同一次构建只写一次。
    private bool TryAppendValueEnhance(TooltipSections.Writer builder, GameItem item)
        => ValueEnhance.TryWrite(builder, item);

    // ---------------- P1 隐藏特性揭示（真假条件） ----------------
    private bool TryAppendFeatureReveal(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.HiddenTruth) return false;
        if (IsLiquid(item)) return false;   // 水质模块已汇总真假
        return AppendLines(builder, FeatureReveal.Collect(item));
    }

    // ---------------- 储存容器：压缩率 + 内容物图标格 ----------------
    private bool TryAppendStorage(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.ValueEnhance && !ModSettings.Container) return false;
        return AppendLines(builder, StorageInfo.Collect(item,
            includeRatio: ModSettings.ValueEnhance,
            includeContents: ModSettings.Container,
            allowGrid: true, builder: builder.Native));
    }

    // ---------------- 机器与槽位容器内部：滤芯、液体、工具、剩余几晚 ----------------
    private bool TryAppendMachineInterior(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Container && !ModSettings.Machines && !ModSettings.MachineBattery) return false;
        return AppendLines(builder, MachineInterior.Collect(item,
            listing: ModSettings.Container,
            adapters: ModSettings.Machines,
            hiddenTruth: ModSettings.HiddenTruth,
            purity: LiquidGate.Enabled));
    }

    // ---------------- 自学习算法模块：学习进度 ----------------
    private bool TryAppendModuleLearning(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.ModuleEffects) return false;
        return AppendLines(builder, ModuleLearning.Collect(item, builder));
    }

    // ---------------- 瑕疵/状态对价值的影响 ----------------
    private bool TryAppendConditions(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.PriceImpact) return false;
        if (IsLiquid(item)) return false;   // 水质模块已汇总，避免重复
        return AppendLines(builder, ConditionEffects.Collect(item, skipRevealed: ModSettings.HiddenTruth));
    }

    // ---------------- 违禁等级与黑市溢价 ----------------
    private bool TryAppendContraband(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Contraband) return false;

        var r = ContrabandInfo.Analyze(item);
        if (!r.HasData) return false;
        // 原版 ContrabandHelper 已经显示违禁等级，这里只补它没有的黑市溢价。
        if (r.MarkupPercent < 0) return false;
        builder.Field("markupPercent", r.MarkupPercent);

        builder.AddLine(
            Loc.T("contraband.markup", "Black market: +{0}%", r.MarkupPercent),
            RenderHandler.ColorPalette.LightTurquoise);
        return true;
    }

    // ---------------- 彩票开奖结果 ----------------
    private bool TryAppendLottery(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Lottery) return false;
        return AppendLines(builder, LotteryInfo.Collect(item, builder));
    }

    // ---------------- 液体组分：水质 / 杂质 / 药物成分 ----------------
    private bool TryAppendWater(TooltipSections.Writer builder, GameItem item)
    {
        var r = LiquidQueries.Analyze(item);
        if (r == null) return false;
        builder.Field("analysis", r.Data);
        if (LiquidQueries.TryDisplay(ItemDetailsClient.AnalysisDisplay, item, r.Data))
        {
            if (!builder.Capturing) return true;
            builder.AlreadyHandled();
        }

        bool wrote = false;

        if (!r.IsSolid)
        {
            // 液体容器：表头「成分 + 体积条 + 2000/2000mL 档位」，下面每种成分一行；
            // 三列用 TMP 的 <pos> 精确定位（见 WaterColumns），比空格补位准。
            // 三列按像素定位后比原版折行预算宽，先放宽本段 tooltip 的折行宽度。
            builder.RequireWidth(WaterTableWidth);

            AppendLines(builder, WaterColumns.Build(r, item));
            wrote = true;
        }
        else
        {
            // 固体化学品（药片、净水片等）：没有容器与体积，仍按占比列出。
            var actives = new List<string>();
            var others = new List<string>();
            foreach (var component in r.Components)
            {
                if (component.Role == "active")
                    actives.Add(component.Name + " " + WaterInfo.Amount(component.Microliters));
                else
                    others.Add(component.Name + " " + WaterLayout.PercentText(component.Microliters, r.TotalMicroliters, Loc.Culture) + "%");
            }
            if (actives.Count > 0)
            {
                builder.AddLine(Loc.T("water.actives", "Actives: ") + string.Join(Loc.T("list.sep", ", "), actives),
                    RenderHandler.ColorPalette.LightYellow);
                wrote = true;
            }
            if (others.Count > 0 || r.HiddenComponents > 0)
            {
                string label = actives.Count == 0 ? Loc.T("water.composition", "Composition: ") : Loc.T("water.other", "Other: ");
                string parts = others.Count > 0 ? string.Join(" ", others) : Loc.T("water.trace", "trace (below display threshold)");
                builder.AddLine(label + parts, RenderHandler.ColorPalette.LightCyan);
                wrote = true;
            }
        }

        string effects = r.Effects.Text;
        if (effects.Length > 0)
        {
            builder.AddLine(Loc.T("chem.effects", "Effects: {0}", effects),
                r.Effects.LethalDose > 0 || r.Effects.Toxic ? RenderHandler.ColorPalette.LightRed : RenderHandler.ColorPalette.LightYellow);
            wrote = true;
        }

        return wrote;
    }

    // ---------------- 密封箱预览（透视，默认关闭） ----------------
    private bool TryAppendSealedBox(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.SealedBox) return false;
        return AppendLines(builder, SealedBoxInfo.Collect(item, allowGrid: true, builder: builder.Native));
    }

    // ---------------- 刮刮卡结果（透视） ----------------
    private bool TryAppendScratcher(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Scratcher) return false;
        return AppendLines(builder, ScratcherInfo.Collect(item));
    }

    // ---------------- 种子性状（透视） ----------------
    private bool TryAppendSeed(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Plant) return false;

        var lines = SeedInfo.Collect(item);
        if (lines.Count == 0) return false;

        // 性状逐项一行太占版面，接在表头后写成一行
        if (builder.Capturing)
        {
            var traits = new List<(string Id, string Name, int Rank)>();
            foreach (var line in lines)
                if (line.Data is ValueTuple<string, string, int> trait) traits.Add(trait);
            builder.Field("traits", traits.AsReadOnly());
        }
        var texts = lines.ConvertAll(l => l.Text);
        builder.AddLine(Loc.T("seed.line", "Traits (unanalyzed): {0}", string.Join(Loc.T("list.sep", ", "), texts)),
            RenderHandler.ColorPalette.LightTurquoise);
        return true;
    }

    /// <summary>水质成分表的折行预算（字符数）；原版 tooltip 约 40，三列定位后需要更宽。</summary>
    private const int WaterTableWidth = 64;

    // ---------------- 含水容器：净化收益（探测版） ----------------
    private bool TryAppendWaterAdvice(TooltipSections.Writer builder, GameItem item)
    {
        var methods = LiquidQueries.GetAdvice(item);
        if (methods == null) return false;
        builder.Field("methods", methods);
        if (LiquidQueries.TryDisplay(ItemDetailsClient.AdviceDisplay, item, methods))
        {
            if (!builder.Capturing) return true;
            builder.AlreadyHandled();
        }
        if (methods.Count == 0) return builder.Capturing;

        bool wrote = false;
        foreach (var mtd in methods)
        {
            if (wrote) builder.Add("　", RenderHandler.ColorPalette.Gray);
            builder.Add(mtd.Name + " +" + mtd.GainPercentagePoints.ToString("0.00", Loc.Culture) + "%", RenderHandler.ColorPalette.LightTurquoise);
            wrote = true;
        }
        builder.NextLine();

        return true;
    }

    // ---------------- 酒：分数 / 成分 / 赝品（信息增强） ----------------
    private bool TryAppendWine(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Wine) return false;
        var lines = WineInfo.Collect(item);
        if (lines.Count == 0) return false;
        AppendLines(builder, lines);
        return true;
    }

    // ---------------- 肉类来源 / 朊病毒（信息增强） ----------------
    private bool TryAppendMeat(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.Meat) return false;
        return AppendLines(builder, MeatInfo.Collect(item));
    }

    // ---------------- 市场修正（店铺事件对价格的百分比影响） ----------------
    private bool TryAppendMarket(TooltipSections.Writer builder, GameItem item)
    {
        if (!ModSettings.PriceImpact) return false;
        return AppendLines(builder, MarketModifier.Collect(item, builder), RenderHandler.ColorPalette.Orange);
    }

    // ---------------- 工具 ----------------

    /// <summary>输出模块已经定好颜色的一组行；返回是否真的写入了内容。</summary>
    private static bool AppendLines(TooltipSections.Writer builder, List<TooltipLine> lines)
    {
        List<(int Line, string Kind, object Value)>? rowData = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var ln = lines[i];
            if (builder.Capturing && ln.DataKind != null && ln.Data != null)
                (rowData ??= new()).Add((i, ln.DataKind, ln.Data));
            if (ln.HasBar) builder.Bar(ln);
            else builder.AddLine(ln.Text, ln.Color);
        }
        if (rowData != null) builder.Field("rowData", rowData.AsReadOnly());
        return lines.Count > 0;
    }

    /// <summary>
    /// 输出进度条行：前缀 → 条文本 → 后缀。
    /// 原版布局直接输出条文本，避免 noBreak 改变 CJK 换行宽度。美化布局从 AddProgressBar 读取比例。
    /// </summary>
    internal static void AddBarLine(RichTextBuilder builder, TooltipLine ln)
    {
        try
        {
            if (SectionedLayout.UsesNativeBars)
            {
                // 完整说明在前，让带冒号的译文也把条留在值列。美化布局自行决定条与文字的位置。
                builder.Add((ln.Text + " " + ln.Tail).Trim() + " ", ln.Color);
                builder.AddProgressBar(ln.Bar, ln.BarLength, ln.Color, ln.BarDark);
                builder.NextLine();
                return;
            }
            if (ln.Text.Length > 0) builder.Add(ln.Text, ln.Color);
            int visible = ProgressBarStyle.VisibleLength(ln.Text) + ln.BarLength + 1
                + ProgressBarStyle.VisibleLength(ln.Tail);
            string bar = ProgressBarStyle.Format(ln.Bar, ln.BarLength, ln.BarDark, fullLength: true,
                allowSprite: ProgressBarStyle.FitsOneLine(builder, visible));
            builder.Add(bar, ln.Color, end: " ");
            AddLine(builder, ln.Tail ?? "", ln.Color);
        }
        catch (Exception ex)
        {
            Log.Warn("AddBarLine", ex);
        }
    }

    /// <summary>输出同色的一组纯文本行（未做本地化的模块用）。</summary>
    private static bool AppendLines(TooltipSections.Writer builder, List<string> lines, RenderHandler.ColorPalette color)
    {
        foreach (string s in lines)
            builder.AddLine(s, color);
        return lines.Count > 0;
    }

    internal static void AddLine(RichTextBuilder builder, string text, RenderHandler.ColorPalette color)
    {
        try
        {
            builder.AddLine(text, false, color, false, false, false, false,
                RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None);
            Log.MarkOk("AddLine");   // 恢复正常后清掉失败计数，下次再坏会重新报第一次
        }
        catch (Exception ex)
        {
            Log.Warn("AddLine", ex);
        }
    }
}
