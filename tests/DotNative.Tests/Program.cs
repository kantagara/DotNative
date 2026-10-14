using System.Buffers.Binary;
using System.Text;
using DotNative;

if (args is ["--export-font-collections", var directory])
{
    Directory.CreateDirectory(directory);
    var ttc = FontCollectionTests.Fixture("DotNativeTest.ttc");
    File.WriteAllBytes(
        Path.Combine(directory, "DotNativeTest.ttf"),
        FontSource.FromCollectionBytes("DotNativeTest", ttc, 0).Data
    );
    File.WriteAllBytes(
        Path.Combine(directory, "DotNativeTest-Bold.ttf"),
        FontSource.FromCollectionBytes("DotNativeTest", ttc, 1).Data
    );
    File.WriteAllBytes(
        Path.Combine(directory, "DotNativeCff.otf"),
        FontSource
            .FromCollectionBytes(
                "DotNativeCff",
                FontCollectionTests.Fixture("DotNativeTest.otc"),
                0
            )
            .Data
    );
    Console.WriteLine("Exported TTC/OTC faces for native font smoke tests.");
    return;
}

if (args.Contains("--native-smoke"))
{
    // Real C# LibraryImport -> Rust parser -> Taffy. Container-only until Step 4
    // installs native intrinsic measurement for text/buttons.
    var dispatcher = new Dispatcher();
    Node[] children = [new VStack().Padding(8).WithKey("a"), new VStack().Padding(4).WithKey("b")];
    using var host = new RenderHost(
        new Factory(() => new VStack(children).Padding(16)),
        dispatcher,
        new NativeTransport()
    );
    host.SetViewport(390, 844);
    host.RenderNow();
    children = [children[1], children[0]];
    host.RenderNow();
    host.SetViewport(844, 390);
    dispatcher.Drain();
    children = [];
    host.RenderNow();
    host.Dispose();
    Console.WriteLine("PASS real C# -> Rust FFI: mount, reorder, resize, removal, disposal");
    return;
}

