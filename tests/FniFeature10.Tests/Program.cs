using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNative;
using DotNative.Plugins;
using DotNative.Preferences;
using FaliNamIgrac;
using Feature10Fixtures;

var tests = new (string, Action)[]
{
    ("failed language write preserves UI and durable value, retry commits", Failure),
    ("duplicate requests cannot race persistence", Duplicate),
    ("cancellation before write and late failure", Cancel),
    ("successful commit wins cancellation for device preference", Commit),
    ("theme failure, retry, commit cancellation and navigation", Theme),
    ("new preference instance restores persisted language", Restart),
    ("real theme controller restores stored mode and system fallback", ThemeRestart),
    ("unsupported and regional locale fallback, missing translation fallback", Fallback),
    ("date, currency and timezone formats independent of host culture", Formats),
    ("ARB key parity, placeholders and intended empty values", Arb),
};
foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine("PASS " + name);
}
Console.WriteLine($"Passed {tests.Length} presentation test groups.");

static void Check(bool value, string message)
{
    if (!value)
        throw new Exception(message);
}
static void Failure()
{
    foreach (var language in new[] { "en", "sr", "ru" })
    {
        var ui = new Dispatcher();
        var p = new PresentationState();
        p.Locale.Value = language;
        var stored = language;
        var fail = true;
        var count = 0;
        var n = new SettingsNotifier(
            ui,
            new FakeTheme(),
            p,
            (value, _) =>
            {
                count++;
                if (fail)
                    throw new IOException("private storage details");
                stored = value;
                return Task.CompletedTask;
            }
        );
        var next = language == "ru" ? "en" : "ru";
        ui.Finish(n.SetLanguageAsync(next, default));
        Check(
            p.Locale.Value == language && stored == language && !n.Busy,
            "failed save changed preference"
        );
        Check(
            n.State == SettingsTexts.ForLocale(language).SaveFailed && !n.State.Contains("private"),
            "wrong error"
        );
        fail = false;
        ui.Finish(n.RetryAsync(default));
        Check(
            stored == next && p.Locale.Value == next && n.State == "" && count == 2,
            "retry failed"
        );
        ui.Finish(n.RetryAsync(default));
        Check(count == 2, "successful retry retained");
    }
}
static void Duplicate()
{
    var ui = new Dispatcher();
    var p = new PresentationState();
    var gate = new TaskCompletionSource();
    var count = 0;
    var n = new SettingsNotifier(
        ui,
        new FakeTheme(),
        p,
        async (_, _) =>
        {
            count++;
            await gate.Task;
        }
    );
    var first = n.SetLanguageAsync("sr", default);
    Check(n.Busy && p.Locale.Value == "en", "optimistic or no loading");
    ui.Finish(n.SetLanguageAsync("ru", default));
    ui.Finish(n.RetryAsync(default));
    gate.SetResult();
    ui.Finish(first);
    Check(count == 1 && p.Locale.Value == "sr" && !n.Busy, "duplicate write");
}
static void Cancel()
{
    var ui = new Dispatcher();
    var p = new PresentationState();
    var gate = new TaskCompletionSource();
    var count = 0;
    var n = new SettingsNotifier(
        ui,
        new FakeTheme(),
        p,
        async (_, token) =>
        {
            count++;
            await gate.Task;
            token.ThrowIfCancellationRequested();
        }
    );
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    ui.Finish(n.SetLanguageAsync("sr", cts.Token));
    Check(count == 0, "pre-cancel wrote");
    using var later = new CancellationTokenSource();
    var pending = n.SetLanguageAsync("ru", later.Token);
    later.Cancel();
    gate.SetResult();
    ui.Finish(pending);
    Check(p.Locale.Value == "en" && n.State == "" && !n.Busy, "cancel published");
    var failure = new TaskCompletionSource();
    n = new(ui, new FakeTheme(), p, (_, _) => failure.Task);
    using var late = new CancellationTokenSource();
    pending = n.SetLanguageAsync("ru", late.Token);
    late.Cancel();
    failure.SetException(new IOException("late"));
    ui.Finish(pending);
    Check(n.State == "" && p.Locale.Value == "en", "late error leaked");
    try
    {
        n.SetLanguageAsync("xx", default);
        throw new Exception("invalid accepted");
    }
    catch (ArgumentOutOfRangeException) { }
}
static void Commit()
{
    var ui = new Dispatcher();
    var p = new PresentationState();
    using var cts = new CancellationTokenSource();
    var stored = "en";
    var n = new SettingsNotifier(
        ui,
        new FakeTheme(),
        p,
        (value, _) =>
        {
            stored = value;
            cts.Cancel();
            return Task.CompletedTask;
        }
    );
    ui.Finish(n.SetLanguageAsync("sr", cts.Token));
    Check(stored == p.Locale.Value && stored == "sr", "committed language differs until restart");
}
static void Theme()
{
    var ui = new Dispatcher();
    var p = new PresentationState();
    var theme = new FakeTheme { Fail = true };
    var n = new SettingsNotifier(ui, theme, p, (_, _) => Task.CompletedTask);
    ui.Finish(n.SetThemeAsync(ThemeMode.Dark, default));
    Check(
        theme.Current.Value.Mode == ThemeMode.Light
            && theme.Stored == ThemeMode.Light
            && n.State.Length > 0,
        "failed theme changed"
    );
    theme.Fail = false;
    ui.Finish(n.RetryAsync(default));
    p.CurrentScreen.Value = ApplicationScreen.Landing;
    Check(
        theme.Current.Value.Mode == ThemeMode.Dark && theme.Stored == ThemeMode.Dark,
        "navigation lost theme"
    );
    using var cts = new CancellationTokenSource();
    theme.AfterCommit = cts.Cancel;
    ui.Finish(n.SetThemeAsync(ThemeMode.Light, cts.Token));
    Check(
        theme.Current.Value.Mode == ThemeMode.Light && theme.Loads == 1,
        "canceled commit not reloaded"
    );
}
static void Restart()
{
    var id = "FniFeature10.Tests." + Guid.NewGuid().ToString("N");
    var preferences = new FilePreferences(id);
    var ui = new Dispatcher();
    var p = new PresentationState();
    try
    {
        var n = new SettingsNotifier(
            ui,
            new FakeTheme(),
            p,
            (locale, token) => preferences.SetAsync("selected_local", locale, token)
        );
        ui.Finish(n.SetLanguageAsync("ru", default));
        var restarted = new FilePreferences(id);
        var read = restarted.GetAsync("selected_local", "en");
        ui.Finish(read);
        Check(
            read.Result == "ru" && PresentationLocale.Text(read.Result).Locale == "ru",
            "not durable across instances"
        );
    }
    finally
    {
        ui.Finish(preferences.ClearAsync());
    }
}
static void ThemeRestart()
{
    var ui = new Dispatcher();
    using var channels = new PlatformChannels(ui, null);
    // ThemeController defaults to entry-assembly storage: exclusively this test executable.
    var preferences = new FilePreferences("FniFeature10.Tests");
    var old = preferences.GetAsync<string?>("selected_theme", null);
    ui.Finish(old);
    try
    {
        ui.Finish(preferences.RemoveAsync("selected_theme"));
        var first = new ThemeController(channels, PresentationTarget.Local, new Appearance(), ui);
        ui.Finish(first.LoadAsync(default));
        Check(first.Current.Value.Mode == ThemeMode.Dark, "system fallback lost");
        ui.Finish(first.SetAsync(ThemeMode.Light, default));
        var second = new ThemeController(channels, PresentationTarget.Local, new Appearance(), ui);
        ui.Finish(second.LoadAsync(default));
        Check(second.Current.Value.Mode == ThemeMode.Light, "theme restart lost preference");
        ui.Finish(preferences.SetAsync("selected_theme", "invalid"));
        ui.Finish(second.LoadAsync(default));
        Check(second.Current.Value.Mode == ThemeMode.Light, "invalid stored theme fallback");
    }
    finally
    {
        ui.Finish(
            old.Result is null
                ? preferences.RemoveAsync("selected_theme")
                : preferences.SetAsync("selected_theme", old.Result)
        );
    }
}
static void Fallback()
{
    foreach (var unknown in new string?[] { null, "", "de-DE", "broken" })
        Check(PresentationLocale.Text(unknown).Locale == "en", "unknown must safely use English");
    Check(PresentationLocale.Text("SR_latn_RS").Locale == "sr", "Serbian regional locale");
    Check(PresentationLocale.Text("ru-RU").Locale == "ru", "Russian regional locale");
    Check(
        FallbackFixture.ForLocale("sr").Missing("Ana") == "English fallback Ana",
        "missing Serbian key fallback"
    );
    Check(
        FallbackFixture.ForLocale("ru").Missing("Ana") == "English fallback Ana",
        "missing Russian key fallback"
    );
}
static void Formats()
{
    var original = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        Check(PresentationLocale.Price(1234.5m, "RSD", "en") == "1,234.5 RSD", "English price");
        Check(
            PresentationLocale.Price(1234.5m, "EUR", "sr") == "1.234,5 EUR",
            "Serbian price/currency"
        );
        Check(
            PresentationLocale.Price(1234.5m, "RSD", "ru").EndsWith("234,5 RSD"),
            "Russian decimal"
        );
        var day = new DateOnly(2026, 10, 13);
        Check(PresentationLocale.Date(day, "en") == "13/10/2026", "English date");
        Check(PresentationLocale.Date(day, "ru") == "13.10.2026", "Russian date");
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Test+2",
            TimeSpan.FromHours(2),
            "Test+2",
            "Test+2"
        );
        Check(
            PresentationLocale
                .Timestamp(new(2026, 10, 13, 23, 15, 0, TimeSpan.Zero), zone, "en")
                .Contains("14/10/2026 01:15"),
            "timezone rollover"
        );
    }
    finally
    {
        CultureInfo.CurrentCulture = original;
    }
}
static void Arb()
{
    using var en = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppArb/app_en.arb"))
    );
    foreach (var locale in new[] { "sr", "ru" })
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, $"AppArb/app_{locale}.arb"))
        );
        foreach (var key in en.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('@')))
        {
            Check(
                doc.RootElement.TryGetProperty(key.Name, out var value),
                $"Missing {locale}/{key.Name}"
            );
            Check(
                value.GetString()!.Length > 0
                    || key.Name is "languageBody" or "enterNewPasswordBody" or "chooseSportLabel",
                $"Empty {locale}/{key.Name}"
            );
            var args = Regex
                .Matches(key.Value.GetString()!, @"\{\w+\}")
                .Select(m => m.Value)
                .Order()
                .ToArray();
            Check(
                args.SequenceEqual(
                    Regex.Matches(value.GetString()!, @"\{\w+\}").Select(m => m.Value).Order()
                ),
                $"Placeholder mismatch {locale}/{key.Name}"
            );
        }
    }
}

