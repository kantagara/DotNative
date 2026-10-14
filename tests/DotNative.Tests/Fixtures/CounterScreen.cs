using DotNative;

namespace DotNative.Samples;

public sealed class CounterScreen : Component
{
    private readonly State<int> _count = new(0);

    public override Element Build() =>
        new VStack(
            new Text($"Count: {_count.Value}").FontSize(28).WithKey("count"),
            new PrimaryButton("Increment", () => _count.Value++, "increment")
        )
            .Padding(24)
            .BackgroundColor(Color.White);
}
