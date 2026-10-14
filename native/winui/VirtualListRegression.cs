using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DotNative.WinUI;

public static unsafe partial class WinUIBridge
{
    private sealed class VirtualListRegression : IDisposable
    {
        private readonly Renderer _renderer;
        private readonly Element _navigation;
        private readonly Action _complete;
        private readonly DispatcherTimer _timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        private Element? _scroll;
        private int _ticks,
            _frames;
        private bool _disposed;
        private string? _expectedSelection;

        internal VirtualListRegression(
            Renderer renderer,
            Element navigation,
            Button open,
            Action complete
        )
        {
            _renderer = renderer;
            _navigation = navigation;
            _complete = complete;
            var peer = new ButtonAutomationPeer(open);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            _timer.Tick += Tick;
            CompositionTarget.Rendering += Rendering;
            _timer.Start();
        }

        private IEnumerable<Element> Descendants(uint id)
        {
            var element = _renderer.Elements[id];
            yield return element;
            foreach (var child in element.Node.Children)
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }

        private void Tick(object? sender, object args)
        {
            try
            {
                if (
                    _renderer.Elements.Values.Any(e =>
                        e.NavigationPending || e.NavigationAnimation != null
                    )
                )
                    return;
                if (_navigation.PresentedDepth <= 1)
                    return;
                _scroll ??= Descendants(_navigation.PresentedPage)
                    .SingleOrDefault(e => e.Content is ScrollViewer && e.Node.Virtualized);
                if (_scroll == null)
                    return;
                var scroll = (ScrollViewer)_scroll.Content;
                if (++_ticks < 20)
                    return; // Let automatic row measurements settle.
                if (_ticks < 180)
                {
                    double delta = _ticks < 100 ? 120 : -120;
                    scroll.ChangeView(
                        null,
                        Math.Clamp(scroll.VerticalOffset + delta, 0, scroll.ScrollableHeight),
                        null,
                        true
                    );
                }
                else if (_ticks == 195)
                {
                    if (_frames < 30)
                        throw new InvalidOperationException(
                            "Fast-scroll coverage was not exercised"
                        );
                    if (
                        _renderer.ReusedRowTrees == 0
                        || _renderer.ReusedLeafControls == 0
                        || _renderer.PooledLeafCount > 64
                    )
                        throw new InvalidOperationException(
                            "Leaf recycling was not exercised or exceeded its bound"
                        );
                    var button = Descendants(_scroll.Node.Id)
                        .First(e =>
                            e.Recycled && e.Content is Button && e.Node.Text.StartsWith("Item ")
                        );
                    int item = int.Parse(button.Node.Text.AsSpan(5, 5));
                    _expectedSelection = $"Selected item {item}";
                    var peer = new ButtonAutomationPeer((Button)button.Content);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                }
                else if (_ticks > 195)
                {
                    if (
                        Descendants(_navigation.PresentedPage)
                            .Any(e => e.Node.Text == _expectedSelection)
                    )
                    {
                        Console.WriteLine(
                            $"PASS virtual list fast scroll/reversal: {_frames} covered frames, height={scroll.ViewportHeight:0}, bounded rows; {_renderer.ReusedRowTrees} complete row reuses, {_renderer.ReusedLeafControls} leaf reuses, recycled callback selects correct item"
                        );
                        Dispose();
                        _complete();
                    }
                    else if (_ticks > 300)
                        throw new InvalidOperationException(
                            "Recycled button callback targeted a stale item"
                        );
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private void Rendering(object? sender, object args)
        {
            if (_disposed || _scroll == null || _ticks < 20)
                return;
            try
            {
                var scroll = (ScrollViewer)_scroll.Content;
                var content = _renderer.Elements[_scroll.Node.Children.Single()];
                var rows = content
                    .Node.Children.Select(id => _renderer.Elements[id])
                    .Where(e => e.Node.Children.Length > 0)
                    .ToArray();
                if (rows.Length == 0 || rows.Length > 180)
                    throw new InvalidOperationException(
                        $"Unbounded/empty list window: {rows.Length}"
                    );
                double top = rows.Min(e => e.Node.Y),
                    bottom = rows.Max(e => e.Node.Y + e.Node.Height);
                if (
                    top > scroll.VerticalOffset + 1
                    || bottom + 1 < scroll.VerticalOffset + scroll.ViewportHeight
                )
                    throw new InvalidOperationException(
                        $"Blank list viewport: rows={top:0}..{bottom:0}, viewport={scroll.VerticalOffset:0}..{scroll.VerticalOffset + scroll.ViewportHeight:0}"
                    );
                double covered = scroll.VerticalOffset;
                foreach (var row in rows.OrderBy(e => e.Node.Y))
                {
                    if (row.Node.Y + row.Node.Height <= covered)
                        continue;
                    if (row.Node.Y > covered + 1)
                        throw new InvalidOperationException(
                            $"Gap inside list viewport: {covered:0}..{row.Node.Y:0}"
                        );
                    covered = Math.Max(covered, row.Node.Y + row.Node.Height);
                    if (covered >= scroll.VerticalOffset + scroll.ViewportHeight - 1)
                        break;
                }
                _frames++;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private void Fail(Exception error)
        {
            Console.Error.WriteLine($"FAIL virtual list rapid scrolling: {error}");
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= Tick;
            CompositionTarget.Rendering -= Rendering;
        }
    }
}
