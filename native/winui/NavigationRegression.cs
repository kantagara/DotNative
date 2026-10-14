using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DotNative.WinUI;

public static unsafe partial class WinUIBridge
{
    // Opt-in end-to-end test for the real WidgetSamples managed host. Invokes
    // actual WinUI header buttons, then checks presentation on composition ticks.
    private sealed class NavigationRegression : IDisposable
    {
        private readonly Renderer _renderer;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
        private readonly Stopwatch _deadline = Stopwatch.StartNew();
        private readonly Element _sidebar;
        private readonly Element _navigation;
        private readonly Border _root;
        private readonly bool _animations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        private readonly Dictionary<uint, Element> _initialStableElements;
        private int _steps, _frames, _transitionFrames, _radioStage;
        private string? _radioExpected;
        private readonly Dictionary<string, RadioButton> _radios = new();
        private int _expectedDepth = 1;
        private ScrollViewer? _homeScroll;
        private double _homeOffset;
        private bool _scrollPrimed;
        private bool _disposed;
        private VirtualListRegression? _listRegression;

        internal NavigationRegression(Renderer renderer)
        {
            _renderer = renderer;
            CheckMeasurementReuse(renderer);
            _sidebar = renderer.Elements.Values.Single(e => e.Node.Kind == 15);
            _navigation = renderer.Elements.Values.Single(e => e.Node.Kind == 10);
            _root = (Border)renderer.Root.Children[0];
            _initialStableElements = StableElements(renderer);
            _timer.Tick += Tick;
            CompositionTarget.Rendering += Rendering;
            _timer.Start();
        }

