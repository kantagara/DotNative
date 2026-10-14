using Microsoft.Extensions.DependencyInjection;

namespace DotNative;

internal sealed class ApplicationServiceScope(IServiceProvider services, Element child) : Component
{
    public override Element Build() => new ContextScope<IServiceProvider>(services, child);
}

/// <summary>
/// Mounts a DI-registered component with constructor injection. Retain this wrapper
/// in a field, like other stateful components. DI owns the resolved component's lifetime.
/// </summary>
public sealed class Inject<T> : Component
    where T : Component
{
    private IServiceProvider? provider;
    private T? component;

    public override Element Build(BuildContext context)
    {
        var current = context.Get<IServiceProvider>();
        if (!ReferenceEquals(current, provider))
        {
            component = current.GetRequiredService<T>();
            provider = current;
        }
        return component!;
    }
}
