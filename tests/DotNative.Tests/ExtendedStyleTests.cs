using System.Buffers.Binary;
using DotNative;

internal static class ExtendedStyleTests
{
    public static void Run()
    {
        var original = new Text("styled");
        if (
            original.Visual.Opacity != 1
            || original.Visual.ScaleX != 1
            || original.Typography.Weight != FontWeight.Normal
        )
            throw new Exception("New descriptions must use semantic defaults.");
        var styled = original
            .Margin(1, 2, 3, 4)
            .Padding(5, 6, 7, 8)
            .MinWidth(30)
            .MaxWidth(90)
            .MinHeight(20)
            .MaxHeight(80)
            .FlexBasis(40)
            .Grow(2)
            .Shrink()
            .AlignSelf(SelfAlignment.Center);
        if (original.Layout != default || styled.Layout.Margin != new EdgeInsets(1, 2, 3, 4))
            throw new Exception("Styling mutated its input.");
        var dispatcher = new Dispatcher();
        var model = new NativeModel();
        var state = new State<Text>(styled);
        using var host = new RenderHost(
            new Factory(() => new VStack(state.Value)),
            dispatcher,
            model
        );
        host.RenderNow();
        var id = model.Find("styled");
        var bytes = model.Nodes[id].ExtendedLayout;
        float Read(int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));
        if (
            Read(0) != 1
            || Read(12) != 4
            || Read(16) != 30
            || Read(32) != 40
            || bytes[37] != (byte)SelfAlignment.Center
            || Read(38) != 5
            || Read(50) != 8
        )
            throw new Exception("Extended layout wire order mismatch.");
        state.Value = original;
        dispatcher.Drain();
        bytes = model.Nodes[id].ExtendedLayout;
        if (model.Find("styled") != id || Read(0) != 0 || Read(16) != -1 || Read(38) != -1)
            throw new Exception("Extended layout reset lost identity or retained a style.");
        if (
            styled.Padding(10).Layout.PaddingEdges != null
            || styled.Width(20).AutoWidth().Layout.Width != null
        )
            throw new Exception("Uniform padding or auto size failed to reset.");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => original.Margin(invalid));
            Reject(() => original.MinWidth(invalid));
            Reject(() => original.Padding(invalid, 0));
        }
        Reject(() => original.MinWidth(20).MaxWidth(10));
        Reject(() => original.FlexBasis(-1));
        Reject(() => original.AlignSelf((SelfAlignment)99));
        Reject(() => new VStack().Wrap((FlexWrap)99));
        var buffer = new CommandBuffer();
        Reject(() => buffer.SetLayout(1, new(MinHeight: 20, MaxHeight: 10)));
        if (!buffer.IsEmpty)
            throw new Exception("Invalid layout partially wrote a command.");
        Reject(() => original.Opacity(1.1f));
        Reject(() => original.Border(-1, Color.Red));
        Reject(() => original.Rotate(float.NaN));
        Reject(() => original.Shadow(Color.Black, -1));
        Reject(() => original.MaxLines(-1));
        Reject(() => original.FontWeight((FontWeight)450));
        Reject(() => original.LetterSpacing(float.NaN));
        Reject(() => original.LineHeight(0));
        Reject(() => original.TextDecoration((TextDecoration)8));
        var styledButton = new Button("flow", () => { })
            .MaxLines(2)
            .TextOverflow(TextOverflow.EllipsisMiddle)
            .LetterSpacing(2)
            .LineHeight(28)
            .Underline();
        var styledRadio = new RadioButton("flow", false, () => { })
            .MaxLines(3)
            .TextOverflow(TextOverflow.Wrap)
            .LetterSpacing(1)
            .LineHeight(24)
            .Strikethrough();
        if (
            styledButton.Typography.MaxLines != 2
            || styledButton.Typography.Overflow != TextOverflow.EllipsisMiddle
            || styledButton.Typography.Decoration != TextDecoration.Underline
            || styledRadio.Typography.MaxLines != 3
            || styledRadio.Typography.Overflow != TextOverflow.Wrap
            || styledRadio.Typography.Decoration != TextDecoration.Strikethrough
        )
            throw new Exception("Control text-flow styles were not retained.");
        Reject(() => original.AspectRatio(0));
        Reject(() => original.WidthPercent(-1));
        Reject(() => original.Left(float.PositiveInfinity));
        state.Value = original
            .Opacity(0.5f)
            .Border(2, Color.Red)
            .Scale(2)
            .Translate(-3, 4)
            .Shadow(Color.Black, 8, 1, 2)
            .ClipToBounds(false)
            .Rotate(30)
            .FontFamily("Example")
            .Bold()
            .Italic()
            .TextAlignment(TextAlignment.Right)
            .MaxLines(2)
            .TextOverflow(TextOverflow.EllipsisEnd)
            .LetterSpacing(1)
            .LineHeight(30)
            .Underline()
            .Strikethrough()
            .WidthPercent(50)
            .AspectRatio(2)
            .Position(PositionMode.Absolute)
            .Left(-10)
            .Top(20)
            .Visibility(Visibility.Hidden);
        dispatcher.Drain();
        var visual = model.Nodes[id].VisualStyle;
        var type = model.Nodes[id].Typography;
        var behavior = model.Nodes[id].LayoutBehavior;
        if (
            BinaryPrimitives.ReadSingleLittleEndian(visual) != 0.5f
            || visual[48] != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(type) != 700
            || type[2] != 1
            || type[17] != 3
            || BinaryPrimitives.ReadSingleLittleEndian(behavior) != 50
            || behavior[13] != 1
            || behavior[14] != 3
        )
            throw new Exception("Visual, typography or positioning wire mismatch.");
        if (
            state.Value.Width(12).Layout.Behavior.WidthPercent != null
            || state.Value.AutoWidth().Layout.Behavior.WidthPercent != null
        )
            throw new Exception("Fixed/auto dimensions did not clear percentage.");
        state.Value = original;
        dispatcher.Drain();
        visual = model.Nodes[id].VisualStyle;
        type = model.Nodes[id].Typography;
        behavior = model.Nodes[id].LayoutBehavior;
        if (
            model.Find("styled") != id
            || BinaryPrimitives.ReadSingleLittleEndian(visual) != 1
            || visual[48] != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(type) != 400
            || type[2] != 0
            || type[17] != 0
            || behavior[13] != 0
        )
            throw new Exception("Visual, typography or positioning failed to reset.");
        Reject(() => buffer.SetVisualStyle(1, VisualStyle.Default with { Opacity = float.NaN }));
        Reject(() => buffer.SetTypography(1, TypographyStyle.Default with { MaxLines = -3 }));
        if (!buffer.IsEmpty)
            throw new Exception("Invalid style partially advanced writer.");
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new Exception("Invalid style accepted.");
    }
}
