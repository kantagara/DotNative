using DotNative;

namespace WidgetSamples.Widgets;

public sealed class ContentSamples : Component
{
    private static readonly int[] Rows = Enumerable.Range(1, 30).ToArray();

    public override Element Build(BuildContext context) =>
        new VStack(
            new Text("Scrolling").FontSize(22),
            new Text("Scroll inside this list. Rows retain their keys.").FontSize(14),
            new ListView<int>(
                Rows,
                row => new Text($"Sample item {row:00}").Padding(12),
                row => row.ToString()
            )
            {
                Height = 240,
                NestedScrolling = NestedScrollMode.Contain,
            }
        ).Spacing(12);
}
