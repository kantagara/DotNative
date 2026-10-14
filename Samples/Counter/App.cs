using DotNative;

namespace Counter;

public sealed class App : Component
{
    private readonly State<int> _count = new(0);
#if DOTNATIVE_BINDINGS
    private readonly string _platform = ReadPlatform();

    private static string ReadPlatform()
    {
        var value = DotNative.BindingsProbe.PlatformProbe.Read();
        Console.WriteLine($"DOTNATIVE_BINDINGS_PASS: {value}");
        return value;
    }
#endif

    public override Element Build() =>
        new VStack(
            new Text("Hello, DotNative!").FontSize(32).WithKey("title"),
#if DOTNATIVE_BINDINGS
            new Text(_platform).FontSize(16).WithKey("platform-plugin"),
#endif
            new Text($"Count: {_count.Value}").FontSize(28).Padding(16).WithKey("count"),
            new PrimaryButton("Increment", () => _count.Value++, "increment"),
            new VStack().Padding(20),
            new PrimaryButton("Reset", () => _count.Value = 0, "reset"),
            new Text("C# state · Rust layout · Native controls").FontSize(16).Padding(16)
        )
            .Padding(24)
            .BackgroundColor(Color.White);
}
