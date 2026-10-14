using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.Text;

namespace DotNative.WinUI;

public static class PlatformViews
{
    private static readonly Dictionary<string, Func<uint, UIElement?>> Factories = new(StringComparer.Ordinal);

    public static void Register(string type, Func<uint, UIElement?> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(create);
        Factories[type] = create;
    }

    internal static UIElement? Create(string type, uint id) =>
        Factories.TryGetValue(type, out var create) ? create(id) : null;
}

public static unsafe partial class WinUIBridge
{
    // Brushes are immutable after creation and are shared just like native theme
    // resources. Bound the cache so animated/arbitrary colors cannot accumulate.
    private static readonly Dictionary<Windows.UI.Color, SolidColorBrush> ColorBrushes = new();
    private static SolidColorBrush ColorBrush(Windows.UI.Color color)
    {
        if (ColorBrushes.TryGetValue(color, out var brush)) return brush;
        if (ColorBrushes.Count == 64) ColorBrushes.Clear();
        ColorBrushes.Add(color, brush = new SolidColorBrush(color));
        return brush;
    }

    // Versioned, private presentation ABI. The Rust engine validates the public
    // command batch before sending changed nodes and their committed frames.
    private sealed class PresentationNode
    {
        internal uint Id,
            Kind;
        internal float X,
            Y,
            Width,
            Height;
        internal float[] Padding = [];
        internal Windows.UI.Color Background,
            Foreground,
            BorderColor;
        internal float Radius,
            FontSize,
            Opacity,
            BorderWidth;
        internal float[] Transform = [];
        internal Matrix4x4 Projection = Matrix4x4.Identity;
        internal bool Enabled,
            Clip;
        internal int Flow,
            Visibility,
            Z;
        internal string Text = "",
            Family = "",
            Placeholder = "";
        internal int Weight,
            Alignment,
            Lines,
            Overflow,
            Decoration;
        internal bool Italic;
        internal float Spacing,
            LineHeight,
            Value,
            Minimum,
            Maximum;
        internal uint FocusRevision,
            ScrollRevision;
        internal bool Focus,
            ObserveScroll, Virtualized;
        internal int[] Input = [];
        internal int Axis,
            Fit;
        internal float Offset;
        internal byte[] Image = [];
        internal uint[] Children = [];
        internal bool HeaderVisible,
            BackVisible,
            CanPop;
        internal int Leading;
        internal string[] Actions = [];
        internal int Theme,
            Selected;
        internal bool Open;
        internal float PaneWidth;
        internal string Title = "";
        internal (string Label, string Icon)[] Menu = [];
        internal Run[] Runs = [];
        internal int NavigationKind, NavigationEasing, GradientKind, BackgroundFit;
        internal float NavigationDuration;
        internal float[] GradientPoints = [], Corners = [];
        internal (float Offset, Windows.UI.Color Color)[] Stops = [];
        internal byte[] BackgroundImage = [], BorderSvg = [];
        internal string ViewType = "";

