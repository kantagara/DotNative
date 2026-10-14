namespace DotNative.Plugins;

/// <summary>The channels and renderer target belonging to one application context.</summary>
public sealed record PluginRuntimeContext(IPlatformChannels Channels, PresentationTarget Target);

/// <summary>Accesses the current application's plugin runtime without a process-global default.</summary>
public static class PluginRuntime
{
    /// <summary>
    /// Available during application startup, UI/lifecycle callbacks and their async continuations.
    /// Throws outside an application context or after that session stops. Independently started
    /// threads must receive an explicit service/context rather than guessing an active app.
    /// </summary>
    public static PluginRuntimeContext Current => Hosting.ApplicationSession.RequirePluginContext();
}
