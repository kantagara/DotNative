using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace DotNative;

public sealed class DotNativeApplicationBuilder
{
    private bool _built;
    public IServiceCollection Services { get; } = new ServiceCollection();

    public DotNativeApplicationBuilder()
    {
        Services.AddSingleton<ApplicationActivations>();
        Services.AddSingleton<IApplicationActivations>(p =>
            p.GetRequiredService<ApplicationActivations>()
        );
        Services.AddSingleton<OverlayPresenter>();
        Services.AddSingleton<IOverlayPresenter>(p => p.GetRequiredService<OverlayPresenter>());
        if (Hosting.ApplicationSession.CurrentChannels is { } channels)
            Services.AddSingleton(channels);
        Services.AddSingleton(Hosting.ApplicationSession.CurrentTarget ?? PresentationTarget.Local);
        if (Hosting.ApplicationSession.CurrentDispatcher is { } dispatcher)
            Services.AddSingleton<IUiDispatcher>(dispatcher);
        Services.AddSingleton<ISystemAccessibility>(
            Hosting.ApplicationSession.CurrentAccessibility ?? new SystemAccessibility()
        );
        Services.AddSingleton<ISystemAppearance>(
            Hosting.ApplicationSession.CurrentAppearance ?? new SystemAppearance(ThemeMode.Light)
        );
        Services.AddSingleton<IApplicationLifecycle>(
            Hosting.ApplicationSession.CurrentLifecycle ?? new ApplicationLifecycle()
        );
    }

    public DotNativeApplication Build()
    {
        if (_built)
            throw new InvalidOperationException("A builder can only build once.");
        _built = true;
        var provider = Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        var app = new DotNativeApplication(provider);
        try
        {
            Hosting.ApplicationSession.Attach(app);
            app.PrepareInitializers();
            return app;
        }
        catch
        {
            app.Dispose();
            throw;
        }
    }
}

public sealed class DotNativeApplication : IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;
    private readonly CancellationTokenSource _stopping;
    private IApplicationInitializer[] _initializers = [];
    private Task? _disposeTask;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private Component? _ownedRoot;
    private bool _ran,
        _disposed;
    internal RenderHost? Host { get; private set; }
    public IServiceProvider Services =>
        !_disposed
            ? _scope.ServiceProvider
            : throw new ObjectDisposedException(nameof(DotNativeApplication));

    internal DotNativeApplication(ServiceProvider provider)
    {
        (_provider, _scope) = (provider, provider.CreateAsyncScope());
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(
            Hosting.ApplicationSession.CurrentStopping
        );
        Stopping = _stopping.Token;
    }

    internal void PrepareInitializers() =>
        _initializers = Services.GetServices<IApplicationInitializer>().ToArray();

    /// <summary>Completion of automatically started services. Faults are logged and remain awaitable.</summary>
    public Task Initialization { get; private set; } = Task.CompletedTask;

    public static DotNativeApplicationBuilder CreateBuilder() => new();

    public CancellationToken Stopping { get; }

    // Main may await initialization. Run mounts the tree and returns;
    // UIKit/AppKit/Android continue to own their event loop.
    public void Run<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T
    >()
        where T : Component
    {
        Verify();
        if (_ran)
            throw new InvalidOperationException("Run may only be called once.");
        var session = Hosting.ApplicationSession.RequireCurrent(this);
        _ran = true;
        var root = Services.GetService<T>();
        if (root is null)
            root = (T)(_ownedRoot = ActivatorUtilities.CreateInstance<T>(Services));
        Host = new RenderHost(
            new ApplicationServiceScope(
                Services,
                new OverlayHost(root, Services.GetRequiredService<OverlayPresenter>())
            ),
            session.Dispatcher,
            session.Transport
        );
        Host.ActivationReceived = Services.GetRequiredService<ApplicationActivations>().Publish;
        Host.SetSystemAccessibility(session.Accessibility);
        Host.SetViewport(session.Width, session.Height);
        Host.SetWindowInsets(session.Insets.Top, session.Insets.Bottom, session.Insets.Keyboard);
        Host.RenderNow();
        Initialization = Task.WhenAll(_initializers.Select(InitializeServiceAsync));
        _ = ObserveInitializationAsync();
    }

    private async Task InitializeServiceAsync(IApplicationInitializer initializer)
    {
        try
        {
            Stopping.ThrowIfCancellationRequested();
            await initializer.InitializeAsync(Stopping);
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{initializer.GetType().Name} initialization failed: {error}");
            throw;
        }
    }

    private async Task ObserveInitializationAsync()
    {
        try
        {
            await Initialization;
        }
        catch
        { /* Errors were logged individually; Initialization retains them for callers. */
        }
    }

    private void Verify()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Application lifecycle requires its UI thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    internal void DetachHost()
    {
        Verify();
        try
        {
            Host?.Dispose();
        }
        finally
        {
            Host = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Application lifecycle requires its UI thread.");
        if (_disposeTask is not null)
            return new(_disposeTask);
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        _disposeTask = DisposeCoreAsync();
        return new(_disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        var errors = new List<Exception>();
        try
        {
            _stopping.Cancel();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            await Initialization;
        }
        catch
        { /* Already observed and reported by the startup runner. */
        }
        try
        {
            Host?.Dispose();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        Host = null;
        try
        {
            if (_ownedRoot is IAsyncDisposable asynchronous)
                await asynchronous.DisposeAsync();
            else
                (_ownedRoot as IDisposable)?.Dispose();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            await _scope.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        try
        {
            await _provider.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        _stopping.Dispose();
        if (errors.Count > 0)
            throw new AggregateException(errors);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Verify();
        if (!Initialization.IsCompleted)
            throw new InvalidOperationException(
                "Use DisposeAsync while service initialization is pending."
            );
        _disposed = true;
        try
        {
            _stopping.Cancel();
        }
        finally
        {
            DisposeSynchronously();
        }
    }

    private void DisposeSynchronously()
    {
        // Attempt every cleanup even if a user-owned service throws on disposal.
        try
        {
            Host?.Dispose();
        }
        finally
        {
            Host = null;
            try
            {
                (_ownedRoot as IDisposable)?.Dispose();
            }
            finally
            {
                try
                {
                    _scope.Dispose();
                }
                finally
                {
                    try
                    {
                        _provider.Dispose();
                    }
                    finally
                    {
                        _stopping.Dispose();
                    }
                }
            }
        }
    }
}