var tests = new (string Name, Action Run)[]
{
    ("Golden binary frame, strict UTF-8 and buffer growth", Wire),
    ("Reactive batching, fresh callbacks and disposal", Reactive),
    ("Keyed reorder, insertion, replacement and style reset", Reorder),
    ("Conditional subscriptions and provider integration", Dependencies),
    ("Build failure, duplicate keys and state mutation", BuildFailure),
    ("Thread affinity and development reload hook", Threading),
    ("Uncertain transport failure faults the host", TransportFailure),
    ("Random keyed edits preserve native tree invariants", RandomEdits),
    ("Composition sample builds and updates", Sample),
    ("Scoped context, themes, layout and input callbacks", ContextAndControls),
    ("Mobile text input options, submit events, resets and editor replacement", TextInputTests.Run),
    ("Main startup, DI ownership, scopes and failure cleanup", StartupTests.Run),
    ("Cold and warm OS activations", ActivationTests.Run),
    ("Async startup, UI continuations, cancellation and disposal", AsyncStartupTests.Run),
    ("Automatic service initialization and shutdown ownership", InitializerTests.Run),
    ("Retained navigation screens and scoped DI cleanup", NavigationTests.Run),
    ("DI shared state, conditional readers and UI dispatch", SharedStateTests.Run),
    ("System appearance and explicit theme overrides", SystemAppearanceTests.Run),
    ("Native radio selection, group validation and stable identity", RadioTests.Run),
    ("Platform view type is wired and validated", PlatformViewTests.Run),
    ("Navigation headers, reactive options and native actions", HeaderTests.Run),
    ("Nested scroll policy updates", ScrollPolicyTests.Run),
    (
        "Incremental row extents, fractional boundaries and allocation-free updates",
        RowExtentIndexTests.Run
    ),
    ("Virtual lists bound 10,000 rows, retain anchors and reject stale events", ListViewTests.Run),
    ("Scroll axis and shrink updates, resets and validation", LayoutPolicyTests.Run),
    (
        "Extended styles validate, serialize and reset without replacing nodes",
        ExtendedStyleTests.Run
    ),
    ("Grid, logical edges and gradient wire/reset contracts", AdvancedStyleTests.Run),
    ("Component-local invalidation, context and rollback", ComponentInvalidationTests.Run),
    ("Corner and edge decoration wire, validation and reset", BoxDecorationTests.Run),
    ("Image fitting, tint and z-order preserve identity and reset", ImageAndLayerTests.Run),
    ("Declarative transitions retarget, reset, reduce motion and dispose", TransitionTests.Run),
    ("Dock callbacks, hidden controls, interrupted transitions and reduced motion", DockTests.Run),
    ("Native interaction states cascade, reset and disable actions", InteractionStyleTests.Run),
    ("Rich text run wire, immutability and reset", RichTextTests.Run),
    ("OS accessibility preferences scale, reset and stop motion", AccessibilityTests.Run),
    ("Font assets and variable axes copy, serialize and reset", FontTests.Run),
    ("Font collections extract validated standalone faces", FontCollectionTests.Run),
    ("Lifecycle transitions", LifecycleTests.Run),
    ("Animation retargeting, cancellation, disposal and reactive frames", AnimationTests.Run),
    (
        "Wheel settling preserves subpixels and rejects rounded scroll feedback",
        WheelPickerTests.Run
    ),
    ("FilePicker cancellation races and stream ownership", FilePickerTests.Run),
    ("Application-scoped plugin runtime and stale contexts", PluginRuntimeTests.Run),
    ("Binary plugin codec, requests, errors, cancellation and bounds", PluginTests.Run),
    (
        "Native event subscriptions, disposal, cancelled listen and competing consumers",
        EventChannelTests.Run
    ),
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"{tests.Length} tests passed.");

static void ContextAndControls()
{
    var dispatcher = new Dispatcher();
    var native = new NativeModel();
    var value = new State<string>("hello");
    var focus = new FocusNode();
    var theme = new State<ThemeData>(ThemeData.Dark);
    BuildContext? escaped = null;
    var root = new Factory(() =>
        new Theme(
            theme.Value,
            new Builder(context =>
            {
                escaped = context;
                Check(Theme.Of(context).Equals(theme.Value));
                return new VStack(
                    new Text("inherited").WithKey("inherited"),
                    new Theme(
                        ThemeData.Light,
                        new Builder(inner =>
                            new Text("local").ForegroundColor(Theme.Of(inner).Foreground)
                        )
                    ),
                    new HStack(new SizedBox(20, 10), new Spacer(), new Text("right")).Spacing(8),
                    new TextField(value.Value, text => value.Value = text) { Focus = focus },
                    new Switch(true, _ => { }),
                    new Slider(0.5f, _ => { }),
                    new ScrollView(new VStack(new Text("scroll"))).Height(120)
                );
            })
        )
    );
    using var host = new RenderHost(root, dispatcher, native);
    host.SetViewport(320, 640);
    host.RenderNow();
    Check(
        native
            .Nodes[native.Find("inherited")]
            .Style.AsSpan(12, 4)
            .SequenceEqual(new byte[] { 255, 255, 255, 255 })
    );
    Check(
        native
            .Nodes[native.Find("local")]
            .Style.AsSpan(12, 4)
            .SequenceEqual(new byte[] { 0, 0, 0, 255 })
    );
    Throws<InvalidOperationException>(() => Theme.Of(escaped!));
    uint field = native.Find("hello");
    host.DispatchInput(field, NativeEventKind.TextChanged, 0, "Živeli 🙂");
    dispatcher.Drain();
    Check(native.Find("Živeli 🙂") == field);
    focus.RequestFocus();
    dispatcher.Drain();
    Check(BinaryPrimitives.ReadUInt32LittleEndian(native.Nodes[field].Control.AsSpan(12)) == 1);
    theme.Value = ThemeData.Light;
    dispatcher.Drain();
    Check(
        native
            .Nodes[native.Find("inherited")]
            .Style.AsSpan(12, 4)
            .SequenceEqual(new byte[] { 0, 0, 0, 255 })
    );
    Throws<ArgumentOutOfRangeException>(() => new Slider(2, _ => { }));
}

