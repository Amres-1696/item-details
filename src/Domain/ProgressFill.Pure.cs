using System;

namespace ItemDetails;

/// <summary>
/// 进度条已填格数。原版是 floor(比例 × 格数)，只差一点没满也只画 length − 1 格；
/// 而旁边的数字往往四舍五入成已满（水罐差 0.4 ml 也写「3000 ml / 3000 ml」）。
/// 差额不到 <see cref="FullThreshold"/> 时按满画，其余与原版一致。
/// </summary>
internal static class ProgressFill
{
    internal const float FullThreshold = 0.9995f;

    internal static int Filled(float rawRatio, int length)
    {
        if (length <= 0) return 0;
        float ratio = float.IsFinite(rawRatio) ? Math.Clamp(rawRatio, 0f, 1f) : 0f;
        if (ratio >= FullThreshold) return length;
        return Native(rawRatio, length);
    }

    /// <summary>
    /// 原版 <c>RichTextBuilder.AddProgressBar</c> 的已填格数：直接截断，接近满也照样留一格空的。
    /// 本模组自己的条按 <see cref="Filled"/> 画，两种数法都会出现在同一张提示框里。
    /// </summary>
    internal static int Native(float rawRatio, int length)
    {
        if (length <= 0) return 0;
        float ratio = float.IsFinite(rawRatio) ? Math.Clamp(rawRatio, 0f, 1f) : 0f;
        return (int)(ratio * length);
    }

    /// <summary>
    /// 这段文字里的 [=====  ] 是不是这条记录画出来的：长度和格数都要对得上。
    /// 两种格数都认（原版截断、本模组补满），否则 99.99% 的水罐会因为差一格匹配不上，
    /// 退回按字符数还原成九成。比例对不上的记录不会借。
    /// </summary>
    internal static bool Accepts(float rawRatio, int length, int equalsCount)
        => length > 0
           && (equalsCount == Filled(rawRatio, length) || equalsCount == Native(rawRatio, length));
}
