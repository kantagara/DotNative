using DotNative;

namespace FaliNamIgrac;

public abstract class PinScreen(UiContext ui) : ScreenComponent(ui)
{
    protected readonly State<bool> pinFocused = new(false);
    protected readonly State<float> pinCursor = new(0);
    protected readonly FocusNode pinFocus = new();
    protected int pinCursorRevision;

    protected Element AuthPin(
        float width,
        float w,
        float h,
        float r,
        Func<string, float, string, Text> label,
        string code,
        Action<string> setCode,
        FocusNode focus,
        string key
    )
    {
        // Pinput also uses one hidden editor and six rendered cells, with an 8-point separator.
        var cellWidth = Math.Min(56 * w, (width - 40) / 6);
        var cells = Enumerable
            .Range(0, 6)
            .Select(index =>
            {
                var glyph =
                    index < code.Length ? code[index].ToString()
                    : index == code.Length && pinFocused.Value ? "|"
                    : "";
                return (Element)
                    new VStack(
                        label(glyph, 24, "Bold")
                            .ForegroundColor(Ink)
                            .Opacity(index == code.Length ? pinCursor.Value : 1)
                    )
                        .MainAlignment(MainAxisAlignment.Center)
                        .CrossAlignment(CrossAxisAlignment.Center)
                        .Width(cellWidth)
                        .Height(56 * h)
                        .BackgroundColor(theme.Surface)
                        .CornerRadius(16 * r);
            })
            .ToArray();
        var editor = new TextField(
            code,
            text =>
            {
                var digits = text.Where(character => character is >= '0' and <= '9')
                    .Take(6)
                    .ToArray();
                setCode(new string(digits));
                if (digits.Length >= 6)
                    focus.Unfocus();
            }
        )
        {
            Focus = focus,
            OnFocusChanged = focused =>
            {
                pinFocused.Value = focused;
                var revision = ++pinCursorRevision;
                pinCursor.Value = 0;
                if (focused)
                    _ = BlinkPinAsync(revision);
            },
            Input = new()
            {
                Keyboard = TextKeyboard.Number,
                Autofill = TextAutofill.OneTimeCode,
                Autocorrect = false,
            },
        }
            .Width(width)
            .Height(56 * h)
            .Opacity(0)
            .Position(PositionMode.Absolute)
            .Left(0)
            .Top(0)
            .WithKey(key);
        return new VStack(
            new HStack(cells).Spacing(8).MainAlignment(MainAxisAlignment.Center),
            editor,
            new Button("", focus.RequestFocus)
                .Width(width)
                .Height(56 * h)
                .Position(PositionMode.Absolute)
                .Left(0)
                .Top(0)
        ).Height(56 * h);
    }

    protected async Task BlinkPinAsync(int revision)
    {
        var started = Environment.TickCount64;
        try
        {
            while (!disposed && revision == pinCursorRevision && pinFocused.Value)
            {
                await Task.Delay(16, lifetime.Token);
                if (disposed || revision != pinCursorRevision || !pinFocused.Value)
                    continue;
                var elapsed = (Environment.TickCount64 - started) % 900;
                pinCursor.Value = 1 - Math.Abs((float)elapsed / 450 - 1);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public override void Leave()
    {
        pinFocus.Unfocus();
        pinFocused.Value = false;
        pinCursorRevision++;
    }
}
