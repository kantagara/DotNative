using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DotNative;
using Microsoft.Extensions.DependencyInjection;

namespace InteractionApp;

public static class Program
{
    public static async Task Main()
    {
        var builder = DotNativeApplication.CreateBuilder();
        builder.Services.AddDotNativeNavigation();
        builder.Services.AddSingleton<Probe>();
        builder.Services.AddScoped<Visit>();
        var app = builder.Build();
        await app.Services.GetRequiredService<Navigator>().PushAsync<Home>("Home");
        app.Run<App>();
        if (Environment.GetEnvironmentVariable("DOTNATIVE_NAVIGATION_AUTORUN") == "1")
        {
            await Task.Delay(1000);
            await app
                .Services.GetRequiredService<Probe>()
                .Run(app.Services.GetRequiredService<Navigator>());
        }
    }
}

public sealed class Probe
{
    public NavigationTransition Transition = new();
    public State<string> Result = new("Ready");
    public readonly State<int> Disposals = new(0);
    public int Disposed
    {
        get => Disposals.Value;
        set => Disposals.Value = value;
    }

    public async Task Run(Navigator navigator)
    {
        try
        {
            var cases = 0;
            foreach (
                var kind in new[]
                {
                    NavigationTransitionKind.Slide,
                    NavigationTransitionKind.Fade,
                    NavigationTransitionKind.None,
                }
            )
            foreach (var easing in Enum.GetValues<TransitionEasing>())
            foreach (var milliseconds in new[] { 150, 600 })
            {
                Transition = new()
                {
                    Kind = kind,
                    Easing = easing,
                    Duration = TimeSpan.FromMilliseconds(milliseconds),
                };
                var before = Disposed;
                var timer = Stopwatch.StartNew();
                await navigator.PushAsync<Detail>("Detail");
                CheckTime(timer.Elapsed.TotalMilliseconds, "push");
                if (Disposed != before)
                    throw new Exception("Live screen scope disposed on push");
                await Task.Delay(80);
                timer.Restart();
                if (!await navigator.PopAsync())
                    throw new Exception("Pop rejected");
                CheckTime(timer.Elapsed.TotalMilliseconds, "pop");
                if (Disposed != before + 1)
                    throw new Exception("Popped scope not disposed exactly once");
                cases++;
            }
            Result.Value = $"PASS {cases} round trips";
        }
        catch (Exception ex)
        {
            Result.Value = "FAIL " + ex.Message;
        }
        Console.WriteLine("NAV_RESULT " + Result.Value);
    }

    private void CheckTime(double elapsed, string operation)
    {
        Console.WriteLine(
            $"NAV_TIMING {Transition.Kind} {Transition.Easing} {Transition.Duration.TotalMilliseconds} {operation} {elapsed:F1}"
        );
        var min =
            Transition.Kind == NavigationTransitionKind.None
                ? 0
                : Transition.Duration.TotalMilliseconds * .8;
        var max =
            Transition.Kind == NavigationTransitionKind.None
                ? 350
                : Transition.Duration.TotalMilliseconds + 1000;
        if (elapsed < min || elapsed > max)
            throw new Exception(
                $"{Transition.Kind}/{Transition.Easing} {operation}: {elapsed:F0}ms, expected {min:F0}..{max:F0}"
            );
    }
}

public sealed class Visit(Probe probe) : IDisposable
{
    public void Dispose() => probe.Disposed++;
}

public sealed class App(Navigator navigator) : Component
{
    public override Element Build() => new NavigationHost(navigator);
}

public sealed class Home(Navigator navigator, Probe probe) : Component
{
    public override Element Build() => new VStack(
            new Text(probe.Result.Value),
            new Text($"Disposed: {probe.Disposed}"),
            new Button("Run timing suite", async () => await probe.Run(navigator)),
            new Button(
                "Open native Back",
                async () =>
                {
                    probe.Transition = new()
                    {
                        Kind = NavigationTransitionKind.Fade,
                        Duration = TimeSpan.FromMilliseconds(600),
                    };
                    await navigator.PushAsync<Detail>("Detail");
                }
            ),
            new Button(
                "Open curve probe",
                async () =>
                {
                    probe.Transition = new()
                    {
                        Kind = NavigationTransitionKind.Fade,
                        Duration = TimeSpan.FromMilliseconds(1500),
                        Easing = TransitionEasing.EaseInCubic,
                    };
                    await navigator.PushAsync<Detail>("Detail");
                }
            ),
            new Button(
                "Open slide swipe",
                async () =>
                {
                    probe.Transition = new()
                    {
                        Kind = NavigationTransitionKind.Slide,
                        Duration = TimeSpan.FromMilliseconds(600),
                    };
                    await navigator.PushAsync<Detail>("Detail");
                }
            ),
            new Button(
                "Open system swipe",
                async () =>
                {
                    probe.Transition = new();
                    await navigator.PushAsync<Detail>("Detail");
                }
            )
        ).Padding(24).Spacing(24).BackgroundColor(Color.White);
}

public sealed class Detail(Navigator navigator, Probe probe, Visit visit)
    : Component,
        INavigationScreen
{
    private readonly Visit _visit = visit;
    private readonly State<int> _edits = new(0);

    public ScreenOptions BuildOptions(BuildContext context) =>
        new() { Transition = probe.Transition };

    public override Element Build() => new VStack(
            new Text("Detail ready"),
            new Text($"Disposed: {probe.Disposed}"),
            new Text($"Retained edits: {_edits.Value}"),
            new Button("Edit retained state", () => _edits.Value++),
            new Button(
                "Managed Back",
                async () =>
                {
                    await navigator.PopAsync();
                }
            )
        ).Padding(24).Spacing(24).BackgroundColor(new Color(40, 120, 200));
}
