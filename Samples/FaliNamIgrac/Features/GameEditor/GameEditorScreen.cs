using DotNative;
using DotNative.WheelDatePicker;
using DotNative.WheelTimePicker;

namespace FaliNamIgrac;

public enum GameEditorLabel
{
    Create,
    Edit,
    Sport,
    Location,
    Date,
    Start,
    End,
    Needed,
    Total,
    Price,
    Currency,
    Description,
    Terrain,
    Unspecified,
    Indoor,
    Outdoor,
    Grass,
    Concrete,
    Parquet,
    Clinker,
    Sand,
    Save,
    Cancel,
    Retry,
    Loading,
}

public interface IGameEditorText
{
    string Label(GameEditorLabel label);
    string Error(GameEditorIssue issue);
}

/// <summary>Call InitializeAsync after mounting. Leave cancels all editor work; create a fresh instance to reopen.</summary>
public sealed partial class GameEditorScreen(
    UiContext ui,
    GameEditorNotifier editor,
    IGameEditorText text,
    IWheelTimePicker timePicker,
    Action onClose,
    Action<GameEditorSaved> onSaved
) : ScreenComponent(ui)
{
    private bool picking;

    public async Task InitializeAsync()
    {
        await editor.LoadAsync();
        await dispatcher.InvokeAsync(() =>
        {
            var draft = editor.State.Draft;
            if (
                editor.IsCurrent
                && string.IsNullOrEmpty(draft.Terrain)
                && AllowedTerrains(draft.SportId, draft.Venue).FirstOrDefault() is { } terrain
            )
                editor.Change(d => d with { Terrain = terrain });
        });
    }

    public override void Leave() => editor.Dispose();

    private string L(GameEditorLabel label) => text.Label(label);

    private readonly State<bool> choosingSport = new(false);

    public override Element Build()
    {
        var state = editor.State;
        var d = state.Draft;
        var size = presentation.Dimensions.Value ?? (375, 812);
        var r = Math.Min(size.Item1 / 375f, size.Item2 / 812f);
        r = Math.Max(.8f, r);
        Text Label(string value) =>
            new Text(value).TextStyle(TextStyleFor()).ForegroundColor(Ink).FontSize(16 * r);
        Element Field(
            GameEditorLabel label,
            string value,
            Action<string> change,
            bool multiline = false
        ) =>
            new TextField(value, change)
            {
                Placeholder = L(label),
                Input = new()
                {
                    IsMultiline = multiline,
                    Keyboard = label
                        is GameEditorLabel.Needed
                            or GameEditorLabel.Total
                            or GameEditorLabel.Price
                        ? TextKeyboard.Decimal
                        : TextKeyboard.Text,
                },
            }
                .TextStyle(TextStyleFor())
                .ForegroundColor(Ink)
                .FontSize(16 * r)
                .Padding(16 * r)
                .Height((multiline ? 112 : 56) * r)
                .BackgroundColor(theme.Surface)
                .CornerRadius(16 * r)
                .WithKey("game-editor-" + label);
        Element Select(string value, Action tap, bool calendar = false) =>
            new HStack(
                calendar
                    ? new Text("\ue122")
                        .TextStyle(TextStyleFor("Icons"))
                        .FontSize(24 * r)
                        .ForegroundColor(Ink)
                        .Width(32 * r)
                    : new VStack(),
                Label(value)
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinWidth(0)
                    .MaxLines(1)
                    .TextOverflow(TextOverflow.EllipsisEnd),
                new Image(FniAssets.Image("icons/arrow_down.png"))
                    .Width(16 * r)
                    .Height(16 * r)
                    .TintColor(Ink),
                new Button("", tap).Position(PositionMode.Absolute).Insets(new(0, 0, 0, 0))
            )
                .Padding(16 * r)
                .Height(56 * r)
                .BackgroundColor(theme.Surface)
                .CornerRadius(16 * r)
                .CrossAlignment(CrossAxisAlignment.Center);
        Element Choice(string value, bool selected, Action tap) =>
            new Button(value, tap)
                .TextStyle(TextStyleFor())
                .FontSize(16 * r)
                .ForegroundColor(selected ? Accent : Ink)
                .TextAlignment(TextAlignment.Left)
                .Padding(16 * r, 8 * r)
                .MinHeight(48 * r);
        var fields = new List<Element>();
        if (state.Error is { } error)
            fields.Add(Label(text.Error(error)).ForegroundColor(theme.Error));
        if (!state.Loaded)
        {
            fields.Add(Label(L(editor.Busy ? GameEditorLabel.Loading : GameEditorLabel.Retry)));
            if (!editor.Busy)
                fields.Add(Choice(L(GameEditorLabel.Retry), false, () => _ = editor.LoadAsync()));
        }
        else
        {
            fields.Add(SportDropdown(d, r));
            fields.Add(
                Field(GameEditorLabel.Location, d.Address, value => _ = editor.SearchAsync(value))
            );
            if (state.PlacesBusy)
                fields.Add(Label(L(GameEditorLabel.Loading)));
            if (state.PlacesError is { } placeError)
                fields.Add(Label(text.Error(placeError)).ForegroundColor(theme.Error));
            fields.AddRange(
                state.Suggestions.Select(place =>
                    Choice(place.Description, false, () => _ = editor.SelectPlaceAsync(place))
                )
            );
            fields.Add(
                Select(d.Date?.ToString("dd.MM.yyyy") ?? Loc.ChooseDate, () => _ = PickDate(), true)
            );
            fields.Add(
                new HStack(
                    new VStack(
                        Select(
                            d.Start?.ToString("HH:mm") ?? L(GameEditorLabel.Start),
                            () => _ = PickTime(true)
                        )
                    )
                        .Grow()
                        .Shrink()
                        .FlexBasis(0)
                        .MinWidth(0),
                    new VStack(
                        Select(
                            d.End?.ToString("HH:mm") ?? L(GameEditorLabel.End),
                            () => _ = PickTime(false)
                        )
                    )
                        .Grow()
                        .Shrink()
                        .FlexBasis(0)
                        .MinWidth(0)
                ).Spacing(16 * r)
            );
            var terrain = TerrainOptions(d, r);
            if (terrain is not null)
                fields.Add(terrain);
            if (d.SportId != 11)
                fields.Add(VenueTabs(d, r));
            fields.Add(PlayerCounters(d, r));
            fields.Add(
                Field(
                    GameEditorLabel.Description,
                    d.Description ?? "",
                    value => editor.Change(draft => draft with { Description = value }),
                    true
                )
            );
            fields.Add(PriceDropdown(d, r));
            fields.AddRange(
                state.Validation.Select(issue =>
                    Label(text.Error(issue)).ForegroundColor(theme.Error)
                )
            );
        }
        return new VStack(
            new Button("", onClose)
                .Position(PositionMode.Absolute)
                .Insets(new(0, 0, 0, 0))
                .BackgroundColor(new Color(0, 0, 0, 115)),
            new VStack(
                new HStack(
                    new Text(
                        L(editor.GameId is null ? GameEditorLabel.Create : GameEditorLabel.Edit)
                    )
                        .TextStyle(TextStyleFor("Bold"))
                        .FontSize(24 * r)
                        .ForegroundColor(Ink)
                        .Grow()
                        .Shrink()
                        .FlexBasis(0)
                        .MinWidth(0),
                    new Button(
                        "×",
                        () =>
                        {
                            Leave();
                            onClose();
                        }
                    ).FontSize(28 * r).TextAlignment(TextAlignment.Center).ForegroundColor(Color.White).BackgroundColor(new Color(128, 128, 128)).Width(48 * r).Height(48 * r).CornerRadius(16 * r)
                )
                    .CrossAlignment(CrossAxisAlignment.Center)
                    .Spacing(8 * r),
                new ScrollView(
                    new VStack(fields.ToArray())
                        .Spacing(32 * r)
                        .Padding(0, 24 * r)
                        .ClipToBounds(false)
                )
                    .Grow()
                    .Shrink()
                    .FlexBasis(0)
                    .MinHeight(0),
                new Button(
                    L(editor.Busy ? GameEditorLabel.Loading : GameEditorLabel.Save),
                    () => _ = Save()
                )
                    .TextStyle(TextStyleFor("Medium"))
                    .FontSize(16 * r)
                    .TextAlignment(TextAlignment.Center)
                    .ForegroundColor(Color.White)
                    .BackgroundColor(Accent)
                    .Height(56 * r)
                    .CornerRadius(16 * r)
                    .Enabled(
                        state.Loaded
                            && !editor.Busy
                            && !state.PlacesBusy
                            && !state.Saved
                            && editor.IsCurrent
                    )
            )
                .Padding(16 * r)
                .CornerRadius(16 * r)
                .BackgroundColor(theme.Background)
                .WidthPercent(100)
                .MaxWidth(520)
                .Height(
                    Math.Max(
                        160,
                        Math.Min(
                            size.Item2 * .8f,
                            size.Item2
                                - Insets.Top
                                - Math.Max(Insets.Bottom, Insets.Keyboard)
                                - 32 * r
                        )
                    )
                )
        )
            .Padding(
                16 * r,
                Insets.Top + 16 * r,
                16 * r,
                Math.Max(Insets.Bottom, Insets.Keyboard) + 16 * r
            )
            .MainAlignment(MainAxisAlignment.Center)
            .CrossAlignment(CrossAxisAlignment.Center)
            .WidthPercent(100)
            .HeightPercent(100);
    }

    private async Task Save()
    {
        var saved = await editor.SaveAsync();
        await dispatcher.InvokeAsync(() =>
        {
            if (saved is not null && editor.IsCurrent)
                onSaved(saved);
        });
    }

    private string Locale =>
        presentation.Locale.Value switch
        {
            "sr" => "sr-Latn-RS",
            "ru" => "ru-RU",
            _ => "en-GB",
        };

    private async Task PickDate()
    {
        if (picking || editor.Busy || !editor.IsCurrent)
            return;
        picking = true;
        try
        {
            var date = await ui.ShowDatePickerAsync(
                new DatePickerOptions
                {
                    Renderer = DatePickerRenderer.SharedWheel,
                    InitialDate = editor.State.Draft.Date,
                    MinimumDate = new(1930, 1, 1),
                    MaximumDate = new(2090, 12, 31),
                    BackgroundColor = Argb(Background),
                    TextColor = Argb(Ink),
                    ItemColor = Argb(Ink),
                    FontFamily = FniAssets.Font().Family,
                    FontData = FniAssets.FontData(),
                    TitleFontFamily = FniAssets.Font("Bold").Family,
                    TitleFontData = FniAssets.FontData("Bold"),
                    ActionFontFamily = FniAssets.Font("Medium").Family,
                    ActionFontData = FniAssets.FontData("Medium"),
                    SafeArea = new(0, Insets.Top, 0, Insets.Bottom),
                    Locale = Locale,
                    Title = Loc.ChooseDate,
                    ConfirmText = L(GameEditorLabel.Save),
                    CancelText = L(GameEditorLabel.Cancel),
                },
                editor.Token
            );
            await dispatcher.InvokeAsync(() =>
            {
                if (date is not null)
                    editor.Change(d => d with { Date = date });
            });
        }
        catch (OperationCanceledException) when (editor.Token.IsCancellationRequested) { }
        catch
        {
            await dispatcher.InvokeAsync(editor.ReportPickerError);
        }
        finally
        {
            picking = false;
        }
    }

    private async Task PickTime(bool start)
    {
        if (picking || editor.Busy || !editor.IsCurrent)
            return;
        picking = true;
        try
        {
            var time = await timePicker.ShowAsync(
                new TimePickerOptions
                {
                    Renderer = TimePickerRenderer.SharedWheel,
                    BackgroundColor = Argb(theme.Background),
                    TextColor = Argb(Ink),
                    ItemColor = Argb(Ink),
                    FontFamily = FniAssets.Font().Family,
                    FontData = FniAssets.FontData(),
                    TitleFontFamily = FniAssets.Font("Bold").Family,
                    TitleFontData = FniAssets.FontData("Bold"),
                    ActionFontFamily = FniAssets.Font("Medium").Family,
                    ActionFontData = FniAssets.FontData("Medium"),
                    SafeArea = new(0, Insets.Top, 0, Insets.Bottom),
                    InitialTime = start ? editor.State.Draft.Start : editor.State.Draft.End,
                    Locale = Locale,
                    Title = L(start ? GameEditorLabel.Start : GameEditorLabel.End),
                    ConfirmText = L(GameEditorLabel.Save),
                    CancelText = L(GameEditorLabel.Cancel),
                },
                editor.Token
            );
            await dispatcher.InvokeAsync(() =>
            {
                if (time is not null)
                    editor.Change(d =>
                        start
                            ? d with
                            {
                                Start = time,
                                End = d.End ?? time.Value.AddHours(1),
                            }
                            : d with
                            {
                                End = time,
                            }
                    );
            });
        }
        catch (OperationCanceledException) when (editor.Token.IsCancellationRequested) { }
        catch
        {
            await dispatcher.InvokeAsync(editor.ReportPickerError);
        }
        finally
        {
            picking = false;
        }
    }
}
