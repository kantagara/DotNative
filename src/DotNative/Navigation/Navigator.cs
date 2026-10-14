using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNative;

// Owns screen instances and their DI scopes. Native transition/presentation
// adapters must commit a pop only after interactive dismissal is accepted.
public sealed class Navigator(
    IServiceScopeFactory scopes,
    IUiDispatcher dispatcher,
    PresentationTarget? target = null
) : ReactiveSource, IAsyncDisposable
{
    private sealed record Entry(
        Component Screen,
        AsyncServiceScope Scope,
        string Title,
        string Key
    );

    private readonly List<Entry> _entries = [];
    private bool _changing;
    private bool _nativeInteraction;
    private int _nextKey;
    private int? _requestedDepth;
    private TaskCompletionSource<int>? _nativeCompletion;
    internal bool NativePresentation { get; init; } =
        (target ?? PresentationTarget.Local).Platform
        is NativePlatform.IOS
            or NativePlatform.Windows
            or NativePlatform.Android
            or NativePlatform.MacOS
            or NativePlatform.Linux;

    public ScreenOptions DefaultOptions { get; init; } = new();

    private ScreenOptions Options(Entry entry, BuildContext context) =>
        (
            entry.Screen is INavigationScreen configured
                ? configured.BuildOptions(context)
                : DefaultOptions
        ).Snapshot(entry.Title);

    private async void HeaderBack()
    {
        if (_changing || _nativeInteraction || !CanPop)
            return;
        try
        {
            await PopAsync();
        }
        catch (Exception error)
        {
            dispatcher.Post(() =>
                throw new InvalidOperationException("Back navigation failed.", error)
            );
        }
    }

    internal Element BuildPresentation(BuildContext context)
    {
        TrackRead();
        if (_entries.Count == 0)
            return new VStack();
        if (!NativePresentation)
        {
            var entry = _entries[^1];
            var options = Options(entry, context);
            if (
                !options.HeaderVisible
                && (target ?? PresentationTarget.Local).Platform
                    is not (
                        NativePlatform.Android
                        or NativePlatform.MacOS
                        or NativePlatform.Windows
                        or NativePlatform.Linux
                    )
            )
                return entry.Screen;
            Element header;
            if (
                (target ?? PresentationTarget.Local).Platform
                is NativePlatform.Android
                    or NativePlatform.MacOS
                    or NativePlatform.Windows
                    or NativePlatform.Linux
            )
                header = new NativeHeaderNode(
                    options with
                    {
                        BackButtonVisible = options.BackButtonVisible && CanPop,
                        CanGoBack = CanPop,
                    },
                    HeaderBack
                ).Height(
                    options.HeaderVisible
                    && (target ?? PresentationTarget.Local).Platform != NativePlatform.MacOS
                        ? (target ?? PresentationTarget.Local).Platform == NativePlatform.Android
                            ? 56
                            : 48
                        : 0
                );
            else
            {
                var actions = new List<Element>();
                if (CanPop && options.BackButtonVisible)
                    actions.Add(new Button("Back", HeaderBack).Padding(8));
                actions.AddRange(
                    options.Leading.Select(action =>
                        (Element)new Button(action.Title, action.Execute).Padding(8)
                    )
                );
                actions.Add(new Text(options.Title!).FontSize(18));
                actions.Add(new Spacer());
                actions.AddRange(
                    options.Trailing.Select(action =>
                        (Element)new Button(action.Title, action.Execute).Padding(8)
                    )
                );
                header = new HStack(actions.ToArray()).Padding(8).Spacing(8);
            }
            return new VStack(
                header,
                new FillNavigationContent(entry.Screen, entry.Key)
            ).BackgroundColor(Theme.Of(context).Background);
        }
        return new NativeNavigationNode(
            _entries
                .Select(entry =>
                    (Element)
                        new NativeNavigationPage(
                            (target ?? PresentationTarget.Local).Platform != NativePlatform.IOS
                                ? BuildNativePage(entry, context)
                                : entry.Screen,
                            (target ?? PresentationTarget.Local).Platform != NativePlatform.IOS
                                ? Options(entry, context) with
                                {
                                    HeaderVisible = false,
                                }
                                : Options(entry, context)
                        )
                        {
                            Key = entry.Key,
                        }.BackgroundColor(Theme.Of(context).Background)
                )
                .ToArray(),
            _requestedDepth ?? _entries.Count,
            Theme.Of(context).Mode,
            Options(_entries[^1], context).Transition,
            OnNativeShown,
            busy => _nativeInteraction = busy
        ).BackgroundColor(Theme.Of(context).Background);
    }

    private Element BuildNativePage(Entry entry, BuildContext context)
    {
        var options = Options(entry, context);
        bool canPop = _entries.IndexOf(entry) > 0;
        return new VStack(
            new NativeHeaderNode(
                options with
                {
                    BackButtonVisible = options.BackButtonVisible && canPop,
                    CanGoBack = canPop,
                },
                HeaderBack
            ).Height(
                !options.HeaderVisible
                || (target ?? PresentationTarget.Local).Platform == NativePlatform.MacOS
                    ? 0
                : (target ?? PresentationTarget.Local).Platform == NativePlatform.Android ? 56
                : 48
            ),
            new FillNavigationContent(entry.Screen, entry.Key)
        ).BackgroundColor(Theme.Of(context).Background);
    }

    private async void OnNativeShown(int depth)
    {
        _nativeInteraction = false;
        if (_disposed || depth < 1 || depth > _entries.Count)
            return;
        if (_nativeCompletion is { } completion)
        {
            completion.TrySetResult(depth);
            return;
        }
        if (depth == _entries.Count)
            return; // Includes a cancelled interactive pop.
        _changing = true;
        try
        {
            while (_entries.Count > depth)
            {
                var entry = _entries[^1];
                _entries.RemoveAt(_entries.Count - 1);
                await Release(entry);
            }
            PublishChange();
        }
        catch (Exception error)
        {
            dispatcher.Post(() =>
                throw new InvalidOperationException("Navigation cleanup failed.", error)
            );
        }
        finally
        {
            _changing = false;
        }
    }

    private bool _disposed;
    private Task? _disposal;

    public int Count
    {
        get
        {
            TrackRead();
            return _entries.Count;
        }
    }

    public bool CanPop => Count > 1;

    public Component? Current
    {
        get
        {
            TrackRead();
            return _entries.Count == 0 ? null : _entries[^1].Screen;
        }
    }

    public async Task PushAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T
    >(string? title = null)
        where T : Component
    {
        BeginChange();
        var scope = scopes.CreateAsyncScope();
        Component? screen = null;
        bool committed = false;
        try
        {
            // The stack owns the screen; DI owns its scoped dependencies. Creating
            // it directly avoids resolving a registered singleton as two entries.
            screen = ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider);
            _entries.Add(
                new(
                    screen,
                    scope,
                    title ?? typeof(T).Name,
                    (++_nextKey).ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            );
            committed = true;
            if (NativePresentation && _entries.Count > 1)
                _nativeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            PublishChange();
            if (_nativeCompletion is { } completion)
                await completion.Task;
        }
        catch (Exception creationError) when (!committed)
        {
            try
            {
                await Release(new(screen!, scope, "", ""));
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(creationError, cleanupError);
            }
            throw;
        }
        finally
        {
            _nativeCompletion = null;
            _requestedDepth = null;
            _changing = false;
        }
    }

    public async Task<bool> PopAsync()
    {
        BeginChange();
        try
        {
            if (_entries.Count <= 1)
                return false;
            if (NativePresentation)
            {
                _requestedDepth = _entries.Count - 1;
                _nativeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                PublishChange();
                if (await _nativeCompletion.Task != _requestedDepth)
                    return false;
            }
            var entry = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            // A popped screen never becomes current again, even if cleanup fails.
            try
            {
                PublishChange();
            }
            finally
            {
                await Release(entry);
            }
            return true;
        }
        finally
        {
            _nativeCompletion = null;
            _requestedDepth = null;
            _changing = false;
        }
    }

    private void BeginChange()
    {
        VerifyCanWrite();
        if (!dispatcher.CheckAccess())
            throw new InvalidOperationException("Navigation requires the UI thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_changing || _nativeInteraction)
            throw new InvalidOperationException("A navigation operation is already in progress.");
        _changing = true;
    }

    private static async Task Release(Entry entry)
    {
        var errors = new List<Exception>();
        try
        {
            if (entry.Screen is IAsyncDisposable asynchronous)
                await asynchronous.DisposeAsync();
            else
                (entry.Screen as IDisposable)?.Dispose();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            await entry.Scope.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        if (errors.Count != 0)
            throw new AggregateException(errors);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposal is not null)
            return new(_disposal);
        VerifyCanWrite();
        _disposed = true;
        _nativeCompletion?.TrySetCanceled();
        _disposal = DisposeCoreAsync();
        return new(_disposal);
    }

    private async Task DisposeCoreAsync()
    {
        var entries = _entries.ToArray();
        _entries.Clear();
        var errors = new List<Exception>();
        try
        {
            PublishChange();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        foreach (var entry in entries.Reverse())
        {
            try
            {
                await Release(entry);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        _changing = false;
        if (errors.Count != 0)
            throw new AggregateException(errors);
    }
}

// iOS retains each screen in a UIKit navigation controller. Other platforms
// currently render the current screen until their native adapters are connected.
public sealed class NavigationHost(Navigator navigator) : Component
{
    public override Element Build(BuildContext context) => navigator.BuildPresentation(context);
}

public static class NavigationServices
{
    public static IServiceCollection AddDotNativeNavigation(
        this IServiceCollection services,
        ScreenOptions? defaults = null
    )
    {
        services.TryAddSingleton<Navigator>(provider => new Navigator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IUiDispatcher>(),
            provider.GetService<PresentationTarget>()
        )
        {
            DefaultOptions = defaults ?? new(),
        });
        return services;
    }
}
