using System.Buffers.Binary;
using System.Text;

namespace DotNative;

public enum NodeKind : byte
{
    VStack = 1,
    Text = 2,
    Button = 3,
    ScrollView = 4,
    TextField = 5,
    Switch = 6,
    Slider = 7,
    Image = 8,
    Icon = 9,
    Navigation = 10,
    NavigationPage = 11,
    RadioButton = 12,
    NavigationHeader = 13,
    BottomNavigationBar = 14,
    DesktopNavigation = 15,
    PlatformView = 16,
}

public enum OpCode : byte
{
    Create = 1,
    SetText = 2,
    SetStyle = 3,
    SetChildren = 4,
    SetRoot = 5,
    Delete = 6,
    SetViewport = 7,
    SetLayout = 8,
    SetControl = 9,
    SetImage = 10,
    SetHeader = 11,
    SetScroll = 12,
    SetHeaderOpacity = 13,
    SetFlexShrink = 14,
    SetScrollAxis = 15,
    SetExtendedLayout = 16,
    SetVisualStyle = 17,
    SetTypography = 18,
    SetLayoutBehavior = 19,
    SetAdvancedLayout = 20,
    SetGradient = 21,
    SetZIndex = 22,
    SetImageFit = 23,
    SetImageTint = 24,
    SetBackgroundImage = 25,
    SetBoxDecoration = 26,
    SetEnabled = 27,
    SetTextRuns = 28,
    SetFontVariations = 29,
    SetFontSource = 30,
    SetNavigationTransition = 31,
    SetBottomNavigation = 32,
    SetDesktopNavigation = 33,
    SetTextInput = 34,
    SetScrollPosition = 35,
    ObserveLayout = 36,
    SetVirtualList = 37,
    SetProjection = 38,
    RegisterFont = 39,
    SetFontReference = 40,
    SetPlatformView = 41,
}

// Little-endian, explicitly sized fields; no managed struct layout crosses FFI.
// A buffer is UI-thread confined. Its span is borrowed until the next mutation.
public sealed class CommandBuffer
{
    public const int HeaderSize = 16;
    public const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private byte[] _bytes;
    private int _length;
    private uint _count;

