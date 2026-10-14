using System.Buffers.Binary;
using System.Globalization;
using DotNative;

internal static class ListViewTests
{
    private static void VirtualizedByDefault()
    {
        var values = Enumerable.Range(0, 10_000).ToArray();
        var list = new ListView<int>(values, x => new Text(x.ToString()), x => x.ToString());
        Check(list.Height == 200, "Keep the existing ListView viewport default");
        var native = new NativeModel();
        var dispatcher = new Dispatcher();
        using var host = new RenderHost(list, dispatcher, native);
        host.SetViewport(300, 400);
        host.RenderNow();
        Check(
            native.Nodes.Values.Single(n => n.Kind == 4).Virtualized,
            "ListView must select native virtualization by default"
        );
        Check(native.Nodes.Count < 100, "Default list must not realize 10,000 rows");
        list.SetItems([]);
        dispatcher.Drain();
        Check(!native.Nodes.Values.Any(n => n.Kind == 2), "Clear releases row contents");
    }

    public static void Run()
    {
        StaleScrollCallbackRejected();
        VirtualizedByDefault();
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        int built = 0,
            clicked = -1;
        var items = Enumerable.Range(0, 10_000).ToArray();
        var list = new ListView<int>(
            items,
            value =>
            {
                built++;
                return new Button($"Item {value}", () => clicked = value);
            },
            value => value.ToString(CultureInfo.InvariantCulture),
            40
        )
        {
            CacheExtent = 0,
            Height = 400,
            Overscan = 4,
        };
        using var host = new RenderHost(list, dispatcher, native);
        host.SetViewport(300, 400);
        host.RenderNow();
        var scrollId = native.Nodes.Single(x => x.Value.Kind == 4).Key;
        Check(
            native.Nodes[scrollId].Virtualized,
            "VirtualList must opt into the native collection backend"
        );
        Check(built <= 20 && native.Nodes.Count < 50, "10,000 rows must not become 10,000 views");
        var obsoleteButton = native.Find("Item 0");
        Scroll(0, 400, 0);
        dispatcher.Drain();
        Scroll(20_000, 400, 0);
        dispatcher.Drain();
        Check(native.Nodes.Count < 50 && built < 50, "Window size must remain bounded");
        Check(!native.Nodes.ContainsKey(obsoleteButton), "Offscreen views must be released");
        host.DispatchClick(obsoleteButton);
        Check(clicked == -1, "Obsolete row callback must not run");
        var retained = native.Find("Item 500");
        host.DispatchClick(retained);
        Check(clicked == 500, "Visible callback must address correct item");
        int before = built;
        Scroll(20_001, 400, 0);
        dispatcher.Drain();
        Check(
            native.Find("Item 500") == retained && built == before,
            "Pixel scrolling within the same window must retain rows"
        );
        Check(
            host.LastResolvedElements <= 6 && host.LastDiffedNodes <= 6,
            "Pixel scrolling must visit only the window/controller path, not every mounted row"
        );
        list.SetItems(new[] { -1 }.Concat(items).ToArray());
        dispatcher.Drain();
        Check(
            list.Controller.Offset == 20_041 && native.Find("Item 500") == retained,
            "Prepending must preserve visible key and intra-row offset"
        );
        var revision = BinaryPrimitives.ReadUInt32LittleEndian(
            native.Nodes[scrollId].ScrollPosition
        );
        Check(revision > 0, "Anchor adjustment must issue a native scroll request");
        Scroll(0, 400, 0);
        dispatcher.Drain();
        Check(
            list.Controller.Offset == 20_041,
            "Old native events must not undo a programmatic scroll"
        );
        list.SetItems(items.Reverse().ToArray());
        dispatcher.Drain();
        Check(
            list.Controller.Offset == 9499 * 40 + 1 && native.Find("Item 500") == retained,
            "Reordering must preserve the anchor"
        );
        list.ScrollToIndex(9999);
        dispatcher.Drain();
        Check(
            list.Controller.Offset == 399_600 && native.Nodes.Values.Any(n => n.Text == "Item 0"),
            "Jump-to-end must realize the final row"
        );
        list.SetItems([]);
        dispatcher.Drain();
        Check(
            native.Nodes.Count == 4 && list.Controller.Offset == 0,
            "Empty lists must release all row views"
        );
        var bad = new CommandBuffer();
        try
        {
            bad.SetScrollPosition(1, (1, float.NaN, true));
            throw new Exception("Invalid offset accepted");
        }
        catch (ArgumentOutOfRangeException)
        {
            Check(bad.IsEmpty, "Invalid offset wrote a partial command");
        }
        try
        {
            list.SetItems([1, 1]);
            throw new Exception("Duplicate keys accepted");
        }
        catch (ArgumentException)
        {
            Check(native.Nodes.Count == 4, "Invalid data modified the list");
        }
        ThemeChangesAcrossCachedAndRecycledRows();
        CachedWindowCoversFastScroll();
        AutomaticMeasurements();
        InitiallyTallRowStartsAtTop();
        UnchangedRowsSurviveSnapshots();
        VariableExtents();
        SharedControllerRejected();
        BackendChangesReplaceNativeNodes();
        OffscreenSubscriptionsAreReleased();
        void Scroll(float offset, float viewport, uint sequence) =>
            host.DispatchInput(
                scrollId,
                NativeEventKind.ScrollChanged,
                offset,
                $"{sequence}:{viewport.ToString(CultureInfo.InvariantCulture)}"
            );
    }