sealed class FakeTheme : IThemeController
{
    public State<IThemeData> Current { get; } = new(ThemeData.Light);
    public ThemeMode Stored = ThemeMode.Light;
    public bool Fail;
    public int Loads;
    public Action? AfterCommit;

    public Task LoadAsync(CancellationToken token)
    {
        Loads++;
        Current.Value = Stored == ThemeMode.Dark ? ThemeData.Dark : ThemeData.Light;
        return Task.CompletedTask;
    }

    public Task SetAsync(ThemeMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Fail)
            throw new IOException("disk");
        Stored = mode;
        AfterCommit?.Invoke();
        if (!token.IsCancellationRequested)
            Current.Value = mode == ThemeMode.Dark ? ThemeData.Dark : ThemeData.Light;
        return Task.CompletedTask;
    }
}

sealed class Dispatcher : IUiDispatcher
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> queue = new();

    public bool CheckAccess() => Environment.CurrentManagedThreadId == thread;

    public void Post(Action action) => queue.Enqueue(action);

    public void Finish(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            while (queue.TryDequeue(out var action))
                action();
            Thread.Sleep(1);
        }
        if (!task.IsCompleted)
            throw new TimeoutException("Test task did not finish");
        task.GetAwaiter().GetResult();
        while (queue.TryDequeue(out var action))
            action();
    }
}

sealed class Appearance : ISystemAppearance
{
    public ThemeMode Theme => ThemeMode.Dark;
}
