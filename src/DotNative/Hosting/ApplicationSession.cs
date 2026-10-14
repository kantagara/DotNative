using System.ComponentModel;

namespace DotNative.Hosting;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ApplicationSession : IDisposable, IAsyncDisposable
{
    private static readonly AsyncLocal<ApplicationSession?> Current = new();
    internal static ApplicationSession? Ambient => Current.Value;

    internal void RunInContext(Action action)
    {
        var previous = Current.Value;
        Current.Value = this;
        try { action(); }
        finally { Current.Value = previous; }
    }

    internal static Plugins.PluginRuntimeContext RequirePluginContext()
    {
        var session = Current.Value ?? throw new InvalidOperationException(
            "No DotNative application context. Call from application startup or an application callback.");
        if (session._stopRequested)
            throw new ObjectDisposedException(nameof(ApplicationSession));
        if (session._application is { } app)
            _ = app.Services; // Reject a directly disposed application too.
        return new(session.Channels, session.Transport.Target);
    }

    private readonly CancellationTokenSource _stopping = new();
    private DotNativeApplication? _application;
    private Task? _disposeTask;
    private volatile bool _stopRequested;
    private readonly ApplicationLifecycle _lifecycle = new();
    private readonly SystemAccessibility _accessibility = new();
    internal static ISystemAccessibility? CurrentAccessibility => Current.Value?._accessibility;
    internal AccessibilityPreferences Accessibility => _accessibility.Preferences;
    private readonly SystemAppearance _appearance = new(ThemeMode.Light);
    internal static ISystemAppearance? CurrentAppearance => Current.Value?._appearance;
    internal static PresentationTarget? CurrentTarget => Current.Value?.Transport.Target;
    internal static Plugins.IPlatformChannels? CurrentChannels => Current.Value?.Channels;
    public Plugins.PlatformChannels Channels { get; }
    internal static IUiDispatcher? CurrentDispatcher => Current.Value?.Dispatcher;
    internal static IApplicationLifecycle? CurrentLifecycle => Current.Value?._lifecycle;
    internal IUiDispatcher Dispatcher { get; }
    internal ICommandTransport Transport { get; }
    internal float Width { get; private set; }
    internal float Height { get; private set; }
    internal WindowInsets Insets { get; private set; }
    internal CancellationToken Stopping => _stopping.Token;
    public Task Startup { get; private set; } = Task.CompletedTask;
    public RenderHost? MountedHost => _application?.Host;
    public RenderHost Host =>
        MountedHost ?? throw new InvalidOperationException("Application is not running.");

    private ApplicationSession(
        IUiDispatcher dispatcher,
        ICommandTransport transport,
        float width,
        float height
    )
    {
        (Dispatcher, Transport, Width, Height) = (dispatcher, transport, width, height);
        Channels = new Plugins.PlatformChannels(dispatcher, transport as Plugins.IPluginTransport);
    }

