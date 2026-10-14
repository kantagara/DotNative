using System.Numerics;

namespace DotNative;

/// <summary>A bounded, optionally looping selector rendered through the shared component tree.</summary>
public sealed class WheelPicker : Component, IDisposable
{
    private sealed record Range(string[] Items);

    private readonly State<Range> range;
    private readonly State<int> selected;
    private readonly ScrollController scroll = new();
    private readonly AnimationController snap;
    private readonly IUiDispatcher dispatcher;
    private readonly TimeProvider clock;
    private readonly Action<int> changed;
    private ITimer? idle;
    private int idleGeneration;
    private float? requestedAnimationOffset;
    private bool disposed;
    private bool snapping;
    private WheelScrollSimulation? simulation;
    private bool dragging;
    private int animationGeneration;
    private float origin;
    private string? registeredFamily;
    public float Height { get; init; } = 160;
    public float ItemExtent { get; init; } = 36;
    public float VerticalPadding { get; init; } = 18;
    public float HorizontalPadding { get; init; } = 7;
    public float Squeeze { get; init; } = 0.95f;
    public float DiameterRatio { get; init; } = 1.5f;
    public float Perspective { get; init; } = 0.003f;
    public float OutsideOpacity { get; init; } = 0.447f;
    public bool Looping { get; init; } = true;
    public float? DividerSpacing { get; init; }
    public float DividerThickness { get; init; } = 2;
    public bool ShowDividers { get; init; } = true;
    public float FontSize { get; init; } = 16;
    public FontSource? Font { get; init; }
    public Color Foreground { get; init; } = Color.Black;

    public WheelPicker(
        IReadOnlyList<string> items,
        int initialIndex,
        Action<int> onChanged,
        IUiDispatcher dispatcher
    )
        : this(items, initialIndex, onChanged, dispatcher, TimeProvider.System) { }

    internal WheelPicker(
        IReadOnlyList<string> items,
        int initialIndex,
        Action<int> onChanged,
        IUiDispatcher dispatcher,
        TimeProvider clock
    )
    {
        Validate(items, initialIndex);
        this.dispatcher = dispatcher;
        this.clock = clock;
        changed = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        range = new(new(items.ToArray()));
        selected = new(initialIndex);
        snap = new(dispatcher, clock: clock);
        // Initialize on the UI thread after object-initializer options have been applied.
        dispatcher.Post(() =>
        {
            if (!disposed)
                Configure(items, initialIndex);
        });
    }

    public void SetItems(IReadOnlyList<string> items, int selectedIndex)
    {
        Validate(items, selectedIndex);
        if (!dispatcher.CheckAccess())
            throw new InvalidOperationException("UI thread required.");
        if (selected.Value == selectedIndex && range.Value.Items.SequenceEqual(items))
            return;
        Configure(items, selectedIndex);
    }