    private static void ThemeChangesAcrossCachedAndRecycledRows()
    {
        var theme = new State<ThemeData>(ThemeData.Light);
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var list = new ListView<int>(
            Enumerable.Range(0, 10_000).ToArray(),
            value => new Text($"Themed {value}"),
            value => value.ToString(),
            40
        )
        {
            Height = 320,
        };
        using var host = new RenderHost(
            new Factory(() => new Theme(theme.Value, list)),
            dispatcher,
            native
        );
        host.SetViewport(390, 844);
        host.RenderNow();
        var scroll = native.Nodes.Single(pair => pair.Value.Kind == 4).Key;
        for (var cycle = 0; cycle < 20; cycle++)
        {
            var dark = cycle % 2 == 0;
            theme.Value = dark ? ThemeData.Dark : ThemeData.Light;
            // Several native offsets arrive before the managed frame; only the latest window matters.
            var offset =
                cycle % 3 == 0 ? 0
                : cycle % 3 == 1 ? 200_000
                : 80_000;
            host.DispatchInput(scroll, NativeEventKind.ScrollChanged, offset / 2, "0:320");
            host.DispatchInput(scroll, NativeEventKind.ScrollChanged, offset, "0:320");
            dispatcher.Drain();
            var visible = native
                .Nodes.Values.Where(node => node.Text.StartsWith("Themed "))
                .ToArray();
            Check(
                visible.Length > 0 && native.Nodes.Count < 100,
                "Theme/scroll changes left an empty or unbounded list."
            );
            Check(
                visible.Any(node => node.Text == $"Themed {offset / 40}"),
                "Latest scroll target is missing."
            );
            var color = dark ? new byte[] { 255, 255, 255, 255 } : new byte[] { 0, 0, 0, 255 };
            Check(
                visible.All(node => node.Style.AsSpan(12, 4).SequenceEqual(color)),
                "Cached/recycled rows retained an obsolete theme foreground."
            );
        }
    }

