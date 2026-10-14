using System.Text;
using DotNative;
using DotNative.Svg;

namespace FaliNamIgrac;

public sealed partial class HomeScreen
{
    private Element Bar(
        int index,
        Sheet open,
        (float W, float H, float R) scale,
        float height,
        bool dark
    )
    {
        var r = scale.R;
        var width = presentation.Dimensions.Value?.Width ?? 375;
        var content = new HStack(
            TabSlot(0, TabIcon("svg/home.svg", 24 * r, 1), index, r),
            TabSlot(1, TabIcon("svg/games.svg", 24 * r, 24f / 27f), index, r),
            Gap(56 * r),
            TabSlot(2, TabIcon("svg/chat.svg", 24 * r, 1), index, r),
            TabSlot(
                3,
                new VStack(
                    Avatar(
                        account.State.Saved?.Photo,
                        account.State.Saved?.Firstname ?? profileName.Value ?? "",
                        28 * r
                    )
                ),
                index,
                r
            )
        )
            .Height(height)
            .Padding(8 * r, 0, 8 * r, Insets.Bottom)
            .CrossAlignment(CrossAxisAlignment.Center)
            .ClipToBounds(false);
        return new VStack([
            new Image(NavigationShape(width, height, r, dark, index < 2))
                .Fit(ImageFit.Fill)
                .Width(width)
                .Height(height)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0),
            content,
            .. index < 2
                ? new Element[]
                {
                    CreateButton(open, r)
                        .Position(PositionMode.Absolute)
                        .Left((width - 56 * r) / 2)
                        .Top(-28 * r),
                }
                : [],
        ])
            .Width(width)
            .Height(height)
            .MinHeight(height)
            .Shrink(0)
            .ClipToBounds(false);
    }

    private (float Width, float Height, float Scale, bool Dark, bool Notch)? navigationShapeKey;
    private ImageSource? navigationShape;

    private ImageSource NavigationShape(float width, float height, float r, bool dark, bool notch)
    {
        var key = (width, height, r, dark, notch);
        if (navigationShapeKey == key && navigationShape is not null)
            return navigationShape;
        var center = width / 2;
        var corner = 32 * r;
        var top = notch
            ? FormattableString.Invariant(
                $"H {center - 52 * r} C {center - 36 * r} 0 {center - 38 * r} {34 * r} {center} {34 * r} C {center + 38 * r} {34 * r} {center + 36 * r} 0 {center + 52 * r} 0"
            )
            : "";
        var fill = dark ? "#262626" : "#ffffff";
        var stroke = dark ? "#4a4a4a" : "#e6e8ea";
        var svg = FormattableString.Invariant(
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'><path d='M 0 {height} V {corner} Q 0 0 {corner} 0 {top} H {width - corner} Q {width} 0 {width} {corner} V {height} Z' fill='{fill}' stroke='{stroke}' stroke-width='1'/></svg>"
        );
        navigationShapeKey = key;
        return navigationShape = Svg.Picture(Encoding.UTF8.GetBytes(svg), width, height);
    }

    private VStack TabSlot<T>(int index, T icon, int current, float r)
        where T : Node, IVisualBox =>
        new VStack(
            icon.Opacity(current == index ? 1 : 0.2f),
            new Button("", () => SelectTab(index))
                .WidthPercent(100)
                .HeightPercent(100)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        )
            .Grow()
            .Height(64 * r)
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center);

    private VStack CreateButton(Sheet open, float r)
    {
        var size = 56 * r;
        return new VStack(
            new Text("+")
                .TextStyle(TextStyleFor("Bold"))
                .FontSize(32 * r)
                .ForegroundColor(Color.White),
            new Button("", () => OpenEditor?.Invoke(null))
                .Width(size)
                .Height(size)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        )
            .Width(size)
            .Height(size)
            .CornerRadius(size / 2)
            .BackgroundColor(Accent)
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center);
    }

    private Image TabIcon(string asset, float height, float aspect) =>
        Icon(asset, height, height * aspect, Ink);
}
