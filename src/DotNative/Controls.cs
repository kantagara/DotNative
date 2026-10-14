namespace DotNative;

public sealed class FocusNode : ReactiveSource
{
    private uint _revision;
    private bool _requested;

    public void RequestFocus() => Request(true);

    public void Unfocus() => Request(false);

    private void Request(bool value)
    {
        VerifyCanWrite();
        _requested = value;
        _revision = checked(_revision + 1);
        PublishChange();
    }

    internal (uint Revision, bool Requested) Read()
    {
        TrackRead();
        return (_revision, _requested);
    }
}

public sealed record TextField : Node, IStateStyled, ILayoutable, IVisualBox, IPaddable, ITextual
{
    public string Value { get; }
    public Action<string> OnChanged { get; }
    public string Placeholder { get; init; } = "";
    public FocusNode? Focus { get; init; }
    public Action<bool>? OnFocusChanged { get; init; }
    public TextInputOptions Input { get; init; } = TextInputOptions.Default;

    /// <summary>Called on a native keyboard action. Next/focus traversal is controlled by the app.</summary>
    public Action<string>? OnSubmitted { get; init; }

    public TextField(string value, Action<string> onChanged) =>
        (Value, OnChanged) = (
            value ?? throw new ArgumentNullException(nameof(value)),
            onChanged ?? throw new ArgumentNullException(nameof(onChanged))
        );
}

public sealed record Switch : Node, IStateStyled, ILayoutable
{
    public bool Value { get; }
    public Action<bool> OnChanged { get; }

    public Switch(bool value, Action<bool> onChanged) =>
        (Value, OnChanged) = (
            value,
            onChanged ?? throw new ArgumentNullException(nameof(onChanged))
        );
}

public sealed record Slider : Node, IStateStyled, ILayoutable
{
    public float Value { get; }
    public float Minimum { get; }
    public float Maximum { get; }
    public Action<float> OnChanged { get; }

    public Slider(float value, Action<float> onChanged, float minimum = 0, float maximum = 1)
    {
        if (
            !float.IsFinite(value)
            || !float.IsFinite(minimum)
            || !float.IsFinite(maximum)
            || maximum <= minimum
            || value < minimum
            || value > maximum
        )
            throw new ArgumentOutOfRangeException(nameof(value));
        (Value, Minimum, Maximum, OnChanged) = (
            value,
            minimum,
            maximum,
            onChanged ?? throw new ArgumentNullException(nameof(onChanged))
        );
    }
}

public enum NestedScrollMode : byte
{
    Chain = 0,
    Contain = 1,
}

public sealed record ScrollView : ContainerNode
{
    internal bool Virtualized { get; init; }

    /// <summary>The scrolling axis, independent of the child layout direction.</summary>
    public Axis ScrollAxis { get; init; } = Axis.Vertical;
    public ScrollController? Controller { get; init; }
    public Action<ScrollMetrics>? OnScrolled { get; init; }
    public Action<float>? OnTapped { get; init; }
    public Action<bool>? OnDraggingChanged { get; init; }
    public Action<float>? OnFling { get; init; }

    /// <summary>Android gesture policy; vertical scrolling also supports nested fling transfer. Defaults to Contain.</summary>
    public NestedScrollMode NestedScrolling { get; init; } = NestedScrollMode.Contain;

    // One content node owns the scrollable extent; Taffy lays it out naturally.
    public ScrollView(Element child)
        : base(child) { }
}

public sealed class ImageSource
{
    internal byte[] Data { get; }

    private ImageSource(byte[] data) => Data = data;

    public static ImageSource FromBytes(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length == 0 || encoded.Length > 8 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(encoded));
        return new(encoded.ToArray());
    }

    public static async Task<ImageSource> FromFileAsync(
        string path,
        CancellationToken cancellation = default
    ) => FromBytes(await File.ReadAllBytesAsync(path, cancellation));
}

public enum ImageFit : byte
{
    Contain,
    Cover,
    Fill,
    None,
    ScaleDown,
}

public static class ImageStyling
{
    /// <summary>Colors the image alpha mask; null restores original colors.</summary>
    public static Image TintColor(this Image image, Color? value) =>
        image with
        {
            ImageTint = value,
        };

    /// <summary>Fits the image inside its layout box. Intrinsic measurement is unchanged.</summary>
    public static Image Fit(this Image image, ImageFit value)
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        return image with { ImageFit = value };
    }
}

public sealed record Image(ImageSource Source) : Node, ILayoutable, IVisualBox
{
    public ImageFit ImageFit { get; internal init; }
    public Color? ImageTint { get; internal init; }
}

public enum IconName
{
    Star,
    Check,
    Info,
}

/// <summary>
/// A layout slot filled by a plugin-created native view. Width and height come
/// from layout. An unregistered <see cref="ViewType"/> stays an empty rectangle.
/// Apple and GTK plugins register through <c>dotnative_register_platform_view</c>.
/// Android uses <c>NativeHost.registerPlatformView</c>. Windows uses
/// <c>DotNative.WinUI.PlatformViews.Register</c>.
/// </summary>
public sealed record PlatformView : Node, ILayoutable, IVisualBox
{
    public string ViewType { get; }

    public PlatformView(string viewType)
    {
        ArgumentNullException.ThrowIfNull(viewType);
        if (
            viewType.Length is < 1 or > 64
            || viewType.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')
            )
        )
            throw new ArgumentException(
                "Platform view type must be 1-64 ASCII letters, digits, '.', '_' or '-'.",
                nameof(viewType)
            );
        ViewType = viewType;
    }
}

public sealed record Icon : Node, ILayoutable, IVisualBox, ITintable
{
    public IconName Name { get; }

    public Icon(IconName name)
    {
        if (!Enum.IsDefined(name))
            throw new ArgumentOutOfRangeException(nameof(name));
        Name = name;
        Layout = new(Width: 24, Height: 24);
    }
}

internal readonly record struct ControlProperties(
    float Value = 0,
    float Minimum = 0,
    float Maximum = 1,
    uint FocusRevision = 0,
    bool FocusRequested = false,
    string Placeholder = ""
);

internal enum NativeEventKind : uint
{
    Click = 1,
    TextChanged = 2,
    ValueChanged = 3,
    FocusChanged = 4,
    NavigationShown = 5,
    NavigationStarted = 6,
    PressedChanged = 7,
    HoverChanged = 8,
    TextSubmitted = 9,
    ScrollChanged = 10,
    LayoutMeasured = 11,
    ScrollTapped = 12,
    ScrollDraggingChanged = 13,
    ScrollFling = 14,
    OpenUri = 20,
    OpenNotification = 21,
}
