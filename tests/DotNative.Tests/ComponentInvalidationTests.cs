using DotNative;

internal static class ComponentInvalidationTests
{
    public static void Run()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var counter = new Counter();
        int rootBuilds = 0;
        int staticBuilds = 0;
        var show = new State<bool>(true);
        var theme = new State<ThemeData>(ThemeData.Light);
        var unrelatedContext = new State<string>("first");
        var staticBranch = new Builder(_ =>
        {
            staticBuilds++;
            return new VStack(
                Enumerable.Range(0, 10_000).Select(i => (Element)new Text($"static {i}")).ToArray()
            );
        });
        var root = new Builder(_ =>
        {
            rootBuilds++;
            return new ContextScope<string>(
                unrelatedContext.Value,
                new Theme(
                    theme.Value,
                    new VStack(staticBranch, show.Value ? counter : new Text("hidden"))
                )
            );
        });
        using var host = new RenderHost(root, dispatcher, native);
        host.RenderNow();
        Require(host.LastResolvedElements > 10_000);
        counter.Value.Value = 1;
        counter.Value.Value = 2;
        dispatcher.Drain();
        Require(rootBuilds == 1 && staticBuilds == 1 && counter.Builds == 2);
        Require(host.LastResolvedElements < 20 && host.LastDiffedNodes < 10);
        Require(native.Find("counter 2") != 0);

        // A skipped branch must retain callbacks, including freshly built closures.
        Require(host.DispatchClick(native.Find("capture 2")));
        Require(counter.Captured == 2);
        theme.Value = ThemeData.Dark;
        dispatcher.Drain();
        Require(rootBuilds == 2 && staticBuilds == 1 && counter.Builds == 3);
        Require(
            native
                .Nodes[native.Find("static 9999")]
                .Style.AsSpan(12, 4)
                .SequenceEqual(new byte[] { 255, 255, 255, 255 })
        );
        unrelatedContext.Value = "second";
        dispatcher.Drain();
        Require(counter.Builds == 3 && staticBuilds == 1);

        // Failed candidates retain the previous subscriptions and can be retried.
        counter.Fail = true;
        counter.Value.Value = 3;
        try
        {
            dispatcher.Drain();
            throw new Exception("Expected failed build.");
        }
        catch (BuildException) { }
        Require(native.Find("counter 2") != 0);
        counter.Fail = false;
        counter.Value.Value = 4;
        dispatcher.Drain();
        Require(native.Find("counter 4") != 0);

        show.Value = false;
        dispatcher.Drain();
        int builds = counter.Builds;
        int flushes = native.Flushes;
        counter.Value.Value = 5;
        dispatcher.Drain();
        Require(counter.Builds == builds && native.Flushes == flushes);
        show.Value = true;
        dispatcher.Drain();
        Require(counter.Builds == builds + 1 && native.Find("counter 5") != 0);

        int beforeReload = staticBuilds;
        host.RequestRebuild();
        dispatcher.Drain();
        Require(staticBuilds == beforeReload + 1);
        host.Dispose();
        counter.Value.Value = 6;
        dispatcher.Drain();
        RetainedInsertionAndSharedOccurrences();
    }

    private static void RetainedInsertionAndSharedOccurrences()
    {
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        var prepend = new State<bool>(false);
        var value = new State<int>(0);
        int builds = 0;
        var child = new Builder(_ =>
        {
            builds++;
            return new Text($"retained {value.Value}").WithKey("retained");
        });
        using var host = new RenderHost(
            new Factory(() =>
                prepend.Value ? new VStack(new Text("new"), child) : new VStack(child)
            ),
            dispatcher,
            native
        );
        host.RenderNow();
        uint id = native.Find("retained 0");
        prepend.Value = true;
        dispatcher.Drain();
        Require(builds == 1 && native.Find("retained 0") == id);
        value.Value = 1;
        dispatcher.Drain();
        Require(builds == 2 && native.Find("retained 1") == id);

        var shared = new Counter();
        var two = new State<bool>(true);
        var otherNative = new NativeModel();
        using var other = new RenderHost(
            new Factory(() => two.Value ? new VStack(shared, shared) : new VStack(shared)),
            dispatcher,
            otherNative
        );
        other.RenderNow();
        Require(shared.Builds == 2);
        two.Value = false;
        dispatcher.Drain();
        shared.Value.Value = 1;
        dispatcher.Drain();
        Require(shared.Builds == 3 && otherNative.Find("counter 1") != 0);
    }

    private static void Require(bool value)
    {
        if (!value)
            throw new Exception("Component invalidation assertion failed.");
    }

    private sealed class BuildException : Exception { }

    private sealed class Counter : Component
    {
        internal readonly State<int> Value = new(0);
        internal int Builds;
        internal int Captured;
        internal bool Fail;

        public override Element Build(BuildContext context)
        {
            Builds++;
            _ = Theme.Of(context);
            int value = Value.Value;
            if (Fail)
                throw new BuildException();
            return new VStack(
                new Text($"counter {value}"),
                new Button($"capture {value}", () => Captured = value)
            );
        }
    }
}
