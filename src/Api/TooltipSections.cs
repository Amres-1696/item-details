using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using ItemDetails.Integration;
using Color = RenderHandler.ColorPalette;
using Run = (string Kind, string Text, RenderHandler.ColorPalette Color, float Ratio, int Length, RenderHandler.ColorPalette Background, string Tail);
using Runs = System.Collections.Generic.IReadOnlyList<(string Kind, string Text, RenderHandler.ColorPalette Color, float Ratio, int Length, RenderHandler.ColorPalette Background, string Tail)>;
using Display = System.Func<GameItem, RichTextBuilder, string, System.Collections.Generic.IReadOnlyList<(string Kind, string Text, RenderHandler.ColorPalette Color, float Ratio, int Length, RenderHandler.ColorPalette Background, string Tail)>, System.Collections.Generic.IReadOnlyDictionary<string, object>, bool>;
using Fields = System.Collections.Generic.IReadOnlyDictionary<string, object>;

namespace ItemDetails;

internal static class TooltipSections
{
    private sealed class Frame
    {
        internal int Build;
        internal readonly List<TooltipSection> Sections = new();
    }

    private static readonly ConditionalWeakTable<RichTextBuilder, Frame> Frames = new();
    private static readonly Func<RichTextBuilder, Action<string, Runs, Fields>, bool> Query = Read;
    private static readonly Fields EmptyFields = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());

    internal static void Enable() => AppDomain.CurrentDomain.SetData(ItemDetailsClient.SectionQuery, Query);

    internal static void Disable()
    {
        if (ReferenceEquals(AppDomain.CurrentDomain.GetData(ItemDetailsClient.SectionQuery), Query))
            AppDomain.CurrentDomain.SetData(ItemDetailsClient.SectionQuery, null);
        EndTooltip();
        Frames.Clear();
    }

    internal static void EndTooltip()
    {
        listenersBuild = -1;
        listeners = default;
    }

    // For a native text slice shared with another game helper (e.g. the label prefix).
    internal static void ObserveText(RichTextBuilder builder, GameItem item, string id, string text, Color color)
    {
        var output = new Writer(builder, item, id);
        if (!output.Capturing) return;
        output.AddLine(text, color);
        output.Observe();
    }

    private static bool Read(RichTextBuilder builder, Action<string, Runs, Fields> emit)
    {
        if (builder == null || !Frames.TryGetValue(builder, out var frame) || frame.Sections.Count == 0) return false;
        foreach (var section in frame.Sections) emit(section.Id, section.Runs, section.Fields);
        return true;
    }

    private static int listenersBuild = -1;
    private static (Display? Displays, bool Watch) listeners;

    private static (Display? Displays, bool Watch) Listeners()
    {
        bool building = TooltipPipeline.IsBuilding;
        if (building && listenersBuild == TooltipPipeline.BuildId) return listeners;
        var domain = AppDomain.CurrentDomain;
        var result = (domain.GetData(ItemDetailsClient.SectionDisplay) as Display,
                      domain.GetData(ItemDetailsClient.SectionWatch) is Action);
        if (building)
        {
            listeners = result;
            listenersBuild = TooltipPipeline.BuildId;
        }
        return result;
    }

    // Outside integration mode writes go straight to the original game builder.
    internal readonly struct Writer
    {
        internal readonly RichTextBuilder Native;
        private readonly Buffer? buffer;
        internal bool Capturing => buffer != null;

        internal Writer(RichTextBuilder builder, GameItem item, string id)
        {
            Native = builder;
            var (displays, watch) = Listeners();
            buffer = displays != null || watch ? new Buffer(item, id, displays, watch) : null;
        }

        internal void Field<T>(string key, T value) where T : notnull
        {
            if (buffer != null) (buffer.Fields ??= new Dictionary<string, object>())[key] = value;
        }

        internal void Add(string text, Color color) => Write(("text", text, color, 0, 0, default, ""));
        internal void AddLine(string text, Color color) => Write(("line", text, color, 0, 0, default, ""));
        internal void NextLine() => Write(("break", "", default, 0, 0, default, ""));
        internal void Bar(TooltipLine line) => Write(("bar", line.Text, line.Color, line.Bar, line.BarLength, line.BarDark, line.Tail ?? ""));

        private void Write(Run run)
        {
            if (buffer == null) Replay(Native, run);
            else (buffer.Runs ??= new List<Run>()).Add(run);
        }

        internal void RequireWidth(int width)
        {
            if (buffer == null) Native.maxCharWidth = Math.Max(width, Native.maxCharWidth);
            else buffer.Width = Math.Max(width, buffer.Width);
        }

        // A native method owns this line or overlay. Expose its data without changing its rendering.
        internal void Observe()
        {
            if (buffer == null) return;
            Finish(observeOnly: true);
        }

        internal void AlreadyHandled() { if (buffer != null) buffer.Handled = true; }

        // true means a replacement actually claimed the section (also used by the contents overlay).
        internal bool Finish(bool observeOnly = false)
        {
            if (buffer == null) return false;
            Runs runs = buffer.Runs == null ? Array.Empty<Run>() : buffer.Runs.AsReadOnly();
            var frame = buffer.Watch ? Frames.GetOrCreateValue(Native) : null;
            if (frame != null && frame.Build != TooltipPipeline.BuildId)
            {
                frame.Sections.Clear();
                frame.Build = TooltipPipeline.BuildId;
            }
            if (runs.Count == 0 && buffer.Fields == null) return false;
            observeOnly |= buffer.Handled;
            if (observeOnly) Field("canReplace", false);
            Fields fields = buffer.Fields == null ? EmptyFields : new ReadOnlyDictionary<string, object>(buffer.Fields);
            frame?.Sections.Add(new TooltipSection(buffer.Id, runs, fields));
            if (buffer.Displays != null)
            {
                foreach (Display prepare in buffer.Displays.GetInvocationList())
                {
                    try { if (prepare(buffer.Item, Native, buffer.Id, runs, fields) && !observeOnly) return true; }
                    catch (Exception ex) { Log.Warn("TooltipDisplay." + buffer.Id, ex); }
                }
            }
            if (observeOnly) return buffer.Handled;
            if (buffer.Width > 0) Native.maxCharWidth = Math.Max(buffer.Width, Native.maxCharWidth);
            foreach (var run in runs) Replay(Native, run);
            return false;
        }

        private static void Replay(RichTextBuilder builder, Run run)
        {
            switch (run.Kind)
            {
                case "text": builder.Add(run.Text, run.Color); break;
                case "line": ModEntry.AddLine(builder, run.Text, run.Color); break;
                case "break": builder.NextLine(); break;
                case "bar": ModEntry.AddBarLine(builder, new TooltipLine(run.Text, run.Ratio, run.Length,
                    run.Tail, run.Color, run.Background)); break;
            }
        }
    }

    private sealed class Buffer
    {
        internal readonly GameItem Item;
        internal readonly string Id;
        internal readonly Display? Displays;
        internal readonly bool Watch;
        internal bool Handled;
        internal int Width;
        internal List<Run>? Runs;
        internal Dictionary<string, object>? Fields;

        internal Buffer(GameItem item, string id, Display? displays, bool watch)
        {
            Item = item;
            Id = id;
            Displays = displays;
            Watch = watch;
        }
    }
}
