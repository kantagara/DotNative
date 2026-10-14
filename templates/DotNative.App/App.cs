using DotNative;

namespace AppName;

public sealed class App : Component
{
    private readonly State<int> _count = new(0);

    public override Element Build() =>
        new VStack(
            new Text("Hello, DotNative!").FontSize(32).WithKey("title"),
            new Text($"Count: {_count.Value}").FontSize(28).Padding(16).WithKey("count"),
            new PrimaryButton("Increment", () => _count.Value++, "increment"),
            new PrimaryButton("Reset", () => _count.Value = 0, "reset"),
            new Text("C# state · Rust layout · Native controls").FontSize(16).Padding(16)
        )
            .Padding(24)
            .BackgroundColor(Color.White);
}
