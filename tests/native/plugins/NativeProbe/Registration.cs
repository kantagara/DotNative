using DotNative;
using Microsoft.Extensions.DependencyInjection;

namespace NativeProbe;

public static class Registration
{
    public static DotNativeApplicationBuilder UseNativeProbe(
        this DotNativeApplicationBuilder builder
    )
    {
        builder.Services.AddSingleton<NativeService>();
        return builder;
    }
}
