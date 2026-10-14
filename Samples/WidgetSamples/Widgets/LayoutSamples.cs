using DotNative;

namespace WidgetSamples.Widgets;

public sealed class LayoutSamples : Component
{
    public override Element Build(BuildContext context)
    {
        var theme = Theme.Of(context);
        return new VStack(
            new Text("Layout & composition").FontSize(22).Bold(),
            new HStack(
                new Icon(IconName.Check).TintColor(theme.Primary),
                new Text("HStack + Spacer"),
                new Spacer(),
                new Icon(IconName.Star).TintColor(theme.Primary)
            )
                .Spacing(12)
                .CrossAlignment(CrossAxisAlignment.Center),
            new Container(
                new Text("A styled container around ordinary content.").TextColor(theme.OnPrimary)
            )
                .Padding(18)
                .BackgroundColor(theme.Primary)
                .CornerRadius(12),
            new HStack(
                new Text("Per-edge padding").Padding(12, 6, 20, 10).Border(1, theme.Primary),
                new Text("Wrapped row")
                    .Padding(12, 8)
                    .BackgroundColor(theme.Primary)
                    .TextColor(theme.OnPrimary),
                new Text("Min / max dimensions").MinWidth(120).MaxWidth(220).Margin(4, 8)
            )
                .Wrap()
                .Spacing(10)
                .CrossAlignment(CrossAxisAlignment.Center),
            new Container(new Text("Per-corner radii · independent border styles").Padding(18))
                .CornerRadius(24, 4, 24, 4)
                .Border(2, theme.Primary, BorderLineStyle.Dashed)
                .BorderLeft(new BorderSide(6, theme.Primary))
                .BorderBottom(new BorderSide(3, theme.Primary, BorderLineStyle.Dotted)),
            new Button("Hover, focus or press", () => { })
                .Padding(16)
                .BackgroundColor(theme.Primary)
                .TextColor(theme.OnPrimary)
                .CornerRadius(12)
                .OnState(InteractionState.Hovered, new(Opacity: 0.85f))
                .OnState(InteractionState.Focused, new(BorderWidth: 3, BorderColor: theme.Primary))
                .OnState(InteractionState.Pressed, new(Opacity: 0.65f, TranslationY: 2))
                .Transition(new StyleTransition(TimeSpan.FromMilliseconds(140))),
            new Grid(
                new GridDefinition([GridTrack.Fraction(), GridTrack.Fraction(2)]),
                new Text("1fr").Padding(12).Border(1, theme.Primary),
                new Text("2fr").Padding(12).Border(1, theme.Primary),
                new Container(new Text("Spans both columns").TextColor(Color.White))
                    .GridColumn(1, 2)
                    .Padding(12)
                    .BackgroundGradient(
                        Gradient.Linear(
                            new(0, 0),
                            new(1, 0),
                            new(0, new Color(65, 80, 210)),
                            new(1, new Color(150, 40, 170))
                        )
                    )
                    .CornerRadius(10)
            )
                .RowGap(8)
                .ColumnGap(12),
            new HStack(new Text("Start").TextAlignment(TextAlignment.Start), new Text("End"))
                .FlowDirection(FlowDirection.RightToLeft)
                .PaddingInline(20, 8)
                .Spacing(12),
            new Container(new Text("50% width · 2:1 aspect ratio").Bold().Padding(12))
                .WidthPercent(50)
                .AspectRatio(2)
                .AlignSelf(SelfAlignment.Start)
                .Border(2, theme.Primary)
                .CornerRadius(10)
        ).Spacing(12);
    }
}
