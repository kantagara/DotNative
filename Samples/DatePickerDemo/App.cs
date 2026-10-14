using System.Globalization;
using DotNative;
using DotNative.NativeDatePicker;
using DotNative.WheelDatePicker;

namespace DatePickerDemo;

public sealed class App : Component
{
    private readonly IWheelDatePicker wheel;
    private readonly INativeDatePicker native;
    private readonly IUiDispatcher dispatcher;

    public App(IWheelDatePicker wheel, INativeDatePicker native, IUiDispatcher dispatcher)
    {
        this.wheel = wheel;
        this.native = native;
        this.dispatcher = dispatcher;
        if (Environment.GetEnvironmentVariable("DOTNATIVE_DEMO_OPEN_PICKER") == "wheel")
            dispatcher.Post(() => _ = OpenAutomatic());
    }

    private readonly State<bool> busy = new(false);
    private readonly State<string> result = new("Choose a picker");
    private readonly State<string> locale = new("sr-Latn-RS");

    public override Element Build() =>
        new VStack(
            new Text("Date picker demo").FontSize(24),
            new Text("Wheel locale").FontSize(16),
            new RadioGroup<string>(
                locale.Value,
                value => locale.Value = value,
                new("sr-Latn-RS", "sr-Latn-RS — D / M / Y"),
                new("en-US", "en-US — M / D / Y"),
                new("en-GB", "en-GB — D / M / Y"),
                new("ja-JP", "ja-JP — Y / M / D")
            ),
            new Button("Plugin picker (automatic)", () => _ = OpenAutomatic())
                .Enabled(!busy.Value)
                .Padding(12),
            new Button("Wheel picker", () => _ = OpenWheel()).Enabled(!busy.Value).Padding(12),
            new Button("Native picker", () => _ = OpenNative()).Enabled(!busy.Value).Padding(12),
            new Text(result.Value)
        )
            .Spacing(16)
            .Padding(24)
            .WidthPercent(100)
            .HeightPercent(100)
            .BackgroundColor(Color.White);

    private Task OpenAutomatic() =>
        Show(
            "Automatic",
            () =>
                wheel.ShowAsync(
                    new DotNative.WheelDatePicker.DatePickerOptions
                    {
                        Title = "Plugin picker",
                        Locale = locale.Value,
                        MinimumDate = new(2023, 1, 1),
                        MaximumDate = new(2025, 12, 31),
                        InitialDate = new(2024, 1, 31),
                    }
                ),
            locale.Value
        );

    private Task OpenWheel() =>
        Show(
            "Wheel",
            () =>
                wheel.ShowAsync(
                    new DotNative.WheelDatePicker.DatePickerOptions
                    {
                        Title = "Wheel picker",
                        Locale = locale.Value,
                        Renderer = DatePickerRenderer.SharedWheel,
                        MinimumDate = new(2023, 1, 1),
                        MaximumDate = new(2025, 12, 31),
                        InitialDate = new(2024, 1, 31),
                    }
                ),
            locale.Value
        );

    private Task OpenNative() =>
        Show(
            "Native",
            () =>
                native.ShowAsync(
                    new DotNative.NativeDatePicker.DatePickerOptions
                    {
                        Title = "Native picker",
                        MinimumDate = new(2023, 1, 1),
                        MaximumDate = new(2025, 12, 31),
                        InitialDate = new(2024, 1, 31),
                    }
                )
        );

    private async Task Show(string name, Func<Task<DateOnly?>> show, string? localeName = null)
    {
        if (busy.Value)
            return;
        busy.Value = true;
        result.Value = name + ": opening";
        string message;
        var culture = localeName is null
            ? CultureInfo.CurrentCulture
            : CultureInfo.GetCultureInfo(localeName);
        try
        {
            message = name + ": " + ((await show())?.ToString("d", culture) ?? "cancelled");
        }
        catch (Exception e)
        {
            message = name + ": " + e.Message;
        }
        dispatcher.Post(() =>
        {
            result.Value = message;
            busy.Value = false;
        });
    }
}