    public static ApplicationSession Begin(
        Func<Task> main,
        IUiDispatcher dispatcher,
        ICommandTransport transport,
        float width,
        float height,
        ThemeMode systemTheme = ThemeMode.Light,
        AccessibilityPreferences? accessibility = null
    )
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(transport);
        if (!dispatcher.CheckAccess())
            throw new InvalidOperationException("Main must run on the OS UI thread.");
        if (Current.Value is not null)
            throw new InvalidOperationException("Nested application startup is forbidden.");
        Styling.Nonnegative(width);
        Styling.Nonnegative(height);
        var session = new ApplicationSession(dispatcher, transport, width, height);
        session._appearance.Update(systemTheme);
        session._accessibility.Update(accessibility ?? AccessibilityPreferences.Default);
        new DispatcherSynchronizationContext(dispatcher).Run(() =>
            session.Startup = session.ExecuteMain(main)
        );
        return session;
    }

    private async Task ExecuteMain(Func<Task> main)
    {
        Current.Value = this;
        try
        {
            await main();
            _stopping.Token.ThrowIfCancellationRequested();
            if (MountedHost is null)
                throw new InvalidOperationException(
                    "Program.Main must call builder.Build().Run<App>()."
                );
        }
        finally
        {
            Current.Value = null;
        }
    }

    // Compatibility for synchronous custom hosts. Native runners use Begin.
    public static ApplicationSession Start(
        Action main,
        IUiDispatcher dispatcher,
        ICommandTransport transport,
        float width,
        float height
    )
    {
        var session = Begin(() => MainEntry.Invoke(main), dispatcher, transport, width, height);
        try
        {
            session.Startup.GetAwaiter().GetResult();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public void Resize(float width, float height)
    {
        VerifyThread();
        Styling.Nonnegative(width);
        Styling.Nonnegative(height);
        Width = width;
        Height = height;
        if (!_stopRequested)
            MountedHost?.SetViewport(width, height);
    }

    public void SetWindowInsets(float top, float bottom, float keyboard)
    {
        VerifyThread();
        Styling.Nonnegative(top);
        Styling.Nonnegative(bottom);
        Styling.Nonnegative(keyboard);
        Insets = new(top, bottom, keyboard);
        if (!_stopRequested)
            MountedHost?.SetWindowInsets(top, bottom, keyboard);
    }

    public void SetSystemAccessibility(AccessibilityPreferences preferences)
    {
        VerifyThread();
        preferences.Validate();
        if (_stopRequested)
            return;
        _accessibility.Update(preferences);
        MountedHost?.SetSystemAccessibility(preferences);
    }

    public void SetSystemTheme(ThemeMode theme)
    {
        VerifyThread();
        if (!_stopRequested)
            _appearance.Update(theme);
    }

    public void SetActivity(ApplicationActivity activity)
    {
        VerifyThread();
        if (_stopRequested)
            return;
        new DispatcherSynchronizationContext(Dispatcher, this).Run(() => _lifecycle.Transition(activity));
    }

    internal static void Attach(DotNativeApplication app)
    {
        if (Current.Value is not { } session)
            return;
        session.VerifyThread();
        session._stopping.Token.ThrowIfCancellationRequested();
        if (session._application is not null)
            throw new InvalidOperationException("Only one application can be built per startup.");
        session._application = app;
    }

    internal static CancellationToken CurrentStopping =>
        Current.Value?.Stopping ?? CancellationToken.None;

    internal static ApplicationSession RequireCurrent(DotNativeApplication app)
    {
        var session = Current.Value;
        if (session is null || !ReferenceEquals(session._application, app))
            throw new InvalidOperationException("Run must be called from Program.Main.");
        session.VerifyThread();
        session._stopping.Token.ThrowIfCancellationRequested();
        return session;
    }

    private void VerifyThread()
    {
        if (!Dispatcher.CheckAccess())
            throw new InvalidOperationException("Application lifecycle requires the UI thread.");
    }

    public ValueTask DisposeAsync()
    {
        VerifyThread();
        Channels.Dispose();
        if (_disposeTask is not null)
            return new(_disposeTask);
        _stopRequested = true;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _disposeTask = completion.Task;
        new DispatcherSynchronizationContext(Dispatcher, this).Run(() =>
            _ = CompleteDisposal(completion)
        );
        return new(_disposeTask);
    }

    private async Task CompleteDisposal(TaskCompletionSource completion)
    {
        Exception? detachError = null,
            cancellationError = null,
            lifecycleError = null;
        try
        {
            _lifecycle.Transition(ApplicationActivity.Stopping);
        }
        catch (Exception error)
        {
            lifecycleError = error;
        }
        try
        {
            _application?.DetachHost();
        }
        catch (Exception error)
        {
            detachError = error;
        }
        try
        {
            _stopping.Cancel();
        }
        catch (Exception error)
        {
            cancellationError = error;
        }
        try
        {
            await DisposeCore(detachError, cancellationError, lifecycleError);
            completion.SetResult();
        }
        catch (Exception error)
        {
            completion.SetException(error);
        }
    }

    private async Task DisposeCore(
        Exception? detachError,
        Exception? cancellationError,
        Exception? lifecycleError
    )
    {
        var errors = new List<Exception>();
        if (lifecycleError is not null)
            errors.Add(lifecycleError);
        if (detachError is not null)
            errors.Add(detachError);
        if (cancellationError is not null)
            errors.Add(cancellationError);
        try
        {
            await Startup;
        }
        catch (OperationCanceledException error)
            when (_stopRequested
                && (
                    error.CancellationToken == _stopping.Token
                    || (_application is { } app && error.CancellationToken == app.Stopping)
                )
            ) { }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            if (_application is not null)
                await _application.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        finally
        {
            _application = null;
            _stopping.Dispose();
        }
        if (errors.Count > 0)
            throw new AggregateException(errors);
    }

    public void Dispose()
    {
        VerifyThread();
        if (!Startup.IsCompleted || _application?.Initialization.IsCompleted == false)
            throw new InvalidOperationException("Use DisposeAsync while startup is pending.");
        _stopRequested = true;
        Channels.Dispose();
        // Synchronous callers retain standard IDisposable semantics.
        try
        {
            _application?.Dispose();
        }
        finally
        {
            _application = null;
        }
    }
}