static void Check(bool value, string message = "Assertion failed")
{
    if (!value)
        throw new Exception(message);
}
static void Throws<T>(Action action)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new Exception($"Expected {typeof(T).Name}");
}
static void Wire()
{
    var b = new CommandBuffer(16);
    b.Create(0x01020304, NodeKind.Text);
    Check(
        Convert.ToHexString(b.WrittenSpan) == "444E5456010010001A0000000100000001050000000403020102"
    );
    b.Reset();
    b.SetText(1, "é\0🙂");
    var bytes = b.WrittenSpan.ToArray();
    Check(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(25)) == 7);
    Check(Convert.ToHexString(bytes.AsSpan(29)) == "C3A900F09F9982");
    Throws<EncoderFallbackException>(() => b.SetText(1, "\uD800"));
    Check(bytes.SequenceEqual(b.WrittenSpan.ToArray()));
    Throws<ArgumentOutOfRangeException>(() => b.Create(0, NodeKind.Text));
    Throws<ArgumentOutOfRangeException>(() => b.SetViewport(float.NaN, 2));
    b.Reset();
    b.SetText(1, new string('x', 10000));
    Check(b.WrittenSpan.Length == 10029);
    Throws<InvalidOperationException>(() =>
        b.SetText(1, new string('x', CommandBuffer.MaximumBytes))
    );
    Check(b.WrittenSpan.Length == 10029);
}
static void Reactive()
{
    var state = new State<int>(0);
    int clicked = -1;
    var root = new Factory(() =>
    {
        int captured = state.Value;
        return new Button("same", () => clicked = captured);
    });
    var dispatcher = new Dispatcher();
    var native = new NativeModel();
    using var host = new RenderHost(root, dispatcher, native);
    host.RenderNow();
    uint id = native.Root;
    int flushes = native.Flushes;
    state.Value = 1;
    state.Value = 2;
    Check(dispatcher.Count == 1);
    dispatcher.Drain();
    Check(native.Flushes == flushes);
    Check(host.DispatchClick(id) && clicked == 2);
    host.Dispose();
    Check(native.Nodes.Count == 0 && native.Root == 0);
    state.Value = 3;
    Check(dispatcher.Count == 0);
}
static void Reorder()
{
    Node[] items = [new Text("a").WithKey("a").Padding(9), new Text("b").WithKey("b")];
    var dispatcher = new Dispatcher();
    var native = new NativeModel();
    using var host = new RenderHost(new Factory(() => new VStack(items)), dispatcher, native);
    host.RenderNow();
    uint a = native.Find("a"),
        b = native.Find("b");
    items = [new Text("b").WithKey("b"), new Text("a").WithKey("a")];
    host.RenderNow();
    Check(native.Find("a") == a && native.Find("b") == b);
    Check(native.Nodes[native.Root].Children.SequenceEqual(new[] { b, a }));
    Check(BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[a].Style) == 0);
    items = [new Button("b", () => { }).WithKey("b"), new Text("c").WithKey("c")];
    host.RenderNow();
    Check(!native.Nodes.ContainsKey(a) && !native.Nodes.ContainsKey(b));
    Check(!host.DispatchClick(b));
    host.RenderNow();
    int count = native.Flushes;
    host.RenderNow();
    Check(native.Flushes == count);
}
static void Dependencies()
{
    var show = new State<bool>(true);
    var a = new State<int>(0);
    var provider = new Provider();
    var dispatcher = new Dispatcher();
    var native = new NativeModel();
    using var host = new RenderHost(
        new Factory(() => new Text($"{(show.Value ? a.Value : provider.Value)}")),
        dispatcher,
        native
    );
    host.RenderNow();
    show.Value = false;
    dispatcher.Drain();
    a.Value = 2;
    Check(dispatcher.Count == 0);
    provider.Set(9);
    Check(dispatcher.Count == 1);
    dispatcher.Drain();
    Check(native.Find("9") != 0);
}
static void BuildFailure()
{
    var state = new State<int>(0);
    int mode = 0;
    var dispatcher = new Dispatcher();
    var native = new NativeModel();
    using var host = new RenderHost(
        new Factory(() =>
        {
            _ = state.Value;
            if (mode == 1)
                return new VStack(new Text("a").WithKey("x"), new Text("b").WithKey("x"));
            if (mode == 2)
                state.Value++;
            if (mode == 3)
                return new Text("bad").Padding(-1);
            if (mode == 4)
                return new Text("\uD800");
            return new Text("good");
        }),
        dispatcher,
        native
    );
    host.RenderNow();
    int flushes = native.Flushes;
    mode = 1;
    Throws<InvalidOperationException>(host.RenderNow);
    mode = 2;
    Throws<InvalidOperationException>(host.RenderNow);
    mode = 3;
    Throws<ArgumentOutOfRangeException>(host.RenderNow);
    mode = 4;
    Throws<EncoderFallbackException>(host.RenderNow);
    Check(native.Flushes == flushes);
    mode = 0;
    state.Value++;
    dispatcher.Drain();
    Check(native.Find("good") != 0);
}
static void Threading()
{
    var state = new State<int>(1);
    var dispatcher = new Dispatcher();
    var native = new NativeModel();
    using var host = new RenderHost(
        new Factory(() => new Text($"{state.Value}")),
        dispatcher,
        native
    );
    host.RenderNow();
    Task.Run(() =>
        {
            Throws<InvalidOperationException>(() => state.Value = 3);
            Throws<InvalidOperationException>(host.RenderNow);
            HotReload.UpdateApplication(null);
        })
        .GetAwaiter()
        .GetResult();
    Check(dispatcher.Count == 1);
    dispatcher.Drain();
}
static void TransportFailure()
{
    var dispatcher = new Dispatcher();
    using var host = new RenderHost(
        new Factory(() => new Text("x")),
        dispatcher,
        new BrokenTransport()
    );
    Throws<IOException>(host.RenderNow);
    try
    {
        host.RenderNow();
        throw new Exception("Faulted transport was reused");
    }
    catch (InvalidOperationException error)
    {
        Check(error.InnerException is IOException { Message: "Injected transport failure" });
    }
}
static void RandomEdits()
{
    var random = new Random(19);
    var entries = new List<(int Key, bool Button)>();
    int next = 0;
    var dispatcher = new Dispatcher();
    var native = new NativeModel();
    using var host = new RenderHost(
        new Factory(() =>
            new VStack(
                entries
                    .Select(x =>
                        (Element)(
                            x.Button
                                ? (Node)new Button($"{x.Key}", () => { }).WithKey($"{x.Key}")
                                : new Text($"{x.Key}").WithKey($"{x.Key}")
                        )
                    )
                    .ToArray()
            )
        ),
        dispatcher,
        native
    );
    var previous = new Dictionary<int, (bool Button, uint Id)>();
    for (int iteration = 0; iteration < 500; ++iteration)
    {
        int op = random.Next(4);
        if (op == 0 || entries.Count == 0)
            entries.Insert(random.Next(entries.Count + 1), (++next, false));
        else if (op == 1)
            entries.RemoveAt(random.Next(entries.Count));
        else if (op == 2)
            entries.Reverse();
        else
        {
            int i = random.Next(entries.Count);
            entries[i] = (entries[i].Key, !entries[i].Button);
        }
        host.RenderNow();
        Check(native.Nodes.Count == entries.Count + 1);
        Check(
            native
                .Nodes[native.Root]
                .Children.Select(id => native.Nodes[id].Text)
                .SequenceEqual(entries.Select(x => $"{x.Key}"))
        );
        foreach (var entry in entries)
            if (previous.TryGetValue(entry.Key, out var old) && old.Button == entry.Button)
                Check(native.Find($"{entry.Key}") == old.Id);
        previous = entries.ToDictionary(x => x.Key, x => (x.Button, native.Find($"{x.Key}")));
    }
}
static void Sample()
{
    var dispatcher = new Dispatcher();
    var native = new NativeModel();
    using var host = new RenderHost(new DotNative.Samples.CounterScreen(), dispatcher, native);
    host.SetViewport(390, 844);
    dispatcher.Drain();
    host.DispatchClick(native.Find("Increment"));
    dispatcher.Drain();
    Check(native.Find("Count: 1") != 0);
}