    public CommandBuffer(int initialCapacity = 4096)
    {
        if (initialCapacity < HeaderSize || initialCapacity > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _bytes = new byte[initialCapacity];
        Reset();
    }

    public bool IsEmpty => _count == 0;

    public void Reset()
    {
        _length = HeaderSize;
        _count = 0;
        _bytes.AsSpan(0, HeaderSize).Clear();
        "DNTV"u8.CopyTo(_bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(6), HeaderSize);
    }

    public ReadOnlySpan<byte> WrittenSpan
    {
        get
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(8), (uint)_length);
            BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(12), _count);
            return _bytes.AsSpan(0, _length);
        }
    }

    private Span<byte> Begin(OpCode code, int payloadLength)
    {
        int next = checked(_length + 5 + payloadLength);
        if (next > MaximumBytes)
            throw new InvalidOperationException("Command batch exceeds 16 MiB.");
        if (next > _bytes.Length)
            Array.Resize(ref _bytes, Math.Min(MaximumBytes, Math.Max(next, _bytes.Length * 2)));
        _bytes[_length] = (byte)code;
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(_length + 1), (uint)payloadLength);
        var payload = _bytes.AsSpan(_length + 5, payloadLength);
        _length = next;
        ++_count;
        return payload;
    }

    public void Create(uint id, NodeKind kind)
    {
        RequireId(id);
        if (kind is < NodeKind.VStack or > NodeKind.PlatformView)
            throw new ArgumentOutOfRangeException(nameof(kind));
        var p = Begin(OpCode.Create, 5);
        U32(p, id);
        p[4] = (byte)kind;
    }

    public void SetText(uint id, string value)
    {
        RequireId(id);
        ArgumentNullException.ThrowIfNull(value);
        // Validate before advancing the writer, including invalid UTF-16.
        int count = Utf8.GetByteCount(value);
        var p = Begin(OpCode.SetText, checked(8 + count));
        U32(p, id);
        U32(p[4..], (uint)count);
        Utf8.GetBytes(value.AsSpan(), p[8..]);
    }

    public void SetPlatformView(uint id, string viewType)
    {
        RequireId(id);
        ArgumentNullException.ThrowIfNull(viewType);
        int count = Utf8.GetByteCount(viewType);
        var payload = Begin(OpCode.SetPlatformView, checked(8 + count));
        U32(payload, id);
        U32(payload[4..], (uint)count);
        Utf8.GetBytes(viewType.AsSpan(), payload[8..]);
    }

    public void SetStyle(uint id, Style style)
    {
        RequireId(id);
        Styling.Nonnegative(style.Padding);
        Styling.Nonnegative(style.CornerRadius);
        if (!float.IsFinite(style.FontSize) || style.FontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(style));
        var p = Begin(OpCode.SetStyle, 24);
        U32(p, id);
        F32(p[4..], style.Padding);
        Rgba(p[8..], style.Background);
        F32(p[12..], style.CornerRadius);
        Rgba(p[16..], style.Foreground);
        F32(p[20..], style.FontSize);
    }

    public void SetVisualStyle(uint id, VisualStyle style)
    {
        RequireId(id);
        style.Validate();
        var p = Begin(OpCode.SetVisualStyle, 53);
        U32(p, id);
        F32(p[4..], style.Opacity);
        F32(p[8..], style.BorderWidth);
        Rgba(p[12..], style.BorderColor);
        Rgba(p[16..], style.Shadow.Color);
        F32(p[20..], style.Shadow.Blur);
        F32(p[24..], style.Shadow.OffsetX);
        F32(p[28..], style.Shadow.OffsetY);
        F32(p[32..], style.TranslationX);
        F32(p[36..], style.TranslationY);
        F32(p[40..], style.ScaleX);
        F32(p[44..], style.ScaleY);
        F32(p[48..], style.Rotation);
        p[52] = style.ClipToBounds ? (byte)1 : (byte)0;
    }

    public void SetProjection(uint id, System.Numerics.Matrix4x4 matrix)
    {
        RequireId(id);
        var p = Begin(OpCode.SetProjection, 68);
        U32(p, id);
        var values = VisualStyling.ProjectionValues(matrix);
        for (var index = 0; index < values.Length; index++)
            F32(p[(4 + index * 4)..], values[index]);
    }

    public void SetTypography(uint id, TypographyStyle style)
    {
        RequireId(id);
        style.Validate();
        int count = Utf8.GetByteCount(style.FontFamily);
        var p = Begin(OpCode.SetTypography, checked(26 + count));
        U32(p, id);
        BinaryPrimitives.WriteUInt16LittleEndian(p[4..], (ushort)style.Weight);
        p[6] = style.Italic ? (byte)1 : (byte)0;
        p[7] = (byte)style.Alignment;
        BinaryPrimitives.WriteInt32LittleEndian(p[8..], style.MaxLines ?? -1);
        p[12] = (byte)style.Overflow;
        F32(p[13..], style.LetterSpacing);
        F32(p[17..], style.LineHeight ?? 0);
        p[21] = (byte)style.Decoration;
        U32(p[22..], (uint)count);
        Utf8.GetBytes(style.FontFamily, p[26..]);
    }

    public void SetAdvancedLayout(uint id, AdvancedLayoutStyle style)
    {
        RequireId(id);
        style.Validate();
        int columns = style.Grid?.Columns.Count ?? 0,
            rows = style.Grid?.Rows.Count ?? 0;
        var p = Begin(OpCode.SetAdvancedLayout, 56 + 5 * (columns + rows));
        U32(p, id);
        p[4] = (byte)style.FlowDirection;
        p[5] = (byte)style.AlignContent;
        p[6] = style.Reverse ? (byte)1 : (byte)0;
        p[7] = style.Grid is null ? (byte)0 : (byte)1;
        F32(p[8..], style.RowGap ?? -1);
        F32(p[12..], style.ColumnGap ?? -1);
        float?[] logical =
        [
            style.MarginStart,
            style.MarginEnd,
            style.PaddingStart,
            style.PaddingEnd,
            style.InsetStart,
            style.InsetEnd,
        ];
        byte mask = 0;
        for (int i = 0; i < logical.Length; i++)
        {
            if (logical[i].HasValue)
                mask |= (byte)(1 << i);
            F32(p[(16 + i * 4)..], logical[i] ?? 0);
        }
        p[40] = mask;
        p[41] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(p[42..], (ushort)style.GridColumn);
        BinaryPrimitives.WriteUInt16LittleEndian(p[44..], (ushort)style.ColumnSpan);
        BinaryPrimitives.WriteUInt16LittleEndian(p[46..], (ushort)style.GridRow);
        BinaryPrimitives.WriteUInt16LittleEndian(p[48..], (ushort)style.RowSpan);
        BinaryPrimitives.WriteUInt16LittleEndian(p[50..], (ushort)columns);
        BinaryPrimitives.WriteUInt16LittleEndian(p[52..], (ushort)rows);
        p[54] = p[55] = 0;
        int offset = 56;
        if (style.Grid is { } grid)
            foreach (var track in grid.Columns.Concat(grid.Rows))
            {
                p[offset] = (byte)track.Unit;
                F32(p[(offset + 1)..], track.Value);
                offset += 5;
            }
    }

    internal void RegisterFont(uint key, FontSource source)
    {
        RequireId(key);
        int count = Utf8.GetByteCount(source.Family),
            length = source.Data.Length;
        var p = Begin(OpCode.RegisterFont, 12 + count + length);
        U32(p, key);
        U32(p[4..], (uint)count);
        Utf8.GetBytes(source.Family, p[8..]);
        U32(p[(8 + count)..], (uint)length);
        source.Data.CopyTo(p[(12 + count)..]);
    }

    internal void SetFontReference(uint id, uint key)
    {
        RequireId(id);
        RequireId(key);
        var p = Begin(OpCode.SetFontReference, 8);
        U32(p, id);
        U32(p[4..], key);
    }

    public void SetFontSource(uint id, FontSource? source)
    {
        RequireId(id);
        var family = source?.Family ?? "";
        int count = Utf8.GetByteCount(family),
            length = source?.Data.Length ?? 0;
        var p = Begin(OpCode.SetFontSource, 12 + count + length);
        U32(p, id);
        U32(p[4..], (uint)count);
        Utf8.GetBytes(family, p[8..]);
        U32(p[(8 + count)..], (uint)length);
        source?.Data.CopyTo(p[(12 + count)..]);
    }

    public void SetFontVariations(uint id, FontVariationSettings? settings)
    {
        RequireId(id);
        var axes = settings?.Axes;
        int count = axes?.Count ?? 0;
        var p = Begin(OpCode.SetFontVariations, 5 + count * 8);
        U32(p, id);
        p[4] = (byte)count;
        for (int i = 0; i < count; i++)
        {
            int offset = 5 + i * 8;
            var axis = axes![i];
            for (int j = 0; j < 4; j++)
                p[offset + j] = (byte)axis.Tag[j];
            F32(p[(offset + 4)..], axis.Value);
        }
    }

    public void SetTextRuns(uint id, IReadOnlyList<TextRun> runs)
    {
        RequireId(id);
        ArgumentNullException.ThrowIfNull(runs);
        var copy = runs.ToArray();
        if (copy.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(runs));
        int length = 8;
        foreach (var run in copy)
        {
            ArgumentNullException.ThrowIfNull(run);
            run.Validate();
            length = checked(
                length + 21 + Utf8.GetByteCount(run.Text) + Utf8.GetByteCount(run.FontFamily ?? "")
            );
        }
        var p = Begin(OpCode.SetTextRuns, length);
        U32(p, id);
        U32(p[4..], (uint)copy.Length);
        int offset = 8;
        foreach (var run in copy)
        {
            int textCount = Utf8.GetByteCount(run.Text);
            U32(p[offset..], (uint)textCount);
            offset += 4;
            Utf8.GetBytes(run.Text, p[offset..]);
            offset += textCount;
            p[offset++] = (byte)(
                (run.FontSize.HasValue ? 1 : 0)
                | (run.Foreground.HasValue ? 2 : 0)
                | (run.Weight.HasValue ? 4 : 0)
                | (run.Italic.HasValue ? 8 : 0)
                | (run.FontFamily is not null ? 16 : 0)
                | (run.Decoration.HasValue ? 32 : 0)
            );
            F32(p[offset..], run.FontSize ?? 17);
            offset += 4;
            var color = run.Foreground ?? default;
            p[offset++] = color.R;
            p[offset++] = color.G;
            p[offset++] = color.B;
            p[offset++] = color.A;
            BinaryPrimitives.WriteUInt16LittleEndian(
                p[offset..],
                (ushort)(run.Weight ?? FontWeight.Normal)
            );
            offset += 2;
            p[offset++] = run.Italic == true ? (byte)1 : (byte)0;
            p[offset++] = (byte)(run.Decoration ?? TextDecoration.None);
            var family = run.FontFamily ?? "";
            int familyCount = Utf8.GetByteCount(family);
            U32(p[offset..], (uint)familyCount);
            offset += 4;
            Utf8.GetBytes(family, p[offset..]);
            offset += familyCount;
        }
    }

    public void SetEnabled(uint id, bool enabled)
    {
        RequireId(id);
        var p = Begin(OpCode.SetEnabled, 5);
        U32(p, id);
        p[4] = enabled ? (byte)1 : (byte)0;
    }

    public void SetBoxDecoration(uint id, BoxDecoration value)
    {
        RequireId(id);
        value.Validate();
        var p = Begin(OpCode.SetBoxDecoration, 57);
        U32(p, id);
        var r = value.Corners ?? default;
        p[4] = value.Corners.HasValue ? (byte)1 : (byte)0;
        F32(p[5..], r.TopLeft);
        F32(p[9..], r.TopRight);
        F32(p[13..], r.BottomRight);
        F32(p[17..], r.BottomLeft);
        BorderSide?[] sides = [value.Left, value.Top, value.Right, value.Bottom];
        for (int i = 0; i < 4; i++)
        {
            if (sides[i].HasValue)
                p[4] |= (byte)(2 << i);
            var side = sides[i] ?? default;
            int offset = 21 + i * 9;
            F32(p[offset..], side.Width);
            Rgba(p[(offset + 4)..], side.Color);
            p[offset + 8] = (byte)side.LineStyle;
        }
    }

    internal void SetBackgroundImage(uint id, ImageSource? source, ImageFit fit)
    {
        RequireId(id);
        if (!Enum.IsDefined(fit))
            throw new ArgumentOutOfRangeException(nameof(fit));
        int length = source?.Data.Length ?? 0;
        var p = Begin(OpCode.SetBackgroundImage, 9 + length);
        U32(p, id);
        p[4] = (byte)fit;
        U32(p[5..], (uint)length);
        source?.Data.CopyTo(p[9..]);
    }

    public void SetImageTint(uint id, Color? value)
    {
        RequireId(id);
        var p = Begin(OpCode.SetImageTint, 9);
        U32(p, id);
        p[4] = value is null ? (byte)0 : (byte)1;
        Rgba(p[5..], value ?? Color.Transparent);
    }

    public void SetImageFit(uint id, ImageFit value)
    {
        RequireId(id);
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var p = Begin(OpCode.SetImageFit, 5);
        U32(p, id);
        p[4] = (byte)value;
    }

    public void SetZIndex(uint id, int value)
    {
        RequireId(id);
        var p = Begin(OpCode.SetZIndex, 8);
        U32(p, id);
        BinaryPrimitives.WriteInt32LittleEndian(p[4..], value);
    }

    public void SetGradient(uint id, Gradient? gradient)
    {
        RequireId(id);
        var p = Begin(OpCode.SetGradient, 22 + (gradient?.Stops.Count ?? 0) * 8);
        U32(p, id);
        p[4] = gradient is null ? (byte)0 : (byte)((byte)gradient.Kind + 1);
        p[5] = (byte)(gradient?.Stops.Count ?? 0);
        F32(p[6..], gradient?.Start.X ?? 0);
        F32(p[10..], gradient?.Start.Y ?? 0);
        F32(p[14..], gradient?.End.X ?? 0);
        F32(p[18..], gradient?.End.Y ?? 0);
        if (gradient is not null)
            for (int i = 0; i < gradient.Stops.Count; i++)
            {
                F32(p[(22 + i * 8)..], gradient.Stops[i].Offset);
                Rgba(p[(26 + i * 8)..], gradient.Stops[i].Color);
            }
    }

    internal void SetScrollPosition(uint id, (uint Revision, float Offset, bool Observe) request)
    {
        RequireId(id);
        if (!float.IsFinite(request.Offset) || request.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        var p = Begin(OpCode.SetScrollPosition, 13);
        U32(p, id);
        U32(p[4..], request.Revision);
        F32(p[8..], request.Offset);
        p[12] = request.Observe ? (byte)1 : (byte)0;
    }

    internal void SetTextInput(uint id, TextInputOptions input)
    {
        RequireId(id);
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var p = Begin(OpCode.SetTextInput, input.PlaceholderColor is null ? 9 : 13);
        U32(p, id);
        p[4] = (byte)input.Keyboard;
        p[5] = (byte)input.Action;
        p[6] = (byte)input.Capitalization;
        p[7] = (byte)input.Autofill;
        p[8] = (byte)(
            (input.IsPassword ? 1 : 0) | (input.IsMultiline ? 2 : 0) | (input.Autocorrect ? 4 : 0)
        );
        if (input.PlaceholderColor is { } hint)
        {
            p[9] = hint.R;
            p[10] = hint.G;
            p[11] = hint.B;
            p[12] = hint.A;
        }
    }

    internal void SetControl(uint id, ControlProperties control)
    {
        RequireId(id);
        int count = Utf8.GetByteCount(control.Placeholder);
        var p = Begin(OpCode.SetControl, checked(28 + count));
        U32(p, id);
        F32(p[4..], control.Value);
        F32(p[8..], control.Minimum);
        F32(p[12..], control.Maximum);
        U32(p[16..], control.FocusRevision);
        U32(p[20..], control.FocusRequested ? 1u : 0);
        U32(p[24..], (uint)count);
        Utf8.GetBytes(control.Placeholder, p[28..]);
    }

    internal void SetNavigationTransition(uint id, NavigationTransition transition)
    {
        RequireId(id);
        ArgumentNullException.ThrowIfNull(transition);
        if (!Enum.IsDefined(transition.Kind))
            throw new ArgumentOutOfRangeException(nameof(transition));
        if (
            transition.Duration < TimeSpan.Zero
            || transition.Duration > TimeSpan.FromSeconds(10)
            || (
                transition.Kind != NavigationTransitionKind.None
                && transition.Duration == TimeSpan.Zero
            )
        )
            throw new ArgumentOutOfRangeException(nameof(transition));
        if (!Enum.IsDefined(transition.Easing))
            throw new ArgumentOutOfRangeException(nameof(transition));
        var p = Begin(OpCode.SetNavigationTransition, 10);
        U32(p, id);
        p[4] = (byte)transition.Kind;
        F32(p[5..], (float)transition.Duration.TotalSeconds);
        p[9] = (byte)transition.Easing;
    }

    internal void SetScrollAxis(uint id, Axis axis)
    {
        RequireId(id);
        if (!Enum.IsDefined(axis))
            throw new ArgumentOutOfRangeException(nameof(axis));
        var p = Begin(OpCode.SetScrollAxis, 5);
        U32(p, id);
        p[4] = (byte)axis;
    }

    internal void SetScroll(uint id, NestedScrollMode mode)
    {
        RequireId(id);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        var p = Begin(OpCode.SetScroll, 5);
        U32(p, id);
        p[4] = (byte)mode;
    }

    internal void SetHeaderOpacity(uint id, float opacity)
    {
        RequireId(id);
        if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        var p = Begin(OpCode.SetHeaderOpacity, 8);
        U32(p, id);
        F32(p[4..], opacity);
    }

    internal void SetDesktopNavigation(uint id, NativeDesktopNavigation node)
    {
        RequireId(id);
        var strings = new[] { node.Title }
            .Concat(node.Items.SelectMany(i => new[] { i.Title, i.Icon ?? "" }))
            .ToArray();
        var p = Begin(
            OpCode.SetDesktopNavigation,
            checked(24 + strings.Sum(v => checked(4 + Utf8.GetByteCount(v))))
        );
        U32(p, id);
        BinaryPrimitives.WriteInt32LittleEndian(p[4..], node.CurrentIndex);
        U32(p[8..], node.IsOpen ? 1u : 0u);
        F32(p[12..], node.PaneWidth);
        U32(p[16..], (uint)node.Items.Length);
        U32(p[20..], (uint)node.ThemeMode);
        int offset = 24;
        foreach (var value in strings)
        {
            int length = Utf8.GetByteCount(value);
            U32(p[offset..], (uint)length);
            Utf8.GetBytes(value, p[(offset + 4)..]);
            offset += 4 + length;
        }
    }

    internal void SetBottomNavigation(uint id, BottomNavigationBar bar)
    {
        RequireId(id);
        int size = checked(12 + bar.Items.Sum(item => checked(8 + Utf8.GetByteCount(item.Label))));
        var p = Begin(OpCode.SetBottomNavigation, size);
        BinaryPrimitives.WriteUInt32LittleEndian(p, id);
        BinaryPrimitives.WriteUInt32LittleEndian(p[4..], (uint)bar.CurrentIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(p[8..], (uint)bar.Items.Count);
        int offset = 12;
        foreach (var item in bar.Items)
        {
            int length = Utf8.GetByteCount(item.Label);
            BinaryPrimitives.WriteUInt32LittleEndian(p[offset..], (uint)item.Icon);
            BinaryPrimitives.WriteUInt32LittleEndian(p[(offset + 4)..], (uint)length);
            Utf8.GetBytes(item.Label, p[(offset + 8)..]);
            offset += 8 + length;
        }
    }

    internal void SetHeader(uint id, ScreenOptions options)
    {
        RequireId(id);
        var actions = options.Leading.Concat(options.Trailing).ToArray();
        var lengths = actions.Select(action => Utf8.GetByteCount(action.Title)).ToArray();
        var p = Begin(OpCode.SetHeader, checked(16 + lengths.Sum() + 4 * lengths.Length));
        U32(p, id);
        U32(
            p[4..],
            (options.HeaderVisible ? 1u : 0u)
                | (options.BackButtonVisible ? 2u : 0u)
                | (options.CanGoBack ? 4u : 0u)
                | (options.TitleDisplay == NavigationTitleDisplay.Large ? 8u : 0u)
        );
        U32(p[8..], (uint)options.Leading.Count);
        U32(p[12..], (uint)options.Trailing.Count);
        int offset = 16;
        for (int i = 0; i < actions.Length; i++)
        {
            U32(p[offset..], (uint)lengths[i]);
            offset += 4;
            Utf8.GetBytes(actions[i].Title, p[offset..]);
            offset += lengths[i];
        }
    }

    internal void SetImage(uint id, ImageSource source)
    {
        RequireId(id);
        ArgumentNullException.ThrowIfNull(source);
        var p = Begin(OpCode.SetImage, checked(8 + source.Data.Length));
        U32(p, id);
        U32(p[4..], (uint)source.Data.Length);
        source.Data.CopyTo(p[8..]);
    }

    public void SetLayout(uint id, LayoutStyle layout)
    {
        RequireId(id);
        if (layout.Width is { } w)
            Styling.Nonnegative(w);
        if (layout.Height is { } h)
            Styling.Nonnegative(h);
        Styling.Nonnegative(layout.Gap);
        Styling.Nonnegative(layout.FlexGrow);
        Styling.Nonnegative(layout.FlexShrink);
        LayoutExtensions.ValidateExtended(layout);
        if (
            !Enum.IsDefined(layout.Direction)
            || !Enum.IsDefined(layout.MainAlignment)
            || !Enum.IsDefined(layout.CrossAlignment)
        )
            throw new ArgumentOutOfRangeException(nameof(layout));
        // All four records form one setter; capacity failure must not leave a partial update.
        if (_length > MaximumBytes - 146)
            throw new InvalidOperationException("Command batch exceeds 16 MiB.");
        var p = Begin(OpCode.SetLayout, 24);
        U32(p, id);
        F32(p[4..], layout.Width ?? -1);
        F32(p[8..], layout.Height ?? -1);
        F32(p[12..], layout.Gap);
        F32(p[16..], layout.FlexGrow);
        p[20] = (byte)layout.Direction;
        p[21] = (byte)layout.MainAlignment;
        p[22] = (byte)layout.CrossAlignment;
        p[23] = 0;
        // Separate opcode preserves the v1 SetLayout payload and older fixtures.
        var shrink = Begin(OpCode.SetFlexShrink, 8);
        U32(shrink, id);
        F32(shrink[4..], layout.FlexShrink);
        var extended = Begin(OpCode.SetExtendedLayout, 58);
        U32(extended, id);
        F32(extended[4..], layout.Margin.Left);
        F32(extended[8..], layout.Margin.Top);
        F32(extended[12..], layout.Margin.Right);
        F32(extended[16..], layout.Margin.Bottom);
        F32(extended[20..], layout.MinWidth ?? -1);
        F32(extended[24..], layout.MinHeight ?? -1);
        F32(extended[28..], layout.MaxWidth ?? -1);
        F32(extended[32..], layout.MaxHeight ?? -1);
        F32(extended[36..], layout.FlexBasis ?? -1);
        extended[40] = (byte)layout.Wrap;
        extended[41] = (byte)layout.AlignSelf;
        F32(extended[42..], layout.PaddingEdges?.Left ?? -1);
        F32(extended[46..], layout.PaddingEdges?.Top ?? -1);
        F32(extended[50..], layout.PaddingEdges?.Right ?? -1);
        F32(extended[54..], layout.PaddingEdges?.Bottom ?? -1);
        var behavior = layout.Behavior;
        var b = Begin(OpCode.SetLayoutBehavior, 36);
        U32(b, id);
        F32(b[4..], behavior.WidthPercent ?? -1);
        F32(b[8..], behavior.HeightPercent ?? -1);
        F32(b[12..], behavior.AspectRatio ?? -1);
        b[16] = (byte)behavior.Position;
        b[17] = (byte)behavior.Visibility;
        b[18] = (byte)(
            (behavior.Insets.Left.HasValue ? 1 : 0)
            | (behavior.Insets.Top.HasValue ? 2 : 0)
            | (behavior.Insets.Right.HasValue ? 4 : 0)
            | (behavior.Insets.Bottom.HasValue ? 8 : 0)
        );
        b[19] = 0;
        F32(b[20..], behavior.Insets.Left ?? 0);
        F32(b[24..], behavior.Insets.Top ?? 0);
        F32(b[28..], behavior.Insets.Right ?? 0);
        F32(b[32..], behavior.Insets.Bottom ?? 0);
    }

    public void SetChildren(uint parent, ReadOnlySpan<uint> children)
    {
        RequireId(parent);
        foreach (uint child in children)
            RequireId(child);
        var p = Begin(OpCode.SetChildren, checked(8 + children.Length * 4));
        U32(p, parent);
        U32(p[4..], (uint)children.Length);
        for (int i = 0; i < children.Length; ++i)
            U32(p[(8 + i * 4)..], children[i]);
    }

    internal void SetVirtualList(uint id)
    {
        RequireId(id);
        U32(Begin(OpCode.SetVirtualList, 4), id);
    }

    internal void ObserveLayout(uint id, uint revision)
    {
        RequireId(id);
        var p = Begin(OpCode.ObserveLayout, 8);
        U32(p, id);
        U32(p[4..], revision);
    }

    public void SetRoot(uint id) => U32(Begin(OpCode.SetRoot, 4), id);

    public void Delete(uint id)
    {
        RequireId(id);
        U32(Begin(OpCode.Delete, 4), id);
    }

    public void SetViewport(float width, float height)
    {
        Styling.Nonnegative(width);
        Styling.Nonnegative(height);
        var p = Begin(OpCode.SetViewport, 8);
        F32(p, width);
        F32(p[4..], height);
    }

    private static void RequireId(uint id)
    {
        if (id == 0)
            throw new ArgumentOutOfRangeException(
                nameof(id),
                "Zero is reserved for an empty root."
            );
    }

    private static void U32(Span<byte> p, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(p, value);

    private static void F32(Span<byte> p, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(p, value);

    private static void Rgba(Span<byte> p, Color c)
    {
        p[0] = c.R;
        p[1] = c.G;
        p[2] = c.B;
        p[3] = c.A;
    }
}