    private static void CachedWindowCoversFastScroll()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var list = new ListView<int>(
            Enumerable.Range(0, 10_000).ToArray(),
            value => new Text($"Cached {value}"),
            value => value.ToString(),
            40
        )
        {
            Height = 400,
            Overscan = 0,
            CacheExtent = 800,
            FillAvailableHeight = true,
        };
        using var host = new RenderHost(list, dispatcher, native);
        host.SetViewport(300, 400);
        host.RenderNow();
        var scroll = native.Nodes.Single(n => n.Value.Kind == 4).Key;
        foreach (float offset in new[] { 4000f, 8000f, 12000f, 20000f })
            host.DispatchInput(scroll, NativeEventKind.ScrollChanged, offset, "0:400");
        dispatcher.Drain();
        Check(
            native.Nodes.Values.Any(n => n.Text == "Cached 480")
                && native.Nodes.Values.Any(n => n.Text == "Cached 530"),
            "Fast scrolling must prepare two viewports ahead and behind the latest offset"
        );
        Check(
            native.Nodes.Count < 120,
            "Cache must remain bounded independently of 10,000-item count"
        );
        host.DispatchInput(scroll, NativeEventKind.ScrollChanged, 16000, "0:600");
        dispatcher.Drain();
        Check(
            native.Nodes.Values.Any(n => n.Text == "Cached 380")
                && native.Nodes.Values.Any(n => n.Text == "Cached 435"),
            "Direction reversal and viewport resize must prepare the new visible range plus cache"
        );
    }

    private sealed record Item(int Id, string Title);

    private static void UnchangedRowsSurviveSnapshots()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var items = Enumerable.Range(0, 100).Select(i => new Item(i, $"Content {i}")).ToArray();
        int built = 0;
        string clicked = "";
        var list = new ListView<Item>(
            items,
            item =>
            {
                built++;
                return new Button(item.Title, () => clicked = item.Title);
            },
            item => item.Id.ToString()
        );
        using var host = new RenderHost(list, dispatcher, native);
        host.SetViewport(300, 400);
        host.RenderNow();
        int baseline = built;
        uint id = native.Find("Content 0");
        list.SetItems(items.Select(item => item with { }).ToArray());
        dispatcher.Drain();
        Check(
            built == baseline,
            "An equal snapshot must not rebuild unchanged visible row content"
        );
        items[0] = new Item(0, "Updated content");
        list.SetItems(items);
        dispatcher.Drain();
        Check(
            built == baseline + 1 && native.Find("Updated content") == id,
            "Content diff must rebuild only the changed row and retain its native identity"
        );
        host.DispatchClick(id);
        Check(
            clicked == "Updated content",
            "Retained native identity must dispatch to current content"
        );
    }

    private static void InitiallyTallRowStartsAtTop()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var list = new ListView<int>([1], x => new Text("Tall"), x => x.ToString())
        {
            CacheExtent = 0,
            Height = 200,
        };
        using var host = new RenderHost(list, dispatcher, native);
        host.RenderNow();
        var observed = native.Nodes.Single(n => n.Value.LayoutRevision != 0);
        host.DispatchInput(
            observed.Key,
            NativeEventKind.LayoutMeasured,
            1000,
            $"{observed.Value.LayoutRevision}:300"
        );
        dispatcher.Drain();
        Check(
            list.Controller.Offset == 0,
            "First measurement must not move a tall initial row to its bottom"
        );
    }

    private static void AutomaticMeasurements()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var items = Enumerable.Range(0, 1000).ToArray();
        int built = 0;
        var list = new ListView<int>(
            items,
            x =>
            {
                built++;
                return new Text($"Auto {x}");
            },
            x => x.ToString()
        )
        {
            CacheExtent = 0,
            Height = 200,
        };
        using var host = new RenderHost(list, dispatcher, native);
        host.SetViewport(300, 200);
        host.RenderNow();
        uint Wrapper(int index) =>
            native.Nodes.Single(n => n.Value.Children.Contains(native.Find($"Auto {index}"))).Key;
        void Measure(int index, float height, float width = 300, uint? revision = null)
        {
            var id = Wrapper(index);
            host.DispatchInput(
                id,
                NativeEventKind.LayoutMeasured,
                height,
                $"{revision ?? native.Nodes[id].LayoutRevision}:{width}"
            );
            dispatcher.Drain();
        }
        list.ScrollToIndex(5);
        dispatcher.Drain();
        var retained = native.Find("Auto 5");
        var builders = built;
        Measure(4, 100);
        Check(
            list.Controller.Offset == 324 && native.Find("Auto 5") == retained && built == builders,
            "Measuring a preceding row must preserve anchor and row components"
        );
        Measure(4, 20);
        Check(list.Controller.Offset == 244, "A preceding row shrinking must adjust the anchor");
        Measure(5, 180);
        Check(list.Controller.Offset == 244, "Anchor row growth must retain its top position");
        Measure(4, 40, 240);
        Check(
            list.Controller.Offset == 264,
            "Width changes must invalidate cached heights and retain the anchor"
        );
        var stale = native.Nodes[Wrapper(4)].LayoutRevision;
        list.SetItems(items);
        dispatcher.Drain();
        Measure(4, 300, 240, stale);
        Check(
            list.Controller.Offset == 264,
            "Measurements from an earlier item snapshot must be ignored"
        );
        list.SetItems(new[] { -1 }.Concat(items).ToArray());
        dispatcher.Drain();
        Check(
            list.Controller.Offset == 320,
            "Prepend must retain measured heights for unchanged keys"
        );
        list.ScrollToEnd();
        dispatcher.Drain();
        var before = list.Controller.Offset;
        Measure(999, 120, 240);
        Check(
            list.Controller.Offset == before + 64,
            "Final row measurement must keep a requested end visible"
        );
        Check(native.Nodes.Count < 50, "Automatic heights must retain a bounded window");
    }

    private static void VariableExtents()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var list = new ListView<int>(
            Enumerable.Range(0, 1000).ToArray(),
            x => new Text(x.ToString()),
            x => x.ToString(),
            x => x % 2 == 0 ? 20 : 60
        )
        {
            CacheExtent = 0,
            Height = 200,
            Overscan = 2,
            KeepEndVisible = true,
        };
        using var host = new RenderHost(list, dispatcher, native);
        host.SetViewport(300, 200);
        host.RenderNow();
        list.ScrollToIndex(101);
        dispatcher.Drain();
        Check(list.Controller.Offset == 4020, "Variable extents must use prefix sums");
        list.ScrollToEnd();
        dispatcher.Drain();
        list.SetItems(Enumerable.Range(0, 1001).ToArray());
        dispatcher.Drain();
        Check(list.Controller.Offset == 39820, "Appending while at end must keep the end visible");
        list.ScrollToIndex(100);
        dispatcher.Drain();
        list.SetItems(Enumerable.Range(0, 1002).ToArray());
        dispatcher.Drain();
        Check(
            list.Controller.Offset == 4000,
            "Appending while reading earlier rows must not jump to end"
        );
    }

    private static void OffscreenSubscriptionsAreReleased()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var rowState = new State<int>(0);
        var list = new ListView<int>(
            Enumerable.Range(0, 1000).ToArray(),
            x => new Text(x == 0 ? $"Live {rowState.Value}" : $"Row {x}"),
            x => x.ToString(),
            40
        );
        using var host = new RenderHost(list, dispatcher, native);
        host.RenderNow();
        rowState.Value = 1;
        dispatcher.Drain();
        Check(
            native.Nodes.Values.Any(n => n.Text == "Live 1"),
            "Visible row state must still rebuild independently"
        );
        list.ScrollToIndex(500);
        dispatcher.Drain();
        var flushes = native.Flushes;
        rowState.Value = 2;
        dispatcher.Drain();
        Check(native.Flushes == flushes, "Offscreen row subscriptions must be released");
    }

    private static void BackendChangesReplaceNativeNodes()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var virtualized = new State<bool>(false);
        using var host = new RenderHost(
            new Factory(() =>
                new ScrollView(new VStack(new Text("Row"))) { Virtualized = virtualized.Value }
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        var plain = native.Root;
        virtualized.Value = true;
        dispatcher.Drain();
        var collection = native.Root;
        Check(
            collection != plain
                && native.Nodes[collection].Virtualized
                && !native.Nodes.ContainsKey(plain),
            "Changing to a native collection must replace the underlying native class"
        );
        virtualized.Value = false;
        dispatcher.Drain();
        Check(
            native.Root != collection && !native.Nodes[native.Root].Virtualized,
            "Changing back to plain scrolling must replace the collection"
        );
    }

    private static void StaleScrollCallbackRejected()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var controller = new ScrollController();
        var callbacks = 0;
        using var host = new RenderHost(
            new Factory(() =>
                new ScrollView(new Container().Height(1000))
                {
                    Controller = controller,
                    OnScrolled = _ =>
                    {
                        callbacks++;
                        if (callbacks == 1)
                            controller.JumpTo(100);
                    },
                }
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        var id = native.Root;
        host.DispatchInput(id, NativeEventKind.ScrollChanged, 20, "0:100");
        // The mounted tree still has revision zero until the queued render runs.
        host.DispatchInput(id, NativeEventKind.ScrollChanged, 30, "0:100");
        Check(
            callbacks == 1 && controller.Offset == 100,
            "A controller reset must reject stale callbacks before the next render"
        );
        dispatcher.Drain();
        host.DispatchInput(id, NativeEventKind.ScrollChanged, 100, "1:100");
        Check(callbacks == 2, "Reports for the current revision must still reach the callback");
    }

    private static void SharedControllerRejected()
    {
        var controller = new ScrollController();
        using var host = new RenderHost(
            new Factory(() =>
                new VStack(
                    new ScrollView(new Text("a")) { Controller = controller },
                    new ScrollView(new Text("b")) { Controller = controller }
                )
            ),
            new Dispatcher(),
            new NativeModel()
        );
        try
        {
            host.RenderNow();
            throw new Exception("Shared scroll controller accepted");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("only one")) { }
    }

    private static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }
}