        // Virtualized content legitimately grows/shrinks as native heights settle.
        // Everything outside those realization windows must keep its exact identity.
        // Comparing the full remaining dictionary also catches orphaned page controls.
        private static Dictionary<uint, Element> StableElements(Renderer renderer)
        {
            var dynamic = new HashSet<uint>();
            void Collect(uint id)
            {
                if (!dynamic.Add(id)) return;
                foreach (var child in renderer.Elements[id].Node.Children) Collect(child);
            }
            foreach (var list in renderer.Elements.Values.Where(e => e.Node.Virtualized))
                foreach (var child in list.Node.Children) Collect(child);
            return renderer.Elements.Where(pair => !dynamic.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private static void CheckMeasurementReuse(Renderer renderer)
        {
            foreach (uint kind in new uint[] { 2, 3, 12 })
                for (int variant = 0; variant < 12; variant++)
                {
                    var node = new PresentationNode {
                        Kind = kind, Padding = [0, 0, 0, 0], Transform = [0, 0, 1, 1, 0],
                        Enabled = true, Opacity = 1, FontSize = variant % 3 == 0 ? 24 : 14,
                        Weight = variant % 2 == 0 ? 700 : 400,
                        Family = variant % 2 == 0 ? "Consolas" : "",
                        Text = variant % 3 == 0 ? "A longer label that wraps across several lines" : "Short",
                        Lines = variant % 2, Foreground = Microsoft.UI.Colors.White,
                        Background = variant % 2 == 0 ? Microsoft.UI.Colors.Blue : Microsoft.UI.Colors.Transparent,
                    };
                    foreach (float width in new float[] { 32, 160, 1000 })
                    {
                        var measured = renderer.Measure(node, width, float.PositiveInfinity);
                        var fresh = new Element(node, null);
                        fresh.Root.Child = null;
                        renderer.MeasurementRoot.Children.Add(fresh.Content);
                        try {
                            fresh.Apply(node, null, true);
                            if (fresh.Content is Control control) control.ApplyTemplate();
                            fresh.Content.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
                            var expected = fresh.Content.DesiredSize;
                            if (Math.Abs(expected.Width - measured.Width) > .01
                                || Math.Abs(expected.Height - measured.Height) > .01)
                                throw new InvalidOperationException($"Reused measurement differs: kind={kind}, variant={variant}, width={width}: {measured} vs {expected}");
                        }
                        finally { renderer.MeasurementRoot.Children.Remove(fresh.Content); }
                    }
                }
            if (renderer.MeasurementRoot.Children.Count != 3)
                throw new InvalidOperationException("Measurement pool exceeded three retained controls");
            foreach (var child in renderer.MeasurementRoot.Children)
                if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetAccessibilityView(child)
                        != Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw
                    || child is Control { IsTabStop: true })
                    throw new InvalidOperationException("Measurement controls leaked into keyboard/accessibility navigation");
            Console.WriteLine("PASS reused WinUI measurement: 108 fresh-control comparisons, bounded to three controls");
        }

        private void Rendering(object? sender, object args)
        {
            if (_disposed || _navigation.NavigationAnimation is null)
                return;
            try
            {
                _frames++;
                _transitionFrames++;
                var incoming = _renderer.Elements[_navigation.PresentedPage];
                var outgoing = _renderer.Elements[_navigation.OutgoingPage];
                if (incoming.Root.Visibility != Visibility.Visible
                    || outgoing.Root.Visibility != Visibility.Visible
                    || outgoing.Root.Opacity < .999
                    || outgoing.Root.ActualWidth <= 0
                    || outgoing.Root.ActualHeight <= 0)
                    throw new InvalidOperationException("Transition exposed an empty or transparent page");
                CheckShell();
                if (_sidebar.TabEntranceCount != 0)
                    throw new InvalidOperationException("Sidebar entrance animation competed with page navigation");
                if (_navigation.PresentedDepth == 1 && _homeScroll != null
                    && Math.Abs(_homeScroll.VerticalOffset - _homeOffset) > .5)
                    throw new InvalidOperationException($"Home scroll moved during return: {_homeOffset} -> {_homeScroll.VerticalOffset}");
            }
            catch (Exception error) { Fail(error); }
        }

        private void CheckShell()
        {
            if (_renderer.Root.Children.Count != 1 || !ReferenceEquals(_root, _renderer.Root.Children[0])
                || !_renderer.Elements.TryGetValue(_sidebar.Node.Id, out var sidebar)
                || !ReferenceEquals(sidebar, _sidebar)
                || _renderer.Elements.Values.Count(e => e.Node.Kind == 15) != 1)
                throw new InvalidOperationException("Root/sidebar replaced during navigation");
        }

        private void Tick(object? sender, object args)
        {
            try
            {
                if (_deadline.Elapsed.TotalSeconds > 20)
                    throw new TimeoutException($"WidgetSamples navigation did not complete: radioStage={_radioStage}, expectedRadio={_radioExpected}, steps={_steps}, depth={_navigation.PresentedDepth}/{_expectedDepth}, scroll={_homeScroll?.VerticalOffset}/{_homeOffset}, maxScroll={_homeScroll?.ScrollableHeight}");
                CheckShell();
                if (_steps == 0 && !CheckRadios())
                    return;
                if (_navigation.PresentedDepth != _expectedDepth
                    || _navigation.NavigationAnimation != null || _navigation.NavigationPending)
                    return;
                if (!_scrollPrimed)
                {
                    _homeScroll = FindScroll(_renderer.Elements[_navigation.PresentedPage].Root)
                        ?? throw new InvalidOperationException("Home scroll missing");
                    _homeOffset = Math.Min(150, _homeScroll.ScrollableHeight);
                    _homeScroll.ChangeView(null, _homeOffset, null, true);
                    _scrollPrimed = true;
                    return;
                }
                if (_steps == 0 && Math.Abs(_homeScroll!.VerticalOffset - _homeOffset) > .5)
                    return;
                // Pop completion reaches managed code, which disposes the scoped
                // screen and sends deletion in the next batch.
                if (_expectedDepth == 1 && _navigation.Node.Children.Length != 1)
                    return;
                if (_steps > 0 && _animations && _transitionFrames == 0)
                    throw new InvalidOperationException("Transition completed without a rendered animation frame");
                if (_expectedDepth == 1)
                {
                    var stable = StableElements(_renderer);
                    if (stable.Count != _initialStableElements.Count
                        || _initialStableElements.Any(pair => !stable.TryGetValue(pair.Key, out var current)
                            || !ReferenceEquals(current, pair.Value)))
                        throw new InvalidOperationException($"Pop leaked or remounted nonvirtual page controls: {stable.Count}/{_initialStableElements.Count}");
                }
                if (_steps == 12)
                {
                    _timer.Stop();
                    CompositionTarget.Rendering -= Rendering;
                    var open = FindButton(_renderer.Elements[_navigation.PresentedPage].Root, "Virtual list · 10,000 items")
                        ?? throw new InvalidOperationException("Missing virtual list entry");
                    _listRegression = new VirtualListRegression(_renderer, _navigation, open, () =>
                    {
                        Console.WriteLine($"PASS WidgetSamples radio selection/grouping/state + Home/Cart: 12 transitions, {_frames} animation frames, stable sidebar/root, no empty page, scoped controls released; reducedMotion={!_animations}");
                        Dispose();
                    });
                    return;
                }
                string label = _expectedDepth == 1 ? "Cart" : "Back";
                var page = _renderer.Elements[_navigation.PresentedPage];
                var button = FindButton(page.Root, label)
                    ?? throw new InvalidOperationException($"Missing visible header button: {label}");
                _transitionFrames = 0;
                _expectedDepth = _expectedDepth == 1 ? 2 : 1;
                _steps++;
                _deadline.Restart();
                if (_steps <= 6)
                {
                    var peer = new ButtonAutomationPeer(button);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                }
                else
                {
                    var nav = (NavigationView)_sidebar.Content;
                    var item = (NavigationViewItem)nav.MenuItems[_expectedDepth == 2 ? 1 : 0];
                    nav.SelectedItem = item;
                    // NavigationViewItem does not expose InvokePattern. Exercise
                    // the same callback used by NavigationView.ItemInvoked.
                    _sidebar.InvokeNavigationItem(_renderer, nav.MenuItems.IndexOf(item));
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private bool CheckRadios()
        {
            string[] labels = ["Light", "System", "Dark"];
            var choices = labels.Select(label => _renderer.Elements.Values.Single(e =>
                e.Content is RadioButton && e.Node.Text == label)).ToArray();
            foreach (var e in choices)
            {
                var label = FindLabel(e.Content, e.Node.Text)
                    ?? throw new InvalidOperationException($"Radio label missing: {e.Node.Text}");
                var bounds = label.TransformToVisual(e.Content).TransformBounds(
                    new Windows.Foundation.Rect(0, 0, label.ActualWidth, label.ActualHeight));
                if (label.ActualWidth <= 0 || label.IsTextTrimmed
                    || bounds.X < 0 || bounds.Right > e.Content.ActualWidth + .5)
                    throw new InvalidOperationException($"Radio label clipped: {e.Node.Text}, control={e.Content.ActualWidth}, label={bounds}");
            }
            if (_radios.Count == 0)
                foreach (var e in choices)
                    _radios.Add(e.Node.Text, (RadioButton)e.Content);
            if (choices.Any(e => !ReferenceEquals(_radios[e.Node.Text], e.Content)))
                throw new InvalidOperationException("Radio native identity changed");
            if (_radioExpected != null)
            {
                if (choices.Single(e => e.Node.Text == _radioExpected).Node.Value != 1)
                    return false;
                string group = ((RadioButton)choices[0].Content).GroupName;
                if (group.Length == 0 || choices.Any(e => ((RadioButton)e.Content).GroupName != group)
                    || choices.Count(e => ((RadioButton)e.Content).IsChecked == true) != 1
                    || choices.Any(e => (((RadioButton)e.Content).IsChecked == true) != (e.Node.Value == 1)))
                    throw new InvalidOperationException("Radio native/managed group state disagrees");
                CheckSidebarTheme();
                _radioExpected = null;
            }
            if (_radioStage == labels.Length * 4)
                return true;
            _radioExpected = labels[_radioStage++ % labels.Length];
            var radio = _radios[_radioExpected];
            var peer = new RadioButtonAutomationPeer(radio);
            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
            _deadline.Restart();
            return false;
        }

        private void CheckSidebarTheme()
        {
            var nav = (NavigationView)_sidebar.Content;
            if (nav.ReadLocalValue(Control.ForegroundProperty) != DependencyProperty.UnsetValue)
                throw new InvalidOperationException("Sidebar overrides native theme foreground");
            if (_sidebar.Root.Background is not SolidColorBrush surface
                || surface.Color.A != 255
                || (nav.ActualTheme == ElementTheme.Dark
                    ? surface.Color.R + surface.Color.G + surface.Color.B > 384
                    : surface.Color.R + surface.Color.G + surface.Color.B < 384))
                throw new InvalidOperationException("Sidebar surface disagrees with native theme");
            if (_navigation.Root.RequestedTheme != (ElementTheme)_navigation.Node.FocusRevision)
                throw new InvalidOperationException("Navigation host did not apply its requested theme");
            foreach (var element in _renderer.Elements.Values)
            {
                if (element.Content is not StackPanel header || element.Node.Kind != 13) continue;
                foreach (var child in header.Children)
                {
                    var ink = child switch {
                        TextBlock label => label.Foreground as SolidColorBrush,
                        Control button => button.Foreground as SolidColorBrush,
                        _ => null,
                    };
                    if (ink == null || ink.Color != element.Node.Foreground)
                        throw new InvalidOperationException("Retained header lost its resolved foreground");
                }
            }
            foreach (NavigationViewItem item in nav.MenuItems)
            {
                var label = FindLabel(item, (string)item.Content)
                    ?? throw new InvalidOperationException("Sidebar label missing");
                if (label.ActualWidth <= 0 || label.Foreground is not SolidColorBrush brush)
                    throw new InvalidOperationException("Sidebar label has no visible foreground");
                var color = brush.Color;
                int brightness = color.R + color.G + color.B;
                if (color.A == 0 || (nav.ActualTheme == ElementTheme.Dark ? brightness < 384 : brightness > 384))
                    throw new InvalidOperationException($"Sidebar foreground disagrees with {nav.ActualTheme}: {color}");
                if (item.Icon is FontIcon icon && icon.Foreground is SolidColorBrush iconBrush)
                {
                    var ink = iconBrush.Color;
                    int iconBrightness = ink.R + ink.G + ink.B;
                    if (ink.A == 0 || (nav.ActualTheme == ElementTheme.Dark ? iconBrightness < 384 : iconBrightness > 384))
                        throw new InvalidOperationException("Sidebar icon foreground disagrees with theme");
                }
            }
        }

        private static ScrollViewer? FindScroll(DependencyObject root)
        {
            if (root is ScrollViewer scroll) return scroll;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }

        private static TextBlock? FindLabel(DependencyObject root, string text)
        {
            if (root is TextBlock label && label.Text == text)
                return label;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindLabel(VisualTreeHelper.GetChild(root, i), text) is { } found)
                    return found;
            return null;
        }

        private static Button? FindButton(DependencyObject root, string text)
        {
            if (root is UIElement { Visibility: Visibility.Collapsed })
                return null;
            if (root is Button button && button.Content is string content && content == text)
                return button;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindButton(VisualTreeHelper.GetChild(root, i), text) is { } found)
                    return found;
            return null;
        }

        private void Fail(Exception error)
        {
            Console.Error.WriteLine($"FAIL WidgetSamples navigation: {error}");
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _listRegression?.Dispose();
            _timer.Stop();
            _timer.Tick -= Tick;
            CompositionTarget.Rendering -= Rendering;
        }
    }
}
