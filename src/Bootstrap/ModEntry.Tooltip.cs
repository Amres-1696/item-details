using System;
using System.Collections.Generic;
using System.Text;

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
    private bool TryAppendAuthenticity(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Auth) return false;
        var r = Authenticity.Analyze(item);
        if (!r.HasData) return false;

        if (r.Faults > 0)
        {
            string names = string.Join(Loc.T("list.sep", ", "), r.FaultNames);
            AddLine(builder, Loc.T("auth.counterfeit", "× Counterfeit ({0})", names),
                RenderHandler.ColorPalette.LightRed);
        }
        else if (r.IsGenuine)
        {
            AddLine(builder, Loc.T("auth.genuine", "√ Genuine"), RenderHandler.ColorPalette.Green);
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
    private bool TryAppendValue(RichTextBuilder builder, GameItem item)
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
        totalLine += Loc.T("value.perfect_range", " | perfect {0}-{1}", lo, hi);
        AddLine(builder, totalLine, RenderHandler.ColorPalette.Green);

        AppendParts(builder, r);
        return true;
    }

    /// <summary>
    /// 来源明细单行输出；标签已缩短，避免原版定宽折行把数字拆断。
    ///
    /// 符号与分隔符合一：正值写「+加固60」，负值写「-破裂150」并用红字。
    /// 旧写法把符号塞进数值（「+破裂-150」），一个负项会出现两个符号，既难读也占宽。
    /// </summary>
    private void AppendParts(RichTextBuilder builder, ValueBreakdown.Result r)
    {
        builder.Add(Loc.T("value.from", "From: "), RenderHandler.ColorPalette.White);

        foreach (var p in r.Parts)
        {
            // 文案由纯公式层给出「键 + 英文兜底 + 参数」，这里只做解析。
            string label = Loc.T(p.Key, p.En, p.Args);

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

        builder.NextLine();
    }

    // ---------------- 赝品肋明治：具体原因 ----------------
    private bool TryAppendRibwichReasons(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Auth) return false;   // 赝品原因属于真伪鉴定

        var lines = RibwichCounterfeit.Collect(item);
        if (lines.Count == 0) return false;

        AddLine(builder, Loc.T("ribwich.header", "Why it's counterfeit:"), RenderHandler.ColorPalette.LightRed);
        foreach (string s in lines)
            AddLine(builder, "• " + s, RenderHandler.ColorPalette.LightRed);
        return true;
    }

    // ---------------- 第三方显式提供的鉴定分解 ----------------
    private bool TryAppendInjectedValue(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Value) return false;

        var r = ThirdPartyProviders.Appraisal(item);
        if (r == null) return false;

        string totalLine = Loc.T("value.total_line", "Total: {0}", r.Total);
        var (lo, hi) = AppraisalRanges.GamePerfect(r.Total);
        totalLine += Loc.T("value.perfect_range", " | perfect {0}-{1}", lo, hi);
        AddLine(builder, totalLine, RenderHandler.ColorPalette.Green);

        var parts = new List<string>();
        foreach (var p in r.Parts)
        {
            if (string.IsNullOrWhiteSpace(p.Label)) continue;
            string amount = p.IsBase || p.Amount < 0 ? p.Amount.ToString() : "+" + p.Amount;
            parts.Add($"{p.Label} {amount}");
        }
        if (parts.Count > 0)
            AddLine(builder, Loc.T("generic.line", "From: {0}", string.Join(Loc.T("list.sep", ", "), parts)),
                RenderHandler.ColorPalette.White);
        return true;
    }

    // ---------------- 果实品质（BERRY_*） ----------------
    private bool TryAppendBerry(RichTextBuilder builder, GameItem item)
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
            if (showQuality)
                builder.Add(Loc.T("berry.line", "{0} quality: {1}", itemName, name!), color);
            // 酿酒值：这颗莓在 WineHelper.StartFermentation 里给果酿评分加的点数，灰字补在同一行末尾。
            if (ModSettings.Wine)
            {
                string score = GameRead.BerryWineScore(item).ToString("+0;-0;0");
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
    private bool TryAppendHidden(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.HiddenTruth) return false;
        if (IsLiquid(item)) return false;   // 水质模块已汇总
        return AppendLines(builder, HiddenInfo.Collect(item));
    }

    // ---------------- 性价比（兜底） ----------------
    // 正常由 TooltipPipeline 写在价格块下一行；价格行没改写时才落到这里，同一次构建只写一次。
    private bool TryAppendValueEnhance(RichTextBuilder builder, GameItem item)
        => ValueEnhance.TryWrite(builder, item);

    // ---------------- P1 隐藏特性揭示（真假条件） ----------------
    private bool TryAppendFeatureReveal(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.HiddenTruth) return false;
        if (IsLiquid(item)) return false;   // 水质模块已汇总真假
        return AppendLines(builder, FeatureReveal.Collect(item));
    }

    // ---------------- 储存容器：压缩率 + 内容物图标格 ----------------
    private bool TryAppendStorage(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.ValueEnhance && !ModSettings.Container) return false;
        return AppendLines(builder, StorageInfo.Collect(item,
            includeRatio: ModSettings.ValueEnhance,
            includeContents: ModSettings.Container,
            allowGrid: true));
    }

    // ---------------- 机器与槽位容器内部：滤芯、液体、工具、剩余几晚 ----------------
    private bool TryAppendMachineInterior(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Container && !ModSettings.Machines && !ModSettings.MachineBattery) return false;
        return AppendLines(builder, MachineInterior.Collect(item,
            listing: ModSettings.Container,
            adapters: ModSettings.Machines,
            hiddenTruth: ModSettings.HiddenTruth,
            purity: LiquidGate.Enabled));
    }

    // ---------------- 自学习算法模块：学习进度 ----------------
    private bool TryAppendModuleLearning(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.ModuleEffects) return false;
        return AppendLines(builder, ModuleLearning.Collect(item));
    }

    // ---------------- 瑕疵/状态对价值的影响 ----------------
    private bool TryAppendConditions(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.PriceImpact) return false;
        if (IsLiquid(item)) return false;   // 水质模块已汇总，避免重复
        return AppendLines(builder, ConditionEffects.Collect(item, skipRevealed: ModSettings.HiddenTruth));
    }

    // ---------------- 违禁等级与黑市溢价 ----------------
    private bool TryAppendContraband(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Contraband) return false;

        var r = ContrabandInfo.Analyze(item);
        if (!r.HasData) return false;
        // 原版 ContrabandHelper 已经显示违禁等级，这里只补它没有的黑市溢价。
        if (r.MarkupPercent < 0) return false;

        AddLine(builder,
            Loc.T("contraband.markup", "Black market: +{0}%", r.MarkupPercent),
            RenderHandler.ColorPalette.LightTurquoise);
        return true;
    }

    // ---------------- 彩票开奖结果 ----------------
    private bool TryAppendLottery(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Lottery) return false;
        return AppendLines(builder, LotteryInfo.Collect(item));
    }

    // ---------------- 液体组分：水质 / 杂质 / 药物成分 ----------------
    private bool TryAppendWater(RichTextBuilder builder, GameItem item)
    {
        if (!LiquidGate.Enabled) return false;

        var r = WaterInfo.Analyze(item);
        if (!r.HasData) return false;

        bool wrote = false;

        if (!r.Solid)
        {
            // 液体容器：表头「成分 + 体积条 + 2000/2000mL 档位」，下面每种成分一行。
            // 三列（名称 / 体积 / 百分比）按 tooltip 实际字体像素宽补位，左对齐，
            // 见 WaterColumns：中文约是西文的 1.9 倍宽，按字符补空格对不齐。
            // 成分表三列按字体宽度补位后比原版折行预算宽，先放宽本段 tooltip 的折行宽度，
            // 否则每行都会被按字符数拆开（中文按 0.66 计，估不准实际宽度）。
            try { builder.maxCharWidth = Math.Max(builder.maxCharWidth, WaterTableWidth); } catch { }

            int totalMl = r.TotalParts / 1000;
            bool liters = Volume.UseLiters(totalMl);

            int capacityMl = 0;
            try { capacityMl = item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt() / 1000; } catch { }
            float ratio = capacityMl > 0 ? Math.Min(1f, (float)totalMl / capacityMl) : 0f;

            var comps = new List<(string Name, int Parts, RenderHandler.ColorPalette Color)>();
            bool mismatch = false;
            if (r.WaterParts > 0)
            {
                var color = r.Purity >= WaterInfo.PurityGood ? RenderHandler.ColorPalette.LightGreen
                          : r.Purity >= WaterInfo.PurityFair ? RenderHandler.ColorPalette.LightYellow
                          : RenderHandler.ColorPalette.LightRed;
                string name = r.WaterName.Length > 0 ? r.WaterName : "?";
                mismatch = !string.IsNullOrEmpty(r.Label) && !string.IsNullOrEmpty(r.Tier)
                        && !string.Equals(r.Label, r.Tier, StringComparison.Ordinal);
                comps.Add((mismatch ? name + Loc.T("water.mismatch", " (doesn't match label)") : name,
                    r.WaterParts, color));
            }
            foreach (var active in r.Actives)
                comps.Add((active.Name, active.Parts, RenderHandler.ColorPalette.LightYellow));

            int hidden = 0, colorIndex = 0;
            foreach (var part in r.Impurities)
            {
                if (part.Parts < WaterInfo.MinVisibleParts) { hidden++; continue; }
                if (part.Parts * 100.0 / r.TotalParts < 0.005) { hidden++; continue; }
                comps.Add((part.Name, part.Parts, ImpurityColor(colorIndex++)));
            }
            comps.Sort((a, b) => b.Parts.CompareTo(a.Parts));

            // 先量列宽：名称列取表头与所有成分名的最大值，体积列取所有体积串的最大值。
            string label = Loc.T("water.components", "Components");
            float nameWidth = WaterColumns.Measure(label);
            float volWidth = 0f;
            foreach (var c in comps)
            {
                nameWidth = Math.Max(nameWidth, WaterColumns.Measure(c.Name));
                volWidth = Math.Max(volWidth, WaterColumns.Measure(VolumeOf(c.Parts, liters)));
            }
            const string gap = "  ";

            string tail = " " + WaterLayout.Capacity(totalMl, capacityMl, liters, Loc.Culture)
                        + (r.Tier.Length > 0 ? " " + r.Tier : "");
            // 条长 6：原版 10 格加上前面名称与后面体积会折行
            AppendLines(builder, new List<TooltipLine>
            {
                new(WaterColumns.PadTo(label, nameWidth) + gap, ratio, 6, tail,
                    RenderHandler.ColorPalette.LightCyan, RenderHandler.ColorPalette.VeryDarkCyan),
            });
            wrote = true;

            foreach (var c in comps)
            {
                string line = WaterColumns.PadTo(c.Name, nameWidth) + gap
                            + WaterColumns.PadTo(VolumeOf(c.Parts, liters), volWidth) + gap
                            + WaterLayout.PercentText(c.Parts, r.TotalParts, Loc.Culture) + "%";
                AddLine(builder, line, c.Color);
            }

            if (hidden > 0)
            {
                AddLine(builder, Loc.T("water.trace", "trace (below display threshold)"),
                    RenderHandler.ColorPalette.Gray);
            }
        }
        else
        {
            // 固体化学品（药片、净水片等）：没有容器与体积，仍按占比列出。
            if (r.Actives.Count > 0)
            {
                var actives = new StringBuilder();
                actives.Append(Loc.T("water.actives", "Actives: "));
                for (int i = 0; i < r.Actives.Count; i++)
                {
                    if (i > 0) actives.Append(Loc.T("list.sep", ", "));
                    var active = r.Actives[i];
                    actives.Append(active.Name).Append(' ').Append(WaterInfo.Amount(active.Parts));
                }
                AddLine(builder, actives.ToString(), RenderHandler.ColorPalette.LightYellow);
                wrote = true;
            }

            if (r.Impurities.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append(r.Actives.Count == 0 ? Loc.T("water.composition", "Composition: ")
                                               : Loc.T("water.other", "Other: "));
                bool any = false;
                for (int i = 0; i < r.Impurities.Count; i++)
                {
                    var part = r.Impurities[i];
                    if (part.Parts < WaterInfo.MinVisibleParts) continue;
                    double partPct = part.Parts * 100.0 / r.TotalParts;
                    if (partPct < 0.005) continue;
                    if (any) sb.Append(' ');
                    any = true;
                    sb.Append(part.Name).Append(' ').Append(partPct.ToString("0.00", Loc.Culture)).Append('%');
                }
                if (!any) sb.Append(Loc.T("water.trace", "trace (below display threshold)"));
                AddLine(builder, sb.ToString(), RenderHandler.ColorPalette.LightCyan);
                wrote = true;
            }
        }

        // 药效结论：与化学扫描仪同一组判定，省得玩家自己对阈值。
        if (r.HasEffects)
        {
            AddLine(builder, Loc.T("chem.effects", "Effects: {0}", ChemEffects(r)),
                r.LethalDose > 0 || r.Toxic ? RenderHandler.ColorPalette.LightRed : RenderHandler.ColorPalette.LightYellow);
            wrote = true;
        }

        return wrote;
    }

    // ---------------- 密封箱预览（透视，默认关闭） ----------------
    private bool TryAppendSealedBox(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.SealedBox) return false;
        return AppendLines(builder, SealedBoxInfo.Collect(item, allowGrid: true));
    }

    // ---------------- 刮刮卡结果（透视） ----------------
    private bool TryAppendScratcher(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Scratcher) return false;
        return AppendLines(builder, ScratcherInfo.Collect(item));
    }

    // ---------------- 种子性状（透视） ----------------
    private bool TryAppendSeed(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Plant) return false;

        var lines = SeedInfo.Collect(item);
        if (lines.Count == 0) return false;

        // 性状逐项一行太占版面，接在表头后写成一行
        var texts = lines.ConvertAll(l => l.Text);
        AddLine(builder, Loc.T("seed.line", "Traits (unanalyzed): {0}", string.Join(Loc.T("list.sep", ", "), texts)),
            RenderHandler.ColorPalette.LightTurquoise);
        return true;
    }

    /// <summary>水质成分表的折行预算（字符数）；原版 tooltip 约 40，三列按像素补位后需要更宽。</summary>
    private const int WaterTableWidth = 64;

    /// <summary>杂质配色：每种杂质按出现顺序取一个不同颜色，超出调色板长度才循环。</summary>
    private static readonly RenderHandler.ColorPalette[] ImpurityColors =
    {
        RenderHandler.ColorPalette.LightRed,
        RenderHandler.ColorPalette.LightOrange,
        RenderHandler.ColorPalette.LightYellow,
        RenderHandler.ColorPalette.LightPurple,
        RenderHandler.ColorPalette.LightBlue,
        RenderHandler.ColorPalette.LightTurquoise,
        RenderHandler.ColorPalette.ERed,
        RenderHandler.ColorPalette.Orange,
        RenderHandler.ColorPalette.Purple,
    };

    private static string VolumeOf(int parts, bool liters)
        => WaterLayout.VolumeText(parts, liters, Loc.Culture)
           + (liters ? WaterLayout.LiterUnit : WaterLayout.MilliliterUnit);

    private static RenderHandler.ColorPalette ImpurityColor(int index)
        => ImpurityColors[index % ImpurityColors.Length];

    /// <summary>「致死（2 倍致死量）、强烈欣快、戒瘾」</summary>
    private static string ChemEffects(WaterInfo.Result r)
    {
        var parts = new List<string>();
        if (r.LethalDose > 0) parts.Add(Loc.T("chem.lethal", "lethal ({0}x lethal dose)", r.LethalDose));
        else if (r.Toxic) parts.Add(Loc.T("chem.toxic", "toxic"));
        switch (r.Euphoria)
        {
            case 1: parts.Add(Loc.T("chem.euphoria_weak", "weak euphoria")); break;
            case 2: parts.Add(Loc.T("chem.euphoria_mild", "mild euphoria")); break;
            case 3: parts.Add(Loc.T("chem.euphoria_strong", "strong euphoria")); break;
            case 4: parts.Add(Loc.T("chem.euphoria_extreme", "extreme euphoria")); break;
        }
        if (r.Synaprest) parts.Add(Loc.T("chem.synaprest", "anti-addiction"));
        if (r.Contraceptive) parts.Add(Loc.T("chem.contraceptive", "contraceptive"));
        return string.Join(Loc.T("list.sep", ", "), parts);
    }

    // ---------------- 含水容器：净化收益（探测版） ----------------
    private bool TryAppendWaterAdvice(RichTextBuilder builder, GameItem item)
    {
        if (!LiquidGate.Enabled) return false;

        // 按玩家实际拥有的手段逐项列出纯度提升（每种都在独立深拷贝探针上模拟）
        var methods = WaterAdvice.SimulateMethods(item);
        if (methods.Count == 0) return false;

        bool wrote = false;
        foreach (var mtd in methods)
        {
            if (wrote) builder.Add("　", RenderHandler.ColorPalette.Gray);
            builder.Add(mtd.Name + " +" + mtd.GainPct.ToString("0.00", Loc.Culture) + "%", RenderHandler.ColorPalette.LightTurquoise);
            wrote = true;
        }
        builder.NextLine();

        return true;
    }

    // ---------------- 酒：分数 / 成分 / 赝品（信息增强） ----------------
    private bool TryAppendWine(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Wine) return false;
        var lines = WineInfo.Collect(item);
        if (lines.Count == 0) return false;
        AppendLines(builder, lines);
        return true;
    }

    // ---------------- 肉类来源 / 朊病毒（信息增强） ----------------
    private bool TryAppendMeat(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.Meat) return false;
        return AppendLines(builder, MeatInfo.Collect(item));
    }

    // ---------------- 市场修正（店铺事件对价格的百分比影响） ----------------
    private bool TryAppendMarket(RichTextBuilder builder, GameItem item)
    {
        if (!ModSettings.PriceImpact) return false;
        return AppendLines(builder, MarketModifier.Collect(item), RenderHandler.ColorPalette.Orange);
    }

    // ---------------- 工具 ----------------

    /// <summary>输出模块已经定好颜色的一组行；返回是否真的写入了内容。</summary>
    private static bool AppendLines(RichTextBuilder builder, List<TooltipLine> lines)
    {
        foreach (var ln in lines)
        {
            if (ln.HasBar) AddBarLine(builder, ln);
            else AddLine(builder, ln.Text, ln.Color);
        }
        return lines.Count > 0;
    }

    /// <summary>
    /// 输出进度条行：前缀 → 条文本 → 后缀。
    /// 不调用原版 AddProgressBar()，避免该方法添加的 noBreak 标记改变整个 tooltip 在 CJK 下的换行宽度。
    /// </summary>
    private static void AddBarLine(RichTextBuilder builder, TooltipLine ln)
    {
        try
        {
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
    private static bool AppendLines(RichTextBuilder builder, List<string> lines, RenderHandler.ColorPalette color)
    {
        foreach (string s in lines)
            AddLine(builder, s, color);
        return lines.Count > 0;
    }

    private static void AddLine(RichTextBuilder builder, string text, RenderHandler.ColorPalette color)
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