    private static void Validate(IReadOnlyList<string> items, int index)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (
            items.Count == 0
            || items.Count > 10000
            || index < 0
            || index >= items.Count
            || items.Any(x => x is null)
        )
            throw new ArgumentOutOfRangeException(nameof(items));
    }

    private void Configure(IReadOnlyList<string> items, int index)
    {
        CancelIdle();
        animationGeneration++;
        snap.Stop();
        snapping = false;
        range.Value = new(items.ToArray());
        selected.Value = index;
        origin = Looping ? items.Count * 500 : 0;
        scroll.JumpTo((origin + index) * ItemExtent);
    }

    private void Scrolled(ScrollMetrics metrics)
    {
        if (disposed)
            return;
        var count = range.Value.Items.Length;
        var index = (int)MathF.Floor(metrics.Offset / ItemExtent + 0.5f) - (int)origin;
        index = Looping ? ((index % count) + count) % count : Math.Clamp(index, 0, count - 1);
        if (index != selected.Value)
        {
            selected.Value = index;
            changed(index);
        }
        if (snapping || dragging)
            return;
        ScheduleSnap();
    }

    private void ScheduleSnap()
    {
        CancelIdle();
        var generation = idleGeneration;
        idle = clock.CreateTimer(
            _ =>
                dispatcher.Post(() =>
                {
                    if (!disposed && generation == idleGeneration && !dragging && !snapping)
                        _ = SnapAsync();
                }),
            null,
            TimeSpan.FromMilliseconds(80),
            Timeout.InfiniteTimeSpan
        );
    }

    private void CancelIdle()
    {
        idleGeneration++;
        idle?.Dispose();
        idle = null;
    }

    private Task FlingAsync(float velocity)
    {
        if (disposed || !float.IsFinite(velocity))
            return Task.CompletedTask;
        var maximum = (range.Value.Items.Length * (Looping ? 1000 : 1) - 1) * ItemExtent;
        var nearest = MathF.Floor(scroll.Offset / ItemExtent + 0.5f) * ItemExtent;
        if (MathF.Abs(velocity) < 10 && MathF.Abs(scroll.Offset - nearest) < 0.001f)
        {
            CancelIdle();
            return Task.CompletedTask;
        }
        return SimulateAsync(
            WheelScrollSimulation.Create(scroll.Offset, velocity, ItemExtent, maximum)
        );
    }

    private Task SnapAsync() => FlingAsync(0);

    private async Task SimulateAsync(WheelScrollSimulation motion)
    {
        CancelIdle();
        var generation = ++animationGeneration;
        requestedAnimationOffset = scroll.Offset;
        snap.Stop();
        snap.SnapTo(0);
        simulation = motion;
        snapping = true;
        try
        {
            await snap.AnimateToAsync(1, TimeSpan.FromSeconds(motion.Duration));
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (generation == animationGeneration && !disposed)
            {
                scroll.JumpTo(motion.Target);
                Scrolled(new ScrollMetrics(motion.Target, scroll.ViewportExtent));
                snapping = false;
                simulation = null;
            }
        }
    }

    private Task TapAsync(float y)
    {
        var viewport = Height - 2 * VerticalPadding;
        var target =
            MathF.Floor(scroll.Offset / ItemExtent + (y - viewport / 2) / ItemExtent + 0.5f)
            * ItemExtent;
        var maximum = (range.Value.Items.Length * (Looping ? 1000 : 1) - 1) * ItemExtent;
        target = Math.Clamp(target, 0, maximum);
        var start = (double)scroll.Offset;
        var easing = Easings.CubicBezier(0.42f, 0, 0.58f, 1);
        return SimulateAsync(
            new(target, 0.3, time => start + (target - start) * easing((float)(time / 0.3)))
        );
    }

    public override Element Build(BuildContext context)
    {
        if (
            !float.IsFinite(ItemExtent)
            || ItemExtent <= 0
            || Height <= 2 * VerticalPadding
            || DiameterRatio <= 0
            || Squeeze <= 0
        )
            throw new InvalidOperationException("Invalid wheel geometry.");
        var dividerSpacing = DividerSpacing ?? context.Get<ViewportSize>().Width * 0.02f;
        var items = range.Value.Items;
        var animatedOffset = simulation?.At(snap.Value) ?? snap.Value;
        // Preserve subpixel precision even in the middle of a long looping list.
        var localOffset = (float)(
            (snapping ? animatedOffset : scroll.Offset) - (double)origin * ItemExtent
        );
        var offset = localOffset / ItemExtent;
        var nativeOffset = (float)animatedOffset;
        if (snapping && requestedAnimationOffset != nativeOffset)
        {
            var generation = animationGeneration;
            var frame = nativeOffset;
            // Native scroll views can round to device pixels. Compare with our
            // last request, not their rounded reply, to avoid a render feedback loop.
            requestedAnimationOffset = frame;
            dispatcher.Post(() =>
            {
                if (
                    !disposed
                    && snapping
                    && generation == animationGeneration
                    && requestedAnimationOffset == frame
                )
                    scroll.JumpTo(frame);
            });
        }
        var viewport = Height - 2 * VerticalPadding;
        var band = (viewport - ItemExtent) / 2;
        var first = (int)MathF.Floor(offset) - (int)MathF.Ceiling(viewport / ItemExtent);
        var last = first + (int)MathF.Ceiling(viewport / ItemExtent) * 2;
        var lineHeight = MathF.Ceiling(FontSize * 4 / 3);
        Text Label(string text) =>
            new Text(text)
                .FontSize(FontSize)
                .LineHeight(lineHeight)
                .Height(lineHeight)
                .Shrink(0)
                .MaxLines(1)
                .ForegroundColor(Foreground);
        Element Rows(int part, float top, float extent)
        {
            var rows = new List<Element>();
            for (var index = first; index <= last; index++)
            {
                if (!Looping && (index < 0 || index >= items.Length))
                    continue;
                var delta = index * ItemExtent - localOffset;
                var angle =
                    -delta / viewport * 2 * MathF.Asin(Math.Min(1, 1 / DiameterRatio)) / Squeeze;
                if (MathF.Abs(angle) > MathF.PI / 2)
                    continue;
                var value = ((index % items.Length) + items.Length) % items.Length;
                var text = Label(items[value])
                    .WidthPercent(100)
                    .TextAlignment(TextAlignment.Center);
                if (Font is { } font)
                {
                    text =
                        registeredFamily == font.Family
                            ? text.FontFamily(font.Family)
                            : text.FontSource(font);
                    registeredFamily = font.Family;
                }
                var label = new Container(text)
                    .MainAlignment(MainAxisAlignment.Center)
                    .Height(ItemExtent)
                    .WidthPercent(100)
                    .Position(PositionMode.Absolute)
                    .Left(0)
                    .Top(Height / 2 - top - ItemExtent / 2);
                if (part == 1)
                    label = label.Top(Height / 2 - top - ItemExtent / 2 + delta);
                else
                {
                    var radius = viewport * DiameterRatio / 2;
                    var projection = Matrix4x4.Identity;
                    projection.M23 = MathF.Sin(angle);
                    projection.M24 = -Perspective * MathF.Sin(angle);
                    projection.M44 = 1 + Perspective * radius * (1 - MathF.Cos(angle));
                    var projectedY = -radius * MathF.Sin(angle) / projection.M44;
                    // Give every new native row its actual position before the
                    // backing layer exists. Compensate the local projection for
                    // this translation so the cylinder still uses one division.
                    label = label.Top(Height / 2 - top - ItemExtent / 2 + projectedY);
                    projection.M22 = MathF.Cos(angle) - projectedY * projection.M24;
                    projection.M43 = radius * (MathF.Cos(angle) - 1);
                    label = label.ProjectiveTransform(projection).Opacity(OutsideOpacity);
                }
                rows.Add(label.WithKey($"wheel:{part}:{index}"));
            }
            return new VStack(rows.ToArray())
                .Position(PositionMode.Absolute)
                .Left(HorizontalPadding)
                .Right(HorizontalPadding)
                .Top(top)
                .Height(extent)
                .ClipToBounds();
        }
        var height = (items.Length * (Looping ? 1000 : 1) - 1) * ItemExtent + viewport;
        return new VStack(
            Rows(0, VerticalPadding, band),
            Rows(1, VerticalPadding + band, ItemExtent),
            Rows(2, VerticalPadding + band + ItemExtent, band),
            new Container()
                .BackgroundColor(ShowDividers ? Foreground : Color.Transparent)
                .Position(PositionMode.Absolute)
                .Left(dividerSpacing)
                .Right(dividerSpacing)
                .Top(Height / 2 - ItemExtent / 2 - DividerThickness / 2)
                .Height(DividerThickness),
            new Container()
                .BackgroundColor(ShowDividers ? Foreground : Color.Transparent)
                .Position(PositionMode.Absolute)
                .Left(dividerSpacing)
                .Right(dividerSpacing)
                .Top(Height / 2 + ItemExtent / 2 - DividerThickness / 2)
                .Height(DividerThickness),
            new ScrollView(new Container().Height(height))
            {
                Controller = scroll,
                OnScrolled = Scrolled,
                OnTapped = y => _ = TapAsync(y),
                OnFling = velocity => _ = FlingAsync(velocity),
                OnDraggingChanged = active =>
                {
                    dragging = active;
                    CancelIdle();
                    if (active)
                    {
                        animationGeneration++;
                        snap.Stop();
                        snapping = false;
                    }
                    else if (!disposed)
                        ScheduleSnap();
                },
            }
                .Position(PositionMode.Absolute)
                .Left(HorizontalPadding)
                .Right(HorizontalPadding)
                .Top(VerticalPadding)
                .Height(viewport)
        ).Height(Height).WidthPercent(100).ClipToBounds();
    }

    public void Dispose()
    {
        disposed = true;
        CancelIdle();
        snap.Dispose();
    }
}