sealed class Factory(Func<Element> build) : Component
{
    public override Element Build() => build();
}

sealed class Provider : ReactiveSource
{
    private int _value;
    public int Value
    {
        get
        {
            TrackRead();
            return _value;
        }
    }

    public void Set(int value)
    {
        VerifyCanWrite();
        _value = value;
        PublishChange();
    }
}

sealed class Dispatcher : IUiDispatcher
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue = new();

    public bool CheckAccess() => _thread == Environment.CurrentManagedThreadId;

    public int Count => _queue.Count;

    public void Post(Action action) => _queue.Enqueue(action);

    public void Drain()
    {
        while (_queue.TryDequeue(out var action))
            action();
    }
}

sealed class BrokenTransport : ICommandTransport
{
    public void Flush(ReadOnlySpan<byte> commands) =>
        throw new IOException("Injected transport failure");
}

// Independent in-memory receiver validates hierarchy invariants after each batch.
// This is a test oracle, not an implementation of the future Rust/native layer.
sealed class NativeModel : ICommandTransport
{
    internal sealed class NativeNode(byte kind)
    {
        public byte Kind = kind;
        public string Text = "";
        public string ViewType = "";
        public byte[] Style = [];
        public byte[] Control = [];
        public byte[] TextInput = [];
        public byte[] ScrollPosition = [];
        public uint LayoutRevision;
        public bool Virtualized;
        public byte[] Header = [];
        public byte[] BottomNavigation = [];
        public byte[] DesktopNavigation = [];
        public byte ScrollMode;
        public byte ScrollAxis;
        public float FlexShrink;
        public byte[] ExtendedLayout = [];
        public byte[] VisualStyle = [];
        public byte[] Projection = [];
        public byte[] Typography = [];
        public byte[] LayoutBehavior = [];
        public byte[] AdvancedLayout = [];
        public byte[] Gradient = [];
        public int ZIndex;
        public byte ImageFit;
        public byte[] BackgroundImage = [];
        public byte[] Decoration = [];
        public bool Enabled = true;
        public byte[] TextRuns = [];
        public byte[] FontAxes = [];
        public byte[] FontAsset = [];
        public byte[] ImageTint = [];
        public byte[] NavigationTransition = [];
        public float HeaderOpacity = 1;
        public List<uint> Children = [];
    }

