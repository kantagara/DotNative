# Application startup and dependency injection

Every generated app contains Program.cs and App.cs in one project:

```csharp
public static class Program
{
    public static void Main()
    {
        var builder = DotNativeApplication.CreateBuilder();
        builder.Services.AddSingleton<ISettings, Settings>();
        builder.Build().Run<App>();
    }
}
```

Use `Microsoft.Extensions.DependencyInjection` for the real Microsoft service
registration extension methods. App can receive services through its public
constructor. A root component need not be registered: ActivatorUtilities creates
it with constructor injection. If explicitly registered, DI owns the root.

OS startup enters the platform runner, initializes the native presentation host,
and calls the managed C ABI entry. The generated adapter calls Program.Main
exactly once per successful session startup. Main is parameterless, public and static, returning either void or Task. `Run<App>()` mounts the first frame and returns to the native
event loop; returning from Main does not terminate the process. Async void Main and top-level statements are not supported by this library-based entry contract.
Normal awaits resume through the UI dispatcher. ConfigureAwait(false) deliberately
opts out; calling Run or manipulating UI state from that continuation is rejected.

Use `dotnet run --project Samples/WidgetSamples/WidgetSamples.csproj` for the
booted iOS simulator. Add `-r osx-arm64` for macOS, or the documented experimental
Android RID. The same Program.Main is invoked on each platform.

## Lifetimes and ownership

- The provider is created once per native app session, not per render.
- Singleton services live until that session stops.
- Root construction uses an application scope; scoped services resolved there
  live until session stop. This is not an automatic screen/navigation scope.
- Inject IServiceScopeFactory to create shorter explicit scopes.
- DI-owned instances are disposed by their scope/provider. Externally supplied
  singleton instances remain the caller's responsibility, as in standard DI.
- The render host is disposed before the root and its service dependencies.
- Startup exceptions clean up the application already built, including failures
  after mounting. Missing Run and duplicate Run calls are rejected.

The application and session implement IAsyncDisposable. Async-only dependencies
are awaited on shutdown; custom async roots are disposed before their DI scope.
The native stop ABI returns 1 while cleanup is pending, 0 when complete and -1
on failure. Runners poll it on their main loop without blocking. Android delays
a replacement Activity startup until the previous session releases its registry.
macOS defers normal termination; iOS termination remains best effort. Abrupt OS process
termination cannot guarantee any managed cleanup. Do not rely on shutdown to
persist user data. DI does not automatically start IHostedService/BackgroundService.

BuildContext remains a tree-local UI scope, independent of the DI container.
Components manually created with `new` receive dependencies explicitly; the
renderer does not resolve new services on every Build. Service callbacks that
update state must post to the UI thread.

Explicit generic registrations preserve constructor metadata for NativeAOT.
Dynamic assembly scanning is not part of this startup system. Added packages
must still pass AOT/trimming validation.

## Asynchronous initialization

```csharp
public static async Task Main()
{
    var builder = DotNativeApplication.CreateBuilder();
    builder.Services.AddSingleton<ISessionService, SessionService>();
    var app = builder.Build();
    await app.Services.GetRequiredService<ISessionService>().RestoreAsync(app.Stopping);
    app.Run<App>();
}
```

Stopping cancels app.Stopping, immediately detaches the native tree, waits for
Main to settle, and then asynchronously disposes the application. Pass the token
to startup I/O. Non-cooperative initialization can delay orderly shutdown; the
runtime does not dispose a service while Main may still be using it. Resizes
received during initialization are remembered for the first rendered frame.
Startup exceptions are observed and cleanup completes before failure is reported.

The main thread is available while awaiting, but no DotNative UI appears until
Run. Mount a loading screen first when initialization could take substantial time.
DI retains Microsoft's disposal behavior; services with UI-affine cleanup must
explicitly dispatch that work if called from outside the UI context.