        internal static PresentationNode Read(BinaryReader r)
        {
            var n = new PresentationNode
            {
                Id = r.ReadUInt32(),
                Kind = r.ReadUInt32(),
                X = r.ReadSingle(),
                Y = r.ReadSingle(),
                Width = r.ReadSingle(),
                Height = r.ReadSingle(),
            };
            n.Padding = Enumerable.Range(0, 4).Select(_ => r.ReadSingle()).ToArray();
            n.Background = Color(r);
            n.Foreground = Color(r);
            n.Radius = r.ReadSingle();
            n.FontSize = r.ReadSingle();
            n.Opacity = r.ReadSingle();
            n.BorderWidth = r.ReadSingle();
            n.BorderColor = Color(r);
            n.Transform = Enumerable.Range(0, 5).Select(_ => r.ReadSingle()).ToArray();
            n.Projection = new Matrix4x4(
                r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),
                r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),
                r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),
                r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
            n.Enabled = r.ReadUInt32() != 0;
            n.Clip = r.ReadUInt32() != 0;
            n.Flow = r.ReadInt32();
            n.Visibility = r.ReadInt32();
            n.Z = r.ReadInt32();
            n.Text = TextValue(r);
            n.Family = TextValue(r);
            n.Weight = r.ReadInt32();
            n.Italic = r.ReadUInt32() != 0;
            n.Alignment = r.ReadInt32();
            n.Lines = r.ReadInt32();
            n.Overflow = r.ReadInt32();
            n.Decoration = r.ReadInt32();
            n.Spacing = r.ReadSingle();
            n.LineHeight = r.ReadSingle();
            n.Value = r.ReadSingle();
            n.Minimum = r.ReadSingle();
            n.Maximum = r.ReadSingle();
            n.FocusRevision = r.ReadUInt32();
            n.Focus = r.ReadUInt32() != 0;
            n.Placeholder = TextValue(r);
            n.Input = Enumerable.Range(0, 5).Select(_ => r.ReadInt32()).ToArray();
            n.Virtualized = r.ReadUInt32() != 0;
            n.Axis = r.ReadInt32();
            n.ScrollRevision = r.ReadUInt32();
            n.Offset = r.ReadSingle();
            n.ObserveScroll = r.ReadUInt32() != 0;
            n.Image = Bytes(r);
            n.Fit = r.ReadInt32();
            n.Children = Enumerable.Range(0, r.ReadInt32()).Select(_ => r.ReadUInt32()).ToArray();
            n.HeaderVisible = r.ReadUInt32() != 0;
            n.BackVisible = r.ReadUInt32() != 0;
            n.CanPop = r.ReadUInt32() != 0;
            n.Leading = r.ReadInt32();
            n.Actions = Enumerable.Range(0, r.ReadInt32()).Select(_ => TextValue(r)).ToArray();
            n.Theme = r.ReadInt32();
            n.Selected = r.ReadInt32();
            n.Open = r.ReadUInt32() != 0;
            n.PaneWidth = r.ReadSingle();
            n.Title = TextValue(r);
            n.Menu = Enumerable
                .Range(0, r.ReadInt32())
                .Select(_ => (TextValue(r), TextValue(r)))
                .ToArray();
            n.Runs = Enumerable
                .Range(0, r.ReadInt32())
                .Select(_ =>
                {
                    var text = TextValue(r);
                    var mask = r.ReadUInt32();
                    var size = r.ReadSingle();
                    var color = Color(r);
                    var weight = r.ReadInt32();
                    var italic = r.ReadUInt32() != 0;
                    var family = TextValue(r);
                    var decoration = r.ReadInt32();
                    var run = new Run { Text = text };
                    if ((mask & 1) != 0)
                        run.FontSize = size;
                    if ((mask & 2) != 0)
                        run.Foreground = ColorBrush(color);
                    if ((mask & 4) != 0)
                        run.FontWeight = new FontWeight((ushort)weight);
                    if ((mask & 8) != 0)
                        run.FontStyle = italic ? FontStyle.Italic : FontStyle.Normal;
                    if ((mask & 16) != 0)
                        run.FontFamily = new FontFamily(family);
                    if ((mask & 32) != 0)
                        run.TextDecorations = (TextDecorations)decoration;
                    return run;
                })
                .ToArray();
            n.NavigationKind = r.ReadInt32();
            n.NavigationDuration = r.ReadSingle();
            n.NavigationEasing = r.ReadInt32();
            n.GradientKind = r.ReadInt32();
            if (n.GradientKind != 0)
            {
                n.GradientPoints = Enumerable.Range(0, 4).Select(_ => r.ReadSingle()).ToArray();
                n.Stops = Enumerable.Range(0, r.ReadInt32()).Select(_ => (r.ReadSingle(), Color(r))).ToArray();
            }
            n.BackgroundImage = Bytes(r);
            n.BackgroundFit = r.ReadInt32();
            n.Corners = Enumerable.Range(0, 4).Select(_ => r.ReadSingle()).ToArray();
            n.BorderSvg = Bytes(r);
            n.ViewType = TextValue(r);
            return n;
        }
    }

    private static Windows.UI.Color Color(BinaryReader r)
    {
        var red = r.ReadByte();
        var green = r.ReadByte();
        var blue = r.ReadByte();
        return Windows.UI.Color.FromArgb(r.ReadByte(), red, green, blue);
    }

    private static byte[] Bytes(BinaryReader r)
    {
        var length = r.ReadInt32();
        if (length < 0 || length > 64 * 1024 * 1024)
            throw new InvalidDataException();
        var data = r.ReadBytes(length);
        if (data.Length != length)
            throw new EndOfStreamException();
        return data;
    }

    private static string TextValue(BinaryReader r) => Encoding.UTF8.GetString(Bytes(r));

    private static BinaryReader Reader(byte* bytes, int length) =>
        new(new MemoryStream(new ReadOnlySpan<byte>(bytes, length).ToArray()), Encoding.UTF8);

    private sealed class Element
    {
        internal Border Root = new();
        internal readonly FrameworkElement Content;
        internal readonly Canvas? Children;
        internal PresentationNode Node;
        internal int PresentedDepth = -1;
        internal uint PresentedPage, OutgoingPage;
        internal bool NavigationPending;
        internal Storyboard? NavigationAnimation;
        internal long NavigationGeneration;
        private Grid? _layers;
        private Border? _imageBackground;
        private Image? _borderImage;
        private int _borderGeneration;
        internal bool Active = true;
        internal bool Recycled;
        private bool _presented, _pendingTabEntrance, _textApplied;
        internal int TabEntranceCount;
        private Microsoft.UI.Composition.Vector3KeyFrameAnimation? _tabAnimation;
        private static readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
        internal uint FocusRevision,
            ScrollRevision;
        internal (uint, double, double)? LastScroll;

        internal Element(PresentationNode node, Renderer? owner)
        {
            Node = node;
            if (node.Kind == 15)
                Root = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                    "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\" />");
            Children = node.Kind is 1 or 4 or 10 or 11 or 15 ? new Canvas() : null;
            Content = node.Kind switch
            {
                1 or 10 or 11 => Children!,
                2 => new TextBlock(),
                3 => new Button(),
                4 => new ScrollViewer
                {
                    Content = Children,
                    ZoomMode = ZoomMode.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
                5 when (node.Input[4] & 1) != 0 => new PasswordBox(),
                5 => new TextBox(),
                6 => new ToggleSwitch(),
                7 => new Slider(),
                8 => new Image(),
                9 => new FontIcon(),
                12 => new RadioButton(),
                16 => new Border(),
                13 => new StackPanel { Orientation = Orientation.Horizontal },
                15 => new NavigationView
                {
                    Content = Children,
                    IsSettingsVisible = false,
                    PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                    IsTitleBarAutoPaddingEnabled = false,
                    IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
                },
                _ => throw new NotSupportedException($"WinUI node kind {node.Kind}"),
            };
            Root.Child = Content;
            if (Content is Control control)
            {
                control.MinWidth = 0;
                control.MinHeight = 0;
                control.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                control.VerticalContentAlignment = VerticalAlignment.Stretch;
            }
            if (owner is null)
                return;
            void Emit(uint kind, float value = 0, string text = "")
            {
                if (Active && !owner.Updating)
                    owner.Emit(Node.Id, kind, value, text);
            }
            // Only controls exposing these states in the shared API need routed
            // input handlers. Text/layout nodes otherwise add unused delegates
            // and forward every bubbling pointer/focus event across the bridge.
            if (node.Kind is 3 or 5 or 6 or 7 or 12)
            {
                Content.GotFocus += (_, _) => Emit(4, 1);
                Content.LostFocus += (_, _) => Emit(4, 0);
                Content.PointerEntered += (_, _) => Emit(8, 1);
                Content.PointerExited += (_, _) => Emit(8, 0);
            }
            if (node.Kind is 3 or 12)
            {
                Content.PointerPressed += (_, _) => Emit(7, 1);
                Content.PointerReleased += (_, _) => Emit(7, 0);
                Content.PointerCanceled += (_, _) => Emit(7, 0);
            }
            switch (Content)
            {
                case Button b:
                    b.Click += (_, _) => Emit(1);
                    break;
                case TextBox t:
                    t.TextChanged += (_, _) => Emit(2, 0, t.Text);
                    t.KeyDown += (_, e) =>
                    {
                        if (e.Key == Windows.System.VirtualKey.Enter && !t.AcceptsReturn)
                            Emit(9, Node.Input[1], t.Text);
                    };
                    break;
                case PasswordBox p:
                    p.PasswordChanged += (_, _) => Emit(2, 0, p.Password);
                    p.KeyDown += (_, e) =>
                    {
                        if (e.Key == Windows.System.VirtualKey.Enter)
                            Emit(9, Node.Input[1], p.Password);
                    };
                    break;
                case ToggleSwitch t:
                    t.Toggled += (_, _) => Emit(3, t.IsOn ? 1 : 0);
                    break;
                case RadioButton r:
                    // Checked covers pointer, keyboard and accessibility selection.
                    // Click misses arrow-key/UIA changes and leaves managed state stale.
                    r.Checked += (_, _) => Emit(3, 1);
                    break;
                case Slider s:
                    s.ValueChanged += (_, _) => Emit(3, (float)s.Value);
                    break;
                case ScrollViewer s:
                    Point? pressed = null;
                    s.PointerPressed += (_, args) => pressed = args.GetCurrentPoint(s).Position;
                    s.PointerMoved += (_, args) => {
                        var point = args.GetCurrentPoint(s).Position;
                        if (pressed is { } start && (Math.Abs(point.X-start.X)>4 || Math.Abs(point.Y-start.Y)>4)) pressed=null;
                    };
                    s.PointerCanceled += (_, _) => pressed=null;
                    s.PointerReleased += (_, args) => {
                        if (pressed is not null) Emit(12,(float)args.GetCurrentPoint(s).Position.Y);
                        pressed=null;
                    };
                    s.ViewChanged += (_, _) => ReportScroll(owner);
                    s.SizeChanged += (_, _) => ReportScroll(owner);
                    break;
                case NavigationView nav:
                    nav.ItemInvoked += (_, args) =>
                    {
                        if (args.InvokedItemContainer is NavigationViewItem item)
                            InvokeNavigationItem(owner, nav.MenuItems.IndexOf(item));
                    };
                    nav.PaneOpened += (_, _) =>
                    {
                        if (!Node.Open)
                            Emit(3, -1);
                    };
                    nav.PaneClosed += (_, _) =>
                    {
                        if (Node.Open)
                            Emit(3, -1);
                    };
                    break;
            }
        }

        internal void InvokeNavigationItem(Renderer owner, int index)
        {
            if (Active && !owner.Updating)
                owner.Emit(Node.Id, 3, index, "");
        }

        internal void ReportScroll(Renderer owner)
        {
            if (!Active || owner.Updating || !Node.ObserveScroll || Content is not ScrollViewer scroll)
                return;
            double offset = Node.Axis == 1 ? scroll.HorizontalOffset : scroll.VerticalOffset;
            double viewport = Node.Axis == 1 ? scroll.ViewportWidth : scroll.ViewportHeight;
            var metric = (Node.ScrollRevision, offset, viewport);
            if (LastScroll == metric)
                return;
            LastScroll = metric;
            owner.Emit(
                Node.Id,
                10,
                (float)offset,
                FormattableString.Invariant($"{Node.ScrollRevision}:{viewport}")
            );
        }

        // Animate the content canvas only. The NavigationView selection indicator
        // and pane keep their native animation and never get rebuilt for a tab change.
        internal void PresentTab(bool? animationsEnabled = null)
        {
            if (!_pendingTabEntrance || Children is null)
                return;
            _pendingTabEntrance = false;
            StopTabAnimation();
            Children.Translation = Vector3.Zero;
            if (!(animationsEnabled ?? _uiSettings.AnimationsEnabled))
                return;
            var compositor = ElementCompositionPreview.GetElementVisual(Children).Compositor;
            var animation = compositor.CreateVector3KeyFrameAnimation();
            _tabAnimation = animation;
            using var easing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(.16f, 1), new Vector2(.3f, 1));
            animation.Target = "Translation";
            animation.InsertKeyFrame(0, new Vector3(0, 24, 0));
            animation.InsertKeyFrame(1, Vector3.Zero, easing);
            animation.Duration = TimeSpan.FromMilliseconds(180);
            Children.StartAnimation(animation);
            TabEntranceCount++;
        }

        internal void SettleTab()
        {
            _pendingTabEntrance = false;
            StopTabAnimation();
            if (Children != null)
                Children.Translation = Vector3.Zero;
        }

        internal void StopTabAnimation()
        {
            if (_tabAnimation is null)
                return;
            Children!.StopAnimation(_tabAnimation);
            _tabAnimation.Dispose();
            _tabAnimation = null;
        }

        internal void PlaceChild(Element child)
        {
            // Shared layout already reserves SideDock padding. NavigationView
            // reserves its own pane; remove the shared inset exactly once.
            Canvas.SetLeft(child.Root, child.Node.X - (Node.Kind == 15 ? Node.Padding[0] : 0));
            Canvas.SetTop(child.Root, child.Node.Y);
        }

        private Grid Layers()
        {
            if (_layers is null)
            {
                Root.Child = null;
                _layers = new Grid();
                _layers.Children.Add(Content);
                Root.Child = _layers;
            }
            return _layers;
        }

        private void ApplyDecoration(PresentationNode n, PresentationNode prior, bool measuring)
        {
            if (n.GradientKind == 1)
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(n.GradientPoints[0], n.GradientPoints[1]),
                    EndPoint = new Point(n.GradientPoints[2], n.GradientPoints[3]),
                };
                foreach (var stop in n.Stops)
                    brush.GradientStops.Add(new GradientStop { Offset = stop.Offset, Color = stop.Color });
                Root.Background = brush;
            }
            if (measuring)
                return;
            if (n.BackgroundImage.Length > 0)
            {
                if (_imageBackground is null)
                {
                    _imageBackground = new Border { IsHitTestVisible = false };
                    Layers().Children.Insert(0, _imageBackground);
                }
                if (_imageBackground.Background is not ImageBrush
                    || !n.BackgroundImage.AsSpan().SequenceEqual(prior.BackgroundImage))
                {
                    using var stream = new MemoryStream(n.BackgroundImage);
                    var bitmap = new BitmapImage();
                    bitmap.SetSource(stream.AsRandomAccessStream());
                    _imageBackground.Background = new ImageBrush { ImageSource = bitmap };
                }
                ((ImageBrush)_imageBackground.Background).Stretch = n.BackgroundFit switch
                {
                    1 => Stretch.UniformToFill, 2 => Stretch.Fill, 3 => Stretch.None, _ => Stretch.Uniform
                };
                _imageBackground.CornerRadius = Root.CornerRadius;
                _imageBackground.Visibility = Visibility.Visible;
            }
            else if (_imageBackground != null)
                _imageBackground.Visibility = Visibility.Collapsed;
            if (n.BorderSvg.Length > 0)
            {
                if (_borderImage is null)
                {
                    _borderImage = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill };
                    Layers().Children.Add(_borderImage);
                }
                _borderImage.Visibility = Visibility.Visible;
                if (_borderImage.Source is null || !n.BorderSvg.AsSpan().SequenceEqual(prior.BorderSvg))
                {
                    int generation = ++_borderGeneration;
                    _ = WinUIImages.SetSvg(_borderImage, n.BorderSvg,
                        () => Active && generation == _borderGeneration);
                }
            }
            else if (_borderImage != null)
            {
                ++_borderGeneration;
                _borderImage.Visibility = Visibility.Collapsed;
            }
        }

        internal void Apply(PresentationNode n, Renderer? owner, bool measuring = false)
        {
            var prior = Node;
            if (!measuring && _presented && n.Kind == 15 && n.Selected != prior.Selected)
                _pendingTabEntrance = true;
            Node = n;
            _presented |= !measuring;
            if (n.Kind == 16 && Content is Border slot && !measuring
                && (prior.Id != n.Id || prior.ViewType != n.ViewType || slot.Child == null))
            {
                var child = PlatformViews.Create(n.ViewType, n.Id);
                if (child != null)
                {
                    child.HorizontalAlignment = HorizontalAlignment.Stretch;
                    child.VerticalAlignment = VerticalAlignment.Stretch;
                }
                slot.Child = child;
            }
            if (n.Kind != 15)
                Root.Background = ColorBrush(n.Background);
            Root.CornerRadius = n.Corners.Length == 4
                ? new CornerRadius(n.Corners[0], n.Corners[1], n.Corners[2], n.Corners[3])
                : new CornerRadius(n.Radius);
            Root.BorderBrush = ColorBrush(n.BorderColor);
            Root.BorderThickness = new Thickness(n.BorderSvg.Length == 0 ? n.BorderWidth : 0);
            ApplyDecoration(n, prior, measuring);
            Root.Padding = Children is null
                ? new Thickness(n.Padding[0], n.Padding[1], n.Padding[2], n.Padding[3])
                : new Thickness(0);
            Root.Opacity = n.Opacity;
            Root.FlowDirection =
                n.Flow == 2 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Root.Visibility = n.Visibility == 2 ? Visibility.Collapsed : Visibility.Visible;
            Root.IsHitTestVisible = n.Enabled;
            if (Root.RenderTransform is not CompositeTransform transform)
                Root.RenderTransform = transform = new CompositeTransform();
            transform.TranslateX = n.Transform[0];
            transform.TranslateY = n.Transform[1];
            transform.ScaleX = n.Transform[2];
            transform.ScaleY = n.Transform[3];
            transform.Rotation = n.Transform[4];
            Root.RenderTransformOrigin = new Point(.5, .5);
            if (!measuring)
            {
                Root.Width = n.Width;
                Root.Height = n.Height;
                var visual = ElementCompositionPreview.GetElementVisual(Root);
                visual.TransformMatrix = Matrix4x4.CreateTranslation(-n.Width/2, -n.Height/2, 0)
                    * n.Projection * Matrix4x4.CreateTranslation(n.Width/2, n.Height/2, 0);
                Canvas.SetLeft(Root, n.X);
                Canvas.SetTop(Root, n.Y);
                Canvas.SetZIndex(Root, n.Z);
                Root.Clip = n.Clip || n.Kind == 10
                    ? new RectangleGeometry { Rect = new Rect(0, 0, n.Width, n.Height) }
                    : null;
            }
            // Navigation carries ThemeMode in the control revision slot. Apply it
            // even when the host is used without a desktop sidebar.
            if (n.Kind == 10)
                Root.RequestedTheme = (ElementTheme)n.FocusRevision;
            var foreground = ColorBrush(n.Foreground);
            if (Content is Control c)
            {
                c.IsEnabled = n.Enabled;
                c.FontSize = n.FontSize;
                c.FontWeight = new FontWeight((ushort)n.Weight);
                c.FontStyle = n.Italic ? FontStyle.Italic : FontStyle.Normal;
                // NavigationView owns theme-aware label/icon brushes. A local
                // protocol foreground masks its ThemeResource values in Dark mode.
                if (c is NavigationView)
                    c.ClearValue(Control.ForegroundProperty);
                else
                    c.Foreground = foreground;
                if (n.Family.Length > 0)
                    c.FontFamily = new FontFamily(n.Family);
                else
                    c.ClearValue(Control.FontFamilyProperty);
            }
            switch (Content)
            {
                case TextBlock t:
                    t.FontSize = n.FontSize;
                    t.FontWeight = new FontWeight((ushort)n.Weight);
                    t.FontStyle = n.Italic ? FontStyle.Italic : FontStyle.Normal;
                    t.Foreground = foreground;
                    t.FontFamily = new FontFamily(n.Family.Length > 0 ? n.Family : "Segoe UI");
                    t.TextAlignment = n.Alignment switch
                    {
                        1 => TextAlignment.Center,
                        2 => TextAlignment.Right,
                        3 => TextAlignment.Justify,
                        _ => TextAlignment.Left,
                    };
                    t.TextWrapping = TextWrapping.Wrap;
                    t.MaxLines = Math.Max(0, n.Lines);
                    t.TextTrimming =
                        n.Overflow == 5 ? TextTrimming.CharacterEllipsis : TextTrimming.None;
                    t.CharacterSpacing = (int)(n.Spacing / Math.Max(1, n.FontSize) * 1000);
                    t.LineHeight = n.LineHeight;
                    t.TextDecorations = (TextDecorations)n.Decoration;
                    if (_textApplied && prior.Text == n.Text && prior.Runs.SequenceEqual(n.Runs))
                        break;
                    _textApplied = true;
                    t.Inlines.Clear();
                    if (n.Runs.Length > 0)
                    {
                        foreach (var run in n.Runs)
                            t.Inlines.Add(run);
                    }
                    else
                        t.Text = n.Text;
                    break;
                case Button b:
                    if (!Equals(b.Content, n.Text))
                        b.Content = n.Text;
                    if (n.Background.A != 0 || n.GradientKind != 0 || n.BackgroundImage.Length > 0)
                    {
                        b.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                        b.BorderThickness = new Thickness(0);
                        b.Padding = new Thickness(0);
                    }
                    else
                    {
                        b.ClearValue(Control.BackgroundProperty);
                        b.ClearValue(Control.BorderThicknessProperty);
                        b.ClearValue(Control.PaddingProperty);
                    }
                    break;
                case TextBox t:
                    if (t.Text != n.Text)
                        t.Text = n.Text;
                    t.PlaceholderText = n.Placeholder;
                    t.AcceptsReturn = (n.Input[4] & 2) != 0;
                    t.TextWrapping = t.AcceptsReturn ? TextWrapping.Wrap : TextWrapping.NoWrap;
                    t.IsSpellCheckEnabled = (n.Input[4] & 4) != 0;
                    break;
                case PasswordBox p:
                    if (p.Password != n.Text)
                        p.Password = n.Text;
                    p.PlaceholderText = n.Placeholder;
                    break;
                case ToggleSwitch t:
                    t.IsOn = n.Value != 0;
                    break;
                case RadioButton r:
                    r.Content = n.Text;
                    r.IsChecked = n.Value != 0;
                    break;
                case Slider s:
                    s.Minimum = n.Minimum;
                    s.Maximum = n.Maximum;
                    s.Value = n.Value;
                    break;
                case FontIcon icon:
                    icon.Glyph = n.Text switch
                    {
                        "star" => "\uE734",
                        "check" => "\uE73E",
                        "info" => "\uE946",
                        _ => n.Text,
                    };
                    icon.FontSize = n.FontSize;
                    icon.Foreground = foreground;
                    break;
                case Image image:
                    image.Stretch = n.Fit switch
                    {
                        1 => Stretch.UniformToFill,
                        2 => Stretch.Fill,
                        3 => Stretch.None,
                        _ => Stretch.Uniform,
                    };
                    if (!prior.Image.AsSpan().SequenceEqual(n.Image) || image.Source is null)
                    {
                        if (n.Image.Length == 0)
                            image.Source = null;
                        else
                        {
                            using var stream = new MemoryStream(n.Image);
                            var bitmap = new BitmapImage();
                            bitmap.SetSource(stream.AsRandomAccessStream());
                            image.Source = bitmap;
                        }
                    }
                    break;
                case ScrollViewer scroll:
                    scroll.HorizontalScrollMode =
                        n.Axis == 1 ? ScrollMode.Enabled : ScrollMode.Disabled;
                    scroll.VerticalScrollMode =
                        n.Axis == 1 ? ScrollMode.Disabled : ScrollMode.Enabled;
                    scroll.HorizontalScrollBarVisibility =
                        n.Axis == 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
                    scroll.VerticalScrollBarVisibility =
                        n.Axis == 1 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
                    break;
                case NavigationView nav:
                    // Theme both the opaque island surface and its native controls.
                    Root.RequestedTheme = (ElementTheme)n.Theme;
                    nav.RequestedTheme = (ElementTheme)n.Theme;
                    nav.PaneTitle = n.Title;
                    nav.OpenPaneLength = Math.Max(52, n.PaneWidth);
                    nav.CompactPaneLength = 52;
                    if (nav.IsPaneOpen != n.Open)
                        nav.IsPaneOpen = n.Open;
                    if (!prior.Menu.SequenceEqual(n.Menu) || nav.MenuItems.Count != n.Menu.Length)
                    {
                        nav.MenuItems.Clear();
                        foreach (var item in n.Menu)
                            nav.MenuItems.Add(
                                new NavigationViewItem
                                {
                                    Content = item.Label,
                                    Icon = new FontIcon { Glyph = item.Icon },
                                }
                            );
                    }
                    var selectedItem = n.Selected >= 0 && n.Selected < nav.MenuItems.Count
                        ? nav.MenuItems[n.Selected] : null;
                    if (!ReferenceEquals(nav.SelectedItem, selectedItem))
                        nav.SelectedItem = selectedItem;
                    break;
                case StackPanel header:
                    header.Visibility = n.HeaderVisible ? Visibility.Visible : Visibility.Collapsed;
                    // Update retained children on theme changes without recreating
                    // buttons (and losing their native focus/pressed state).
                    foreach (var child in header.Children)
                        if (child is TextBlock label) label.Foreground = foreground;
                        else if (child is Control action) action.Foreground = foreground;
                    if (header.Children.Count > 0 && prior.Text == n.Text
                        && prior.FontSize == n.FontSize && prior.BackVisible == n.BackVisible
                        && prior.CanPop == n.CanPop && prior.Leading == n.Leading
                        && prior.Actions.SequenceEqual(n.Actions))
                        break;
                    header.Children.Clear();
                    void ActionButton(string text, float value)
                    {
                        var b = new Button { Content = text, Foreground = foreground };
                        if (owner != null)
                            b.Click += (_, _) => owner.Emit(n.Id, 3, value, "");
                        header.Children.Add(b);
                    }
                    if (n.BackVisible && n.CanPop)
                        ActionButton("Back", -1);
                    for (int i = 0; i < n.Leading && i < n.Actions.Length; i++)
                        ActionButton(n.Actions[i], i);
                    header.Children.Add(
                        new TextBlock
                        {
                            Text = n.Text,
                            Foreground = foreground,
                            FontSize = n.FontSize,
                            VerticalAlignment = VerticalAlignment.Center,
                        }
                    );
                    for (int i = n.Leading; i < n.Actions.Length; i++)
                        ActionButton(n.Actions[i], i);
                    break;
            }
            if (!measuring && n.FocusRevision != FocusRevision)
            {
                FocusRevision = n.FocusRevision;
                if (n.Focus && Content is Control input)
                    input.Focus(FocusState.Programmatic);
            }
        }
    }

    private sealed class Renderer : IDisposable
    {
        internal readonly DesktopWindowXamlSource Source = new();
        internal readonly Canvas Root = new();
        internal readonly Canvas MeasurementRoot = new() { Opacity = 0, IsHitTestVisible = false };
        private readonly Dictionary<uint, Element> _measurementControls = new();
        internal Size Measure(PresentationNode node, float width, float height)
        {
            bool reusable = node.Kind is 2 or 3 or 12;
            if (!reusable || !_measurementControls.TryGetValue(node.Kind, out var element))
            {
                element = new Element(node, null);
                element.Root.Child = null;
                AutomationProperties.SetAccessibilityView(element.Content, AccessibilityView.Raw);
                if (element.Content is Control measuredControl) measuredControl.IsTabStop = false;
                MeasurementRoot.Children.Add(element.Content);
                if (reusable) _measurementControls.Add(node.Kind, element);
            }
            try
            {
                element.Apply(node, null, true);
                if (element.Content is Control control) control.ApplyTemplate();
                // The hidden canvas can arrange between requests; do not reuse
                // its layout result for a different engine measurement pass.
                element.Content.InvalidateMeasure();
                element.Content.Measure(new Size(width, height));
                return element.Content.DesiredSize;
            }
            finally
            {
                if (!reusable) MeasurementRoot.Children.Remove(element.Content);
            }
        }
        private readonly Dictionary<uint, Stack<Element>> _leafPool = new();
        internal int ReusedLeafControls;
        internal int PooledLeafCount => _leafPool.Values.Sum(pool => pool.Count);
        private void PoolLeaf(Element element)
        {
            // Never carry input focus/pressed state or asynchronous image work
            // over to another logical node. Retain at most 32 controls per kind.
            if (!ReferenceEquals(element.Root.Child, element.Content)
                || VisualTreeHelper.GetParent(element.Root) != null
                || element.Node.Kind is not (2 or 3)
                || element.Node.BackgroundImage.Length != 0 || element.Node.BorderSvg.Length != 0
                || element.Content is Control { FocusState: not FocusState.Unfocused }
                || element.Content is Button { IsPressed: true }) return;
            if (!_leafPool.TryGetValue(element.Node.Kind, out var pool))
                _leafPool.Add(element.Node.Kind, pool = new());
            if (pool.Count < 32)
            {
                // WinUI may retain a removed layout wrapper until its unload
                // finishes, even after Parent becomes null. Reuse the expensive
                // native control/template, with a fresh lightweight wrapper.
                element.Root.Child = null;
                element.Root = new Border { Child = element.Content };
                pool.Push(element);
            }
        }
        private Element RentElement(PresentationNode node)
        {
            if (_leafPool.TryGetValue(node.Kind, out var pool) && pool.TryPop(out var element))
            {
                ReusedLeafControls++;
                element.Recycled = true;
                element.Active = true;
                return element;
            }
            return new Element(node, this);
        }
        internal readonly Dictionary<uint, Element> Elements = new();
        internal bool Updating;
        private (int Width, int Height) _size;
        private NavigationRegression? _navigationRegression;
        private WindowRegression? _windowRegression;
        private readonly nint _host;
        private readonly string _radioGroupPrefix = $"dotnative-{Guid.NewGuid():N}-";
        private readonly delegate* unmanaged[Cdecl]<uint, uint, float, byte*, int, void> _emit;

        internal Renderer(
            nint host,
            delegate* unmanaged[Cdecl]<uint, uint, float, byte*, int, void> emit
        )
        {
            _emit = emit;
            _host = host;
            Source.Initialize(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(host));
            var surface = new Grid();
            surface.Children.Add(Root);
            surface.Children.Add(MeasurementRoot);
            Source.Content = surface;
        }

        internal void Emit(uint id, uint kind, float value, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            fixed (byte* data = bytes)
                _emit(id, kind, value, data, bytes.Length);
        }

        public void Dispose()
        {
            _navigationRegression?.Dispose();
            foreach (var element in Elements.Values)
            {
                element.Active = false;
                ++element.NavigationGeneration;
                element.NavigationAnimation?.Stop();
                element.StopTabAnimation();
            }
            Source.Content = null;
            MeasurementRoot.Children.Clear();
            _leafPool.Clear();
            _measurementControls.Clear();
            Elements.Clear();
            Source.Dispose();
        }

        internal int ReusedRowTrees;

        private void RecycleRows(uint[] removed, PresentationNode[] changed)
        {
            var retired = removed.ToHashSet();
            var incoming = changed.ToDictionary(n => n.Id);
            bool OldTree(uint id, List<Element> tree, List<string> shape)
            {
                if (tree.Count >= 256 || !retired.Contains(id) || !Elements.TryGetValue(id, out var e)
                    || e.Node.Kind is not (1 or 2 or 3) || e.Node.BackgroundImage.Length != 0
                    || e.Node.BorderSvg.Length != 0 || !ReferenceEquals(e.Root.Child, e.Content)
                    || e.Content is Control c && c.FocusState != FocusState.Unfocused
                    || e.Content is Button b && (b.IsPressed || b.IsPointerOver)) return false;
                tree.Add(e);
                shape.Add($"{e.Node.Kind}:{e.Node.Children.Length}");
                foreach (var child in e.Node.Children) if (!OldTree(child, tree, shape)) return false;
                return true;
            }
            bool NewTree(uint id, List<PresentationNode> tree, List<string> shape)
            {
                if (tree.Count >= 256 || Elements.ContainsKey(id) || !incoming.TryGetValue(id, out var n)
                    || n.Kind is not (1 or 2 or 3) || n.BackgroundImage.Length != 0 || n.BorderSvg.Length != 0) return false;
                tree.Add(n);
                shape.Add($"{n.Kind}:{n.Children.Length}");
                foreach (var child in n.Children) if (!NewTree(child, tree, shape)) return false;
                return true;
            }
            // Match only rows inside a surviving scroll document, never across lists.
            foreach (var scroll in Elements.Values.Where(e => e.Node.Kind == 4 && e.Node.Virtualized).ToArray())
            {
                if (retired.Contains(scroll.Node.Id) || scroll.Node.Children.Length != 1) continue;
                uint documentId = scroll.Node.Children[0];
                if (!Elements.TryGetValue(documentId, out var document) || !incoming.TryGetValue(documentId, out var nextDocument)) continue;
                var spare = new Dictionary<string, Stack<List<Element>>>();
                foreach (var row in document.Node.Children)
                {
                    var tree = new List<Element>(); var shape = new List<string>();
                    if (!OldTree(row, tree, shape)) continue;
                    var key = string.Join(",", shape);
                    if (!spare.TryGetValue(key, out var bucket)) spare[key] = bucket = new();
                    bucket.Push(tree);
                }
                foreach (var row in nextDocument.Children)
                {
                    var tree = new List<PresentationNode>(); var shape = new List<string>();
                    if (!NewTree(row, tree, shape) || !spare.TryGetValue(string.Join(",", shape), out var bucket) || bucket.Count == 0) continue;
                    var old = bucket.Pop();
                    // Keep the inner row Canvas and its controls; WinUI needs a fresh outer wrapper after detachment.
                    Detach(old[0].Root);
                    old[0].Root.Child = null;
                    old[0].Root = new Border { Child = old[0].Content };
                    for (int index = 0; index < old.Count; index++)
                    {
                        var e = old[index];
                        Elements.Remove(e.Node.Id);
                        e.StopTabAnimation();
                        e.Recycled = true;
                        Elements.Add(tree[index].Id, e);
                        if (e.Node.Kind is 2 or 3) ReusedLeafControls++;
                    }
                    ReusedRowTrees++;
                }
            }
        }

        internal void Update(BinaryReader r, int width, int height)
        {
            if (r.ReadUInt32() != 3)
                throw new InvalidDataException("WinUI presentation ABI mismatch");
            var root = r.ReadUInt32();
            var removed = Enumerable.Range(0, r.ReadInt32()).Select(_ => r.ReadUInt32()).ToArray();
            var changed = Enumerable
                .Range(0, r.ReadInt32())
                .Select(_ =>
                {
                    using var nodeReader = new BinaryReader(new MemoryStream(Bytes(r)));
                    return PresentationNode.Read(nodeReader);
                })
                .ToArray();
            Updating = true;
            try
            {
                RecycleRows(removed, changed);
                foreach (var id in removed)
                    if (Elements.Remove(id, out var old))
                    {
                        old.Active = false;
                        ++old.NavigationGeneration;
                        old.NavigationAnimation?.Stop();
                        old.StopTabAnimation();
                        Detach(old.Root);
                        PoolLeaf(old);
                    }
                foreach (var n in changed)
                {
                    if (!Elements.TryGetValue(n.Id, out var e))
                        Elements[n.Id] = e = RentElement(n);
                    else if (n.Kind == 5 && (n.Input[4] & 1) != (e.Node.Input[4] & 1))
                    {
                        e.Active = false;
                        Detach(e.Root);
                        Elements[n.Id] = e = new Element(n, this);
                    }
                    e.Apply(n, this);
                }
                foreach (var e in Elements.Values)
                    if (e.Children is { } panel)
                    {
                        var desired = e.Node.Children.Select(id => Elements[id].Root).ToArray();
                        foreach (var child in panel.Children.ToArray())
                            if (!desired.Contains(child))
                                panel.Children.Remove(child);
                        foreach (var child in desired)
                            if (!panel.Children.Contains(child))
                            {
                                Detach(child);
                                try { panel.Children.Add(child); }
                                catch (Exception error) {
                                    throw new InvalidOperationException($"Attach failed: parent={e.Node.Id}, child={Elements.Values.First(x => ReferenceEquals(x.Root, child)).Node.Id}, logical={child.Parent?.GetType().Name}, visual={VisualTreeHelper.GetParent(child)?.GetType().Name}, childIndex={panel.Children.IndexOf(child)}", error);
                                }
                            }
                        double contentWidth = 0,
                            contentHeight = 0;
                        foreach (var id in e.Node.Children)
                        {
                            var child = Elements[id];
                            if (child.Content is RadioButton radio)
                            {
                                string group = _radioGroupPrefix + e.Node.Id;
                                if (radio.GroupName != group)
                                    radio.GroupName = group;
                            }
                            e.PlaceChild(child);
                            contentWidth = Math.Max(contentWidth, child.Node.X + child.Node.Width);
                            contentHeight = Math.Max(
                                contentHeight,
                                child.Node.Y + child.Node.Height
                            );
                        }
                        if (e.Content is ScrollViewer)
                        {
                            panel.Width = Math.Max(0, contentWidth);
                            panel.Height = Math.Max(0, contentHeight);
                        }
                        if (e.Node.Kind == 10)
                            SynchronizeNavigation(e);
                    }
                if (root != 0 && Elements[root].Root.Parent != Root)
                {
                    Root.Children.Clear();
                    Detach(Elements[root].Root);
                    Root.Children.Add(Elements[root].Root);
                }
                if (root == 0)
                    Root.Children.Clear();
                if (_size != (width, height))
                {
                    Source.SiteBridge.MoveAndResize(
                        new Windows.Graphics.RectInt32(0, 0, width, height));
                    _size = (width, height);
                }
                Root.UpdateLayout();
                // A navigation host owns the page transition, including the batch
                // between sidebar selection and the asynchronous push/pop.
                bool hasNavigationHost = Elements.Values.Any(e => e.Node.Kind == 10);
                foreach (var e in Elements.Values)
                {
                    if (hasNavigationHost)
                        e.SettleTab();
                    else
                        e.PresentTab();
                    if (e.NavigationPending)
                        PresentNavigation(e);
                }
                foreach (var e in Elements.Values)
                    if (
                        e.Content is ScrollViewer scroll
                        && e.ScrollRevision != e.Node.ScrollRevision
                    )
                    {
                        e.ScrollRevision = e.Node.ScrollRevision;
                        scroll.ChangeView(
                            e.Node.Axis == 1 ? e.Node.Offset : null,
                            e.Node.Axis == 0 ? e.Node.Offset : null,
                            null,
                            true
                        );
                    }
            }
            finally
            {
                Updating = false;
            }
            foreach (var e in Elements.Values)
                e.ReportScroll(this);
            if (_windowRegression is null && Environment.GetEnvironmentVariable("DOTNATIVE_WINUI_WINDOW_TEST") == "1")
                _windowRegression = new WindowRegression(_host);
            if (_navigationRegression is null
                && Environment.GetEnvironmentVariable("DOTNATIVE_WINUI_NAVIGATION_TEST") == "1")
                _navigationRegression = new NavigationRegression(this);
        }

        private void SynchronizeNavigation(Element host)
        {
            int depth = Math.Clamp((int)host.Node.Value, 0, host.Node.Children.Length);
            uint target = depth == 0 ? 0 : host.Node.Children[depth - 1];
            if (host.PresentedPage != target || host.PresentedDepth != depth)
            {
                ++host.NavigationGeneration;
                host.NavigationAnimation?.Stop();
                host.NavigationAnimation = null;
                host.OutgoingPage = host.PresentedPage;
                host.PresentedPage = target;
                host.PresentedDepth = depth;
                host.NavigationPending = true;
            }
            foreach (uint id in host.Node.Children)
            {
                var page = Elements[id];
                bool visible = id == target || id == host.OutgoingPage;
                page.Root.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                page.Root.IsHitTestVisible = id == target;
                Canvas.SetZIndex(page.Root, id == target ? 1 : 0);
            }
        }

        private void PresentNavigation(Element host)
        {
            host.NavigationPending = false;
            long generation = host.NavigationGeneration;
            Elements.TryGetValue(host.PresentedPage, out var incoming);
            Elements.TryGetValue(host.OutgoingPage, out var outgoing);
            void Complete()
            {
                if (!host.Active || generation != host.NavigationGeneration)
                    return;
                host.NavigationAnimation?.Stop();
                host.NavigationAnimation = null;
                if (outgoing != null && outgoing != incoming)
                    outgoing.Root.Visibility = Visibility.Collapsed;
                if (incoming != null)
                {
                    incoming.Root.Opacity = incoming.Node.Opacity;
                    Canvas.SetZIndex(incoming.Root, incoming.Node.Z);
                }
                host.OutgoingPage = 0;
                Emit(host.Node.Id, 5, host.PresentedDepth, "");
            }
            if (incoming is null || outgoing is null || incoming == outgoing
                || host.Node.NavigationKind == 3
                || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            {
                Complete();
                return;
            }
            Emit(host.Node.Id, 6, 0, "");
            var storyboard = new Storyboard();
            host.NavigationAnimation = storyboard;
            EasingFunctionBase? easing = host.Node.NavigationEasing switch
            {
                0 => null,
                2 => new CubicEase { EasingMode = EasingMode.EaseOut },
                3 => new CubicEase { EasingMode = EasingMode.EaseIn },
                _ => new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            var duration = new Duration(TimeSpan.FromSeconds(Math.Max(.001, host.Node.NavigationDuration)));
            if (host.Node.NavigationKind == 2)
            {
                // Keep the old page opaque underneath: fading both pages exposes
                // the host background and produces the visible flash.
                var animation = new DoubleAnimation
                {
                    From = 0, To = incoming.Node.Opacity, Duration = duration, EasingFunction = easing,
                };
                Storyboard.SetTarget(animation, incoming.Root);
                Storyboard.SetTargetProperty(animation, "Opacity");
                storyboard.Children.Add(animation);
            }
            else
            {
                var transform = (CompositeTransform)incoming.Root.RenderTransform;
                var animation = new DoubleAnimation
                {
                    From = incoming.Node.Transform[0] + (host.Node.NavigationKind == 1 ? incoming.Node.Width : 28),
                    To = incoming.Node.Transform[0], Duration = duration, EasingFunction = easing,
                };
                Storyboard.SetTarget(animation, transform);
                Storyboard.SetTargetProperty(animation, "TranslateX");
                storyboard.Children.Add(animation);
            }
            storyboard.Completed += (_, _) => Complete();
            storyboard.Begin();
        }

        private static void Detach(FrameworkElement element)
        {
            var parent = element.Parent ?? VisualTreeHelper.GetParent(element);
            if (parent is Panel panel)
                panel.Children.Remove(element);
            else if (parent is Border border)
                border.Child = null;
        }
    }

    private static readonly Dictionary<nint, Renderer> Renderers = new();

    [UnmanagedCallersOnly(
        EntryPoint = "dotnative_winui_create",
        CallConvs = [typeof(CallConvCdecl)]
    )]
    public static nint RendererCreate(
        nint host,
        delegate* unmanaged[Cdecl]<uint, uint, float, byte*, int, void> emit
    )
    {
        try
        {
            EnsureInitialized();
            var renderer = new Renderer(host, emit);
            var handle = ++_next;
            Renderers.Add(handle, renderer);
            return handle;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 0;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "dotnative_winui_update",
        CallConvs = [typeof(CallConvCdecl)]
    )]
    public static int RendererUpdate(nint handle, byte* bytes, int length, int width, int height)
    {
        try
        {
            using var r = Reader(bytes, length);
            Renderers[handle].Update(r, width, height);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return -1;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "dotnative_winui_measure",
        CallConvs = [typeof(CallConvCdecl)]
    )]
    public static int RendererMeasure(
        byte* bytes,
        int length,
        float width,
        float height,
        float* result
    )
    {
        try
        {
            using var r = Reader(bytes, length);
            var n = PresentationNode.Read(r);
            var size = Renderers.Values.First().Measure(n, width, height);
            result[0] = (float)size.Width;
            result[1] = (float)size.Height;
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return -1;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "dotnative_winui_destroy",
        CallConvs = [typeof(CallConvCdecl)]
    )]
    public static void RendererDestroy(nint handle)
    {
        try
        {
            if (Renderers.Remove(handle, out var renderer))
                renderer.Dispose();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
    }

    private static void SmokeTabPresentation(Renderer renderer)
    {
        PresentationNode Tab(int selected) => new()
        {
            Id = uint.MaxValue, Kind = 15, Width = 640, Height = 400,
            Padding = [261, 0, 0, 0], Transform = [0, 0, 1, 1, 0],
            FontSize = 14, Weight = 400, Opacity = 1, Enabled = true,
            PaneWidth = 260, Open = true, Selected = selected,
            Menu = [("Home", "\uE80F"), ("List", "\uE8FD")],
        };
        var sidebar = new Element(Tab(0), renderer);
        renderer.Updating = true;
        try
        {
            sidebar.Apply(Tab(0), renderer);
            renderer.Root.Children.Add(sidebar.Root);
            renderer.Root.UpdateLayout();
            sidebar.PresentTab(true);
            if (sidebar.TabEntranceCount != 0)
                throw new InvalidOperationException("Initial tab must not animate");
            var nav = (NavigationView)sidebar.Content;
            var firstItem = nav.MenuItems[0];
            for (int i = 0; i < 20; i++)
            {
                var next = Tab((i + 1) % 2);
                sidebar.Apply(next, renderer);
                sidebar.PresentTab(true);
                sidebar.Apply(Tab(next.Selected), renderer);
                sidebar.PresentTab(true);
            }
            if (sidebar.TabEntranceCount != 20 || !ReferenceEquals(firstItem, nav.MenuItems[0]))
                throw new InvalidOperationException("Tab changes rebuilt menu or restarted unchanged animation");
            sidebar.Apply(Tab(1), renderer);
            sidebar.PresentTab(false);
            if (sidebar.TabEntranceCount != 20 || sidebar.Children!.Translation != Vector3.Zero)
                throw new InvalidOperationException("Reduced motion must settle immediately");
            var childNode = Tab(0);
            childNode.Kind = 1;
            childNode.X = 277;
            var child = new Element(childNode, null);
            sidebar.PlaceChild(child);
            if (Canvas.GetLeft(child.Root) != 16)
                throw new InvalidOperationException("Sidebar inset applied twice");
            Console.WriteLine("PASS WinUI tab retention, rapid switching, reduced motion and content inset");
        }
        finally
        {
            sidebar.Active = false;
            sidebar.StopTabAnimation();
            renderer.Root.Children.Remove(sidebar.Root);
            renderer.Updating = false;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "dotnative_winui_smoke",
        CallConvs = [typeof(CallConvCdecl)]
    )]
    public static int RendererSmoke(nint handle, uint id)
    {
        try
        {
            var element = Renderers[handle].Elements[id];
            if (element.Content is not Button button)
                throw new InvalidOperationException("Expected WinUI Button");
            var peer = new ButtonAutomationPeer(button);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            SmokeTabPresentation(Renderers[handle]);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return -1;
        }
    }
}

// Keep async image loading outside the unsafe ABI class.
internal static class WinUIImages
{
    internal static async Task SetSvg(Image image, byte[] bytes, Func<bool> current)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var source = new SvgImageSource();
            var result = await source.SetSourceAsync(stream.AsRandomAccessStream());
            if (result != SvgImageSourceLoadStatus.Success)
                throw new InvalidDataException($"WinUI SVG decoration failed: {result}");
            if (current())
                image.Source = source;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
    }
}