    public readonly Dictionary<uint, NativeNode> Nodes = [];
    public readonly Dictionary<uint, byte[]> Fonts = [];
    public uint Root;
    public int Flushes;

    public uint Find(string text) => Nodes.Single(x => x.Value.Text == text).Key;

    private static uint U32(ReadOnlySpan<byte> p) => BinaryPrimitives.ReadUInt32LittleEndian(p);

    private static void Require(bool condition)
    {
        if (!condition)
            throw new Exception("Invalid wire/tree");
    }

    public void Flush(ReadOnlySpan<byte> commands)
    {
        Require(commands[..4].SequenceEqual("DNTV"u8));
        Require(BinaryPrimitives.ReadUInt16LittleEndian(commands[4..]) == 1);
        Require(BinaryPrimitives.ReadUInt16LittleEndian(commands[6..]) == 16);
        Require(U32(commands[8..]) == commands.Length);
        uint count = U32(commands[12..]);
        int cursor = 16;
        for (uint i = 0; i < count; ++i)
        {
            byte op = commands[cursor];
            int length = checked((int)U32(commands[(cursor + 1)..]));
            var p = commands.Slice(cursor + 5, length);
            cursor += 5 + length;
            uint id = U32(p);
            switch (op)
            {
                case 1:
                    Require(length == 5 && id != 0);
                    Nodes.Add(id, new(p[4]));
                    break;
                case 2:
                    Require(length == 8 + U32(p[4..]));
                    Nodes[id].Text = new UTF8Encoding(false, true).GetString(p[8..]);
                    break;
                case 3:
                    Require(length == 24);
                    Nodes[id].Style = p[4..].ToArray();
                    break;
                case 4:
                    Require(
                        (Nodes[id].Kind is 1 or 4 or 10 or 11 or 15)
                            && length == 8 + U32(p[4..]) * 4
                    );
                    Nodes[id].Children.Clear();
                    for (int c = 8; c < length; c += 4)
                    {
                        uint child = U32(p[c..]);
                        Require(Nodes.ContainsKey(child));
                        Nodes[id].Children.Add(child);
                    }
                    break;
                case 5:
                    Require(length == 4 && (id == 0 || Nodes.ContainsKey(id)));
                    Root = id;
                    break;
                case 6:
                    Require(length == 4 && id != Root && Nodes[id].Children.Count == 0);
                    Require(Nodes.Remove(id));
                    foreach (var parent in Nodes.Values)
                        parent.Children.Remove(id);
                    break;
                case 7:
                    Require(length == 8);
                    break;
                case 8:
                    Require(length == 24);
                    break;
                case 9:
                    Require(length >= 28);
                    Nodes[id].Control = p[4..].ToArray();
                    break;
                case 10:
                    Require(length >= 8);
                    break;
                case 14:
                    Require(length == 8);
                    Nodes[id].FlexShrink = BinaryPrimitives.ReadSingleLittleEndian(p[4..]);
                    Require(float.IsFinite(Nodes[id].FlexShrink) && Nodes[id].FlexShrink >= 0);
                    break;
                case 29:
                    Require(length == 5 + p[4] * 8);
                    Nodes[id].FontAxes = p[4..].ToArray();
                    break;
                case 30:
                    Require(length >= 12);
                    Nodes[id].FontAsset = p[4..].ToArray();
                    break;
                case 38:
                    Require(length == 68);
                    for (var element = 4; element < length; element += 4)
                        Require(
                            float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(p[element..]))
                        );
                    Nodes[id].Projection = p[4..].ToArray();
                    break;
                case 37:
                    Nodes[id].Virtualized = true;
                    break;
                case 36:
                    Nodes[id].LayoutRevision = BinaryPrimitives.ReadUInt32LittleEndian(p[4..]);
                    break;
                case 35:
                    Require(length == 13 && Nodes[id].Kind == 4);
                    Nodes[id].ScrollPosition = p[4..].ToArray();
                    break;
                case 34:
                    Require((length == 9 || length == 13) && Nodes[id].Kind == 5);
                    Nodes[id].TextInput = p[4..].ToArray();
                    break;
                case 33:
                    Nodes[id].DesktopNavigation = p[4..].ToArray();
                    break;
                case 32:
                    Require(length >= 12 && Nodes[id].Kind == 14);
                    Nodes[id].BottomNavigation = p[4..].ToArray();
                    break;
                case 31:
                    Require(length == 10 && Nodes[id].Kind == 10 && p[4] <= 3 && p[9] <= 3);
                    Nodes[id].NavigationTransition = p[4..].ToArray();
                    break;
                case 28:
                    Require(length >= 8);
                    Nodes[id].TextRuns = p[4..].ToArray();
                    break;
                case 27:
                    Require(length == 5 && p[4] <= 1);
                    Nodes[id].Enabled = p[4] != 0;
                    break;
                case 26:
                    Require(length == 57);
                    Nodes[id].Decoration = p[4..].ToArray();
                    break;
                case 25:
                    Require(length >= 9);
                    Nodes[id].BackgroundImage = p[4..].ToArray();
                    break;
                case 24:
                    Require(length == 9);
                    Nodes[id].ImageTint = p[4..].ToArray();
                    break;
                case 23:
                    Require(length == 5);
                    Nodes[id].ImageFit = p[4];
                    break;
                case 22:
                    Require(length == 8);
                    Nodes[id].ZIndex = BinaryPrimitives.ReadInt32LittleEndian(p[4..]);
                    break;
                case 21:
                    Nodes[id].Gradient = p[4..].ToArray();
                    Require(length >= 22);
                    break;
                case 20:
                    Nodes[id].AdvancedLayout = p[4..].ToArray();
                    Require(length >= 56);
                    break;
                case 19:
                    Require(length == 36);
                    Nodes[id].LayoutBehavior = p[4..].ToArray();
                    break;
                case 18:
                    Require(length >= 26);
                    Nodes[id].Typography = p[4..].ToArray();
                    break;
                case 17:
                    Require(length == 53);
                    Nodes[id].VisualStyle = p[4..].ToArray();
                    break;
                case 16:
                    Require(length == 58);
                    Nodes[id].ExtendedLayout = p[4..].ToArray();
                    break;
                case 15:
                    Require(length == 5 && Nodes[id].Kind == 4 && p[4] <= 1);
                    Nodes[id].ScrollAxis = p[4];
                    break;
                case 13:
                    Require(length == 8);
                    Nodes[id].HeaderOpacity = BinaryPrimitives.ReadSingleLittleEndian(p[4..]);
                    break;
                case 12:
                    Require(length == 5 && Nodes[id].Kind == 4 && p[4] <= 1);
                    Nodes[id].ScrollMode = p[4];
                    break;
                case 11:
                    Require(length >= 16);
                    Nodes[id].Header = p[4..].ToArray();
                    break;
                case 40:
                    Require(length == 8 && Nodes.ContainsKey(id) && Fonts.ContainsKey(U32(p[4..])));
                    Nodes[id].FontAsset = Fonts[U32(p[4..])];
                    break;
                case 39:
                    Require(length >= 12 && id != 0 && !Fonts.ContainsKey(id));
                    var familySize = checked((int)U32(p[4..]));
                    Require(familySize > 0 && familySize <= 4096 && familySize <= length - 12);
                    var dataSize = checked((int)U32(p[(8 + familySize)..]));
                    Require(
                        dataSize > 0
                            && dataSize <= 8 * 1024 * 1024
                            && length == 12 + familySize + dataSize
                    );
                    Fonts.Add(id, p[4..].ToArray());
                    break;
                case 41:
                    Require(length == 8 + (int)U32(p[4..]) && Nodes[id].Kind == 16);
                    Nodes[id].ViewType = new UTF8Encoding(false, true).GetString(p[8..]);
                    break;
                default:
                    throw new Exception("Unknown opcode");
            }
        }
        Require(cursor == commands.Length);
        var seen = new HashSet<uint>();
        void Visit(uint id)
        {
            Require(seen.Add(id));
            foreach (uint child in Nodes[id].Children)
                Visit(child);
        }
        if (Root != 0)
            Visit(Root);
        Require(seen.Count == Nodes.Count);
        ++Flushes;
    }
}
