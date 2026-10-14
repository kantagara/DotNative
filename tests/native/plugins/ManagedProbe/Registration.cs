using DotNative;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedProbe;

public static class Registration
{
    public static DotNativeApplicationBuilder UseManagedProbe(
        this DotNativeApplicationBuilder builder
    )
    {
        builder.Services.AddSingleton<NativeService>();
        return builder;
    }
}
